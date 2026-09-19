namespace GIC.Tool
{


    /// <summary>
    /// 卡牌排序工具 — 提供统一的排序比较方法
    /// </summary>
    public static class CardSortUtility
    {
        /// <summary>
        /// 通用卡牌比较：主键升序 → 星级降序 → 配置顺序升序
        /// </summary>
        public static int CompareByPrimaryThenStar(int primaryA, int primaryB, int starA, int starB, int configIndexA, int configIndexB)
        {
            int cmp = primaryA.CompareTo(primaryB);
            if (cmp != 0) return cmp;
            cmp = starB.CompareTo(starA); // 星级从高到低
            if (cmp != 0) return cmp;
            return configIndexA.CompareTo(configIndexB); // 配置顺序从前到后
        }
    }

}


