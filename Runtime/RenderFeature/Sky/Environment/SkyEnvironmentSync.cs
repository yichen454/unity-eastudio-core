using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

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
        private static readonly int s_TexBID = Shader.PropertyToID("_TexB");
        private static readonly int s_BlendWeightID = Shader.PropertyToID("_BlendWeight");
        private static readonly int s_RotationID = Shader.PropertyToID("_Rotation");
        private static readonly int s_ExposureID = Shader.PropertyToID("_Exposure");
        private static readonly int s_TintID = Shader.PropertyToID("_Tint");

        private struct ActiveSkyEntry
        {
            public float priority;
            public float weight;
            public Cubemap cubemap;
        }

        private static readonly List<ActiveSkyEntry> s_ActiveSkies = new List<ActiveSkyEntry>(4);

        private static Material EnsureMaterial()
        {
            if (s_SkyboxMaterial != null)
                return s_SkyboxMaterial;

            if (s_SkyboxShader == null)
                s_SkyboxShader = Shader.Find(k_SkyboxShaderName);

            if (s_SkyboxShader == null)
                s_SkyboxShader = Shader.Find("Skybox/Cubemap");

            if (s_SkyboxShader != null)
            {
                s_SkyboxMaterial = CoreUtils.CreateEngineMaterial(s_SkyboxShader);
                s_SkyboxMaterial.name = "Volume_Skybox_Runtime";
            }

            return s_SkyboxMaterial;
        }

        private static void CleanupLegacyProbes()
        {
            GameObject oldProbe = GameObject.Find("[SkyVolume_ReflectionProbe]");
            if (oldProbe != null)
            {
                CoreUtils.Destroy(oldProbe);
            }
        }

        private static float ComputeVolumeWeight(Volume volume, Vector3 triggerPos)
        {
            if (volume == null || !volume.enabled || volume.profile == null || volume.weight <= 0f)
                return 0f;

            if (volume.isGlobal)
                return Mathf.Clamp01(volume.weight);

            var colliders = volume.colliders;
            if (colliders == null || colliders.Count == 0)
                return 0f;

            float closestDistanceSqr = float.PositiveInfinity;
            for (int i = 0; i < colliders.Count; i++)
            {
                var collider = colliders[i];
                if (collider == null || !collider.enabled)
                    continue;

                var closestPoint = collider.ClosestPoint(triggerPos);
                float d = (closestPoint - triggerPos).sqrMagnitude;
                if (d < closestDistanceSqr)
                    closestDistanceSqr = d;
            }

            float blendDist = volume.blendDistance;
            float blendDistSqr = blendDist * blendDist;

            if (closestDistanceSqr > blendDistSqr)
                return 0f;

            float interpFactor = 1f;
            if (blendDistSqr > 0f)
                interpFactor = 1f - (closestDistanceSqr / blendDistSqr);

            return Mathf.Clamp01(interpFactor * Mathf.Clamp01(volume.weight));
        }

        public static void UpdateEnvironment(Camera camera, VisualEnvironment visualEnv, HDRISky hdriSky)
        {
            CleanupLegacyProbes();

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

            // 1. Gather active volumes to compute transition blend weight
            Cubemap cubemapA = hdriSky.hdriSky.value;
            Cubemap cubemapB = null;
            float blendWeight = 0f;

            if (camera != null)
            {
                s_ActiveSkies.Clear();
                Vector3 camPos = camera.transform.position;
                LayerMask mask = 1;
                if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalData))
                {
                    mask = additionalData.volumeLayerMask;
                }
                Volume[] volumes = VolumeManager.instance.GetVolumes(mask);

                for (int i = 0; i < volumes.Length; i++)
                {
                    Volume vol = volumes[i];
                    if (vol == null || vol.profile == null)
                        continue;

                    if (vol.profile.TryGet<HDRISky>(out var sky) && sky.active && sky.hdriSky.value != null)
                    {
                        float w = ComputeVolumeWeight(vol, camPos);
                        if (w > 0.001f)
                        {
                            s_ActiveSkies.Add(new ActiveSkyEntry
                            {
                                priority = vol.priority,
                                weight = w,
                                cubemap = sky.hdriSky.value
                            });
                        }
                    }
                }

                s_ActiveSkies.Sort((a, b) => a.priority.CompareTo(b.priority));

                if (s_ActiveSkies.Count >= 2)
                {
                    var baseSky = s_ActiveSkies[s_ActiveSkies.Count - 2];
                    var topSky = s_ActiveSkies[s_ActiveSkies.Count - 1];

                    if (baseSky.cubemap != topSky.cubemap)
                    {
                        cubemapA = baseSky.cubemap;
                        cubemapB = topSky.cubemap;
                        blendWeight = topSky.weight;

                        if (blendWeight >= 0.999f)
                        {
                            cubemapA = topSky.cubemap;
                            cubemapB = null;
                            blendWeight = 0f;
                        }
                    }
                    else
                    {
                        cubemapA = topSky.cubemap;
                        blendWeight = 0f;
                    }
                }
                else if (s_ActiveSkies.Count == 1)
                {
                    cubemapA = s_ActiveSkies[0].cubemap;
                    blendWeight = 0f;
                }
            }

            float rotation = hdriSky.rotation.value;
            float exposure = hdriSky.exposure.value;
            float lightingMultiplier = visualEnv.lightingMultiplier.value;
            Color tint = hdriSky.tint.value;
            SkyAmbientMode ambientMode = visualEnv.skyAmbientMode.value;

            // 2. Manage and update Skybox Material (XR Multiview & cross-fade supported)
            Material skyMat = EnsureMaterial();
            if (skyMat != null)
            {
                if (!s_HasStoredOriginal)
                {
                    s_OriginalSkyboxMaterial = RenderSettings.skybox;
                    s_HasStoredOriginal = true;
                }

                skyMat.SetTexture(s_TexID, cubemapA);
                skyMat.SetTexture(s_TexBID, cubemapB != null ? cubemapB : cubemapA);
                skyMat.SetFloat(s_BlendWeightID, blendWeight);
                skyMat.SetFloat(s_RotationID, rotation);
                skyMat.SetFloat(s_ExposureID, exposure);
                skyMat.SetColor(s_TintID, tint);

                if (RenderSettings.skybox != skyMat)
                {
                    RenderSettings.skybox = skyMat;
                }
            }

            // 3. State hash check for rate-limiting heavy environment updates
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

                if (ambientMode == SkyAmbientMode.OnChanged && hash == s_LastStateHash)
                    return;

                s_LastStateHash = hash;
            }

            // 4. Update ambient spherical harmonics (SH) (interpolated between both skies if transitioning)
            float lightingIntensity = lightingMultiplier;
            if (SphericalHarmonicsUtils.ExtractFromCubemap(cubemapA, out var baseSHA))
            {
                SphericalHarmonicsL2 blendedBaseSH = baseSHA;

                if (blendWeight > 0.001f && cubemapB != null && SphericalHarmonicsUtils.ExtractFromCubemap(cubemapB, out var baseSHB))
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

            // Notify Unity engine
            DynamicGI.UpdateEnvironment();
        }

        public static void RestoreOriginalSkybox()
        {
            CleanupLegacyProbes();

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
