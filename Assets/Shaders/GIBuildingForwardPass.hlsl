#ifndef UNIVERSAL_SIMPLE_LIT_PASS_INCLUDED
#define UNIVERSAL_SIMPLE_LIT_PASS_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#if defined(LOD_FADE_CROSSFADE)
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/LODCrossFade.hlsl"
#endif

struct Attributes
{
    float4 positionOS    : POSITION;
    float3 normalOS      : NORMAL;
    float4 tangentOS     : TANGENT;
    float2 texcoord      : TEXCOORD0;
    float2 staticLightmapUV    : TEXCOORD1;
    float2 dynamicLightmapUV    : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct Varyings
{
    float2 uv                       : TEXCOORD0;

    float3 positionWS                  : TEXCOORD1;    // xyz: posWS

    #ifdef _NORMALMAP
        half4 normalWS                 : TEXCOORD2;    // xyz: normal, w: viewDir.x
        half4 tangentWS                : TEXCOORD3;    // xyz: tangent, w: viewDir.y
        half4 bitangentWS              : TEXCOORD4;    // xyz: bitangent, w: viewDir.z
    #else
        half3  normalWS                : TEXCOORD2;
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        half4 fogFactorAndVertexLight  : TEXCOORD5; // x: fogFactor, yzw: vertex light
    #else
        half  fogFactor                 : TEXCOORD5;
    #endif

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        float4 shadowCoord             : TEXCOORD6;
    #endif

    DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 7);

#ifdef DYNAMICLIGHTMAP_ON
    float2  dynamicLightmapUV : TEXCOORD8; // Dynamic lightmap UVs
#endif

    float4 positionCS                  : SV_POSITION;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

void InitializeInputData(Varyings input, half3 normalTS, out InputData inputData)
{
    inputData = (InputData)0;

    inputData.positionWS = input.positionWS;

    #ifdef _NORMALMAP
        half3 viewDirWS = half3(input.normalWS.w, input.tangentWS.w, input.bitangentWS.w);
        inputData.tangentToWorld = half3x3(input.tangentWS.xyz, input.bitangentWS.xyz, input.normalWS.xyz);
        inputData.normalWS = TransformTangentToWorld(normalTS, inputData.tangentToWorld);
    #else
        half3 viewDirWS = GetWorldSpaceNormalizeViewDir(inputData.positionWS);
        inputData.normalWS = input.normalWS;
    #endif

    inputData.normalWS = NormalizeNormalPerPixel(inputData.normalWS);
    viewDirWS = SafeNormalize(viewDirWS);

    inputData.viewDirectionWS = viewDirWS;

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        inputData.shadowCoord = input.shadowCoord;
    #elif defined(MAIN_LIGHT_CALCULATE_SHADOWS)
        inputData.shadowCoord = TransformWorldToShadowCoord(inputData.positionWS);
    #else
        inputData.shadowCoord = float4(0, 0, 0, 0);
    #endif

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        inputData.fogCoord = InitializeInputDataFog(float4(inputData.positionWS, 1.0), input.fogFactorAndVertexLight.x);
        inputData.vertexLighting = input.fogFactorAndVertexLight.yzw;
    #else
        inputData.fogCoord = InitializeInputDataFog(float4(inputData.positionWS, 1.0), input.fogFactor);
        inputData.vertexLighting = half3(0, 0, 0);
    #endif
    
    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
    inputData.shadowMask = SAMPLE_SHADOWMASK(input.staticLightmapUV);

    #if defined(DYNAMICLIGHTMAP_ON)
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.dynamicLightmapUV, input.vertexSH, inputData.normalWS);
    #elif (!defined(DIRLIGHTMAP_COMBINED) && !defined(LIGHTMAP_ON) && defined(USE_PROBE_SYSTEM) )
    inputData.bakedGI = SAMPLE_GI(inputData.positionWS, inputData.normalWS, inputData.normalizedScreenSpaceUV, inputData.shadowMask);
    #else
    inputData.bakedGI = SAMPLE_GI(input.staticLightmapUV, input.vertexSH, inputData.normalWS);
    #endif



    #if defined(DEBUG_DISPLAY)
    #if defined(DYNAMICLIGHTMAP_ON)
    inputData.dynamicLightmapUV = input.dynamicLightmapUV.xy;
    #endif
    #if defined(LIGHTMAP_ON)
    inputData.staticLightmapUV = input.staticLightmapUV;
    #else
    inputData.vertexSH = input.vertexSH;
    #endif
    #endif
}

