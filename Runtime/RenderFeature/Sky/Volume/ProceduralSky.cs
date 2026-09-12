using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    [Serializable, VolumeComponentMenu("Sky/Procedural Sky (Reserved)")]
    public class ProceduralSky : SkySettings
    {
        [Tooltip("Sun disk angular diameter.")]
        public ClampedFloatParameter sunSize = new ClampedFloatParameter(0.04f, 0.001f, 0.2f);

        [Tooltip("Sun halo convergence exponent.")]
        public ClampedFloatParameter sunConvergence = new ClampedFloatParameter(5f, 1f, 20f);

        [Tooltip("Atmospheric Rayleigh scattering density.")]
        public ClampedFloatParameter atmosphereThickness = new ClampedFloatParameter(1.0f, 0f, 5.0f);

        [Tooltip("Sky dome color tint.")]
        public ColorParameter skyTint = new ColorParameter(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);

        [Tooltip("Ground hemisphere ambient color.")]
        public ColorParameter groundColor = new ColorParameter(new Color(0.369f, 0.349f, 0.341f, 1f), false, false, true);
    }
}
