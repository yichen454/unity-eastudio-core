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

        [Header(Night Sky HDRI)]
        [NoScaleOffset] _NightSkyMap ("Night Sky Cubemap", Cube) = "" {}
        _NightExposure ("Night Sky Exposure", Float) = 1.0
        _NightRotation ("Night Sky Rotation", Range(0, 360)) = 0.0
        [HideInInspector] _HasNightSkyMap ("Has Night Sky Map", Float) = 0.0

        [Header(Moon)]
        [NoScaleOffset] _MoonTexture ("Moon Surface Texture", 2D) = "white" {}
        [HideInInspector] _MoonDirection ("Moon Direction", Vector) = (0, -0.707, -0.707, 0)
        [HideInInspector] _MoonLightLocal ("Moon Light Local Direction", Vector) = (0, 0, 1, 0)
        [HideInInspector] _MoonParams ("Moon Parameters (Size, Brightness, Earthshine, Halo)", Vector) = (0.06, 1.2, 0.04, 0.5)
        [HideInInspector] _MoonColor ("Moon Color", Color) = (0.92, 0.95, 1.0, 1)
        [HideInInspector] _EnableMoon ("Enable Moon", Float) = 1.0

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
            TEXTURECUBE(_NightSkyMap);
            SAMPLER(sampler_NightSkyMap);
            float4 _NightSkyMap_HDR;
            TEXTURE2D(_MoonTexture);
            SAMPLER(sampler_MoonTexture);

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

                float _NightExposure;
                float _NightRotation;
                float _HasNightSkyMap;
                float _HasClouds;

                float4 _MoonDirection;
                float4 _MoonLightLocal;
                float4 _MoonParams;
                float4 _MoonColor;
                float _EnableMoon;
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
            #define C_RAYLEIGH (float3(5.802, 13.558, 33.100) * 1e-6)
            #define C_MIE      (float3(3.996, 3.996, 3.996) * 1e-6)
            #define C_OZONE    (float3(0.650, 1.881, 0.085) * 1e-6)

            #define RAYLEIGH_MAX_LUM 2.5
            #define MIE_MAX_LUM      0.3
            #define M_FAKE_MS        0.3
            #define M_AERIAL         2.5
            #define NIGHT_LIGHT      0.15
            #define M_MIE            float3(0.95, 0.85, 0.75)
            #define M_OZONE2         5.0
            #define M_DENSITY_HEIGHT_MOD 1e-12

            static const float kPlanetRadius = 6371000.0;
            static const float kAtmosphereHeight = 100000.0;

            float CalcSunAttenuation(float3 lightPos, float3 ray, float sunSize, float sunConvergence)
            {
                float eyeCos = dot(lightPos, ray);
                if (eyeCos <= 0.0)
                    return 0.0;

                // Angular distance in radians: 2 * (1 - cos(theta)) ≈ theta^2
                float dist2 = max(0.0, 2.0 * (1.0 - eyeCos));
                float dist = sqrt(dist2);

                // Core sun radius (angular radius scaled 1:1 with moonRadius)
                float sunRadius = clamp(sunSize, 0.005, 0.2) * 0.40;
                float normDist = dist / max(sunRadius, 1e-5);

                // 1. Crisp Sun Disc with distinct limb boundary visible on all standard screens
                float discEdge = smoothstep(1.05, 0.94, normDist);
                float core = discEdge * (1.0 + 2.5 * saturate(1.0 - normDist * 0.85));

                // 2. Smooth Coronal Glow enveloping the sun disc
                float convergence = clamp(sunConvergence, 1.0, 30.0);
                float haloSpread = sunRadius * (14.0 / convergence);
                float halo = exp(-dist / max(haloSpread, 1e-4)) * 0.35;

                return core + halo;
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

            float3 RotateAroundY(float3 v, float deg)
            {
                float rad = deg * 0.0174532925;
                float s, c;
                sincos(rad, s, c);
                return float3(v.x * c - v.z * s, v.y, v.x * s + v.z * c);
            }

            float PhaseRayleigh(float costh)
            {
                return (1.0 + costh * costh) * 0.06;
            }

            float4 CalcMoon(float3 rayDir)
            {
                if (_EnableMoon < 0.5)
                    return float4(0, 0, 0, 0);

                float3 moonDir = _MoonDirection.xyz;
                float eyeCos = dot(rayDir, moonDir);
                // Early exit: covers wide atmospheric halo up to ~50 degrees (eyeCos > 0.65)
                if (eyeCos < 0.65)
                    return float4(0, 0, 0, 0);

                float lenSq = dot(moonDir, moonDir);
                if (lenSq < 0.001)
                    return float4(0, 0, 0, 0);
                moonDir = moonDir * rsqrt(lenSq);

                // Orthonormal basis for moon billboard projection
                float3 upRef = abs(moonDir.y) > 0.99 ? float3(0, 0, 1) : float3(0, 1, 0);
                float3 moonRight = normalize(cross(upRef, moonDir));
                float3 moonUp = cross(moonDir, moonRight);

                // Angular projection (scaled 1:1 with sunRadius for visually consistent SIZE)
                float2 uvOffset = float2(dot(rayDir, moonRight), dot(rayDir, moonUp)) / eyeCos;
                float moonRadius = clamp(_MoonParams.x, 0.008, 0.25) * 0.40;
                float2 normUV = uvOffset / moonRadius;
                float r2 = dot(normUV, normUV);

                float3 moonColor = float3(0, 0, 0);
                float moonMask = 0.0;

                // Inside the spherical moon disc
                if (r2 <= 1.05)
                {
                    float discEdge = smoothstep(1.02, 0.96, sqrt(r2));
                    float z = sqrt(saturate(1.0 - r2));
                    float3 localNormal = normalize(float3(normUV.x, normUV.y, z));

                    // NASA 2:1 equirectangular spherical UV mapping
                    float u = atan2(localNormal.x, localNormal.z) / (2.0 * 3.14159265) + 0.5;
                    float v = asin(clamp(localNormal.y, -1.0, 1.0)) / 3.14159265 + 0.5;
                    float3 moonAlbedo = SAMPLE_TEXTURE2D_LOD(_MoonTexture, sampler_LinearRepeat, float2(u, v), 0).rgb;

                    // Lunar phase terminator lighting + earthshine
                    float lambert = smoothstep(-0.05, 0.15, dot(localNormal, _MoonLightLocal.xyz));
                    float earthshine = _MoonParams.z * 0.15;
                    float shading = lambert + earthshine;

                    moonColor = moonAlbedo * shading * _MoonColor.rgb * _MoonParams.y * discEdge;
                    moonMask = discEdge;
                }

                // Expansive, multi-layer atmospheric moonlight halo
                float haloIntensity = _MoonParams.w;
                float wideHalo = pow(eyeCos, 16.0) * 0.20;
                float midHalo = pow(eyeCos, 60.0) * 0.35;
                float innerHalo = pow(eyeCos, 200.0) * 0.45;
                float moonHalo = (wideHalo + midHalo + innerHalo) * haloIntensity;
                float3 haloColor = moonHalo * _MoonColor.rgb * (1.0 - moonMask * 0.85);

                return float4(moonColor + haloColor, moonMask);
            }

            float PhaseM(float costh, float g)
            {
                g = min(g, 0.9381);
                float k = 1.55 * g - 0.55 * g * g * g;
                float a = 1.0 - k * k;
                float b = 12.57 * pow(1.0 - k * costh, 2.0);
                return a / max(b, 1e-4);
            }

            void GetRayleighMie(float opticalDepth, float densityR, float densityM, out float3 R, out float3 M)
            {
                R = (1.0 - exp(-opticalDepth * densityR * C_RAYLEIGH / RAYLEIGH_MAX_LUM)) * RAYLEIGH_MAX_LUM;
                M = (1.0 - exp(-opticalDepth * densityM * C_MIE / MIE_MAX_LUM)) * MIE_MAX_LUM;
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

                float fadeWidth = clamp(_GroundFade, 0.02, 1.0);
                float groundBlend = smoothstep(0.0, fadeWidth, saturate(-o_rayDir.y));

                // Early exit when looking below the horizon:
                // Skips 100% of atmospheric scattering, sphere intersections, and texture samples for the ground!
                if (groundBlend >= 0.999)
                {
                    float3 groundBase = _GroundColor.rgb * (saturate(_SunDirection.y * 2.0 + 0.2) * 0.6 + 0.1);
                    float exposure = _Exposure <= 0.001 ? 1.0 : _Exposure;
                    return half4(groundBase * exposure, 1.0);
                }

                // Safe sun direction extraction
                float3 lightDir = _SunDirection.xyz;
                float lenSq = dot(lightDir, lightDir);
                if (lenSq < 0.001)
                    lightDir = float3(0.0, 0.7071, 0.7071);
                else
                    lightDir = lightDir * rsqrt(lenSq);

                float density = max(_AtmosphereThickness, 0.1) * max(_AerosolHaze, 0.1);
                float sunSize = clamp(_SunSize, 0.005, 0.2);
                float convergence = clamp(_SunConvergence, 1.0, 30.0);
                float ozone = max(_OzoneAbsorption, 0.0);

                // Planet and atmosphere intersection (Fast Sky 2 exact method)
                float3 planetCenter = float3(0.0, -kPlanetRadius, 0.0);
                float2 t1 = SphereIntersection(rayDir, planetCenter, kPlanetRadius);
                float2 t2 = SphereIntersection(rayDir, planetCenter, kPlanetRadius + kAtmosphereHeight);

                float opticalDepth = min(t2.y, 1e7 * M_AERIAL);

                // Height density
                float hbias = 1.0 - 1.0 / (2.0 + pow(t2.y, 2.0) * M_DENSITY_HEIGHT_MOD);
                float sqhbias = hbias * hbias;
                float densityR = sqhbias * density;
                float densityM = sqhbias * sqhbias * hbias * max(_AerosolHaze, 0.1);

                // Sunset transmission: modulated directly by _SunColor (carrying Light color * intensity)
                float adjLightY = lightDir.y;
                float ly = adjLightY;
                ly += saturate(-adjLightY + 0.02) * saturate(adjLightY + 0.7);
                ly = clamp(ly, -1.0, 1.0);
                float3 sunTransmittance = GetAtmosphereSunTransmittance(float3(lightDir.x, ly, lightDir.z), density, hbias, M_OZONE2 * ozone);
                float3 lightColor = sunTransmittance * _SunColor.rgb;

                // Atmospheric Rayleigh & Mie scattering
                float3 R, M;
                GetRayleighMie(opticalDepth, densityR, densityM, R, M);

                float costh = dot(o_rayDir, lightDir);
                float phaseR = PhaseRayleigh(costh);
                float phaseM = PhaseM(costh, 0.88) * smoothstep(-0.35, -0.2, o_rayDir.y);

                // Apply forward illuminated sunlight to atmosphere
                float3 rayleigh = (phaseR + phaseR * M_FAKE_MS) * lightColor + NIGHT_LIGHT * phaseR;
                float3 mie = ((phaseM + phaseR * M_FAKE_MS) * lightColor + NIGHT_LIGHT * phaseR) * M_MIE;
                float3 scattering = mie * M + rayleigh * R;

                // Night sky transition: fades in as sun descends below horizon
                float nightWeight = smoothstep(0.04, -0.20, adjLightY);

                if (_HasNightSkyMap > 0.5)
                {
                    float3 rotatedNightDir = RotateAroundY(o_rayDir, _NightRotation);
                    float4 rawNightTex = SAMPLE_TEXTURECUBE_LOD(_NightSkyMap, sampler_LinearClamp, rotatedNightDir, 0);
                    float3 nightSkyHDR = DecodeHDREnvironment(rawNightTex, _NightSkyMap_HDR) * _NightExposure;

                    // Blend daytime atmospheric scattering smoothly into the starry HDRI backdrop
                    scattering = lerp(scattering, nightSkyHDR, nightWeight);
                }
                else
                {
                    scattering += _NightSkyColor.rgb * smoothstep(0.1, -0.33, adjLightY);
                }

                // Planet / ground atmospheric absorption
                if (t1.y > 0.0)
                {
                    float planetOpticalDepth = t1.y - max(0.0, t1.x);
                    float skyWeight = exp(-planetOpticalDepth * 1e-6);
                    scattering *= lerp(_GroundColor.rgb, 1.0, skyWeight);
                }

                // Modulate by SkyTint
                scattering *= _SkyTint.rgb * 2.0;

                // --- Moon rendering and background composition ---
                float4 moonData = CalcMoon(o_rayDir);
                scattering = scattering * (1.0 - moonData.a) + moonData.rgb;

                // --- Crisp Sun Shape & Tight Coronal Halo (Deep Space) ---
                float sunAttenuation = CalcSunAttenuation(lightDir, o_rayDir, sunSize, convergence);
                float3 sunRadiance = 6.0 * saturate(sunTransmittance) * _SunColor.rgb;
                float sunHorizonFade = saturate(1.0 - groundBlend * 2.0);
                float3 sunFinal = sunRadiance * sunAttenuation * sunHorizonFade;
                scattering += sunFinal;

                // --- Troposphere Cloud Deck Occlusion (covers Sun, Moon, and Deep Sky) ---
                if (_HasClouds > 0.5)
                {
                    float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                    half4 cloud = SAMPLE_TEXTURE2D_LOD(_CloudTexture, sampler_LinearClamp, screenUV, 0);

                    // Physical occlusion: dense clouds 100% block the solar disc and celestial backdrop;
                    // thin wisps softly and gracefully filter the sunlight.
                    float cloudTransmittance = saturate(1.0 - cloud.a);
                    scattering = scattering * cloudTransmittance + cloud.rgb;
                }

                // Synchronize ground transition: ground smoothly covers sky, clouds, and sun below horizon!
                float3 groundBase = _GroundColor.rgb * (saturate(lightDir.y * 2.0 + 0.2) * 0.6 + 0.1);
                scattering = lerp(scattering, groundBase, groundBlend);

                // Linear exposure
                float exposure = _Exposure <= 0.001 ? 1.0 : _Exposure;
                scattering *= exposure;

                return half4(scattering, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
