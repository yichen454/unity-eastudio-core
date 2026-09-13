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
            Name "GenerateCloudsPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Cloud_0_Tex);
            TEXTURE2D(_Cloud_0_DetailTex);
            TEXTURE2D(_Cloud_1_Tex);
            TEXTURE2D(_Cloud_1_DetailTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _Cloud_0_SampleParams;       // x: scale, y: curvature*0.5, z: 1 - coverage, w: rev_inv_coverage
                float4 _Cloud_0_DetailParams;       // x: detailScale, y: detailWeight
                float4 _Cloud_0_SilverLiningParams; // x: width, y: strength
                float4 _Cloud_0_MaskParams;         // x: rcp(horizonMask), y: horizonMaskBlend, z: rcp(1-zenithMask), w: zenithMaskBlend
                float4 _Cloud_0_LightingParams;     // x: opacity, y: absorption, z: absorptionLimit, w: thickness * 0.02
                float4 _Cloud_0_WindVector;         // xy: windVector, w: detailWindSpeed
                float4 _Cloud_0_LightmarchSteps;    // x: steps, y: rcp(steps)
                float4 _Cloud_0_Color;
                float4 _Cloud_0_LightTransmittance;

                float4 _Cloud_1_SampleParams;
                float4 _Cloud_1_DetailParams;
                float4 _Cloud_1_SilverLiningParams;
                float4 _Cloud_1_MaskParams;
                float4 _Cloud_1_LightingParams;
                float4 _Cloud_1_WindVector;
                float4 _Cloud_1_LightmarchSteps;
                float4 _Cloud_1_Color;
                float4 _Cloud_1_LightTransmittance;

                float4 _ParallaxTransitionedMainLightDir;
                float4 _SunDirection;
                float4 _SunColor;
                float4 _GroundColor;
                float4 _NightSkyColor;
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

            float smootherstep(float a, float b, float x)
            {
                x = saturate((x - a) / max(0.0001, b - a));
                return x * x * x * (x * (x * 6.0 - 15.0) + 10.0);
            }

            float hg(float eyeCos, float g)
            {
                float g2 = g * g;
                return (1.0 - g2) / (4.0 * 3.14159265 * pow(max(1.0 + g2 - 2.0 * g * eyeCos, 0.001), 1.5));
            }

            void ProcessCloudLayer(
                Texture2D tex,
                Texture2D detailTex,
                float4 sampleParams,
                float2 detailParams,
                float2 silverLiningParams,
                float4 maskParams,
                float4 lightingParams,
                float4 windVector,
                float2 lightmarchSteps,
                float3 layerColor,
                float3 rayDir,
                float3 L,
                float3 directSunLight,
                float3 ambientSky,
                float eyeCos,
                float phase,
                float eyeCos_moon,
                float moonPhase,
                float sunDirectIntensity,
                float timeVal,
                float2 parallaxMainLightDir,
                inout float3 color,
                inout float totalTransmittance)
            {
                // If coverage is 0 or layer disabled, sampleParams.w == 0, strictly skip!
                if (sampleParams.w < 0.001)
                    return;

                float curvature = sampleParams.y;
                float2 parallaxViewDir = rayDir.xz * rcp(lerp(rayDir.y, 1.0, curvature));
                float2 samplePos = parallaxViewDir * sampleParams.x * (curvature + 1.0);
                float2 windOffset = windVector.xy * timeVal;
                float2 samplePosBase = samplePos + windOffset;

                // Base fractal density
                float rawTex = SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearRepeat, samplePosBase, 0).r;
                float rawDiff = rawTex - sampleParams.z;
                if (rawDiff <= 0.0)
                    return;

                float density = rawDiff * sampleParams.w;
                float baseDensity = density;

                if (density > 0.0)
                {
                    float2 samplePosDetail = samplePos * detailParams.x + windOffset * windVector.w;
                    float detail = SAMPLE_TEXTURE2D_LOD(detailTex, sampler_LinearRepeat, samplePosDetail, 0).r;
                    float erodeWeight = pow(saturate(1.0 - density), 4.0) * detailParams.y;
                    density = max(0.0, density - (1.0 - detail) * erodeWeight);
                    density = smoothstep(0.0, 0.04, density) * density;
                }

                if (density <= 0.0001)
                    return;

                // Horizon mask only: smoothly fades near ground, keeps zenith (正上方) 100% full and continuous
                float tH = smootherstep(maskParams.y, 1.0, (rayDir.y + 0.02) * maskParams.x);
                float mul = tH;

                float transmittance = exp(-density * mul * lightingParams.x);

                // Light marching step: marches through cloud slab along sunlight direction
                // Prevents step vector from collapsing to 0 at the sun position (which caused the dark hole!)
                float2 sunSlabDir = -normalize(L.xz + float2(0.0001, 0.0001));
                float2 dirToLight = (parallaxMainLightDir - parallaxViewDir);
                float dirLen = length(dirToLight);
                float2 viewShift = dirLen > 0.001 ? (dirToLight / dirLen * min(dirLen, 2.0)) : float2(0, 0);

                float2 stepDir = normalize(sunSlabDir + viewShift * 0.35);
                float2 lightRayStep = lightingParams.w * lightmarchSteps.y * stepDir * 0.15;
                float jitter = frac(sin(dot(samplePosBase * 50.0, float2(12.9898, 78.233))) * 43758.5453);
                float2 lightRayPos = samplePosBase + lightRayStep * (jitter * 0.5 + 0.5);

                float lightAtt = baseDensity * 0.5;

                int steps = (int)lightmarchSteps.x;
                UNITY_LOOP
                for (int l = 0; l < steps; l++)
                {
                    float stepVal = max(0.0, SAMPLE_TEXTURE2D_LOD(tex, sampler_LinearRepeat, lightRayPos, 0).r - sampleParams.z) * sampleParams.w;
                    lightAtt += stepVal;
                    lightRayPos += lightRayStep;
                }

                // Smooth Beer-Lambert absorption without harsh cliff
                float attFactor = lightAtt * lightmarchSteps.y * lightingParams.y;
                float absorptionCurve = exp(-attFactor * 0.7);
                float absorptionLimit = lightingParams.z;
                float absorption = lerp(absorptionLimit, 1.0, absorptionCurve);

                // Forward scattering glow inside cloud body looking toward the sun (prevents dark hole)
                float forwardGlow = pow(eyeCos, 6.0) * 0.35;
                absorption = max(absorption, forwardGlow + absorptionLimit * 0.8);

                // Physical continuous forward scattering (replaces harsh step-cliff with exponential penetration)
                float hgNorm = min(phase, 2.0);
                float forwardScatter = exp(-density * 3.5);
                float silverLining = hgNorm * forwardScatter * min(silverLiningParams.y, 1.2) * 0.4;

                // --- Atmospheric Day / Sunset / Night Lighting ---
                // 1. Direct Sunlit component: fades out at night, glows golden-red at sunset
                float3 sunlitColor = directSunLight * (silverLining + absorption);

                // 2. Ambient Sky/Ground component: soft blue in day, rose-purple at dusk, dark at night
                float3 ambientColor = ambientSky * (0.55 + 0.45 * (1.0 - absorption));

                // 3. Moonlight illumination on clouds at night (soft exponential penetration, zero hard cliff)
                float moonHG = min(moonPhase, 2.0);
                float moonScatter = exp(-density * 3.5);
                float moonSilver = moonHG * moonScatter * min(silverLiningParams.y, 1.2) * 0.4;
                float3 moonLight = _MoonColor.rgb * _MoonLightIntensity * (moonSilver + absorption * 0.3);
                float3 nocturnalLight = moonLight * (1.0 - sunDirectIntensity);

                // 4. Combined cloud color modulated by layer tint
                float3 cloudColor = (sunlitColor + ambientColor + nocturnalLight) * layerColor;

                totalTransmittance *= transmittance;
                color = lerp(cloudColor, color, transmittance);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 worldPos = ComputeWorldSpacePosition(input.uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 rayDir = normalize(worldPos - _WorldSpaceCameraPos.xyz);

                if (rayDir.y <= -0.01)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // Early exit if both layers are disabled or have 0 coverage
                if (_Cloud_0_SampleParams.w < 0.001 && _Cloud_1_SampleParams.w < 0.001)
                    return half4(0.0, 0.0, 0.0, 0.0);

                float3 L = _SunDirection.xyz;
                float lenSq = dot(L, L);
                if (lenSq < 0.001) L = float3(0.0, 0.7071, 0.7071);
                else L = L * rsqrt(lenSq);

                float eyeCos = max(0.0, dot(rayDir, L));
                float phase = hg(eyeCos, 0.9) * 0.7;

                // Moonlight forward scattering
                float3 M = _MoonDirection.xyz;
                float lenM = dot(M, M);
                if (lenM < 0.001) M = float3(0.0, 0.7071, -0.7071);
                else M = M * rsqrt(lenM);
                float eyeCos_moon = max(0.0, dot(rayDir, M));
                float moonPhase = hg(eyeCos_moon, 0.85);

                // --- Sun Elevation Driven Lighting (Day -> Sunset -> Night) ---
                // 1. Direct sunlight intensity: 1.0 in day (L.y > 0.05), fades smoothly to 0.0 at night (L.y < -0.15)
                float sunDirectIntensity = smoothstep(-0.15, 0.05, L.y);

                // 2. Sunset color transition: shifts smoothly as sun approaches and dips below horizon
                float sunsetProgress = smoothstep(0.28, -0.02, L.y);
                float3 sunsetColor = lerp(float3(1.0, 0.95, 0.90), float3(1.0, 0.45, 0.12), smoothstep(0.28, 0.05, L.y));
                sunsetColor = lerp(sunsetColor, float3(0.95, 0.22, 0.06), smoothstep(0.05, -0.05, L.y));
                float3 directSunLight = lerp(float3(1.0, 1.0, 1.0), sunsetColor, sunsetProgress) * sunDirectIntensity * _SunColor.rgb;

                // 3. Ambient lighting transition (Daytime blue -> Sunset purple/rose -> Night dark sky)
                float daylightFactor = smoothstep(-0.18, 0.12, L.y);
                float sunsetFactor = smoothstep(0.25, -0.02, L.y) * smoothstep(-0.15, 0.08, L.y);

                float3 dayAmbient = float3(0.28, 0.35, 0.45);
                float3 sunsetAmbient = float3(0.35, 0.22, 0.28);
                float3 nightAmbient = _NightSkyColor.rgb * 1.5 + float3(0.015, 0.02, 0.035);

                // Ground bounce light adds earth tone near ground
                float3 groundBounce = _GroundColor.rgb * (saturate(L.y * 1.5 + 0.2) * 0.35 + 0.05);
                float3 ambientSky = lerp(nightAmbient, lerp(dayAmbient, sunsetAmbient, sunsetFactor), daylightFactor);
                ambientSky = lerp(groundBounce, ambientSky, saturate(rayDir.y * 2.0));

                float timeVal = (_CloudWindTime > 0.0001) ? _CloudWindTime : _Time.y;

                float3 cloudColor = float3(0.0, 0.0, 0.0);
                float totalTransmittance = 1.0;

                // Layer 1 (Low altitude)
                ProcessCloudLayer(
                    _Cloud_0_Tex,
                    _Cloud_0_DetailTex,
                    _Cloud_0_SampleParams,
                    _Cloud_0_DetailParams.xy,
                    _Cloud_0_SilverLiningParams.xy,
                    _Cloud_0_MaskParams,
                    _Cloud_0_LightingParams,
                    _Cloud_0_WindVector,
                    _Cloud_0_LightmarchSteps.xy,
                    _Cloud_0_Color.rgb,
                    rayDir, L, directSunLight, ambientSky, eyeCos, phase, eyeCos_moon, moonPhase, sunDirectIntensity, timeVal,
                    _ParallaxTransitionedMainLightDir.xy,
                    cloudColor, totalTransmittance);

                // Layer 2 (High altitude)
                ProcessCloudLayer(
                    _Cloud_1_Tex,
                    _Cloud_1_DetailTex,
                    _Cloud_1_SampleParams,
                    _Cloud_1_DetailParams.xy,
                    _Cloud_1_SilverLiningParams.xy,
                    _Cloud_1_MaskParams,
                    _Cloud_1_LightingParams,
                    _Cloud_1_WindVector,
                    _Cloud_1_LightmarchSteps.xy,
                    _Cloud_1_Color.rgb,
                    rayDir, L, directSunLight, ambientSky, eyeCos, phase, eyeCos_moon, moonPhase, sunDirectIntensity, timeVal,
                    _ParallaxTransitionedMainLightDir.xy,
                    cloudColor, totalTransmittance);

                float opacity = saturate(1.0 - totalTransmittance);
                return half4(cloudColor, opacity);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
