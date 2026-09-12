Shader "Hidden/EAStudio/CloudGenerator"
{
    Properties
    {
        _Layer1Shape ("Layer 1 Shape Type", Int) = 0
        _CloudCoverage ("Coverage", Range(0, 1)) = 0.5
        _CloudDensity ("Density", Range(0.1, 3.0)) = 1.0
        _CloudScale ("Scale", Float) = 1.0

        _EnableLayer2 ("Enable Layer 2", Float) = 1.0
        _Layer2Shape ("Layer 2 Shape Type", Int) = 1
        _Layer2Coverage ("Layer 2 Coverage", Range(0, 1)) = 0.4
        _Layer2Density ("Layer 2 Density", Range(0.05, 2.0)) = 0.5
        _Layer2Scale ("Layer 2 Scale", Float) = 2.5
        _Layer2SpeedMul ("Layer 2 Speed Multiplier", Float) = 1.5

        _DetailErosion ("Detail Erosion", Range(0, 1)) = 0.55
        _CloudHorizonFade ("Horizon Fade", Range(0.02, 0.5)) = 0.16
        _SilverLiningStrength ("Silver Lining", Float) = 2.5
        _AtmosphereThickness ("Atmosphere Thickness", Float) = 1.0
        _CloudColor ("Cloud Color", Color) = (1, 1, 1, 1)
        _CloudShadowColor ("Shadow Color", Color) = (0.35, 0.38, 0.45, 1)

        _CloudWindDirection ("Wind Direction", Vector) = (1, 0, 0, 0)
        _CloudWindSpeed ("Wind Speed", Float) = 5.0
        _CloudWindTime ("Wind Time", Float) = 0.0

        _SunDirection ("Sun Direction", Vector) = (0, 0.707, 0.707, 0)
        _SunColor ("Sun Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "GenerateCloudsPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                int _Layer1Shape;
                float _CloudCoverage;
                float _CloudDensity;
                float _CloudScale;

                float _EnableLayer2;
                int _Layer2Shape;
                float _Layer2Coverage;
                float _Layer2Density;
                float _Layer2Scale;
                float _Layer2SpeedMul;

                float _DetailErosion;
                float _CloudHorizonFade;
                float _SilverLiningStrength;
                float _AtmosphereThickness;
                float4 _CloudColor;
                float4 _CloudShadowColor;

                float4 _CloudWindDirection;
                float _CloudWindSpeed;
                float _CloudWindTime;

                float4 _SunDirection;
                float4 _SunColor;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, 1.0);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // =========================================================================================
            // Fast Analytical Procedural Noise Library (Worley, Perlin, Billow, Stratocumulus)
            // =========================================================================================
            float2 Hash22(float2 p)
            {
                p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
                return frac(sin(p) * 43758.5453123);
            }

            // 1. Worley / Voronoi Cellular Noise (Puffy cumulus)
            float Worley2D(float2 p)
            {
                float2 i_pos = floor(p);
                float2 f_pos = frac(p);
                float minDist = 1.0;
                UNITY_UNROLL
                for (int y = -1; y <= 1; y++)
                {
                    UNITY_UNROLL
                    for (int x = -1; x <= 1; x++)
                    {
                        float2 neighbor = float2(x, y);
                        float2 randPoint = Hash22(i_pos + neighbor);
                        float2 diff = neighbor + randPoint - f_pos;
                        minDist = min(minDist, length(diff));
                    }
                }
                return saturate(1.0 - minDist);
            }

            // 2. Perlin Gradient Wave Noise (Smooth wispy cirrus)
            float Perlin2D(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(dot(Hash22(i + float2(0.0, 0.0)) * 2.0 - 1.0, f - float2(0.0, 0.0)),
                         dot(Hash22(i + float2(1.0, 0.0)) * 2.0 - 1.0, f - float2(1.0, 0.0)), u.x),
                    lerp(dot(Hash22(i + float2(0.0, 1.0)) * 2.0 - 1.0, f - float2(0.0, 1.0)),
                         dot(Hash22(i + float2(1.0, 1.0)) * 2.0 - 1.0, f - float2(1.0, 1.0)), u.x), u.y) * 0.5 + 0.5;
            }

            // 3. Billow Turbulent Noise (Cauliflower puffs)
            float Billow2D(float2 p)
            {
                return abs(Worley2D(p) * 2.0 - 1.0);
            }

            // 4. Stratocumulus Layered Hybrid Noise (Deck clouds)
            float Stratocumulus2D(float2 p)
            {
                float pVal = Perlin2D(p);
                float wVal = Worley2D(p * 1.5);
                return saturate(pVal * 0.6 + wVal * 0.5);
            }

            float SampleNoiseArchetype(float2 p, int archetype)
            {
                if (archetype == 0) return Worley2D(p);
                if (archetype == 1) return Perlin2D(p);
                if (archetype == 2) return Billow2D(p);
                return Stratocumulus2D(p);
            }

            float FractalShape(float2 p, int archetype)
            {
                float v = 0.0;
                float amp = 0.5;
                float2 shift = float2(100.0, 100.0);
                UNITY_UNROLL
                for (int i = 0; i < 3; i++)
                {
                    v += amp * SampleNoiseArchetype(p, archetype);
                    p = p * 2.04 + shift;
                    amp *= 0.5;
                }
                return v * 1.15;
            }

            // Sunlight atmospheric transmittance (sunset reddening)
            float3 CalculateSunTransmittance(float3 L, float thickness)
            {
                float3 C_RAYLEIGH = float3(5.8, 13.5, 33.1) * 1e-6;
                float3 C_MIE = float3(3.996, 3.996, 3.996) * 1e-6;
                float3 C_OZONE = float3(0.65, 1.88, 0.085) * 1e-6;

                float lightExtinctionAmount = exp(-(saturate(L.y + 0.05) * 35.0)) +
                                            exp(-(saturate(L.y + 0.5) * 4.5)) * 0.4 +
                                            pow(saturate(1.0 - L.y), 2.0) * 0.02 + 0.002;

                return exp(-(C_RAYLEIGH * 1.5 + C_MIE + C_OZONE * 4.0) * lightExtinctionAmount * thickness * 1e6);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 worldPos = ComputeWorldSpacePosition(input.uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 rayDir = normalize(worldPos - _WorldSpaceCameraPos.xyz);

                if (rayDir.y <= 0.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // Safe sun direction
                float3 L = _SunDirection.xyz;
                float lenSq = dot(L, L);
                if (lenSq < 0.001)
                    L = float3(0.0, 0.7071, 0.7071);
                else
                    L = L * rsqrt(lenSq);

                float3 sunColor = _SunColor.rgb;
                if (dot(sunColor, sunColor) < 0.001)
                    sunColor = float3(1.0, 1.0, 1.0);

                float thickness = max(_AtmosphereThickness, 0.1);

                // Sunlight transmitted through atmosphere
                float3 sunTransmittance = CalculateSunTransmittance(L, thickness);
                float daylightFactor = saturate((L.y + 0.2) / 0.35);
                float3 filteredSun = sunColor * sunTransmittance * daylightFactor;
                float3 ambientSkyTint = float3(0.12, 0.18, 0.25) * daylightFactor + float3(0.01, 0.015, 0.02);

                float cosTheta = dot(rayDir, L);
                float hgPhase = (1.0 - 0.81) / pow(max(1.0 + 0.81 - 1.8 * cosTheta, 0.01), 1.5);
                float horizonFade = smoothstep(0.0, max(_CloudHorizonFade, 0.02), rayDir.y);

                // =========================================================================================
                // Shader-level Time Animation: Uses _CloudWindTime or built-in _Time.y
                // =========================================================================================
                float timeVal = (_CloudWindTime > 0.0001) ? _CloudWindTime : _Time.y;
                float2 windDir = normalize(_CloudWindDirection.xy + float2(0.0001, 0.0));
                float2 baseWindOffset = windDir * (_CloudWindSpeed * timeVal * 0.005);

                // =========================================================================================
                // Layer 1: Low-Altitude Main Cloud Deck (Cumulus / Stratocumulus)
                // =========================================================================================
                float curvature1 = 0.22;
                float2 domeUV1 = (rayDir.xz / max(rayDir.y + curvature1, 0.04)) * _CloudScale * 1.5;
                float2 sampleUV1 = domeUV1 + baseWindOffset;

                float noise1 = FractalShape(sampleUV1, _Layer1Shape);
                float rawDensity1 = saturate((noise1 - (1.0 - _CloudCoverage)) / max(_CloudCoverage, 0.01));

                float erodedDensity1 = rawDensity1;
                if (rawDensity1 > 0.001)
                {
                    float detail1 = Perlin2D(sampleUV1 * 3.5 + baseWindOffset * 0.25);
                    erodedDensity1 = saturate(rawDensity1 - (1.0 - detail1) * pow(saturate(1.0 - rawDensity1), 2.0) * _DetailErosion);
                }

                float finalDensity1 = erodedDensity1 * _CloudDensity * horizonFade;
                float opacity1 = saturate(1.0 - exp(-finalDensity1 * 3.0));

                float3 litColor1 = float3(0, 0, 0);
                if (opacity1 > 0.001)
                {
                    float silverLining1 = pow(saturate(1.0 - erodedDensity1), 2.5) * hgPhase * _SilverLiningStrength;

                    // Self-shadowing along light ray
                    float2 shadowStep1 = -normalize(L.xz + float2(0.001, 0.001)) * 0.02;
                    float shadowNoise1 = FractalShape(sampleUV1 + shadowStep1, _Layer1Shape);
                    float shadowDensity1 = saturate((shadowNoise1 - (1.0 - _CloudCoverage)) / max(_CloudCoverage, 0.01));
                    float selfShadow1 = exp(-shadowDensity1 * 3.2);

                    litColor1 = lerp(_CloudShadowColor.rgb * ambientSkyTint, _CloudColor.rgb * filteredSun, selfShadow1);
                    litColor1 += silverLining1 * filteredSun * 1.5;
                }

                float3 finalColor = litColor1 * opacity1;
                float finalOpacity = opacity1;

                // =========================================================================================
                // Layer 2: High-Altitude Secondary Cloud Deck (Wispy Cirrus)
                // =========================================================================================
                if (_EnableLayer2 > 0.5)
                {
                    float curvature2 = 0.12;
                    float2 domeUV2 = (rayDir.xz / max(rayDir.y + curvature2, 0.03)) * _Layer2Scale * 1.5;
                    float2 sampleUV2 = domeUV2 + baseWindOffset * _Layer2SpeedMul;

                    float noise2 = FractalShape(sampleUV2, _Layer2Shape);
                    float rawDensity2 = saturate((noise2 - (1.0 - _Layer2Coverage)) / max(_Layer2Coverage, 0.01));

                    float finalDensity2 = rawDensity2 * _Layer2Density * horizonFade;
                    float opacity2 = saturate(1.0 - exp(-finalDensity2 * 2.2));

                    if (opacity2 > 0.001)
                    {
                        float silverLining2 = pow(saturate(1.0 - rawDensity2), 2.0) * hgPhase * (_SilverLiningStrength * 0.6);
                        float3 litColor2 = _CloudColor.rgb * filteredSun + silverLining2 * filteredSun;

                        // Front-to-back layer compositing (Layer 1 in front, Layer 2 behind)
                        finalColor += litColor2 * opacity2 * (1.0 - finalOpacity);
                        finalOpacity = finalOpacity + opacity2 * (1.0 - finalOpacity);
                    }
                }

                return half4(finalColor, finalOpacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
