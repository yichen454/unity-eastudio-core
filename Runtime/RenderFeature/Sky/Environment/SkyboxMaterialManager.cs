using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Manages applying and restoring the scene Skybox material and tracking original state.
    /// </summary>
    public static class SkyboxMaterialManager
    {
        private static Material s_OriginalSkyboxMaterial;
        private static bool s_HasStoredOriginal;

        public static void ApplySkybox(Material skyMat)
        {
            if (skyMat == null)
                return;

            if (!s_HasStoredOriginal)
            {
                s_OriginalSkyboxMaterial = RenderSettings.skybox;
                s_HasStoredOriginal = true;
            }

            if (RenderSettings.skybox != skyMat)
            {
                RenderSettings.skybox = skyMat;
            }
        }

        public static void RestoreOriginalSkybox(Material hdriMat, Material procMat)
        {
            if (s_HasStoredOriginal)
            {
                if (RenderSettings.skybox == hdriMat || RenderSettings.skybox == procMat)
                {
                    RenderSettings.skybox = s_OriginalSkyboxMaterial;
                }
            }
        }

        public static void ResetState(Material hdriMat, Material procMat)
        {
            RestoreOriginalSkybox(hdriMat, procMat);
            s_HasStoredOriginal = false;
            s_OriginalSkyboxMaterial = null;
        }
    }
}
