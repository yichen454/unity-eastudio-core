using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public static class SkyEnvironmentSync
    {
        private static int s_LastStateHash = -1;

        public static void UpdateEnvironment(VisualEnvironment visualEnv, HDRISky hdriSky)
        {
            if (visualEnv == null || visualEnv.skyAmbientMode.value == SkyAmbientMode.Off)
                return;

            if (hdriSky == null || hdriSky.hdriSky.value == null)
                return;

            Cubemap cubemap = hdriSky.hdriSky.value;
            float rotation = hdriSky.rotation.value;
            float exposure = hdriSky.exposure.value;
            float multiplier = hdriSky.multiplier.value;
            Color tint = hdriSky.tint.value;
            SkyAmbientMode ambientMode = visualEnv.skyAmbientMode.value;

            unchecked
            {
                int hash = 17;
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

            // 1. Update ambient spherical harmonics
            if (SphericalHarmonicsUtils.ExtractFromCubemap(cubemap, out var baseSH))
            {
                // Negative angle because rotating the skybox turns incoming radiance in the opposite direction
                SphericalHarmonicsL2 rotatedSH = SphericalHarmonicsUtils.RotateY(baseSH, -rotation);
                float intensity = Mathf.Exp(exposure * 0.69314718f) * multiplier;
                SphericalHarmonicsL2 finalSH = SphericalHarmonicsUtils.Scale(rotatedSH, tint, intensity);

                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientProbe = finalSH;
            }

            // 2. Synchronize reflection probe
            float reflIntensity = Mathf.Exp(exposure * 0.69314718f) * multiplier;
            if (RenderSettings.defaultReflectionMode != DefaultReflectionMode.Custom || RenderSettings.customReflectionTexture != cubemap)
            {
                RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
                RenderSettings.customReflectionTexture = cubemap;
            }
            RenderSettings.reflectionIntensity = reflIntensity;
        }

        public static void ResetState()
        {
            s_LastStateHash = -1;
        }
    }
}
