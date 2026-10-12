// BattleMapPreviewUI.cs - 房间地图预览（真实地形贴图版，2026-10-13 预览升级批）
// 顶视 2D 小图：每格=战斗同源真实顶面贴图（TileVisualsConfig 材质变体 + BattleBoard.CellVariantIndex
// 同款确定性哈希 → 逐格变体与战场建盘完全一致，所见即所得）；水面格=GIC/Battle/WaterFlow shader
// 运行时实例（波峰高光关闭——UI 顶点无波相位意义，双层 UV 流动保留=预览水面持续流动）。
// 入场动画：从地图中心波纹式扩散，每格 easeOutBack 从小放大到正常（过冲回落），出生点标记随波前弹出；
// 材质/贴图缺失兜底=RA2 式平涂色块零崩溃。
// 挂房间面板右上角；数据源=当前权威地图（RoomManager.RoomMapConfigName）+ 房间名册。
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GIC.Data;
using GIC.Battle;
namespace GIC.UI
{


    public class BattleMapPreviewUI : MonoBehaviour
    {
        [Header("地图预览")]
        [Tooltip("预览底板 RawImage（方形暗底，框出地图区域；地形格运行时建于其上层）")]
        [SerializeField] private RawImage 地形图;

        [Tooltip("出生点标记容器（标记程序化生成，挂本层）")]
        [SerializeField] private RectTransform 标记层;

        [Header("入场波纹动画")]
        [Tooltip("波纹每单位距离的延迟（秒）：格距图心越远越晚长出；0=整图同时长出")]
        [SerializeField] private float 波纹每格延迟秒 = 0.045f;
        [Tooltip("每格从小放大到正常的时长（秒）")]
        [SerializeField] private float 单格生长时长秒 = 0.28f;
        [Tooltip("波纹起始前的整体延迟（秒）；给面板入场动画留出前奏")]
        [SerializeField] private float 波纹起始延迟秒 = 0.15f;
        [Tooltip("出生点标记相对波前到达的额外延迟（秒）")]
        [SerializeField] private float 标记弹出延迟秒 = 0.14f;

        [Header("标记样式")]
        [Tooltip("空闲出生点圆点色（暗金）")]
        [SerializeField] private Color 空闲出生点色 = new Color(0.85f, 0.70f, 0.35f, 0.9f);
        [Tooltip("被占用出生点描边色（暗底圈）")]
        [SerializeField] private Color 占用描边色 = new Color(0f, 0f, 0f, 0.65f);

        // 平涂兜底色板（材质/贴图不可达时的 RA2 式色块；语义色与战斗地形字符约定一致）
        private static readonly Color32 草地色 = new Color32(0x5E, 0x8C, 0x3A, 0xFF);
        private static readonly Color32 平原色 = new Color32(0x8A, 0xA5, 0x4A, 0xFF);
        private static readonly Color32 水面色 = new Color32(0x2E, 0x5F, 0x8A, 0xFF);
        private static readonly Color32 石路色 = new Color32(0x7A, 0x7A, 0x74, 0xFF);
        private static readonly Color32 土地色 = new Color32(0x7D, 0x5A, 0x3C, 0xFF);
        private static readonly Color32 圆石色 = new Color32(0x9A, 0x96, 0x8C, 0xFF);

        // ---- 静态缓存（房间反复开关零重建；会话级生命周期，域重载自清） ----
        private static TileVisualsConfig _visualConfig;
        private static readonly Dictionary<Texture2D, Sprite> _spriteCache = new Dictionary<Texture2D, Sprite>();
        private static Material _waterMaterial;   // WaterFlow 运行时实例（_Highlight=0）
        private static Texture2D _backingTexture; // 底板 2×2 暗色

        // ---- 运行时状态 ----
        private RectTransform _tilesLayer;                       // 地形格层（运行时建，底板与标记层之间）
        private BattleMapData _map;                               // 当前地图运行时数据（BuildData 缓存）
        private float _side;                                      // 当前方形边长（FitSquareAspect 产出）
        private Vector2 _regionMin, _regionMax;                   // 预览区锚点（Awake 一次性捕获序列化原值）
        private bool _waveOnEnable;                               // OnEnable 后下一次 Refresh 重播波纹（重进房间）
        private Coroutine _waveRoutine;

