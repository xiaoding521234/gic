using System;
using System.Collections.Generic;
using System.Text;
using GIC.Data;

namespace GIC.Framework
{
    /// <summary>
    /// 卡组密语编解码（分享码）：卡组名 + 有序卡牌列表 → "GIC1." + Base64URL 紧凑串。
    /// 供卡组管理面板导出/导入（复制分享、一键配置卡组）。
    /// 编码格式（LEB128 varint）：
    ///   [名称字节数 varint][名称 UTF8][卡牌数 varint]{ [卡牌类型 byte 0=Item 1=Unit][卡牌值 varint] }*
    /// Base64URL（+→-，/→_，去填充）保证密语可安全粘贴进聊天软件不换行断字。
    /// </summary>
    public static class DeckCodeCodec
    {
        public const string Prefix = "GIC1.";

        /// <summary>编码卡组为密语；卡牌数超上限截断（>MaxDeckSize 的卡组不是合法游戏状态，防御处理）</summary>
        public static string Encode(string name, IReadOnlyList<CardId> cards)
        {
            cards ??= new List<CardId>();
            if (cards.Count > CardManager.MaxDeckSize)
                cards = new List<CardId>(cards).GetRange(0, CardManager.MaxDeckSize);

            var nameBytes = Encoding.UTF8.GetBytes(name ?? "");
            var payload = new List<byte>(nameBytes.Length + cards.Count * 4 + 8);

            AddVarint(payload, (uint)nameBytes.Length);
            payload.AddRange(nameBytes);
            AddVarint(payload, (uint)cards.Count);
            foreach (var card in cards)
            {
                payload.Add((byte)card.cardType);
                AddVarint(payload, (uint)card.value);
            }

            string b64 = Convert.ToBase64String(payload.ToArray())
                .TrimEnd('=')
                .Replace('+', '-').Replace('/', '_');
            return Prefix + b64;
        }

        /// <summary>解析密语；格式非法/卡牌数超限返回 false（不抛异常）</summary>
        public static bool TryDecode(string code, out string name, out List<CardId> cards)
        {
            name = null;
            cards = null;

            if (string.IsNullOrEmpty(code) || !code.StartsWith(Prefix, System.StringComparison.Ordinal))
                return false;

            string b64 = code.Substring(Prefix.Length).Replace('-', '+').Replace('_', '/');
            int pad = b64.Length % 4;
            if (pad == 1) return false; // Base64 长度模 4 余 1 恒非法
            if (pad > 0) b64 += new string('=', 4 - pad);

            byte[] bytes;
            try { bytes = Convert.FromBase64String(b64); }
            catch { return false; }

            int pos = 0;
            if (!TryReadVarint(bytes, ref pos, out uint nameLen) || nameLen > bytes.Length - pos)
                return false;
            name = Encoding.UTF8.GetString(bytes, pos, (int)nameLen);
            pos += (int)nameLen;

            if (!TryReadVarint(bytes, ref pos, out uint count) || count > CardManager.MaxDeckSize)
                return false;

            cards = new List<CardId>((int)count);
            for (int i = 0; i < count; i++)
            {
                if (pos >= bytes.Length) return false;
                byte type = bytes[pos++];
                if (type != (byte)CardType.Item && type != (byte)CardType.Unit) return false;
                if (!TryReadVarint(bytes, ref pos, out uint value)) return false;
                cards.Add(new CardId((CardType)type, (int)value));
            }

            // 允许尾部留白（截断误差）；正文必须恰好消费完所有结构化字段
            return true;
        }

        private static void AddVarint(List<byte> buffer, uint v)
        {
            while (v >= 0x80)
            {
                buffer.Add((byte)(v | 0x80));
                v >>= 7;
            }
            buffer.Add((byte)v);
        }

        private static bool TryReadVarint(byte[] bytes, ref int pos, out uint value)
        {
            value = 0;
            int shift = 0;
            while (true)
            {
                if (pos >= bytes.Length || shift > 28) return false; // 越界或 varint 恶意超长
                byte b = bytes[pos++];
                value |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return true;
                shift += 7;
            }
        }
    }
}
