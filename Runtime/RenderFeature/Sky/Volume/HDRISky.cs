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

        [Tooltip("Y-axis rotation of the skybox in degrees.")]
        public ClampedFloatParameter rotation = new ClampedFloatParameter(0f, 0f, 360f);

        [Tooltip("Color tint multiplied with the sky color. Default is neutral #808080 like Unity standard skybox.")]
        public ColorParameter tint = new ColorParameter(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);

        public override int GetParameterHashCode()
        {
            unchecked
            {
                int hash = base.GetParameterHashCode();
                hash = hash * 31 + (hdriSky.value != null ? hdriSky.value.GetInstanceID() : 0);
                hash = hash * 31 + rotation.value.GetHashCode();
                hash = hash * 31 + tint.value.GetHashCode();
                return hash;
            }
        }
    }
}
