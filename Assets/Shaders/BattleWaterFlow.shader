// 战场水面流动 shader（B1 战场格子水面）
// 半透明 + 双层 UV 反向滚动（流动感）+ 顶点起伏波纹；水下深色底面由网格提供（不透明）
// 自包含无场景灯依赖；URP 单 Pass（多 Pass 在 URP 下只执行第一个，docs/19 实证）
Shader "GIC/Battle/WaterFlow"
{
    Properties
    {
        _MainTex ("水面贴图", 2D) = "white" {}
        _BaseColor ("水色", Color) = (0.32, 0.74, 0.82, 0.86)
        _DeepColor ("深水色", Color) = (0.10, 0.32, 0.48, 1)
        _FlowSpeed ("流动速度", Float) = 0.045
        _FlowDirA ("流向A", Vector) = (1, 0.35, 0, 0)
        _FlowDirB ("流向B", Vector) = (-0.55, -1, 0, 0)
        _LayerBStrength ("层B强度", Range(0, 1)) = 0.4
        _WaveAmplitude ("表面起伏", Float) = 0.02
        _WaveSpeed ("起伏速度", Float) = 1.9
        _Highlight ("波峰高光", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
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
            float4 _MainTex_ST;
            fixed4 _BaseColor;
            fixed4 _DeepColor;
            float _FlowSpeed;
            float4 _FlowDirA;
            float4 _FlowDirB;
            float _LayerBStrength;
            float _WaveAmplitude;
            float _WaveSpeed;
            float _Highlight;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uvBase : TEXCOORD0;
                float wave : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // 表面起伏（绕 Y 旋转的格子 object.y == world.y，直接偏移安全）
                float wave = sin(_Time.y * _WaveSpeed + worldPos.x * 2.3 + worldPos.z * 1.9)
                           + 0.5 * sin(_Time.y * _WaveSpeed * 1.37 + worldPos.z * 3.1 - worldPos.x * 1.3);
                v.vertex.y += wave * _WaveAmplitude;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uvBase = TRANSFORM_TEX(v.uv, _MainTex);
                o.wave = wave;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 双层反向滚动 → 持续流动感
                half4 layerA = tex2D(_MainTex, i.uvBase + _Time.y * _FlowSpeed * _FlowDirA.xy);
                half4 layerB = tex2D(_MainTex, i.uvBase + _Time.y * _FlowSpeed * _FlowDirB.xy);
                half pattern = lerp(layerA.r, layerB.r, _LayerBStrength);

                // 基色 × 贴图亮度 + 波峰高光
                fixed3 col = _DeepColor.rgb + (_BaseColor.rgb - _DeepColor.rgb) * (0.35 + 0.65 * pattern);
                col += _Highlight * saturate(i.wave * 0.5 + 0.5) * pattern;

                fixed alpha = _BaseColor.a + pattern * 0.08;
                return fixed4(col, saturate(alpha));
            }
            ENDCG
        }
    }
}
