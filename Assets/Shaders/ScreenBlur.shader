Shader "Hidden/ScreenBlur"
{
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "KawaseBlur"
            ZWrite Off ZTest Always Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BlitTexture);
            SAMPLER(sampler_LinearClamp);

            float4 _BlitTexture_TexelSize;
            float  _BlurSize;   // 当前迭代偏移量（由 C# 端计算传入）

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // DrawProcedural 不提供顶点属性，用 SV_VertexID 生成全屏三角形
            Varyings vert(uint vertexID : SV_VertexID)
            {
                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float2 pos = float2((vertexID << 1) & 2, vertexID & 2) * 2.0 - 1.0;
                output.positionCS = float4(pos, 0.0, 1.0);
                output.uv = pos * 0.5 + 0.5;
                output.uv.y = 1.0 - output.uv.y;
                return output;
            }

            // Kawase 模糊：5-tap（中心 + 四角对角采样）
            // 每次迭代偏移量递增，多次迭代等效高斯
            half4 frag(Varyings input) : SV_Target
            {
                float2 texel = _BlitTexture_TexelSize.xy;
                float2 offset = texel * _BlurSize;

                half4 col = 0;
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv);
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv + float2(-offset.x, -offset.y));
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv + float2( offset.x, -offset.y));
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv + float2(-offset.x,  offset.y));
                col += SAMPLE_TEXTURE2D(_BlitTexture, sampler_LinearClamp, input.uv + float2( offset.x,  offset.y));
                col *= 0.2;

                return col;
            }
            ENDHLSL
        }
    }
}
