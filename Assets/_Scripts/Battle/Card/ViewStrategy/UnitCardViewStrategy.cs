using UnityEngine;
using GIC.Framework;
using GIC.UI;
using GIC.Data;
using GIC.Tool;
namespace GIC.Battle
{


    /// <summary>
    /// 角色卡牌的视图策略 — 处理 Card 本身的显示逻辑
    /// </summary>
    public class UnitCardViewStrategy : CardViewStrategyBase
    {
        public override void InitCardDisplay(Card card, SaveCardData data, CardDetailView detailView)
        {
            var raw = CardConfigResolver.Instance?.UnitConfig?.GetUnitData(data.id.AsUnitName());
            if (raw != null)
            {
                card.cardBack.color = StarVisualConfig.GetStarColor(raw.starLevel);
                card.unitImage.gameObject.SetActive(true);
                MissingImageGuard.Assign(card.unitImage, raw.GetCard(data.skin)); // 立绘缺失兜底（docs：调用点显式）
                card.obtainText.SetSingleEntry(raw.GetObtainDescriptionEntry());
            }

            card.itemImage.gameObject.SetActive(false);
            card.countImage.gameObject.SetActive(false);

            // 部署费角标（2026-10-19；决策八十六批F 勘正：**新建筑可打出=有角标**——旧「建筑=不可打出
            // 隐藏」收窄为「不可打出的建筑（Core tag 协议核心——开局在场）隐藏」；物品卡由
            // ItemCardViewStrategy 自理恒隐藏）
            bool coreBuilding = raw != null && raw.unitType == UnitType.Building && raw.tags != null
                && System.Array.IndexOf(raw.tags, UnitTag.Core) >= 0;
            bool showCost = raw != null && !coreBuilding;
            card.部署费角标?.SetActive(showCost);
            if (showCost && card.部署费数字 != null)
            {
                int cost = raw.GetEffectiveDeployCost();
                card.部署费数字.text = cost.ToString();
                // 位数自适应（同日追拍「三位数明显超出」）：prefab 基准字号=1~2 位满配（10/50）；
                // 3 位（琴 100/温迪 300）整体缩 0.75、4 位 0.6——走 localScale 而非 fontSize：
                // 基准恒 1 不脱钩 prefab 字号（Inspector 调基准自动跟随）、描边随缩同步
                float shrink = cost >= 1000 ? 0.6f : cost >= 100 ? 0.75f : 1f;
                card.部署费数字.transform.localScale = new Vector3(shrink, shrink, 1f);
            }

            // 初始态：未拥有卡显示半透明遮罩（不含获取文本，仍可点击查看详情）
            SetOverlayState(card, data.count < 1, false);

            InitCommon(card, data, detailView);
        }

        public override void EnterEditMode(Card card)
        {
            if (card.saveCardData.count < 1)
                SetOverlayState(card, true, true); // 编辑模式：遮罩+获取方式文本（现状语义）
        }

        public override void ExitEditDeck(Card card) =>
            SetOverlayState(card, card.saveCardData.count < 1, false); // 回初始态：未拥有仅遮罩

        public override void ApplySkin(Card card, int skinIndex) =>
            ApplySkinTo(card, skinIndex, card.unitImage);

        public override bool ShouldOverlayInEditMode(Card card) =>
            card.saveCardData.count < 1;
    }
}
