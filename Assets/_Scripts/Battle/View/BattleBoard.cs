using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using GIC.Data;
namespace GIC.Battle
{


    /// <summary>
    /// 战场棋盘：按 BattleMapData 实例化 3D 格子（有厚度地块）并做格↔世界坐标换算。
    /// 棋盘平面 = XZ（Y 向上），斜俯视正交相机呈现。
    /// </summary>
    public class BattleBoard : MonoBehaviour
    {
        [Header("格子预制体")]
        [SerializeField] private GameObject _grassTilePrefab;
        [SerializeField] private GameObject _waterTilePrefab;
        [SerializeField] private GameObject _stoneTilePrefab;
        [SerializeField] private GameObject _dirtTilePrefab;
        [SerializeField] private GameObject _cobbleTilePrefab;

        [Header("地块视觉")]
        [Tooltip("地块视觉配置（顶面变体材质/草簇装饰参数；null=全部用地块 prefab 自带材质、无草簇）")]
        [SerializeField] private TileVisualsConfig _tileVisuals;

        [Header("表面高度")]
        [Tooltip("地面格顶面高度（单位站此高度）")]
        [SerializeField] private float _tileTopHeight = 0.5f;
        [Tooltip("水面格顶面高度（水面相对下沉）")]
        [SerializeField] private float _waterSurfaceHeight = 0.36f;

        [Header("水面")]
        [Tooltip("每格水面细分格数：顶点波浪按细分采样插值（4=顶点间距 0.25 格，波浪平滑；1=每格仅 4 顶点，波面呈格状折痕）")]
        [InspectorName("水面细分")]
        [SerializeField] private int _waterSurfaceSubdiv = 4;

        [Header("薄冰（决策七十六：冰元素冻结水面——半透明冰面板叠水面，水下流动仍隐约可见）")]
        [Tooltip("薄冰主色（运行时克隆水面材质改 tint——波场公式同源=冰面随下方波浪起伏恒贴水面）")]
        [SerializeField] private Color 薄冰色 = new Color(0.78f, 0.90f, 0.96f, 0.80f);
        [Tooltip("薄冰深色（shader _DeepColor——冰下水的暗部）")]
        [SerializeField] private Color 薄冰深色 = new Color(0.30f, 0.44f, 0.55f, 1f);
        [Tooltip("薄冰流动速度（低于水面=冰下缓流感；0=静止）")]
        [SerializeField] private float 薄冰流动速度 = 0.012f;
        [Tooltip("薄冰波峰高光（低=哑光冰面）")]
        [SerializeField, Range(0f, 1f)] private float 薄冰高光 = 0.10f;
        [Tooltip("薄冰面板相对水面的高度余量（同波场同相位=恒定间隙不穿插）")]
        [SerializeField] private float 薄冰抬升 = 0.006f;

        [Header("迷雾（2026-10-17 迷雾批，docs/active/44：对战=半透明雾面盖未探索格 / 任务=不透明混沌盖替换地形）")]
        [Tooltip("对战迷雾色（半透明暗雾——RA2 式：地形完整可见，雾面只作「没去过」标记）")]
        [SerializeField] private Color 对战迷雾色 = new Color(0.031f, 0.055f, 0.110f, 0.52f);
        [Tooltip("雾面抬升（世界单位——雾面悬在地形视觉表面之上防 z-fight；水格视觉表面已含波峰带）")]
        [SerializeField] private float 迷雾面抬升 = 0.012f;
        [Tooltip("任务迷雾混沌盖顶面高度（盖体自 y=0 全包围真地块；顶面略高于地块顶 0.5）")]
        [SerializeField] private float 混沌盖高 = 0.505f;
        [Tooltip("混沌盖边界外扩（世界单位——盖侧越过格界盖住邻格地块侧面：防真地形缝隙泄露、避共面 z-fight）")]
        [SerializeField] private float 混沌盖外扩 = 0.005f;
        [Tooltip("任务迷雾混沌盖材质（null=运行时 Resources/UI/Battle/ChaosFog 贴图自建 Unlit 材质；再缺=深暗纯色兜底）")]
        [SerializeField] private Material 混沌盖材质;

        public BattleMapData Map { get; private set; }

        /// <summary>地面格顶面高度（板面点击拾取的视差修正初判平面高度，docs/14 §86）</summary>
        public float TileTopHeight => _tileTopHeight;

        private Transform _tilesRoot;

        /// <summary>本盘运行时生成的 mesh（水面焊接面等；Build 重建/OnDestroy 显式销毁——
        /// Destroy 物体不销 mesh，§63① mesh 条款，2026-09-28 批6）</summary>
        private readonly List<Mesh> _runtimeMeshes = new List<Mesh>();

        /// <summary>薄冰面板运行时件（mesh/材质实例/物体——元素×地形变化重建、OnDestroy 释放，
        /// 决策七十六）</summary>
        private GameObject _thinIceSurface;
        private Mesh _thinIceMesh;
        private Material _thinIceMaterial;
        /// <summary>#17 审查批：薄冰面板重建帧内合并——同帧多条 TerrainChange 命令（多格冻结/融化）
        /// 只触发一次全量重建（真值 Map.SetTile 仍即时，瞄准/落地判定等逻辑消费方不受影响）</summary>
        private bool _thinIceDirty;

        /// <summary>迷雾面/混沌盖运行时件（探索态变化重建、OnDestroy 释放，迷雾批 2026-10-17）</summary>
        private GameObject _fogOverlayGo;
        private Mesh _fogOverlayMesh;
        private Material _fogOverlayMaterial; // 对战雾面（半透明实例）或混沌盖（贴图/纯色实例）
        private static Texture2D _chaosFogTexture; // 混沌贴图静态缓存（Resources/UI/Battle/ChaosFog）

        /// <summary>草簇装饰层引用（迷雾批：探索态过滤重建用——未探索格不生成草簇防草尖穿混沌盖）</summary>
        private BattleGrassDecor _grassDecor;

        private void OnDestroy()
        {
            foreach (var mesh in _runtimeMeshes)
                if (mesh != null) Destroy(mesh);
            _runtimeMeshes.Clear();
            if (_thinIceMaterial != null) Destroy(_thinIceMaterial);
            if (_moraPileMaterial != null) Destroy(_moraPileMaterial); // 灰盒材质实例清理（§63 纪律）
        }

