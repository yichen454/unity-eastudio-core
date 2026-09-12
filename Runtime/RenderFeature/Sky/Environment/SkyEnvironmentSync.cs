using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public static class SkyEnvironmentSync
    {
        private const string k_SkyboxShaderName = "Skybox/EAStudio/HDRISky";
        private static Shader s_SkyboxShader;
        private static Material s_SkyboxMaterial;
        private static Material s_OriginalSkyboxMaterial;
        private static bool s_HasStoredOriginal;

        private static int s_LastStateHash = -1;

        private static readonly int s_TexID = Shader.PropertyToID("_Tex");
        private static readonly int s_RotationID = Shader.PropertyToID("_Rotation");
        private static readonly int s_ExposureID = Shader.PropertyToID("_Exposure");
        private static readonly int s_MultiplierID = Shader.PropertyToID("_Multiplier");
        private static readonly int s_TintID = Shader.PropertyToID("_Tint");

        private static Material EnsureMaterial()
        {
            if (s_SkyboxMaterial != null)
                return s_SkyboxMaterial;

            if (s_SkyboxShader == null)
                s_SkyboxShader = Shader.Find(k_SkyboxShaderName);

            if (s_SkyboxShader == null)
            {
                // Fallback to Unity built-in Skybox/Cubemap
                s_SkyboxShader = Shader.Find("Skybox/Cubemap");
            }

            if (s_SkyboxShader != null)
            {
                s_SkyboxMaterial = CoreUtils.CreateEngineMaterial(s_SkyboxShader);
                s_SkyboxMaterial.name = "Volume_Skybox_Runtime";
            }

            return s_SkyboxMaterial;
        }

        public static void UpdateEnvironment(VisualEnvironment visualEnv, HDRISky hdriSky)
        {
            if (visualEnv == null || visualEnv.skyAmbientMode.value == SkyAmbientMode.Off)
            {
                RestoreOriginalSkybox();
                return;
            }

            if (hdriSky == null || hdriSky.hdriSky.value == null)
            {
                RestoreOriginalSkybox();
                return;
            }

            Cubemap cubemap = hdriSky.hdriSky.value;
            float rotation = hdriSky.rotation.value;
            float exposure = hdriSky.exposure.value;
            float multiplier = hdriSky.multiplier.value;
            Color tint = hdriSky.tint.value;
            SkyAmbientMode ambientMode = visualEnv.skyAmbientMode.value;

            // 1. Manage and update Skybox Material
            Material skyMat = EnsureMaterial();
            if (skyMat != null)
            {
                if (!s_HasStoredOriginal)
                {
                    s_OriginalSkyboxMaterial = RenderSettings.skybox;
                    s_HasStoredOriginal = true;
                }

                skyMat.SetTexture(s_TexID, cubemap);
                skyMat.SetFloat(s_RotationID, rotation);
                skyMat.SetFloat(s_ExposureID, exposure);
                skyMat.SetFloat(s_MultiplierID, multiplier);
                skyMat.SetColor(s_TintID, tint);

                if (RenderSettings.skybox != skyMat)
                {
                    RenderSettings.skybox = skyMat;
                }
            }

            // 2. Check state hash for rate-limiting heavy environment updates
            int hash;
            unchecked
            {
                hash = 17;
                hash = hash * 31 + cubemap.GetInstanceID();
                hash = hash * 31 + rotation.GetHashCode();
                hash = hash * 31 + exposure.GetHashCode();
                hash = hash * 31 + multiplier.GetHashCode();
                hash = hash * 31 + tint.GetHashCode();
                hash = hash * 31 + ((int)ambientMode).GetHashCode();

                if (ambientMode == SkyAmbientMode.OnChanged && hash == s_LastStateHash)
                    return;

                s_LastStateHash = hash;
            }

            // 3. Update ambient spherical harmonics
            if (SphericalHarmonicsUtils.ExtractFromCubemap(cubemap, out var baseSH))
            {
                SphericalHarmonicsL2 rotatedSH = SphericalHarmonicsUtils.RotateY(baseSH, -rotation);
                float intensity = Mathf.Exp(exposure * 0.69314718f) * multiplier;
                SphericalHarmonicsL2 finalSH = SphericalHarmonicsUtils.Scale(rotatedSH, tint, intensity);

                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientProbe = finalSH;
            }

            // 4. Update reflection probe using Custom mode directly bound to Volume's HDRI cubemap
            float reflIntensity = Mathf.Exp(exposure * 0.69314718f) * multiplier;
            RenderSettings.reflectionIntensity = reflIntensity;

            if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom || RenderSettings.customReflectionTexture != cubemap)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cubemap;
            }

            // Notify Unity engine to update ambient lighting probe
            DynamicGI.UpdateEnvironment();
        }

        public static void RestoreOriginalSkybox()
        {
            if (s_HasStoredOriginal)
            {
                if (RenderSettings.skybox == s_SkyboxMaterial)
                {
                    RenderSettings.skybox = s_OriginalSkyboxMaterial;
                }
            }
            s_LastStateHash = -1;
        }

        public static void ResetState()
        {
            RestoreOriginalSkybox();
            CoreUtils.Destroy(s_SkyboxMaterial);
            s_SkyboxMaterial = null;
            s_LastStateHash = -1;
            s_HasStoredOriginal = false;
        }
    }
}
