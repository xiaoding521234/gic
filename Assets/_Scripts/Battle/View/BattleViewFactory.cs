using UnityEngine;
using TMPro;
using GIC.Data;

namespace GIC.Battle
{
    /// <summary>
    /// 战斗世界层程序化视觉工厂（2026-09-18 统一化批次：世界 Quad 基元 + Unlit 材质 + 世界 TMP 字体的唯一出口，
    /// 收口 UnitView×3 + BattleHud×2 的手搓样板与 4 处 Shader.Find 重复，docs/14 §63）。
    /// 材质生命周期归调用方：独立材质须持有引用并在 OnDestroy Destroy（Destroy 物体不销材质，§63①）。
    /// </summary>
    public static class BattleViewFactory
    {
        private static Shader _unlitShader;

        /// <summary>URP Unlit（已核验在 GraphicsSettings→Always Included Shaders 清单内，构建不被剥离，docs/14 §63③）</summary>
        public static Shader UnlitShader
        {
            get
            {
                if (_unlitShader == null)
                {
                    _unlitShader = Shader.Find("Universal Render Pipeline/Unlit");
                    if (_unlitShader == null) _unlitShader = Shader.Find("Unlit/Color");
                }
                return _unlitShader;
            }
        }

        private static TMP_FontAsset _worldTextFont;

        /// <summary>世界空间 TMP 字体（zh-cn SDF——世界 TMP 无 UGUI 默认字体链，须显式指定防 tofu，docs/14 §62①③）</summary>
        public static TMP_FontAsset WorldTextFont =>
            _worldTextFont != null ? _worldTextFont
            : (_worldTextFont = Resources.Load<TMP_FontAsset>("Fonts & Materials/zh-cn SDF"));

        /// <summary>新建 Unlit 纯色材质（调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateUnlitMaterial(Color color)
        {
            var material = new Material(UnlitShader);
            material.color = color;
            return material;
        }

        /// <summary>新建 Unlit 贴图材质（不透明——迷雾批任务迷雾混沌盖消费：贴图可空=纯色兜底；
        /// 调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateUnlitTexturedMaterial(Texture2D texture, Color tint)
        {
            var material = new Material(UnlitShader);
            if (texture != null) material.mainTexture = texture;
            material.color = tint;
            return material;
        }

        /// <summary>新建 Unlit 半透明材质（URP Unlit 透明态全套写入——等价编辑器 Surface=Transparent 的
        /// ShaderGUI 落值；不设 _Surface 时 OutputAlpha 把 alpha 强制成 1，半透明失效。
        /// 首个消费者=瞄准高亮推荐/不推荐分色，2026-09-23。调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateTransparentUnlitMaterial(Color color)
        {
            var material = new Material(UnlitShader);
            material.color = color;
            material.SetFloat("_Surface", 1f);     // 透明面（IsSurfaceTypeTransparent→alpha 输出有效）
            material.SetFloat("_Blend", 0f);
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetFloat("_ZWrite", 0f);
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); // 透明面跳过 SSAO 洗色（UnlitForwardPass 同名分支）
            material.SetOverrideTag("RenderType", "Transparent");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            return material;
        }

        private static Shader _chromaKeyShader;

        /// <summary>立牌动画视频 ChromaKey shader（GIC/Battle/ChromaKeyVideo，B-S3 视频路线）——
        /// 已登记 GraphicsSettings Always Included Shaders 防构建剥离（同 URP Unlit 清单纪律，docs/14 §63③）；
        /// 找不到返回 null（调用方自然回落静态立牌）</summary>
        public static Shader ChromaKeyShader
        {
            get
            {
                if (_chromaKeyShader == null)
                {
                    _chromaKeyShader = Shader.Find("GIC/Battle/ChromaKeyVideo");
                    if (_chromaKeyShader == null)
                        Debug.LogWarning("[BattleViewFactory] ChromaKeyVideo shader 未找到（未导入/被剥离），立牌动画视频将回落静态立牌");
                }
                return _chromaKeyShader;
            }
        }

        /// <summary>新建立牌动画视频 ChromaKey 材质（消费 VideoPlayer→RT 的绿幕画面运行时抠色；
        /// _Color 承载尸体灰/受击闪红 tint（UnitView.RefreshTint 写入，同 SpriteRenderer.color 语义；
        /// 冻结配色 2026-10-02 起由 _FrozenAmount 霜化接管——tint 置白）。
        /// 调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateChromaKeyMaterial()
        {
            return new Material(ChromaKeyShader);
        }

