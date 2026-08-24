Shader "Hidden/GIC/PaimonShadowComposite"
{
    // 派蒙桌宠投影阴影·合成 pass：主相机正前方全屏 Quad，Background 队列最先绘制，
    // 采样模糊后的剪影 RT（UV 平移=屏幕像素偏移），本体 Opaque 后画自然盖住重叠区。
    // _OffsetUV 由 PaimonDropShadowController 按 阴影偏移像素/屏幕分辨率 每帧写入。
    Properties
    {
        _MainTex ("Shadow Map", 2D) = "black" {}
        _Color ("Tint", Color) = (0, 0, 0, 0.45)
        _OffsetUV ("Offset UV", Vector) = (0, 0, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Background+10"
            "RenderType"="Transparent"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "COMPOSITE"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Off
            ZWrite Off
            ZTest Always
            // alpha 通道用 One Zero 直写：Blend SrcAlpha OneMinusSrcAlpha 单写法会把 alpha 同样平方
            //（A_out = A_src×A_src，dst 起始为 0），0.45 浓度实际写入 swapchain 只剩 ~0.2——
            // DWM 合成后影子过淡的根因（2026-08-24）。rgb 侧维持原混合（黑影两解释下视觉一致）。
            Blend SrcAlpha OneMinusSrcAlpha, One Zero

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _OffsetUV;

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

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv - _OffsetUV.xy;
                // RT 边缘 Clamp 寻址防拉出 streak：界外 alpha 置 0
                float inBounds = (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) ? 1.0 : 0.0;
                float a = tex2D(_MainTex, uv).a * inBounds;
                return fixed4(_Color.rgb, _Color.a * a);
            }
            ENDCG
        }
    }
}
