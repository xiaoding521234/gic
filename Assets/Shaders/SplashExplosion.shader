Shader "UI/SplashExplosion"
{
    Properties
    {
        _Progress ("Splash Progress", Range(0, 1)) = 0
        _SplashIntensity ("Splash Intensity", Range(0.5, 5)) = 2.5
        _DropletCount ("Droplet Count", Range(10, 60)) = 30
        _SplashHeight ("Splash Height", Range(0.1, 1)) = 0.6
        _ColorSpeed ("Color Speed", Float) = 1.5
        _SparkleBrightness ("Sparkle Brightness", Range(1, 10)) = 4
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent"
            "RenderType"="Transparent"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            float _Progress;
            float _SplashIntensity;
            float _DropletCount;
            float _SplashHeight;
            float _ColorSpeed;
            float _SparkleBrightness;

            float3 hsv2rgb(float3 c)
            {
                float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
                float3 p = abs(frac(c.xxx + K.xyz) * 6.0 - K.www);
                return c.z * lerp(K.xxx, saturate(p - K.xxx), c.y);
            }

            float hash(float2 p)
            {
                return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453);
            }

            float hash1(float n)
            {
                return frac(sin(n) * 43758.5453);
            }

            float noise2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(hash(i), hash(i + float2(1, 0)), f.x),
                    lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x),
                    f.y
                );
            }

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = i.uv;
                
                // 底部边缘线（整条底部线都在泼水）
                float bottomY = 0.0;
                
                // 从底部向上的距离
                float heightFromBottom = uv.y - bottomY;
                
                // ===== 主水花溅射面 =====
                // 多个溅射波叠加，形成不规则水花
                float splashMask = 0;
                float splashEdge = 0;
                
                for (int s = 0; s < 6; s++)
                {
                    // 每道水花的水平偏移
                    float offsetX = hash1(s * 13.7) * 0.3 - 0.15;
                    float centerX = uv.x + offsetX;
                    
                    // 水花达到的高度（随机但有整体规律）
                    float waveHeight = _SplashHeight * (0.6 + hash1(s * 27.1) * 0.4);
                    
                    // 不规则的水花轮廓
                    float noiseFreq = 4 + s * 1.5;
                    float noiseAmp = 0.08 * (1 + hash1(s * 41.3));
                    float waveEdge = sin(centerX * noiseFreq + s * 2.5 + _Time.y * 0.5) * noiseAmp;
                    waveEdge += sin(centerX * noiseFreq * 2.3 + s * 3.7) * noiseAmp * 0.6;
                    
                    // 当前这道水花的高度线
                    float currentHeight = waveHeight * _Progress + waveEdge;
                    
                    // 水花主体
                    float bodyThickness = 0.03 + hash1(s * 53.9) * 0.04;
                    float body = smoothstep(currentHeight, currentHeight - bodyThickness, heightFromBottom)
                               * smoothstep(0, bodyThickness * 0.5, heightFromBottom);
                    
                    // 水花飞溅尖端
                    float tipSharpness = 0.005;
                    float tip = smoothstep(currentHeight + tipSharpness, currentHeight, heightFromBottom)
                              * smoothstep(currentHeight - tipSharpness * 2, currentHeight - tipSharpness, heightFromBottom);
                    
                    splashMask += body * 0.3;
                    splashEdge += tip * 2.0;
                }
                
                // ===== 飞溅水滴 =====
                float droplets = 0;
                for (int d = 0; d < 20; d++)
                {
                    float seedX = hash1(d * 17.3);
                    float seedY = hash1(d * 29.7);
                    float seedSize = hash1(d * 43.1);
                    
                    // 水滴散布在底部到最大溅射高度之间
                    float dropX = seedX;
                    float dropMaxY = _SplashHeight * (0.3 + seedY * 0.7);
                    float dropY = dropMaxY * _Progress;
                    
                    float dropDist = length(float2(uv.x - dropX, uv.y - dropY));
                    float dropSize = 0.003 + seedSize * 0.012;
                    
                    // 水滴随时间向上飞
                    float lifePhase = frac(seedY * 2.7 + _Progress * 1.5);
                    float dropAlpha = smoothstep(dropSize, 0, dropDist);
                    dropAlpha *= sin(lifePhase * 3.14159);
                    dropAlpha *= smoothstep(0, 0.2, lifePhase);
                    
                    droplets += dropAlpha * (0.5 + seedSize);
                }
                
                // ===== 底部基底光 =====
                float baseGlow = exp(-heightFromBottom * 2.5 / max(_Progress, 0.01)) * 0.5;
                
                // 底部水平光带的不规则性
                float baseNoise = noise2D(float2(uv.x * 8, uv.y * 3 + _Time.y * 0.3));
                baseGlow *= 0.7 + baseNoise * 0.3;
                
                // ===== 水花边缘的微小粒子 =====
                float microParticles = 0;
                float2 particleUV = uv * float2(40, 30);
                float particleNoise = noise2D(particleUV + _Time.y * 2.5);
                microParticles = particleNoise > 0.88 ? 1 : 0;
                microParticles *= smoothstep(_SplashHeight * _Progress * 1.1, _SplashHeight * _Progress * 0.3, heightFromBottom);
                microParticles *= smoothstep(0.01, 0.05, heightFromBottom);
                
                // ===== 组合所有效果 =====
                float totalIntensity = splashMask + splashEdge * 0.5 + droplets * 0.7 + baseGlow * 0.4 + microParticles * 0.3;
                totalIntensity *= _SplashIntensity;
                
                // 淡入淡出
                totalIntensity *= smoothstep(0, 0.02, _Progress);
                totalIntensity *= smoothstep(1, 0.9, _Progress);
                
                // 超出溅射高度就透明
                totalIntensity *= smoothstep(_SplashHeight * 1.1, _SplashHeight * 0.9, heightFromBottom);
                
                // ===== 颜色计算 =====
                float hue = uv.x * 0.7 + _Time.y * _ColorSpeed * 0.3;
                float3 color = hsv2rgb(float3(frac(hue), 0.7, 1));
                
                // 边缘高亮偏白
                float edgeRatio = splashEdge / max(totalIntensity, 0.001);
                float3 finalColor = lerp(color, float3(1, 1, 1), edgeRatio * 0.7);
                
                return fixed4(finalColor * totalIntensity, totalIntensity);
            }
            ENDCG
        }
    }
}