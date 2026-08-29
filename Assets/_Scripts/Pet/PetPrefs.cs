using System;
using System.IO;
using UnityEngine;

namespace GIC.Pet
{
    /// <summary>
    /// 派蒙统一运行状态存档（2026-08-27 用户拍板"统一 pet.json"）：桌面版与游戏画面内版的
    /// 缩放/位置等易变状态共存这一个文件（{persistentDataPath}/pet.json），各形态用各的字段
    /// （屏幕物理像素与 UI 锚点坐标系不同，不共享值）。删 pet.json=重置派蒙，游戏进度零风险。
    ///
    /// 铁律不变（docs/19 §5.8）：独立桌宠进程只碰本文件、永不读写主存档；游戏内形态跑在主进程，
    /// 同样只写本文件（滚轮每格都触发防抖落盘，写主存档=全程全量存档 churn）。
    /// 形态选择（petForm）/连带关闭（closePetOnExit）属游戏设置类，留在主存档由设置界面管——
    /// 重置派蒙时用户已选的形态应保留。
    ///
    /// v1（2026-08-25 小窗体制）：缩放+客户区原点（桌面版专用）。
    /// v2（2026-08-27 统一）：+游戏内缩放/悬浮位置（UI 归一化坐标，分辨率无关）；v1 字段保留兼容。
    /// "单一派蒙不变量"（用户拍板：任何时刻只存在一个派蒙）：Mutex 保证桌面侧唯一，
    /// petForm 状态机保证桌面/游戏内互斥——文件无并发写者（游戏内+手动桌面同活的极罕见场景
    /// 靠防抖重写自愈，Demo 可接受）。
    /// </summary>
    public static class PetPrefs
    {
        [Serializable]
        public class PetSave
        {
            public int version = 3;
            // 桌面版（物理像素，虚拟桌面系）
            public float 桌面缩放 = -1f;        // <0 = 无记录
            public int 桌面客户区X, 桌面客户区Y;
            public bool 桌面有位置 = false;
            // 游戏画面内版（UI 归一化坐标 0..1，分辨率无关）
            public float 游戏内缩放 = -1f;      // <0 = 无记录
            public float 游戏内位置X = -1f, 游戏内位置Y = -1f; // <0 = 无记录
            // v3（2026-08-28 对话功能）：对话 API Key 密文（PetApiKeyCrypto AES+设备指纹）。
            // 写入方=设置界面（主进程，同步写主存档与 pet.json）；读取方=两形态的 DeepSeekClient——
            // 桌面进程永不读主存档（双进程铁律），靠 pet.json 这条既有共享通道拿密文（加密态传输安全）。
            public string chatCipher = "";
            // v1 兼容字段（旧档迁移读）
            public float scale = -1f;
            public int 客户区X, 客户区Y;
            public bool 有位置 = false;
        }

        public static string SavePath => Path.Combine(Application.persistentDataPath, "pet.json");

        static PetSave _cache;

        /// <summary>读档（进程内缓存，首个读取者落盘缓存；文件缺失/损坏返回默认实例不抛）</summary>
        public static PetSave Load()
        {
            if (_cache != null) return _cache;
            try
            {
                if (File.Exists(SavePath))
                {
                    var d = JsonUtility.FromJson<PetSave>(File.ReadAllText(SavePath));
                    if (d != null)
                    {
                        // v1→v2 迁移：桌面字段空而 v1 字段有值
                        if (d.桌面缩放 < 0f && d.scale >= 0f) d.桌面缩放 = d.scale;
                        if (!d.桌面有位置 && d.有位置) { d.桌面客户区X = d.客户区X; d.桌面客户区Y = d.客户区Y; d.桌面有位置 = true; }
                        _cache = d;
                        return _cache;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetPrefs] pet.json 读取失败（按无存档处理）：{e.Message}");
            }
            _cache = new PetSave();
            return _cache;
        }

        /// <summary>落盘（同步写；调用方自行做防抖节流）。编辑器恒跳过（桌宠状态不入编辑器会话）。</summary>
        public static void Save()
        {
#if !UNITY_EDITOR
            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(Load(), true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PetPrefs] pet.json 写入失败：{e.Message}");
            }
#endif
        }
    }
}