        /// <summary>
        /// 建盘（幂等：重复调用先清空重建）
        /// </summary>
        public void Build(BattleMapData map)
        {
            Map = map;
            // 维度初始地形快照（2026-10-25 拍板：地脉不与主世界同步地形——开局与主世界一模一样，
            // 此后主世界 TerrainChange〔冻薄冰/融化〕不演化非主世界维度；②c 泛化=每非主世界维度
            // 一份快照，未来某维度需独立演化时在其秒拍末规则里写自己的表即可）
            _dimensionInitialTiles.Clear();
            foreach (BattleDimension dim in Enum.GetValues(typeof(BattleDimension)))
            {
                if (dim == BattleDimension.MainWorld) continue;
                _dimensionInitialTiles[dim] = map.tiles != null ? map.tiles.ToArray() : null;
            }
            _activeTerrainDimension = BattleDimension.MainWorld;

            if (_tilesRoot == null)
            {
                var rootGo = new GameObject("Tiles");
                rootGo.transform.SetParent(transform, false);
                _tilesRoot = rootGo.transform;
            }
            // 旧盘运行时 mesh 显式销毁（Destroy 物体不销 mesh——§63① mesh 条款，2026-09-28 批6）
            foreach (var mesh in _runtimeMeshes)
                if (mesh != null) Destroy(mesh);
            _runtimeMeshes.Clear();
            _thinIceDirty = false; // #17 审查批：清掉挂起的重建（Build 尾部自己调 RebuildThinIceSurface）
            _fogOverlayGo = null;    // #1 审查批：随 Tiles 子级销毁的字段复位（EnsureFogOverlayGO 空则重建）
            _fogOverlayMesh = null;
            _grassDecor = null;      // 同上（草簇层随 Tiles 子级销毁）
            for (int i = _tilesRoot.childCount - 1; i >= 0; i--)
                Destroy(_tilesRoot.GetChild(i).gameObject);

            for (int y = 0; y < map.height; y++)
            {
                for (int x = 0; x < map.width; x++)
                {
                    var tileType = map.GetTile(x, y);
                    if (!map.HasTile(x, y)) continue; // 虚空格不放地块

                    var prefab = GetTilePrefab(tileType);
                    if (prefab == null) continue;

                    var tile = Instantiate(prefab, _tilesRoot);
                    tile.name = $"Tile_{x}_{y}";
                    tile.transform.localPosition = TileBottomPosition(x, y);
                    ApplyTopVariant(tile, tileType, x, y);
                    // 朝向默认统一（2026-09-21 用户拍板：随机 90° 旋转观感乱，全盘同向）
                }
            }

            BuildWaterSurface(map);
            RebuildThinIceSurface(); // 幂等（初始地图无薄冰=空面板；防御性支持已含薄冰的重建）
            RebuildResourceNodes(map.resourceNodes); // 资源点表现件（docs/47：开局态；快照/命令增量维护）
        }

        // ==================== 资源点表现（docs/47 资源点批，2026-10-27；同日追拍「直接用摩拉素材+
        // 数量随采集变少」；2026-10-28 返修一=55° 平面内堆叠+迷雾隐藏；返修二=透明排序→cutout 深度管线） ====================
        // 摩拉堆=官方 mora.png（ItemConfig.Mora icon[0] 同源）立式小片堆：**件数=剩余可采次数**
        //（满堆 10 件金字塔〔底 4/二 3/三 2/顶 1〕，采集 1 次=顶件隐藏，余 1 件=底排居中）——
        // 「显示的摩拉数量也变少」拍板直译。
        // **材质=cutout（AlphaTest 2450+ZWrite On，2026-10-28 返修二「顶件像在水下」）**：返修一的
        // 件层序（sortingOrder=−3−index）把顶件压到水面（−2）之下——透明材质不写 Z-buffer、
        // 透明件之间只按 sortingOrder 排（**与世界坐标高度无关**，Unity 官方 Renderer.sortingOrder
        // 口径），水面后画半透明叠上=顶件隔水观感。**业界正解（HD-2D 路线，调研=Octopath billboard
        // 深度管线+newarteest「hard-edged cutouts 深度排序正确」）**：硬边小件走 cutout 写深度，
        // 先画并占据深度，后画的透明面（水面/雾面/立牌/瞄准贴片）depth test 自动只在无更近深度处
        // 混合——「高于水面就盖住水面」由 Z-buffer 机制保证，不再依赖手排 order。
        // 挂 BattleMain 层=地脉视角 culling mask 自动隐藏（BattleViewFactory.SetMainWorldLayer 单口）。

        /// <summary>格→资源点表现件（每格至多一个；Build 清空重建/命令增量维护）</summary>
        private readonly Dictionary<BattleCell, GameObject> _resourceNodeViews = new Dictionary<BattleCell, GameObject>();

        /// <summary>堆件迷雾隐藏态缓存（cell→hidden；跨快照重建存续——秒拍头 RebuildResourceNodes
        /// 重建件按缓存态落地，防「秒拍头未探索堆闪现一帧再隐藏」）</summary>
        private readonly Dictionary<BattleCell, bool> _resourceNodeFogHidden = new Dictionary<BattleCell, bool>();

        /// <summary>摩拉件共享材质（OnDestroy 清理实例——_thinIceMaterial 同款纪律）</summary>
        private Material _moraPileMaterial;

        /// <summary>摩拉件 55° 面旋转（布局面内坐标→世界偏移的变换单源；yaw 抖动只转件自身
        /// 不改堆叠面——「堆叠在他们自己的55度平面内」2026-10-28 返拍的本体）</summary>
        private static readonly Quaternion MoraChunkPlaneRotation = Quaternion.Euler(55f, 0f, 0f);

        /// <summary>摩拉件固定堆叠布局（金字塔从底到顶；件数=remainingCharges 从底部取前 N 件——
        /// 采集从顶减=堆自然变小；余 1 件=底排居中）。**坐标=件局部系**（2026-10-28 返修一
        /// 「应当堆叠是在他们自己的55度平面内」+返修二 z 分量语义）：x=面内横向〔世界 X〕、
        /// y=面内高度〔沿面内向上 u=(0,cos55°,sin55°)=上+远离相机；行进 0.10/排=上排件自下排
        /// 件顶探出；底排 y=0.20=半件面内高→件底边恰触地块表面〕、**z=法向微偏移**
        /// 〔返修二：0.0025×(9−index) 底排最朝相机逐件递减——件横距 0.17<件宽 0.40、行距 0.10
        /// <件高 0.40 均屏幕重叠，纯共面则 cutout ZWrite 下重叠区深度全等=z-fighting；法向逐件
        /// 错开=每件独立平行平面（深度差 0.0025 起≫深度精度），底排（近）恒盖上排（远）下缘
        /// =金字塔正确遮挡；总量 ≤0.023 视觉不可辨，法向偏移同时微沉底件=插入表面更扎实〕——
        /// 世界偏移=MoraChunkPlaneRotation×(x,y,z)。确定性写死零随机。
        /// 旧版=世界系垂直抬升+z 朝相机倒伏（上排悬空探向镜头）——平移视角时上层件与堆体脱开
        /// =「上面的摩拉在浮空」报障根因，勿回退。2026-10-27 二次追拍「摩拉要调大」：
        /// 件 0.24→0.40 格+布局放大 ~1.5×（屏上约 27px/枚；首版 0.24〔约 16px/枚〕被用户目检
        /// 「完全未看见」淘汰）</summary>
        private static readonly Vector4[] MoraChunkLayout =
        {
            // 底排 4 件（y=0.20=半件面内高，件底边触地；z 法向偏移最大=最朝相机）
            new Vector4(-0.265f, 0.20f, 0.0225f,  12f),
            new Vector4(-0.088f, 0.20f, 0.0200f,  -7f),
            new Vector4( 0.084f, 0.20f, 0.0175f,  16f),
            new Vector4( 0.258f, 0.20f, 0.0150f, -11f),
            // 二排 3 件（面内 +0.10）
            new Vector4(-0.171f, 0.30f, 0.0125f, -14f),
            new Vector4( 0.012f, 0.30f, 0.0100f,   9f),
            new Vector4( 0.192f, 0.30f, 0.0075f,  -5f),
            // 三排 2 件
            new Vector4(-0.072f, 0.40f, 0.0050f,   7f),
            new Vector4( 0.117f, 0.40f, 0.0025f, -13f),
            // 顶 1 件（法向偏移 0=基准面）
            new Vector4( 0.018f, 0.50f, 0.0000f,   4f),
        };

