using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Manages procedural atmospheric sky material uniforms, moon/lunar phase projection, and analytical Trilight ambient SH synthesis.
    /// </summary>
    public class ProceduralSkyController
    {
        private const string k_ProceduralPath = "Skybox/EAStudio/ProceduralSky";

        private static readonly int s_ExposureID = Shader.PropertyToID("_Exposure");
        private static readonly int s_SunDirectionID = Shader.PropertyToID("_SunDirection");
        private static readonly int s_SunColorID = Shader.PropertyToID("_SunColor");
        private static readonly int s_SunSizeID = Shader.PropertyToID("_SunSize");
        private static readonly int s_SunConvergenceID = Shader.PropertyToID("_SunConvergence");
        private static readonly int s_AtmosphereThicknessID = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int s_OzoneAbsorptionID = Shader.PropertyToID("_OzoneAbsorption");
        private static readonly int s_AerosolHazeID = Shader.PropertyToID("_AerosolHaze");
        private static readonly int s_GroundFadeID = Shader.PropertyToID("_GroundFade");
        private static readonly int s_SkyTintID = Shader.PropertyToID("_SkyTint");
        private static readonly int s_GroundColorID = Shader.PropertyToID("_GroundColor");
        private static readonly int s_NightSkyColorID = Shader.PropertyToID("_NightSkyColor");
        private static readonly int s_NightSkyMapID = Shader.PropertyToID("_NightSkyMap");
        private static readonly int s_NightSkyMapHDRID = Shader.PropertyToID("_NightSkyMap_HDR");
        private static readonly int s_NightExposureID = Shader.PropertyToID("_NightExposure");
        private static readonly int s_NightRotationID = Shader.PropertyToID("_NightRotation");
        private static readonly int s_HasNightSkyMapID = Shader.PropertyToID("_HasNightSkyMap");
        private static readonly int s_HasCloudsID = Shader.PropertyToID("_HasClouds");
        private static readonly int s_MoonDirectionID = Shader.PropertyToID("_MoonDirection");
        private static readonly int s_MoonLightLocalID = Shader.PropertyToID("_MoonLightLocal");
        private static readonly int s_MoonParamsID = Shader.PropertyToID("_MoonParams");
        private static readonly int s_MoonColorID = Shader.PropertyToID("_MoonColor");
        private static readonly int s_MoonTextureID = Shader.PropertyToID("_MoonTexture");
        private static readonly int s_EnableMoonID = Shader.PropertyToID("_EnableMoon");

        private Shader m_Shader;
        private Material m_Material;
        private Texture2D m_MoonSurfaceTex;
        private int m_LastStateHash = -1;

        public Material Material => EnsureMaterial();

        public Material EnsureMaterial()
        {
            if (m_Material != null)
                return m_Material;

            if (m_Shader == null)
                m_Shader = Shader.Find(k_ProceduralPath);

            if (m_Shader == null)
                m_Shader = Shader.Find("Skybox/Procedural");

            if (m_Shader != null)
            {
                m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
                m_Material.name = "Volume_ProceduralSky_Runtime";
            }

            return m_Material;
        }

        public void Update(Camera camera, VisualEnvironment visualEnv, ProceduralSky proceduralSky, MoonSettings moonSettings, bool hasClouds)
        {
            if (visualEnv == null || proceduralSky == null)
                return;

            Light sun = CelestialLightManager.FindSunLight();
            Light moonLight = CelestialLightManager.FindMoonLight();
            if (moonLight == sun)
                moonLight = null;

            Vector3 sunDir = sun != null ? -sun.transform.forward : new Vector3(0f, 0.7071f, 0.7071f);
            Color sunColor = sun != null ? (sun.color * sun.intensity) : Color.white;

            bool enableMoon = moonSettings == null || moonSettings.enableMoon.value;
            Vector3 moonDir;
            if (moonLight != null)
            {
                moonDir = -moonLight.transform.forward;
                CelestialLightManager.EnsureSeparated(sunDir, ref moonDir);
            }
            else
            {
                moonDir = Vector3.Normalize(new Vector3(-sunDir.x, -sunDir.y * 0.95f + 0.12f, -sunDir.z));
            }

            float moonSize = moonSettings != null ? moonSettings.moonSize.value : 0.06f;
            float moonBrightness = moonSettings != null ? moonSettings.moonBrightness.value : 1.2f;
            Color baseMoonColor = moonSettings != null ? moonSettings.moonColor.value : new Color(0.92f, 0.95f, 1f, 1f);
            Color moonLightColor = moonLight != null ? (moonLight.color * moonLight.intensity) : Color.white;
            Color moonColor = baseMoonColor * moonLightColor;
            float earthshine = moonSettings != null ? moonSettings.earthshine.value : 0.04f;
            float haloIntensity = moonSettings != null ? moonSettings.haloIntensity.value : 0.5f;

            Vector3 upRef = Mathf.Abs(moonDir.y) > 0.99f ? Vector3.forward : Vector3.up;
            Vector3 moonRight = Vector3.Normalize(Vector3.Cross(upRef, moonDir));
            Vector3 moonUp = Vector3.Cross(moonDir, moonRight);

            Vector3 moonLightLocal;
            float phaseVal = 0.5f;
            if (moonSettings != null && moonSettings.phaseMode.value == MoonPhaseMode.Manual)
            {
                phaseVal = moonSettings.lunarPhase.value;
                float phaseAngle = (phaseVal - 0.5f) * 2.0f * Mathf.PI;
                moonLightLocal = new Vector3(Mathf.Sin(phaseAngle), 0f, Mathf.Cos(phaseAngle));
            }
            else
            {
                float localX = Vector3.Dot(sunDir, moonRight);
                float localY = Vector3.Dot(sunDir, moonUp);
                float localZ = -Vector3.Dot(sunDir, moonDir);
                moonLightLocal = new Vector3(localX, localY, localZ).normalized;
                phaseVal = Mathf.Clamp01(0.5f - Vector3.Dot(sunDir, moonDir) * 0.5f);
            }

            Texture moonTex = (moonSettings != null && moonSettings.customMoonTexture.value != null)
                ? moonSettings.customMoonTexture.value : EnsureMoonTexture();

            float exposure = (proceduralSky != null && proceduralSky.exposure.value > 0.001f) ? proceduralSky.exposure.value : 1.0f;
            float sunSize = proceduralSky != null ? proceduralSky.sunSize.value : 0.04f;
            float sunConvergence = proceduralSky != null ? proceduralSky.sunConvergence.value : 8.0f;
            float thickness = proceduralSky != null ? proceduralSky.atmosphereThickness.value : 1.0f;
            float ozone = proceduralSky != null ? proceduralSky.ozoneAbsorption.value : 1.0f;
            float aerosol = proceduralSky != null ? proceduralSky.aerosolHaze.value : 1.0f;
            float groundFade = proceduralSky != null ? proceduralSky.groundFade.value : 0.25f;
            Color skyTint = proceduralSky != null ? proceduralSky.skyTint.value : new Color(0.5f, 0.5f, 0.5f, 1f);
            Color groundColor = proceduralSky != null ? proceduralSky.groundColor.value : new Color(0.369f, 0.349f, 0.341f, 1f);
            Color nightSkyColor = proceduralSky != null ? proceduralSky.nightSkyColor.value : new Color(0.02f, 0.03f, 0.06f, 1f);
            Cubemap nightSkyMap = proceduralSky != null ? proceduralSky.nightSkyMap.value : null;
            float nightExposure = (proceduralSky != null && proceduralSky.nightExposure.value > 0.001f) ? proceduralSky.nightExposure.value : 1.0f;
            float nightRotation = proceduralSky != null ? proceduralSky.nightRotation.value : 0.0f;
            float lightingMultiplier = visualEnv.lightingMultiplier.value;
            SkyAmbientMode ambientMode = visualEnv.skyAmbientMode.value;

            Material skyMat = EnsureMaterial();
            if (skyMat != null)
            {
                skyMat.SetVector(s_SunDirectionID, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
                skyMat.SetColor(s_SunColorID, sunColor);
                skyMat.SetFloat(s_SunSizeID, sunSize);
                skyMat.SetFloat(s_SunConvergenceID, sunConvergence);
                skyMat.SetFloat(s_AtmosphereThicknessID, thickness);
                skyMat.SetFloat(s_OzoneAbsorptionID, ozone);
                skyMat.SetFloat(s_AerosolHazeID, aerosol);
                skyMat.SetFloat(s_GroundFadeID, groundFade);
                skyMat.SetColor(s_SkyTintID, skyTint);
                skyMat.SetColor(s_GroundColorID, groundColor);
                skyMat.SetColor(s_NightSkyColorID, nightSkyColor);
                skyMat.SetFloat(s_ExposureID, exposure);
                skyMat.SetFloat(s_HasCloudsID, hasClouds ? 1.0f : 0.0f);

                skyMat.SetVector(s_MoonDirectionID, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
                skyMat.SetVector(s_MoonLightLocalID, new Vector4(moonLightLocal.x, moonLightLocal.y, moonLightLocal.z, 0f));
                skyMat.SetVector(s_MoonParamsID, new Vector4(moonSize, moonBrightness, earthshine, haloIntensity));
                skyMat.SetColor(s_MoonColorID, moonColor);
                skyMat.SetTexture(s_MoonTextureID, moonTex != null ? moonTex : Texture2D.whiteTexture);
                skyMat.SetFloat(s_EnableMoonID, enableMoon ? 1.0f : 0.0f);

                if (nightSkyMap != null)
                {
                    skyMat.SetTexture(s_NightSkyMapID, nightSkyMap);
                    skyMat.SetVector(s_NightSkyMapHDRID, new Vector4(1f, 1f, 0f, 0f));
                    skyMat.SetFloat(s_NightExposureID, nightExposure);
                    skyMat.SetFloat(s_NightRotationID, nightRotation);
                    skyMat.SetFloat(s_HasNightSkyMapID, 1.0f);
                }
                else
                {
                    skyMat.SetFloat(s_HasNightSkyMapID, 0.0f);
                }

                SkyboxMaterialManager.ApplySkybox(skyMat);
            }

            int hash;
            unchecked
            {
                hash = 17;
                hash = hash * 31 + sunDir.GetHashCode();
                hash = hash * 31 + sunColor.GetHashCode();
                hash = hash * 31 + sunSize.GetHashCode();
                hash = hash * 31 + sunConvergence.GetHashCode();
                hash = hash * 31 + thickness.GetHashCode();
                hash = hash * 31 + ozone.GetHashCode();
                hash = hash * 31 + aerosol.GetHashCode();
                hash = hash * 31 + groundFade.GetHashCode();
                hash = hash * 31 + skyTint.GetHashCode();
                hash = hash * 31 + groundColor.GetHashCode();
                hash = hash * 31 + nightSkyColor.GetHashCode();
                hash = hash * 31 + (nightSkyMap != null ? nightSkyMap.GetInstanceID() : 0);
                hash = hash * 31 + nightExposure.GetHashCode();
                hash = hash * 31 + nightRotation.GetHashCode();
                hash = hash * 31 + enableMoon.GetHashCode();
                hash = hash * 31 + moonDir.GetHashCode();
                hash = hash * 31 + phaseVal.GetHashCode();
                hash = hash * 31 + moonSize.GetHashCode();
                hash = hash * 31 + moonBrightness.GetHashCode();
                hash = hash * 31 + exposure.GetHashCode();
                hash = hash * 31 + lightingMultiplier.GetHashCode();
                hash = hash * 31 + ((int)ambientMode).GetHashCode();
                hash = hash * 31 + (hasClouds ? 1 : 0);

                if (hash == m_LastStateHash)
                    return;

                m_LastStateHash = hash;
            }

            // Ambient SH probe update: skip if ambient evaluation is turned Off
            if (ambientMode == SkyAmbientMode.Off)
                return;

            float ly = sunDir.y + Mathf.Clamp01(-sunDir.y + 0.02f) * Mathf.Clamp01(sunDir.y + 0.7f);
            ly = Mathf.Clamp(ly, -1f, 1f);

            float totalDensity = thickness * aerosol;
            float lightExtinction = Mathf.Exp(-Mathf.Clamp01(ly + 0.05f) * 40f) +
                                    Mathf.Exp(-Mathf.Clamp01(ly + 0.5f) * 5f) * 0.4f +
                                    Mathf.Pow(Mathf.Clamp01(1f - ly), 2f) * 0.02f + 0.002f;

            Color sunTrans = new Color(
                Mathf.Exp(-(5.8e-6f + 3.996e-6f + 0.65e-6f * 5f * ozone) * lightExtinction * totalDensity * 1e6f),
                Mathf.Exp(-(13.5e-6f + 3.996e-6f + 1.88e-6f * 5f * ozone) * lightExtinction * totalDensity * 1e6f),
                Mathf.Exp(-(33.1e-6f + 3.996e-6f + 0.085e-6f * 5f * ozone) * lightExtinction * totalDensity * 1e6f)
            );

            Color transmittedSun = sunColor * sunTrans;
            float sunLum = Mathf.Max(sunColor.r, Mathf.Max(sunColor.g, sunColor.b));
            Color zenith = skyTint * new Color(0.18f, 0.45f, 1.0f) * thickness * 1.5f * Mathf.Clamp01((sunDir.y + 0.25f) / 0.4f) * sunLum;
            Color horizon = transmittedSun * 1.2f + new Color(0.02f, 0.03f, 0.05f) * Mathf.Clamp01(sunLum * 0.5f + 0.5f);
            Color ground = groundColor * (Mathf.Clamp01(sunDir.y * 2f + 0.2f) * 0.6f + 0.1f) * Mathf.Clamp01(sunLum * 0.8f + 0.2f);

            SphericalHarmonicsL2 baseSH = SphericalHarmonicsUtils.FromTrilight(zenith, horizon, ground);

            float nightWeight = Mathf.Clamp01((0.04f - sunDir.y) / 0.24f);
            if (nightSkyMap != null && nightWeight > 0.001f)
            {
                if (SphericalHarmonicsUtils.ExtractFromCubemap(nightSkyMap, out var nightSH))
                {
                    if (Mathf.Abs(nightRotation) > 0.01f)
                    {
                        nightSH = SphericalHarmonicsUtils.RotateY(nightSH, nightRotation);
                    }
                    nightSH = SphericalHarmonicsUtils.Scale(nightSH, nightExposure);
                    baseSH = SphericalHarmonicsUtils.Lerp(baseSH, nightSH, nightWeight);
                }
            }

            SphericalHarmonicsL2 finalSH = SphericalHarmonicsUtils.Scale(baseSH, lightingMultiplier);

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientProbe = finalSH;
            RenderSettings.ambientIntensity = lightingMultiplier;


        }

        public void ResetState()
        {
            m_LastStateHash = -1;
        }

        public void Dispose()
        {
            ResetState();
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
            m_Shader = null;
            m_MoonSurfaceTex = null;
        }

        private Texture2D EnsureMoonTexture()
        {
            if (m_MoonSurfaceTex != null)
                return m_MoonSurfaceTex;

            m_MoonSurfaceTex = Resources.Load<Texture2D>("EAStudio/Sky/moon-surface");
#if UNITY_EDITOR
            if (m_MoonSurfaceTex == null)
            {
                m_MoonSurfaceTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                    "Packages/com.eastudio.core/Runtime/RenderFeature/Sky/Resources/EAStudio/Sky/moon-surface.png");
                if (m_MoonSurfaceTex == null)
                {
                    m_MoonSurfaceTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                        "Packages/unity-eastudio-core/Runtime/RenderFeature/Sky/Resources/EAStudio/Sky/moon-surface.png");
                }
            }
#endif
            return m_MoonSurfaceTex;
        }
    }
}