        // 瞄准格底图（白芯+内嵌黑边环；运行时生成免资产文件）
        private static Texture2D _aimCellTexture;

        /// <summary>瞄准格底图（128×128，白芯+向内黑边环 12px，Clamp+Bilinear）：黑边 rgb=0 乘任意
        /// tint 恒黑——推荐/不推荐两色共享同一张；填充区 rgb=1×tint=色块本色；全图 alpha=1
        /// （透明度由材质 _BaseColor.a 承载）。2026-09-24 拍板：色块带内嵌黑边更明显。</summary>
        public static Texture2D AimCellTexture
        {
            get
            {
                if (_aimCellTexture != null) return _aimCellTexture;
                const int size = 128, border = 12;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
                {
                    name = "BattleAimCellTexture",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color32[size * size];
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        bool rim = x < border || y < border || x >= size - border || y >= size - border;
                        byte v = rim ? (byte)0 : (byte)255;
                        pixels[y * size + x] = new Color32(v, v, v, 255);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _aimCellTexture = tex;
                return tex;
            }
        }

        /// <summary>新建瞄准格材质（白芯黑边底图 × tint 纯色，透明配方复用 CreateTransparentUnlitMaterial。
        /// 调用方负责持有与 OnDestroy 释放）</summary>
        public static Material CreateAimCellMaterial(Color color)
        {
            var material = CreateTransparentUnlitMaterial(color);
            material.mainTexture = AimCellTexture;
            return material;
        }

        // ==================== 瞄准增强程序纹理（2026-10-15 瞄准预览批；与 AimCellTexture 同族——
        // 指示器类程序生成免资产文件，rgb 恒白由材质 tint 承色，alpha 承形状/渐变） ====================

        private static Texture2D _aimArrowBandTexture;

        /// <summary>方向箭头带纹理（256×256 单节 chevron 平铺，Repeat+Bilinear；设计面=1 格×1 格，
        /// tiling=(带长,1)）。2026-10-15 二轮拍板「箭头非常不明显——应当和之前的格子一样铺满整个格子，
        /// 参考格子高亮加黑色描边」：上下长边 24px（9.4%=AimCellTexture 边框同比例）纯黑描边环 +
        /// 实心车道底（rgb 195 略暗于 chevron=滚动有对比）+ 粗亮 chevron（rgb 255 全亮、笔画 σ10 加粗）
        /// ——U 向=前进向，offset 滚动 chevron 流动、V 向黑边恒定不动（U 滚动不动 V 边=单 quad 承载
        /// 「实心车道+描边+滚动箭羽」三层视觉）</summary>
        public static Texture2D AimArrowBandTexture
        {
            get
            {
                if (_aimArrowBandTexture != null) return _aimArrowBandTexture;
                const int s = 256;
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
                {
                    name = "BattleAimArrowBandTexture",
                    wrapMode = TextureWrapMode.Repeat,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color32[s * s];
                const int border = 24;                    // 长边黑描边宽（9.4%=格子高亮同比例）
                float apexX = s * 0.68f, baseX = s * 0.30f, midY = s * 0.5f;
                float chevronSigma2 = 2f * 10f * 10f;    // chevron 笔画高斯（二轮加粗 5.5→10）
                const byte laneV = 195;                   // 车道底亮度（略暗于 chevron=对比滚动）
                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        byte v, a;
                        if (y < border || y >= s - border)
                        {
                            v = 0; a = 255;               // 长边黑描边（rgb 0 乘任意 tint 恒黑=格子高亮同语言）
                        }
                        else
                        {
                            // 实心车道底 + 粗亮 chevron（臂起止=描边内缘，贴边画满整格宽）
                            float d = Mathf.Min(
                                DistToSegment(x, y, baseX, border, apexX, midY),
                                DistToSegment(x, y, baseX, s - border, apexX, midY));
                            float chev = Mathf.Exp(-d * d / chevronSigma2);
                            v = chev > 0.5f ? (byte)255 : laneV;
                            a = 255;
                        }
                        pixels[y * s + x] = new Color32(v, v, v, a);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _aimArrowBandTexture = tex;
                return tex;
            }
        }

        private static Texture2D _aimArrowHeadTexture;

        /// <summary>方向箭头头部纹理（256×256 实心三角指向 +U，Clamp+Bilinear；设计面=1 格×1 格）。
        /// 2026-10-15 二轮拍板同批加黑描边：三角内缘 8px 纯黑描边环（格子高亮同语言）+ 白芯 +
        /// 边外 8px 白羽化软边（不生硬）。非滚动——悬停/待定只滚 chevron 带，箭头静止指向前方</summary>
        public static Texture2D AimArrowHeadTexture
        {
            get
            {
                if (_aimArrowHeadTexture != null) return _aimArrowHeadTexture;
                const int s = 256;
                var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
                {
                    name = "BattleAimArrowHeadTexture",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color32[s * s];
                float apexX = 236f, tailX = 28f, midY = 128f, halfH = 104f;
                const float rim = 8f;                     // 内缘黑描边宽
                float halo2 = 2f * 6f * 6f;               // 边外羽化
                for (int y = 0; y < s; y++)
                {
                    for (int x = 0; x < s; x++)
                    {
                        float d = DistToTriangleInterior(x, y, tailX, midY - halfH, apexX, midY, tailX, midY + halfH);
                        byte r, g, b, a;
                        if (d <= -rim) { r = g = b = 255; a = 255; }   // 白芯
                        else if (d <= 0f) { r = g = b = 0; a = 255; }  // 内缘黑描边
                        else                                                    // 边外白羽化
                        {
                            r = g = b = 255;
                            a = (byte)Mathf.RoundToInt(Mathf.Exp(-d * d / halo2) * 255f);
                        }
                        pixels[y * s + x] = new Color32(r, g, b, a);
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _aimArrowHeadTexture = tex;
                return tex;
            }
        }

        private static Texture2D _aimMarkerTexture;

        /// <summary>预计命中竖线纹理（96×256 竖向，Clamp+Bilinear）：横向=实心亮芯（|dx|≤18px 全亮
        /// 平台）+ 光肩平滑淡出（18→46px）——2026-10-15 二轮拍板「只看见描边未看见光柱」加亮加宽
        /// （首版 64px 细高斯淡到看不见）；纵向=底亮顶淡幂次渐变（pow 1.2），软边非生硬直线。
        /// v=0=柱底（全亮）→ v=1=柱顶（淡出）</summary>
        public static Texture2D AimMarkerTexture
        {
            get
            {
                if (_aimMarkerTexture != null) return _aimMarkerTexture;
                const int w = 96, h = 256;
                var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "BattleAimMarkerTexture",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                };
                var pixels = new Color32[w * h];
                float center = (w - 1) * 0.5f;
                for (int y = 0; y < h; y++)
                {
                    float v = y / (float)(h - 1);
                    float vertical = Mathf.Pow(1f - v, 1.2f); // 底亮顶淡（缓于首版 1.45）
                    for (int x = 0; x < w; x++)
                    {
                        // 实心亮芯平台 + 光肩（SmoothStep 18→46px）——软边非硬线但芯恒亮
                        float t = Mathf.Clamp01((Mathf.Abs(x - center) - 18f) / 28f);
                        float horizontal = 1f - t * t * (3f - 2f * t);
                        float a = horizontal * vertical;
                        pixels[y * w + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                    }
                }
                tex.SetPixels32(pixels);
                tex.Apply(false, true);
                _aimMarkerTexture = tex;
                return tex;
            }
        }

        private static Sprite _soulGlowSprite;

        /// <summary>灵魂柔光晕 sprite（决策九十一，2026-10-25）：128² 白色软圆（径向 SmoothStep
        /// 0.30→0.85 淡出、30% 亮芯）——tint 承色（灵魂蓝×光晕不透明度，SpriteRenderer.color）。
        /// 静态缓存（域重载随 static 归零自然重建）；消费=UnitView.SetSoulVisual 挂倾斜组子级
        /// （幽灵立牌的柔光底，与立牌同平面随幽浮同漂）</summary>
        public static Sprite SoulGlowSprite()
        {
            if (_soulGlowSprite != null) return _soulGlowSprite;
            const int s = 128;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                name = "BattleSoulGlowTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[s * s];
            float center = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = x - center, dy = y - center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy) / center;
                    float a = 1f - Mathf.SmoothStep(0.30f, 0.85f, d); // 亮芯+柔和淡出
                    pixels[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _soulGlowSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
            return _soulGlowSprite;
        }

        private static Sprite _leyLineGradientSprite;

        /// <summary>地脉滤镜渐变 sprite（决策九十一）：4×256 白 RGB、alpha 顶 0→底 1（垂直渐变）
        /// ——BattlePlayer 阴间滤镜层消费（顶片 y 翻转承滤镜顶色、底片承滤镜底色=下沉感）。
        /// 静态缓存（域重载归零重建）</summary>
        public static Sprite LeyLineGradientSprite()
        {
            if (_leyLineGradientSprite != null) return _leyLineGradientSprite;
            const int w = 4, h = 256;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false)
            {
                name = "LeyLineGradientTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[w * h];
            for (int y = 0; y < h; y++)
            {
                byte a = (byte)Mathf.RoundToInt(y / (float)(h - 1) * 255f); // v=0 底亮 → v=1 顶淡
                for (int x = 0; x < w; x++)
                    pixels[y * w + x] = new Color32(255, 255, 255, a);
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _leyLineGradientSprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(0.5f, 0.5f), 100f);
            return _leyLineGradientSprite;
        }

        private static Sprite _leyLineVignetteSprite;

        /// <summary>地脉滤镜暗角 sprite（决策九十一）：256² 径向（中心透明→0.55 半径起 SmoothStep
        /// 到边缘实心）——四缘压暗的「阴间」包边。静态缓存</summary>
        public static Sprite LeyLineVignetteSprite()
        {
            if (_leyLineVignetteSprite != null) return _leyLineVignetteSprite;
            const int s = 256;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                name = "LeyLineVignetteTexture",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[s * s];
            float center = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    float dx = (x - center) / center, dy = (y - center) / center;
                    float d = Mathf.Sqrt(dx * dx + dy * dy); // 0=中心 → ~1.41 角点
                    float a = Mathf.SmoothStep(0.55f, 1.0f, d); // 中心 55% 内透明，四缘实
                    pixels[y * s + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(a * 255f));
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _leyLineVignetteSprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), s);
            return _leyLineVignetteSprite;
        }

        private static readonly System.Collections.Generic.Dictionary<int, Texture2D> _aimRadiusBlockTextures
            = new System.Collections.Generic.Dictionary<int, Texture2D>();

        /// <summary>范围提示整块纹理（按半径缓存；Clamp+Bilinear；设计面=(2R+1)格×(2R+1)格、每格 128px
        /// ——AimCellTexture 同密度）。2026-10-15 拍板「范围提示……不是复用现有的单格高亮，而是完整
        /// 的一块，可以参考滚动带」：实心白底（材质 tint 承色=格子高亮同语言）+ **整块外缘**黑描边环
        /// （12px=9.4% 单格宽——AimCellTexture 内嵌黑边同比例；块中间无格线=完整一块）。
        /// 复用族=自施放光环瞄准（寒冰之棱半径提示）+未来「选格降闪电攻击半径内敌人」类技能的范围
        /// 提示（提示中心跟随待定格）</summary>
        public static Texture2D AimRadiusBlockTexture(int radius)
        {
            int n = radius * 2 + 1;
            if (_aimRadiusBlockTextures.TryGetValue(radius, out var cached) && cached != null) return cached;
            const int cell = 128, border = 12;          // 每格 128px；描边 12px=AimCellTexture 边框同比例
            int s = cell * n;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false)
            {
                name = $"BattleAimRadiusBlockTexture_{n}x{n}",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            var pixels = new Color32[s * s];
            for (int y = 0; y < s; y++)
            {
                for (int x = 0; x < s; x++)
                {
                    bool edge = x < border || y < border || x >= s - border || y >= s - border;
                    // 黑描边 rgb 0（乘任意 tint 恒黑）+ 白芯 rgb 255（材质 tint 承色）——alpha 恒满
                    byte v = edge ? (byte)0 : (byte)255;
                    pixels[y * s + x] = new Color32(v, v, v, 255);
                }
            }
            tex.SetPixels32(pixels);
            tex.Apply(false, true);
            _aimRadiusBlockTextures[radius] = tex;
            return tex;
        }

        /// <summary>范围提示整块材质（白底纹理+调色 tint——CreateAimCellMaterial 同族）</summary>
        public static Material CreateAimRadiusBlockMaterial(Color color, int radius)
        {
            var material = CreateTransparentUnlitMaterial(color);
            material.mainTexture = AimRadiusBlockTexture(radius);
            return material;
        }

        /// <summary>点到线段距离（箭头 chevron 高斯距离场用）</summary>
        private static float DistToSegment(float px, float py, float ax, float ay, float bx, float by)
        {
            float abx = bx - ax, aby = by - ay;
            float lenSq = abx * abx + aby * aby;
            float t = lenSq > 0f ? Mathf.Clamp(((px - ax) * abx + (py - ay) * aby) / lenSq, 0f, 1f) : 0f;
            float dx = px - (ax + abx * t), dy = py - (ay + aby * t);
            return Mathf.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>点到三角形三边的最小距离（形内=0——软边只作用边缘；箭头头部用）</summary>
        private static float DistToTriangleInterior(float px, float py,
            float ax, float ay, float bx, float by, float cx, float cy)
        {
            bool inside = SameSide(px, py, ax, ay, bx, by, cx, cy)
                && SameSide(px, py, bx, by, cx, cy, ax, ay)
                && SameSide(px, py, cx, cy, ax, ay, bx, by);
            if (inside) return 0f;
            return Mathf.Min(
                Mathf.Min(DistToSegment(px, py, ax, ay, bx, by), DistToSegment(px, py, bx, by, cx, cy)),
                DistToSegment(px, py, cx, cy, ax, ay));
        }

        private static bool SameSide(float px, float py, float ax, float ay, float bx, float by, float cx, float cy)
        {
            float d1 = (bx - ax) * (py - ay) - (by - ay) * (px - ax);
            float d2 = (bx - ax) * (cy - ay) - (by - ay) * (cx - ax);
            return d1 * d2 >= 0f;
        }

        /// <summary>主世界战斗渲染层名（2026-10-25 维度泄漏修复拍板 A：世界特效 quad/圆盘统一挂
        /// BattleMain 层——相机 cullingMask 切地脉视角时一并隐藏，根治「切地脉见主世界箭矢」；
        /// ProjectSettings/TagManager layer 9；灵魂立牌树=BattleLeyLine〔layer 10，BattlePlayer.CreateView 落层〕）</summary>
        public const string LayerBattleMain = "BattleMain";

        /// <summary>地脉渲染层名（灵魂立牌树整树挂载）</summary>
        public const string LayerBattleLeyLine = "BattleLeyLine";

        /// <summary>维度→渲染层名映射（2026-10-26 fix-batch2 ②b：加新维度=枚举值+此处一行 case；
        /// 层未配置时 NameToLayer&lt;0=调用方静默退回旧行为）</summary>
        public static string DimensionLayerName(BattleDimension dimension)
        {
            switch (dimension)
            {
                case BattleDimension.LeyLine: return LayerBattleLeyLine;
                case BattleDimension.MainWorld:
                default: return LayerBattleMain;
            }
        }

        /// <summary>世界件挂主世界层（TagManager 未配层时静默退回 Default=旧行为）</summary>
        public static void SetMainWorldLayer(GameObject go)
        {
            int layer = LayerMask.NameToLayer(LayerBattleMain);
            if (layer >= 0 && go != null) go.layer = layer;
        }

        /// <summary>世界层纯色 Quad（基元 + 去 Collider + 挂材质；姿态/缩放由调用方补——既有调用点世界系/本地系两种用法）</summary>
        public static GameObject CreateQuad(Transform parent, string name, Material sharedMaterial)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = name;
            UnityEngine.Object.Destroy(quad.GetComponent<Collider>());
            quad.transform.SetParent(parent, false);
            quad.GetComponent<MeshRenderer>().sharedMaterial = sharedMaterial;
            SetMainWorldLayer(quad); // 维度渲染层（2026-10-25 泄漏修复：瞄准贴片/范围块/箭雨 cue 等全走本工厂）
            return quad;
        }

        /// <summary>圆盘扇面分段数（中心顶点+24 段圆周）</summary>
        private const int DiscSegments = 24;

        /// <summary>共享圆盘 mesh（工厂持有常驻——Destroy 物体不销 mesh，共享单实例免释放）</summary>
        private static Mesh _discMesh;

        /// <summary>世界层纯色圆面片（程序化扇面 mesh：中心顶点+24 段圆周，直径 1、法线 -Z 与 Quad 同向；
        /// 单位受击圆柱的底座圆盘可视化——视觉即判定，直径=BattleMetrics.UnitCylinderDiameter，docs/18 决策二。
        /// localScale x/y 语义与 Quad 一致（旋转 90° 平铺后=地面直径），支持非均匀缩放出椭圆（尸体压扁）。
        /// mesh=工厂静态共享单实例（全单位同构，2026-09-28 批6 收口：原每调用 new Mesh=同构 mesh ×N 份
        /// 且无持有者释放——共享根治 §63① mesh 条款）</summary>
        public static GameObject CreateDisc(Transform parent, string name, Material sharedMaterial)
        {
            if (_discMesh == null)
            {
                var vertices = new Vector3[DiscSegments + 1];
                var triangles = new int[DiscSegments * 3];
                vertices[0] = Vector3.zero;
                for (int i = 0; i < DiscSegments; i++)
                {
                    float angle = 2f * Mathf.PI * i / DiscSegments;
                    vertices[i + 1] = new Vector3(Mathf.Cos(angle) * 0.5f, Mathf.Sin(angle) * 0.5f, 0f);
                    int next = i + 1 == DiscSegments ? 1 : i + 2;
                    triangles[i * 3] = 0;
                    triangles[i * 3 + 1] = next;
                    triangles[i * 3 + 2] = i + 1;
                }

                _discMesh = new Mesh { name = "BattleDiscMesh" };
                _discMesh.vertices = vertices;
                _discMesh.triangles = triangles;
                _discMesh.RecalculateNormals();
                _discMesh.RecalculateBounds();
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = _discMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = sharedMaterial;
            SetMainWorldLayer(go); // 维度渲染层（2026-10-25 泄漏修复）
            return go;
        }

        /// <summary>底座盘描边环 mesh（内径 0.465/外径 0.535 归一化——localScale=盘径即等比带宽：
        /// 盘缘 0.5 平分环带，外露黑边=盘径×3.5%〔首版 7% 用户目检「太粗了，砍一半」2026-10-06〕；
        /// 拍板「底部圆盘加不透明黑色描边」。共享单实例常驻（同 _discMesh；环宽随盘径等比=
        /// 核心 0.8 大盘描边自动加粗，免参数化缓存）</summary>
        private static Mesh _discOutlineMesh;

        /// <summary>描边环共享不透明黑材质（常驻单实例免释放——环靠 opaque 深度天然画于水面之上，
        /// 与透明盘的 sortingOrder 排序体系正交；Cull Off=薄带环绕向免究恒可见）</summary>
        private static Material _discOutlineMaterial;

        /// <summary>底座盘描边环共享黑材质（不透明 Geometry 队列——黑边不透出底下的地形/水面；
        /// 懒建常驻单实例勿 Destroy，同 _discMesh 生命周期口径）</summary>
        public static Material DiscOutlineMaterial
        {
            get
            {
                if (_discOutlineMaterial == null)
                {
                    _discOutlineMaterial = new Material(UnlitShader)
                    {
                        color = Color.black,
                        renderQueue = (int)UnityEngine.Rendering.RenderQueue.Geometry,
                    };
                    _discOutlineMaterial.SetFloat("_Cull", 0f); // Cull Off——双面渲染，环带绕向免究
                }
                return _discOutlineMaterial;
            }
        }

        /// <summary>世界层圆环面片（程序化双圈带状 mesh：内圈 0.465+外圈 0.535 各 DiscSegments 段；
        /// mesh=工厂静态共享单实例〔全单位同构，§63① mesh 条款〕；姿态/缩放由调用方补——
        /// localScale=(盘径,盘径,1) 即盘缘平分环带=外露描边 3.5% 盘径；支持非均匀缩放出椭圆（尸体压扁）。
        /// 消费方=UnitView 底座盘不透明黑描边（2026-10-06 拍板）</summary>
        public static GameObject CreateRing(Transform parent, string name, Material sharedMaterial)
        {
            if (_discOutlineMesh == null)
            {
                int segs = DiscSegments;
                var vertices = new Vector3[segs * 2];
                var triangles = new int[segs * 6];
                for (int i = 0; i < segs; i++)
                {
                    float angle = 2f * Mathf.PI * i / segs;
                    var dir = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
                    vertices[i] = dir * 0.465f;         // 内圈
                    vertices[segs + i] = dir * 0.535f;  // 外圈
                    int next = (i + 1) % segs;
                    int inA = i, inB = next, outA = segs + i, outB = segs + next;
                    int t = i * 6; // 段四边形（法线 RecalculateNormals 补；绕向交由材质 Cull Off 免究）
                    triangles[t] = inA; triangles[t + 1] = inB; triangles[t + 2] = outB;
                    triangles[t + 3] = inA; triangles[t + 4] = outB; triangles[t + 5] = outA;
                }
                _discOutlineMesh = new Mesh { name = "BattleDiscOutlineMesh" };
                _discOutlineMesh.vertices = vertices;
                _discOutlineMesh.triangles = triangles;
                _discOutlineMesh.RecalculateNormals();
                _discOutlineMesh.RecalculateBounds();
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = _discOutlineMesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = sharedMaterial;
            return go;
        }
    }
}
