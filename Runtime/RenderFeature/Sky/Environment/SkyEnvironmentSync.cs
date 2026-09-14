using UnityEngine;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Unified environment synchronization facade. Coordinates celestial lighting, volume evaluations, and sky controllers.
    /// </summary>
    public static class SkyEnvironmentSync
    {
        private static readonly HDRISkyController s_HDRIController = new HDRISkyController();
        private static readonly ProceduralSkyController s_ProceduralController = new ProceduralSkyController();

        public static Light FindSunLight() => CelestialLightManager.FindSunLight();
        public static Light FindMoonLight() => CelestialLightManager.FindMoonLight();

        public static void SetShaderOverrides(Shader hdriShader, Shader proceduralShader)
        {
            s_HDRIController.SetShaderOverride(hdriShader);
            s_ProceduralController.SetShaderOverride(proceduralShader);
        }

        public static void UpdateHDRIEnvironment(Camera camera, VisualEnvironment visualEnv, HDRISky hdriSky)
        {
            if (visualEnv == null || hdriSky == null || hdriSky.hdriSky.value == null)
            {
                RestoreOriginalSkybox();
                return;
            }

            s_HDRIController.Update(camera, visualEnv, hdriSky);
        }

        public static void UpdateProceduralEnvironment(Camera camera, VisualEnvironment visualEnv, ProceduralSky proceduralSky, MoonSettings moonSettings = null, bool hasClouds = false)
        {
            if (visualEnv == null || proceduralSky == null)
            {
                RestoreOriginalSkybox();
                return;
            }

            s_ProceduralController.Update(camera, visualEnv, proceduralSky, moonSettings, hasClouds);
        }

        public static void RestoreOriginalSkybox()
        {
            SkyboxMaterialManager.RestoreOriginalSkybox(s_HDRIController.Material, s_ProceduralController.Material);
            s_HDRIController.ResetState();
            s_ProceduralController.ResetState();
        }

        public static void ResetState()
        {
            SkyboxMaterialManager.ResetState(s_HDRIController.Material, s_ProceduralController.Material);
            s_HDRIController.Dispose();
            s_ProceduralController.Dispose();
            CelestialLightManager.Reset();
        }
    }
}
