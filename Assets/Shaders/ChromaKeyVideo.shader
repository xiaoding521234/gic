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
                half4 c = tex2D(_MainTex, i.uv);
                half shapeAlpha = ChromaShape(c.rgb); // 抠色后的形状 alpha（边缘霜光基准）
                c.g = min(c.g, max(c.r, c.b)); // despill
                half alpha = shapeAlpha * _Color.a;
                half4 col = half4(c.rgb * _Color.rgb, alpha);

                // 轮廓外延冰缘的邻域形状 alpha（±4 texel 十字四采样取 max——视频路径形状=各自抠色）
                float2 o4 = _MainTex_TexelSize.xy * 4.0;
                half neighborAlpha = max(
                    max(ChromaShape(tex2D(_MainTex, i.uv + float2(o4.x, 0.0)).rgb),
                        ChromaShape(tex2D(_MainTex, i.uv - float2(o4.x, 0.0)).rgb)),
                    max(ChromaShape(tex2D(_MainTex, i.uv + float2(0.0, o4.y)).rgb),
                        ChromaShape(tex2D(_MainTex, i.uv - float2(0.0, o4.y)).rgb)));

                FrostApply(col, i.worldPos.y, i.uv, shapeAlpha, neighborAlpha); // _FrozenAmount=0 时零成本直通

                // 选中描边（2026-10-05 拍板「立牌加描边，颜色=所属玩家色」）：形状外邻域环采样
                // （8 方向 ±_OutlineWidth texel，形状=各自抠色）→ 队伍色描边；frost 之后应用
                // （选中态描边盖过轮廓外冰缘保可读）。_OutlineEnabled=0 时分支不执行
                if (_OutlineEnabled > 0.5 && shapeAlpha < 0.35)
                {
                    float2 oo = _MainTex_TexelSize.xy * _OutlineWidth;
                    half ring = max(
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
