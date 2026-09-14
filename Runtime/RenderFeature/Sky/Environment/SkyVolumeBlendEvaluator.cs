using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Evaluates volume weight falloff and cross-fade blending between multiple sky volumes.
    /// </summary>
    public static class SkyVolumeBlendEvaluator
    {
        private struct ActiveSkyEntry
        {
            public float priority;
            public float weight;
            public Cubemap cubemap;
        }

        private static readonly List<ActiveSkyEntry> s_ActiveSkies = new List<ActiveSkyEntry>(4);

        public static VolumeProfile GetVolumeProfile(Volume volume)
        {
            if (volume == null)
                return null;
            return volume.HasInstantiatedProfile() ? volume.profile : volume.sharedProfile;
        }

        public static float ComputeVolumeWeight(Volume volume, Vector3 triggerPos)
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

        public static void EvaluateHDRITransition(Camera camera, HDRISky currentSky, out Cubemap cubemapA, out Cubemap cubemapB, out float blendWeight)
        {
            cubemapA = currentSky.hdriSky.value;
            cubemapB = cubemapA;
            blendWeight = 0f;

            if (camera == null)
                return;

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
        }
    }
}
