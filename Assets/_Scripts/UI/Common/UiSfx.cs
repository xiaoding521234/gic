using System.Collections.Generic;
using GIC.Framework;
using UnityEngine;

namespace GIC.UI
{
    /// <summary>
    /// UI 交互音效枚举（素材=本地《原神》解包官方音效，按交互功能命名，目录 Resources/Audios/UI/）。
    /// 文件名=枚举名转 snake_case（Click → ui_click.wav）。
    /// </summary>
    public enum UiSfxKind
    {
        Click,          // 经典 tak 按钮点击（大厅按钮/通用）
        ClickDing,      // 清亮 ding 变体点击
        ClickSoft,      // 软质啵声点击（轻量交互）
        ClickHeavy,     // 低沉厚重点击（重操作）
        Confirm,        // 确认提示叮
        ConfirmBright,  // 明亮弹出确认叮
        HeavyConfirm,   // 重型确认轰鸣（大事件完成）
        Toggle,         // 开关切换咔哒
        PanelOpen,      // 面板打开嗖嗖
        PanelClose,     // 面板关闭嗖嗖
        CardFlip,       // 卡牌翻面啪嗒
        PageSwitch,     // 页面/标签切换破空
        Popup,          // 小弹窗冒泡
        Notify,         // 通知提示叮
        ItemGet,        // 物品获得叮
        Pickup,         // 图标拾取弹啵
        Sparkle,        // 星光闪烁
        Achievement,    // 成就达成长叮
    }

    /// <summary>
    /// UI 音效单源播放入口：Resources 懒加载缓存，统一走 AudioManager.PlaySFXOneShot（SFX 混音通道）。
    /// 缺素材静默跳过（每键仅 Warn 一次）；AudioManager 未就绪时安全空操作。
    /// </summary>
    public static class UiSfx
    {
        private static readonly Dictionary<UiSfxKind, AudioClip> _cache = new();
        private static readonly HashSet<UiSfxKind> _missingWarned = new();

        public static void Play(UiSfxKind kind, float volumeScale = 1f)
        {
            var clip = GetClip(kind);
            if (clip == null) return;
            if (AudioManager.Instance == null) return;
            AudioManager.Instance.PlaySFXOneShot(clip, volumeScale);
        }

        private static AudioClip GetClip(UiSfxKind kind)
        {
            if (_cache.TryGetValue(kind, out var cached)) return cached;
            if (_missingWarned.Contains(kind)) return null;

            var clip = Resources.Load<AudioClip>("Audios/UI/" + ToFileName(kind));
            if (clip == null)
            {
                _missingWarned.Add(kind);
                GICLog.Warn($"[UiSfx] 缺少音效资源: Audios/UI/{ToFileName(kind)}");
                return null;
            }
            _cache[kind] = clip;
            return clip;
        }

        /// <summary>枚举名 → snake_case 文件名（不含扩展名）</summary>
        private static string ToFileName(UiSfxKind kind)
        {
            var s = kind.ToString();
            var sb = new System.Text.StringBuilder("ui_");
            for (int i = 0; i < s.Length; i++)
            {
                if (i > 0 && char.IsUpper(s[i])) sb.Append('_');
                sb.Append(char.ToLower(s[i]));
            }
            return sb.ToString();
        }
    }
}
