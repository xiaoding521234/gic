Shader "GIC/PaimonShadowSilhouette"
{
    // 派蒙桌宠投影阴影·剪影 pass（docs/19 §5.10 高斯投影方案）：
    //   _DropShadow 影子壳（共享本体骨骼的复制 SMR）挂本材质，仅被影子相机渲到小 RT。
    //   输出纯黑 alpha=1 剪影，供后续高斯模糊 + 偏移合成（CSS drop-shadow 同款管线）。
    // 无属性可调——浓度/偏移/模糊半径全部在 PaimonDropShadowController 组件上（Inspector 中文）。
    Properties
    {
    }

    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "IgnoreProjector"="True"
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "SILHOUETTE"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Back
            ZWrite On
            ZTest LEqual

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
            };

            float4 vert (appdata v) : SV_POSITION
            {
                return UnityObjectToClipPos(v.vertex);
            }

            fixed4 frag () : SV_Target
            {
                return fixed4(0, 0, 0, 1);
            }
            ENDCG
        }
    }
}
