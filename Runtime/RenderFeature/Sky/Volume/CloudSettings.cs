using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    [Serializable, VolumeComponentMenu("Sky/Cloud Settings (Reserved)")]
    public class CloudSettings : VolumeComponent
    {
        [Tooltip("Overall opacity of the cloud layer.")]
        public ClampedFloatParameter opacity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [Tooltip("Cloud base altitude in meters.")]
        public MinFloatParameter altitude = new MinFloatParameter(2000f, 0f);

        [Tooltip("Cloud layer thickness in meters.")]
        public MinFloatParameter thickness = new MinFloatParameter(1000f, 0f);

        [Tooltip("Color tint applied to the cloud layer.")]
        public ColorParameter tint = new ColorParameter(Color.white, false, false, true);
    }
}
