using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public enum CloudDownscale
    {
        Full = 1,
        Half = 2,
        Quarter = 4
    }

    [Serializable]
    public sealed class CloudDownscaleParameter : VolumeParameter<CloudDownscale>
    {
        public CloudDownscaleParameter(CloudDownscale value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Sky/Cloud Settings")]
    public class CloudSettings : VolumeComponent
    {
        [Tooltip("是否启用云层渲染。")]
        public BoolParameter enableClouds = new BoolParameter(true);

        [Tooltip("云层离屏渲染分辨率倍率：Full(1:1 全分辨率)、Half(1/2 半分辨率，推荐)、Quarter(1/4 四分之一分辨率)。")]
        public CloudDownscaleParameter downscale = new CloudDownscaleParameter(CloudDownscale.Half);

        public CloudSettings()
        {
            enableClouds.overrideState = true;
            downscale.overrideState = true;
        }

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + enableClouds.value.GetHashCode();
                hash = hash * 31 + downscale.value.GetHashCode();
                return hash;
            }
        }
    }
}