///////////////////////////////////////////////////////////////////////////////
//                  Vertex and Fragment functions                            //
///////////////////////////////////////////////////////////////////////////////

// Used in Standard (Simple Lighting) shader
Varyings LitPassVertexSimple(Attributes input)
{
    Varyings output = (Varyings)0;

    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

    VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
    VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS, input.tangentOS);

#if defined(_FOG_FRAGMENT)
        half fogFactor = 0;
#else
        half fogFactor = ComputeFogFactor(vertexInput.positionCS.z);
#endif

    output.uv = TRANSFORM_TEX(input.texcoord, _BaseMap);
    output.positionWS.xyz = vertexInput.positionWS;
    output.positionCS = vertexInput.positionCS;

#ifdef _NORMALMAP
    half3 viewDirWS = GetWorldSpaceViewDir(vertexInput.positionWS);
    output.normalWS = half4(normalInput.normalWS, viewDirWS.x);
    output.tangentWS = half4(normalInput.tangentWS, viewDirWS.y);
    output.bitangentWS = half4(normalInput.bitangentWS, viewDirWS.z);
#else
    output.normalWS = NormalizeNormalPerVertex(normalInput.normalWS);
#endif

    OUTPUT_LIGHTMAP_UV(input.staticLightmapUV, unity_LightmapST, output.staticLightmapUV);
#ifdef DYNAMICLIGHTMAP_ON
    output.dynamicLightmapUV = input.dynamicLightmapUV.xy * unity_DynamicLightmapST.xy + unity_DynamicLightmapST.zw;
#endif
    OUTPUT_SH(output.normalWS.xyz, output.vertexSH);

    #ifdef _ADDITIONAL_LIGHTS_VERTEX
        half3 vertexLight = VertexLighting(vertexInput.positionWS, normalInput.normalWS);
        output.fogFactorAndVertexLight = half4(fogFactor, vertexLight);
    #else
        output.fogFactor = fogFactor;
    #endif

    #if defined(REQUIRES_VERTEX_SHADOW_COORD_INTERPOLATOR)
        output.shadowCoord = GetShadowCoord(vertexInput);
    #endif

    return output;
}

// Used for StandardSimpleLighting shader

TEXTURE2D(_DetailMask); SAMPLER(sampler_DetailMask);
TEXTURE2D(_SecondaryTex); SAMPLER(sampler_SecondaryTex);
TEXTURE2D(_SecondaryBumpMap); SAMPLER(sampler_SecondaryBumpMap);
TEXTURE2D(_SecondaryDetailMask); SAMPLER(sampler_SecondaryDetailMask);
TEXTURE2D(_POMHeightMap); SAMPLER(sampler_POMHeightMap);
float _InvertBlend;
float _DiffuseBlendIntensity;
float _NormalBlendIntensity;
float _POMHeight;
float _EmissionType;
float _EmissionStrength;
float _NightEmission;
float _RGStrength;
float _RGPower;
half4 _RGColor;
float _FacadeFillStrength;
half4 _FacadeFillColor;
float4 _FacadeFillDirection;
float _NightSpillStrength;
float _NightSpillCount;
half4 _NightSpillColor;
float4 _NightSpillCenters[8];
float4 _NightSpillExtents[8];

float2 ApplyGIBuildingParallax(float2 uv, Varyings input)
{
#if defined(_POM) && defined(_NORMALMAP)
    half3 normalWS = normalize(input.normalWS.xyz);
    half3 tangentWS = normalize(input.tangentWS.xyz);
    half3 bitangentWS = normalize(input.bitangentWS.xyz);
    half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
    half3 viewTS = normalize(half3(-dot(viewWS, tangentWS), -dot(viewWS, bitangentWS), -dot(viewWS, normalWS)));
    float2 direction = viewTS.xy / max(0.12, viewTS.z);
    float2 offset = direction * _POMHeight;
    float2 previousUV = uv;
    float previousHeight = 0;
    [unroll] for (int stepIndex = 1; stepIndex <= 12; stepIndex++)
    {
        previousUV = uv - offset * previousHeight;
        previousHeight = 1.0 - SAMPLE_TEXTURE2D(_POMHeightMap, sampler_POMHeightMap, previousUV).r;
    }
    float2 nextUV = uv - offset * previousHeight;
    float nextHeight = 1.0 - SAMPLE_TEXTURE2D(_POMHeightMap, sampler_POMHeightMap, nextUV).r;
    float afterHeight = previousHeight - stepIndex / 12.0;
    float beforeHeight = nextHeight - (stepIndex - 1) / 12.0;
    float weight = afterHeight / max(0.0001, afterHeight - beforeHeight);
    return lerp(previousUV, nextUV, saturate(weight));
#else
    return uv;
#endif
}

