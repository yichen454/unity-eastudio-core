Shader "Hidden/EAStudio/CloudGenerator"
{
    Properties
    {
        _CloudWindTime ("Wind Time", Float) = 0.0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "GenerateVolumetricClouds"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "VolumetricCloudCore.hlsl"

            TEXTURE2D(_BaseTex);
            TEXTURE2D(_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Cloud_Params0;      // x: scale, y: coverage, z: density, w: cloudType
                float4 _Cloud_Params1;      // x: detailScale, y: detailErosion, z: bottomRoundness, w: topSoftness
                float4 _Cloud_Params2;      // x: altitude, y: thickness, z: earthRadius, w: horizonFade
                float4 _Cloud_Lighting;     // x: absorption, y: selfShadowStrength, z: powderEffect, w: sunLightIntensity
                float4 _Cloud_Lighting2;    // x: silverLiningIntensity, y: silverLiningSpread, z: backlitStrength, w: ambientFloor
                float4 _Cloud_Scattering;   // x: multiScattering, y: multiScatterFalloff, z: horizonFadeStart, w: 0
                float4 _Cloud_Steps;        // x: minSteps, y: maxSteps, z: lightmarchSteps, w: enableSkipping
                float4 _Cloud_WindVector;   // xy: windOffset, z: detailWindSpeed, w: 0
                float4 _Cloud_Color;        // rgb: cloudColor

                float4 _SunDirection;
                float4 _SunColor;
                float4 _GroundColor;
                float4 _NightSkyColor;
                float4 _AmbientSkyColor;
                float4 _AmbientSunsetColor;
                float4 _ShadowColor;
                float4 _MoonDirection;
                float4 _MoonColor;
                float _MoonLightIntensity;
                float _GroundFade;
                float _CloudWindTime;
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

                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            // Static 4x4 Ordered Bayer Dither (Smooth & temporal-noise-free)
            float GetBayer4x4(float2 screenPos)
            {
                int2 p = int2(fmod(screenPos, 4.0));
                static const float bayer[16] = {
                     0.0 / 16.0,  8.0 / 16.0,  2.0 / 16.0, 10.0 / 16.0,
                    12.0 / 16.0,  4.0 / 16.0, 14.0 / 16.0,  6.0 / 16.0,
                     3.0 / 16.0, 11.0 / 16.0,  1.0 / 16.0,  9.0 / 16.0,
                    15.0 / 16.0,  7.0 / 16.0, 13.0 / 16.0,  5.0 / 16.0
                };
                return bayer[p.y * 4 + p.x];
            }

            // -------------------------------------------------------------------
            // Enviro 3 Exact Cloud Density & Shaping Pipeline
            // -------------------------------------------------------------------
            float SampleCloudDensity(float3 pos, float3 planetCenter, float innerRadius, float thickness, float lod, bool sampleDetail)
            {
                float height = CalculateHeightFraction(pos, planetCenter, innerRadius, thickness);
                if (height <= 0.001 || height >= 0.999)
                    return 0.0;

                // 1. Wind settings & height advection (wind shear tilt)
                float cloud_top_offset = thickness * 0.45;
                float2 windDir = length(_Cloud_WindVector.xy) > 1e-5 ? normalize(_Cloud_WindVector.xy) : float2(1.0, 0.0);
                float3 advectedPos = pos;
                advectedPos.xz += height * windDir * cloud_top_offset;
                advectedPos.xz += _Cloud_WindVector.xy * 1200.0;

                // 2. Base coordinates & procedural multi-octave FBM
                const float baseFreq = 1e-5;
                float baseScale = _Cloud_Params0.x * 2.8;
                float2 coordBase = advectedPos.xz * (baseFreq * baseScale);

                float baseNoiseR = SAMPLE_TEXTURE2D_LOD(_BaseTex, sampler_LinearRepeat, coordBase, lod * 0.5).r;
                float baseNoiseG = SAMPLE_TEXTURE2D_LOD(_BaseTex, sampler_LinearRepeat, coordBase * 2.17 + float2(0.35, 0.65), lod * 0.5 + 0.5).r;
                float baseNoiseB = SAMPLE_TEXTURE2D_LOD(_BaseTex, sampler_LinearRepeat, coordBase * 4.31 + float2(0.12, 0.81), lod * 0.5 + 1.0).r;
                float low_freq_fBm = (baseNoiseG * 0.625) + (baseNoiseB * 0.375);

                // 3. Base cloud with low-frequency erosion
                float baseErosion = _Cloud_Params1.y;
                float base_cloud = RemapEnviro(baseNoiseR, -(1.0 - low_freq_fBm) * (baseErosion * 0.85), 1.0, 0.0, 1.0);

                // 4. Enviro 3 Vertical Shaping
                float bottomShape = _Cloud_Params1.z;
                float midShape    = 1.0 - abs(_Cloud_Params0.w - 0.5) * 2.0;
                float topShape    = _Cloud_Params1.w;
                float rampShape   = 0.25;
                float targetShape = CloudVerticalShapingEnviro(height, bottomShape, midShape, topShape, rampShape);

                float4 gradient = GetHeightGradientEnviro(_Cloud_Params0.w);
                float heightGradient = GradientStepEnviro(height, gradient);
                float shapedCloud = base_cloud * targetShape;
                shapedCloud *= lerp(1.0, heightGradient, 0.75);

                // 5. Coverage Remap (Enviro exact thresholding)
                float coverage = _Cloud_Params0.y;
                float cloud_coverage = saturate(1.0 - coverage);
                if (height > 0.90)
                    cloud_coverage = saturate(cloud_coverage + (height - 0.90) * 5.0);

                float cloudDensity = RemapEnviro(shapedCloud, cloud_coverage, 1.0, 0.0, 1.0);

                // 6. Detail Noise Erosion with Height Inversion (fine wispy top fibers)
                if (sampleDetail && _Cloud_Params1.y > 0.01 && cloudDensity > 0.001)
                {
                    float detailScale = _Cloud_Params1.x * 2.2;
                    float2 coordDetail = advectedPos.xz * (baseFreq * baseScale * detailScale) + _Cloud_WindVector.xy * (1200.0 * _Cloud_WindVector.z);

                    float d1 = SAMPLE_TEXTURE2D_LOD(_DetailTex, sampler_LinearRepeat, coordDetail, lod).r;
                    float d2 = SAMPLE_TEXTURE2D_LOD(_DetailTex, sampler_LinearRepeat, coordDetail * 2.23 + float2(0.25, 0.75), lod + 0.5).r;
                    float high_freq_fBm = (d1 * 0.65) + (d2 * 0.35);

                    float high_freq_noise_modifier = lerp(high_freq_fBm, 1.0 - high_freq_fBm, saturate(height * 8.0));
                    cloudDensity = RemapEnviro(cloudDensity, saturate(high_freq_noise_modifier * baseErosion * 0.65), 1.0, 0.0, 1.0);
                }

                if (height > 0.90)
                    cloudDensity *= smoothstep(1.0, 0.88, height);

                return cloudDensity * _Cloud_Params0.z;
            }

            // Enviro 3 Exact Light Marching (6-tap exponentially expanding shadow sample pattern)
            float GetDensityAlongRayEnviro(float3 pos, float3 lightDir, float3 planetCenter, float innerRadius, float thickness, int numSteps)
            {
                static const float shadowSampleDist[6] = {0.25, 1.0, 3.0, 8.0, 16.0, 32.0};
                float opticalDepth = 0.0;
                float distanceTraveled = 0.0;
                float sunHeight = saturate(dot(lightDir, float3(0.0, 1.0, 0.0)));
                float airPathFactor = lerp(0.6, 1.4, pow(sunHeight, 0.5));

                UNITY_LOOP
                for (int i = 0; i < numSteps && i < 6; i++)
                {
                    float altitude = pos.y;
                    float densityFalloff = exp(-altitude / 5000.0) * 0.25;
                    float baseStep = shadowSampleDist[i] * (thickness * 0.12) * densityFalloff * airPathFactor;
                    distanceTraveled += baseStep;

                    float3 samplePos = pos + lightDir * distanceTraveled;
                    float d = SampleCloudDensity(samplePos, planetCenter, innerRadius, thickness, float(i) * 0.6, false);
                    float weight = exp(-0.7 * float(i));
                    opticalDepth += weight * d * baseStep;
                }

                return opticalDepth;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 worldPos = ComputeWorldSpacePosition(input.uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 camPos   = _WorldSpaceCameraPos.xyz;
                float3 rayDir   = normalize(worldPos - camPos);

                // Horizon fade mask setup
                float horizonFadeWidth = max(0.005, _Cloud_Params2.w);
                float horizonFadeStart = _Cloud_Scattering.z;
                float horizonFadeMask  = smoothstep(horizonFadeStart, horizonFadeStart + horizonFadeWidth, rayDir.y);

                if (horizonFadeMask <= 0.0001)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // Planet center & spherical atmosphere bounds
                float earthRadius = _Cloud_Params2.z;
                float3 planetCenter = float3(camPos.x, -earthRadius, camPos.z);
                float altitude = _Cloud_Params2.x;
                float thickness = _Cloud_Params2.y;
                float innerRadius = earthRadius + altitude;
                float outerRadius = innerRadius + thickness;

                float tStart, tEnd;
                if (!ResolveRayStartEnd(camPos, rayDir, planetCenter, innerRadius, outerRadius, tStart, tEnd))
                    return half4(0.0, 0.0, 0.0, 0.0);

                if (tEnd <= tStart || tEnd <= 0.0)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // Scene depth clipping
                float rawDepth = SampleSceneDepth(input.uv);
#if UNITY_REVERSED_Z
                bool isBackground = rawDepth < 0.0001;
#else
                bool isBackground = rawDepth > 0.9999;
#endif
                if (!isBackground)
                {
                    float3 sceneWorldPos = ComputeWorldSpacePosition(input.uv, rawDepth, UNITY_MATRIX_I_VP);
                    float sceneDist = length(sceneWorldPos - camPos);
                    tEnd = min(tEnd, sceneDist);
                    if (tEnd <= tStart)
                        return half4(0.0, 0.0, 0.0, 0.0);
                }

                // Celestial light directions and colors (Previous clean version)
                float3 L = normalize(_SunDirection.xyz);
                float3 M = normalize(_MoonDirection.xyz);
                float nightFactor = smoothstep(0.04, -0.06, L.y);

                float sunsetExtinctionFactor = smoothstep(0.25, 0.05, L.y) * smoothstep(-0.01, 0.04, L.y);
                float sunAlt = max(L.y, 0.001);
                float airmass = 1.0 / max(0.035, sunAlt + 0.16);
                float3 rayleighExtinction = exp(-float3(0.04, 0.22, 0.75) * (airmass * 2.0));
                float3 effectiveSunColor = saturate(lerp(_SunColor.rgb, _SunColor.rgb * rayleighExtinction * 1.4, sunsetExtinctionFactor));

                float3 directSunLight = effectiveSunColor * smoothstep(-0.02, 0.04, L.y);
                float3 directMoonLight = _MoonColor.rgb * (_MoonLightIntensity * nightFactor);

                float3 activeLightDir = normalize(lerp(L, M, nightFactor));
                float3 activeLightColor = lerp(directSunLight, directMoonLight, nightFactor);
                float sunHeight = saturate(dot(activeLightDir, float3(0.0, 1.0, 0.0)));

                // Sky dome ambient
                float daylightFactor = smoothstep(-0.18, 0.12, L.y);
                float sunsetTwilightFactor = smoothstep(0.25, -0.02, L.y) * smoothstep(-0.15, 0.08, L.y);
                float3 dayAmbient = _AmbientSkyColor.rgb;
                float3 sunsetAmbient = _AmbientSunsetColor.rgb;
                float3 nightAmbient = _NightSkyColor.rgb * 1.2 + float3(0.005, 0.008, 0.015);
                float3 skyDomeLight = lerp(nightAmbient, lerp(dayAmbient, sunsetAmbient, sunsetTwilightFactor), daylightFactor);

                // Raymarch step setup
                float minSteps = _Cloud_Steps.x;
                float maxSteps = _Cloud_Steps.y;
                int lightSteps = (int)clamp(_Cloud_Steps.z, 1.0, 6.0);
                bool enableSkipping = _Cloud_Steps.w > 0.5;

                float elevation = saturate(rayDir.y * 1.6);
                int targetSteps = (int)clamp(lerp(maxSteps, minSteps, elevation), 8.0, 96.0);
                float totalPath = tEnd - tStart;
                float rayStepLength = totalPath / float(targetSteps);
                float3 rayStep = rayDir * rayStepLength;

                // Static Bayer dither
                float bayerDither = GetBayer4x4(input.positionCS.xy);
                float3 currentPos = camPos + rayDir * (tStart + bayerDither * (rayStepLength * 0.35));

                float cosTheta = dot(rayDir, activeLightDir);

                // Enviro lighting parameters packing (Restored clean parameters)
                EnviroLightParameters lightParams;
                lightParams.scatteringCoef        = _Cloud_Lighting.w * 1.5;   // sunLightIntensity
                lightParams.silverLiningIntensity = _Cloud_Lighting2.x;        // silverLiningIntensity
                lightParams.silverLiningSpread    = _Cloud_Lighting2.y;        // silverLiningSpread
                lightParams.edgeHighlightStrength = _Cloud_Lighting.z;         // powderEffect
                lightParams.multiScatterStrength  = _Cloud_Scattering.x;       // multiScattering
                lightParams.multiScatterFalloff   = _Cloud_Scattering.y;       // multiScatterFalloff
                lightParams.ambientFloor          = _Cloud_Lighting2.w * 0.5;  // ambientFloor
                lightParams.lightAbsorb           = _Cloud_Lighting.x;         // absorption
                lightParams.exposure              = 1.0;

                // Main Raymarching Loop (Enviro 3 Exact Integration)
                float trans = 1.0;
                float accumIntensity = 0.0;
                float depthAccum = 0.0;
                float depthWeightSum = 0.00001;

                float cloud_test = 0.0;
                int zeroSampleCount = 0;

                UNITY_LOOP
                for (int s = 0; s < targetSteps; s++)
                {
                    if (trans <= 0.01)
                        break;

                    float distToCam = length(currentPos - camPos);
                    float lod = saturate((distToCam - 2000.0) / 35000.0) * 3.5;

                    if (enableSkipping && cloud_test <= 0.0)
                    {
                        float dTest = SampleCloudDensity(currentPos, planetCenter, innerRadius, thickness, lod + 1.0, false);
                        if (dTest <= 0.001)
                        {
                            currentPos += rayStep * 2.0;
                            continue;
                        }
                        else
                        {
                            currentPos -= rayStep;
                            cloud_test = 1.0;
                            zeroSampleCount = 0;
                        }
                    }

                    float sampled_density = SampleCloudDensity(currentPos, planetCenter, innerRadius, thickness, lod, true);

                    if (sampled_density <= 0.001)
                    {
                        zeroSampleCount++;
                        if (zeroSampleCount >= 6)
                        {
                            cloud_test = 0.0;
                        }
                        currentPos += rayStep;
                        continue;
                    }
                    else
                    {
                        zeroSampleCount = 0;
                    }

                    // 1. Extinction & Transmittance (Enviro density smoothness formula)
                    float extinction = pow(max(_Cloud_Params0.z * sampled_density, 0.0001), 1.25);
                    float stepTransmittance = exp(-extinction * (rayStepLength * 0.00085));

                    // 2. Optical depth along light ray
                    float tau = GetDensityAlongRayEnviro(currentPos, activeLightDir, planetCenter, innerRadius, thickness, lightSteps);

                    // 3. Exact Enviro Energy calculation
                    float luminance = SampleEnviroEnergy(cosTheta, sunHeight, tau, lightParams);

                    // 4. Inscattering integration
                    float integScatt = luminance - luminance * stepTransmittance;

                    accumIntensity += trans * integScatt;
                    depthAccum     += trans * distToCam;
                    depthWeightSum += trans;

                    trans *= stepTransmittance;
                    currentPos += rayStep;
                }

                // Enviro 3 Blend Pass & Smooth Horizon Transition
                float cloudAlpha = saturate(1.0 - trans);

                // Feather out micro-alpha boundaries to eliminate edge stepping/dark halos
                float microFeather = smoothstep(0.0001, 0.035, cloudAlpha);
                cloudAlpha *= microFeather;

                float cloudDistance = depthWeightSum > 0.0001 ? (depthAccum / depthWeightSum) : tStart;
                float3 sunColor = pow(activeLightColor.rgb, 2.0) * 2.5;

                // Height influence on ambient sky light
                float3 cloudCenterPos = camPos + rayDir * cloudDistance;
                float normalizedHeight = CalculateHeightFraction(cloudCenterPos, planetCenter, innerRadius, thickness);
                float heightFactor = lerp(0.7, 1.3, normalizedHeight);
                float distFalloff = exp(-cloudDistance * 0.000015);
                float ambientVal = saturate(cloudAlpha * heightFactor * distFalloff);

                // Clean ambient lighting without muddy complementary tint shifts
                float3 cloudRGB = (accumIntensity * sunColor + skyDomeLight * (ambientVal * _Cloud_Lighting2.w * 1.6)) * _Cloud_Color.rgb;

                // Atmospheric Blend Distance (Enviro Blend)
                float cloudLum = dot(saturate(cloudRGB), float3(0.299, 0.587, 0.114));
                float baseDist = 70000.0;
                float blendDist = lerp(baseDist * 0.85, baseDist * 1.85, cloudLum);
                float atmosphericBlendFactor = saturate(exp(-cloudDistance / blendDist));
                cloudRGB = lerp(skyDomeLight, cloudRGB, atmosphericBlendFactor);

                // Horizon Fade: smoothly melts clouds into the distant atmospheric horizon haze
                cloudAlpha *= horizonFadeMask;

                return half4(cloudRGB, cloudAlpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
