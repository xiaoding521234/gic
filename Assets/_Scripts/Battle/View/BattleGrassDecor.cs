using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GIC.Framework;
using GIC.Data;

namespace GIC.Battle
{


    /// <summary>
    /// 战场草簇装饰层（2026-09-27 地形批次）：AI 生成草簇贴图按立牌同款朝向（billboard yaw + 绕底边后倾）
    /// 随机铺满草地格，合并单 Mesh（submesh=贴图变体，材质=GIC/Battle/GrassSway 顶点摆动、根部锚定）。
    /// 旧方案（TileGrass mesh 烘焙的交叉面草簇 submesh）已退役为空。
    /// 纯视觉装饰：Host 判定/拾取均不感知（与水面波浪视觉件同口径）。
    /// </summary>
    public class BattleGrassDecor : MonoBehaviour
    {
        /// <summary>
        /// 构建草簇层（挂 Tiles 根下；建盘 Build 清空重建时随 Tiles 一并销毁=幂等）。
        /// cam=null 时朝向兜底正北（编辑器侧建盘断言无相机的场景）。
        /// </summary>
        public static BattleGrassDecor Build(Transform parent, BattleMapData map, BattleBoard board,
            TileVisualsConfig config, Camera cam)
        {
            if (config == null || map == null || board == null) return null;

            // 过滤空材质槽（变体数组里允许占位 null）
            var allMats = config.草簇材质变体;
            var tuftMats = new List<Material>();
            if (allMats != null)
                foreach (var m in allMats)
                    if (m != null) tuftMats.Add(m);
            if (tuftMats.Count == 0) return null;

            // 相机水平朝向（与 BattlePlayer 立牌 billboard 同口径：forward 压平到 XZ 平面）
            var camForward = Vector3.forward;
            if (cam != null)
            {
                var f = cam.transform.forward;
                f.y = 0f;
                if (f.sqrMagnitude > 0.001f) camForward = f.normalized;
            }

            float tiltRad = config.草簇后倾角 * Mathf.Deg2Rad;
            float tiltCos = Mathf.Cos(tiltRad);
            float tiltSin = Mathf.Sin(tiltRad);
            float halfWidth = map.width / 2f;
            float halfHeight = map.height / 2f;
            float margin = Mathf.Clamp(config.草簇格内留白, 0f, 0.45f);
            int minCount = Mathf.Max(0, Mathf.Min(config.每格草簇数.x, config.每格草簇数.y));
            int maxCount = Mathf.Max(minCount, Mathf.Max(config.每格草簇数.x, config.每格草簇数.y));
            float hMin = Mathf.Min(config.草簇高度范围.x, config.草簇高度范围.y);
            float hMax = Mathf.Max(hMin, Mathf.Max(config.草簇高度范围.x, config.草簇高度范围.y));
            float jitter = config.草簇朝向抖动;

            // 草簇贴图已离线裁剪到内容框（四周无留白），宽高比=贴图宽/高
            var aspects = new float[tuftMats.Count];
            for (int v = 0; v < tuftMats.Count; v++)
            {
                var tex = tuftMats[v].mainTexture;
                aspects[v] = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 1f;
            }

            var rng = new System.Random();
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>[tuftMats.Count];
            for (int v = 0; v < tris.Length; v++) tris[v] = new List<int>();

            for (int y = 0; y < map.height; y++)
            {
                for (int x = 0; x < map.width; x++)
                {
                    var tileType = map.GetTile(x, y);
                    if (tileType != TileType.Grassland && tileType != TileType.Plain) continue; // 只铺草地（雪/沙/冰不铺）

                    float surfaceY = board.GetSurfaceHeight(new BattleCell(x, y)) + 0.008f; // 根部微抬防与格面 z-fight
                    int count = minCount >= maxCount ? minCount : rng.Next(minCount, maxCount + 1);
                    for (int i = 0; i < count; i++)
                    {
                        int variant = rng.Next(tuftMats.Count);
                        float ox = (float)(rng.NextDouble()) * (1f - 2f * margin) + margin; // 格内留白随机落点（相对格左下角）
                        float oz = (float)(rng.NextDouble()) * (1f - 2f * margin) + margin;
                        var root = new Vector3(x + ox - halfWidth, surfaceY, y + oz - halfHeight);

                        float h = hMin + (hMax - hMin) * (float)rng.NextDouble();
                        float w = h * aspects[variant];

                        // 朝向：立牌同款（billboard yaw + 小随机抖动 + 绕底边后倾）
                        float yaw = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter * Mathf.Deg2Rad;
                        float cosY = Mathf.Cos(yaw), sinY = Mathf.Sin(yaw);
                        // 绕竖直轴旋转 camForward（XZ 平面内）
                        var fwd = new Vector3(camForward.x * cosY - camForward.z * sinY, 0f,
                            camForward.x * sinY + camForward.z * cosY);
                        var right = new Vector3(fwd.z, 0f, -fwd.x); // 水平右向（up×fwd）
                        // 卡片"向上"轴向：顶部远离相机后仰 → up·cos(倾角) + fwd·sin(倾角)
                        var upAlong = new Vector3(fwd.x * tiltSin, tiltCos, fwd.z * tiltSin);

                        int baseIdx = verts.Count;
                        var bl = root - right * (w * 0.5f);
                        var br = root + right * (w * 0.5f);
                        verts.Add(bl); verts.Add(br);
                        verts.Add(br + upAlong * h); verts.Add(bl + upAlong * h);
                        uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                        uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                        // 绕向使面法线朝相机侧（与立牌正面同侧；GrassSway Cull Off 双面渲染下视觉等价，
                        // 但防后续接剔除/新消费方时踩背面）
                        var t = tris[variant];
                        t.Add(baseIdx); t.Add(baseIdx + 2); t.Add(baseIdx + 1);
                        t.Add(baseIdx); t.Add(baseIdx + 3); t.Add(baseIdx + 2);
                    }
                }
            }

            if (verts.Count == 0) return null;

            var go = new GameObject("GrassDecor");
            go.transform.SetParent(parent, false);
            var decor = go.AddComponent<BattleGrassDecor>();

            var mesh = new Mesh { name = "BattleGrassDecor" };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = tuftMats.Count;
            for (int v = 0; v < tuftMats.Count; v++)
                mesh.SetTriangles(tris[v], v);
            mesh.RecalculateBounds();

            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = tuftMats.ToArray();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            GICLog.Info($"[BattleGrassDecor] 草簇层构建完成：{verts.Count / 4} 簇 / {tuftMats.Count} 变体");
            return decor;
        }
    }
}