        /// <summary>资源点表现件全量重建（Build 开局态+秒拍头快照权威自愈同口）</summary>
        public void RebuildResourceNodes(List<ResourceNodeState> nodes)
        {
            foreach (var view in _resourceNodeViews.Values)
                if (view != null) DestroyResourceNodeView(view);
            _resourceNodeViews.Clear();
            if (nodes == null) return;
            foreach (var node in nodes)
            {
                if (node == null || node.remainingCharges <= 0) continue;
                var view = CreateResourceNodeView(node);
                if (view != null) _resourceNodeViews[node.cell] = view;
            }
        }

        /// <summary>采集增量（ResourceHarvest 命令客户端入口）：**件数=剩余次数**（追拍「数量随采集
        /// 变少」）——remaining>0=隐藏顶部 (N−remaining) 件；=0=表现件整体移除（耗尽消失拍板⑤）。
        /// 幂等：未知格直通</summary>
        public void ApplyResourceHarvest(BattleCell cell, int remaining)
        {
            if (!_resourceNodeViews.TryGetValue(cell, out var view)) return;
            if (view == null) { _resourceNodeViews.Remove(cell); return; }
            if (remaining <= 0)
            {
                DestroyResourceNodeView(view);
                _resourceNodeViews.Remove(cell);
                return;
            }
            ApplyChunkCount(view, remaining);
        }

        /// <summary>表现件销毁（双域安全——BattleSimState.RemoveUnit 同款纪律：编辑模式 Destroy
        /// 报错且静默不销毁〔地图预览/编辑器域断言路径会踩〕，isPlaying 分流）</summary>
        private static void DestroyResourceNodeView(GameObject view)
        {
            if (Application.isPlaying) Destroy(view);
            else DestroyImmediate(view);
        }

        /// <summary>摩拉件堆建件：件数=remainingCharges（追拍语义本体），根下 N 子件按
        /// MoraChunkLayout 前 N 位摆（透明贴图材质=mora.png 白 tint）</summary>
        private GameObject CreateResourceNodeView(ResourceNodeState node)
        {
            if (Map == null) return null;
            if (_moraPileMaterial == null)
            {
                var tex = UnityEngine.Resources.Load<Texture2D>("UI/Items/mora"); // 官方摩拉图标（ItemConfig.Mora icon[0] 同源）
                // cutout 材质（2026-10-28 返修二）：写深度+AlphaTest 队列——与水面等透明面由
                // depth test 机制性正确遮挡（「高于水面=盖住水面」），透明排序病灶根治
                _moraPileMaterial = BattleViewFactory.CreateCutoutUnlitTexturedMaterial(tex, Color.white);
            }

            var rootGo = new GameObject($"ResourceNode_{node.cell.x}_{node.cell.y}");
            rootGo.transform.SetParent(_tilesRoot, false);
            // **堆放地块表面**（2026-10-27 返修二真根因）：TileBottomPosition=预制体底面中心（y=0），
            // 地块是厚 _tileTopHeight(0.5) 的 3D 块——首版堆从底面起算=整堆埋进地形块内部，
            // 相机全图渲染四堆窗金像素=0、用户两轮「完全未看见」（灰盒版同错故从未可见）；
            // 草簇能见=GetSurfaceHeight 贴表面先例。抬升走 _tileTopHeight 序列化值（勿写死 0.5 双源）
            rootGo.transform.localPosition = TileBottomPosition(node.cell.x, node.cell.y)
                + new Vector3(0f, _tileTopHeight, 0f);
            BattleViewFactory.SetMainWorldLayer(rootGo); // 地脉视角自动隐藏（子件层随根同步见 SpawnMoraChunk）
            int count = Mathf.Clamp(node.remainingCharges, 0, MoraChunkLayout.Length);
            for (int i = 0; i < count; i++)
                SpawnMoraChunk(rootGo.transform, i);
            if (_resourceNodeFogHidden.TryGetValue(node.cell, out var fogHidden) && fogHidden)
                rootGo.SetActive(false); // 迷雾隐藏态跨重建保持（防秒拍头快照重建未探索堆闪现一帧）
            return rootGo;
        }

        /// <summary>按件数刷子件显隐（采集增量——从顶减：显示前 remaining 件=布局表从底取，余 1 件
        /// 落在底排居中附近）</summary>
        private static void ApplyChunkCount(GameObject view, int remaining)
        {
            int target = Mathf.Clamp(remaining, 0, MoraChunkLayout.Length);
            for (int i = 0; i < view.transform.childCount; i++)
            {
                var chunk = view.transform.GetChild(i);
                if (chunk.name.StartsWith("MoraChunk"))
                    chunk.gameObject.SetActive(i < target);
            }
        }

        /// <summary>资源点迷雾显隐（BattleFog 每帧消费，2026-10-28 返修拍板「摩拉堆在迷雾中应当
        /// 隐藏」）：未探索格堆件整体隐藏（表现层镜像——探索过永久可见同单位口径；采集角色对
        /// 资源点的透视=AI 感知层豁免〔CompanionBrain 采集候选不查探索账本，核心透视 IsUnitVisibleTo
        /// 同构〕，客户端渲染无透视）。幂等：隐藏态未变零开销；写入缓存供重建路径（Create 时落地）
        /// 消费——防秒拍头快照重建闪现</summary>
        public void ApplyResourceNodeFogVisibility(System.Func<BattleCell, bool> isVisible)
        {
            foreach (var kv in _resourceNodeViews)
            {
                if (kv.Value == null) continue;
                bool hidden = !isVisible(kv.Key);
                if (_resourceNodeFogHidden.TryGetValue(kv.Key, out var prev) && prev == hidden) continue;
                _resourceNodeFogHidden[kv.Key] = hidden;
                kv.Value.SetActive(!hidden);
            }
        }

