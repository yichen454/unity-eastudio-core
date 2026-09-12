Shader "Skybox/EAStudio/ProceduralSky"
{
    Properties
    {
        [Gamma] _Exposure ("Exposure", Float) = 1.0

        _SunSize ("Sun Size", Range(0.001, 0.2)) = 0.04
        _SunConvergence ("Sun Halo Convergence", Range(1.0, 30.0)) = 8.0

        _AtmosphereThickness ("Atmosphere Thickness", Range(0.1, 5.0)) = 1.0
        _OzoneAbsorption ("Ozone Absorption", Range(0.0, 5.0)) = 1.0
        _AerosolHaze ("Aerosol Haze", Range(0.1, 5.0)) = 1.0

        _SkyTint ("Sky Tint", Color) = (.5, .5, .5, 1)
        _GroundColor ("Ground Color", Color) = (0.369, 0.349, 0.341, 1)
        _GroundFade ("Ground Transition Width", Range(0.01, 1.0)) = 0.25
        _NightSkyColor ("Night Sky Color", Color) = (0.02, 0.03, 0.06, 1)

        [HideInInspector] _SunDirection ("Sun Direction", Vector) = (0, 0.707, 0.707, 0)
        [HideInInspector] _SunColor ("Sun Color", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            Name "ProceduralSkyboxPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            #if !defined(UNITY_COLORSPACE_GAMMA)
            #define unity_ColorSpaceDouble half4(4.59479380, 4.59479380, 4.59479380, 2.0)
            #else
            #define unity_ColorSpaceDouble half4(2.0, 2.0, 2.0, 2.0)
            #endif

            TEXTURE2D(_CloudTexture);

            CBUFFER_START(UnityPerMaterial)
                float _Exposure;
                float _SunSize;
                float _SunConvergence;
                float _AtmosphereThickness;
                float _OzoneAbsorption;
                float _AerosolHaze;
                float _GroundFade;
                float4 _SkyTint;
                float4 _GroundColor;
                float4 _NightSkyColor;
                float4 _SunDirection;
                float4 _SunColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.texcoord = input.positionOS.xyz;
                return output;
            }

            // Atmosphere Constants (Felix Westin analytical model)
            #define C_RAYLEIGH float3(5.8, 13.5, 33.1) * 1e-6
            #define C_MIE      float3(3.996, 3.996, 3.996) * 1e-6
            #define C_OZONE    float3(0.65, 1.88, 0.085) * 1e-6

            #define RAYLEIGH_MAX_LUM 2.5
            #define M_FAKE_MS        0.3
            #define M_AERIAL         2.5
            #define NIGHT_LIGHT      0.15

            static const float kPlanetRadius = 6371000.0;
            static const float kAtmosphereHeight = 100000.0;

            // HDRP Original Mie Constants for Sun Attenuation
            #define MIE_G (-0.990)
            #define MIE_G2 0.9801

            float GetMiePhaseHDRP(float eyeCos, float eyeCos2, float sunSize)
            {
                float temp = 1.0 + MIE_G2 - 2.0 * MIE_G * eyeCos;
                temp = pow(max(temp, 1.0e-4), pow(max(sunSize, 0.001), 0.65) * 10.0);
                return 1.5 * ((1.0 - MIE_G2) / (2.0 + MIE_G2)) * (1.0 + eyeCos2) / max(temp, 1.0e-4);
            }

            float CalcSunAttenuationHDRP(float3 lightPos, float3 ray, float sunSize, float sunConvergence)
            {
                float focusedEyeCos = pow(saturate(dot(lightPos, ray)), sunConvergence);
                return GetMiePhaseHDRP(-focusedEyeCos, focusedEyeCos * focusedEyeCos, sunSize);
            }

            float2 SphereIntersection(float3 rayDir, float3 sphereCenter, float sphereRadius)
            {
                float3 oc = -sphereCenter;
                float b = dot(oc, rayDir);
                float c = dot(oc, oc) - (sphereRadius * sphereRadius);
                float h = b * b - c;
                h = sqrt(max(0.0, h));
                return float2(-b - h, -b + h);
            }

            float PhaseRayleigh(float costh)
            {
                return (1.0 + costh * costh) * 0.06;
            }

            void GetRayleigh(float opticalDepth, float densityR, out float3 R)
            {
                R = (1.0 - exp(-opticalDepth * densityR * C_RAYLEIGH / RAYLEIGH_MAX_LUM)) * RAYLEIGH_MAX_LUM;
            }

            // Direct solar transmittance through atmosphere (computes sunset reddening with ozone control)
            float3 GetAtmosphereSunTransmittance(float3 lightDir, float density, float multiplier, float ozoneMultiplier)
            {
                float lightExtinctionAmount = exp(-(saturate(lightDir.y + 0.05) * 40.0)) +
                    exp(-(saturate(lightDir.y + 0.5) * 5.0)) * 0.4 +
                    pow(saturate(1.0 - lightDir.y), 2.0) * 0.02 +
                    0.002;

                return exp(-(C_RAYLEIGH + C_MIE + C_OZONE * ozoneMultiplier) * lightExtinctionAmount * density * multiplier * 1e6);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 rayDir = normalize(input.texcoord);
                float3 o_rayDir = rayDir;

                // Safe sun direction extraction
                float3 lightDir = _SunDirection.xyz;
                float lenSq = dot(lightDir, lightDir);
                if (lenSq < 0.001)
                    lightDir = float3(0.0, 0.7071, 0.7071);
                else
                    lightDir = lightDir * rsqrt(lenSq);

                float3 sunCol = _SunColor.rgb;
                if (dot(sunCol, sunCol) < 0.001)
                    sunCol = float3(1.0, 1.0, 1.0);

                float density = max(_AtmosphereThickness, 0.1) * max(_AerosolHaze, 0.1);
                float sunSize = clamp(_SunSize, 0.005, 0.2);
                float convergence = clamp(_SunConvergence, 1.0, 30.0);
                float ozone = max(_OzoneAbsorption, 0.0);

                // Planet spheres
                float3 planetCenter = float3(0.0, -kPlanetRadius, 0.0);
                float2 tAtmosphere = SphereIntersection(rayDir, planetCenter, kPlanetRadius + kAtmosphereHeight);

                float opticalDepth = min(tAtmosphere.y, 1e7 * M_AERIAL);

                // Height density
                float hbias = 1.0 - 1.0 / (2.0 + pow(min(1e7, tAtmosphere.y), 2.0) * 1e-12);
                float sqhbias = hbias * hbias;
                float densityR = sqhbias * density;

                // Sunset transmission: when lightDir.y is low, lightColor turns deep golden/red/magenta!
                float adjLightY = lightDir.y;
                float ly = adjLightY + saturate(-adjLightY + 0.02) * saturate(adjLightY + 0.7);
                ly = clamp(ly, -1.0, 1.0);
                float3 lightColor = GetAtmosphereSunTransmittance(float3(lightDir.x, ly, lightDir.z), density, hbias, 5.0 * ozone) * sunCol;

                // Atmospheric Rayleigh scattering
                float3 R;
                GetRayleigh(opticalDepth, densityR, R);

                float costh = dot(o_rayDir, lightDir);
                float phaseR = PhaseRayleigh(costh);

                // Apply sunset light color to Rayleigh scattering (glorious sunset glow / 晚霞!)
                float3 rayleigh = (phaseR + phaseR * M_FAKE_MS) * lightColor + NIGHT_LIGHT * phaseR;
                float3 scattering = rayleigh * R;

                // Modulate by SkyTint
                scattering *= _SkyTint.rgb * 2.0;

                // Night sky background modulated by user-configurable NightSkyColor
                float3 nightSkyColor = _NightSkyColor.rgb;
                scattering += nightSkyColor * smoothstep(0.1, -0.33, adjLightY);

                // Ground & Horizon transition with groundFade
                float fadeWidth = clamp(_GroundFade, 0.02, 1.0);
                float groundBlend = smoothstep(0.0, fadeWidth, saturate(-o_rayDir.y));
                float3 groundBase = _GroundColor.rgb * (saturate(lightDir.y * 2.0 + 0.2) * 0.6 + 0.1);
                scattering = lerp(scattering, groundBase, groundBlend);

                // --- Exact HDRP Sun Shape & Attenuation ---
                float sunAttenuation = CalcSunAttenuationHDRP(lightDir, o_rayDir, sunSize, convergence);
                float lightColorIntensity = max(length(sunCol), 0.25);
                float3 sunRadiance = 15.0 * saturate(lightColor) * sunCol / lightColorIntensity;
                float3 sunFinal = sunRadiance * sunAttenuation;

                float sunHorizonFade = saturate(1.0 - groundBlend * 2.0);
                scattering += sunFinal * sunHorizonFade;

                // Linear exposure
                float exposure = _Exposure <= 0.001 ? 1.0 : _Exposure;
                scattering *= exposure;

                // --- Direct Cloud Integration ---
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                half4 cloud = SAMPLE_TEXTURE2D_LOD(_CloudTexture, sampler_LinearClamp, screenUV, 0);

                // Composite cloud over sky (cloud is premultiplied RGB and alpha opacity)
                scattering = scattering * (1.0 - cloud.a) + cloud.rgb;

                return half4(scattering, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
