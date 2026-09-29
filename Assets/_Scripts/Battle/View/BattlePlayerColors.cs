using UnityEngine;
using GIC.Data;

namespace GIC.Battle
{
    /// <summary>
    /// 玩家配色解析单出口（2026-09-29 执行预览拍板「只需要在角色头像的边框染色玩家色——让玩家直观
    /// 看出这个单位是谁的」）：本端视角相对序——我自己=我方玩家色[0]（蓝）、我队其余玩家按
    /// PlayerIds 序=绿…、敌队玩家按序=红/紫…。色源=BattlePalette 玩家配色两数组（数组式可扩 3v3，
    /// 超出取末色）。只服务执行预览头像素；立牌底座/弧光等队伍色口径不动（拍板④A 暂不推广）。
    /// </summary>
    public static class BattlePlayerColors
    {
        public static Color Resolve(BattleSimState sim, string myPlayerId, string playerId)
        {
            var palette = BattlePalette.Instance;
            if (palette == null) return Color.white;
            if (string.IsNullOrEmpty(playerId)) return palette.敌方主色;
            if (playerId == myPlayerId) return ColorAt(palette.我方玩家色, 0);

            var myTeam = sim != null ? sim.GetTeamOf(myPlayerId) : TeamType.A;
            bool sameTeam = sim != null && sim.GetTeamOf(playerId) == myTeam;
            var list = sameTeam ? palette.我方玩家色 : palette.敌方玩家色;
            if (list == null || list.Count == 0) return Color.white;

            // 队内序（本端视角）：我方=自己 0 号、其余按 PlayerIds 序取 1 起；敌方=PlayerIds 序取 0 起
            int idx = 0;
            if (sim != null && sim.PlayerIds != null)
            {
                foreach (var pid in sim.PlayerIds)
                {
                    if (pid == myPlayerId) continue;            // 我自己=0 号恒定（队内其余从 1 起数）
                    if (sim.GetTeamOf(pid) != myTeam) continue;
                    if (pid == playerId) break;
                    idx++;
                }
            }
            return ColorAt(list, idx);
        }

        private static Color ColorAt(System.Collections.Generic.List<Color> list, int idx)
        {
            return list[Mathf.Clamp(idx, 0, list.Count - 1)];
        }
    }
}
