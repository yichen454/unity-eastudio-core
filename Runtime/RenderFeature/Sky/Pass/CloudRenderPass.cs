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

        private Shader m_GeneratorShader;
        private Material m_GeneratorMaterial;

        private int m_DownscaleFactor = 2;

        private static readonly int s_CloudTextureID = Shader.PropertyToID("_CloudTexture");
        private static readonly int s_CloudShadowParamsID = Shader.PropertyToID("_CloudShadowParams");
        private static readonly int s_CloudGlobalWindOffsetID = Shader.PropertyToID("_CloudGlobalWindOffset");
        private static readonly int s_CloudGlobalSunDirectionID = Shader.PropertyToID("_CloudGlobalSunDirection");

        // Generator shader property IDs
        private static readonly int s_Layer1ShapeID = Shader.PropertyToID("_Layer1Shape");
        private static readonly int s_CoverageID = Shader.PropertyToID("_CloudCoverage");
        private static readonly int s_DensityID = Shader.PropertyToID("_CloudDensity");
        private static readonly int s_ScaleID = Shader.PropertyToID("_CloudScale");

        private static readonly int s_EnableLayer2ID = Shader.PropertyToID("_EnableLayer2");
        private static readonly int s_Layer2ShapeID = Shader.PropertyToID("_Layer2Shape");
        private static readonly int s_Layer2CoverageID = Shader.PropertyToID("_Layer2Coverage");
        private static readonly int s_Layer2DensityID = Shader.PropertyToID("_Layer2Density");
        private static readonly int s_Layer2ScaleID = Shader.PropertyToID("_Layer2Scale");
        private static readonly int s_Layer2SpeedMulID = Shader.PropertyToID("_Layer2SpeedMul");

        private static readonly int s_DetailErosionID = Shader.PropertyToID("_DetailErosion");
        private static readonly int s_CloudHorizonFadeID = Shader.PropertyToID("_CloudHorizonFade");
        private static readonly int s_SilverLiningID = Shader.PropertyToID("_SilverLiningStrength");
        private static readonly int s_AtmosphereThicknessID = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int s_CloudColorID = Shader.PropertyToID("_CloudColor");
        private static readonly int s_ShadowColorID = Shader.PropertyToID("_CloudShadowColor");

        private static readonly int s_WindDirID = Shader.PropertyToID("_CloudWindDirection");
        private static readonly int s_WindSpeedID = Shader.PropertyToID("_CloudWindSpeed");
        private static readonly int s_WindTimeID = Shader.PropertyToID("_CloudWindTime");

        private static readonly int s_SunDirID = Shader.PropertyToID("_SunDirection");
        private static readonly int s_SunColorID = Shader.PropertyToID("_SunColor");

        private readonly LowResPass m_LowResPass;

        public LowResPass LowRes => m_LowResPass;

        public CloudRenderPass()
        {
            m_LowResPass = new LowResPass(this);
        }

        private bool EnsureMaterial()
        {
            if (m_GeneratorMaterial != null)
                return true;

            if (m_GeneratorShader == null)
                m_GeneratorShader = Shader.Find(k_GeneratorShader);

            if (m_GeneratorShader != null)
                m_GeneratorMaterial = CoreUtils.CreateEngineMaterial(m_GeneratorShader);

            return m_GeneratorMaterial != null;
        }

        public void Setup(VisualEnvironment visualEnv, CloudSettings cloudSettings, ProceduralSky proceduralSky, Light sunLight)
        {
            if (!EnsureMaterial() || visualEnv == null || cloudSettings == null)
                return;

            // Layer 1
            int shape1 = (int)cloudSettings.shapeType.value;
            float coverage1 = cloudSettings.coverage.value;
            float density1 = cloudSettings.density.value;
            float scale1 = cloudSettings.scale.value;
            float altitude1 = cloudSettings.altitude.value;

            // Layer 2
            bool enableLayer2 = cloudSettings.enableLayer2.value;
            int shape2 = (int)cloudSettings.layer2ShapeType.value;
            float coverage2 = cloudSettings.layer2Coverage.value;
            float density2 = cloudSettings.layer2Density.value;
            float scale2 = cloudSettings.layer2Scale.value;
            float speedMul2 = cloudSettings.layer2SpeedMultiplier.value;

            // Details & Lighting
            float detailErosion = cloudSettings.detailErosion.value;
            float horizonFade = cloudSettings.horizonFade.value;
            float silverLining = cloudSettings.silverLining.value;
            Color cloudColor = cloudSettings.cloudColor.value;
            Color shadowColor = cloudSettings.shadowColor.value;
            float shadowStrength = cloudSettings.shadowStrength.value;

            // Downscale
            m_DownscaleFactor = Mathf.Max(1, (int)cloudSettings.downscale.value);

            // Link atmosphere thickness
            float atmosphereThickness = proceduralSky != null ? proceduralSky.atmosphereThickness.value : 1.0f;

            float windOrientation = visualEnv.windOrientation.value;
            float windSpeed = visualEnv.windSpeed.value;

            // Compute wind offset vector using real-time progression in both Editor and Play modes
#if UNITY_EDITOR
            float currentTime = Application.isPlaying ? Time.time : (float)UnityEditor.EditorApplication.timeSinceStartup;
#else
            float currentTime = Time.time;
#endif

            float windRad = windOrientation * Mathf.Deg2Rad;
            Vector2 windDir = new Vector2(Mathf.Cos(windRad), Mathf.Sin(windRad));

            // Compute sun direction and color
            Vector3 sunDir = sunLight != null ? -sunLight.transform.forward : new Vector3(0f, 0.7071f, 0.7071f);
            Color sunColor = sunLight != null ? sunLight.color * sunLight.intensity : Color.white;

            // 1. Configure Generator Material
            m_GeneratorMaterial.SetInt(s_Layer1ShapeID, shape1);
            m_GeneratorMaterial.SetFloat(s_CoverageID, coverage1);
            m_GeneratorMaterial.SetFloat(s_DensityID, density1);
            m_GeneratorMaterial.SetFloat(s_ScaleID, scale1);

            m_GeneratorMaterial.SetFloat(s_EnableLayer2ID, enableLayer2 ? 1f : 0f);
            m_GeneratorMaterial.SetInt(s_Layer2ShapeID, shape2);
            m_GeneratorMaterial.SetFloat(s_Layer2CoverageID, coverage2);
            m_GeneratorMaterial.SetFloat(s_Layer2DensityID, density2);
            m_GeneratorMaterial.SetFloat(s_Layer2ScaleID, scale2);
            m_GeneratorMaterial.SetFloat(s_Layer2SpeedMulID, speedMul2);

            m_GeneratorMaterial.SetFloat(s_DetailErosionID, detailErosion);
            m_GeneratorMaterial.SetFloat(s_CloudHorizonFadeID, horizonFade);
            m_GeneratorMaterial.SetFloat(s_SilverLiningID, silverLining);
            m_GeneratorMaterial.SetFloat(s_AtmosphereThicknessID, atmosphereThickness);
            m_GeneratorMaterial.SetColor(s_CloudColorID, cloudColor);
            m_GeneratorMaterial.SetColor(s_ShadowColorID, shadowColor);

            // Shader-level wind animation parameters
            m_GeneratorMaterial.SetVector(s_WindDirID, new Vector4(windDir.x, windDir.y, 0f, 0f));
            m_GeneratorMaterial.SetFloat(s_WindSpeedID, windSpeed);
            m_GeneratorMaterial.SetFloat(s_WindTimeID, currentTime);

            m_GeneratorMaterial.SetVector(s_SunDirID, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
            m_GeneratorMaterial.SetColor(s_SunColorID, sunColor);

            // 2. Set Global properties for shadows and god rays (uses Layer 1 altitude)
            Vector2 windOffset = windDir * (windSpeed * currentTime * 0.005f);
            Shader.SetGlobalVector(s_CloudShadowParamsID, new Vector4(altitude1, scale1, shadowStrength, 1f));
            Shader.SetGlobalVector(s_CloudGlobalWindOffsetID, new Vector4(windOffset.x, windOffset.y, 0f, 0f));
            Shader.SetGlobalVector(s_CloudGlobalSunDirectionID, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_GeneratorMaterial);
            m_GeneratorMaterial = null;

            // Turn off global cloud shadow flag and clear texture
            Shader.SetGlobalVector(s_CloudShadowParamsID, Vector4.zero);
            Shader.SetGlobalTexture(s_CloudTextureID, Texture2D.blackTexture);
        }

        // =========================================================================================
        // Pass 1: Before Rendering Opaques - Render Multi-layer Cloud Map to RenderGraph
        // =========================================================================================
        public class LowResPass : ScriptableRenderPass
        {
            private readonly CloudRenderPass m_Parent;

            public LowResPass(CloudRenderPass parent)
            {
                m_Parent = parent;
                renderPassEvent = RenderPassEvent.BeforeRenderingOpaques;
            }

            private class PassData
            {
                public Material material;
                public TextureHandle cloudTexture;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (m_Parent.m_GeneratorMaterial == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;

                // Dynamically apply downscale factor from CloudSettings
                int factor = m_Parent.m_DownscaleFactor;
                desc.width = Mathf.Max(1, desc.width / factor);
                desc.height = Mathf.Max(1, desc.height / factor);
                desc.colorFormat = RenderTextureFormat.ARGBHalf;
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

                // Store in ContextItem for cross-pass consumers (Shadows, God Rays, etc.)
                SkyCloudFrameData cloudFrameData = frameData.GetOrCreate<SkyCloudFrameData>();
                cloudFrameData.cloudTexture = cloudTex;
                cloudFrameData.hasClouds = true;

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
            }
        }
    }
}
