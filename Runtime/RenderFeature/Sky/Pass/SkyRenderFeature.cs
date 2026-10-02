using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    public class SkyRenderFeature : ScriptableRendererFeature
    {
        [System.Serializable]
        public class SkyShaderResources
        {
            [SerializeField] public Shader proceduralSkyShader;
            [SerializeField] public Shader hdriSkyShader;
            [SerializeField] public Shader cloudGeneratorShader;
        }

        [SerializeField, HideInInspector]
        private SkyShaderResources m_Shaders = new SkyShaderResources();

        [SerializeField, Tooltip("云层生成渲染事件节点。推荐 BeforeRenderingSkybox（支持场景地形/物体的深度穿插交互）。")]
        private RenderPassEvent m_CloudRenderPassEvent = RenderPassEvent.BeforeRenderingSkybox;

        private CloudRenderPass m_CloudRenderPass;

        public override void Create()
        {
            EnsureShaders();
            m_CloudRenderPass = new CloudRenderPass(m_Shaders?.cloudGeneratorShader, m_CloudRenderPassEvent);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            EnsureShaders();
            if (m_CloudRenderPass != null)
            {
                m_CloudRenderPass.UpdateRenderPassEvent(m_CloudRenderPassEvent);
            }
        }

        private void Reset()
        {
            EnsureShaders();
        }
#endif

        public void EnsureShaders()
        {
            if (m_Shaders == null)
                m_Shaders = new SkyShaderResources();

#if UNITY_EDITOR
            if (m_Shaders.proceduralSkyShader == null)
                m_Shaders.proceduralSkyShader = Shader.Find("Skybox/EAStudio/ProceduralSky");
            if (m_Shaders.hdriSkyShader == null)
                m_Shaders.hdriSkyShader = Shader.Find("Skybox/EAStudio/HDRISky");
            if (m_Shaders.cloudGeneratorShader == null)
                m_Shaders.cloudGeneratorShader = Shader.Find("Hidden/EAStudio/CloudGenerator");
#endif
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

            ProceduralSky proceduralSky = null;
            MoonSettings moonSettings = stack.GetComponent<MoonSettings>();
            CloudLayer cloudLayer = (visualEnv.cloudType.value == CloudType.CloudLayer) ? stack.GetComponent<CloudLayer>() : null;
            bool hasActiveClouds = cloudLayer != null && cloudLayer.enableClouds.value;

            EnsureShaders();
            SkyEnvironmentSync.SetShaderOverrides(m_Shaders?.hdriSkyShader, m_Shaders?.proceduralSkyShader);
            if (m_CloudRenderPass != null)
            {
                m_CloudRenderPass.SetShaderOverride(m_Shaders?.cloudGeneratorShader);
                m_CloudRenderPass.UpdateRenderPassEvent(m_CloudRenderPassEvent);
            }

            // 1. Skybox background evaluation and update
            if (visualEnv.skyType.value == SkyType.HDRI)
            {
                HDRISky hdriSky = stack.GetComponent<HDRISky>();
                if (hdriSky == null || hdriSky.hdriSky.value == null)
                {
                    SkyEnvironmentSync.RestoreOriginalSkybox();
                    return;
                }

                SkyEnvironmentSync.UpdateHDRIEnvironment(camera, visualEnv, hdriSky, hasActiveClouds);
            }
            else if (visualEnv.skyType.value == SkyType.Procedural)
            {
                proceduralSky = stack.GetComponent<ProceduralSky>();
                SkyEnvironmentSync.UpdateProceduralEnvironment(camera, visualEnv, proceduralSky, moonSettings, hasActiveClouds);
            }

            // 2. Cloud layer: Downscaled offscreen pass
            if (visualEnv.cloudType.value == CloudType.CloudLayer && m_CloudRenderPass != null)
            {
                if (hasActiveClouds)
                {
                    m_CloudRenderPass.Setup(visualEnv, cloudLayer);
                    renderer.EnqueuePass(m_CloudRenderPass.LowRes);
                }
                else
                {
                    Shader.SetGlobalTexture(Shader.PropertyToID("_CloudTexture"), Texture2D.blackTexture);
                }
            }
            else
            {
                Shader.SetGlobalTexture(Shader.PropertyToID("_CloudTexture"), Texture2D.blackTexture);
            }
        }

        protected override void Dispose(bool disposing)
        {
            SkyEnvironmentSync.ResetState();
            if (m_CloudRenderPass != null)
            {
                m_CloudRenderPass.Dispose();
                m_CloudRenderPass = null;
            }
        }
    }
}
