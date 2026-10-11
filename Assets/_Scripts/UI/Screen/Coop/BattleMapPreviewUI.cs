// BattleMapPreviewUI.cs - 房间地图预览（2026-10-11 联机批，参考红色警戒2 房间界面）：
// 程序化地形小图（BattleMapConfig.rows 字符布局 → Texture2D 平涂）+ 出生点标记层
// （编号圆点；被玩家选走的出生点染玩家色=「房间选的出生位置」可视化）。
// 挂房间面板右上角；数据源=当前权威地图（RoomManager.RoomMapConfigName）+ 房间名册。
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using GIC.Data;
namespace GIC.UI
{


    public class BattleMapPreviewUI : MonoBehaviour
    {
        [Header("地图预览")]
        [Tooltip("地形渲染 RawImage（程序化纹理填充；尺寸=地图格数×每格像素）")]
        [SerializeField] private RawImage 地形图;

        [Tooltip("出生点标记容器（标记程序化生成，挂本层）")]
        [SerializeField] private RectTransform 标记层;

        [Header("标记样式")]
        [Tooltip("空闲出生点圆点色（暗金）")]
        [SerializeField] private Color 空闲出生点色 = new Color(0.85f, 0.70f, 0.35f, 0.9f);
        [Tooltip("被占用出生点描边色（暗底圈）")]
        [SerializeField] private Color 占用描边色 = new Color(0f, 0f, 0f, 0.65f);

        // 地形色板（地图字符语义固定色，代码常量；RA2 式平涂）
        private static readonly Color 草地色 = new Color32(0x5E, 0x8C, 0x3A, 0xFF);
        private static readonly Color 平原色 = new Color32(0x8A, 0xA5, 0x4A, 0xFF);
        private static readonly Color 水面色 = new Color32(0x2E, 0x5F, 0x8A, 0xFF);
        private static readonly Color 石路色 = new Color32(0x7A, 0x7A, 0x74, 0xFF);
        private static readonly Color 土地色 = new Color32(0x7D, 0x5A, 0x3C, 0xFF);
        private static readonly Color 圆石色 = new Color32(0x9A, 0x96, 0x8C, 0xFF);
        private static readonly Color 虚空色 = new Color32(0x14, 0x14, 0x18, 0xFF);

        // 纹理缓存（地图名→Texture2D；房间反复开关零重建）
        private static readonly Dictionary<string, Texture2D> _textureCache = new Dictionary<string, Texture2D>();

        // 出生点标记（序号→UI 根；标记层子物体，随地图重建）
        private readonly List<RectTransform> _markers = new List<RectTransform>();
        private readonly List<Image> _markerImages = new List<Image>();
        private readonly List<TMP_Text> _markerLabels = new List<TMP_Text>();

        private BattleMapConfig _current;

        /// <summary>
        /// 刷新预览（单入口）：方形适配（地形图正方纹理防拉伸）→ 重建地形纹理+标记 → 按名册染色。
        /// players=null/空=只显示地形与空闲出生点。
        /// </summary>
        public void Refresh(BattleMapConfig config, IEnumerable<PlayerInfo> players)
        {
            if (config == null) return;
            _current = config;
            FitSquareAspect();
            RebuildTerrain(config);
            RebuildMarkers(config);
            ApplyPlayerMarks(config, players);
        }

        /// <summary>
        /// 内衬方形适配：RawImage 无 preserveAspect（Image 专属）——正方地形纹理由组件自算居中方形，
        /// 地形图与标记层同 rect（标记归一化坐标=纹理格坐标，两层必须逐像素对齐）
        /// </summary>
        private void FitSquareAspect()
        {
            if (地形图 == null || 标记层 == null) return;
            var parent = 标记层.parent as RectTransform;
            if (parent == null || parent.rect.width < 1f || parent.rect.height < 1f) return;
            float side = Mathf.Min(parent.rect.width, parent.rect.height);

            var imgRt = (RectTransform)地形图.transform;
            imgRt.anchorMin = imgRt.anchorMax = new Vector2(0.5f, 0.5f);
            imgRt.anchoredPosition = Vector2.zero;
            imgRt.sizeDelta = new Vector2(side, side);

            标记层.anchorMin = 标记层.anchorMax = new Vector2(0.5f, 0.5f);
            标记层.anchoredPosition = Vector2.zero;
            标记层.sizeDelta = new Vector2(side, side);
        }

        /// <summary>仅刷新名册占位（地图没变时省纹理重建——名册事件高频触发）</summary>
        public void RefreshPlayersOnly(IEnumerable<PlayerInfo> players)
        {
            if (_current == null) return;
            ApplyPlayerMarks(_current, players);
        }

        // ==================== 地形纹理 ====================

        private void RebuildTerrain(BattleMapConfig config)
        {
            if (地形图 == null) return;
            // 纹理走缓存（按地图名，房间反复开关零重建；缓存纹理不随本组件销毁）
            地形图.texture = GetOrCreateTexture(config);
        }

        private static Texture2D GetOrCreateTexture(BattleMapConfig config)
        {
            string key = config.name;
            if (_textureCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            int w = Mathf.Max(1, config.width);
            int h = Mathf.Max(1, config.height);
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                string row = (config.rows != null && y < config.rows.Count) ? config.rows[y] : "";
                for (int x = 0; x < w; x++)
                {
                    char c = x < row.Length ? row[x] : '.';
                    pixels[y * w + x] = TileColor(c);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, false);

            _textureCache[key] = tex;
            return tex;
        }

        private static Color32 TileColor(char c) => c switch
        {
            'G' => 草地色,
            'P' => 平原色,
            'W' => 水面色,
            'S' => 石路色,
            'D' => 土地色,
            'C' => 圆石色,
            _ => 虚空色,
        };

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

            int count = config.spawnZones != null ? config.spawnZones.Count : 0;
            var layerSize = 标记层.rect.size;
            if (layerSize.x < 1f || layerSize.y < 1f)
                layerSize = new Vector2(240f, 240f); // 布局未就绪兜底（面板预热渲染后正常不触发）
            for (int i = 0; i < count; i++)
            {
                var zone = config.spawnZones[i];
                var marker = BuildMarker(i + 1, zone.center, config.width, config.height, layerSize);
                _markers.Add(marker);
                _markerImages.Add(marker.GetComponent<Image>());
                _markerLabels.Add(marker.GetComponentInChildren<TMP_Text>());
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
    }
}
