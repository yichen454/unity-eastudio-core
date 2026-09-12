using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    [Serializable]
    public sealed class CubemapParameter : VolumeParameter<Cubemap>
    {
        public CubemapParameter(Cubemap value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Sky/HDRI Sky")]
    public class HDRISky : SkySettings
    {
        [Tooltip("The HDRI Cubemap texture.")]
        public CubemapParameter hdriSky = new CubemapParameter(null);

        public override int GetParameterHashCode()
        {
            unchecked
            {
                int hash = base.GetParameterHashCode();
                hash = hash * 31 + (hdriSky.value != null ? hdriSky.value.GetInstanceID() : 0);
                return hash;
            }
        }
    }
}
