using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public enum SkyType
    {
        None = 0,
        HDRI = 1,
        Procedural = 2
    }

    public enum SkyAmbientMode
    {
        Realtime = 0,
        OnChanged = 1,
        Off = 2
    }

    public enum CloudType
    {
        None = 0,
        Volumetric = 1
    }

    [Serializable]
    public sealed class SkyTypeParameter : VolumeParameter<SkyType>
    {
        public SkyTypeParameter(SkyType value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class SkyAmbientModeParameter : VolumeParameter<SkyAmbientMode>
    {
        public SkyAmbientModeParameter(SkyAmbientMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class CloudTypeParameter : VolumeParameter<CloudType>
    {
        public CloudTypeParameter(CloudType value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Sky/Visual Environment")]
    public class VisualEnvironment : VolumeComponent
    {
        [Tooltip("Type of sky to display and evaluate.")]
        public SkyTypeParameter skyType = new SkyTypeParameter(SkyType.HDRI);

        [Tooltip("Evaluation mode for ambient probe (SH).")]
        public SkyAmbientModeParameter skyAmbientMode = new SkyAmbientModeParameter(SkyAmbientMode.Realtime);

        [Header("Environment Lighting")]
        [Tooltip("Linear intensity multiplier for Environment Lighting (Diffuse SH / Ambient). Matches Lighting -> Environment Lighting -> Intensity Multiplier.")]
        public MinFloatParameter lightingMultiplier = new MinFloatParameter(1f, 0f);

        [Header("Clouds & Atmosphere")]
        [Tooltip("Type of clouds to render (Reserved for future expansion).")]
        public CloudTypeParameter cloudType = new CloudTypeParameter(CloudType.None);

        [Header("Wind")]
        [Tooltip("Global wind orientation in degrees.")]
        public ClampedFloatParameter windOrientation = new ClampedFloatParameter(0f, 0f, 360f);

        [Tooltip("Global wind speed in m/s.")]
        public MinFloatParameter windSpeed = new MinFloatParameter(0f, 0f);
    }
}
