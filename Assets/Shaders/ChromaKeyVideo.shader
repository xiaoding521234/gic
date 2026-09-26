// 立牌循环动画视频 ChromaKey shader（B-S3 视频路线）
// 消费 VideoPlayer→RenderTexture 的绿幕画面，运行时抠色成透明底；
// 抠色阈值与离线抽帧管线（extract_build.py）同参：excess=g-max(r,b) 平滑带 [20/255,45/255] + 暗部亮度门 g>60/255，
// despill=边缘绿压回 max(r,b)（安柏主体零绿色素，不受影响）。
// _Color 承载 tint（尸体灰/冻结冰色/受击闪红，同 SpriteRenderer.color 语义，由 UnitView.RefreshTint 写入）。
Shader "GIC/Battle/ChromaKeyVideo"
{
    Properties
    {
        _MainTex ("视频帧 (RenderTexture)", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
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

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            fixed4 _Color;

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

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                half4 c = tex2D(_MainTex, i.uv);

                half maxRB = max(c.r, c.b);
                half excess = c.g - maxRB;
                half key = smoothstep(20.0 / 255.0, 45.0 / 255.0, excess);
                key *= step(60.0 / 255.0, c.g); // 暗部亮度门：模型画的深绿阴影不误抠

                c.g = min(c.g, maxRB); // despill

                half alpha = (1.0 - key) * _Color.a;
                return fixed4(c.rgb * _Color.rgb, alpha);
            }
            ENDCG
        }
    }
}
