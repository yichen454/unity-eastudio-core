using System.Collections.Generic;
using EAStudio.Core.RenderFeature.Sky;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Shadow
{
    public static class CameraClipVolumeSystem
    {
        private struct ClipPlanes
        {
            public bool applied;
            public float near;
            public float far;
        }

        private static readonly Dictionary<Camera, Stack<ClipPlanes>> s_Snapshots = new Dictionary<Camera, Stack<ClipPlanes>>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize() => Register();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        private static void InitializeEditor() => Register();
#endif

        private static void Register()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RenderPipelineManager.endCameraRendering -= EndCamera;
            RenderPipelineManager.beginCameraRendering += BeginCamera;
            RenderPipelineManager.endCameraRendering += EndCamera;
            Application.quitting -= RestoreAll;
            Application.quitting += RestoreAll;
#if UNITY_EDITOR
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload -= RestoreAll;
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += RestoreAll;
#endif
        }

        private static void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                return;

            Transform trigger = camera.transform;
            LayerMask mask = 1;
            if (camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData))
            {
                mask = cameraData.volumeLayerMask;
                if (cameraData.volumeTrigger != null)
                    trigger = cameraData.volumeTrigger;
            }

            float near = camera.nearClipPlane;
            float far = camera.farClipPlane;
            bool overridden = false;
            foreach (Volume volume in VolumeManager.instance.GetVolumes(mask))
            {
                VolumeProfile profile = SkyVolumeBlendEvaluator.GetVolumeProfile(volume);
                if (profile == null || !profile.TryGet(out CameraClipSettings settings) || !settings.active)
                    continue;

                float weight = SkyVolumeBlendEvaluator.ComputeVolumeWeight(volume, trigger.position);
                if (weight <= 0f)
                    continue;

                if (settings.nearClipPlane.overrideState && IsFinite(settings.nearClipPlane.value))
                {
                    near = Mathf.Lerp(near, settings.nearClipPlane.value, weight);
                    overridden = true;
                }
                if (settings.farClipPlane.overrideState && IsFinite(settings.farClipPlane.value))
                {
                    far = Mathf.Lerp(far, settings.farClipPlane.value, weight);
                    overridden = true;
                }
            }

            if (!overridden || !IsFinite(near) || !IsFinite(far) || near <= 0f || far <= near)
            {
                if (s_Snapshots.TryGetValue(camera, out var pendingSnapshots))
                    pendingSnapshots.Push(default);
                return;
            }

            if (!s_Snapshots.TryGetValue(camera, out var snapshots))
                s_Snapshots.Add(camera, snapshots = new Stack<ClipPlanes>());
            snapshots.Push(new ClipPlanes { applied = true, near = camera.nearClipPlane, far = camera.farClipPlane });
            camera.nearClipPlane = near;
            camera.farClipPlane = far;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void EndCamera(ScriptableRenderContext context, Camera camera)
        {
            if (camera == null || !s_Snapshots.TryGetValue(camera, out var snapshots) || snapshots.Count == 0)
                return;
            ClipPlanes snapshot = snapshots.Pop();
            if (snapshot.applied)
            {
                camera.nearClipPlane = snapshot.near;
                camera.farClipPlane = snapshot.far;
            }
            if (snapshots.Count == 0)
                s_Snapshots.Remove(camera);
        }

        private static void RestoreAll()
        {
            foreach (var entry in s_Snapshots)
            {
                if (entry.Key == null)
                    continue;
                while (entry.Value.Count > 0)
                {
                    ClipPlanes snapshot = entry.Value.Pop();
                    if (snapshot.applied)
                    {
                        entry.Key.nearClipPlane = snapshot.near;
                        entry.Key.farClipPlane = snapshot.far;
                    }
                }
            }
            s_Snapshots.Clear();
        }
    }
}