        /// <summary>单枚摩拉小片：Quad 0.40 立式后仰 55°（草簇同款「顶部远离相机后仰」姿态）+yaw 抖动
        ///（布局表确定性）；**布局偏移经 55° 平面旋转变换落地**（2026-10-28 返修一「堆叠在他们自己
        /// 的55度平面内」——件局部系坐标含法向微偏移，见 MoraChunkLayout 注释）；材质=cutout 写深度
        ///（返修二）——件间遮挡由 depth test 决定（法向偏移逐件分离防 z-fighting、底排近=盖上排），
        /// 与水面/雾面/立牌的遮挡同样由深度管线承载；sortingOrder 统一登记层位（cutout 队列内
        /// order 只影响绘制顺序不影响结果，留作未来查层定位）。
        /// **角度符号=+55 勿改负**（2026-10-27 返修：-55° 把 Quad 法线转到朝下 (0,−0.819,−0.574)
        /// =相机俯视只见背面，URP Unlit 默认 Cull Back 剔除=整堆隐形〔用户报障「怎么没看到有摩拉」
        /// 根因〕；+55° 法线 (0,+0.819,−0.574) 朝上偏相机侧=正面可见。Quad 正面法线朝 −Z（草簇无
        /// 此坑=自建 mesh 手排法线+Cull Off；ChromaKeyVideo/FrozenSprite 同族「翻面剔除」先例）。
        /// Collider 销毁双域安全（编辑模式 Destroy 报错不销毁——RemoveUnit 同款纪律）</summary>
        private void SpawnMoraChunk(Transform parent, int layoutIndex)
        {
            var chunk = GameObject.CreatePrimitive(PrimitiveType.Quad);
            chunk.name = "MoraChunk";
            var collider = chunk.GetComponent<Collider>();
            if (Application.isPlaying) Destroy(collider); // 资源点非物理件（勿挡射线/碰撞）
            else DestroyImmediate(collider);
            chunk.transform.SetParent(parent, false);
            var layout = MoraChunkLayout[layoutIndex];
            chunk.transform.localPosition = MoraChunkPlaneRotation * new Vector3(layout.x, layout.y, layout.z);
            chunk.transform.localRotation = Quaternion.Euler(55f, layout.w, 0f);
            chunk.transform.localScale = Vector3.one * 0.40f; // 2026-10-27 二次追拍「摩拉要调大」：0.24→0.40（约 27px/枚）
            var renderer = chunk.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = _moraPileMaterial;
            renderer.sortingOrder = BattleMetrics.MoraPileSortingOrder; // 层位登记（遮挡由深度管线决定，勿再加 −index）
            chunk.layer = parent.gameObject.layer; // 维度层随根
        }

        /// <summary>地形变化应用（决策七十六元素×地形，TerrainChange 命令客户端入口）：
        /// 本地 map 真值写入（移动瞄准/箭矢落地判地形等消费方即时正确）+薄冰面板重建
        /// （#17 审查批改帧内合并：同帧多命令一次全量重建——全量重建仍最简且天然幂等；
        /// 水面焊接面不动——薄冰格**保留**下方流动水面=拍板「半透明冰面板叠水面」）</summary>
        public void ApplyTerrainChange(BattleCell cell, TileType newTile)
        {
            if (Map == null) return;
            Map.SetTile(cell.x, cell.y, newTile); // 真值即时（逻辑消费方立刻正确）
            _thinIceDirty = true;                  // 面板重建帧内合并（Update 消费）
        }

        private void Update()
        {
            if (_thinIceDirty)
            {
                _thinIceDirty = false;
                RebuildThinIceSurface();
            }
        }

        // ==================== 迷雾（2026-10-17 迷雾批，docs/active/44） ====================

        /// <summary>清迷雾面/混沌盖（探索满格=无雾；ClearAll/重建前调用）</summary>
        public void ClearFogOverlay()
        {
            if (_fogOverlayGo != null) { Destroy(_fogOverlayGo); _fogOverlayGo = null; }
            if (_fogOverlayMesh != null) { _runtimeMeshes.Remove(_fogOverlayMesh); Destroy(_fogOverlayMesh); _fogOverlayMesh = null; }
            _fogOverlayIntent = false; // 决策九十一：GO 已毁，意图复位（重建后按雾格集重评）
        }

        // ==================== 维度视角雾面让位（决策九十一，2026-10-25） ====================

        /// <summary>雾面重建的「应有」意图（探索集决定：true=有未探索格应显示）</summary>
        private bool _fogOverlayIntent;

        /// <summary>地脉视角隐藏旗（灵界透视——切地脉视角雾面整体让位；重建 SetActive 经本旗门控，
        /// 视角切回自动恢复当下正确态）</summary>
        private bool _dimensionFogHidden;

        /// <summary>雾面可见性落地单出口：意图 && !地脉视角隐藏——重建方与维度切换方共用</summary>
        private void SetFogOverlayActive(bool intent)
        {
            _fogOverlayIntent = intent;
            if (_fogOverlayGo != null)
                _fogOverlayGo.SetActive(intent && !_dimensionFogHidden); // 唯一原生活性写点（本方法体内的直写勿再替换）
        }

        /// <summary>雾面显隐（决策九十一：BattlePlayer.SetDimensionView 消费——地脉视角=灵界透视
        /// 雾面让位、主世界视角=恢复当下探索态）。幂等直通</summary>
        public void SetFogOverlayVisible(bool visible)
        {
            if (_dimensionFogHidden == !visible) return;
            _dimensionFogHidden = !visible;
            SetFogOverlayActive(_fogOverlayIntent); // 经单出口重放当前意图（含地脉旗）
        }

        // ==================== 地脉维度地形（2026-10-25 拍板追记：地脉地形独立演化）====================

        /// <summary>各非主世界维度的初始地形快照（维度→平铺 tiles 拷贝；主世界=live Map 真值不占表。
        /// 2026-10-26 ②c 泛化：原单份 _initialTiles 升字典）</summary>
        private readonly Dictionary<BattleDimension, int[]> _dimensionInitialTiles = new Dictionary<BattleDimension, int[]>();

        /// <summary>当前视角取用的地形维度（主世界=Map 当前真值；雾面让位旗同族视角态）</summary>
        private BattleDimension _activeTerrainDimension = BattleDimension.MainWorld;

        /// <summary>按维度取地形：非主世界=该维度初始快照（主世界冻/融不演化）、主世界=当前真值</summary>
        private TileType TerrainSourceTile(int x, int y)
        {
            if (_activeTerrainDimension != BattleDimension.MainWorld
                && _dimensionInitialTiles.TryGetValue(_activeTerrainDimension, out var tiles)
                && tiles != null && Map != null
                && x >= 0 && x < Map.width && y >= 0 && y < Map.height)
                return (TileType)tiles[y * Map.width + x];
            return Map != null ? Map.GetTile(x, y) : TileType.None;
        }

