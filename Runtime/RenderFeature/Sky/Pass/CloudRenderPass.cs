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
        private bool m_EnableDepthBlending = true;

        public Texture ShadowCookieTexture => m_ShadowCookieRTHandle?.rt;

        private static readonly int s_CloudTextureID = Shader.PropertyToID("_CloudTexture");
        private static readonly int s_CloudShadowParamsID = Shader.PropertyToID("_CloudShadowParams");
        private static readonly int s_CloudGlobalWindOffsetID = Shader.PropertyToID("_CloudGlobalWindOffset");
        private static readonly int s_CloudGlobalSunDirectionID = Shader.PropertyToID("_CloudGlobalSunDirection");

        // Textures
        private static readonly int s_BaseTexID = Shader.PropertyToID("_BaseTex");
        private static readonly int s_DetailTexID = Shader.PropertyToID("_DetailTex");

        // Cookie Shader Properties
        private static readonly int s_CloudWindOffsetID = Shader.PropertyToID("_CloudWindOffset");
        private static readonly int s_CoverageID = Shader.PropertyToID("_Coverage");
        private static readonly int s_RevInvCoverageID = Shader.PropertyToID("_RevInvCoverage");
        private static readonly int s_ShadowStrengthID = Shader.PropertyToID("_ShadowStrength");
        private static readonly int s_ScaleID = Shader.PropertyToID("_Scale");

        // Volumetric Raymarching Properties
        private static readonly int s_CloudParams0ID = Shader.PropertyToID("_Cloud_Params0");
        private static readonly int s_CloudParams1ID = Shader.PropertyToID("_Cloud_Params1");
        private static readonly int s_CloudParams2ID = Shader.PropertyToID("_Cloud_Params2");
        private static readonly int s_CloudLightingID = Shader.PropertyToID("_Cloud_Lighting");
        private static readonly int s_CloudLighting2ID = Shader.PropertyToID("_Cloud_Lighting2");
        private static readonly int s_CloudScatteringID = Shader.PropertyToID("_Cloud_Scattering");
        private static readonly int s_CloudStepsID = Shader.PropertyToID("_Cloud_Steps");
        private static readonly int s_CloudWindVectorID = Shader.PropertyToID("_Cloud_WindVector");
        private static readonly int s_CloudColorID = Shader.PropertyToID("_Cloud_Color");

        // Environment Lighting
        private static readonly int s_SunDirID = Shader.PropertyToID("_SunDirection");
        private static readonly int s_SunColorID = Shader.PropertyToID("_SunColor");
        private static readonly int s_GroundColorID = Shader.PropertyToID("_GroundColor");
        private static readonly int s_GroundFadeID = Shader.PropertyToID("_GroundFade");
        private static readonly int s_NightSkyColorID = Shader.PropertyToID("_NightSkyColor");
        private static readonly int s_AmbientSkyColorID = Shader.PropertyToID("_AmbientSkyColor");
        private static readonly int s_AmbientSunsetColorID = Shader.PropertyToID("_AmbientSunsetColor");
        private static readonly int s_ShadowColorID = Shader.PropertyToID("_ShadowColor");
        private static readonly int s_MoonDirID = Shader.PropertyToID("_MoonDirection");
        private static readonly int s_MoonColorID = Shader.PropertyToID("_MoonColor");
        private static readonly int s_MoonLightIntensityID = Shader.PropertyToID("_MoonLightIntensity");
        private static readonly int s_WindTimeID = Shader.PropertyToID("_CloudWindTime");

        private readonly LowResPass m_LowResPass;
        private RenderPassEvent m_RenderPassEvent = RenderPassEvent.BeforeRenderingOpaques;

        public LowResPass LowRes => m_LowResPass;
        public RenderPassEvent RenderPassEvent => m_RenderPassEvent;

        public void UpdateRenderPassEvent(RenderPassEvent renderPassEvent)
        {
            m_RenderPassEvent = renderPassEvent;
            if (m_LowResPass != null)
                m_LowResPass.renderPassEvent = renderPassEvent;
        }

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
            if (m_GeneratorShaderOverride != generatorShader)
            {
                m_GeneratorShaderOverride = generatorShader;
                CoreUtils.Destroy(m_GeneratorMaterial);
                m_GeneratorMaterial = null;
                m_GeneratorShader = null;
            }

            if (m_CookieShaderOverride != cookieShader)
            {
                m_CookieShaderOverride = cookieShader;
                CoreUtils.Destroy(m_CookieMaterial);
                m_CookieMaterial = null;
                m_CookieShader = null;
            }
        }

        private bool EnsureMaterial()
        {
            if (m_GeneratorMaterial != null)
                return true;

            Shader s = m_GeneratorShaderOverride != null ? m_GeneratorShaderOverride : Shader.Find(k_GeneratorShader);
            if (s == null)
            {
                Debug.LogError($"[CloudRenderPass] Cannot find shader: {k_GeneratorShader}");
                return false;
            }

            m_GeneratorShader = s;
            m_GeneratorMaterial = CoreUtils.CreateEngineMaterial(m_GeneratorShader);
            return m_GeneratorMaterial != null;
        }

        private bool EnsureCookieMaterial()
        {
            if (m_CookieMaterial != null)
                return true;

            Shader s = m_CookieShaderOverride != null ? m_CookieShaderOverride : Shader.Find(k_CookieShader);
            if (s == null)
                return false;

            m_CookieShader = s;
            m_CookieMaterial = CoreUtils.CreateEngineMaterial(m_CookieShader);
            return m_CookieMaterial != null;
        }

        private void EnsureShadowCookieRTHandle()
        {
            if (m_ShadowCookieRTHandle != null)
                return;

            RenderTextureDescriptor desc = new RenderTextureDescriptor(512, 512, RenderTextureFormat.R8, 0)
            {
                msaaSamples = 1,
                sRGB = false,
                useMipMap = false,
                autoGenerateMips = false
            };

            m_ShadowCookieRTHandle = RTHandles.Alloc(
                desc,
                FilterMode.Bilinear,
                TextureWrapMode.Repeat,
                isShadowMap: false,
                anisoLevel: 1,
                mipMapBias: 0f,
                name: "_CloudShadowCookieRT"
            );
        }

        private static Texture2D GetBaseTexture(CloudNoiseType type)
        {
            switch (type)
            {
                case CloudNoiseType.Worley:
                    if (s_WorleyTex == null) s_WorleyTex = Resources.Load<Texture2D>("EAStudio/Sky/CloudNoise_Worley");
                    return s_WorleyTex;
                case CloudNoiseType.Billow:
                    if (s_BillowTex == null) s_BillowTex = Resources.Load<Texture2D>("EAStudio/Sky/CloudNoise_Billow");
                    return s_BillowTex;
                case CloudNoiseType.Perlin:
                    if (s_PerlinTex == null) s_PerlinTex = Resources.Load<Texture2D>("EAStudio/Sky/CloudNoise_Perlin");
                    return s_PerlinTex;
                case CloudNoiseType.Stratocumulus:
                    if (s_StratocumulusTex == null) s_StratocumulusTex = Resources.Load<Texture2D>("EAStudio/Sky/CloudNoise_Stratocumulus");
                    return s_StratocumulusTex;
                default:
                    if (s_WorleyTex == null) s_WorleyTex = Resources.Load<Texture2D>("EAStudio/Sky/CloudNoise_Worley");
                    return s_WorleyTex;
            }
        }

        private static Texture2D GetDetailTexture(CloudDetailType type)
        {
            switch (type)
            {
                case CloudDetailType.Worley:
                    if (s_WorleyDetailTex == null) s_WorleyDetailTex = Resources.Load<Texture2D>("EAStudio/Sky/CloudNoise_Detail_Worley");
                    return s_WorleyDetailTex;
                case CloudDetailType.Perlin:
                    if (s_PerlinDetailTex == null) s_PerlinDetailTex = Resources.Load<Texture2D>("EAStudio/Sky/CloudNoise_Detail_Perlin");
                    return s_PerlinDetailTex;
                default:
                    if (s_WorleyDetailTex == null) s_WorleyDetailTex = Resources.Load<Texture2D>("EAStudio/Sky/CloudNoise_Detail_Worley");
                    return s_WorleyDetailTex;
            }
        }

        public void Setup(VisualEnvironment visualEnv, CloudSettings cloudSettings, ProceduralSky proceduralSky, MoonSettings moonSettings, Light sunLight, bool castShadows = true)
        {
            if (!EnsureMaterial() || visualEnv == null || cloudSettings == null)
                return;

            m_CastShadows = castShadows;
            m_EnableDepthBlending = cloudSettings.enableDepthBlending.value;

            // Downscale factor
            m_DownscaleFactor = Mathf.Max(1, (int)cloudSettings.downscale.value);

            bool cloudsActive = cloudSettings.enableClouds.value && cloudSettings.coverage.value > 0.001f;
            float coverage = cloudsActive ? cloudSettings.coverage.value : 0.0f;
            float revInvCov = coverage > 0.001f ? (1.0f / coverage) : 0.0f;

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

            // Sunlight & Direction (Normalized chromaticity to prevent HDR blowout)
            Vector3 sunDir = sunLight != null ? -sunLight.transform.forward : new Vector3(0f, 0.7071f, 0.7071f);
            Color sunColorRaw = (sunLight != null && sunLight.isActiveAndEnabled)
                ? (sunLight.color * sunLight.intensity) : Color.black;
            float sunColorMax = Mathf.Max(sunColorRaw.r, Mathf.Max(sunColorRaw.g, sunColorRaw.b));
            Color sunColor = sunColorMax > 0.001f
                ? new Color(sunColorRaw.r / sunColorMax, sunColorRaw.g / sunColorMax, sunColorRaw.b / sunColorMax, 1f)
                : Color.black;

            // Resolve Textures
            Texture baseTex = cloudSettings.customBaseTexture.value != null ? cloudSettings.customBaseTexture.value : (Texture)GetBaseTexture(cloudSettings.shapeType.value);
            Texture detailTex = cloudSettings.customDetailTexture.value != null ? cloudSettings.customDetailTexture.value : (Texture)GetDetailTexture(cloudSettings.detailType.value);
            m_GeneratorMaterial.SetTexture(s_BaseTexID, baseTex != null ? baseTex : Texture2D.whiteTexture);
            m_GeneratorMaterial.SetTexture(s_DetailTexID, detailTex != null ? detailTex : Texture2D.whiteTexture);

            // Shape & Position Properties
            float earthRadius = 6371000f;
            float altitude = cloudSettings.altitude.value;
            float thickness = cloudSettings.thickness.value;

            m_GeneratorMaterial.SetVector(s_CloudParams0ID, new Vector4(cloudSettings.scale.value, coverage, cloudSettings.density.value, cloudSettings.cloudType.value));
            m_GeneratorMaterial.SetVector(s_CloudParams1ID, new Vector4(cloudSettings.detailScale.value, cloudSettings.detailErosion.value, cloudSettings.bottomRoundness.value, cloudSettings.topSoftness.value));
            m_GeneratorMaterial.SetVector(s_CloudParams2ID, new Vector4(altitude, thickness, earthRadius, cloudSettings.horizonFade.value));

            // Lighting & Multi-Scattering Properties
            float scaledAbsorption = cloudSettings.absorption.value * 0.0022f;
            m_GeneratorMaterial.SetVector(s_CloudLightingID, new Vector4(scaledAbsorption, cloudSettings.selfShadowStrength.value, cloudSettings.powderEffect.value, cloudSettings.sunLightIntensity.value));
            m_GeneratorMaterial.SetVector(s_CloudLighting2ID, new Vector4(cloudSettings.silverLiningIntensity.value, cloudSettings.silverLiningSpread.value, cloudSettings.backlitStrength.value, cloudSettings.ambientFloor.value));
            m_GeneratorMaterial.SetVector(s_CloudScatteringID, new Vector4(cloudSettings.multiScattering.value, cloudSettings.multiScatterFalloff.value, cloudSettings.horizonFadeStart.value, 0f));

            // Steps & Performance
            float minSteps = cloudSettings.minSteps.value;
            float maxSteps = cloudSettings.maxSteps.value;
            float lightmarchSteps = cloudSettings.lightmarchSteps.value;
            float skipping = cloudSettings.enableEmptySpaceSkipping.value ? 1f : 0f;
            m_GeneratorMaterial.SetVector(s_CloudStepsID, new Vector4(minSteps, maxSteps, lightmarchSteps, skipping));

            // Wind Offset
            Vector2 windOffset = windDir * (windSpeed * currentTime * 0.00008f);
            m_GeneratorMaterial.SetVector(s_CloudWindVectorID, new Vector4(windOffset.x, windOffset.y, 1.5f, 0f));
            m_GeneratorMaterial.SetColor(s_CloudColorID, cloudSettings.cloudColor.value);

            // Celestial Lighting
            m_GeneratorMaterial.SetVector(s_SunDirID, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
            m_GeneratorMaterial.SetColor(s_SunColorID, sunColor);

            Color groundColor = proceduralSky != null ? proceduralSky.groundColor.value : new Color(0.369f, 0.349f, 0.341f, 1f);
            float groundFade = proceduralSky != null ? proceduralSky.groundFade.value : 0.25f;
            Color nightSkyColor = proceduralSky != null ? proceduralSky.nightSkyColor.value : new Color(0.02f, 0.03f, 0.06f, 1f);

            Color skyTint = proceduralSky != null ? proceduralSky.skyTint.value : new Color(0.5f, 0.5f, 0.5f, 1f);
            float skyThickness = proceduralSky != null ? proceduralSky.atmosphereThickness.value : 1.0f;
            float lightingMultiplier = visualEnv != null ? visualEnv.lightingMultiplier.value : 1.0f;
            Color dayZenith = skyTint * new Color(0.35f, 0.55f, 0.92f) * skyThickness * 1.4f * Mathf.Clamp01((sunDir.y + 0.25f) / 0.4f);
            Color sunsetTwilight = sunColor * new Color(1.0f, 0.55f, 0.22f) * 1.6f;

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
            float nightFactor = Mathf.Clamp01((0.05f - sunDir.y) / 0.15f);
            float baseMoonIntensity = (moonSettings == null || moonSettings.enableMoon.value) ?
                (moonSettings != null ? moonSettings.cloudMoonlightIntensity.value : 0.7f) : 0.0f;
            float moonIntensity = baseMoonIntensity * nightFactor;

            m_GeneratorMaterial.SetColor(s_GroundColorID, groundColor);
            m_GeneratorMaterial.SetFloat(s_GroundFadeID, groundFade);
            m_GeneratorMaterial.SetColor(s_NightSkyColorID, nightSkyColor);
            m_GeneratorMaterial.SetColor(s_AmbientSkyColorID, dayZenith * lightingMultiplier);
            m_GeneratorMaterial.SetColor(s_AmbientSunsetColorID, sunsetTwilight * lightingMultiplier);
            m_GeneratorMaterial.SetColor(s_ShadowColorID, cloudSettings.shadowColor.value);
            m_GeneratorMaterial.SetVector(s_MoonDirID, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
            m_GeneratorMaterial.SetColor(s_MoonColorID, moonColor);
            m_GeneratorMaterial.SetFloat(s_MoonLightIntensityID, moonIntensity);
            m_GeneratorMaterial.SetFloat(s_WindTimeID, currentTime);

            // Global Properties for Ground Shadows
            float shadowStrength = cloudsActive ? cloudSettings.shadowStrength.value : 0.0f;
            Shader.SetGlobalVector(s_CloudShadowParamsID, new Vector4(altitude, cloudSettings.scale.value, shadowStrength, cloudsActive ? 1f : 0f));
            Shader.SetGlobalVector(s_CloudGlobalWindOffsetID, new Vector4(windOffset.x, windOffset.y, 0f, 0f));
            Shader.SetGlobalVector(s_CloudGlobalSunDirectionID, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));

            // Setup Directional Light Cookie Material
            if (m_CastShadows && EnsureCookieMaterial())
            {
                EnsureShadowCookieRTHandle();
                m_CookieMaterial.SetTexture(s_BaseTexID, baseTex != null ? baseTex : Texture2D.whiteTexture);
                m_CookieMaterial.SetVector(s_CloudWindOffsetID, new Vector4(windOffset.x, windOffset.y, 0f, 0f));
                m_CookieMaterial.SetFloat(s_CoverageID, 1.0f - coverage);
                m_CookieMaterial.SetFloat(s_RevInvCoverageID, revInvCov);
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
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
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

                // Pass 1: Render volumetric clouds
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Generate Volumetric Clouds", out var passData))
                {
                    passData.material = m_Parent.m_GeneratorMaterial;
                    passData.cloudTexture = cloudTex;

                    // Declare read dependency on depth texture for depth blending
                    if (m_Parent.m_EnableDepthBlending && resourceData != null && resourceData.cameraDepthTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    }

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
