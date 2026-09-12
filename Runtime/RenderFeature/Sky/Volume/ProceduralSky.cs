using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    [Serializable, VolumeComponentMenu("Sky/Procedural Sky")]
    public class ProceduralSky : SkySettings
    {
        [Header("Sun & Corona")]
        [Tooltip("Sun disk angular diameter.")]
        public ClampedFloatParameter sunSize = new ClampedFloatParameter(0.04f, 0.001f, 0.2f);

        [Tooltip("Sun halo convergence exponent (Mie forward scattering).")]
        public ClampedFloatParameter sunConvergence = new ClampedFloatParameter(8f, 1f, 30f);

        [Header("Atmosphere Physics")]
        [Tooltip("Atmospheric Rayleigh scattering density.")]
        public ClampedFloatParameter atmosphereThickness = new ClampedFloatParameter(1.0f, 0.1f, 5.0f);

        [Tooltip("Ozone absorption multiplier. Controls the deep purple/magenta twilight at sunset (0 = dusty orange, 1 = Earth physical, 2+ = alien violet).")]
        public ClampedFloatParameter ozoneAbsorption = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);

        [Tooltip("Aerosol water vapor and dust haze density near horizon.")]
        public ClampedFloatParameter aerosolHaze = new ClampedFloatParameter(1.0f, 0.1f, 5.0f);

        [Header("Colors & Transitions")]
        [Tooltip("Sky dome color tint.")]
        public ColorParameter skyTint = new ColorParameter(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);

        [Tooltip("Ground hemisphere ambient color.")]
        public ColorParameter groundColor = new ColorParameter(new Color(0.369f, 0.349f, 0.341f, 1f), false, false, true);

        [Tooltip("Controls the smoothness of the atmospheric haze transition between sky and ground across the horizon.")]
        public ClampedFloatParameter groundFade = new ClampedFloatParameter(0.25f, 0.02f, 1.0f);

        [Tooltip("Night sky background color when the sun is below the horizon.")]
        public ColorParameter nightSkyColor = new ColorParameter(new Color(0.02f, 0.03f, 0.06f, 1f), false, false, true);

        public override int GetParameterHashCode()
        {
            unchecked
            {
                int hash = base.GetParameterHashCode();
                hash = hash * 31 + sunSize.value.GetHashCode();
                hash = hash * 31 + sunConvergence.value.GetHashCode();
                hash = hash * 31 + atmosphereThickness.value.GetHashCode();
                hash = hash * 31 + ozoneAbsorption.value.GetHashCode();
                hash = hash * 31 + aerosolHaze.value.GetHashCode();
                hash = hash * 31 + skyTint.value.GetHashCode();
                hash = hash * 31 + groundColor.value.GetHashCode();
                hash = hash * 31 + groundFade.value.GetHashCode();
                hash = hash * 31 + nightSkyColor.value.GetHashCode();
                return hash;
            }
        }
    }
}
