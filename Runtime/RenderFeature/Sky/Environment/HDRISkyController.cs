using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Manages HDRI skybox material properties, cross-fade blending, and ambient SH probe updates.
    /// </summary>
    public class HDRISkyController
    {
        private const string k_HDRIPath = "Skybox/EAStudio/HDRISky";

        private static readonly int s_TexID = Shader.PropertyToID("_Tex");
        private static readonly int s_TexBID = Shader.PropertyToID("_TexB");
        private static readonly int s_BlendWeightID = Shader.PropertyToID("_BlendWeight");
        private static readonly int s_RotationID = Shader.PropertyToID("_Rotation");
        private static readonly int s_ExposureID = Shader.PropertyToID("_Exposure");
        private static readonly int s_TintID = Shader.PropertyToID("_Tint");

        private Shader m_Shader;
        private Material m_Material;
        private int m_LastStateHash = -1;

        public Material Material => EnsureMaterial();

        public Material EnsureMaterial()
        {
            if (m_Material != null)
                return m_Material;

            if (m_Shader == null)
                m_Shader = Shader.Find(k_HDRIPath);

            if (m_Shader == null)
                m_Shader = Shader.Find("Skybox/Cubemap");

            if (m_Shader != null)
            {
                m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
                m_Material.name = "Volume_HDRISky_Runtime";
            }

            return m_Material;
        }

        public void Update(Camera camera, VisualEnvironment visualEnv, HDRISky hdriSky)
        {
            if (visualEnv == null || hdriSky == null || hdriSky.hdriSky.value == null)
                return;

            SkyVolumeBlendEvaluator.EvaluateHDRITransition(camera, hdriSky, out var cubemapA, out var cubemapB, out var blendWeight);

            float rotation = hdriSky.rotation.value;
            float exposure = hdriSky.exposure.value;
            float lightingMultiplier = visualEnv.lightingMultiplier.value;
            Color tint = hdriSky.tint.value;
            SkyAmbientMode ambientMode = visualEnv.skyAmbientMode.value;

            Material skyMat = EnsureMaterial();
            if (skyMat != null)
            {
                skyMat.SetTexture(s_TexID, cubemapA);
                skyMat.SetTexture(s_TexBID, cubemapB);
                skyMat.SetFloat(s_BlendWeightID, blendWeight);
                skyMat.SetFloat(s_RotationID, rotation);
                skyMat.SetFloat(s_ExposureID, exposure);
                skyMat.SetColor(s_TintID, tint);

                SkyboxMaterialManager.ApplySkybox(skyMat);
            }

            bool isInitialBinding = (m_LastStateHash == -1);
            int hash;
            unchecked
            {
                hash = 17;
                hash = hash * 31 + cubemapA.GetInstanceID();
                hash = hash * 31 + (cubemapB != null ? cubemapB.GetInstanceID() : 0);
                hash = hash * 31 + blendWeight.GetHashCode();
                hash = hash * 31 + rotation.GetHashCode();
                hash = hash * 31 + exposure.GetHashCode();
                hash = hash * 31 + lightingMultiplier.GetHashCode();
                hash = hash * 31 + tint.GetHashCode();
                hash = hash * 31 + ((int)ambientMode).GetHashCode();

                if (hash == m_LastStateHash)
                    return;

                m_LastStateHash = hash;
            }

            // Ambient SH probe update: skip if ambient evaluation is turned Off
            if (ambientMode == SkyAmbientMode.Off)
                return;

            float lightingIntensity = lightingMultiplier;
            if (SphericalHarmonicsUtils.ExtractFromCubemap(cubemapA, out var baseSHA))
            {
                SphericalHarmonicsL2 blendedBaseSH = baseSHA;

                if (blendWeight > 0.001f && cubemapB != null && cubemapA != cubemapB && SphericalHarmonicsUtils.ExtractFromCubemap(cubemapB, out var baseSHB))
                {
                    blendedBaseSH = SphericalHarmonicsUtils.Lerp(baseSHA, baseSHB, blendWeight);
                }

                SphericalHarmonicsL2 rotatedSH = SphericalHarmonicsUtils.RotateY(blendedBaseSH, -rotation);
                Color effectiveTint = tint * 2.0f;
                SphericalHarmonicsL2 finalSH = SphericalHarmonicsUtils.Scale(rotatedSH, effectiveTint, lightingIntensity);

                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientProbe = finalSH;
            }
            RenderSettings.ambientIntensity = lightingIntensity;

            if (isInitialBinding || ambientMode == SkyAmbientMode.OnChanged)
            {
                DynamicGI.UpdateEnvironment();
            }
        }

        public void ResetState()
        {
            m_LastStateHash = -1;
        }

        public void Dispose()
        {
            ResetState();
            if (m_Material != null)
            {
                CoreUtils.Destroy(m_Material);
                m_Material = null;
            }
            m_Shader = null;
        }
    }
}
