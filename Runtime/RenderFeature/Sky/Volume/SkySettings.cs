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
        [Tooltip("Linear exposure multiplier for the skybox (1.0 = normal, 2.0 = 2x brighter).")]
        public MinFloatParameter exposure = new MinFloatParameter(1f, 0f);

        [Tooltip("Update mode for ambient and reflection synchronization.")]
        public EnvUpdateModeParameter updateMode = new EnvUpdateModeParameter(EnvUpdateMode.Realtime);

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + exposure.value.GetHashCode();
                hash = hash * 31 + updateMode.value.GetHashCode();
                return hash;
            }
        }
    }
}