        /// <summary>维度视角地形切换（BattlePlayer.SetDimensionView 消费）：按维度重铺薄冰面——
        /// 非主世界=初始快照（水格上仍结着开局的冰）、主世界=当前真值（含融化/新冻）。幂等直通；
        /// _thinIceDirty 帧内合并重建（Update 消费，与 ApplyTerrainChange 同路径）</summary>
        public void SetDimensionTerrain(BattleDimension dimension)
        {
            if (_activeTerrainDimension == dimension) return;
            _activeTerrainDimension = dimension;
            _thinIceDirty = true; // 按新地形源全量重建（RebuildThinIceSurface 读 TerrainSourceTile=维度感知）
        }

        /// <summary>雾面/混沌盖共享运行时件确保（#1 审查批 mesh 复用）：首次建 GO+mesh，此后两模式
        /// 重建均 Clear+回填本件——原每次重建 Destroy/new 全套（GO+mesh+组件）为移动期逐事件重建的
        /// GC/卡顿源；材质与层由各模式重建方自行刷新（两模式共用一件、切换换材质）。清盘后字段复位
        /// 由 Build 负责（fake-null 兜底）</summary>
        private void EnsureFogOverlayGO(string meshName)
        {
            if (_fogOverlayGo != null && _fogOverlayMesh != null) return;
            _fogOverlayMesh = new Mesh { name = meshName };
            _runtimeMeshes.Add(_fogOverlayMesh);
            _fogOverlayGo = new GameObject("FogOverlay");
            _fogOverlayGo.transform.SetParent(_tilesRoot, false);
            _fogOverlayGo.AddComponent<MeshFilter>().sharedMesh = _fogOverlayMesh;
            var mr = _fogOverlayGo.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        /// <summary>对战迷雾面重建（cells=未探索格）：合并单 Mesh 半透明暗雾，每格按**视觉表面高度**
        /// +抬升（水/沼/薄冰格已含波峰带）——地形完整可见、雾面只作「没去过」标记（docs/03 §3.6
        /// RA2 式）。虚空格不盖（对战迷雾下虚空豁口正常可见——拍板 D6）。层=-2：水面/冰面（-3）之上、
        /// 底座盘（-1，仅出现在已探索格）之下。
        /// #1 审查批 mesh 复用：EnsureFogOverlayGO 建件一次，重建 Clear+回填（原 Destroy/new 全套）</summary>
        public void RebuildSkirmishFog(List<BattleCell> cells)
        {
            EnsureFogOverlayGO("BattleFogOverlay");
            if (_fogOverlayMaterial == null)
                _fogOverlayMaterial = BattleViewFactory.CreateTransparentUnlitMaterial(对战迷雾色);
            else
                _fogOverlayMaterial.color = 对战迷雾色; // Inspector 热改后重建沿用新值
            var mr = _fogOverlayGo.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _fogOverlayMaterial;
            mr.sortingOrder = BattleMetrics.FogOverlaySortingOrder;

            if (Map == null || cells == null || cells.Count == 0)
            {
                SetFogOverlayActive(false); // 探索满格=无雾面
                return;
            }

            var verts = new List<Vector3>(cells.Count * 4);
            var uvs = new List<Vector2>(cells.Count * 4);
            var tris = new List<int>(cells.Count * 6);
            float halfW = Map.width / 2f, halfH = Map.height / 2f;
            foreach (var cell in cells)
            {
                if (!Map.HasTile(cell.x, cell.y)) continue; // 虚空格不盖
                float h = GetVisualSurfaceHeight(cell) + 迷雾面抬升;
                // 与水面同锚（角点=格界；a=(x0,z0) b=(x1,z0) c=(x1,z1) d=(x0,z1)，绕向法线朝上）
                float x0 = cell.x - halfW, x1 = cell.x + 1f - halfW;
                float z0 = cell.y - halfH, z1 = cell.y + 1f - halfH;
                int a = verts.Count; verts.Add(new Vector3(x0, h, z0));
                int b = verts.Count; verts.Add(new Vector3(x1, h, z0));
                int c = verts.Count; verts.Add(new Vector3(x1, h, z1));
                int d = verts.Count; verts.Add(new Vector3(x0, h, z1));
                uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
                tris.AddRange(new[] { d, c, b, d, b, a });
            }
            if (verts.Count == 0)
            {
                SetFogOverlayActive(false);
                return;
            }

            SetFogOverlayActive(true);
            _fogOverlayMesh.Clear();
            _fogOverlayMesh.SetVertices(verts);
            _fogOverlayMesh.SetUVs(0, uvs);
            _fogOverlayMesh.SetTriangles(tris, 0);
            _fogOverlayMesh.RecalculateBounds();
        }

        /// <summary>任务迷雾混沌盖重建（cells=未探索格，**含虚空格**——整片未知区域浑然一体不泄露
        /// 地图轮廓，拍板 D6）：合并单 Mesh 不透明箱盖（顶+四侧，y 0→混沌盖高），边界外扩越过格界
        /// ——真地块全部暴露面（顶/四侧/水面/薄冰）被罩住=地形类型零泄露；外扩同时避免与邻格地块
        /// 侧面共面 z-fight（盖侧在邻侧面前 5mm 恒遮挡）。不透明+深度写=与透明件排序体系正交。
        /// 每面独立 4 顶点（UV 0..1 全幅——侧面不与顶面共享顶点防贴图压扁）。</summary>
        public void RebuildMissionFog(List<BattleCell> cells)
        {
            EnsureFogOverlayGO("BattleFogLids");
            if (Map == null || cells == null || cells.Count == 0)
            {
                SetFogOverlayActive(false);
                return;
            }

            var verts = new List<Vector3>(cells.Count * 20);
            var uvs = new List<Vector2>(cells.Count * 20);
            var tris = new List<int>(cells.Count * 30);
            float halfW = Map.width / 2f, halfH = Map.height / 2f;
            foreach (var cell in cells)
            {
                if (!Map.InBounds(cell.x, cell.y)) continue;
                float x0 = cell.x - halfW - 混沌盖外扩, x1 = cell.x + 1f - halfW + 混沌盖外扩;
                float z0 = cell.y - halfH - 混沌盖外扩, z1 = cell.y + 1f - halfH + 混沌盖外扩;
                float y0 = 0f, y1 = 混沌盖高;
                // 顶面（法线 +Y 朝上）
                AddFogQuad(verts, uvs, tris,
                    new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1));
                // 四侧面（法线朝外；a=左下 b=右下 c=右上 d=左上，绕向 a→c→b / a→d→c）
                AddFogQuad(verts, uvs, tris,
                    new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0)); // -Z
                AddFogQuad(verts, uvs, tris,
                    new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x1, y1, z1)); // +Z
                AddFogQuad(verts, uvs, tris,
                    new Vector3(x0, y0, z1), new Vector3(x0, y0, z0), new Vector3(x0, y1, z0), new Vector3(x0, y1, z1)); // -X
                AddFogQuad(verts, uvs, tris,
                    new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0)); // +X
            }
            if (verts.Count == 0)
            {
                SetFogOverlayActive(false);
                return;
            }

            var mat = EnsureChaosFogMaterial();
            if (mat == null)
            {
                SetFogOverlayActive(false);
                return;
            }
            _fogOverlayGo.GetComponent<MeshRenderer>().sharedMaterial = mat;

            SetFogOverlayActive(true);
            _fogOverlayMesh.Clear();
            _fogOverlayMesh.SetVertices(verts);
            _fogOverlayMesh.SetUVs(0, uvs);
            _fogOverlayMesh.SetTriangles(tris, 0);
            _fogOverlayMesh.RecalculateBounds();
        }

        /// <summary>混沌盖单面追加（a=左下 b=右下 c=右上 d=左上；绕向 a→c→b / a→d→c=法线朝观察者
        /// 正对面的外侧——Unity 法线=Cross(B−A,C−A)，与水面/圆盘先例同约定；UV 0..1 全幅）</summary>
        private static void AddFogQuad(List<Vector3> verts, List<Vector2> uvs, List<int> tris,
            Vector3 a, Vector3 b, Vector3 c, Vector3 d)
        {
            int i0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c); verts.Add(d);
            uvs.Add(new Vector2(0f, 0f)); uvs.Add(new Vector2(1f, 0f));
            uvs.Add(new Vector2(1f, 1f)); uvs.Add(new Vector2(0f, 1f));
            tris.AddRange(new[] { i0, i0 + 2, i0 + 1, i0, i0 + 3, i0 + 2 });
        }

        /// <summary>混沌盖材质（不透明 Unlit+贴图）：Inspector 指定资产优先；否则 Resources/UI/Battle/
        /// ChaosFog 贴图运行时自建（静态缓存贴图、材质随板释放）；贴图也缺=深暗纯色兜底（Warning）</summary>
        private Material EnsureChaosFogMaterial()
        {
            if (混沌盖材质 != null) return 混沌盖材质;
            if (_fogOverlayMaterial == null)
            {
                if (_chaosFogTexture == null) _chaosFogTexture = Resources.Load<Texture2D>("UI/Battle/ChaosFog");
                if (_chaosFogTexture == null)
                    GIC.Framework.GICLog.Warn("[BattleBoard] 混沌贴图缺失（Resources/UI/Battle/ChaosFog）——混沌盖用深暗纯色兜底");
                _fogOverlayMaterial = BattleViewFactory.CreateUnlitTexturedMaterial(_chaosFogTexture,
                    new Color(0.85f, 0.85f, 0.95f, 1f)); // tint 偏暗防贴图过亮；纯色兜底=暗紫黑
            }
            return _fogOverlayMaterial;
        }

        /// <summary>
        /// 薄冰面板重建（全部 ThinIce 格合并为单 Mesh——与水面焊接面同构：同波场同相位顶点起伏
        /// =冰面恒贴下方波浪，格界零缝）。材质=运行时克隆水面材质改 tint/流速/高光（Inspector 四参
        /// 可调）；无薄冰格=面板移除。
        /// </summary>
        private void RebuildThinIceSurface()
        {
            if (_thinIceSurface != null)
            {
                Destroy(_thinIceSurface);
                _thinIceSurface = null;
            }
            if (_thinIceMesh != null)
            {
                _runtimeMeshes.Remove(_thinIceMesh);
                Destroy(_thinIceMesh);
                _thinIceMesh = null;
            }
            if (Map == null) return;

            var cells = new List<BattleCell>();
            for (int y = 0; y < Map.height; y++)
            for (int x = 0; x < Map.width; x++)
                if (TerrainSourceTile(x, y) == TileType.ThinIce) // 维度感知（2026-10-25 拍板：地脉=初始快照）
                    cells.Add(new BattleCell(x, y));
            if (cells.Count == 0) return;

            var waterMat = GetWaterSurfaceMaterial();
            if (waterMat == null)
            {
                GIC.Framework.GICLog.Warn("[BattleBoard] 薄冰面板缺少水面材质源（水地块 prefab 材质槽 1）——本次跳过");
                return;
            }
            if (_thinIceMaterial == null)
            {
                _thinIceMaterial = new Material(waterMat); // 运行时实例（OnDestroy 释放；波场参数与水面同源）
                if (_thinIceMaterial.HasProperty("_BaseColor")) _thinIceMaterial.SetColor("_BaseColor", 薄冰色);
                if (_thinIceMaterial.HasProperty("_DeepColor")) _thinIceMaterial.SetColor("_DeepColor", 薄冰深色);
                if (_thinIceMaterial.HasProperty("_FlowSpeed")) _thinIceMaterial.SetFloat("_FlowSpeed", 薄冰流动速度);
                if (_thinIceMaterial.HasProperty("_Highlight")) _thinIceMaterial.SetFloat("_Highlight", 薄冰高光);
            }
            else
            {
                // 实例已建（Inspector 参数热改后重建面板沿用新值——一次性 tint 需重写）
                if (_thinIceMaterial.HasProperty("_BaseColor")) _thinIceMaterial.SetColor("_BaseColor", 薄冰色);
                if (_thinIceMaterial.HasProperty("_DeepColor")) _thinIceMaterial.SetColor("_DeepColor", 薄冰深色);
                if (_thinIceMaterial.HasProperty("_FlowSpeed")) _thinIceMaterial.SetFloat("_FlowSpeed", 薄冰流动速度);
                if (_thinIceMaterial.HasProperty("_Highlight")) _thinIceMaterial.SetFloat("_Highlight", 薄冰高光);
            }

            _thinIceMesh = BuildSurfaceMesh(cells, "BattleThinIceSurface", _waterSurfaceHeight + 薄冰抬升);
            _runtimeMeshes.Add(_thinIceMesh);
            _thinIceSurface = new GameObject("ThinIceSurface");
            _thinIceSurface.transform.SetParent(_tilesRoot, false);
            var meshFilter = _thinIceSurface.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = _thinIceMesh;
            var meshRenderer = _thinIceSurface.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = _thinIceMaterial;
            // 同水面（迷雾批起 -3）：薄冰面板恒为最低透明层（其下水面同序-3=同序按距离，冰面恒在水面
            // 上方由高度差+半透明混合保证；雾面 -2 在其上、瞄准贴片 0/立牌 10/箭矢 12 全在其上）
            meshRenderer.sortingOrder = BattleMetrics.WaterSurfaceSortingOrder;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>焊接面 mesh 构建（水面/薄冰面板共用——相邻格共享角点焊接、细分加密波浪平滑；
        /// height=面片基准高度〔水面=_waterSurfaceHeight、薄冰=+抬升——波场位移由 shader 同公式
        /// 施加，与基准高度无关〕）</summary>
        private Mesh BuildSurfaceMesh(List<BattleCell> cells, string meshName, float height)
        {
            int subdiv = Mathf.Max(1, _waterSurfaceSubdiv);
            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            var cornerIndex = new Dictionary<long, int>();

            foreach (var cell in cells)
            {
                int baseSubX = cell.x * subdiv;
                int baseSubY = cell.y * subdiv;
                for (int j = 0; j < subdiv; j++)
                {
                    for (int i = 0; i < subdiv; i++)
                    {
                        int a = GetWaterCorner(cornerIndex, verts, uvs, Map, baseSubX + i, baseSubY + j, subdiv, height);
                        int b = GetWaterCorner(cornerIndex, verts, uvs, Map, baseSubX + i + 1, baseSubY + j, subdiv, height);
                        int c = GetWaterCorner(cornerIndex, verts, uvs, Map, baseSubX + i + 1, baseSubY + j + 1, subdiv, height);
                        int d = GetWaterCorner(cornerIndex, verts, uvs, Map, baseSubX + i, baseSubY + j + 1, subdiv, height);
                        // 绕向与旧格顶面一致（法线朝上）
                        tris.AddRange(new[] { d, c, b, d, b, a });
                    }
                }
            }

            var mesh = new Mesh { name = meshName };
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// 构建整片焊接水面（所有水/沼格合并为单 Mesh，相邻格共享角点焊接，按细分格加密顶点）。
        /// 每格自带半透明顶面的旧方案在格界必有缝：相邻面片 Alpha 双重混合 + 贴图上下缘不无缝
        /// （docs/14 §89）；合并后流动图案按世界坐标连续，格界零缝。顶点细分=波浪位移采样平滑
        /// （每格 4 顶点时波面呈格状折痕）；水格方块墙已退役（波谷下探时墙顶穿出水面=动态缝）。
        /// 水面材质由水地块 prefab 材质槽 1 携带（其顶面 submesh 已退役为空，槽位保留专作水面材质载体）。
        /// </summary>
        private void BuildWaterSurface(BattleMapData map)
        {
            var cells = new List<BattleCell>();
            for (int y = 0; y < map.height; y++)
            {
                for (int x = 0; x < map.width; x++)
                {
                    var tileType = map.GetTile(x, y);
                    if (tileType == TileType.Water || tileType == TileType.Swamp)
                        cells.Add(new BattleCell(x, y));
                }
            }
            if (cells.Count == 0 || _waterTilePrefab == null) return;

            var surfaceMesh = BuildSurfaceMesh(cells, "BattleWaterSurface", _waterSurfaceHeight);
            _runtimeMeshes.Add(surfaceMesh); // 运行时 mesh 登记（Build 重建/OnDestroy 显式销毁，§63①）

            var surfaceGo = new GameObject("WaterSurface");
            surfaceGo.transform.SetParent(_tilesRoot, false);
            var meshFilter = surfaceGo.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = surfaceMesh;
            var meshRenderer = surfaceGo.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = GetWaterSurfaceMaterial();
            // 透明排序实证（2026-10-06 像素回读定裁）：sortingOrder 支配 renderQueue——水面若留
            // order 0 会整片画在底座圆盘（-1）之上=「圆盘沉到水面下」报障根因（旧认知「盘 queue3000
            // > 水 2999 故盘在水面之上」被推翻）。2026-10-17 迷雾批自 -2 下移至 -3（BattleMetrics 常量）：
            // 迷雾面（-2）恒罩水面/冰面（未探索水格被雾压暗）、又在底座盘（-1，仅出现在已探索格）之下。勿"修"回 0
            meshRenderer.sortingOrder = BattleMetrics.WaterSurfaceSortingOrder;
            meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
            meshRenderer.receiveShadows = false;
        }

        /// <summary>
        /// 水面细分角点（焊接：同角点复用同顶点，波浪位移天然连续）。
        /// subX/subY=细分网格坐标（格坐标×细分）；UV=角点世界坐标（贴图 Repeat 每格重复一次，
        /// 与旧每格 0..1 同密度，图案跨格连续）；height=面片基准高度（水面/薄冰抬升共用）
        /// </summary>
        private int GetWaterCorner(Dictionary<long, int> cornerIndex, List<Vector3> verts, List<Vector2> uvs,
            BattleMapData map, int subX, int subY, int subdiv, float height)
        {
            long key = ((long)subX << 32) | (uint)subY;
            if (cornerIndex.TryGetValue(key, out int index)) return index;
            var pos = new Vector3((float)subX / subdiv - map.width / 2f, height, (float)subY / subdiv - map.height / 2f);
            cornerIndex[key] = verts.Count;
            verts.Add(pos);
            uvs.Add(new Vector2(pos.x, pos.z));
            return verts.Count - 1;
        }

        /// <summary>水面材质：水地块 prefab 材质槽 1（BattleWaterFlow）</summary>
        private Material GetWaterSurfaceMaterial()
        {
            if (_waterTilePrefab == null) return null;
            var waterRenderer = _waterTilePrefab.GetComponent<MeshRenderer>();
            if (waterRenderer == null) return null;
            var materials = waterRenderer.sharedMaterials;
            return materials.Length > 1 ? materials[1] : null;
        }

        private float? _waterWaveMaxHeight;

        /// <summary>水面波浪顶点最大抬升（BattleWaterFlow 波场 sin+0.5·sin 峰值 1.5 × 材质 _WaveAmplitude）。
        /// 改 shader 波场公式时峰值系数须同步；材质无该属性时按现配 0.02 兜底 0.03</summary>
        public float GetWaterWaveMaxHeight()
        {
            if (!_waterWaveMaxHeight.HasValue)
            {
                var mat = GetWaterSurfaceMaterial();
                _waterWaveMaxHeight = mat != null && mat.HasProperty("_WaveAmplitude")
                    ? 1.5f * mat.GetFloat("_WaveAmplitude")
                    : 0.03f;
            }
            return _waterWaveMaxHeight.Value;
        }

        /// <summary>表面贴片高度（瞄准高亮/选中标记等贴在表面上的小面片单出口）：
        /// 视觉表面 + 抬升量（z-fighting 静态余量，调用方定）。水/沼格视觉表面已含波峰带
        /// （波浪水面高至 表面+波峰带，贴片抬升不够会被波峰盖过「贴片到水面下」，2026-09-26 报障）</summary>
        public float GetDecalHeight(BattleCell cell, float lift)
        {
            return GetVisualSurfaceHeight(cell) + lift;
        }

        /// <summary>视觉表面高度（世界层视觉件站位单出口）：水/沼格=名义表面+波峰带+贴面余量——
        /// 单位立牌/底座圆盘/投射物路径/贴片等贴水视觉件抬过波峰带，防波浪穿模（2026-09-26 拍板
        /// 「底座圆盘也改」）；薄冰格同口径（冰面同波场起伏=冰上件须同过波峰带，决策七十六）；
        /// 拾取（GetSurfaceHeight）与 Host 判定仍用名义表面，波峰带纯视觉</summary>
        public float GetVisualSurfaceHeight(BattleCell cell)
        {
            float wave = 0f;
            if (Map != null && Map.HasTile(cell.x, cell.y))
            {
                var tile = Map.GetTile(cell.x, cell.y);
                if (tile == TileType.Water || tile == TileType.Swamp || tile == TileType.ThinIce)
                    wave = GetWaterWaveMaxHeight() + 0.01f; // 贴面余量：极端双峰同相波峰不贴片
            }
            return GetSurfaceHeight(cell) + wave;
        }

        private GameObject GetTilePrefab(TileType tileType)
        {
            switch (tileType)
            {
                case TileType.Grassland:
                case TileType.Plain:
                case TileType.Sand:
                case TileType.Snow:
                case TileType.Ice:
                    return _grassTilePrefab;
                case TileType.Water:
                case TileType.Swamp:
                case TileType.ThinIce: // 薄冰=水面体+顶面冰面板（决策七十六；初始地图不使用，防御性支持重建）
                    return _waterTilePrefab;
                case TileType.StonePath:
                    return _stoneTilePrefab;
                case TileType.Dirt:
                    return _dirtTilePrefab;
                case TileType.Cobblestone:
                    return _cobbleTilePrefab;
                default:
                    return _grassTilePrefab;
            }
        }

        /// <summary>
        /// 顶面材质变体（2026-09-27 地形批次）：同型地块每格确定性随机选一变体，防整片重复。
        /// 确定性哈希（非 UnityEngine.Random）=重建稳定，编辑器建盘断言与运行时同结果。
        /// </summary>
        private void ApplyTopVariant(GameObject tile, TileType tileType, int x, int y)
        {
            var variants = _tileVisuals != null ? _tileVisuals.GetTopVariants(tileType) : null;
            if (variants == null || variants.Length == 0) return;
            var renderer = tile.GetComponent<MeshRenderer>();
            if (renderer == null) return;
            var mats = renderer.sharedMaterials;
            if (mats.Length < 2) return; // 顶面=材质槽 1
            mats[1] = variants[CellVariantIndex(x, y, variants.Length)];
            renderer.sharedMaterials = mats;
        }

        /// <summary>格坐标确定性哈希 → [0, count) 变体序号（internal：房间地图预览复用同哈希——
        /// 预览逐格变体与战场建盘完全一致，所见即所得；确定性=重建稳定）</summary>
        internal static int CellVariantIndex(int x, int y, int count)
        {
            unchecked
            {
                uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ 0x9E3779B9u;
                return (int)(h % (uint)count);
            }
        }

        /// <summary>
        /// 构建草簇装饰层（2026-09-27 地形批次：立牌式草簇随机铺满草地格）。
        /// BattlePlayer 建盘后传战场相机（编辑器侧建盘断言无相机时传 null，朝向兜底正北）。
        /// </summary>
        public void BuildGrassDecor(Camera cam)
        {
            if (_grassDecor != null) { Destroy(_grassDecor.gameObject); _grassDecor = null; }
            _grassDecor = BattleGrassDecor.Build(_tilesRoot, Map, this, _tileVisuals, cam);
        }

        /// <summary>草簇探索态过滤重建（迷雾批 2026-10-17）：includeCell=false 的格不生成草簇——
        /// 未探索草地格的草尖会穿出混沌盖泄露地形。固定种子=已探索格草簇布局跨重建稳定
        /// （rng 按全图迭代恒消耗，include 只裁产出不裁消耗——见 BattleGrassDecor.Build）。
        /// #1 审查批 mesh 复用：已有层=原地 Clear+回填（迷雾 ApplyFogChanges 高频调用，原
        /// Destroy/new 整层=移动期 GC/卡顿源）；无层（全过滤期首次揭格）=Build 建层</summary>
        public void RebuildGrassDecor(System.Func<int, int, bool> includeCell, Camera cam)
        {
            if (_grassDecor != null)
            {
                _grassDecor.Rebuild(Map, this, _tileVisuals, cam, includeCell);
                return;
            }
            _grassDecor = BattleGrassDecor.Build(_tilesRoot, Map, this, _tileVisuals, cam, includeCell);
        }

        /// <summary>
        /// 格中心的世界坐标（地块底面基准 Y=0；视觉表面高度——水格含波峰带，§89 第五轮）
        /// </summary>
        public Vector3 CellToWorld(BattleCell cell)
        {
            float x = cell.x + 0.5f - Map.width / 2f;
            float z = cell.y + 0.5f - Map.height / 2f;
            float y = GetVisualSurfaceHeight(cell);
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 连续格心坐标 → 世界坐标（CellToWorld 的连续版；B5 连续判定——投射物命中点/消散点由 Host
        /// 千分定点下发，格 c 的心=c+0.5，与 CellToWorld 同系数）。视觉表面高度取归属格（floor），
        /// 水格含波峰带（§89 第五轮）
        /// </summary>
        public Vector3 ContinuousCellToWorld(float gx, float gy)
        {
            float x = gx - Map.width / 2f;
            float z = gy - Map.height / 2f;
            float y = GetVisualSurfaceHeight(new BattleCell(Mathf.FloorToInt(gx), Mathf.FloorToInt(gy)));
            return new Vector3(x, y, z);
        }

        /// <summary>
        /// 该格表面高度（单位站立面）
        /// </summary>
        public float GetSurfaceHeight(BattleCell cell)
        {
            if (Map != null && Map.HasTile(cell.x, cell.y))
            {
                var tile = Map.GetTile(cell.x, cell.y);
                if (tile == TileType.Water || tile == TileType.Swamp)
                    return _waterSurfaceHeight;
            }
            return _tileTopHeight;
        }

        /// <summary>
        /// 世界坐标反算格坐标（CellToWorld 的逆运算；HUD 点击拾取用，B6）
        /// </summary>
        public BattleCell WorldToCell(Vector3 worldPos)
        {
            float halfW = Map != null ? Map.width / 2f : 0f;
            float halfH = Map != null ? Map.height / 2f : 0f;
            int x = Mathf.FloorToInt(worldPos.x + halfW);
            int y = Mathf.FloorToInt(worldPos.z + halfH);
            return new BattleCell(x, y);
        }

        /// <summary>
        /// 地块预制体摆放位置（预制体轴心在底面中心）
        /// </summary>
        private Vector3 TileBottomPosition(int x, int y)
        {
            return new Vector3(x + 0.5f - Map.width / 2f, 0f, y + 0.5f - Map.height / 2f);
        }
    }
}
