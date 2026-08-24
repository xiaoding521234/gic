Shader "Hidden/GIC/PaimonShadowBlur"
{
    // 派蒙桌宠投影阴影·分离高斯模糊（Pass0 水平 / Pass1 垂直，17-tap）。
    // 仅供 PaimonDropShadowController 对剪影 RT 做 Graphics.Blit（每帧 H+V 共两轮），不直接挂网格。
    // _BlurRadius 单位=阴影贴图 texel（单轮模糊范围），_TexelSize=(1/w,1/h) 由脚本每帧写入。
    // 2026-08-24 消"格子感"：旧 9-tap 间距=radius/4（≈3.5 texel），硬边剪影卷积出平台状阶梯——
    // 相邻 tap 之间 alpha 恒定、tap 位置跳变最高 23%，影子轮廓呈多层格子。改为 17-tap 间距=radius/8
    // （权重 exp(-k²/32) 归一化、与半径无关）+ 控制器跑两轮（每轮半径×0.66，方差相加=旧单轮 σ）。
    Properties
    {
        _MainTex ("Texture", 2D) = "black" {}
        _BlurRadius ("Blur Radius", Float) = 12
        _TexelSize ("Texel Size", Vector) = (0.002, 0.002, 0, 0)
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        CGINCLUDE
        #include "UnityCG.cginc"

        sampler2D _MainTex;
        float _BlurRadius;
        float4 _TexelSize;

        // 17-tap 高斯权重：exp(-k²/32) 归一化（k=0..8 对称取用，总和恰为 1）——
        // 权重形状与 _BlurRadius 无关（半径只拉伸 tap 间距 step=radius/8），任意半径下采样密度恒定
        static const float W9[9] = { 0.10308, 0.09998, 0.09104, 0.07787, 0.06257, 0.04724, 0.03349, 0.02231, 0.01396 };

        struct appdata
        {
            float4 vertex : POSITION;
            float2 uv : TEXCOORD0;
        };

        struct v2f
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        v2f vert (appdata v)
        {
            v2f o;
            o.pos = UnityObjectToClipPos(v.vertex);
            o.uv = v.uv;
            return o;
        }

        fixed4 fragBlur (v2f i, float2 dir) : SV_Target
        {
            float step = max(_BlurRadius, 0.0) / 8.0;
            float2 off = dir * _TexelSize.xy * step;
            float4 c = tex2D(_MainTex, i.uv) * W9[0];
            [unroll] for (int k = 1; k < 9; k++)
            {
                c += tex2D(_MainTex, i.uv + off * k) * W9[k];
                c += tex2D(_MainTex, i.uv - off * k) * W9[k];
            }
            return c;
        }
        ENDCG

        Pass
        {
            Name "BLUR_H"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragH
            fixed4 fragH (v2f i) : SV_Target { return fragBlur(i, float2(1, 0)); }
            ENDCG
        }

        Pass
        {
            Name "BLUR_V"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment fragV
            fixed4 fragV (v2f i) : SV_Target { return fragBlur(i, float2(0, 1)); }
            ENDCG
        }
    }
}
