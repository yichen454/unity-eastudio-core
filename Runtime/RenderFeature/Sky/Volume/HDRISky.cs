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

        [Tooltip("Desired illuminance in Lux (Reserved for photometric physical lighting).")]
        public MinFloatParameter desiredLux = new MinFloatParameter(1000f, 0f);

        [Tooltip("Enable UV flow distortion (Reserved for animated wind simulation).")]
        public BoolParameter enableDistortion = new BoolParameter(false);

        [Tooltip("Distortion flow speed (Reserved).")]
        public MinFloatParameter distortionFlowSpeed = new MinFloatParameter(0.1f, 0f);

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