        // 波纹注册表（地块与标记分列：名册刷新只改色、不动缩放）
        private readonly List<RectTransform> _tileItems = new List<RectTransform>();
        private readonly List<float> _tileDelays = new List<float>();
        private readonly List<RectTransform> _markerItems = new List<RectTransform>();
        private readonly List<float> _markerDelays = new List<float>();

        // 出生点标记（序号→UI 根；标记层子物体，随地图重建）
        private readonly List<RectTransform> _markers = new List<RectTransform>();
        private readonly List<Image> _markerImages = new List<Image>();
        private readonly List<TMP_Text> _markerLabels = new List<TMP_Text>();

        private BattleMapConfig _current;

        private static TileVisualsConfig VisualConfig
        {
            get
            {
                if (_visualConfig == null)
                    _visualConfig = Resources.Load<TileVisualsConfig>("Configs/TileVisualsConfig");
                return _visualConfig;
            }
        }

        private void Awake()
        {
            // 预览区锚点一次性捕获（序列化原值；FitSquareAspect 会改写标记层锚点为居中方形，事后不可再取）
            if (标记层 != null)
            {
                _regionMin = 标记层.anchorMin;
                _regionMax = 标记层.anchorMax;
            }
        }

        private void OnEnable() => _waveOnEnable = true; // 重进房间：下一次 Refresh 重播入场波纹

        /// <summary>
        /// 刷新预览（单入口）：方形适配 → 地图变化时重建地形格+标记 → 入场波纹（地图变化或重进房间时）→
        /// 名册染色。同图刷新（名册增删）零重建零动画，只改标记色。
        /// players=null/空=只显示地形与空闲出生点。
        /// </summary>
        public void Refresh(BattleMapConfig config, IEnumerable<PlayerInfo> players)
        {
            if (config == null) return;
            bool mapChanged = _current == null || _current.name != config.name;
            _current = config;
            bool playWave = mapChanged || _waveOnEnable;
            _waveOnEnable = false;

            FitSquareAspect();
            if (地形图 != null && 地形图.texture == null) 地形图.texture = BackingTexture();

            if (mapChanged)
            {
                _map = config.BuildData();
                RebuildMarkers(config);
                RebuildTiles();
            }
            if (playWave) StartWave();
            ApplyPlayerMarks(config, players);
        }

        /// <summary>仅刷新名册占位（地图没变时省重建——名册事件高频触发）</summary>
        public void RefreshPlayersOnly(IEnumerable<PlayerInfo> players)
        {
            if (_current == null) return;
            ApplyPlayerMarks(_current, players);
        }

        /// <summary>
        /// 方形适配：在序列化预览区（底板 RawImage 的原锚点区域）内居中放正方地形场——
        /// 地形格层与标记层同 rect（标记归一化坐标=纹理格坐标，两层必须逐像素对齐）。
        /// 旧实现取整面板短边会让方形顶进标题条；现按预览区可用高宽取边。
        /// </summary>
        private void FitSquareAspect()
        {
            if (标记层 == null) return;
            var parent = 标记层.parent as RectTransform;
            if (parent == null || parent.rect.width < 1f || parent.rect.height < 1f) return;

            float availW = (_regionMax.x - _regionMin.x) * parent.rect.width;
            float availH = (_regionMax.y - _regionMin.y) * parent.rect.height;
            float side = Mathf.Min(availW, availH);
            if (side < 1f) return;
            _side = side;

            // 垂直方向对齐预览区中心（锚点(0.5,0.5)基准 → 区域中心偏移）
            float regionCy = (_regionMin.y + _regionMax.y) * 0.5f;
            var pos = new Vector2(0f, (regionCy - 0.5f) * parent.rect.height);

            foreach (var rt in new[] { 标记层, EnsureTilesLayer() })
            {
                if (rt == null) continue;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = pos;
                rt.sizeDelta = new Vector2(side, side);
            }
        }

        // ==================== 地形格层（真实贴图） ====================

        /// <summary>地形格层懒建（底板与标记层之间；创建一次，重建只清子物体）</summary>
        private RectTransform EnsureTilesLayer()
        {
            if (_tilesLayer != null) return _tilesLayer;
            var go = new GameObject("TilesLayer", typeof(RectTransform));
            _tilesLayer = (RectTransform)go.transform;
            _tilesLayer.SetParent(标记层 != null ? 标记层.parent : transform, false);
            if (地形图 != null)
                _tilesLayer.SetSiblingIndex(((RectTransform)地形图.transform).GetSiblingIndex() + 1);
            return _tilesLayer;
        }

