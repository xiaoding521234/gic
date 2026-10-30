// Shader targeted for low end devices. Single Pass Forward Rendering.
Shader "GIC/Battle/GIBuilding"
{
    // Keep properties of StandardSpecular shader for upgrade reasons.
    Properties
    {
        [MainTexture] _BaseMap("Base Map (RGB) Smoothness / Alpha (A)", 2D) = "white" {}
        [MainColor]   _BaseColor("Base Color", Color) = (1, 1, 1, 1)

        _Cutoff("Alpha Clipping", Range(0.0, 1.0)) = 0.5

        _Smoothness("Smoothness", Range(0.0, 1.0)) = 0.5
        _SpecColor("Specular Color", Color) = (0.5, 0.5, 0.5, 0.5)
        _SpecGlossMap("Specular Map", 2D) = "white" {}
        _SmoothnessSource("Smoothness Source", Float) = 0.0
        _SpecularHighlights("Specular Highlights", Float) = 1.0

        [HideInInspector] _BumpScale("Scale", Float) = 1.0
        [NoScaleOffset] _BumpMap("Normal Map", 2D) = "bump" {}

        [HDR] _EmissionColor("Emission Color", Color) = (0,0,0)
        [NoScaleOffset]_EmissionMap("Emission Map", 2D) = "white" {}

        // Blending state
        _Surface("__surface", Float) = 0.0
        _Blend("__blend", Float) = 0.0
        _Cull("__cull", Float) = 2.0
        [ToggleUI] _AlphaClip("__clip", Float) = 0.0
        [HideInInspector] _SrcBlend("__src", Float) = 1.0
        [HideInInspector] _DstBlend("__dst", Float) = 0.0
        [HideInInspector] _SrcBlendAlpha("__srcA", Float) = 1.0
        [HideInInspector] _DstBlendAlpha("__dstA", Float) = 0.0
        [HideInInspector] _ZWrite("__zw", Float) = 1.0
        [HideInInspector] _BlendModePreserveSpecular("_BlendModePreserveSpecular", Float) = 1.0
        [HideInInspector] _AlphaToMask("__alphaToMask", Float) = 0.0

        [ToggleUI] _ReceiveShadows("Receive Shadows", Float) = 1.0
        // Editmode props
        _QueueOffset("Queue offset", Float) = 0.0

        // ObsoleteProperties
        [HideInInspector] _MainTex("BaseMap", 2D) = "white" {}
        [HideInInspector] _Color("Base Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _Shininess("Smoothness", Float) = 0.0
        [HideInInspector] _GlossinessSource("GlossinessSource", Float) = 0.0
        [HideInInspector] _SpecSource("SpecularHighlights", Float) = 0.0

        [HideInInspector][NoScaleOffset]unity_Lightmaps("unity_Lightmaps", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_LightmapsInd("unity_LightmapsInd", 2DArray) = "" {}
        [HideInInspector][NoScaleOffset]unity_ShadowMasks("unity_ShadowMasks", 2DArray) = "" {}
        [Header(GIBuilding)]
        _DetailMask ("Main SMBE", 2D) = "black" {}
        [Toggle(_SECOND_TEXTURE)] _EnableSecondTex ("Secondary enabled", Float) = 0
        _SecondaryTex ("Secondary diffuse", 2D) = "white" {}
        _SecondaryBumpMap ("Secondary normal", 2D) = "bump" {}
        _SecondaryDetailMask ("Secondary SMBE", 2D) = "black" {}
        [Toggle] _InvertBlend ("Invert secondary weight", Float) = 0
        _DiffuseBlendIntensity ("Diffuse blend", Range(0,1)) = 1
        _NormalBlendIntensity ("Normal blend", Range(0,1)) = 1
        [Toggle(_POM)] _EnablePOM ("POM enabled", Float) = 0
        _POMHeightMap ("POM height", 2D) = "black" {}
        _POMHeight ("POM height scale", Float) = 0
        _EmissionType ("Emission type", Float) = 0
        _EmissionStrength ("Emission strength", Float) = 1
        _NightEmission ("Night emission", Range(0,1)) = 1
        _RGColor ("RG color", Color) = (1,1,1,1)
        _RGStrength ("RG strength", Float) = 0
        _RGPower ("RG power", Float) = 1
        [Header(Local Enhancement)]
        _FacadeFillStrength ("Facade fill strength", Range(0,0.5)) = 0.16
        _FacadeFillColor ("Facade fill color", Color) = (1,0.96,0.88,1)
        [HideInInspector] _FacadeFillDirection ("Facade fill direction", Vector) = (0,0,-1,0)
        _NightSpillStrength ("Night spill strength", Range(0,1)) = 0.22
        _NightSpillColor ("Night spill color", Color) = (1,0.71353996,0.3098039,1)
    }

    // HLSLINCLUDE
    // #pragma enable_d3d11_debug_symbols
    // ENDHLSL

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "UniversalMaterialType" = "SimpleLit"
            "IgnoreProjector" = "True"
        }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags
            {
                "LightMode" = "UniversalForward"
            }

            // -------------------------------------
            // Render State Commands
            // Use same blending / depth states as Standard shader
            Blend[_SrcBlend][_DstBlend], [_SrcBlendAlpha][_DstBlendAlpha]
            ZWrite[_ZWrite]
            Cull[_Cull]
            AlphaToMask[_AlphaToMask]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex LitPassVertexSimple
            #pragma fragment GIBuildingFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _SECOND_TEXTURE
            #pragma shader_feature_local_fragment _POM
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _ALPHAPREMULTIPLY_ON _ALPHAMODULATE_ON
            #pragma shader_feature_local_fragment _ _SPECGLOSSMAP _SPECULAR_COLOR
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ EVALUATE_SH_MIXED EVALUATE_SH_VERTEX
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ _CLUSTERED_LIGHTING
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            // APS Keywords - all use shader_feature for faster editor compilation
            // APS Keywords - use include_with_pragmas for Editor/Build dual-mode
            // Editor: shader_feature for faster compilation
            // Build: multi_compile for correct ShaderKeywordFilter control
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/APSKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.core/ShaderLibrary/FoveatedRenderingKeywords.hlsl"
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fog
            #pragma multi_compile_fragment _ DEBUG_DISPLAY
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ USE_LIGHTMAP_SINGLE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma multi_compile _ ENABLE_VS_SKINNING

            //--------------------------------------
            // Defines
            #define BUMP_SCALE_NOT_SUPPORTED 1

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
            #include "GIBuildingForwardPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags
            {
                "LightMode" = "ShadowCaster"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            // This is used during shadow map generation to differentiate between directional and punctual light shadows, as they use different formulas to apply Normal Bias
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #pragma multi_compile _ ENABLE_VS_SKINNING

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "GBuffer"
            Tags
            {
                "LightMode" = "UniversalGBuffer"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite[_ZWrite]
            ZTest LEqual
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 4.5

            // Deferred Rendering Path does not support the OpenGL-based graphics API:
            // Desktop OpenGL, OpenGL ES 3.0, WebGL 2.0.
            #pragma exclude_renderers gles3 glcore

            // -------------------------------------
            // Shader Stages
            #pragma vertex LitPassVertexSimple
            #pragma fragment LitPassFragmentSimple

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            //#pragma shader_feature _ALPHAPREMULTIPLY_ON
            #pragma shader_feature_local_fragment _ _SPECGLOSSMAP _SPECULAR_COLOR
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _SECOND_TEXTURE
            #pragma shader_feature_local_fragment _POM
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            //#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            //#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE
            #pragma multi_compile _ USE_LIGHTMAP_SINGLE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options renderinglayer
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            //--------------------------------------
            // Defines
            #define BUMP_SCALE_NOT_SUPPORTED 1

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitGBufferPass.hlsl"
            ENDHLSL
        }

        Pass
        {
            // Lightmode matches the ShaderPassName set in UniversalRenderPipeline.cs. SRPDefaultUnlit and passes with
            // no LightMode tag are also rendered by Universal Render Pipeline
            Name "GBufferSplat"
            Tags
            {
                "LightMode" = "UniversalGBufferSplat"
            }

            Cull Back
            ZWrite Off
            ZTest Equal

            HLSLPROGRAM
            #pragma target 4.5

            #pragma only_renderers d3d11 vulkan metal
            //#pragma enable_d3d11_debug_symbols

            #pragma vertex SimpleLitGBufferSplatPassVertexSimple
            #pragma fragment SimpleLitGBufferSplatPassFragmentSimple

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            // #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ _SPECGLOSSMAP _SPECULAR_COLOR
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _SECOND_TEXTURE
            #pragma shader_feature_local_fragment _POM
            #pragma shader_feature_local _RECEIVE_SHADOWS_OFF

            // -------------------------------------
            // Universal Pipeline keywords
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _DBUFFER_MRT1 _DBUFFER_MRT2 _DBUFFER_MRT3
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            // #pragma multi_compile _ DYNAMICLIGHTMAP_ON
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma multi_compile_fragment _ _RENDER_PASS_ENABLED

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #pragma instancing_options procedural : setup
            #pragma instancing_options renderinglayer
            // #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            //--------------------------------------
            // Defines
            #undef UNITY_GPU_DRIVEN_PIPELINE
            #define UNITY_GPU_DRIVEN_PIPELINE
            #define SIMPLE_LIT_GBUFFER_SPLAT

            // -------------------------------------
            // Includes
            #include_with_pragmas "GPUDataDecodeDefine.hlsl"
            #include "GPUDrivenCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/GBufferSplatProperties.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/ReflectionProbeProperties.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityGBuffer.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/VGUtilities.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitGBufferSplatPass.hlsl"

            #if defined(PROCEDURAL_INSTANCING_ON)
            void setup()
            {
            }
            #endif

            ENDHLSL
        }

        Pass
        {
            Name "VBuffer"
            Tags{ "LightMode" = "VBufferAlphaClip" } // This will be only for opaque object based on the RenderQueue index

			ZWrite On
			ZTest LEqual
			Blend Off

			Stencil
            {
                WriteMask [_StencilWriteMaskGBufferURP]
                Ref [_StencilRefGBufferURP]
                Comp Always
                Pass Replace
            }

            HLSLPROGRAM

			#pragma target 4.5
			#pragma only_renderers d3d11 vulkan metal
			#pragma multi_compile_instancing
			#pragma instancing_options procedural:setup
			//#pragma enable_d3d11_debug_symbols

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #undef UNITY_GPU_DRIVEN_PIPELINE
            #define UNITY_GPU_DRIVEN_PIPELINE
            #define GPU_ALPHA_CLIP_ON
            #define VARYINGS_NEED_TEXCOORD0

			#include_with_pragmas "GPUDataDecodeDefine.hlsl"
            #include "GPUDrivenCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInputGDRP.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"

			#include "GPUVisibleBucketCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitVisibilityPass.hlsl"

        #if defined(PROCEDURAL_INSTANCING_ON)
            void setup()
            {
            }
        #endif
            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL
        }

        pass
        {
            Name "MeshSelection"
            Tags{ "LightMode" = "AlphaMeshSelection" } // This will be only for opaque object based on the RenderQueue index

			Cull Off
            ZTest Off

           HLSLPROGRAM

			#pragma target 4.5
			#pragma only_renderers d3d11 vulkan metal
			#pragma multi_compile_instancing
			#pragma instancing_options procedural:setup
			//#pragma enable_d3d11_debug_symbols

            #pragma shader_feature_local_fragment _ALPHATEST_ON

            #undef UNITY_GPU_DRIVEN_PIPELINE
            #define UNITY_GPU_DRIVEN_PIPELINE
            #define GPU_ALPHA_CLIP_ON
            #define VARYINGS_NEED_TEXCOORD0

			#include_with_pragmas "GPUDataDecodeDefine.hlsl"
            #include "GPUDrivenCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInputGDRP.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"

            #include "GPUVisibleBucketCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/LitAlphaMeshSelectionPass.hlsl"

        #if defined(PROCEDURAL_INSTANCING_ON)
            void setup()
            {
            }
        #endif
            #pragma vertex Vert
            #pragma fragment Frag

            ENDHLSL

        }

        Pass
        {
            Name "VGShadowMapSingleView"
            Tags { "LightMode" = "VGShadowMapAlphaClip" }

            ZWrite On
            ZTest LEqual

            ColorMask 0

			HLSLPROGRAM

			#pragma target 4.5
			#pragma only_renderers d3d11 vulkan metal
			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
			#pragma shader_feature_local _ALPHATEST_ON

			#pragma instancing_options procedural:setup
			// #pragma enable_d3d11_debug_symbols


            #undef UNITY_GPU_DRIVEN_PIPELINE
            #define UNITY_GPU_DRIVEN_PIPELINE
			#undef MULTI_VIEW
            #define GPU_ALPHA_CLIP_ON
			#define VARYINGS_NEED_TEXCOORD0

			#include_with_pragmas "GPUDataDecodeDefine.hlsl"
			#include "GPUDrivenCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInputGDRP.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"

			#include "GPUVisibleBucketCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/VGShadowMap.hlsl"

		#if defined(PROCEDURAL_INSTANCING_ON)
            void setup()
            {

            }
        #endif

            #pragma vertex Vert
            #pragma fragment Frag

			ENDHLSL

        }

        Pass
        {
            Name "VGShadowMapMultiView"
            Tags { "LightMode" = "VGShadowMapAlphaClip" }

            ZWrite On
            ZTest LEqual

            ColorMask 0

			HLSLPROGRAM

			#pragma target 4.5
			#pragma only_renderers d3d11 vulkan metal
			#pragma multi_compile_instancing
			#pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
			#pragma shader_feature_local _ALPHATEST_ON

			#pragma instancing_options procedural:setup
			// #pragma enable_d3d11_debug_symbols


            #undef UNITY_GPU_DRIVEN_PIPELINE
            #define UNITY_GPU_DRIVEN_PIPELINE
			#undef MULTI_VIEW
			#define MULTI_VIEW 1
            #define GPU_ALPHA_CLIP_ON
			#define VARYINGS_NEED_TEXCOORD0

			#include_with_pragmas "GPUDataDecodeDefine.hlsl"
			#include "GPUDrivenCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInputGDRP.hlsl"
	        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"

			#include "GPUVisibleBucketCommon.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/Shaders/VGShadowMap.hlsl"

		#if defined(PROCEDURAL_INSTANCING_ON)
            void setup()
            {

            }
        #endif

            #pragma vertex Vert
            #pragma fragment Frag

			ENDHLSL

        }

        Pass
        {
            Name "DepthOnly"
            Tags
            {
                "LightMode" = "DepthOnly"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            ColorMask R
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma multi_compile _ ENABLE_VS_SKINNING

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthOnlyPass.hlsl"
            ENDHLSL
        }

        // This pass is used when drawing to a _CameraNormalsTexture texture
        Pass
        {
            Name "DepthNormals"
            Tags
            {
                "LightMode" = "DepthNormals"
            }

            // -------------------------------------
            // Render State Commands
            ZWrite On
            Cull[_Cull]

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local _NORMALMAP
            #pragma shader_feature_local _ALPHATEST_ON
            #pragma shader_feature_local_fragment _GLOSSINESS_FROM_BASE_ALPHA

            // -------------------------------------
            // Unity defined keywords
            #pragma multi_compile_fragment _ LOD_FADE_CROSSFADE

            // Universal Pipeline keywords
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            //--------------------------------------
            // GPU Instancing
            #pragma multi_compile_instancing
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DOTS.hlsl"

            #pragma multi_compile _ ENABLE_VS_SKINNING

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitDepthNormalsPass.hlsl"
            ENDHLSL
        }

        // This pass it not used during regular rendering, only for lightmap baking.
        Pass
        {
            Name "Meta"
            Tags
            {
                "LightMode" = "Meta"
            }

            // -------------------------------------
            // Render State Commands
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex UniversalVertexMeta
            #pragma fragment UniversalFragmentMetaSimple

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local_fragment _EMISSION
            #pragma shader_feature_local_fragment _SECOND_TEXTURE
            #pragma shader_feature_local_fragment _POM
            #pragma shader_feature_local_fragment _SPECGLOSSMAP
            #pragma shader_feature EDITOR_VISUALIZATION

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitMetaPass.hlsl"

            ENDHLSL
        }

        Pass
        {
            Name "Universal2D"
            Tags
            {
                "LightMode" = "Universal2D"
                "RenderType" = "Transparent"
                "Queue" = "Transparent"
            }

            HLSLPROGRAM
            #pragma target 2.0

            // -------------------------------------
            // Shader Stages
            #pragma vertex vert
            #pragma fragment frag

            // -------------------------------------
            // Material Keywords
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _ALPHAPREMULTIPLY_ON

            // -------------------------------------
            // Includes
            #include "Packages/com.unity.render-pipelines.universal/Shaders/SimpleLitInput.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/Utils/Universal2D.hlsl"
            ENDHLSL
        }
    }

    Fallback  "Hidden/Universal Render Pipeline/FallbackError"
    CustomEditor "UnityEditor.Rendering.Universal.ShaderGUI.SimpleLitShader"
}
