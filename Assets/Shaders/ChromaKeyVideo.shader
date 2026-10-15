// 立牌循环动画视频 ChromaKey shader（B-S3 视频路线）
// 消费 VideoPlayer→RenderTexture 的绿幕画面，运行时抠色成透明底；
// 抠色阈值与离线抽帧管线（extract_build.py）同参：excess=g-max(r,b) 平滑带 [20/255,45/255] + 暗部亮度门 g>60/255，
// despill=边缘绿压回 max(r,b)（安柏主体零绿色素，不受影响）。
// _Color 承载 tint（尸体灰/受击闪红，同 SpriteRenderer.color 语义，由 UnitView.RefreshTint 写入；
// 冻结配色 2026-10-02 起由霜化接管——UnitView 冻结时 tint 置白）。
// 冻结霜化（2026-10-02 拍板「像真的结冰」shader 实现路线）：霜色重映射+边缘霜光+晶体闪烁+冻结遮罩蔓延，
// 数学=FrozenFrost.cginc（与 FrozenSprite.shader 同源）；_FrozenAmount 由 UnitView 对本组件持有的
// per-unit 材质实例直接 SetFloat（视频路径无 MPB/换材质需求）。轮廓外延冰缘的邻域形状 alpha=
// 四邻域各自抠色求形状（视频帧整幅不透明，形状≠tex alpha——与 sprite 路径形状定义不同）。
Shader "GIC/Battle/ChromaKeyVideo"
{
    Properties
    {
        _MainTex ("视频帧 (RenderTexture)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
        _FrozenAmount ("结冰进度", Range(0, 1)) = 0
        _FreezeFootY ("脚部世界Y", Float) = 0
        _FreezeTopY ("头顶世界Y", Float) = 1
        _FrostDeepColor ("暗部霜色", Color) = (0.58, 0.68, 0.76, 1)
        _FrostBrightColor ("霜白", Color) = (0.85, 0.95, 1.00, 1)
        _FrostDesat ("去饱和强度", Range(0, 1)) = 0.8
        _FrostLift ("霜化提亮", Range(0, 0.5)) = 0.3
        _FrostPatchStrength ("白霜斑强度", Range(0, 1)) = 0.55
        _MistColor ("冰雾色", Color) = (0.71, 0.83, 0.95, 1)
        _MistAlpha ("冰雾浓度", Range(0, 1)) = 0.3
        _MistHeight ("冰雾高度占比", Range(0.05, 1)) = 0.3
        _FrostAlpha ("冰体不透明度", Range(0.05, 1)) = 0.78
        _FrostFringeAlpha ("轮廓外冰缘不透明度", Range(0, 1)) = 0.38

        // 选中描边（2026-10-05 拍板「立牌加描边，颜色=所属玩家色」；shader 装饰性 Header 属性
        // 带连字符会炸 ShaderLab 解析器，故用普通注释分区）
        _OutlineEnabled ("选中描边", Range(0, 1)) = 0
        _OutlineColor ("描边色", Color) = (1, 1, 1, 1)
        _OutlineWidth ("描边宽(texel)", Range(1, 16)) = 9

        // Alpha 打包模式（2026-10-16 离线抠绿批，只对非待机动作片）：1=离线抠好的打包片——
        // 色彩区上半（打包高 2/3，v∈[0,2/3)）、alpha 区下半（v∈[2/3,1]，垂直 ½ 分辨率灰度）——
        // PlayActionVideo/SetMoveVideo 按片置位、回待机时归零（待机恒绿幕运行时抠色）
        _PackedAlpha ("Alpha打包模式", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            // 与立牌 SpriteRenderer 同层 3000：恒后画于水面 2999（docs/14 §89 第六轮队列纪律），
            // 与立牌 sprite 间保持既有的透明距离排序语义
            "Queue"="Transparent"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off

        // 朝向镜像（2026-09-29 拍板③）用负 localScale.x 翻面：绕向随之翻转会被默认 Cull Back 剔成隐形——
        // 立牌恒面向相机无背面可言，直接 Cull Off（零开销）
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "FrozenFrost.cginc"

            sampler2D _MainTex;
            float4 _MainTex_TexelSize; // 轮廓外延冰缘/选中描边的邻域采样步长
            fixed4 _Color;
            float _OutlineEnabled;
            float4 _OutlineColor;
            float _OutlineWidth;

            // 抠色形状 alpha（=1-键值）：抽帧管线同参 excess 平滑带+暗部亮度门；霜化边缘/冰缘共用
            half ChromaShape(half3 c)
            {
                half maxRB = max(c.r, c.b);
                half excess = c.g - maxRB;
                half key = smoothstep(20.0 / 255.0, 45.0 / 255.0, excess);
                key *= step(60.0 / 255.0, c.g); // 暗部亮度门：模型画的深绿阴影不误抠
                return 1.0 - key;
            }

            // —— Alpha 打包模式采样（2026-10-16 离线抠绿批）——
            // 布局=上下分区：色彩区占打包高 2/3（视频行 y∈[0,H) 顶起）、alpha 区下半（打包行 H+y/2，½ 分辨率）。
            // **RT 方向锚（2026-10-16 稀碎像素报障根因）**：绿幕路径正立 ⟹ quad v=1（立牌顶）↔ 视频顶行
            // （RT 顶=视频顶、tex2D v 与视频行反向）——分区公式必须按反向关系推导：
            //   色彩：视频行=(1-v)H → RT_v = 1 − (1-v)H/1.5H = 1/3 + (2/3)v
            //   alpha：打包行 = H+(1-v)H/2 → RT_v = 1 − 行/1.5H = v/3
            // （首版误按同向写 v×2/3 与 2/3+v/3 = 两区对调：灰度当色 × 色彩 g 当 alpha = 稀碎噪点）
            half4 SamplePacked(float2 quadUv)
            {
                return tex2D(_MainTex, float2(quadUv.x, (1.0 / 3.0) + quadUv.y * (2.0 / 3.0)));
            }

            half SamplePackedAlpha(float2 quadUv)
            {
                return tex2D(_MainTex, float2(quadUv.x, quadUv.y / 3.0)).g;
            }

            float _PackedAlpha;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz; // 霜化遮罩=世界系梯度
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 双模式（2026-10-16 离线抠绿批）：_PackedAlpha=1 走离线抠好的打包片（色彩区+alpha 区），
                // =0 走原绿幕运行时抠色（待机片/未迁移动作片）。色彩/形状 alpha/邻域采样双路径同构，
                // despill/霜化/描边对两模式无感（打包片色彩区离线已 despill，再 min 一次恒等无害）
                half4 c;
                half shapeAlpha;
                if (_PackedAlpha > 0.5)
                {
                    c = SamplePacked(i.uv);
                    shapeAlpha = SamplePackedAlpha(i.uv); // 形状=显式 alpha 通道（无阈值逻辑可漂移）
                }
                else
                {
                    c = tex2D(_MainTex, i.uv);
                    shapeAlpha = ChromaShape(c.rgb);
                }
                c.g = min(c.g, max(c.r, c.b)); // despill（打包片色彩区离线已 despill——恒等无害；绿幕路径承载原职责）
                half alpha = shapeAlpha * _Color.a;
                half4 col = half4(c.rgb * _Color.rgb, alpha);

                // 轮廓外延冰缘的邻域形状 alpha（±4 texel 十字四采样取 max）——
                // 绿幕路径=四邻域各自抠色（视频帧整幅不透明，形状≠tex alpha）；
                // 打包路径=alpha 区四邻域直采（邻域步长按 alpha 区分辨率=打包 texel ×(3/2,3)）
                half neighborAlpha;
                if (_PackedAlpha > 0.5)
                {
                    float2 o4a = _MainTex_TexelSize.xy * float2(4.0, 8.0); // x=色彩区同密度；y=alpha 区 ½ 分辨率×区高 ⅓=3 倍 texel 步
                    neighborAlpha = max(
                        max(SamplePackedAlpha(i.uv + float2(o4a.x, 0.0)),
                            SamplePackedAlpha(i.uv - float2(o4a.x, 0.0))),
                        max(SamplePackedAlpha(i.uv + float2(0.0, o4a.y)),
                            SamplePackedAlpha(i.uv - float2(0.0, o4a.y))));
                }
                else
                {
                    float2 o4 = _MainTex_TexelSize.xy * 4.0;
                    neighborAlpha = max(
                        max(ChromaShape(tex2D(_MainTex, i.uv + float2(o4.x, 0.0)).rgb),
                            ChromaShape(tex2D(_MainTex, i.uv - float2(o4.x, 0.0)).rgb)),
                        max(ChromaShape(tex2D(_MainTex, i.uv + float2(0.0, o4.y)).rgb),
                            ChromaShape(tex2D(_MainTex, i.uv - float2(0.0, o4.y)).rgb)));
                }

                FrostApply(col, i.worldPos.y, i.uv, shapeAlpha, neighborAlpha); // _FrozenAmount=0 时零成本直通

                // 选中描边（2026-10-05 拍板「立牌加描边，颜色=所属玩家色」）：形状外邻域环采样
                // → 队伍色描边；frost 之后应用（选中态描边盖过轮廓外冰缘保可读）。_OutlineEnabled=0 时不执行
                if (_OutlineEnabled > 0.5 && shapeAlpha < 0.35)
                {
                    half ring;
                    if (_PackedAlpha > 0.5)
                    {
                        float2 ooa = _MainTex_TexelSize.xy * float2(_OutlineWidth, _OutlineWidth * 2.0);
                        ring = max(
                            max(SamplePackedAlpha(i.uv + float2(ooa.x, 0.0)),
                                SamplePackedAlpha(i.uv - float2(ooa.x, 0.0))),
                            max(SamplePackedAlpha(i.uv + float2(0.0, ooa.y)),
                                SamplePackedAlpha(i.uv - float2(0.0, ooa.y))));
                        float2 odda = ooa * 0.7071;
                        ring = max(ring,
                            max(
                                max(SamplePackedAlpha(i.uv + odda),
                                    SamplePackedAlpha(i.uv - odda)),
                                max(SamplePackedAlpha(i.uv + float2(odda.x, -odda.y)),
                                    SamplePackedAlpha(i.uv - float2(odda.x, -odda.y)))));
                    }
                    else
                    {
                        float2 oo = _MainTex_TexelSize.xy * _OutlineWidth;
                        ring = max(
                            max(ChromaShape(tex2D(_MainTex, i.uv + float2(oo.x, 0.0)).rgb),
                                ChromaShape(tex2D(_MainTex, i.uv - float2(oo.x, 0.0)).rgb)),
                            max(ChromaShape(tex2D(_MainTex, i.uv + float2(0.0, oo.y)).rgb),
                                ChromaShape(tex2D(_MainTex, i.uv - float2(0.0, oo.y)).rgb)));
                        float2 odd = oo * 0.7071;
                        ring = max(ring,
                            max(
                                max(ChromaShape(tex2D(_MainTex, i.uv + odd).rgb),
                                    ChromaShape(tex2D(_MainTex, i.uv - odd).rgb)),
                                max(ChromaShape(tex2D(_MainTex, i.uv + float2(odd.x, -odd.y)).rgb),
                                    ChromaShape(tex2D(_MainTex, i.uv - float2(odd.x, -odd.y)).rgb))));
                    }
                    half m = saturate((ring - 0.2) * 1.6); // 环命中强度（贴边采样 alpha 低处平滑收尾）
                    col.rgb = lerp(col.rgb, _OutlineColor.rgb, m);
                    col.a = max(col.a, m * _OutlineColor.a);
                }
                return col;
            }
            ENDCG
        }
    }
}
