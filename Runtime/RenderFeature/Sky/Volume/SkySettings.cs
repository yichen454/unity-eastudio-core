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

        [Tooltip("Linear exposure multiplier for the skybox (1.0 = normal, 2.0 = 2x brighter).")]
        public MinFloatParameter exposure = new MinFloatParameter(1f, 0f);

        [Tooltip("Color tint multiplied with the sky color. Default is neutral #808080 like Unity standard skybox.")]
        public ColorParameter tint = new ColorParameter(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);

        [Tooltip("Update mode for ambient and reflection synchronization.")]
        public EnvUpdateModeParameter updateMode = new EnvUpdateModeParameter(EnvUpdateMode.Realtime);

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + rotation.value.GetHashCode();
                hash = hash * 31 + exposure.value.GetHashCode();
                hash = hash * 31 + tint.value.GetHashCode();
                hash = hash * 31 + updateMode.value.GetHashCode();
                return hash;
            }
        }
    }
}
