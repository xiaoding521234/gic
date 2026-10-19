using System;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
namespace GIC.Battle
{
    /// <summary>
    /// 地形变化效应（决策七十六「元素系统与地形交互」）：Host 编译层产（冰线/冰棱半径=水面→薄冰；
    /// 火天降线域/火命中薄冰上单位/火 tick 命中=薄冰→水面），应用层 sim.Map.SetTile 真值突变
    /// （移动 BFS/AI 预判/部署校验/瞄准预览全链自动感知——全部经 GetTile/IsPassable 单源读图），
    /// 命令层发 TerrainChange（客户端本地 map 同步+BattleBoard 刷新该格）。
    /// 段内同格去重由编译点各自维护（同来源线/半径天然互异；跨来源重复无害、命令幂等）。
    /// </summary>
    public class TerrainChangeEffect : BattleEffect
    {
        /// <summary>目标格</summary>
        public BattleCell Cell;

        /// <summary>新地形（TileType int——ThinIce=冻结/Water=融化）</summary>
        public int NewTile;

        /// <summary>命中时刻（秒，相对片播放起点；0=立即）——WYSIWYG（决策七十六复测返修
        /// 「箭矢还未飞到薄冰就融化」：Host 状态即时结算维持架构常态，客户端经命令 launchMs 到点
        /// 应用；回合末光环冻结/泡湿类无命中时刻语义=0 立即）</summary>
        public float HitSeconds;

        public TerrainChangeEffect(BattleCell cell, int newTile)
        {
            Cell = cell;
            NewTile = newTile;
        }
    }
}
