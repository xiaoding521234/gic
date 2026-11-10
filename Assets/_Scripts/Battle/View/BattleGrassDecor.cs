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
        /// <summary>本层合并 mesh（OnDestroy 显式销毁——Destroy 物体不销 mesh，§63① mesh 条款，2026-09-28 批6）</summary>
        private Mesh _mesh;

        /// <summary>变体材质缓存（#1 审查批：Rebuild 复用——Build 时解析一次，材质数组不再逐次 ToArray）</summary>
        private Material[] _mats;

        // ==================== 地面贴花子层（碎石/泥土；2026-11-10 画面增强批 A 组） ====================
        // 与草簇同组件同生命周期：BattleBoard.BuildGrassDecor / BattleFog.RebuildGrassDecor 两处
        // 既有接线自动覆盖贴花的迷雾过滤与重建，无需新增消费方。贴花=平贴地表的小 Quad（顶视图贴图），
        // cutout 写深度（与摩拉堆同族）——与水面/雾面/立牌的遮挡由 Z-buffer 承担，不再手排层序。

        /// <summary>贴花合并 mesh（OnDestroy 显式销毁）</summary>
        private Mesh _decalMesh;

        /// <summary>贴花运行时 cutout 材质（每个变体一份；OnDestroy 显式销毁——材质实例生命周期同 _mats 纪律）</summary>
        private Material[] _decalMats;

        /// <summary>贴花材质对应的贴图（取宽高比换算片高）</summary>
        private Texture2D[] _decalTextures;

        /// <summary>碎石变体数量（[0, 该值)=碎石、其后=泥土；由材质数组分段隐含）</summary>
        private int _decalPebbleCount;

        /// <summary>贴花子层物体（无贴花时不建）</summary>
        private GameObject _decalGo;

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
            if (_decalMesh != null) Destroy(_decalMesh);
            if (_decalMats != null)
                foreach (var m in _decalMats)
                    if (m != null) Destroy(m);
        }

        /// <summary>构建草簇层（挂 Tiles 根下；建盘 Build 清空重建时随 Tiles 一并销毁=幂等）。
        /// cam=null 时朝向兜底正北（编辑器侧建盘断言无相机的场景）。
        /// includeCell（迷雾批 2026-10-17）：null=全图；返回 false 的格不产出草簇（任务迷雾未探索格
        /// ——草尖穿混沌盖漏地形）。**固定种子+rng 恒消耗**：随机数按全图逐格逐簇固定序消耗、
        /// include 只裁产出不裁消耗——任意探索态重建下已探索格的草簇布局逐位稳定（揭一格其余草不挪窝）。</summary>
        public static BattleGrassDecor Build(Transform parent, BattleMapData map, BattleBoard board,
            TileVisualsConfig config, Camera cam, System.Func<int, int, bool> includeCell = null)
        {
            if (config == null || map == null || board == null) return null;

            // 过滤空材质槽（变体数组里允许占位 null）
            var allMats = config.草簇材质变体;
            var tuftMats = new List<Material>();
            if (allMats != null)
                foreach (var m in allMats)
                    if (m != null) tuftMats.Add(m);
            var mats = tuftMats.ToArray();

            // 贴花贴图收集（碎石在前、泥土在后——变体索引分段语义）
            CollectDecalTextures(config, out var decalTextures, out int pebbleCount);
            if (mats.Length == 0 && decalTextures.Length == 0) return null;

            // 草簇几何
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>[mats.Length];
            for (int v = 0; v < tris.Length; v++) tris[v] = new List<int>();
            if (mats.Length > 0)
                BuildGeometry(map, board, config, cam, mats, includeCell, verts, uvs, tris);

            // 贴花几何
            var decalVerts = new List<Vector3>();
            var decalUvs = new List<Vector2>();
            var decalTris = new List<int>[decalTextures.Length];
            for (int v = 0; v < decalTris.Length; v++) decalTris[v] = new List<int>();
            if (decalTextures.Length > 0)
                BuildDecalGeometry(map, board, config, decalTextures, pebbleCount, includeCell,
                    decalVerts, decalUvs, decalTris);

            if (verts.Count == 0 && decalVerts.Count == 0) return null;

            var go = new GameObject("GrassDecor");
            go.transform.SetParent(parent, false);
            var decor = go.AddComponent<BattleGrassDecor>();

            if (verts.Count > 0)
            {
                var mesh = new Mesh { name = "BattleGrassDecor" };
                FillMesh(mesh, verts, uvs, tris, mats.Length);

                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                decor._mesh = mesh; // 登记（OnDestroy 显式销毁，§63①）
                decor._mats = mats;
                var renderer = go.AddComponent<MeshRenderer>();
                renderer.sharedMaterials = mats;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            if (decalVerts.Count > 0)
                decor.BuildDecalLayer(go.transform, config, decalTextures, pebbleCount, decalVerts, decalUvs, decalTris);

            GICLog.Info($"[BattleGrassDecor] 装饰层构建完成：草簇 {verts.Count / 4} 簇 / {mats.Length} 变体；"
                + $"贴花 {decalVerts.Count / 4} 件 / {decalTextures.Length} 变体");
            return decor;
        }

        /// <summary>探索态过滤重建（#1 审查批 mesh 复用：迷雾 ApplyFogChanges 高频调用的
        /// BattleBoard.RebuildGrassDecor 改走本口——Clear+回填既有 mesh/材质，不再 Destroy/new
        /// 整层；几何构建与 Build 同源同种子=已探索格布局逐位稳定）。全过滤=mesh 清空+物体失活
        /// （再次揭格自动复活）——与旧「Build 返回 null 下次重建」观感等价</summary>
        public void Rebuild(BattleMapData map, BattleBoard board, TileVisualsConfig config, Camera cam,
            System.Func<int, int, bool> includeCell = null)
        {
            if (map == null || board == null || config == null) return;

            bool hasGrass = _mesh != null && _mats != null && _mats.Length > 0;
            bool hasDecals = _decalMesh != null && _decalMats != null && _decalMats.Length > 0
                && _decalTextures != null && _decalTextures.Length > 0;
            if (!hasGrass && !hasDecals) return;

            bool any = false;
            if (hasGrass)
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>[_mats.Length];
                for (int v = 0; v < tris.Length; v++) tris[v] = new List<int>();
                BuildGeometry(map, board, config, cam, _mats, includeCell, verts, uvs, tris);
                if (verts.Count == 0) _mesh.Clear();
                else { FillMesh(_mesh, verts, uvs, tris, _mats.Length); any = true; }
            }

            if (hasDecals)
            {
                var verts = new List<Vector3>();
                var uvs = new List<Vector2>();
                var tris = new List<int>[_decalMats.Length];
                for (int v = 0; v < tris.Length; v++) tris[v] = new List<int>();
                BuildDecalGeometry(map, board, config, _decalTextures, _decalPebbleCount, includeCell,
                    verts, uvs, tris);
                if (verts.Count == 0) _decalMesh.Clear();
                else { FillMesh(_decalMesh, verts, uvs, tris, _decalMats.Length); any = true; }
            }

            gameObject.SetActive(any);
        }

        /// <summary>mesh 回填（Build/Rebuild 共用：Clear+Set 全量顶点/UV/逐变体 submesh）</summary>
        private static void FillMesh(Mesh mesh, List<Vector3> verts, List<Vector2> uvs, List<int>[] tris,
            int variantCount)
        {
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.subMeshCount = variantCount;
            for (int v = 0; v < variantCount; v++)
                mesh.SetTriangles(tris[v], v);
            mesh.RecalculateBounds();
        }

        // ==================== 地面贴花（碎石/泥土；2026-11-10 画面增强批 A 组） ====================

        /// <summary>收集贴花贴图（碎石在前、泥土在后；变体索引分段语义由 pebbleCount 承载）</summary>
        private static void CollectDecalTextures(TileVisualsConfig config, out Texture2D[] textures, out int pebbleCount)
        {
            var list = new List<Texture2D>();
            pebbleCount = 0;
            if (config.碎石贴图变体 != null)
                foreach (var t in config.碎石贴图变体)
                    if (t != null) { list.Add(t); pebbleCount++; }
            if (config.泥土贴图变体 != null)
                foreach (var t in config.泥土贴图变体)
                    if (t != null) list.Add(t);
            textures = list.ToArray();
        }

        /// <summary>建贴花子层（材质=运行时 cutout 实例：贴图来自配置、参数来自配置；几何已由
        /// BuildDecalGeometry 产出）。cutout 写深度=与水/雾/立牌遮挡交给 Z-buffer（摩拉堆同族结论）</summary>
        private void BuildDecalLayer(Transform parent, TileVisualsConfig config, Texture2D[] textures,
            int pebbleCount, List<Vector3> verts, List<Vector2> uvs, List<int>[] tris)
        {
            _decalTextures = textures;
            _decalPebbleCount = pebbleCount;
            if (_decalMats == null)
            {
                _decalMats = new Material[textures.Length];
                for (int i = 0; i < textures.Length; i++)
                    _decalMats[i] = BattleViewFactory.CreateCutoutUnlitTexturedMaterial(
                        textures[i], Color.white, config.贴花剔除阈值);
            }
            if (_decalGo == null)
            {
                _decalGo = new GameObject("GroundDecals");
                _decalGo.transform.SetParent(parent, false);
                _decalGo.AddComponent<MeshFilter>();
                var newRenderer = _decalGo.AddComponent<MeshRenderer>();
                newRenderer.shadowCastingMode = ShadowCastingMode.Off;
                newRenderer.receiveShadows = false;
            }
            _decalMesh = new Mesh { name = "BattleGroundDecals" };
            FillMesh(_decalMesh, verts, uvs, tris, _decalMats.Length);
            _decalGo.GetComponent<MeshFilter>().sharedMesh = _decalMesh;
            var renderer = _decalGo.GetComponent<MeshRenderer>();
            renderer.sharedMaterials = _decalMats;
            renderer.sortingOrder = BattleMetrics.GroundDecalSortingOrder;
        }

        /// <summary>贴花几何（格级确定性哈希：include 过滤不改变其它格布局，探索态重建逐位稳定）</summary>
        private static void BuildDecalGeometry(BattleMapData map, BattleBoard board, TileVisualsConfig config,
            Texture2D[] textures, int pebbleCount, System.Func<int, int, bool> includeCell,
            List<Vector3> verts, List<Vector2> uvs, List<int>[] tris)
        {
            if (textures == null || textures.Length == 0) return;
            float halfW = map.width / 2f;
            float halfH = map.height / 2f;
            float margin = Mathf.Clamp(config.贴花格内留白, 0f, 0.45f);
            float wMin = Mathf.Min(config.贴花尺寸范围.x, config.贴花尺寸范围.y);
            float wMax = Mathf.Max(wMin, Mathf.Max(config.贴花尺寸范围.x, config.贴花尺寸范围.y));
            float prob = Mathf.Clamp01(config.贴花每格概率);
            float pebbleShare = Mathf.Clamp01(config.碎石占比);
            float lift = config.贴花抬升;
            int pebbleVariants = Mathf.Clamp(pebbleCount, 0, textures.Length);
            int dirtVariants = textures.Length - pebbleVariants;

            for (int y = 0; y < map.height; y++)
            {
                for (int x = 0; x < map.width; x++)
                {
                    if (!IsDecalTile(map.GetTile(x, y))) continue;
                    if (Hash01(x, y, 0) > prob) continue;
                    bool emit = includeCell == null || includeCell(x, y);

                    // —— 参数抽取（与 emit 无关：确定性，探索态重建不挪件）——
                    bool wantPebble = Hash01(x, y, 1) < pebbleShare;
                    int variant;
                    if (wantPebble && pebbleVariants > 0)
                        variant = (int)(Hash01(x, y, 2) * pebbleVariants);
                    else if (!wantPebble && dirtVariants > 0)
                        variant = pebbleVariants + (int)(Hash01(x, y, 2) * dirtVariants);
                    else if (pebbleVariants > 0)
                        variant = (int)(Hash01(x, y, 2) * pebbleVariants); // 泥土缺省回落碎石
                    else
                        variant = (int)(Hash01(x, y, 2) * dirtVariants);   // 碎石缺省回落泥土
                    float ox = margin + Hash01(x, y, 3) * (1f - 2f * margin);
                    float oz = margin + Hash01(x, y, 4) * (1f - 2f * margin);
                    float w = Mathf.Lerp(wMin, wMax, Hash01(x, y, 5));
                    float yaw = Hash01(x, y, 6) * Mathf.PI * 2f;
                    if (!emit) continue;

                    variant = Mathf.Clamp(variant, 0, textures.Length - 1);
                    var tex = textures[variant];
                    float aspect = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 1f;
                    float h = w / Mathf.Max(0.1f, aspect);
                    float cy = board.GetSurfaceHeight(new BattleCell(x, y)) + lift;
                    var center = new Vector3(x + ox - halfW, cy, y + oz - halfH);
                    float cos = Mathf.Cos(yaw), sin = Mathf.Sin(yaw);
                    var right = new Vector3(cos, 0f, sin) * (w * 0.5f);
                    var fwd = new Vector3(-sin, 0f, cos) * (h * 0.5f);

                    int i0 = verts.Count;
                    verts.Add(center - right - fwd); // a 左下
                    verts.Add(center + right - fwd); // b 右下
                    verts.Add(center + right + fwd); // c 右上
                    verts.Add(center - right + fwd); // d 左上
                    uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                    uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                    var t = tris[variant];
                    t.Add(i0 + 3); t.Add(i0 + 2); t.Add(i0 + 1); // d,c,b（绕向=法线 +Y，与水面同约定）
                    t.Add(i0 + 3); t.Add(i0 + 1); t.Add(i0);     // d,b,a
                }
            }
        }

        /// <summary>贴花可铺地形（草地/平原/土地/石路/圆石；沙/雪/冰/水面/沼泽/薄冰不铺）</summary>
        private static bool IsDecalTile(TileType tile)
        {
            return tile == TileType.Grassland || tile == TileType.Plain
                || tile == TileType.Dirt || tile == TileType.StonePath || tile == TileType.Cobblestone;
        }

        /// <summary>格级确定性哈希（salt=用途域；纯装饰零随机源，重建逐位稳定）</summary>
        private static float Hash01(int x, int y, int salt)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791 + 20261110);
                h ^= h >> 13; h *= 0x85EBCA6Bu; h ^= h >> 16;
                return (h & 0xFFFFFF) / 16777215f;
            }
        }

        /// <summary>几何构建（Build/Rebuild 同源）：固定种子 rng 恒消耗+include 只裁产出——
        /// 任意探索态重建下已探索格的草簇布局逐位稳定</summary>
        private static void BuildGeometry(BattleMapData map, BattleBoard board, TileVisualsConfig config, Camera cam,
            Material[] tuftMats, System.Func<int, int, bool> includeCell,
            List<Vector3> verts, List<Vector2> uvs, List<int>[] tris)
        {
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
            var aspects = new float[tuftMats.Length];
            for (int v = 0; v < tuftMats.Length; v++)
            {
                var tex = tuftMats[v].mainTexture;
                aspects[v] = tex != null && tex.height > 0 ? (float)tex.width / tex.height : 1f;
            }

            // 固定种子（迷雾批 2026-10-17）：重建确定性——探索态过滤重建（Rebuild）下
            // 已探索格草簇布局稳定；代价=全图草簇布局相对旧版（无种子）变化一次，纯装饰可接受
            var rng = new System.Random(20260927);

            for (int y = 0; y < map.height; y++)
            {
                for (int x = 0; x < map.width; x++)
                {
                    var tileType = map.GetTile(x, y);
                    if (tileType != TileType.Grassland && tileType != TileType.Plain) continue; // 只铺草地（雪/沙/冰不铺）

                    float surfaceY = board.GetSurfaceHeight(new BattleCell(x, y)) + 0.008f; // 根部微抬防与格面 z-fight
                    int count = minCount >= maxCount ? minCount : rng.Next(minCount, maxCount + 1);
                    // 迷雾批：rng 恒消耗（count 与逐簇参数含 yaw 照常抽取）、include 只裁产出——
                    // 探索态重建不挪已探索格的草窝；includeCell=null（无雾/对战迷雾）=全产出
                    bool emit = includeCell == null || includeCell(x, y);
                    for (int i = 0; i < count; i++)
                    {
                        // —— rng 消耗段（顺序固定，emit 不裁）——
                        int variant = rng.Next(tuftMats.Length);
                        float ox = (float)(rng.NextDouble()) * (1f - 2f * margin) + margin; // 格内留白随机落点（相对格左下角）
                        float oz = (float)(rng.NextDouble()) * (1f - 2f * margin) + margin;
                        float h = hMin + (hMax - hMin) * (float)rng.NextDouble();
                        float yaw = (float)(rng.NextDouble() * 2.0 - 1.0) * jitter * Mathf.Deg2Rad;
                        if (!emit) continue;

                        // —— 产出段（几何，零 rng）——
                        var root = new Vector3(x + ox - halfWidth, surfaceY, y + oz - halfHeight);
                        float w = h * aspects[variant];

                        // 朝向：立牌同款（billboard yaw + 小随机抖动 + 绕底边后倾）
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
        }
    }
}
