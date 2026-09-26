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
            [SerializeField] public Shader cloudShadowCookieShader;
        }

        [SerializeField, HideInInspector]
        private SkyShaderResources m_Shaders = new SkyShaderResources();

        [SerializeField, Tooltip("云层生成渲染事件节点。推荐 BeforeRenderingSkybox（支持场景地形/物体的深度穿插交互）；若仅需要地面阴影且无深度交互可设为 BeforeRenderingOpaques。")]
        private RenderPassEvent m_CloudRenderPassEvent = RenderPassEvent.BeforeRenderingSkybox;

        private CloudRenderPass m_CloudRenderPass;

        public override void Create()
        {
            EnsureShaders();
            m_CloudRenderPass = new CloudRenderPass(m_Shaders?.cloudGeneratorShader, m_Shaders?.cloudShadowCookieShader, m_CloudRenderPassEvent);
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
            if (m_Shaders.cloudShadowCookieShader == null)
                m_Shaders.cloudShadowCookieShader = Shader.Find("Hidden/EAStudio/CloudShadowCookie");
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
            CloudSettings cloudSettings = (visualEnv.cloudType.value != CloudType.None) ? stack.GetComponent<CloudSettings>() : null;
            bool hasActiveClouds = cloudSettings != null && cloudSettings.enableClouds.value && cloudSettings.coverage.value > 0.001f;

            EnsureShaders();
            SkyEnvironmentSync.SetShaderOverrides(m_Shaders?.hdriSkyShader, m_Shaders?.proceduralSkyShader);
            if (m_CloudRenderPass != null)
            {
                m_CloudRenderPass.SetShaderOverrides(m_Shaders?.cloudGeneratorShader, m_Shaders?.cloudShadowCookieShader);
                m_CloudRenderPass.UpdateRenderPassEvent(m_CloudRenderPassEvent);
            }

            Light sunLight = SkyEnvironmentSync.FindSunLight();

            // 1. Skybox background evaluation and update
            if (visualEnv.skyType.value == SkyType.HDRI)
            {
                HDRISky hdriSky = stack.GetComponent<HDRISky>();
                if (hdriSky == null || hdriSky.hdriSky.value == null)
                {
                    SkyEnvironmentSync.RestoreOriginalSkybox();
                    return;
                }

                SkyEnvironmentSync.UpdateHDRIEnvironment(camera, visualEnv, hdriSky);
            }
            else if (visualEnv.skyType.value == SkyType.Procedural)
            {
                proceduralSky = stack.GetComponent<ProceduralSky>();
                SkyEnvironmentSync.UpdateProceduralEnvironment(camera, visualEnv, proceduralSky, moonSettings, hasActiveClouds);
            }

            // 2. Cloud layer: Generate volumetric cloud map
            // Composition is done directly inside SkyboxProceduralSky without an extra blit pass
            if (visualEnv.cloudType.value != CloudType.None && m_CloudRenderPass != null)
            {
                if (hasActiveClouds)
                {
                    bool enableShadows = cloudSettings.castShadows.value;
                    m_CloudRenderPass.Setup(visualEnv, cloudSettings, proceduralSky, moonSettings, sunLight, enableShadows);

                    // Enqueue volumetric cloud pass
                    renderer.EnqueuePass(m_CloudRenderPass.LowRes);

                    // Directional Light Cookie injection
                    if (enableShadows && sunLight != null && m_CloudRenderPass.ShadowCookieTexture != null)
                    {
                        sunLight.cookie = m_CloudRenderPass.ShadowCookieTexture;
                        var additionalData = sunLight.GetUniversalAdditionalLightData();
                        if (additionalData != null)
                        {
                            float area = cloudSettings.shadowArea.value;
                            additionalData.lightCookieSize = new Vector2(area, area);
                        }
                    }
                    else if (sunLight != null && sunLight.cookie == m_CloudRenderPass.ShadowCookieTexture)
                    {
                        sunLight.cookie = null;
                    }
                }
                else
                {
                    if (sunLight != null && sunLight.cookie == m_CloudRenderPass.ShadowCookieTexture)
                    {
                        sunLight.cookie = null;
                    }
                    Shader.SetGlobalTexture(Shader.PropertyToID("_CloudTexture"), Texture2D.blackTexture);
                    Shader.SetGlobalVector(Shader.PropertyToID("_CloudShadowParams"), Vector4.zero);
                }
            }
            else
            {
                if (sunLight != null && m_CloudRenderPass != null && sunLight.cookie == m_CloudRenderPass.ShadowCookieTexture)
                {
                    sunLight.cookie = null;
                }
                Shader.SetGlobalTexture(Shader.PropertyToID("_CloudTexture"), Texture2D.blackTexture);
                Shader.SetGlobalVector(Shader.PropertyToID("_CloudShadowParams"), Vector4.zero);
            }
        }

        protected override void Dispose(bool disposing)
        {
            Light sunLight = SkyEnvironmentSync.FindSunLight();
            if (sunLight != null && m_CloudRenderPass != null && sunLight.cookie == m_CloudRenderPass.ShadowCookieTexture)
            {
                sunLight.cookie = null;
            }

            SkyEnvironmentSync.ResetState();
            if (m_CloudRenderPass != null)
            {
                m_CloudRenderPass.Dispose();
                m_CloudRenderPass = null;
            }
        }
    }
}
