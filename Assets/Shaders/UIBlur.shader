Shader "UI/Blur"
{
    Properties
    {
        _ScreenBlurTex ("Screen Blur", 2D) = "white" {}
        _Tint ("Tint Color", Color) = (0, 0, 0, 0.5)
        _Color ("Color", Color) = (1, 1, 1, 1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderPipeline" = "UniversalPipeline"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
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
        ColorMask [_ColorMask]

        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 全局纹理，由 ScreenBlurRendererFeature 每帧写入
            TEXTURE2D(_ScreenBlurTex);
            SAMPLER(sampler_ScreenBlurTex);

            float4 _Tint;
            float4 _Color;
            float4 _ClipRect;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.worldPosition = input.positionOS;
                // 用裁剪空间坐标计算屏幕 UV，不受 Sprite 切片 UV 影响
                // _ProjectionParams.x: -1=Game View(翻转投影), +1=Scene View(未翻转)
                output.uv = output.positionCS.xy * 0.5 + 0.5;
                output.uv.y = (_ProjectionParams.x < 0.0) ? 1.0 - output.uv.y : output.uv.y;
                output.color = input.color * _Color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                // 用屏幕空间 UV 采样模糊纹理
                half4 col = SAMPLE_TEXTURE2D(_ScreenBlurTex, sampler_ScreenBlurTex, input.uv);

                // 叠加 tint 颜色（变暗效果）
                col.rgb = lerp(col.rgb, _Tint.rgb, _Tint.a);
                col.a = 1.0;

                #ifdef UNITY_UI_CLIP_RECT
                    col.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                    clip(col.a - 0.001);
                #endif

                return col * input.color;
            }

            ENDHLSL
        }
    }
}
