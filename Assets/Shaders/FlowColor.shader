Shader "Custom/FlowColor"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Speed ("Flow Speed", Float) = 0.5
        _Intensity ("Color Intensity", Range(0,1)) = 0.8
        _Frequency ("Pattern Frequency", Float) = 3.0
        
        [HideInInspector] _RendererColor ("RendererColor", Color) = (1,1,1,1)
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

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha  // ← 改这里：标准Alpha混合

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f
            {
                float4 vertex   : SV_POSITION;
                fixed4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
            };

            sampler2D _MainTex;
            fixed4 _Color;
            float _Speed;
            float _Intensity;
            float _Frequency;

            // HSV转RGB辅助函数
            float3 HSVtoRGB(float3 HSV)
            {
                float3 RGB = 0;
                float C = HSV.z * HSV.y;
                float HPrime = fmod(HSV.x / 60, 6);
                float X = C * (1 - abs(fmod(HPrime, 2) - 1));
                float M = HSV.z - C;

                if(0 <= HPrime && HPrime < 1)
                    RGB = float3(C, X, 0);
                else if(1 <= HPrime && HPrime < 2)
                    RGB = float3(X, C, 0);
                else if(2 <= HPrime && HPrime < 3)
                    RGB = float3(0, C, X);
                else if(3 <= HPrime && HPrime < 4)
                    RGB = float3(0, X, C);
                else if(4 <= HPrime && HPrime < 5)
                    RGB = float3(X, 0, C);
                else if(5 <= HPrime && HPrime < 6)
                    RGB = float3(C, 0, X);

                return RGB + M;
            }

            v2f vert (appdata_t v)
            {
                v2f OUT;
                OUT.vertex = UnityObjectToClipPos(v.vertex);
                OUT.texcoord = v.texcoord;
                OUT.color = v.color * _Color;
                return OUT;
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                // 采样原始纹理
                fixed4 texColor = tex2D(_MainTex, IN.texcoord);
                
                // 如果该像素完全透明，直接返回透明（关键优化）
                if (texColor.a < 0.01)
                    return fixed4(0, 0, 0, 0);
                
                // 创建流动效果
                float flowValue = IN.texcoord.x * _Frequency + _Time.y * _Speed;
                
                // 使用HSV产生丰富的色彩
                float hue = fmod(flowValue, 1.0);
                float3 hsvColor = float3(hue * 360, 0.8, 1.0);
                float3 rgbColor = HSVtoRGB(hsvColor);
                
                // 混合原图和流彩色
                fixed4 finalColor;
                finalColor.rgb = lerp(texColor.rgb, rgbColor, _Intensity);
                finalColor.a = texColor.a * IN.color.a;  // 保持原始透明度
                
                return finalColor;
            }
            ENDCG
        }
    }
}