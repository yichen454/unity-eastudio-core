using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Shadow
{
    [Serializable, VolumeComponentMenu("EAStudio/Camera Clip Settings")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public sealed class CameraClipSettings : VolumeComponent
    {
        public MinFloatParameter nearClipPlane = new MinFloatParameter(0.3f, 0.001f);
        public MinFloatParameter farClipPlane = new MinFloatParameter(1000f, 0.001f);
    }
}
