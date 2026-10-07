// DeckRowHighlight —— 切卡组高亮过渡（docs/18 决策五十二）
// 金色背景从左往右填充/退去：前沿用双层 1D 噪声沿 y 扰动（非竖直线），叠加指数扫光带；
// _Erase=0 填充（前沿左侧=金）、=1 退去（前沿右侧=金，金色从左往右被清除）。
// 稳态：_Progress=1.5 → 前沿远出右缘，输出=纯 _Color 实底（与普通 Image tint 视觉恒等）。
Shader "UI/DeckRowHighlight"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Progress ("Progress", Range(0, 1.5)) = 0
        _Erase ("Erase Mode", Float) = 0
        _Seed ("Noise Seed", Float) = 0
        _EdgeNoiseScale ("边界噪声频率", Float) = 5
        _EdgeNoiseWidth ("边界噪声幅度", Range(0, 0.2)) = 0.06
        _GlowWidth ("扫光宽度", Range(0.01, 0.3)) = 0.05
        _GlowStrength ("扫光强度", Range(0, 1)) = 0.85
        _GlowColor ("扫光颜色", Color) = (1, 0.97, 0.85, 1)
        _ColorMask ("Color Mask", Float) = 15

        [HideInInspector] _StencilComp ("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil ("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp ("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask ("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask ("Stencil Read Mask", Float) = 255
        [HideInInspector] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }
    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "IgnoreProjector"="True"
            "RenderType"="Transparent"
            "PreviewType"="Plane"
            "CanUseSpriteAtlas"="True"
        }
        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _TextureSampleAdd;
            fixed4 _Color;
            float4 _ClipRect;
            float _Progress;
            float _Erase;
            float _Seed;
            float _EdgeNoiseScale;
            float _EdgeNoiseWidth;
            float _GlowWidth;
            float _GlowStrength;
            float4 _GlowColor;

            v2f vert(appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            float hash1(float p)
            {
                return frac(sin(p * 127.1) * 43758.5453);
            }

            float noise1(float p)
            {
                float i = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(hash1(i), hash1(i + 1.0), f);
            }

            fixed4 frag(v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;

                // 双层 1D 噪声沿 y 扰动前沿（主频+1/2.4 倍细频叠加）——边界呈不规则波浪锯齿非竖直线
                float n = noise1(uv.y * _EdgeNoiseScale + _Seed) * 0.7
                        + noise1(uv.y * _EdgeNoiseScale * 2.4 + _Seed * 1.7) * 0.3;
                n = n * 2.0 - 1.0; // [-1, 1]

                float w = _EdgeNoiseWidth;
                // 前沿 x：progress 从 0→(1+w)，保证最大噪声负向时也能扫满全行
                float edge = _Progress * (1.0 + w) - w * 0.5 + n * w;

                // 前沿抗锯齿软化（fwidth=当前屏幕像素宽度）
                float aa = fwidth(uv.x) * 1.5;
                float maskFill = smoothstep(edge + aa, edge - aa, uv.x); // 前沿左侧=1
                float mask = lerp(maskFill, 1.0 - maskFill, _Erase);    // erase：前沿右侧=金色区

                // 刻意不采样 _MainTex（同先例 CardLightBand）：UGUI 自定义材质不绑定主纹理，
                // 采样恒黑（texAlpha=0）会把一切输出压成全透明——高亮为纯色矩形无需 sprite 形状
                // 指数衰减扫光带（前沿居中，动画结束时 edge 远出右缘自然归零）
                float glow = exp(-abs(uv.x - edge) / _GlowWidth * 3.0);

                float baseAlpha = mask;
                float glowAlpha = glow * _GlowStrength * (1.0 - mask * 0.35);

                // mask 内=金色（tint 走顶点色=Image.color，材质 _Color 槽恒白勿用），mask 外前沿=亮暖白扫光
                float3 rgb = lerp(_GlowColor.rgb, IN.color.rgb, mask);
                float alpha = saturate(baseAlpha + glowAlpha) * IN.color.a;

                #ifdef UNITY_UI_CLIP_RECT
                alpha *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(alpha - 0.001);
                #endif

                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
}
