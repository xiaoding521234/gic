// 战场草地小草摆动 shader（B1 战场格子草簇交叉面）
// 自包含无场景灯依赖；顶点 sin 摆动，根锚定（uv.y=0 为根部），世界坐标相位打散同步感
// Tuanjie ShaderLab 注意：属性默认值不得带引号，Header 只能 ASCII 无引号（docs/19 实证）
Shader "GIC/Battle/GrassSway"
{
    Properties
    {
        _MainTex ("草簇贴图", 2D) = "white" {}
        _BaseColor ("颜色叠加", Color) = (1, 1, 1, 1)
        _Cutoff ("透明剔除阈值", Range(0, 1)) = 0.42
        _SwayStrength ("摆动幅度", Float) = 0.05
        _SwaySpeed ("摆动速度", Float) = 1.7
        _PhaseScale ("相位打散", Float) = 2.4
        _BaseDarken ("根部压暗", Range(0, 1)) = 0.3
    }

    SubShader
    {
        Tags
        {
            "Queue"="AlphaTest"
            "RenderType"="TransparentCutout"
            "IgnoreProjector"="True"
        }

        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _BaseColor;
            float _Cutoff;
            float _SwayStrength;
            float _SwaySpeed;
            float _PhaseScale;
            float _BaseDarken;

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float height : TEXCOORD1;
            };

            v2f vert(appdata v)
            {
                v2f o;
                float3 worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;

                // 世界坐标相位（每簇草错开；同簇交叉面同步）
                float phase = worldPos.x * 1.3 + worldPos.z * 1.7
                            + sin(worldPos.x * _PhaseScale + worldPos.z * _PhaseScale * 0.57) * 2.7;
                float sway = sin(_Time.y * _SwaySpeed + phase);
                float swaySide = cos(_Time.y * _SwaySpeed * 0.83 + phase * 1.31);

                // 根部锚定：uv.y=0 不动，顶端全幅摆动
                float amount = v.uv.y * v.uv.y * _SwayStrength;
                v.vertex.x += sway * amount;
                v.vertex.z += swaySide * amount * 0.6;

                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.height = v.uv.y;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 tex = tex2D(_MainTex, i.uv);
                clip(tex.a - _Cutoff);

                fixed4 col = tex * _BaseColor;
                // 根部压暗增加插进地里的体积感
                col.rgb *= lerp(1.0 - _BaseDarken, 1.0, i.height);
                return col;
            }
            ENDCG
        }
    }
}
