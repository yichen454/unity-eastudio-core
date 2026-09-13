using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    public class SkyRenderFeature : ScriptableRendererFeature
    {
        private CloudRenderPass m_CloudRenderPass;

        public override void Create()
        {
            m_CloudRenderPass = new CloudRenderPass();
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
                SkyEnvironmentSync.UpdateProceduralEnvironment(camera, visualEnv, proceduralSky);
            }

            // 2. Cloud layer: Only generate low-res cloud map before opaques
            // Composition is done directly inside SkyboxProceduralSky without a second pass!
            if (visualEnv.cloudType.value != CloudType.None && m_CloudRenderPass != null)
            {
                CloudSettings cloudSettings = stack.GetComponent<CloudSettings>();
                bool hasActiveClouds = cloudSettings != null && (
                    (cloudSettings.enableLayer1.value && cloudSettings.coverage.value > 0.001f) ||
                    (cloudSettings.enableLayer2.value && cloudSettings.layer2Coverage.value > 0.001f)
                );

                Light sunLight = SkyEnvironmentSync.FindSunLight();

                if (hasActiveClouds)
                {
                    bool enableShadows = cloudSettings.castShadows.value;
                    m_CloudRenderPass.Setup(visualEnv, cloudSettings, proceduralSky, sunLight, enableShadows);

                    // Low-Res generation Before Opaques -> binds _CloudTexture globally for shadows & skybox
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
                Light sunLight = SkyEnvironmentSync.FindSunLight();
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
