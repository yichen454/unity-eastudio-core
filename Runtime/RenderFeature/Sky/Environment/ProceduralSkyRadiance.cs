using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// CPU mirror of the atmospheric dome evaluated by <c>SkyboxProceduralSky.shader</c>.
    /// <para>
    /// The ambient probe and the dome that the player actually sees must come from one model, otherwise the scene
    /// is lit by a sky that does not exist on screen. Rendering the skybox into a cubemap and projecting it would
    /// avoid the duplication below, but it costs a GPU render plus a readback on every sun change, so the model is
    /// mirrored on the CPU instead.
    /// </para>
    /// <para>
    /// WARNING: this is a mirror, not the source of truth. Any change to the dome math in
    /// <c>SkyboxProceduralSky.shader</c> (constants, term ordering, clamps) must be applied here as well.
    /// Clouds are intentionally excluded: they are composited from a screen-space texture by a separate pass and are
    /// likewise absent from Unity's own Skybox ambient probe.
    /// The textured lunar disc is excluded too (see <c>MoonHalo</c>); only its analytic halo is mirrored.
    /// </para>
    /// <para>
    /// Inputs are the raw Volume parameter values (gamma/sRGB authoring space). The shader never sees those: before
    /// the uniforms reach the GPU, <see cref="Material.SetColor"/> decodes <c>Color</c> properties and the
    /// <c>[Gamma]</c> tag decodes floats such as <c>_Exposure</c>. <see cref="Evaluate"/> applies the same decode,
    /// so callers pass Volume values straight through and never pre-convert.
    /// </para>
    /// </summary>
    public struct ProceduralSkyRadiance
    {
        // --- Constants mirrored from SkyboxProceduralSky.shader ---
        private static readonly Vector3 kRayleigh = new Vector3(5.802e-6f, 13.558e-6f, 33.100e-6f);
        private static readonly Vector3 kOzone = new Vector3(0.650e-6f, 1.881e-6f, 0.085e-6f);
        private static readonly Vector3 kMieColor = new Vector3(0.95f, 0.85f, 0.75f);

        private const float kMie = 3.996e-6f;
        private const float kRayleighMaxLum = 2.5f;
        private const float kMieMaxLum = 0.3f;
        private const float kFakeMultiScatter = 0.3f;
        private const float kAerialPerspective = 2.5f;
        private const float kNightLight = 0.15f;
        private const float kOzoneMultiplier = 5.0f;
        private const float kDensityHeightMod = 1e-12f;
        private const float kSolarIrradiance = 3.0f;
        private const float kSunDiscRadiance = 6.0f;
        private const float kPlanetRadius = 6371000.0f;
        private const float kAtmosphereHeight = 100000.0f;
        private const int kSunDiscSamples = 256;

        public Vector3 sunDirection;
        public float atmosphereThickness;
        public float aerosolHaze;
        public float ozoneAbsorption;
        public float skyBrightness;
        public float sunBrightness;
        public float sunSize;
        public float sunConvergence;
        /// <summary>Gamma-space Volume value; decoded inside <see cref="Evaluate"/> like <c>Material.SetColor</c> does.</summary>
        public Color skyTint;
        /// <summary>Gamma-space Volume value; decoded inside <see cref="Evaluate"/> like <c>Material.SetColor</c> does.</summary>
        public Color groundColor;
        /// <summary>Gamma-space Volume value; decoded inside <see cref="Evaluate"/> like <c>Material.SetColor</c> does.</summary>
        public Color nightSkyColor;
        /// <summary>Gamma-space Volume value; decoded inside <see cref="Evaluate"/> like <c>Material.SetColor</c> does.</summary>
        public Color sunColor;
        public float groundFade;
        /// <summary>Gamma-space Volume value; decoded inside <see cref="Evaluate"/> like the shader's <c>[Gamma] _Exposure</c>.</summary>
        public float exposure;

        /// <summary>Mirrors <c>_EnableMoon</c>: gates the moon halo below.</summary>
        public bool enableMoon;
        /// <summary>Direction of the moon (<c>_MoonDirection</c>).</summary>
        public Vector3 moonDirection;
        /// <summary>Gamma-space moon colour, already tinted by the moonlight (<c>_MoonColor</c>).</summary>
        public Color moonColor;
        /// <summary>Moon halo intensity (<c>_MoonParams.w</c>).</summary>
        public float moonHaloIntensity;
        /// <summary>Sine of the moon's rise band width in degrees (<c>_MoonRiseFade</c>).</summary>
        public float moonRiseFade;

        /// <summary>
        /// When a night HDRI cubemap is assigned the shader replaces the flat night colour with it. This mirror then
        /// returns the daytime terms only, so the caller can blend the projected cubemap SH itself.
        /// </summary>
        public bool hasNightSkyMap;

        /// <summary>Linear HDR radiance of the dome along <paramref name="rayDir"/>, matching the skybox fragment shader.</summary>
        public Vector3 Evaluate(Vector3 rayDir)
        {
            Vector3 ray = NormalizeRay(rayDir);
            return EvaluateAmbient(ray) + EvaluateSunDisc(ray);
        }

        /// <summary>
        /// Every smooth term of the shader: atmosphere, night colour, moon halo and ground, with the planet blend
        /// already applied. The sun disc is excluded because it is a sub-degree feature: a low-discrepancy sphere pass
        /// still aliases it at tens of thousands of samples, so <see cref="ProjectAmbient"/> projects it separately.
        /// </summary>
        public Vector3 EvaluateAmbient(Vector3 rayDir)
        {
            float length = rayDir.magnitude;
            rayDir = length > 1e-6f ? rayDir / length : Vector3.up;

            float fadeWidth = Mathf.Clamp(groundFade, 0.02f, 1.0f);
            float groundBlend = SmoothStep(0f, fadeWidth, Mathf.Clamp01(-rayDir.y));

            // Same decode the material applies before the uniforms reach the GPU (see the type remarks).
            Vector3 groundColorLinear = GammaToLinear(groundColor);
            Vector3 nightSkyColorLinear = GammaToLinear(nightSkyColor);
            Vector3 moonColorLinear = GammaToLinear(moonColor);
            float exposureLinear = GammaToLinear(exposure);
            float exposureValue = exposureLinear <= 0.001f ? 1f : exposureLinear;

            Vector3 lightDir = NormalizeSunDirection();

            Vector3 groundBase = groundColorLinear * (Mathf.Clamp01(lightDir.y * 2f + 0.2f) * 0.6f + 0.1f);

            if (groundBlend >= 0.999f)
                return groundBase * exposureValue;

            float thickness = Mathf.Max(atmosphereThickness, 0f);
            float haze = Mathf.Max(aerosolHaze, 0f);
            float ozone = Mathf.Max(ozoneAbsorption, 0f);
            float sunSizeValue = Mathf.Max(sunSize, 0f);
            float sunBrightnessValue = Mathf.Max(sunBrightness, 0f);
            float skyBrightnessValue = Mathf.Max(skyBrightness, 0f);

            Vector3 planetCenter = new Vector3(0f, -kPlanetRadius, 0f);
            Vector2 t1 = SphereIntersection(rayDir, planetCenter, kPlanetRadius);
            Vector2 t2 = SphereIntersection(rayDir, planetCenter, kPlanetRadius + kAtmosphereHeight);

            float opticalDepth = Mathf.Min(t2.y, 1e7f * kAerialPerspective);

            float hbias = 1f - 1f / (2f + t2.y * t2.y * kDensityHeightMod);
            float sqhbias = hbias * hbias;
            float densityR = sqhbias * thickness;
            float densityM = sqhbias * sqhbias * hbias * thickness * haze;

            float adjLightY = AdjustedLightY(lightDir);
            float ly = adjLightY;

            Vector3 sunTransmittance = AtmosphereTransmittance(new Vector3(lightDir.x, ly, lightDir.z), thickness, haze, hbias, kOzoneMultiplier * ozone);
            Vector3 lightColor = sunTransmittance * kSolarIrradiance;

            float costh = Vector3.Dot(rayDir, lightDir);
            float phaseR = PhaseRayleigh(costh);
            float phaseM = PhaseM(costh, 0.88f) * SmoothStep(-0.35f, -0.2f, rayDir.y);

            float forwardMie = 1f + (phaseM - 1f) * sunBrightnessValue;
            float nightPhase = kNightLight * phaseR;

            Vector3 rayleigh = (phaseR + phaseR * kFakeMultiScatter) * lightColor + new Vector3(nightPhase, nightPhase, nightPhase);
            Vector3 mie = (forwardMie + phaseR * kFakeMultiScatter) * lightColor * sunBrightnessValue
                + new Vector3(nightPhase, nightPhase, nightPhase);
            mie = Vector3.Scale(mie, kMieColor);

            Vector3 scattering = Vector3.Scale(mie, MieTerm(opticalDepth, densityM))
                + Vector3.Scale(rayleigh, RayleighTerm(opticalDepth, densityR));
            scattering *= skyBrightnessValue;

            if (!hasNightSkyMap)
                scattering += nightSkyColorLinear * SmoothStep(0.1f, -0.33f, adjLightY);

            // Planet absorption for rays that graze or fall below the horizon.
            if (t1.y > 0f)
            {
                float skyWeight = Mathf.Exp(-(t1.y - Mathf.Max(0f, t1.x)) * 1e-6f);
                Vector3 attenuation = groundColorLinear * (1f - skyWeight) + new Vector3(skyWeight, skyWeight, skyWeight);
                scattering = Vector3.Scale(scattering, attenuation);
            }

            scattering = Vector3.Scale(scattering, SkyTintFactor(skyTint));

            // Moon halo. The textured lunar disc is deliberately not mirrored: it needs a CPU-readable texture and its
            // solid angle is ~1e-4 sr, i.e. under 0.1% of L0, while the analytic halo is worth several percent.
            // It carries the same atmospheric transmittance and horizon fade as the sun disc (see EvaluateSunDisc), so
            // the moon reddens and dims while rising instead of appearing at full brightness above the skyline.
            if (enableMoon && moonHaloIntensity > 0f)
            {
                Vector3 moonDir = NormalizeMoonDirection();
                Vector3 moonTransmittance = AtmosphereTransmittance(
                    new Vector3(moonDir.x, AdjustedLightY(moonDir), moonDir.z), thickness, haze, hbias, kOzoneMultiplier * ozone);
                float moonHorizonFade = Mathf.Clamp01(1f - groundBlend * 2f);
                // Mirrors the shader: the rise band is keyed to the moon's own elevation, because groundBlend only
                // knows where the viewer looks and is 0 everywhere above the skyline.
                float moonRise = SmoothStep(0f, Mathf.Max(moonRiseFade, 1e-5f), AdjustedLightY(moonDir));
                scattering += Vector3.Scale(MoonHalo(rayDir, moonDir, moonColorLinear), Saturate(moonTransmittance) * (moonHorizonFade * moonRise));
            }

            scattering = Vector3.Lerp(scattering, groundBase, groundBlend);
            return scattering * exposureValue;
        }

        /// <summary>
        /// Contribution of the sun disc and its coronal halo to the final pixel, after the horizon fade and the ground
        /// blend, so that <c>EvaluateAmbient + EvaluateSunDisc</c> reproduces the shader exactly.
        /// </summary>
        public Vector3 EvaluateSunDisc(Vector3 rayDir)
        {
            float length = rayDir.magnitude;
            rayDir = length > 1e-6f ? rayDir / length : Vector3.up;

            float sunSizeValue = Mathf.Max(sunSize, 0f);
            if (sunSizeValue <= 0.00001f)
                return Vector3.zero;

            float fadeWidth = Mathf.Clamp(groundFade, 0.02f, 1.0f);
            float groundBlend = SmoothStep(0f, fadeWidth, Mathf.Clamp01(-rayDir.y));
            if (groundBlend >= 0.999f)
                return Vector3.zero;

            Vector3 lightDir = NormalizeSunDirection();
            float hbias = HeightBias(rayDir);

            float sunBrightnessValue = Mathf.Max(sunBrightness, 0f);
            float thickness = Mathf.Max(atmosphereThickness, 0f);
            float haze = Mathf.Max(aerosolHaze, 0f);
            float ozone = Mathf.Max(ozoneAbsorption, 0f);

            Vector3 sunTransmittance = AtmosphereTransmittance(
                new Vector3(lightDir.x, AdjustedLightY(lightDir), lightDir.z), thickness, haze, hbias, kOzoneMultiplier * ozone);

            float sunAttenuation = CalcSunAttenuation(lightDir, rayDir, sunSizeValue, Mathf.Clamp(sunConvergence, 1f, 30f));
            if (sunAttenuation <= 0f)
                return Vector3.zero;

            Vector3 sunRadiance = kSunDiscRadiance * sunBrightnessValue * Vector3.Scale(Saturate(sunTransmittance), GammaToLinear(sunColor));

            // Matches the shader: the disc is faded near the horizon and then attenuated by the ground blend.
            float exposureValue = GammaToLinear(exposure);
            exposureValue = exposureValue <= 0.001f ? 1f : exposureValue;
            return sunRadiance * (sunAttenuation * Mathf.Clamp01(1f - groundBlend * 2f) * (1f - groundBlend) * exposureValue);
        }

        /// <summary>
        /// Angular radius of the cone that contains the sun disc and its coronal halo, i.e. where the disc term is
        /// non-negligible. <see cref="ProjectAmbient"/> samples this cone to integrate the disc without aliasing.
        /// </summary>
        internal float SunDiscConeAngle()
        {
            float sunRadius = Mathf.Max(sunSize, 0f) * 0.40f;
            float haloSpread = sunRadius * (14f / Mathf.Clamp(sunConvergence, 1f, 30f));
            return Mathf.Min(4.6f * haloSpread + 1.05f * sunRadius, Mathf.PI * 0.5f);
        }

        /// <summary>
        /// Projects the dome radiance into <see cref="RenderSettings.ambientProbe"/> space, i.e. the same SH that
        /// Unity produces for a Skybox ambient mode probe. The coefficients are equal to the physical
        /// <c>integral(L * Ylm)</c> scaled by the clamped-cosine convolution coefficient and the basis constant.
        /// </summary>
        /// <remarks>
        /// Delegates the basis and the clamped-cosine convolution to <see cref="SphericalHarmonicsUtils"/>, so the dome
        /// probe and the night HDRI probe it gets blended with are built in one convention.
        /// </remarks>
        /// <param name="sampleCount">Number of Fibonacci-sphere samples. 512 keeps the L0/L1 error well under a percent.</param>
        public SphericalHarmonicsL2 ProjectAmbient(int sampleCount)
        {
            sampleCount = Mathf.Max(sampleCount, 64);

            SphericalHarmonicsL2 sh = new SphericalHarmonicsL2();
            float sphereSolidAngle = 4f * Mathf.PI / sampleCount;

            for (int i = 0; i < sampleCount; i++)
            {
                Vector3 dir = SphericalHarmonicsUtils.SphereSample(i, sampleCount);
                SphericalHarmonicsUtils.AccumulateAmbient(ref sh, EvaluateAmbient(dir), dir, sphereSolidAngle);
            }

            ProjectSunDisc(ref sh);
            return sh;
        }

        /// <summary>
        /// Integrates the sun disc over the cap that contains it and its coronal halo. The disc is a sub-degree emitter
        /// whose radiance is two orders of magnitude above the sky's, so a uniform sphere pass either misses it or
        /// overshoots as the sun drifts between samples; concentrating the samples in the cap removes that noise for a
        /// fraction of the cost of the sphere pass.
        /// </summary>
        private void ProjectSunDisc(ref SphericalHarmonicsL2 sh)
        {
            float coneAngle = SunDiscConeAngle();
            if (coneAngle <= 0f)
                return;

            Vector3 axis = NormalizeSunDirection();
            float cosCone = Mathf.Cos(coneAngle);
            float weight = (2f * Mathf.PI * (1f - cosCone)) / kSunDiscSamples;
            float goldenAngle = Mathf.PI * (3f - Mathf.Sqrt(5f));

            Vector3 upRef = Mathf.Abs(axis.y) > 0.99f ? Vector3.forward : Vector3.up;
            Vector3 right = Vector3.Normalize(Vector3.Cross(upRef, axis));
            Vector3 up = Vector3.Cross(axis, right);

            for (int i = 0; i < kSunDiscSamples; i++)
            {
                float cosTheta = 1f - (i + 0.5f) * (1f - cosCone) / kSunDiscSamples;
                float sinTheta = Mathf.Sqrt(Mathf.Max(0f, 1f - cosTheta * cosTheta));
                float phi = i * goldenAngle;
                Vector3 dir = right * (Mathf.Cos(phi) * sinTheta) + up * (Mathf.Sin(phi) * sinTheta) + axis * cosTheta;

                SphericalHarmonicsUtils.AccumulateAmbient(ref sh, EvaluateSunDisc(dir), dir, weight);
            }
        }

        /// <summary>
        /// Solar elevation the shader uses for the night transition and for the sunlight transmittance: below the
        /// horizon the raw elevation is lifted so the terminator band does not collapse onto the horizon line.
        /// </summary>
        public static float AdjustedLightY(Vector3 lightDir)
        {
            float y = lightDir.y;
            return Mathf.Clamp(y + Mathf.Clamp01(-y + 0.02f) * Mathf.Clamp01(y + 0.7f), -1f, 1f);
        }

        /// <summary>
        /// Blend factor of the night sky (flat colour or HDRI) over the daytime dome, mirroring the shader's
        /// <c>smoothstep(0.04, -0.20, adjLightY)</c>. Shared with the controller so the ambient probe switches at the
        /// same rate the dome does.
        /// </summary>
        public static float NightWeight(Vector3 lightDir)
        {
            return SmoothStep(0.04f, -0.20f, AdjustedLightY(lightDir));
        }

        /// <summary>
        /// Chromatic transmittance the atmosphere applies to the sunlight along <paramref name="lightDir"/>,
        /// evaluated on the light's own path instead of the view ray the dome folds into <c>lightColor</c>.
        /// <para>
        /// A cloud has no atmosphere below it: the sunlight that lights the deck has already crossed the whole
        /// column, so it reddens and dies with the sun exactly as the dome's <c>lightColor</c> does. Anything
        /// that treats the deck's light as colour-only keeps it at its daytime hue while the sky around it goes
        /// dark, which is what makes a low sun's clouds read as a uniformly bright sheet instead of embers.
        /// HDRP applies the same term to its 2D cloud layer (PhysicallyBasedSky's
        /// <c>EvaluateSunColorAttenuation</c>); the density model here is the one this dome already uses.
        /// </para>
        /// </summary>
        public static Vector3 SunTransmittance(Vector3 lightDir, float atmosphereThickness, float aerosolHaze, float ozoneAbsorption)
        {
            Vector3 dir = lightDir;
            float lengthSq = dir.sqrMagnitude;
            dir = lengthSq < 0.001f ? Vector3.up : dir * (1f / Mathf.Sqrt(lengthSq));

            float hbias = HeightBias(dir);
            Vector3 adjusted = new Vector3(dir.x, AdjustedLightY(dir), dir.z);
            return AtmosphereTransmittance(
                adjusted,
                Mathf.Max(atmosphereThickness, 0f),
                Mathf.Max(aerosolHaze, 0f),
                hbias,
                kOzoneMultiplier * Mathf.Max(ozoneAbsorption, 0f));
        }

        private static Vector3 NormalizeRay(Vector3 rayDir)
        {
            float length = rayDir.magnitude;
            return length > 1e-6f ? rayDir / length : Vector3.up;
        }

        private Vector3 NormalizeSunDirection()
        {
            Vector3 lightDir = sunDirection;
            float lengthSq = lightDir.sqrMagnitude;
            return lengthSq < 0.001f ? new Vector3(0f, 0.7071f, 0.7071f) : lightDir / Mathf.Sqrt(lengthSq);
        }

        /// <summary>
        /// Normalised moon direction, or <see cref="Vector3.zero"/> when no moon is configured. Shared by the halo and
        /// by the atmospheric attenuation so both look down the same direction the shader does.
        /// </summary>
        private Vector3 NormalizeMoonDirection()
        {
            Vector3 moonDir = moonDirection;
            float lengthSq = moonDir.sqrMagnitude;
            return lengthSq < 0.001f ? Vector3.zero : moonDir / Mathf.Sqrt(lengthSq);
        }

        /// <summary>Atmospheric density bias of a ray (the shader's <c>hbias</c>), which also drives the sun extinction.</summary>
        private static float HeightBias(Vector3 rayDir)
        {
            Vector2 t2 = SphereIntersection(rayDir, new Vector3(0f, -kPlanetRadius, 0f), kPlanetRadius + kAtmosphereHeight);
            return 1f - 1f / (2f + t2.y * t2.y * kDensityHeightMod);
        }

        /// <summary>
        /// Linear factor the shader applies to everything it emits (<c>scattering *= _SkyTint.rgb * 2.0</c>), including
        /// the night HDRI it blends in beforehand. Exposed so the controller can tint the night probe identically.
        /// </summary>
        public static Vector3 SkyTintFactor(Color skyTint)
        {
            return GammaToLinear(skyTint) * 2f;
        }

        // Unity only applies these decodes in linear colour space; the rule lives in one place with the probe conventions.
        private static float GammaToLinear(float value)
        {
            return SphericalHarmonicsUtils.DecodeGamma(value);
        }

        private static Vector3 GammaToLinear(Color color)
        {
            return SphericalHarmonicsUtils.DecodeGamma(color);
        }

        private static Vector3 Saturate(Vector3 v)
        {
            return new Vector3(Mathf.Clamp01(v.x), Mathf.Clamp01(v.y), Mathf.Clamp01(v.z));
        }

        private static float SmoothStep(float edge0, float edge1, float x)
        {
            float span = edge1 - edge0;
            if (Mathf.Abs(span) < 1e-8f)
                return x < edge0 ? 0f : 1f;
            float t = Mathf.Clamp01((x - edge0) / span);
            return t * t * (3f - 2f * t);
        }

        private static Vector2 SphereIntersection(Vector3 rayDir, Vector3 sphereCenter, float sphereRadius)
        {
            Vector3 oc = -sphereCenter;
            float b = Vector3.Dot(oc, rayDir);
            float c = Vector3.Dot(oc, oc) - sphereRadius * sphereRadius;
            float h = Mathf.Sqrt(Mathf.Max(0f, b * b - c));
            return new Vector2(-b - h, -b + h);
        }

        private static float PhaseRayleigh(float costh)
        {
            return (1f + costh * costh) * 0.06f;
        }

        private static float PhaseM(float costh, float g)
        {
            g = Mathf.Min(g, 0.9381f);
            float k = 1.55f * g - 0.55f * g * g * g;
            float a = 1f - k * k;
            float oneMinus = 1f - k * costh;
            float b = 12.57f * oneMinus * oneMinus;
            return a / Mathf.Max(b, 1e-4f);
        }

        private static Vector3 RayleighTerm(float opticalDepth, float densityR)
        {
            Vector3 a = kRayleigh * (opticalDepth * densityR / kRayleighMaxLum);
            return new Vector3(
                (1f - Mathf.Exp(-a.x)) * kRayleighMaxLum,
                (1f - Mathf.Exp(-a.y)) * kRayleighMaxLum,
                (1f - Mathf.Exp(-a.z)) * kRayleighMaxLum);
        }

        private static Vector3 MieTerm(float opticalDepth, float densityM)
        {
            float value = (1f - Mathf.Exp(-(opticalDepth * densityM * kMie / kMieMaxLum))) * kMieMaxLum;
            return new Vector3(value, value, value);
        }

        private static Vector3 AtmosphereTransmittance(Vector3 lightDir, float thickness, float haze, float multiplier, float ozoneMultiplier)
        {
            float lightDirY = lightDir.y;
            float extinctionAmount =
                Mathf.Exp(-(Mathf.Clamp01(lightDirY + 0.05f) * 40f)) +
                Mathf.Exp(-(Mathf.Clamp01(lightDirY + 0.5f) * 5f)) * 0.4f +
                Mathf.Pow(Mathf.Clamp01(1f - lightDirY), 2f) * 0.02f +
                0.002f;

            Vector3 coefficient = kRayleigh * thickness
                + new Vector3(kMie * thickness * haze, kMie * thickness * haze, kMie * thickness * haze)
                + kOzone * (ozoneMultiplier * thickness);
            Vector3 extinction = coefficient * (extinctionAmount * multiplier * 1e6f);
            return new Vector3(Mathf.Exp(-extinction.x), Mathf.Exp(-extinction.y), Mathf.Exp(-extinction.z));
        }

        /// <summary>
        /// Mirror of the analytic part of the shader's <c>CalcMoon</c>: the multi-layer atmospheric halo that
        /// surrounds the moon. It is what makes the moon matter to the probe at all (several percent of L0, a fifth of
        /// L1 at these defaults); the disc itself is skipped, see the type remarks.
        /// </summary>
        private Vector3 MoonHalo(Vector3 rayDir, Vector3 moonDir, Vector3 moonColorLinear)
        {
            if (moonDir == Vector3.zero)
                return Vector3.zero;

            float eyeCos = Vector3.Dot(rayDir, moonDir);
            if (eyeCos < 0.65f)
                return Vector3.zero;

            float halo = Mathf.Pow(eyeCos, 16f) * 0.20f
                + Mathf.Pow(eyeCos, 60f) * 0.35f
                + Mathf.Pow(eyeCos, 200f) * 0.45f;

            return moonColorLinear * (halo * moonHaloIntensity);
        }

        private static float CalcSunAttenuation(Vector3 lightPos, Vector3 ray, float sunSize, float sunConvergence)
        {
            if (sunSize <= 0.00001f)
                return 0f;

            float eyeCos = Vector3.Dot(lightPos, ray);
            if (eyeCos <= 0f)
                return 0f;

            float dist = Mathf.Sqrt(Mathf.Max(0f, 2f * (1f - eyeCos)));
            float sunRadius = sunSize * 0.40f;
            float normDist = dist / Mathf.Max(sunRadius, 1e-5f);

            float discEdge = SmoothStep(1.05f, 0.94f, normDist);
            float core = discEdge * (1f + 2.5f * Mathf.Clamp01(1f - normDist * 0.85f));

            float haloSpread = sunRadius * (14f / Mathf.Clamp(sunConvergence, 1f, 30f));
            float halo = Mathf.Exp(-dist / Mathf.Max(haloSpread, 1e-4f)) * 0.35f;

            return core + halo;
        }
    }
}
