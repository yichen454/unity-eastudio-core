Shader "Hidden/EAStudio/CloudGenerator"
{
    Properties
    {
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "GenerateClouds"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.5
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_CloudMap);
            SAMPLER(sampler_CloudMap);

            CBUFFER_START(UnityPerMaterial)
                float4x4 _CloudCameraInvProjection[2];
                float4x4 _CloudCameraToWorld[2];
                float _CloudFlipY;
                float4 _SunDirection;
                float4 _MoonDirection;
                half4 _CloudSunColor;
                half4 _CloudMoonColor;
                half4 _CloudTint;
                float4 _CloudChannelWeights;
                float4 _CloudWindOffset;
                float _CloudAltitude;
                float _CloudHorizonFade;
                float _CloudRotation;
                float _CloudOpacity;
                float _CloudExposure;
                float _CloudRaymarching;
                float _CloudRaymarchingSteps;
                float _CloudRaymarchingDensity;
                float _CloudAmbientProbeDimmer;
                half4 _CloudAmbientColor;
                float _CloudMapTileSize;
                float _CloudUpperHemisphereOnly;
                float _CloudSunHorizonCos;
                float _CloudPlanetRadius;
                float _CloudDeckThickness;
            CBUFFER_END

            // Width of the deck's day/night terminator, as a rate in sin(elevation). The deck's own horizon is
            // a hard line in reality, but a 2D layer has no vertical extent to soften it, so the ramp is spread
            // over roughly one degree of sun elevation; HDRP uses a far tighter band because its shell is a
            // volume. Without any spread the whole deck pops on or off within a frame of the sun crossing.
            static const float kSunHorizonSoftness = 50.0;

            // HDRP caps the light's in-slab distance at 500 m so a grazing sun cannot saturate the march.
            static const float kMaxLightPath = 500.0;

            // HDRP's Henyey-Greenstein lobe split and multi-scattering octave weight. The second octave is
            // what keeps a dense cloud core from collapsing to black; both numbers are copied verbatim.
            static const float kForwardEccentricity = 0.7;
            static const float kBackwardEccentricity = 0.3;
            static const float kMultiScattering = 0.75;

            // Hard upper bound so the loop compiles to a bounded dynamic loop on all targets.
            static const int kMaxRaymarchSteps = 32;

            static const float kInvTwoPi = 0.15915494309189535;
            static const float kInvHalfPi = 0.63661977236758134;

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

            // Projects a direction already expressed in the deck frame onto the equirectangular map.
            // The map is a hemisphere laid out with v = 0 at the horizon and v = 1 at the zenith: its top
            // rows are the smooth grazing-angle haze band and its detail grows toward the bottom, which is
            // the signature of a map read upward from the skyline. Inverting this puts the deck overhead
            // upside down, so the ordering is load-bearing, not cosmetic.
            // A hemisphere map has no second hemisphere to read: its lower half is the upper one mirrored,
            // which is the only mapping that stays continuous through the horizon. When the layer is allowed
            // below the skyline that mirrored half is what gets drawn there.
            float2 DirectionToCloudUV(float3 deckDir)
            {
                float2 uv;
                uv.x = atan2(deckDir.x, deckDir.z) * kInvTwoPi + 0.5;
                float elevation = asin(clamp(deckDir.y, -1.0, 1.0)) * kInvHalfPi;
                uv.y = _CloudUpperHemisphereOnly > 0.5 ? saturate(elevation) : abs(elevation);
                return uv;
            }

            // The deck is a spherical shell of `_CloudDeckThickness` metres riding a planet of
            // `_CloudPlanetRadius`, not a flat card, so how far a ray travels inside it depends on how steeply
            // it enters: 800 m straight up against 29 km at the skyline for the default 2000 m deck. HDRP
            // takes every optical depth from that real chord, and the ratio is what makes a grazed deck go
            // opaque while an overhead one stays thin. A single representative length cannot: with one
            // constant the whole sky is equally dense, which is the flat-decal look.
            // Returns the chord through the shell; `rangeStart` receives the distance at which the ray
            // reaches the shell's near face.
            float CloudShellChord(float sinElevation, out float rangeStart)
            {
                float radiusPlanet = _CloudPlanetRadius;
                float radiusInner = radiusPlanet + _CloudAltitude;
                float radiusOuter = radiusInner + _CloudDeckThickness;
                float cosElevation = sqrt(max(1.0 - sinElevation * sinElevation, 0.0));

                // sqrt(a^2 - b^2) as (a - b)(a + b): with radii around 6.37e6 the direct form spends nearly
                // all of float32's mantissa on the a^2 term and the difference comes back quantised.
                float inner = sqrt(max((radiusInner - radiusPlanet * cosElevation)
                                     * (radiusInner + radiusPlanet * cosElevation), 0.0));
                float outer = sqrt(max((radiusOuter - radiusPlanet * cosElevation)
                                     * (radiusOuter + radiusPlanet * cosElevation), 0.0));

                // The camera stands on the sphere, so the shell's near face lies `inner` away along the ray;
                // the sine term is how much of that travel is horizontal.
                rangeStart = max(inner - radiusPlanet * sinElevation, 0.0);
                return max(outer - inner, 0.0);
            }

            // Henyey-Greenstein phase function. HDRP sums a forward lobe and a weaker backwards lobe, which
            // is what puts the silver lining on the sun side of every cloud and shades the side facing away.
            // This term is the single largest contributor to the deck reading as volume rather than as a
            // stencil: a texel aimed at the sun scatters about 25x more than one aimed across it.
            float HenyeyGreenstein(float g, float cosTheta)
            {
                float g2 = g * g;
                float denom = max(1.0 + g2 - 2.0 * g * cosTheta, 1e-4);
                return (1.0 - g2) / (4.0 * PI * denom * sqrt(denom));
            }

            // One light's scattering luminance before the light color is applied, in HDRP's two-octave form.
            // `cosLight` is the angle between the view ray and the light: both the phase function and the
            // path the light carves through the deck depend on it.
            float CloudLuminance(float cosLight, float opticalDepth)
            {
                float phase0 = HenyeyGreenstein(kForwardEccentricity, cosLight)
                             + HenyeyGreenstein(-kBackwardEccentricity, cosLight);
                float phase1 = HenyeyGreenstein(kForwardEccentricity * kMultiScattering, cosLight)
                             + HenyeyGreenstein(-kBackwardEccentricity * kMultiScattering, cosLight);

                // The second octave is the cheap stand-in for multiple scattering: it re-enters the medium
                // with a fraction of the extinction, so an optically thick core stays lit instead of going
                // black, which is exactly what the HDRP deck does.
                return exp(-opticalDepth) * phase0
                     + exp(-opticalDepth * kMultiScattering) * phase1 * kMultiScattering;
            }

            // Optical depth of the deck along one light direction, in HDRP's units (metres of extinction).
            // Each body needs its own march. HDRP only ever has the sun, but this layer also carries a moon,
            // and letting the moon reuse the sun's path is not a small approximation: once the sun drops
            // below the deck's horizon the sun ray finds no cloud to extinguish, so the moon term loses its
            // `exp(-opticalDepth)` entirely and every texel inside the moon's phase lobe comes out at the
            // same brightness -- a uniformly lit sheet with no cloud shape in it.
            float CloudLightOpticalDepth(float3 samplePos, float3 lightDir, float sigmaT,
                                         float mipLevel, float4 channelWeights,
                                         float cosYaw, float sinYaw, float windScroll)
            {
                float3 samplePosPlanet = samplePos + float3(0.0, _CloudPlanetRadius, 0.0);
                float radiusSample = length(samplePosPlanet);
                float radiusOuter = _CloudPlanetRadius + _CloudAltitude + _CloudDeckThickness;

                // Distance the light travels inside the shell from that point, read straight off the sphere the
                // same way HDRP's ExitCloudVolume does and capped so a grazing light cannot saturate the march.
                // The radicand is written as (R_out - r)(R_out + r) + (P.d)^2 rather than r_out^2 - r^2 + (P.d)^2
                // for the same float32 reason as CloudShellChord.
                float lightSin2 = max(radiusOuter - radiusSample, 0.0) * (radiusOuter + radiusSample);
                float lightPath = clamp(-dot(samplePosPlanet, lightDir)
                                      + sqrt(max(lightSin2 + dot(samplePosPlanet, lightDir) * dot(samplePosPlanet, lightDir), 0.0)),
                                        0.0, kMaxLightPath);

                float opticalDepth = 0.0;
                int steps = (int)_CloudRaymarchingSteps;
                if (steps > 0 && lightPath > 0.0)
                {
                    float stepSize = lightPath / (float)steps;
                    float extinction = 0.0;

                    [loop]
                    for (int i = 1; i <= kMaxRaymarchSteps; i++)
                    {
                        if (i > steps)
                            break;

                        // Walk the light ray in metres and re-project each sample onto the panorama, exactly as
                        // HDRP's EvaluateSunLuminance does. A straight line in texture space cannot stand in:
                        // azimuth is a circle, so any uv delta has to pick a branch and tears a seam at the
                        // light's antipode. A world position has no branch.
                        float3 marchedSample = samplePos + lightDir * (stepSize * (float)i);
                        float3 sampleDirWorld = marchedSample * rsqrt(max(dot(marchedSample, marchedSample), 1e-8));

                        // The deck's own band test: a sample only occludes when it falls inside the shell's
                        // dense band for the local coverage. HDRP's GetDensity returns 0 outside it, and that
                        // threshold is what gives the march its hard internal structure.
                        float sampleRangeStart;
                        float sampleRange = CloudShellChord(sampleDirWorld.y, sampleRangeStart);
                        float3 sampleDeckDir = float3(sampleDirWorld.x * cosYaw - sampleDirWorld.z * sinYaw,
                                                      sampleDirWorld.y,
                                                      sampleDirWorld.x * sinYaw + sampleDirWorld.z * cosYaw);
                        float2 stepUV = DirectionToCloudUV(sampleDeckDir) + float2(windScroll, 0.0);
                        half4 shadowTex = SAMPLE_TEXTURE2D_LOD(_CloudMap, sampler_CloudMap, stepUV, mipLevel);
                        float localThickness = dot(shadowTex, channelWeights);

                        float3 bandCenter = sampleDirWorld * (sampleRangeStart + 0.5 * sampleRange);
                        float distToCenter = length(bandCenter - marchedSample);
                        extinction += (distToCenter > sampleRange * localThickness) ? 0.0 : 0.1 * localThickness;
                    }

                    opticalDepth = stepSize * extinction * sigmaT;
                }

                return opticalDepth;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 rayUV = input.uv;
                rayUV.y = lerp(rayUV.y, 1.0 - rayUV.y, _CloudFlipY);
                float2 ndc = rayUV * 2.0 - 1.0;

                // Exact camera-relative world ray for this low-res texel. The composite skybox pass
                // samples this texture with the matching screen uv, so the uv -> NDC mapping is shared.
                uint eyeIndex = 0;
#if defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED)
                eyeIndex = unity_StereoEyeIndex;
#endif
                float4 viewPos = mul(_CloudCameraInvProjection[eyeIndex], float4(ndc, -1.0, 1.0));
                float3 rayDir = normalize(mul((float3x3)_CloudCameraToWorld[eyeIndex], viewPos.xyz));

                // --- Equirectangular projection ---
                // The cloud pass renders in screen space and the cloud map is an equirectangular hemisphere,
                // so every pixel is projected onto it through its own view ray. An altitude-shell
                // or tangent-plane projection must not be used with this map: it folds where the ray grazes
                // the deck, which collapses the sampled uv to a handful of texels (a flat smear) and tears a
                // hard edge along the fold.
                // _CloudRotation is supplied in radians and spins the panorama around the zenith.
                float cosYaw = cos(_CloudRotation);
                float sinYaw = sin(_CloudRotation);
                float3 deckDir = float3(rayDir.x * cosYaw - rayDir.z * sinYaw,
                                        rayDir.y,
                                        rayDir.x * sinYaw + rayDir.z * cosYaw);
                float2 cloudUV = DirectionToCloudUV(deckDir);

                // Wind drifts the panorama along the axis the artist selected. _CloudWindOffset already
                // carries the world-space wind vector projected onto the wrapping longitude axis by the host.
                // The scroll is kept separate from cloudUV because it slides the whole panorama past the
                // camera: it must move the sampled texel but must NOT rotate any direction derived from
                // cloudUV, or the sun would appear to orbit as the wind accumulates.
                float windScroll = _CloudWindOffset.x / _CloudMapTileSize;
                float2 sampleUV = float2(cloudUV.x + windScroll, cloudUV.y);

                // Upper hemisphere only: the deck stops at the horizon, because the sky above an observer at
                // ground level is what the layer stands in for and a ceiling visible from underneath reads as
                // a flat lid over the scene. With the flag off the layer is allowed to hang below the skyline
                // (see DirectionToCloudUV), which is HDRP's reading of the same switch.
                // `_CloudHorizonFade` is the transition's width in sin(elevation), so the deck dissolves into
                // the sky instead of clipping against it. The epsilon keeps smoothstep away from the
                // undefined edge0 == edge1 case; a Fade of 0 is then a hard edge rather than a NaN.
                float horizonFade = 1.0;
                if (_CloudUpperHemisphereOnly > 0.5)
                {
                    horizonFade = smoothstep(0.0, max(_CloudHorizonFade, 1e-5), rayDir.y);
                    if (horizonFade <= 0.0)
                        return half4(0.0, 0.0, 0.0, 0.0);
                }

                // Manual LOD so the azimuth compression near the zenith does not sparkle and the raymarch
                // reuses one gradient.
                float2 uvDX = ddx(sampleUV);
                float2 uvDY = ddy(sampleUV);
                float mipLevel = 0.5 * log2(max(max(dot(uvDX, uvDX), dot(uvDY, uvDY)), 1e-12));
                mipLevel = clamp(mipLevel, 0.0, 12.0);

                // --- Multi-channel opacity blending ---
                half4 cloudTex = SAMPLE_TEXTURE2D_LOD(_CloudMap, sampler_CloudMap, sampleUV, mipLevel);

                // HDRP normalises the four channel weights by their sum, so a texel can never be more than
                // fully covered no matter how many channels the artist enables.
                float4 channelWeights = _CloudChannelWeights / max(dot(_CloudChannelWeights, float4(1, 1, 1, 1)), 1.0);
                float coverage = saturate(dot(cloudTex, channelWeights));
                if (coverage <= 0.001)
                    return half4(0.0, 0.0, 0.0, 0.0);

                // A deck at `altitude` metres sits on a smaller sphere than the ground does, so its horizon dips
                // below the observer's and the sun keeps lighting it after local sunset. That dip is the whole
                // reason sunset clouds still glow, and `_CloudSunHorizonCos` is its cosine (HDRP's
                // ComputeCosineOfHorizonAngle evaluated at the deck's radius). Gating on the observer's horizon
                // instead extinguishes the deck the moment the sun touches the skyline, which is what made dusk
                // read as black silhouettes. Both bodies share the gate: a 2D layer has one terminator.
                float sunVisibility = saturate((_SunDirection.y - _CloudSunHorizonCos) * kSunHorizonSoftness);
                float moonVisibility = saturate((_MoonDirection.y - _CloudSunHorizonCos) * kSunHorizonSoftness);
                // The deck is lit by the same physical celestial radiance the dome binds, so the day/night
                // ramp, sunset warming and moonlit cloud tops stay consistent with the visible sky. HDRP
                // samples its ambient probe straight down: a cloud base is lit by whatever is below it.
                half3 ambientLight = _CloudAmbientColor.rgb * _CloudAmbientProbeDimmer;
                float globalCoverage = _CloudOpacity * horizonFade;

                half3 color;
                float alpha;
                if (_CloudRaymarching < 0.5)
                {
                    // Lighting off: HDRP bakes the raw map value instead, so the deck is a flat, unlit grey
                    // that only the light color and the ambient probe shape.
                    float flatCoverage = coverage * globalCoverage;
                    half3 flatLight = (_CloudSunColor.rgb * sunVisibility + _CloudMoonColor.rgb * (moonVisibility * 0.8)
                                     + ambientLight) * _CloudTint.rgb * _CloudExposure;
                    return half4(flatLight * flatCoverage, flatCoverage);
                }

                // HDRP's extinction coefficient for the deck at this texel, from Density (its `thickness`).
                float sigmaT = _CloudRaymarchingDensity * 0.095 + 0.005;

                // How far this ray travels inside the shell. Everything below is HDRP's, in metres: the near
                // face is where the deck starts and the chord is how much of it the ray crosses.
                float deckRangeStart;
                float deckRange = CloudShellChord(rayDir.y, deckRangeStart);

                // Transmittance of the deck along the view ray: HDRP's `opacity` enters twice, once as the
                // density and once as the slab thickness. Because `deckRange` grows from 800 m overhead to
                // 29 km at the skyline, a grazed deck turns solid while an overhead one stays translucent --
                // the vertical density gradient a shell has and a flat card cannot.
                float viewOpticalDepth = 0.1 * sigmaT * deckRange * coverage * coverage;
                float transmittance = exp(-viewOpticalDepth);
                float scatterAmount = 1.0 - transmittance;

                float sunCos = dot(rayDir, _SunDirection.xyz);

                // Where in the shell this texel is shaded. HDRP places the scattering point `0.5 * (1 - opacity)`
                // of the way in, so a solid texel is lit from its base and a wispy one from its middle; that
                // offset is what puts a bright rim under a dark top instead of shading the whole texel with a
                // single number.
                float3 samplePos = rayDir * (deckRangeStart + 0.5 * (1.0 - coverage) * deckRange);
                // Only the bodies above the deck's horizon are marched. A body that is gated off contributes
                // nothing, and its march would be wasted work on a path that leaves the shell immediately.
                float sunOpticalDepth = sunVisibility > 0.0
                    ? CloudLightOpticalDepth(samplePos, _SunDirection.xyz, sigmaT, mipLevel, channelWeights, cosYaw, sinYaw, windScroll)
                    : 0.0;
                float moonOpticalDepth = moonVisibility > 0.0
                    ? CloudLightOpticalDepth(samplePos, _MoonDirection.xyz, sigmaT, mipLevel, channelWeights, cosYaw, sinYaw, windScroll)
                    : 0.0;

                // HDRP applies the tint and the exposure to the direct term only; the ambient probe is left
                // alone because it is already the radiance the sky hands back.
                half3 directLight = _CloudSunColor.rgb * sunVisibility * CloudLuminance(sunCos, sunOpticalDepth)
                                  + _CloudMoonColor.rgb * (moonVisibility * 0.8) * CloudLuminance(dot(rayDir, _MoonDirection.xyz), moonOpticalDepth);
                directLight *= _CloudTint.rgb * _CloudExposure;

                // Premultiplied output consumed by the skybox composite: rgb = inscatter, a = opacity.
                color = (directLight + ambientLight) * scatterAmount * globalCoverage;
                alpha = scatterAmount * globalCoverage;
                return half4(color, alpha);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
