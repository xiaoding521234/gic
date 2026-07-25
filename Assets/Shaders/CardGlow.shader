Shader "UI/CardGlow"
{
    Properties
    {
        [PerRendererData] _MainTex ("Card Texture", 2D) = "white" {}
        _GlowColor ("Glow Color", Color) = (1, 0.8, 0.2, 1)
        _GlowIntensity ("Glow Intensity", Range(0, 5)) = 2
        _GlowSpread ("Glow Spread", Range(0, 1)) = 0.3
        _GlowPulse ("Glow Pulse Speed", Float) = 2
        _InnerGlow ("Inner Glow", Range(0, 1)) = 0.5
        _EdgeSoftness ("Edge Softness", Range(0.01, 0.5)) = 0.1
        _VerticalStretch ("Vertical Stretch", Range(0.5, 3)) = 1.5
        
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _GlowColor;
            float _GlowIntensity;
            float _GlowSpread;
            float _GlowPulse;
            float _InnerGlow;
            float _EdgeSoftness;
            float _VerticalStretch;

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                // 采样卡片纹理
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                // 计算UV到边缘的距离（竖直方向为主）
                float2 uv = i.uv;
                
                // 水平边缘距离（左右边缘）
                float distToLeft = uv.x;
                float distToRight = 1.0 - uv.x;
                float distToEdgeH = min(distToLeft, distToRight);
                
                // 竖直边缘距离（上下边缘）
                float distToBottom = uv.y;
                float distToTop = 1.0 - uv.y;
                float distToEdgeV = min(distToBottom, distToTop);
                
                // 竖直方向发光：在上下边缘更亮，水平方向衰减快
                float verticalGlow = exp(-distToEdgeV / _GlowSpread);
                
                // 水平方向快速衰减，保持光在竖直方向
                float horizontalFalloff = exp(-distToEdgeH / (_EdgeSoftness * 0.5));
                
                // 内部柔光（卡片整体微微发光）
                float innerGlow = _InnerGlow * (1.0 - distToEdgeV * 0.5);
                
                // 竖直拉伸：光在上下方向延伸更远
                float stretchFactor = _VerticalStretch;
                float stretchedGlowV = exp(-distToEdgeV / (_GlowSpread * stretchFactor));
                
                // 脉冲动画
                float pulse = 1.0 + sin(_Time.y * _GlowPulse) * 0.3;
                
                // 组合发光
                float glowV = verticalGlow * horizontalFalloff * pulse;
                float glowStretched = stretchedGlowV * horizontalFalloff * pulse * 0.6;
                
                float totalGlow = max(glowV, glowStretched) + innerGlow;
                
                // 卡片边缘高亮（描边感）
                float edgeGlow = 0;
                float edgeThreshold = 0.02;
                if (texColor.a > 0.1)
                {
                    // 检测像素是否在卡片边缘
                    float2 offset1 = float2(edgeThreshold, 0);
                    float2 offset2 = float2(0, edgeThreshold);
                    float alphaLeft = tex2D(_MainTex, i.uv - offset1).a;
                    float alphaRight = tex2D(_MainTex, i.uv + offset1).a;
                    float alphaDown = tex2D(_MainTex, i.uv - offset2).a;
                    float alphaUp = tex2D(_MainTex, i.uv + offset2).a;
                    
                    float edgeAlpha = (1.0 - alphaLeft) + (1.0 - alphaRight) + (1.0 - alphaDown) + (1.0 - alphaUp);
                    edgeGlow = saturate(edgeAlpha * 0.5);
                    
                    // 竖直边缘更亮
                    float vertEdgeAlpha = (1.0 - alphaUp) + (1.0 - alphaDown);
                    edgeGlow += vertEdgeAlpha * 0.3;
                }
                
                // 最终发光强度
                float finalGlow = (totalGlow + edgeGlow * 0.5) * _GlowIntensity;
                
                // 只在卡片不透明区域发光
                float alphaMask = texColor.a;
                
                // 发光向外扩展（在半透明区域也发光）
                float expandedGlow = finalGlow * smoothstep(0, 0.3, alphaMask);
                
                // 颜色
                float3 glowColor = _GlowColor.rgb * _GlowColor.a;
                
                // 中心高亮偏白
                float centerBrightness = 1.0 - abs(uv.y - 0.5) * 2.0;
                glowColor = lerp(glowColor, float3(1, 1, 1), centerBrightness * 0.3);
                
                // 组合
                float3 finalColor = glowColor * expandedGlow + texColor.rgb * alphaMask;
                float finalAlpha = alphaMask + expandedGlow * 0.5;
                
                return fixed4(finalColor, saturate(finalAlpha));
            }
            ENDCG
        }
    }
}