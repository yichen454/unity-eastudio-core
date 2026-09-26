using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Shadow
{
    /// <summary>
    /// Zero-attachment static coordinator for volume-driven shadow settings.
    /// Automatically hooks into URP rendering lifecycle, converts meter-based Volume shadow parameters
    /// into normalized pipeline settings before culling/atlas generation, and restores pipeline asset values on render complete.
    /// </summary>
    public static class ShadowVolumeSystem
    {
        private struct ShadowAssetSnapshot
        {
            public bool hasSnapshot;
            public UniversalRenderPipelineAsset asset;
            public float shadowDistance;
            public int cascadeCount;
            public float cascade2Split;
            public Vector2 cascade3Split;
            public Vector3 cascade4Split;
            public float cascadeBorder;
            public float shadowDepthBias;
            public float shadowNormalBias;

            public void Capture(UniversalRenderPipelineAsset targetAsset)
            {
                if (targetAsset == null)
                    return;

                asset = targetAsset;
                shadowDistance = targetAsset.shadowDistance;
                cascadeCount = targetAsset.shadowCascadeCount;
                cascade2Split = targetAsset.cascade2Split;
                cascade3Split = targetAsset.cascade3Split;
                cascade4Split = targetAsset.cascade4Split;
                cascadeBorder = targetAsset.cascadeBorder;
                shadowDepthBias = targetAsset.shadowDepthBias;
                shadowNormalBias = targetAsset.shadowNormalBias;
                hasSnapshot = true;
            }

            public void Restore()
            {
                if (!hasSnapshot || asset == null)
                {
                    hasSnapshot = false;
                    asset = null;
                    return;
                }

                asset.shadowDistance = shadowDistance;
                asset.shadowCascadeCount = cascadeCount;
                asset.cascade2Split = cascade2Split;
                asset.cascade3Split = cascade3Split;
                asset.cascade4Split = cascade4Split;
                asset.cascadeBorder = cascadeBorder;
                asset.shadowDepthBias = shadowDepthBias;
                asset.shadowNormalBias = shadowNormalBias;

                hasSnapshot = false;
                asset = null;
            }
        }

        private static bool s_IsRegistered;
        private static bool s_Enabled = true;
        private static ShadowAssetSnapshot s_Snapshot;
        private static Camera s_ActiveOverriddenCamera;

        /// <summary>
        /// Global switch to enable or disable Volume-driven shadow override.
        /// </summary>
        public static bool Enabled
        {
            get => s_Enabled;
            set
            {
                if (s_Enabled != value)
                {
                    s_Enabled = value;
                    if (!s_Enabled)
                    {
                        RestoreSnapshot();
                        s_ActiveOverriddenCamera = null;
                    }
                }
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void InitRuntime()
        {
            Register();
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitEditor()
        {
            Register();
        }
#endif

        /// <summary>
        /// Registers rendering hooks and lifecycle cleanups.
        /// </summary>
        public static void Register()
        {
            if (s_IsRegistered)
                return;

            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
            Application.quitting += OnApplicationQuitting;

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif

            s_IsRegistered = true;
        }

        /// <summary>
        /// Unregisters all rendering hooks and safely restores pipeline asset state.
        /// </summary>
        public static void Unregister()
        {
            if (!s_IsRegistered)
                return;

            RestoreSnapshot();
            s_ActiveOverriddenCamera = null;

            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
            RenderPipelineManager.endCameraRendering -= OnEndCameraRendering;
            Application.quitting -= OnApplicationQuitting;

#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif

            s_IsRegistered = false;
        }

        private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (!s_Enabled || camera == null)
                return;

            // Skip preview cameras and reflection probe captures
            if (camera.cameraType == CameraType.Preview || camera.cameraType == CameraType.Reflection)
                return;

            // If a previous camera did not complete or clean up, restore snapshot first to avoid leakage
            if (s_Snapshot.hasSnapshot)
            {
                RestoreSnapshot();
                s_ActiveOverriddenCamera = null;
            }

            // Resolve camera trigger position and volume layer mask
            Transform trigger = camera.transform;
            LayerMask layerMask = 1; // Default layer
            if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var additionalCameraData))
            {
                layerMask = additionalCameraData.volumeLayerMask;
                if (additionalCameraData.volumeTrigger != null)
                    trigger = additionalCameraData.volumeTrigger;
            }

            // Evaluate VolumeStack for current camera position and layer mask
            VolumeManager.instance.Update(trigger, layerMask);
            VolumeStack stack = VolumeManager.instance.stack;
            if (stack == null)
                return;

            ShadowSettings shadowSettings = stack.GetComponent<ShadowSettings>();
            if (shadowSettings == null || !shadowSettings.IsActive())
                return;

            UniversalRenderPipelineAsset asset = UniversalRenderPipeline.asset;
            if (asset == null)
                return;

            s_Snapshot.Capture(asset);

            // Determine effective maximum shadow distance
            float effectiveShadowDistance = asset.shadowDistance;
            if (shadowSettings.shadowDistance.overrideState)
            {
                effectiveShadowDistance = Mathf.Max(0.0f, shadowSettings.shadowDistance.value);
                asset.shadowDistance = effectiveShadowDistance;
            }

            if (shadowSettings.cascadeCount.overrideState)
                asset.shadowCascadeCount = Mathf.Clamp(shadowSettings.cascadeCount.value, 1, 4);

            // Convert meter-based distances into normalized splits (0..1) relative to max shadow distance
            float maxDist = Mathf.Max(0.001f, effectiveShadowDistance);

            if (shadowSettings.cascade2Distance.overrideState)
            {
                float split = Mathf.Clamp01(shadowSettings.cascade2Distance.value / maxDist);
                asset.cascade2Split = split;
            }

            if (shadowSettings.cascade3Distances.overrideState)
            {
                Vector2 dists = shadowSettings.cascade3Distances.value;
                float c1 = Mathf.Clamp01(dists.x / maxDist);
                float c2 = Mathf.Clamp(dists.y / maxDist, c1, 1.0f);
                asset.cascade3Split = new Vector2(c1, c2);
            }

            if (shadowSettings.cascade4Distances.overrideState)
            {
                Vector3 dists = shadowSettings.cascade4Distances.value;
                float c1 = Mathf.Clamp01(dists.x / maxDist);
                float c2 = Mathf.Clamp(dists.y / maxDist, c1, 1.0f);
                float c3 = Mathf.Clamp(dists.z / maxDist, c2, 1.0f);
                asset.cascade4Split = new Vector3(c1, c2, c3);
            }

            if (shadowSettings.cascadeBorderDistance.overrideState)
            {
                float border = Mathf.Clamp01(shadowSettings.cascadeBorderDistance.value / maxDist);
                asset.cascadeBorder = border;
            }

            if (shadowSettings.shadowDepthBias.overrideState)
                asset.shadowDepthBias = Mathf.Max(0.0f, shadowSettings.shadowDepthBias.value);

            if (shadowSettings.shadowNormalBias.overrideState)
                asset.shadowNormalBias = Mathf.Max(0.0f, shadowSettings.shadowNormalBias.value);

            s_ActiveOverriddenCamera = camera;
        }

        private static void OnEndCameraRendering(ScriptableRenderContext context, Camera camera)
        {
            if (s_ActiveOverriddenCamera == camera)
            {
                RestoreSnapshot();
                s_ActiveOverriddenCamera = null;
            }
        }

        /// <summary>
        /// Restores any modified pipeline asset parameters back to their original snapshotted values.
        /// </summary>
        public static void RestoreSnapshot()
        {
            if (s_Snapshot.hasSnapshot)
            {
                s_Snapshot.Restore();
            }
        }

        private static void OnApplicationQuitting()
        {
            RestoreSnapshot();
            s_ActiveOverriddenCamera = null;
        }

#if UNITY_EDITOR
        private static void OnBeforeAssemblyReload()
        {
            RestoreSnapshot();
            s_ActiveOverriddenCamera = null;
        }

        private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
        {
            if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode ||
                state == UnityEditor.PlayModeStateChange.ExitingEditMode)
            {
                RestoreSnapshot();
                s_ActiveOverriddenCamera = null;
            }
        }
#endif
    }
}
