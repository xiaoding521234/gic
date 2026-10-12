using UnityEngine;
using TMPro;
using GIC.Data;
using GIC.Tool;

namespace GIC.UI
{
    /// <summary>
    /// 技能 tag 标签运行时建件（2026-10-11 决策六十四：战斗被动盘与卡片详情技能列表共用）。
    /// 观感契约=主动键 Name 同款：图标底边锚（anchor=(0.5,0)）+顶边 pivot、上半段盖住图标下缘、
    /// zh-cn SDF+白描边 0.08 共享材质（2026-10-13 追拍「需要白色描边」——黑描边已废勿写回；
    /// Resources 入库件勿实例化）、深墨字色 #0A0E14（2026-10-11 追拍「文本颜色和图标色太接近」
    /// ——米白已废勿写回）、长译文超宽自动缩字号（TextCombiner.最大宽度，2026-10-13 追拍
    /// 「限制左右最大宽度」）、raycastTarget=false 勿拦图标点击；无 tag（None）回落技能名
    /// （与四键 ApplySkillButton 同口径）。
    /// 尺寸参数由调用方按图标规格传入（战斗被动 154 基准/卡片详情 SkillPanel 115 基准，各等比）。
    /// </summary>
    public static class SkillTagLabelBuilder
    {
        private static Material _outlineMat; // 白描边共享材质（Resources.Load 缓存；共享引用勿 Destroy）

        public static void Build(Transform iconRoot, SkillConfig.SkillData data,
            float width, float height, float offsetY, float fontSize,
            TMP_FontAsset font = null, Color? color = null)
        {
            if (iconRoot == null || data == null) return;

            var go = new GameObject("Tag", typeof(RectTransform));
            go.transform.SetParent(iconRoot, false);
            var rect = (RectTransform)go.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f); // 图标底边锚（同 Name 控件底边锚）
            rect.pivot = new Vector2(0.5f, 1f);                       // 顶边基准（同 Name，正值=向上）
            rect.anchoredPosition = new Vector2(0f, offsetY);
            rect.sizeDelta = new Vector2(width, height);

            var text = go.AddComponent<TextMeshProUGUI>();
            text.font = font != null ? font : TMP_Settings.defaultFontAsset; // 缺省=项目 TMP 默认 zh-cn SDF（编辑器实核）
            text.fontSize = fontSize;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;          // 单行契约：长译文不折行（超宽走 TextCombiner.最大宽度 缩字）
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;               // 勿拦图标点击（开详情走图标 SelectButton）
            // 深墨 #0A0E14（2026-10-11 追拍「文本颜色和图标色太接近了」：米白暖字与暖色元素底+白纹样
            // 双向趋同——深墨对亮彩元素底=明度对比、对白色纹样=色相对比，双向拉开；=项目深底既有色）
            text.color = color ?? new Color32(0x0A, 0x0E, 0x14, 0xFF);
            if (_outlineMat == null)
                _outlineMat = Resources.Load<Material>("UI/Battle/zh-cn SDF White Outline 0.08");
            if (_outlineMat != null) text.fontSharedMaterial = _outlineMat; // 白描边（2026-10-13 追拍，四键 Name 同款）

            var combiner = go.AddComponent<TextCombiner>(); // Awake 自拾取同物体 TMP；AddEntry 即注册即渲染
            // 2026-10-13 追拍「限制左右最大宽度」：长译文按 preferredWidth 自动缩字号——
            // TMP 自带 Auto Size 不折行不收敛（宽度收敛在折行分支内），见 TextCombiner.ApplyWidthLimit 注释
            combiner.最大宽度 = width;
            combiner.最小字号倍率 = 0.5f;
            combiner.AddEntry(data.技能标签 != SkillTag.None ? data.GetTagEntry() : data.skillID.GetEntry());
        }
    }
}
