using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    public class CloudRenderPass
    {
        private const string k_GeneratorShader = "Hidden/EAStudio/CloudGenerator";
        private const string k_CookieShader = "Hidden/EAStudio/CloudShadowCookie";

        private Shader m_GeneratorShader;
        private Material m_GeneratorMaterial;

        private Shader m_CookieShader;
        private Material m_CookieMaterial;
        private RTHandle m_ShadowCookieRTHandle;

        private static Texture2D s_WorleyTex;
        private static Texture2D s_BillowTex;
        private static Texture2D s_PerlinTex;
        private static Texture2D s_StratocumulusTex;
        private static Texture2D s_WorleyDetailTex;
        private static Texture2D s_PerlinDetailTex;

        private int m_DownscaleFactor = 2;
        private bool m_CastShadows = false;

        public Texture ShadowCookieTexture => m_ShadowCookieRTHandle?.rt;

        private static readonly int s_CloudTextureID = Shader.PropertyToID("_CloudTexture");
        private static readonly int s_CloudShadowParamsID = Shader.PropertyToID("_CloudShadowParams");
        private static readonly int s_CloudGlobalWindOffsetID = Shader.PropertyToID("_CloudGlobalWindOffset");
        private static readonly int s_CloudGlobalSunDirectionID = Shader.PropertyToID("_CloudGlobalSunDirection");

        // Textures
        private static readonly int s_Cloud0TexID = Shader.PropertyToID("_Cloud_0_Tex");
        private static readonly int s_Cloud0DetailTexID = Shader.PropertyToID("_Cloud_0_DetailTex");
        private static readonly int s_Cloud1TexID = Shader.PropertyToID("_Cloud_1_Tex");
        private static readonly int s_Cloud1DetailTexID = Shader.PropertyToID("_Cloud_1_DetailTex");

        // Cookie Shader Properties
        private static readonly int s_BaseTexID = Shader.PropertyToID("_BaseTex");
        private static readonly int s_CloudWindOffsetID = Shader.PropertyToID("_CloudWindOffset");
        private static readonly int s_CoverageID = Shader.PropertyToID("_Coverage");
        private static readonly int s_RevInvCoverageID = Shader.PropertyToID("_RevInvCoverage");
        private static readonly int s_ShadowStrengthID = Shader.PropertyToID("_ShadowStrength");
        private static readonly int s_ScaleID = Shader.PropertyToID("_Scale");

        // Layer 0 Properties
        private static readonly int s_Cloud0SampleParamsID = Shader.PropertyToID("_Cloud_0_SampleParams");
        private static readonly int s_Cloud0DetailParamsID = Shader.PropertyToID("_Cloud_0_DetailParams");
        private static readonly int s_Cloud0SilverLiningParamsID = Shader.PropertyToID("_Cloud_0_SilverLiningParams");
        private static readonly int s_Cloud0MaskParamsID = Shader.PropertyToID("_Cloud_0_MaskParams");
        private static readonly int s_Cloud0LightingParamsID = Shader.PropertyToID("_Cloud_0_LightingParams");
        private static readonly int s_Cloud0WindVectorID = Shader.PropertyToID("_Cloud_0_WindVector");
        private static readonly int s_Cloud0LightmarchStepsID = Shader.PropertyToID("_Cloud_0_LightmarchSteps");
        private static readonly int s_Cloud0ColorID = Shader.PropertyToID("_Cloud_0_Color");
        private static readonly int s_Cloud0LightTransmittanceID = Shader.PropertyToID("_Cloud_0_LightTransmittance");

        // Layer 1 Properties
        private static readonly int s_Cloud1SampleParamsID = Shader.PropertyToID("_Cloud_1_SampleParams");
        private static readonly int s_Cloud1DetailParamsID = Shader.PropertyToID("_Cloud_1_DetailParams");
        private static readonly int s_Cloud1SilverLiningParamsID = Shader.PropertyToID("_Cloud_1_SilverLiningParams");
        private static readonly int s_Cloud1MaskParamsID = Shader.PropertyToID("_Cloud_1_MaskParams");
        private static readonly int s_Cloud1LightingParamsID = Shader.PropertyToID("_Cloud_1_LightingParams");
        private static readonly int s_Cloud1WindVectorID = Shader.PropertyToID("_Cloud_1_WindVector");
        private static readonly int s_Cloud1LightmarchStepsID = Shader.PropertyToID("_Cloud_1_LightmarchSteps");
        private static readonly int s_Cloud1ColorID = Shader.PropertyToID("_Cloud_1_Color");
        private static readonly int s_Cloud1LightTransmittanceID = Shader.PropertyToID("_Cloud_1_LightTransmittance");

        // Common Lighting & Wind
        private static readonly int s_ParallaxMainLightDirID = Shader.PropertyToID("_ParallaxTransitionedMainLightDir");
        private static readonly int s_SunDirID = Shader.PropertyToID("_SunDirection");
        private static readonly int s_SunColorID = Shader.PropertyToID("_SunColor");
        private static readonly int s_GroundColorID = Shader.PropertyToID("_GroundColor");
        private static readonly int s_GroundFadeID = Shader.PropertyToID("_GroundFade");
        private static readonly int s_NightSkyColorID = Shader.PropertyToID("_NightSkyColor");
        private static readonly int s_MoonDirID = Shader.PropertyToID("_MoonDirection");
        private static readonly int s_MoonColorID = Shader.PropertyToID("_MoonColor");
        private static readonly int s_MoonLightIntensityID = Shader.PropertyToID("_MoonLightIntensity");
        private static readonly int s_WindTimeID = Shader.PropertyToID("_CloudWindTime");

        private readonly LowResPass m_LowResPass;
        private RenderPassEvent m_RenderPassEvent = RenderPassEvent.BeforeRenderingOpaques;

        public LowResPass LowRes => m_LowResPass;
        public RenderPassEvent RenderPassEvent => m_RenderPassEvent;

        private Shader m_GeneratorShaderOverride;
        private Shader m_CookieShaderOverride;

        public CloudRenderPass(Shader generatorShader = null, Shader cookieShader = null, RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingOpaques)
        {
            m_GeneratorShaderOverride = generatorShader;
            m_CookieShaderOverride = cookieShader;
            m_RenderPassEvent = renderPassEvent;
            m_LowResPass = new LowResPass(this);
        }

        public void SetShaderOverrides(Shader generatorShader, Shader cookieShader)
        {
            if (generatorShader != null && m_GeneratorShaderOverride != generatorShader)
            {
                m_GeneratorShaderOverride = generatorShader;
                if (m_GeneratorMaterial != null && m_GeneratorMaterial.shader != generatorShader)
                {
                    CoreUtils.Destroy(m_GeneratorMaterial);
                    m_GeneratorMaterial = null;
                }
            }
            if (cookieShader != null && m_CookieShaderOverride != cookieShader)
            {
                m_CookieShaderOverride = cookieShader;
                if (m_CookieMaterial != null && m_CookieMaterial.shader != cookieShader)
                {
                    CoreUtils.Destroy(m_CookieMaterial);
                    m_CookieMaterial = null;
                }
            }
        }

        private bool EnsureMaterial()
        {
            if (m_GeneratorMaterial != null)
                return true;

            m_GeneratorShader = m_GeneratorShaderOverride != null ? m_GeneratorShaderOverride : Shader.Find(k_GeneratorShader);

            if (m_GeneratorShader != null)
                m_GeneratorMaterial = CoreUtils.CreateEngineMaterial(m_GeneratorShader);

            return m_GeneratorMaterial != null;
        }

        private bool EnsureCookieMaterial()
        {
            if (m_CookieMaterial != null)
                return true;

            m_CookieShader = m_CookieShaderOverride != null ? m_CookieShaderOverride : Shader.Find(k_CookieShader);

            if (m_CookieShader != null)
                m_CookieMaterial = CoreUtils.CreateEngineMaterial(m_CookieShader);

            return m_CookieMaterial != null;
        }

        private void EnsureShadowCookieRTHandle()
        {
            if (m_ShadowCookieRTHandle == null)
            {
                m_ShadowCookieRTHandle = RTHandles.Alloc(
                    512, 512,
                    colorFormat: GraphicsFormat.R8_UNorm,
                    filterMode: FilterMode.Bilinear,
                    wrapMode: TextureWrapMode.Repeat,
                    name: "CloudShadow_Cookie"
                );
            }
        }

        private static Texture2D LoadTexture(string resourceName)
        {
            Texture2D tex = Resources.Load<Texture2D>($"EAStudio/Sky/{resourceName}");
#if UNITY_EDITOR
            if (tex == null)
            {
                tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                    $"Packages/com.eastudio.core/Runtime/RenderFeature/Sky/Resources/EAStudio/Sky/{resourceName}.png");
            }
            if (tex == null)
            {
                tex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                    $"Packages/unity-eastudio-core/Runtime/RenderFeature/Sky/Resources/EAStudio/Sky/{resourceName}.png");
            }