        private void RebuildTiles()
        {
            var layer = EnsureTilesLayer();
            for (int i = layer.childCount - 1; i >= 0; i--) Destroy(layer.GetChild(i).gameObject);
            _tileItems.Clear();
            _tileDelays.Clear();
            if (_map == null || _side < 1f) return;

            float cellW = _side / Mathf.Max(1, _map.width);
            float cellH = _side / Mathf.Max(1, _map.height);
            float cx = (_map.width - 1) * 0.5f;
            float cy = (_map.height - 1) * 0.5f;

            for (int y = 0; y < _map.height; y++)
            {
                for (int x = 0; x < _map.width; x++)
                {
                    if (!_map.HasTile(x, y)) continue; // 虚空格：无地形无格子（底板透出）
                    var type = _map.GetTile(x, y);
                    bool water = type == TileType.Water || type == TileType.Swamp;

                    var go = new GameObject(water ? $"Water_{x}_{y}" : $"Tile_{x}_{y}", typeof(RectTransform));
                    var rt = (RectTransform)go.transform;
                    rt.SetParent(layer, false);
                    rt.anchorMin = rt.anchorMax = new Vector2((x + 0.5f) / _map.width, (y + 0.5f) / _map.height);
                    rt.anchoredPosition = Vector2.zero;
                    rt.sizeDelta = new Vector2(cellW, cellH);

                    if (water)
                    {
                        var mat = GetWaterMaterial();
                        if (mat != null)
                        {
                            var raw = go.AddComponent<RawImage>();
                            raw.texture = mat.mainTexture;
                            raw.material = mat; // 双层 UV 随 _Time 流动=预览水面持续流动
                            raw.raycastTarget = false;
                        }
                        else
                        {
                            AddFallbackCell(go, 水面色); // 材质不可达兜底：平涂色块
                        }
                    }
                    else
                    {
                        var sprite = GetTopSprite(type, x, y);
                        if (sprite != null)
                        {
                            var img = go.AddComponent<Image>();
                            img.sprite = sprite;
                            img.raycastTarget = false;
                        }
                        else
                        {
                            AddFallbackCell(go, TileColor(type)); // 变体/贴图不可达兜底：平涂色块
                        }
                    }

                    rt.localScale = Vector3.zero; // 入场波纹接管初始态
                    _tileItems.Add(rt);
                    _tileDelays.Add(Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) * 波纹每格延迟秒
                                    + WaveJitter(x, y));
                }
            }
        }

        private static void AddFallbackCell(GameObject go, Color32 color)
        {
            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
        }

        /// <summary>地形格顶面贴图（战斗同源）：TileVisualsConfig 变体材质 + BattleBoard.CellVariantIndex
        /// 同款确定性哈希选变体——预览逐格变体与战场建盘完全一致；Sprite 按贴图缓存（12 变体级总量）</summary>
        private static Sprite GetTopSprite(TileType type, int x, int y)
        {
            var variants = VisualConfig != null ? VisualConfig.GetTopVariants(type) : null;
            if (variants == null || variants.Length == 0) return null;
            var mat = variants[BattleBoard.CellVariantIndex(x, y, variants.Length)];
            if (mat == null) return null;

            var tex = mat.HasProperty("_BaseMap") ? mat.GetTexture("_BaseMap") as Texture2D : mat.mainTexture as Texture2D;
            if (tex == null) return null;

            if (!_spriteCache.TryGetValue(tex, out var sprite))
            {
                sprite = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                _spriteCache[tex] = sprite;
            }
            return sprite;
        }

        /// <summary>水面材质运行时实例（GIC/Battle/WaterFlow）：波峰高光关闭（波相位取自顶点世界坐标，
        /// UI 顶点上=像素级大数相位→四角亮度随机斑块），双层 UV 流动保留；0.02 幅度顶点位移在 UI 上
        /// ≈0.03px 不可见。其余参数与 BattleWaterFlow 资产同值（实例拷贝，改资产后重启生效）</summary>
        private static Material GetWaterMaterial()
        {
            if (_waterMaterial != null) return _waterMaterial;
            var src = VisualConfig != null ? VisualConfig.水面材质 : null;
            if (src == null || src.shader == null) return null;
            _waterMaterial = new Material(src);
            if (_waterMaterial.HasProperty("_Highlight")) _waterMaterial.SetFloat("_Highlight", 0f);
            return _waterMaterial;
        }

