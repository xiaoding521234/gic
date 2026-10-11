// CoopRoomMapZoneMigration.cs - 联机批出生区重键迁移（2026-10-11）
// ①SpawnZone.playerId（字符串"P1/P2"写死两方）→ zoneIndex（出生区序号 1 起）；
// ②FirstMap 出生区 2→4（四角 (2,2)/(12,2)/(2,12)/(12,12)——RA2 式多点，2v2 结构位）。
// 幂等：目标形态（4 区/序号 1..4/四角中心）已成立即跳过。一次性迁移工具，保留备查。
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using GIC.Data;
namespace GIC.Editor
{


    public static class CoopRoomMapZoneMigration
    {
        private const string FirstMapPath = "Assets/Resources/Configs/BattleMap_FirstMap.asset";

        [MenuItem("Tools/TG/联机批出生区迁移")]
        public static void Run()
        {
            var config = AssetDatabase.LoadAssetAtPath<BattleMapConfig>(FirstMapPath);
            if (config == null)
            {
                Debug.LogError($"[CoopRoomMapZoneMigration] 未找到 {FirstMapPath}");
                return;
            }

            // 目标形态断言（幂等）
            if (IsMigrated(config))
            {
                Debug.Log($"[CoopRoomMapZoneMigration] {config.name} 已是目标形态（4 区四角），跳过");
                return;
            }

            config.spawnZones = new List<SpawnZone>
            {
                new SpawnZone { zoneIndex = 1, center = new BattleCell(2, 2) },
                new SpawnZone { zoneIndex = 2, center = new BattleCell(12, 2) },
                new SpawnZone { zoneIndex = 3, center = new BattleCell(2, 12) },
                new SpawnZone { zoneIndex = 4, center = new BattleCell(12, 12) },
            };

            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();
            Debug.Log($"[CoopRoomMapZoneMigration] {config.name} 出生区迁移完成：4 区四角（序号 1..4）");
        }

        [MenuItem("Tools/TG/联机批出生区迁移", true)]
        private static bool Validate() => !Application.isPlaying;

        private static bool IsMigrated(BattleMapConfig config)
        {
            if (config.spawnZones == null || config.spawnZones.Count != 4) return false;
            var expect = new[] { new BattleCell(2, 2), new BattleCell(12, 2), new BattleCell(2, 12), new BattleCell(12, 12) };
            for (int i = 0; i < 4; i++)
            {
                var zone = config.spawnZones[i];
                if (zone.zoneIndex != i + 1) return false;
                if (zone.center.x != expect[i].x || zone.center.y != expect[i].y) return false;
            }
            return true;
        }
    }
}