#endif
            return tex;
        }

        private static Texture2D GetBaseTexture(CloudNoiseType type)
        {
            switch (type)
            {
                case CloudNoiseType.Worley:
                    if (s_WorleyTex == null) s_WorleyTex = LoadTexture("CloudNoise_Worley");
                    return s_WorleyTex;
                case CloudNoiseType.Billow:
                    if (s_BillowTex == null) s_BillowTex = LoadTexture("CloudNoise_Billow");
                    return s_BillowTex;
                case CloudNoiseType.Perlin:
                case CloudNoiseType.Value:
                    if (s_PerlinTex == null) s_PerlinTex = LoadTexture("CloudNoise_Perlin");
                    return s_PerlinTex;
                case CloudNoiseType.Stratocumulus:
                    if (s_StratocumulusTex == null) s_StratocumulusTex = LoadTexture("CloudNoise_Stratocumulus");
                    return s_StratocumulusTex;
                default:
                    if (s_WorleyTex == null) s_WorleyTex = LoadTexture("CloudNoise_Worley");
                    return s_WorleyTex;
            }
        }

        private static Texture2D GetDetailTexture(CloudDetailType type)
        {
            switch (type)
            {
                case CloudDetailType.Worley:
                    if (s_WorleyDetailTex == null) s_WorleyDetailTex = LoadTexture("CloudNoise_Detail_Worley");
                    return s_WorleyDetailTex;
                case CloudDetailType.Perlin:
                    if (s_PerlinDetailTex == null) s_PerlinDetailTex = LoadTexture("CloudNoise_Detail_Perlin");
                    return s_PerlinDetailTex;
                default:
                    if (s_WorleyDetailTex == null) s_WorleyDetailTex = LoadTexture("CloudNoise_Detail_Worley");
                    return s_WorleyDetailTex;
            }
        }

        public void Setup(VisualEnvironment visualEnv, CloudSettings cloudSettings, ProceduralSky proceduralSky, MoonSettings moonSettings, Light sunLight, bool castShadows = true)
        {
            if (!EnsureMaterial() || visualEnv == null || cloudSettings == null)
                return;

            m_CastShadows = castShadows;

            // Downscale factor
            m_DownscaleFactor = Mathf.Max(1, (int)cloudSettings.downscale.value);

            // Layer 1 - Active evaluation & forced 0 coverage when disabled
            bool active1 = cloudSettings.enableLayer1.value && cloudSettings.coverage.value > 0.001f;
            float cov1 = active1 ? cloudSettings.coverage.value : 0.0f;
            float revInvCov1 = cov1 > 0.001f ? (1.0f / cov1) : 0.0f;
            float scale1 = cloudSettings.scale.value * 0.15f;
            float altitude1 = cloudSettings.altitude.value;
            float curvature = cloudSettings.curvature.value;

            // Layer 2 - Active evaluation & forced 0 coverage when disabled
            bool active2 = cloudSettings.enableLayer2.value && cloudSettings.layer2Coverage.value > 0.001f;
            float cov2 = active2 ? cloudSettings.layer2Coverage.value : 0.0f;
            float revInvCov2 = cov2 > 0.001f ? (1.0f / cov2) : 0.0f;
            float scale2 = cloudSettings.layer2Scale.value * 0.25f;

            // Time & Wind
            float windOrientation = visualEnv.windOrientation.value;
            float windSpeed = visualEnv.windSpeed.value;
            float windRad = windOrientation * Mathf.Deg2Rad;
            Vector2 windDir = new Vector2(Mathf.Cos(windRad), Mathf.Sin(windRad));

#if UNITY_EDITOR
            float currentTime = Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
            float currentTime = Time.time;
#endif

            // Sunlight & Direction
            Vector3 sunDir = sunLight != null ? -sunLight.transform.forward : new Vector3(0f, 0.7071f, 0.7071f);
            Color sunColor = (sunLight != null && sunLight.isActiveAndEnabled) ? (sunLight.color * sunLight.intensity) : Color.black;

            float thicknessVal = proceduralSky != null ? proceduralSky.atmosphereThickness.value : 1.0f;

            // Fast Sky 2 exact cloud light transmittance with PurifyColor
            float orgLightY = sunDir.y;
            float adjLightY = Mathf.Max(sunDir.y, 0.01f);
            float tSunset = Mathf.Clamp01((0.05f - orgLightY) / 0.20f);
            float nightFac = Mathf.Clamp01((0.1f - orgLightY) / 0.20f);
            float darkenFactor = Mathf.Lerp(1.0f, 0.45f, nightFac);

            float lightExt = Mathf.Exp(-(Mathf.Clamp01(adjLightY + 0.05f) * 40.0f)) +
                             Mathf.Exp(-(Mathf.Clamp01(adjLightY + 0.5f) * 5.0f)) * 0.4f +
                             Mathf.Pow(Mathf.Clamp01(1.0f - adjLightY), 2.0f) * 0.02f + 0.002f;
            lightExt = Mathf.Clamp01(lightExt);
            Vector3 ext = new Vector3(5.802e-6f, 13.558e-6f, 33.100e-6f) * (lightExt * 1e6f * 1.5f);
            Color rawTrans = new Color(Mathf.Exp(-ext.x), Mathf.Exp(-ext.y), Mathf.Exp(-ext.z), 1.0f) * sunColor;

            float maxComp = Mathf.Max(Mathf.Max(rawTrans.r, rawTrans.g), rawTrans.b);
            if (maxComp > 0.0001f)
            {
                rawTrans.r /= maxComp;
                rawTrans.g /= maxComp;
                rawTrans.b /= maxComp;
            }
            Color cloudSunTrans = Color.Lerp(rawTrans, Color.white, tSunset);
            Color finalCloudColor = cloudSettings.cloudColor.value * darkenFactor;

            // 1. Resolve Pre-baked or Custom Textures
            Texture tex0 = cloudSettings.customBaseTexture.value != null ? cloudSettings.customBaseTexture.value : (Texture)GetBaseTexture(cloudSettings.shapeType.value);
            Texture detailTex0 = cloudSettings.customDetailTexture.value != null ? cloudSettings.customDetailTexture.value : (Texture)GetDetailTexture(cloudSettings.detailType.value);
            m_GeneratorMaterial.SetTexture(s_Cloud0TexID, tex0 != null ? tex0 : Texture2D.blackTexture);
            m_GeneratorMaterial.SetTexture(s_Cloud0DetailTexID, detailTex0 != null ? detailTex0 : Texture2D.whiteTexture);

            Texture tex1 = cloudSettings.layer2CustomBaseTexture.value != null ? cloudSettings.layer2CustomBaseTexture.value : (Texture)GetBaseTexture(cloudSettings.layer2ShapeType.value);
            Texture detailTex1 = cloudSettings.layer2CustomDetailTexture.value != null ? cloudSettings.layer2CustomDetailTexture.value : (Texture)GetDetailTexture(cloudSettings.layer2DetailType.value);
            m_GeneratorMaterial.SetTexture(s_Cloud1TexID, tex1 != null ? tex1 : Texture2D.blackTexture);
            m_GeneratorMaterial.SetTexture(s_Cloud1DetailTexID, detailTex1 != null ? detailTex1 : Texture2D.whiteTexture);

            // Horizon and Zenith Masks
            float horizonFade = Mathf.Clamp(cloudSettings.horizonFade.value, 0.02f, 0.5f);
            Vector4 maskParams = new Vector4(1.0f / horizonFade, 0.1f, 0f, 0f);

            float thickness0 = Mathf.Clamp(cloudSettings.thickness.value, 0.1f, 50.0f) * 0.003f;
            float thickness1 = Mathf.Clamp(cloudSettings.layer2Thickness.value, 0.1f, 50.0f) * 0.003f;
            float absorption0 = Mathf.Clamp(cloudSettings.absorption.value, 0.1f, 5.0f);
            float absorption1 = Mathf.Clamp(cloudSettings.layer2Absorption.value, 0.1f, 5.0f);

            // 2. Configure Layer 0 Material Properties
            m_GeneratorMaterial.SetVector(s_Cloud0SampleParamsID, new Vector4(scale1, curvature * 0.5f, 1.0f - cov1, revInvCov1));
            m_GeneratorMaterial.SetVector(s_Cloud0DetailParamsID, new Vector4(cloudSettings.detailScale.value, cloudSettings.detailErosion.value, 0f, 0f));
            m_GeneratorMaterial.SetVector(s_Cloud0SilverLiningParamsID, new Vector4(cloudSettings.silverLiningWidth.value, cloudSettings.silverLining.value, 0f, 0f));
            m_GeneratorMaterial.SetVector(s_Cloud0MaskParamsID, maskParams);
            m_GeneratorMaterial.SetVector(s_Cloud0LightingParamsID, new Vector4(cloudSettings.density.value * 4.0f, absorption0, 0.38f, thickness0));
            Vector2 windVec0 = windDir * (windSpeed * 0.005f);
            m_GeneratorMaterial.SetVector(s_Cloud0WindVectorID, new Vector4(windVec0.x, windVec0.y, 0f, -3f));
            int steps = cloudSettings.lightmarchSteps.value;
            m_GeneratorMaterial.SetVector(s_Cloud0LightmarchStepsID, new Vector4(steps, 1.0f / steps, 0f, 0f));
            m_GeneratorMaterial.SetColor(s_Cloud0ColorID, finalCloudColor);
            m_GeneratorMaterial.SetColor(s_Cloud0LightTransmittanceID, cloudSunTrans);

            // 3. Configure Layer 1 Material Properties
            m_GeneratorMaterial.SetVector(s_Cloud1SampleParamsID, new Vector4(scale2, curvature * 0.5f, 1.0f - cov2, revInvCov2));
            m_GeneratorMaterial.SetVector(s_Cloud1DetailParamsID, new Vector4(cloudSettings.detailScale.value * 1.5f, cloudSettings.detailErosion.value, 0f, 0f));
            m_GeneratorMaterial.SetVector(s_Cloud1SilverLiningParamsID, new Vector4(cloudSettings.silverLiningWidth.value, cloudSettings.silverLining.value * 0.6f, 0f, 0f));
            m_GeneratorMaterial.SetVector(s_Cloud1MaskParamsID, maskParams);
            m_GeneratorMaterial.SetVector(s_Cloud1LightingParamsID, new Vector4(cloudSettings.layer2Density.value * 3.0f, absorption1, 0.38f, thickness1));
            Vector2 windVec1 = windDir * (windSpeed * cloudSettings.layer2SpeedMultiplier.value * 0.005f);
            m_GeneratorMaterial.SetVector(s_Cloud1WindVectorID, new Vector4(windVec1.x, windVec1.y, 0f, -3f));
            m_GeneratorMaterial.SetVector(s_Cloud1LightmarchStepsID, new Vector4(steps, 1.0f / steps, 0f, 0f));
            m_GeneratorMaterial.SetColor(s_Cloud1ColorID, finalCloudColor);
            m_GeneratorMaterial.SetColor(s_Cloud1LightTransmittanceID, cloudSunTrans);

            // 4. Parallax Light Direction & Time
            float pFac = 1.0f / Mathf.Max(0.1f, sunDir.y);
            Vector2 parallaxTrsMainLightDir = new Vector2(sunDir.x * pFac, sunDir.z * pFac);
            m_GeneratorMaterial.SetVector(s_ParallaxMainLightDirID, new Vector4(parallaxTrsMainLightDir.x, parallaxTrsMainLightDir.y, 0f, 0f));
            m_GeneratorMaterial.SetVector(s_SunDirID, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
            m_GeneratorMaterial.SetColor(s_SunColorID, sunColor);

            // Synchronize ground environment properties with clouds
            Color groundColor = proceduralSky != null ? proceduralSky.groundColor.value : new Color(0.369f, 0.349f, 0.341f, 1f);
            float groundFade = proceduralSky != null ? proceduralSky.groundFade.value : 0.25f;
            Color nightSkyColor = proceduralSky != null ? proceduralSky.nightSkyColor.value : new Color(0.02f, 0.03f, 0.06f, 1f);

            // Moon properties for night cloud lighting
            Light moonLight = SkyEnvironmentSync.FindMoonLight();
            if (moonLight == sunLight)
                moonLight = null;

            Vector3 moonDir;
            if (moonLight != null)
            {
                moonDir = -moonLight.transform.forward;
                if (Vector3.Dot(sunDir, moonDir) > 0.95f)
                {
                    moonDir = Vector3.Normalize(new Vector3(-sunDir.x, -sunDir.y * 0.95f + 0.12f, -sunDir.z));
                }
            }
            else
            {
                moonDir = Vector3.Normalize(new Vector3(-sunDir.x, -sunDir.y * 0.95f + 0.12f, -sunDir.z));
            }

            Color baseMoonColor = moonSettings != null ? moonSettings.moonColor.value : new Color(0.92f, 0.95f, 1.0f, 1.0f);
            Color moonLightColor = (moonLight != null && moonLight.isActiveAndEnabled) ? (moonLight.color * moonLight.intensity) : (Color.white * 0.25f);
            Color moonColor = baseMoonColor * moonLightColor;
            float moonIntensity = (moonSettings == null || moonSettings.enableMoon.value) ?
                (moonSettings != null ? moonSettings.cloudMoonlightIntensity.value : 0.7f) : 0.0f;

            m_GeneratorMaterial.SetColor(s_GroundColorID, groundColor);
            m_GeneratorMaterial.SetFloat(s_GroundFadeID, groundFade);
            m_GeneratorMaterial.SetColor(s_NightSkyColorID, nightSkyColor);
            m_GeneratorMaterial.SetVector(s_MoonDirID, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
            m_GeneratorMaterial.SetColor(s_MoonColorID, moonColor);
            m_GeneratorMaterial.SetFloat(s_MoonLightIntensityID, moonIntensity);
            m_GeneratorMaterial.SetFloat(s_WindTimeID, currentTime);

            // 5. Global Properties for Ground Shadows & God Rays
            Vector2 windOffset = windDir * (windSpeed * currentTime * 0.005f);
            float shadowStrength = (active1 || active2) ? cloudSettings.shadowStrength.value : 0.0f;
            Shader.SetGlobalVector(s_CloudShadowParamsID, new Vector4(altitude1, cloudSettings.scale.value, shadowStrength, (active1 || active2) ? 1f : 0f));
            Shader.SetGlobalVector(s_CloudGlobalWindOffsetID, new Vector4(windOffset.x, windOffset.y, 0f, 0f));
            Shader.SetGlobalVector(s_CloudGlobalSunDirectionID, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));

            // 6. Setup Directional Light Cookie Material
            if (m_CastShadows && EnsureCookieMaterial())
            {
                EnsureShadowCookieRTHandle();
                m_CookieMaterial.SetTexture(s_BaseTexID, tex0 != null ? tex0 : Texture2D.whiteTexture);
                m_CookieMaterial.SetVector(s_CloudWindOffsetID, new Vector4(windOffset.x, windOffset.y, 0f, 0f));
                m_CookieMaterial.SetFloat(s_CoverageID, 1.0f - cov1);
                m_CookieMaterial.SetFloat(s_RevInvCoverageID, revInvCov1);
                m_CookieMaterial.SetFloat(s_ShadowStrengthID, shadowStrength);
                m_CookieMaterial.SetFloat(s_ScaleID, Mathf.Max(0.1f, cloudSettings.scale.value * 1.5f));
            }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_GeneratorMaterial);
            m_GeneratorMaterial = null;

            CoreUtils.Destroy(m_CookieMaterial);
            m_CookieMaterial = null;

            if (m_ShadowCookieRTHandle != null)
            {
                m_ShadowCookieRTHandle.Release();
                m_ShadowCookieRTHandle = null;
            }

            Shader.SetGlobalVector(s_CloudShadowParamsID, Vector4.zero);
            Shader.SetGlobalTexture(s_CloudTextureID, Texture2D.blackTexture);
        }

        public class LowResPass : ScriptableRenderPass
        {
            private readonly CloudRenderPass m_Parent;

            public LowResPass(CloudRenderPass parent)
            {
                m_Parent = parent;
                renderPassEvent = m_Parent.m_RenderPassEvent;
            }

            private class PassData
            {
                public Material material;
                public TextureHandle cloudTexture;
            }

            private class CookiePassData
            {
                public Material material;
                public TextureHandle cookieTexture;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (m_Parent.m_GeneratorMaterial == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;

                int factor = m_Parent.m_DownscaleFactor;
                desc.width = Mathf.Max(1, desc.width / factor);
                desc.height = Mathf.Max(1, desc.height / factor);
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;

                TextureDesc textureDesc = new TextureDesc(desc.width, desc.height)
                {
                    colorFormat = GraphicsFormat.R8G8B8A8_SRGB,
                    depthBufferBits = 0,
                    msaaSamples = MSAASamples.None,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "_CloudTexture"
                };

                TextureHandle cloudTex = renderGraph.CreateTexture(textureDesc);

                SkyCloudFrameData cloudFrameData = frameData.GetOrCreate<SkyCloudFrameData>();
                cloudFrameData.cloudTexture = cloudTex;
                cloudFrameData.hasClouds = true;

                // Pass 1: Render low-res sky cloud map
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Generate Low-Res Clouds", out var passData))
                {
                    passData.material = m_Parent.m_GeneratorMaterial;
                    passData.cloudTexture = cloudTex;

                    builder.SetRenderAttachment(cloudTex, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(cloudTex, s_CloudTextureID);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
                    });
                }

                // Pass 2: Render Directional Light Cookie for terrain/props cloud shadows
                if (m_Parent.m_CastShadows && m_Parent.m_CookieMaterial != null && m_Parent.m_ShadowCookieRTHandle != null)
                {
                    TextureHandle cookieTex = renderGraph.ImportTexture(m_Parent.m_ShadowCookieRTHandle);
                    using (var builder = renderGraph.AddRasterRenderPass<CookiePassData>("Generate Cloud Shadow Cookie", out var cookiePassData))
                    {
                        cookiePassData.material = m_Parent.m_CookieMaterial;
                        cookiePassData.cookieTexture = cookieTex;

                        builder.SetRenderAttachment(cookieTex, 0, AccessFlags.Write);

                        builder.SetRenderFunc(static (CookiePassData data, RasterGraphContext context) =>
                        {
                            context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
                        });
                    }
                }
            }
        }
    }
}
