using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    public class SkyRenderFeature : ScriptableRendererFeature
    {
        [Header("Settings")]
        [Tooltip("Whether to use a dedicated RenderGraph raster pass to draw the sky on top of the native skybox.")]
        public bool useRenderGraphPass = false;

        [Tooltip("When to execute the sky pass if useRenderGraphPass is true.")]
        public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingSkybox;

        private const string k_ShaderName = "Hidden/EAStudio/HDRISky";
        private Shader m_Shader;
        private Material m_Material;
        private SkyRenderPass m_SkyPass;

        private static readonly int s_CubemapAID = Shader.PropertyToID("_CubemapA");
        private static readonly int s_CubemapBID = Shader.PropertyToID("_CubemapB");
        private static readonly int s_BlendWeightID = Shader.PropertyToID("_BlendWeight");
        private static readonly int s_SkyRotationID = Shader.PropertyToID("_SkyRotation");
        private static readonly int s_ExposureID = Shader.PropertyToID("_Exposure");
        private static readonly int s_MultiplierID = Shader.PropertyToID("_Multiplier");
        private static readonly int s_TintID = Shader.PropertyToID("_Tint");

        public override void Create()
        {
            m_SkyPass = new SkyRenderPass
            {
                renderPassEvent = renderPassEvent
            };
        }

        private bool EnsureMaterial()
        {
            if (m_Material != null)
                return true;

            if (m_Shader == null)
                m_Shader = Shader.Find(k_ShaderName);

            if (m_Shader == null)
                return false;

            m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
            return m_Material != null;
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null || camera.cameraType == CameraType.Preview)
                return;

            VolumeStack stack = VolumeManager.instance.stack;
            if (stack == null)
                return;

            VisualEnvironment visualEnv = stack.GetComponent<VisualEnvironment>();
            if (visualEnv == null || visualEnv.skyType.value == SkyType.None)
            {
                SkyEnvironmentSync.RestoreOriginalSkybox();
                return;
            }

            if (visualEnv.skyType.value == SkyType.HDRI)
            {
                HDRISky hdriSky = stack.GetComponent<HDRISky>();
                if (hdriSky == null || hdriSky.hdriSky.value == null)
                {
                    SkyEnvironmentSync.RestoreOriginalSkybox();
                    return;
                }

                // Synchronize Skybox Material and Environment Lighting
                SkyEnvironmentSync.UpdateEnvironment(visualEnv, hdriSky);

                // Optional RenderGraph override pass
                if (useRenderGraphPass && EnsureMaterial())
                {
                    m_Material.SetTexture(s_CubemapAID, hdriSky.hdriSky.value);
                    m_Material.SetFloat(s_BlendWeightID, 0f);
                    m_Material.SetFloat(s_SkyRotationID, hdriSky.rotation.value);
                    m_Material.SetFloat(s_ExposureID, hdriSky.exposure.value);
                    m_Material.SetFloat(s_MultiplierID, hdriSky.multiplier.value);
                    m_Material.SetColor(s_TintID, hdriSky.tint.value);

                    m_SkyPass.renderPassEvent = renderPassEvent;
                    m_SkyPass.Setup(m_Material);
                    renderer.EnqueuePass(m_SkyPass);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            CoreUtils.Destroy(m_Material);
            m_Material = null;
            SkyEnvironmentSync.ResetState();
        }
    }
}
