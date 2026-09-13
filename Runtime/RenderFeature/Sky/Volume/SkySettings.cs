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
        [Tooltip("天空盒线性曝光强度倍率（1.0 为正常，2.0 为两倍亮度）。")]
        public MinFloatParameter exposure = new MinFloatParameter(1f, 0f);

        [Tooltip("环境漫反射与反射同步更新模式。")]
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
