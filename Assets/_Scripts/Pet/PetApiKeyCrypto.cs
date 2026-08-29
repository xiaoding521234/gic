using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 派蒙对话 API Key 加密存储（2026-08-28，用户拍板"玩家自输自己的 key，存档加密存储"）：
    /// AES-128-CBC（System.Security.Cryptography，Unity/Mono/IL2CPP 全可用，零外部依赖）+
    /// **设备指纹派生密钥**（机器名+用户名+Application.productName+固定盐 → SHA256 截 16 字节）。
    ///
    /// 威胁模型与边界（防呆不防专家）：加密目标是①存档文本编辑器打开看不到明文 key（防"顺手复制泄露"）
    /// ②key 不出现在日志/异常栈。**防不了**本机恶意软件（同机可复现派生密钥）——本地单机存档的
    /// 通行边界（浏览器保存密码同级别），要更高安全性得走系统凭据库（Windows DPAPI ——
    /// System.Security.Cryptography.ProtectedData 在 IL2CPP 不可用，Mono 可用但跨平台断裂，弃）。
    ///
    /// 密文格式：Base64(iv[16] + ciphertext + PKCS7 padding)；篡改/跨机器拷档 → 解密抛异常 →
    /// 调用方按"无 key"处理（清空重输，不崩）。密钥派生故意不含 volatile 信息（分辨率/内存等会变的
    /// 不进指纹，否则换硬件=误锁 key）。
    /// </summary>
    public static class PetApiKeyCrypto
    {
        /// <summary>存档字段名（PlayerSaveData.petApiKeyCipher，Base64 密文；空串=未设置）</summary>
        public const string saveField = "petApiKeyCipher";

        private static byte[] _密钥缓存;

        /// <summary>设备指纹派生 AES-128 密钥（16 字节；进程内缓存一次）</summary>
        static byte[] 派生密钥()
        {
            if (_密钥缓存 != null) return _密钥缓存;
            // 固定盐：与代码同生命周期（改盐=全体密文失效，勿随意改）
            const string 盐 = "GIC-Pet-APIKey-Salt-v1";
            string fingerprint = $"{Environment.MachineName}|{Environment.UserName}|{Application.productName}|{盐}";
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint));
                _密钥缓存 = new byte[16];
                Array.Copy(hash, _密钥缓存, 16);
            }
            return _密钥缓存;
        }

        /// <summary>明文 → Base64(iv+ciphertext)。空串返回空串。异常返回空串（调用方按未设置处理）。</summary>
        public static string Encrypt(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            try
            {
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 128;
                    aes.Key = 派生密钥();
                    aes.GenerateIV();
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    using (var enc = aes.CreateEncryptor())
                    {
                        var data = Encoding.UTF8.GetBytes(plain);
                        var cipher = enc.TransformFinalBlock(data, 0, data.Length);
                        var outBuf = new byte[aes.IV.Length + cipher.Length];
                        Array.Copy(aes.IV, 0, outBuf, 0, aes.IV.Length);
                        Array.Copy(cipher, 0, outBuf, aes.IV.Length, cipher.Length);
                        return Convert.ToBase64String(outBuf);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetApiKeyCrypto] 加密失败（按未设置处理）：{e.Message}");
                return string.Empty;
            }
        }

        /// <summary>Base64(iv+ciphertext) → 明文。空串返回空串；密文损坏/跨机器 → 空串（不抛）。</summary>
        public static string Decrypt(string cipher)
        {
            if (string.IsNullOrEmpty(cipher)) return string.Empty;
            try
            {
                var buf = Convert.FromBase64String(cipher);
                if (buf.Length < 32) return string.Empty; // iv(16)+至少一个块(16)
                using (var aes = Aes.Create())
                {
                    aes.KeySize = 128;
                    aes.Key = 派生密钥();
                    aes.Mode = CipherMode.CBC;
                    aes.Padding = PaddingMode.PKCS7;
                    var iv = new byte[16];
                    Array.Copy(buf, 0, iv, 0, 16);
                    aes.IV = iv;
                    using (var dec = aes.CreateDecryptor())
                    {
                        var plain = dec.TransformFinalBlock(buf, 16, buf.Length - 16);
                        return Encoding.UTF8.GetString(plain);
                    }
                }
            }
            catch
            {
                // 密文被篡改/换机器（指纹不同）/格式坏——按未设置处理，不刷日志（防 key 相关信息进日志）
                return string.Empty;
            }
        }

        /// <summary>显示用脱敏（设置界面回显）：sk-1234…wxyz → 前 6 + … + 后 4；短 key 全打码</summary>
        public static string MaskKey(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            if (plain.Length <= 10) return new string('*', plain.Length);
            return plain.Substring(0, 6) + new string('*', 6) + plain.Substring(plain.Length - 4);
        }
    }
}
