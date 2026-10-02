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
        private static readonly int s_HasCloudsID = Shader.PropertyToID("_HasClouds");
        private static readonly int s_TexHDRID = Shader.PropertyToID("_Tex_HDR");
        private static readonly int s_TexBHDRID = Shader.PropertyToID("_TexB_HDR");

        private Shader m_ShaderOverride;
        private Shader m_Shader;
        private Material m_Material;
        private int m_LastStateHash = -1;

        public Material Material => EnsureMaterial();

        public void SetShaderOverride(Shader shader)
        {
            if (shader != null && m_ShaderOverride != shader)
            {
                m_ShaderOverride = shader;
                if (m_Material != null && m_Material.shader != shader)
                {
                    CoreUtils.Destroy(m_Material);
                    m_Material = null;
                }
            }
        }

        public Material EnsureMaterial()
        {
            if (m_Material != null)
                return m_Material;

            m_Shader = m_ShaderOverride != null ? m_ShaderOverride : Shader.Find(k_HDRIPath);
            if (m_Shader == null)
                m_Shader = Shader.Find("Skybox/Cubemap");

            if (m_Shader != null)
            {
                m_Material = CoreUtils.CreateEngineMaterial(m_Shader);
                m_Material.name = "Volume_HDRISky_Runtime";
                m_Material.SetColor(s_TintID, new Color32(0x80, 0x80, 0x80, 0xFF));
                m_Material.SetFloat(s_ExposureID, 1.0f);
            }

            return m_Material;
        }

        public void Update(Camera camera, VisualEnvironment visualEnv, HDRISky hdriSky, bool hasClouds = false)
        {
            if (visualEnv == null || hdriSky == null || hdriSky.hdriSky.value == null)
                return;

            SkyVolumeBlendEvaluator.EvaluateHDRITransition(camera, hdriSky, out var cubemapA, out var cubemapB, out var blendWeight);

            float rotation = (hdriSky.rotation != null && hdriSky.rotation.overrideState) ? hdriSky.rotation.value : 0f;
            float exposure = (hdriSky.exposure != null && hdriSky.exposure.overrideState) ? hdriSky.exposure.value : 1.0f;
            float lightingMultiplier = (visualEnv.lightingMultiplier != null && visualEnv.lightingMultiplier.overrideState) ? visualEnv.lightingMultiplier.value : 1.0f;
            Color tint = (hdriSky.tint != null && hdriSky.tint.overrideState) ? hdriSky.tint.value : new Color32(0x80, 0x80, 0x80, 0xFF);
            SkyAmbientMode ambientMode = visualEnv.skyAmbientMode.value;

            Vector4 decodeA = Vector4.zero;
            Vector4 decodeB = Vector4.zero;

            Material skyMat = EnsureMaterial();
            if (skyMat != null)
            {
                skyMat.SetTexture(s_TexID, cubemapA);
                skyMat.SetTexture(s_TexBID, cubemapB);
                skyMat.SetFloat(s_BlendWeightID, blendWeight);
                skyMat.SetFloat(s_RotationID, rotation);
                skyMat.SetFloat(s_ExposureID, exposure);
                skyMat.SetColor(s_TintID, tint);
                skyMat.SetFloat(s_HasCloudsID, hasClouds ? 1.0f : 0.0f);

                // Assigning a cubemap makes Unity fill in the matching RGBM decode vector (it differs per texture
                // format). Reading it back keeps the CPU projection on the very decode the shader samples with.
                decodeA = skyMat.GetVector(s_TexHDRID);
                decodeB = skyMat.GetVector(s_TexBHDRID);

                SkyboxMaterialManager.ApplySkybox(skyMat);
            }

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
                hash = hash * 31 + (hasClouds ? 1 : 0);

                if (hash == m_LastStateHash)
                    return;

                m_LastStateHash = hash;
            }

            // Ambient SH probe update: skip if ambient evaluation is turned Off
            if (ambientMode == SkyAmbientMode.Off)
                return;

            if (SphericalHarmonicsUtils.ExtractFromCubemap(cubemapA, decodeA, out var baseSHA))
            {
                SphericalHarmonicsL2 blendedBaseSH = baseSHA;

                if (blendWeight > 0.001f && cubemapB != null && cubemapA != cubemapB
                    && SphericalHarmonicsUtils.ExtractFromCubemap(cubemapB, decodeB, out var baseSHB))
                {
                    blendedBaseSH = SphericalHarmonicsUtils.Lerp(baseSHA, baseSHB, blendWeight);
                }

                // Rebuild in probe space what the skybox shader puts on screen: rotate by -_Rotation (the shader samples
                // the cubemap along R_y(+_Rotation)), then apply the tint and the gamma-decoded exposure. The probe has
                // to carry the same numbers as the visible sky or the scene is lit by a sky that is not there.
                // lightingMultiplier is deliberately NOT folded in: RenderSettings.ambientIntensity already scales a
                // Skybox ambient probe, so applying it here as well would square it.
                SphericalHarmonicsL2 finalSH = SphericalHarmonicsUtils.Scale(
                    SphericalHarmonicsUtils.RotateY(blendedBaseSH, -rotation),
                    SkyTintFactor(tint) * SphericalHarmonicsUtils.DecodeGamma(exposure));

                RenderSettings.ambientMode = AmbientMode.Skybox;
                RenderSettings.ambientProbe = finalSH;
            }

            RenderSettings.ambientIntensity = lightingMultiplier;
        }

        /// <summary>
        /// Linear factor of the skybox shader's <c>col * _Tint.rgb * unity_ColorSpaceDouble.rgb</c>. <c>_Tint</c> is a
        /// Color property, so the material uploads it gamma-decoded, and <c>unity_ColorSpaceDouble</c> is what makes
        /// the default 50% grey a neutral 1.0 instead of 0.5.
        /// </summary>
        private static Vector3 SkyTintFactor(Color tint)
        {
            return SphericalHarmonicsUtils.DecodeGamma(tint)
                * (SphericalHarmonicsUtils.LinearColorSpace ? 4.59479380f : 2f);
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
