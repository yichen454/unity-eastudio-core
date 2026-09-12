using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    public static class SkyEnvironmentSync
    {
        private const string k_HDRIPath = "Skybox/EAStudio/HDRISky";
        private const string k_ProceduralPath = "Skybox/EAStudio/ProceduralSky";

        private static Shader s_HDRIShader;
        private static Material s_HDRISkyboxMaterial;

        private static Shader s_ProceduralShader;
        private static Material s_ProceduralSkyboxMaterial;

        private static Material s_OriginalSkyboxMaterial;
        private static bool s_HasStoredOriginal;

        private static Light s_CachedSunLight;
        private static int s_LastStateHash = -1;

        // HDRI properties
        private static readonly int s_TexID = Shader.PropertyToID("_Tex");
        private static readonly int s_TexBID = Shader.PropertyToID("_TexB");
        private static readonly int s_BlendWeightID = Shader.PropertyToID("_BlendWeight");
        private static readonly int s_RotationID = Shader.PropertyToID("_Rotation");
        private static readonly int s_TintID = Shader.PropertyToID("_Tint");

        // Common properties
        private static readonly int s_ExposureID = Shader.PropertyToID("_Exposure");

        // Procedural sky properties
        private static readonly int s_SunSizeID = Shader.PropertyToID("_SunSize");
        private static readonly int s_SunConvergenceID = Shader.PropertyToID("_SunConvergence");
        private static readonly int s_AtmosphereThicknessID = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int s_OzoneAbsorptionID = Shader.PropertyToID("_OzoneAbsorption");
        private static readonly int s_AerosolHazeID = Shader.PropertyToID("_AerosolHaze");
        private static readonly int s_SkyTintID = Shader.PropertyToID("_SkyTint");
        private static readonly int s_GroundColorID = Shader.PropertyToID("_GroundColor");
        private static readonly int s_GroundFadeID = Shader.PropertyToID("_GroundFade");
        private static readonly int s_NightSkyColorID = Shader.PropertyToID("_NightSkyColor");
        private static readonly int s_SunDirectionID = Shader.PropertyToID("_SunDirection");
        private static readonly int s_SunColorID = Shader.PropertyToID("_SunColor");

        private struct ActiveSkyEntry
        {
            public float priority;
            public float weight;
            public Cubemap cubemap;
        }

        private static readonly List<ActiveSkyEntry> s_ActiveSkies = new List<ActiveSkyEntry>(4);

        private static Material EnsureHDRIMaterial()
        {
            if (s_HDRISkyboxMaterial != null)
                return s_HDRISkyboxMaterial;

            if (s_HDRIShader == null)
                s_HDRIShader = Shader.Find(k_HDRIPath);

            if (s_HDRIShader == null)
                s_HDRIShader = Shader.Find("Skybox/Cubemap");

            if (s_HDRIShader != null)
            {
                s_HDRISkyboxMaterial = CoreUtils.CreateEngineMaterial(s_HDRIShader);
                s_HDRISkyboxMaterial.name = "Volume_HDRISky_Runtime";
            }

            return s_HDRISkyboxMaterial;
        }

        private static Material EnsureProceduralMaterial()
        {
            if (s_ProceduralSkyboxMaterial != null)
                return s_ProceduralSkyboxMaterial;

            if (s_ProceduralShader == null)
                s_ProceduralShader = Shader.Find(k_ProceduralPath);

            if (s_ProceduralShader == null)
                s_ProceduralShader = Shader.Find("Skybox/Procedural");

            if (s_ProceduralShader != null)
            {
                s_ProceduralSkyboxMaterial = CoreUtils.CreateEngineMaterial(s_ProceduralShader);
                s_ProceduralSkyboxMaterial.name = "Volume_ProceduralSky_Runtime";
            }

            return s_ProceduralSkyboxMaterial;
        }

        public static Light FindSunLight()
        {
            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled)
                return RenderSettings.sun;

            if (s_CachedSunLight != null && s_CachedSunLight.isActiveAndEnabled)
                return s_CachedSunLight;

            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i].type == LightType.Directional && lights[i].isActiveAndEnabled)
                {
                    s_CachedSunLight = lights[i];
                    return s_CachedSunLight;
                }
            }

            return null;
        }

        private static void CleanupLegacyProbes()
        {
            GameObject oldProbe = GameObject.Find("[SkyVolume_ReflectionProbe]");
            if (oldProbe != null)
            {
                CoreUtils.Destroy(oldProbe);
            }
        }

        /// <summary>
        /// Safely access Volume's profile without instantiating a runtime clone in the editor.
        /// Accessing volume.profile causes Unity to create a temporary clone in memory,
        /// causing inspector modifications to be lost when entering Play mode.
        /// </summary>
        private static VolumeProfile GetVolumeProfile(Volume volume)
        {
            if (volume == null)
                return null;
            return volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
        }

        private static float ComputeVolumeWeight(Volume volume, Vector3 triggerPos)
        {
            VolumeProfile profile = GetVolumeProfile(volume);
            if (volume == null || !volume.enabled || profile == null || volume.weight <= 0f)
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

        public static void UpdateHDRIEnvironment(Camera camera, VisualEnvironment visualEnv, HDRISky hdriSky)
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
            Cubemap cubemapB = cubemapA;
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
                    VolumeProfile profile = GetVolumeProfile(vol);
                    if (vol == null || profile == null)
                        continue;

                    if (profile.TryGet<VisualEnvironment>(out var vEnv) && vEnv.skyType.value != SkyType.HDRI)
                        continue;

                    if (profile.TryGet<HDRISky>(out var sky) && sky.active && sky.hdriSky.value != null)
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
                        blendWeight = Mathf.Clamp01(topSky.weight);

                        if (blendWeight >= 0.999f)
                        {
                            cubemapA = topSky.cubemap;
                            cubemapB = topSky.cubemap;
                            blendWeight = 0f;
                        }
                        else if (blendWeight <= 0.001f)
                        {
                            cubemapB = cubemapA;
                            blendWeight = 0f;
                        }
                    }
                    else
                    {
                        cubemapA = topSky.cubemap;
                        cubemapB = topSky.cubemap;
                        blendWeight = 0f;
                    }
                }
                else if (s_ActiveSkies.Count == 1)
                {
                    cubemapA = s_ActiveSkies[0].cubemap;
                    cubemapB = s_ActiveSkies[0].cubemap;
                    blendWeight = 0f;
                }
                else
                {
                    cubemapB = cubemapA;
                    blendWeight = 0f;
                }
            }

            float rotation = hdriSky.rotation.value;
            float exposure = hdriSky.exposure.value;
            float lightingMultiplier = visualEnv.lightingMultiplier.value;
            Color tint = hdriSky.tint.value;
            SkyAmbientMode ambientMode = visualEnv.skyAmbientMode.value;

            // 2. Manage and update Skybox Material (XR Multiview & cross-fade supported)
            Material skyMat = EnsureHDRIMaterial();
            if (skyMat != null)
            {
                if (!s_HasStoredOriginal)
                {
                    s_OriginalSkyboxMaterial = RenderSettings.skybox;
                    s_HasStoredOriginal = true;
                }

                skyMat.SetTexture(s_TexID, cubemapA);
                skyMat.SetTexture(s_TexBID, cubemapB);
                skyMat.SetFloat(s_BlendWeightID, blendWeight);
                skyMat.SetFloat(s_RotationID, rotation);
                skyMat.SetFloat(s_ExposureID, exposure);
                skyMat.SetColor(s_TintID, tint);

                if (RenderSettings.skybox != skyMat)
                {
                    RenderSettings.skybox = skyMat;
                }
            }

            // 3. State hash check
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

            // 4. Update ambient spherical harmonics (SH)
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

            // Notify Unity engine
            DynamicGI.UpdateEnvironment();
        }

        public static void UpdateProceduralEnvironment(Camera camera, VisualEnvironment visualEnv, ProceduralSky proceduralSky)
        {
            CleanupLegacyProbes();

            if (visualEnv == null || visualEnv.skyAmbientMode.value == SkyAmbientMode.Off)
            {
                RestoreOriginalSkybox();
                return;
            }

            // 1. Discover active sun directional light
            Light sun = FindSunLight();
            Vector3 sunDir = sun != null ? -sun.transform.forward : new Vector3(0f, 0.7071f, 0.7071f);
            Color sunColor = sun != null ? sun.color * sun.intensity : Color.white;

            // 2. Extract parameters with safe fallbacks
            float exposure = (proceduralSky != null && proceduralSky.exposure.value > 0.001f) ? proceduralSky.exposure.value : 1.0f;
            float sunSize = proceduralSky != null ? proceduralSky.sunSize.value : 0.04f;
            float sunConvergence = proceduralSky != null ? proceduralSky.sunConvergence.value : 8.0f;
            float thickness = proceduralSky != null ? proceduralSky.atmosphereThickness.value : 1.0f;
            float ozone = proceduralSky != null ? proceduralSky.ozoneAbsorption.value : 1.0f;
            float aerosol = proceduralSky != null ? proceduralSky.aerosolHaze.value : 1.0f;
            float groundFade = proceduralSky != null ? proceduralSky.groundFade.value : 0.25f;
            Color skyTint = proceduralSky != null ? proceduralSky.skyTint.value : new Color(0.5f, 0.5f, 0.5f, 1f);
            Color groundColor = proceduralSky != null ? proceduralSky.groundColor.value : new Color(0.369f, 0.349f, 0.341f, 1f);
            Color nightSkyColor = proceduralSky != null ? proceduralSky.nightSkyColor.value : new Color(0.02f, 0.03f, 0.06f, 1f);
            float lightingMultiplier = visualEnv.lightingMultiplier.value;
            SkyAmbientMode ambientMode = visualEnv.skyAmbientMode.value;

            // 3. Manage and update Procedural Skybox Material
            Material skyMat = EnsureProceduralMaterial();
            if (skyMat != null)
            {
                if (!s_HasStoredOriginal)
                {
                    s_OriginalSkyboxMaterial = RenderSettings.skybox;
                    s_HasStoredOriginal = true;
                }

                skyMat.SetVector(s_SunDirectionID, new Vector4(sunDir.x, sunDir.y, sunDir.z, 0f));
                skyMat.SetColor(s_SunColorID, sunColor);
                skyMat.SetFloat(s_SunSizeID, sunSize);
                skyMat.SetFloat(s_SunConvergenceID, sunConvergence);
                skyMat.SetFloat(s_AtmosphereThicknessID, thickness);
                skyMat.SetFloat(s_OzoneAbsorptionID, ozone);
                skyMat.SetFloat(s_AerosolHazeID, aerosol);
                skyMat.SetFloat(s_GroundFadeID, groundFade);
                skyMat.SetColor(s_SkyTintID, skyTint);
                skyMat.SetColor(s_GroundColorID, groundColor);
                skyMat.SetColor(s_NightSkyColorID, nightSkyColor);
                skyMat.SetFloat(s_ExposureID, exposure);

                if (RenderSettings.skybox != skyMat)
                {
                    RenderSettings.skybox = skyMat;
                }
            }

            // 4. State hash check
            int hash;
            unchecked
            {
                hash = 17;
                hash = hash * 31 + sunDir.GetHashCode();
                hash = hash * 31 + sunColor.GetHashCode();
                hash = hash * 31 + sunSize.GetHashCode();
                hash = hash * 31 + sunConvergence.GetHashCode();
                hash = hash * 31 + thickness.GetHashCode();
                hash = hash * 31 + ozone.GetHashCode();
                hash = hash * 31 + aerosol.GetHashCode();
                hash = hash * 31 + groundFade.GetHashCode();
                hash = hash * 31 + skyTint.GetHashCode();
                hash = hash * 31 + groundColor.GetHashCode();
                hash = hash * 31 + nightSkyColor.GetHashCode();
                hash = hash * 31 + exposure.GetHashCode();
                hash = hash * 31 + lightingMultiplier.GetHashCode();
                hash = hash * 31 + ((int)ambientMode).GetHashCode();

                if (ambientMode == SkyAmbientMode.OnChanged && hash == s_LastStateHash)
                    return;

                s_LastStateHash = hash;
            }

            // 5. Analytically generate L2 Spherical Harmonics with physical sunset reddening
            float ly = sunDir.y + Mathf.Clamp01(-sunDir.y + 0.02f) * Mathf.Clamp01(sunDir.y + 0.7f);
            ly = Mathf.Clamp(ly, -1f, 1f);

            float totalDensity = thickness * aerosol;
            float lightExtinction = Mathf.Exp(-Mathf.Clamp01(ly + 0.05f) * 40f) +
                                    Mathf.Exp(-Mathf.Clamp01(ly + 0.5f) * 5f) * 0.4f +
                                    Mathf.Pow(Mathf.Clamp01(1f - ly), 2f) * 0.02f + 0.002f;

            Color sunTrans = new Color(
                Mathf.Exp(-(5.8e-6f + 3.996e-6f + 0.65e-6f * 5f * ozone) * lightExtinction * totalDensity * 1e6f),
                Mathf.Exp(-(13.5e-6f + 3.996e-6f + 1.88e-6f * 5f * ozone) * lightExtinction * totalDensity * 1e6f),
                Mathf.Exp(-(33.1e-6f + 3.996e-6f + 0.085e-6f * 5f * ozone) * lightExtinction * totalDensity * 1e6f)
            );

            Color transmittedSun = sunColor * sunTrans;

            // Zenith deep sky color
            Color zenith = skyTint * new Color(0.18f, 0.45f, 1.0f) * thickness * 1.5f * Mathf.Clamp01((sunDir.y + 0.25f) / 0.4f);

            // Horizon sunset color (rich warm twilight)
            Color horizon = transmittedSun * 1.2f + new Color(0.02f, 0.03f, 0.05f);

            // Ground color
            Color ground = groundColor * (Mathf.Clamp01(sunDir.y * 2f + 0.2f) * 0.6f + 0.1f);

            SphericalHarmonicsL2 baseSH = SphericalHarmonicsUtils.FromTrilight(zenith, horizon, ground);
            SphericalHarmonicsL2 finalSH = SphericalHarmonicsUtils.Scale(baseSH, lightingMultiplier);

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientProbe = finalSH;
            RenderSettings.ambientIntensity = lightingMultiplier;

            // Notify Unity engine
            DynamicGI.UpdateEnvironment();
        }

        public static void RestoreOriginalSkybox()
        {
            CleanupLegacyProbes();

            if (s_HasStoredOriginal)
            {
                if (RenderSettings.skybox == s_HDRISkyboxMaterial || RenderSettings.skybox == s_ProceduralSkyboxMaterial)
                {
                    RenderSettings.skybox = s_OriginalSkyboxMaterial;
                }
            }

            s_LastStateHash = -1;
        }

        public static void ResetState()
        {
            RestoreOriginalSkybox();
            CoreUtils.Destroy(s_HDRISkyboxMaterial);
            s_HDRISkyboxMaterial = null;

            CoreUtils.Destroy(s_ProceduralSkyboxMaterial);
            s_ProceduralSkyboxMaterial = null;

            s_CachedSunLight = null;
            s_LastStateHash = -1;
            s_HasStoredOriginal = false;
        }
    }
}