        private static Texture2D BackingTexture()
        {
            if (_backingTexture != null) return _backingTexture;
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            var c = new Color32(0x0C, 0x16, 0x22, 0xE6); // 面板深蓝底微亮一档，框出地图区域
            tex.SetPixels32(new[] { c, c, c, c });
            tex.Apply(false, false);
            _backingTexture = tex;
            return tex;
        }

        private static Color32 TileColor(TileType t) => t switch
        {
            TileType.Grassland or TileType.Sand or TileType.Snow or TileType.Ice => 草地色,
            TileType.Plain => 平原色,
            TileType.Water or TileType.Swamp => 水面色,
            TileType.StonePath => 石路色,
            TileType.Dirt => 土地色,
            TileType.Cobblestone => 圆石色,
            _ => 虚空色,
        };

        private static readonly Color32 虚空色 = new Color32(0x14, 0x14, 0x18, 0xFF);

        // ==================== 出生点标记 ====================

        private void RebuildMarkers(BattleMapConfig config)
        {
            if (标记层 == null) return;

            // 清旧标记（程序化生成件，Destroy 即净）
            foreach (var m in _markers)
                if (m != null) Destroy(m.gameObject);
            _markers.Clear();
            _markerImages.Clear();
            _markerLabels.Clear();
            _markerItems.Clear();
            _markerDelays.Clear();

            int count = config.spawnZones != null ? config.spawnZones.Count : 0;
            var layerSize = 标记层.rect.size;
            if (layerSize.x < 1f || layerSize.y < 1f)
                layerSize = new Vector2(240f, 240f); // 布局未就绪兜底（面板预热渲染后正常不触发）

            float cx = (Mathf.Max(1, config.width) - 1) * 0.5f;
            float cy = (Mathf.Max(1, config.height) - 1) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                var zone = config.spawnZones[i];
                var marker = BuildMarker(i + 1, zone.center, config.width, config.height, layerSize);
                _markers.Add(marker);
                _markerImages.Add(marker.GetComponent<Image>());
                _markerLabels.Add(marker.GetComponentInChildren<TMP_Text>());

                marker.localScale = Vector3.zero; // 随波纹弹出
                _markerItems.Add(marker);
                _markerDelays.Add(Vector2.Distance(new Vector2(zone.center.x, zone.center.y), new Vector2(cx, cy))
                                  * 波纹每格延迟秒 + 标记弹出延迟秒 + WaveJitter(zone.center.x, zone.center.y));
            }
        }

        /// <summary>程序化生成一枚出生点标记：圆形底（Image）+ 编号（TMP）——归一化格坐标→标记层局部</summary>
        private RectTransform BuildMarker(int zoneIndex, BattleCell center, int mapWidth, int mapHeight, Vector2 layerSize)
        {
            var go = new GameObject($"Spawn{zoneIndex}", typeof(RectTransform), typeof(Image));
            var rt = (RectTransform)go.transform;
            rt.SetParent(标记层, false);

            // 直径=标记层短边 × ~0.09（跨图尺寸自适应；Inspector 无常量依赖）
            float diameter = Mathf.Min(layerSize.x, layerSize.y) * 0.095f;
            rt.sizeDelta = new Vector2(diameter, diameter);

            // 归一化格心（预览纹理 y 向上；Unity UI y 向上同构——v=0 在底）
            float u = (center.x + 0.5f) / Mathf.Max(1, mapWidth);
            float v = (center.y + 0.5f) / Mathf.Max(1, mapHeight);
            rt.anchorMin = rt.anchorMax = new Vector2(u, v);
            rt.anchoredPosition = Vector2.zero;

            var img = go.GetComponent<Image>();
            img.sprite = LoadCircleSprite();
            img.color = 空闲出生点色;
            img.raycastTarget = false;

            // 编号（TMP 子物体，居中）
            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(TextMeshProUGUI));
            var labelRt = (RectTransform)labelGo.transform;
            labelRt.SetParent(rt, false);
            labelRt.anchorMin = labelRt.anchorMax = Vector2.one * 0.5f;
            labelRt.anchoredPosition = Vector2.zero;
            labelRt.sizeDelta = new Vector2(diameter, diameter);
            var tmp = labelGo.GetComponent<TextMeshProUGUI>();
            tmp.text = zoneIndex.ToString();
            tmp.fontSize = Mathf.Max(10f, diameter * 0.62f);
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color32(0x1A, 0x14, 0x08, 0xFF); // 深墨编号（tag 字色同源拍板）
            tmp.raycastTarget = false;

            return rt;
        }

        private static Sprite LoadCircleSprite()
        {
            // 复用项目现有实心白圆盘素材（拖动瞄准小盘/背包 Tab 同源，Resources 可寻址）；
            // 兜底=UGUI 内置 Knob（运行时安全，勿用 UnityEditor API——Player 变体守护段纪律）
            var sprite = Resources.Load<Sprite>("UI/Backpack/TabGlyphs/disc");
            if (sprite == null) sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/Knob.psd");
            return sprite;
        }

        /// <summary>
        /// 名册占位染色：玩家指定出生点（Zone1..N）→ 对应标记染玩家色（房间选色真实可见）；
        /// Random=不占标记（开局自动分配，运行期落位不预占）。同区多选（校验门应拦）取先到者。
        /// </summary>
        private void ApplyPlayerMarks(BattleMapConfig config, IEnumerable<PlayerInfo> players)
        {
            int count = _markerImages.Count;
            var claimed = new Dictionary<int, PlayerInfo>();
            if (players != null)
            {
                foreach (var p in players)
                {
                    int zone = (int)p.SpawnPosition;
                    if (zone >= 1 && zone <= count && !claimed.ContainsKey(zone))
                        claimed[zone] = p;
                }
            }

            for (int i = 0; i < count; i++)
            {
                var img = _markerImages[i];
                if (img == null) continue;
                if (claimed.TryGetValue(i + 1, out var player))
                {
                    img.color = player.Color.ToColor();
                    if (_markerLabels[i] != null)
                        _markerLabels[i].color = new Color32(0x1A, 0x14, 0x08, 0xFF);
                }
                else
                {
                    img.color = 空闲出生点色;
                }
            }
        }

        // ==================== 入场波纹动画 ====================

        /// <summary>波纹起播（同图重进房间=缩位重置后重播；地图变化=重建件已归零）</summary>
        private void StartWave()
        {
            if (单格生长时长秒 <= 0f || !Application.isPlaying)
            {
                SnapWaveItems();
                return; // 非播放态（编辑器工具/冒烟调用，StartCoroutine 不可用）或时长归零=直接满尺寸
            }
            if (_waveRoutine != null) StopCoroutine(_waveRoutine);
            _waveRoutine = StartCoroutine(WaveRoutine());
        }

        private IEnumerator WaveRoutine()
        {
            float t = -波纹起始延迟秒;
            while (true)
            {
                t += Time.unscaledDeltaTime; // 切换动画/转场期间照常推进（不随时间缩放停摆）
                bool tilesDone = AnimateRegistry(_tileItems, _tileDelays, t);
                bool markersDone = AnimateRegistry(_markerItems, _markerDelays, t);
                if (tilesDone && markersDone) break;
                yield return null;
            }
            SnapWaveItems();
            _waveRoutine = null;
        }

        /// <summary>单注册表逐件按延迟-easeOutBack 生长；全部到 1 返回 true（波纹完成判据）</summary>
        private bool AnimateRegistry(List<RectTransform> items, List<float> delays, float t)
        {
            bool done = true;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item == null) continue; // 波纹期间地图重建销毁的旧件（防御；重建会换新表）
                float p = (t - delays[i]) / 单格生长时长秒;
                if (p <= 0f)
                {
                    item.localScale = Vector3.zero;
                    done = false;
                }
                else if (p >= 1f)
                {
                    item.localScale = Vector3.one;
                }
                else
                {
                    item.localScale = Vector3.one * EaseOutBack(p); // 从小放大过冲再回正常
                    done = false;
                }
            }
            return done;
        }

        private void SnapWaveItems()
        {
            foreach (var rt in _tileItems) if (rt != null) rt.localScale = Vector3.one;
            foreach (var rt in _markerItems) if (rt != null) rt.localScale = Vector3.one;
        }

        /// <summary>easeOutBack：0→1 先过冲（峰值≈1.10）再回落到 1——「从小变大再变成正常大小」</summary>
        private static float EaseOutBack(float p)
        {
            const float c1 = 1.70158f;
            const float c3 = c1 + 1f;
            float q = p - 1f;
            return 1f + c3 * q * q * q + c1 * q * q;
        }

        /// <summary>波纹微抖（±12ms，确定性哈希）：消环带机械感、长出更有机；确定性=重进房间可复现</summary>
        private static float WaveJitter(int x, int y)
            => (BattleBoard.CellVariantIndex(x, y, 17) - 8) * 0.0015f;
    }
}
