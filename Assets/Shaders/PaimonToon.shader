Shader "GIC/PaimonToon"
{
    // 派蒙原神风格卡渲（docs/19 桌宠渲染）：
    // 完全自包含无场景灯依赖——虚拟光方向为材质属性，无灯场景模型保持贴图原生亮度。
    // Pass1 本体（两条路径）：
    //   身体路径：半兰伯特 × 官方 ShadowRamp 行采样（Ramp 是 1D 颜色查找表，与模型 UV 无关）。
    //     ramp 行选择依据官方 ILM 数据（Body_Lightmap R 通道=材质ID，皮肤 texel R=1.0/白布 R=0）
    //     × 官方公式 rampV = ilm.r*0.5+0.25 → 皮肤 v=0.75（暖光紫影带）、布料 v=0.25（薰衣草带）。
    //   面部路径（_FACESHADOW）：原神 SDF 面部阴影——官方 Face_Lightmap R 通道（左暗右亮渐变，
    //     鼻梁轮廓带）与 MMD 脸 UV 完全对位（纹理相关性实测 1.000）。光在右侧→阴影从左脸扫入；
    //     光在左侧→镜像采样。亮/暗色取与皮肤同一 ramp 行（面部与身体色调连续）。
    // Pass2 描边：视图空间法线外扩 + 距离补偿（经典壳描边）。
    // 注意：Tuanjie ShaderLab 属性解析器不支持属性值引号/中文（实测 Parse error），Header 只能 ASCII 无引号。
    // Face/Hair 官方 Lightmap 其余通道（GI 眼影/金属等）依赖完整 GI 材质管线，一期不接入；
    // 披风/表情贴图是改模者自绘布局（相关性 0.1），不接入官方图。
    Properties
    {
        [Header(Albedo)]
        _BaseMap ("主贴图", 2D) = "white" {}
        _BaseColor ("主色叠加", Color) = (1, 1, 1, 1)
        _Cutoff ("透明剔除阈值", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("剔除模式", Float) = 2

        [Header(ToonShadow)]
        _Ramp ("Ramp 贴图", 2D) = "white" {}
        _RampV ("Ramp 行 (V 坐标)", Range(0, 1)) = 0.75
        _ShadowThreshold ("阴影阈值 (半兰伯特)", Range(0, 1)) = 0.55
        _ShadowSoftness ("阴影过渡宽度", Range(0.01, 0.5)) = 0.15
        _ShadowStrength ("阴影强度 (0=全亮)", Range(0, 1)) = 1
        _LightDir ("虚拟光方向 (世界)", Vector) = (0.4, 0.65, 0.65, 0)

        [Header(FaceSDF)]
        [Toggle(_FACESHADOW)] _FaceShadowOn ("启用面部SDF阴影", Float) = 0
        _FaceSDFMap ("面部SDF阴影图", 2D) = "white" {}
        _FaceShadowRange ("面部阴影范围", Range(0, 1)) = 1
        _FaceShadowSoftness ("面部阴影边界柔化", Range(0.001, 0.3)) = 0.04

        [Header(Outline)]
        _OutlineWidth ("描边宽度", Range(0, 3)) = 1
        _OutlineColor ("描边颜色", Color) = (0.05, 0.06, 0.13, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue"="Geometry"
            "RenderType"="Opaque"
        }

        // ---------- Pass 1: 本体 ----------
        Pass
        {
            Name "TOON"
            Cull [_Cull]
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature _FACESHADOW
            #include "UnityCG.cginc"

            sampler2D _BaseMap;
            float4 _BaseMap_ST;
            fixed4 _BaseColor;
            float _Cutoff;
            sampler2D _Ramp;
            float _RampV;
            float _ShadowThreshold;
            float _ShadowSoftness;
            float _ShadowStrength;
            float4 _LightDir;
#ifdef _FACESHADOW
            sampler2D _FaceSDFMap;
            float _FaceShadowRange;
            float _FaceShadowSoftness;
#endif

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 worldNormal : TEXCOORD1;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 baseCol = tex2D(_BaseMap, i.uv) * _BaseColor;
                clip(baseCol.a - _Cutoff);

                float3 L = normalize(_LightDir.xyz);
                float3 rampCol;
#ifdef _FACESHADOW
                // 原神 SDF 面部阴影：图高UV区亮=模型左脸(-X)（实测 corr=-0.983）。绑定姿态正对 +Z，世界光≈头部局部光。
                // 光从右(L.x>0)→镜像采样使暗侧对准左脸；光从左→不镜像（暗侧天然在右脸）。
                float2 suv = i.uv;
                if (L.x >= 0.0) suv.x = 1.0 - suv.x;
                float sdf = tex2D(_FaceSDFMap, suv).r;
                float t = saturate(abs(L.x)) * _FaceShadowRange;
                float litMask = smoothstep(t - _FaceShadowSoftness, t + _FaceShadowSoftness, sdf);
                rampCol = tex2D(_Ramp, float2(lerp(0.02, 0.98, litMask), _RampV)).rgb;
#else
                float3 N = normalize(i.worldNormal);
                float halfLambert = dot(N, L) * 0.5 + 0.5;
                float s = smoothstep(_ShadowThreshold - _ShadowSoftness, _ShadowThreshold + _ShadowSoftness, halfLambert);
                rampCol = tex2D(_Ramp, float2(lerp(0.02, 0.98, s), _RampV)).rgb;
#endif
                float3 lightMod = lerp(float3(1, 1, 1), rampCol, _ShadowStrength);
                return fixed4(baseCol.rgb * lightMod, baseCol.a);
            }
            ENDCG
        }

        // ---------- Pass 2: 描边 (背面壳外扩) ----------
        Pass
        {
            Name "OUTLINE"
            Cull Front
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            float _OutlineWidth;
            fixed4 _OutlineColor;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
            };

            v2f vert (appdata v)
            {
                v2f o;
                float3 viewPos = mul(UNITY_MATRIX_MV, v.vertex).xyz;
                float3 viewNormal = normalize(mul((float3x3)UNITY_MATRIX_IT_MV, v.normal));
                float dist = clamp(-viewPos.z, 0.3, 3.0); // 摄像机距离补偿：近处不粗远处不细
                viewPos += viewNormal * _OutlineWidth * 0.0012 * dist;
                o.pos = mul(UNITY_MATRIX_P, float4(viewPos, 1));
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                return _OutlineColor;
            }
            ENDCG
        }
    }
}
