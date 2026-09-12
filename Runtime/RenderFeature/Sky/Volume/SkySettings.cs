using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public enum EnvUpdateMode
    {
        OnDemand = 0,
        Realtime = 1
    }

    [Serializable]
    public sealed class EnvUpdateModeParameter : VolumeParameter<EnvUpdateMode>
    {
        public EnvUpdateModeParameter(EnvUpdateMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    public abstract class SkySettings : VolumeComponent
    {
        [Tooltip("Y-axis rotation of the skybox in degrees.")]
        public ClampedFloatParameter rotation = new ClampedFloatParameter(0f, 0f, 360f);

        [Tooltip("Exposure value (in EV) applied to the sky.")]
        public FloatParameter exposure = new FloatParameter(0f);

        [Tooltip("Linear multiplier for the sky intensity.")]
        public MinFloatParameter multiplier = new MinFloatParameter(1f, 0f);

        [Tooltip("Color tint multiplied with the sky color.")]
        public ColorParameter tint = new ColorParameter(Color.white, false, false, true);

        [Tooltip("Update mode for ambient and reflection synchronization.")]
        public EnvUpdateModeParameter updateMode = new EnvUpdateModeParameter(EnvUpdateMode.Realtime);

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + rotation.value.GetHashCode();
                hash = hash * 31 + exposure.value.GetHashCode();
                hash = hash * 31 + multiplier.value.GetHashCode();
                hash = hash * 31 + tint.value.GetHashCode();
                hash = hash * 31 + updateMode.value.GetHashCode();
                return hash;
            }
        }
    }
}