void ApplyGIBuildingInputs(float2 uv, inout SurfaceData surfaceData)
{
    half4 mainMask = SAMPLE_TEXTURE2D(_DetailMask, sampler_DetailMask, uv);
#if defined(_SECOND_TEXTURE)
    half weight = _InvertBlend > 0.5 ? 1.0 - mainMask.r : mainMask.r;
    half4 secondary = SAMPLE_TEXTURE2D(_SecondaryTex, sampler_SecondaryTex, uv);
    surfaceData.albedo = lerp(surfaceData.albedo, secondary.rgb * _BaseColor.rgb, weight * _DiffuseBlendIntensity);
    half3 secondaryNormal = SampleNormal(uv, TEXTURE2D_ARGS(_SecondaryBumpMap, sampler_SecondaryBumpMap));
    surfaceData.normalTS = normalize(lerp(surfaceData.normalTS, secondaryNormal, weight * _NormalBlendIntensity));
    half4 secondaryMask = SAMPLE_TEXTURE2D(_SecondaryDetailMask, sampler_SecondaryDetailMask, uv);
    surfaceData.specular = lerp(surfaceData.specular, secondaryMask.r * _SpecColor.rgb, weight);
    surfaceData.smoothness = lerp(surfaceData.smoothness, secondaryMask.g, weight);
#else
    surfaceData.specular *= mainMask.r;
    surfaceData.smoothness *= mainMask.g;
#endif
    if (_EmissionType > 0.5)
        surfaceData.emission = _EmissionColor.rgb * _EmissionStrength * _NightEmission * mainMask.a * _BaseColor.rgb;
}

half3 ApplyGIBuildingLocalEnhancements(Varyings input, InputData inputData, SurfaceData surfaceData)
{
    half3 normalWS = normalize(inputData.normalWS);
    half3 fillDirection = normalize(_FacadeFillDirection.xyz);
    half facade = dot(normalWS, fillDirection);
    half wrapped = saturate((facade + 0.6) / 1.6);
    half fillMask = smoothstep(-0.2, 0.5, facade);
    half3 addition = surfaceData.albedo * _FacadeFillColor.rgb * (_FacadeFillStrength * wrapped * fillMask);

    if (_NightEmission > 0.001 && _NightSpillStrength > 0.0)
    {
        float3 positionOS = TransformWorldToObject(input.positionWS);
        half spill = 0;
        [unroll] for (int sourceIndex = 0; sourceIndex < 8; sourceIndex++)
        {
            if (sourceIndex < (int)_NightSpillCount)
            {
                float3 extents = max(float3(0.001, 0.001, 0.001), _NightSpillExtents[sourceIndex].xyz);
                float3 delta = (positionOS - _NightSpillCenters[sourceIndex].xyz) / extents;
                half contribution = saturate(1.0 - length(delta));
                spill = max(spill, contribution * contribution);
            }
        }
        addition += surfaceData.albedo * _NightSpillColor.rgb * (_NightSpillStrength * spill * _NightEmission);
    }
    return addition;
}

void GIBuildingFragment(
    Varyings input
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out float4 outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surfaceData;
    InitializeSimpleLitSurfaceData(input.uv, surfaceData);
    float2 giUV = ApplyGIBuildingParallax(input.uv, input);
    ApplyGIBuildingInputs(giUV, surfaceData);

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, input.uv, _BaseMap);

#ifdef _DBUFFER
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif

    half4 color = UniversalFragmentBlinnPhong(inputData, surfaceData);
    color.rgb += ApplyGIBuildingLocalEnhancements(input, inputData, surfaceData);
    if (_RGStrength > 0) { half rim = pow(saturate(1.0 - dot(inputData.normalWS, inputData.viewDirectionWS)), max(0.05, 1.0 / _RGPower)); half lightSide = saturate(dot(inputData.normalWS, GetMainLight().direction)); color.rgb += _RGColor.rgb * (_RGStrength * rim * lightSide) * _BaseColor.rgb; }
    color.rgb = MixFog(color.rgb, inputData.fogCoord);
    color.a = OutputAlpha(color.a, IsSurfaceTypeTransparent(_Surface));

    outColor = color;

#ifdef _WRITE_RENDERING_LAYERS
    uint renderingLayers = GetMeshRenderingLayer();
    outRenderingLayers = float4(EncodeMeshRenderingLayer(renderingLayers), 0, 0, 0);
#endif
}

#endif
