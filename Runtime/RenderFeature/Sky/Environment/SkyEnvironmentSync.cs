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
        private static readonly int s_NightSkyMapID = Shader.PropertyToID("_NightSkyMap");
        private static readonly int s_NightSkyMapHDRID = Shader.PropertyToID("_NightSkyMap_HDR");
        private static readonly int s_NightExposureID = Shader.PropertyToID("_NightExposure");
        private static readonly int s_NightRotationID = Shader.PropertyToID("_NightRotation");
        private static readonly int s_HasNightSkyMapID = Shader.PropertyToID("_HasNightSkyMap");
        private static readonly int s_HasCloudsID = Shader.PropertyToID("_HasClouds");
        private static readonly int s_MoonDirectionID = Shader.PropertyToID("_MoonDirection");
        private static readonly int s_MoonLightLocalID = Shader.PropertyToID("_MoonLightLocal");
        private static readonly int s_MoonParamsID = Shader.PropertyToID("_MoonParams");
        private static readonly int s_MoonColorID = Shader.PropertyToID("_MoonColor");
        private static readonly int s_MoonTextureID = Shader.PropertyToID("_MoonTexture");
        private static readonly int s_EnableMoonID = Shader.PropertyToID("_EnableMoon");
        private static readonly int s_SunDirectionID = Shader.PropertyToID("_SunDirection");
        private static readonly int s_SunColorID = Shader.PropertyToID("_SunColor");

        private struct ActiveSkyEntry
        {
            public float priority;
            public float weight;
            public Cubemap cubemap;
        }

        private static readonly List<ActiveSkyEntry> s_ActiveSkies = new List<ActiveSkyEntry>(4);
        private static Texture2D s_MoonSurfaceTex;

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

        private static Light s_CachedSunLight;
        private static Light s_CachedMoonLight;
        private static int s_LastLightScanFrame = -1000;

        private static void RefreshLightCacheIfNeeded()
        {
            int currentFrame = Time.frameCount;
            if (s_CachedSunLight != null && s_CachedSunLight.isActiveAndEnabled &&
                (s_CachedMoonLight == null || (s_CachedMoonLight.isActiveAndEnabled && s_CachedMoonLight != s_CachedSunLight)) &&
                Mathf.Abs(currentFrame - s_LastLightScanFrame) < 60)
            {
                return;
            }

            s_LastLightScanFrame = currentFrame;
            s_CachedSunLight = null;
            s_CachedMoonLight = null;

            if (TimeOfDay.Instance != null && TimeOfDay.Instance.sunLight != null && TimeOfDay.Instance.sunLight.isActiveAndEnabled)
            {
                s_CachedSunLight = TimeOfDay.Instance.sunLight;
                if (TimeOfDay.Instance.moonLight != null && TimeOfDay.Instance.moonLight != s_CachedSunLight && TimeOfDay.Instance.moonLight.isActiveAndEnabled)
                {
                    s_CachedMoonLight = TimeOfDay.Instance.moonLight;
                }
                return;
            }

            if (RenderSettings.sun != null && RenderSettings.sun.isActiveAndEnabled)
            {
                s_CachedSunLight = RenderSettings.sun;
            }

            var lights = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
            Light sunFallback = null;
            Light moonFallback = null;

            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l.type != LightType.Directional || !l.isActiveAndEnabled)
                    continue;

                string name = l.name.ToLowerInvariant();
                if (s_CachedSunLight == null)
                {
                    if (name.Contains("sun"))
                        s_CachedSunLight = l;
                    else if (!name.Contains("moon") && sunFallback == null)
                        sunFallback = l;
                }
            }

            if (s_CachedSunLight == null)
                s_CachedSunLight = sunFallback;

            for (int i = 0; i < lights.Length; i++)
            {
                Light l = lights[i];
                if (l.type != LightType.Directional || !l.isActiveAndEnabled || l == s_CachedSunLight)
                    continue;

                string name = l.name.ToLowerInvariant();
                if (name.Contains("moon"))
                {
                    s_CachedMoonLight = l;
                    break;
                }
                if (moonFallback == null)
                    moonFallback = l;
            }

            if (s_CachedMoonLight == null)
                s_CachedMoonLight = moonFallback;
        }

        public static Light FindSunLight()
        {
            RefreshLightCacheIfNeeded();
            return s_CachedSunLight;
        }

        public static Light FindMoonLight()
        {
            RefreshLightCacheIfNeeded();
            return s_CachedMoonLight;
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

                if (hash == s_LastStateHash)
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

        public static void UpdateProceduralEnvironment(Camera camera, VisualEnvironment visualEnv, ProceduralSky proceduralSky, MoonSettings moonSettings = null, bool hasClouds = false)
        {

            if (visualEnv == null || visualEnv.skyAmbientMode.value == SkyAmbientMode.Off)
            {
                RestoreOriginalSkybox();
                return;
            }

            // 1. Discover active sun directional light
            Light sun = FindSunLight();
            Light moonLight = FindMoonLight();
            if (moonLight == sun)
                moonLight = null;

            Vector3 sunDir = sun != null ? -sun.transform.forward : new Vector3(0f, 0.7071f, 0.7071f);
            Color sunColor = sun != null ? sun.color * sun.intensity : Color.white;

            // Moon evaluation (light object, astronomical orbit, lunar phase, and NASA texture)
            bool enableMoon = moonSettings == null || moonSettings.enableMoon.value;
            Vector3 moonDir;
            if (moonLight != null)
            {
                moonDir = -moonLight.transform.forward;
                // Safety guard: if moonLight happens to point in the same hemisphere/direction as sun (< 15 degrees difference),
                // automatically enforce separation so Sun and Moon never overlap in the sky!
                if (Vector3.Dot(sunDir, moonDir) > 0.95f)
                {
                    moonDir = Vector3.Normalize(new Vector3(-sunDir.x, -sunDir.y * 0.95f + 0.12f, -sunDir.z));
                }
            }
            else
            {
                moonDir = Vector3.Normalize(new Vector3(-sunDir.x, -sunDir.y * 0.95f + 0.12f, -sunDir.z));
            }

            float moonSize = moonSettings != null ? moonSettings.moonSize.value : 0.06f;
            float moonBrightness = moonSettings != null ? moonSettings.moonBrightness.value : 1.2f;
            Color baseMoonColor = moonSettings != null ? moonSettings.moonColor.value : new Color(0.92f, 0.95f, 1f, 1f);
            Color moonLightColor = moonLight != null ? (moonLight.color * moonLight.intensity) : Color.white;
            Color moonColor = baseMoonColor * moonLightColor;
            float earthshine = moonSettings != null ? moonSettings.earthshine.value : 0.04f;
            float haloIntensity = moonSettings != null ? moonSettings.haloIntensity.value : 0.5f;

            // Compute 3D billboard basis for moon projection
            Vector3 upRef = Mathf.Abs(moonDir.y) > 0.99f ? Vector3.forward : Vector3.up;
            Vector3 moonRight = Vector3.Normalize(Vector3.Cross(upRef, moonDir));
            Vector3 moonUp = Vector3.Cross(moonDir, moonRight);

            Vector3 moonLightLocal;
            float phaseVal = 0.5f;
            if (moonSettings != null && moonSettings.phaseMode.value == MoonPhaseMode.Manual)
            {
                phaseVal = moonSettings.lunarPhase.value;
                float phaseAngle = (phaseVal - 0.5f) * 2.0f * Mathf.PI;
                moonLightLocal = new Vector3(Mathf.Sin(phaseAngle), 0f, Mathf.Cos(phaseAngle));
            }
            else
            {
                // Automatic astronomical phase:
                // Project sun direction into moon billboard frame
                // When Sun is opposite Moon (cosAlpha = -1), light comes from front (Z = +1, Full Moon!)
                // When Sun is near Moon (cosAlpha = +1), light comes from behind (Z = -1, New Moon!)
                float localX = Vector3.Dot(sunDir, moonRight);
                float localY = Vector3.Dot(sunDir, moonUp);
                float localZ = -Vector3.Dot(sunDir, moonDir);
                moonLightLocal = new Vector3(localX, localY, localZ).normalized;
                phaseVal = Mathf.Clamp01(0.5f - Vector3.Dot(sunDir, moonDir) * 0.5f);
            }

            Texture moonTex = moonSettings != null && moonSettings.customMoonTexture.value != null ?
                moonSettings.customMoonTexture.value : s_MoonSurfaceTex;
            if (moonTex == null)
            {
                s_MoonSurfaceTex = Resources.Load<Texture2D>("EAStudio/Sky/moon-surface");
#if UNITY_EDITOR
                if (s_MoonSurfaceTex == null)
                {
                    s_MoonSurfaceTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                        "Packages/com.eastudio.core/Runtime/RenderFeature/Sky/Resources/EAStudio/Sky/moon-surface.png");
                    if (s_MoonSurfaceTex == null)
                    {
                        s_MoonSurfaceTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(
                            "Packages/unity-eastudio-core/Runtime/RenderFeature/Sky/Resources/EAStudio/Sky/moon-surface.png");
                    }
                }
#endif
                moonTex = s_MoonSurfaceTex;
            }

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
            Cubemap nightSkyMap = proceduralSky != null ? proceduralSky.nightSkyMap.value : null;
            float nightExposure = (proceduralSky != null && proceduralSky.nightExposure.value > 0.001f) ? proceduralSky.nightExposure.value : 1.0f;
            float nightRotation = proceduralSky != null ? proceduralSky.nightRotation.value : 0.0f;
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
                skyMat.SetFloat(s_HasCloudsID, hasClouds ? 1.0f : 0.0f);

                // Moon parameters
                skyMat.SetVector(s_MoonDirectionID, new Vector4(moonDir.x, moonDir.y, moonDir.z, 0f));
                skyMat.SetVector(s_MoonLightLocalID, new Vector4(moonLightLocal.x, moonLightLocal.y, moonLightLocal.z, 0f));
                skyMat.SetVector(s_MoonParamsID, new Vector4(moonSize, moonBrightness, earthshine, haloIntensity));
                skyMat.SetColor(s_MoonColorID, moonColor);
                skyMat.SetTexture(s_MoonTextureID, moonTex != null ? moonTex : Texture2D.whiteTexture);
                skyMat.SetFloat(s_EnableMoonID, enableMoon ? 1.0f : 0.0f);

                if (nightSkyMap != null)
                {
                    skyMat.SetTexture(s_NightSkyMapID, nightSkyMap);
                    skyMat.SetVector(s_NightSkyMapHDRID, new Vector4(1f, 1f, 0f, 0f));
                    skyMat.SetFloat(s_NightExposureID, nightExposure);
                    skyMat.SetFloat(s_NightRotationID, nightRotation);
                    skyMat.SetFloat(s_HasNightSkyMapID, 1.0f);
                }
                else
                {
                    skyMat.SetFloat(s_HasNightSkyMapID, 0.0f);
                }

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
                hash = hash * 31 + (nightSkyMap != null ? nightSkyMap.GetInstanceID() : 0);
                hash = hash * 31 + nightExposure.GetHashCode();
                hash = hash * 31 + nightRotation.GetHashCode();
                hash = hash * 31 + enableMoon.GetHashCode();
                hash = hash * 31 + moonDir.GetHashCode();
                hash = hash * 31 + phaseVal.GetHashCode();
                hash = hash * 31 + moonSize.GetHashCode();
                hash = hash * 31 + moonBrightness.GetHashCode();
                hash = hash * 31 + exposure.GetHashCode();
                hash = hash * 31 + lightingMultiplier.GetHashCode();
                hash = hash * 31 + ((int)ambientMode).GetHashCode();

                if (hash == s_LastStateHash)
                    return;

                bool isInitialBinding = (s_LastStateHash == -1);
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

            // Zenith deep sky color modulated by sun brightness
            float sunLum = Mathf.Max(sunColor.r, Mathf.Max(sunColor.g, sunColor.b));
            Color zenith = skyTint * new Color(0.18f, 0.45f, 1.0f) * thickness * 1.5f * Mathf.Clamp01((sunDir.y + 0.25f) / 0.4f) * sunLum;

            // Horizon sunset color (rich warm twilight)
            Color horizon = transmittedSun * 1.2f + new Color(0.02f, 0.03f, 0.05f) * Mathf.Clamp01(sunLum * 0.5f + 0.5f);

            // Ground color
            Color ground = groundColor * (Mathf.Clamp01(sunDir.y * 2f + 0.2f) * 0.6f + 0.1f) * Mathf.Clamp01(sunLum * 0.8f + 0.2f);

            SphericalHarmonicsL2 baseSH = SphericalHarmonicsUtils.FromTrilight(zenith, horizon, ground);

            // Blend ambient SH into night sky HDRI at dusk
            float nightWeight = Mathf.Clamp01((0.04f - sunDir.y) / 0.24f);
            if (nightSkyMap != null && nightWeight > 0.001f)
            {
                if (SphericalHarmonicsUtils.ExtractFromCubemap(nightSkyMap, out var nightSH))
                {
                    if (Mathf.Abs(nightRotation) > 0.01f)
                    {
                        nightSH = SphericalHarmonicsUtils.RotateY(nightSH, nightRotation);
                    }
                    nightSH = SphericalHarmonicsUtils.Scale(nightSH, nightExposure);
                    baseSH = SphericalHarmonicsUtils.Lerp(baseSH, nightSH, nightWeight);
                }
            }

            SphericalHarmonicsL2 finalSH = SphericalHarmonicsUtils.Scale(baseSH, lightingMultiplier);

            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientProbe = finalSH;
            RenderSettings.ambientIntensity = lightingMultiplier;

            // In Realtime mode, RenderSettings.ambientProbe = finalSH updates all shaders directly every frame without engine stalls!
            // DynamicGI.UpdateEnvironment forces full GI/ReflectionProbe invalidation, so only call on initial binding or OnChanged mode.
            if (isInitialBinding || ambientMode == SkyAmbientMode.OnChanged)
            {
                DynamicGI.UpdateEnvironment();
            }
        }

        public static void RestoreOriginalSkybox()
        {

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

            s_LastStateHash = -1;
            s_HasStoredOriginal = false;
        }
    }
}
