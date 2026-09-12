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

    public enum CloudNoiseType
    {
        Worley = 0,        // Puffy cellular cumulus (细胞积云，饱满成团)
        Perlin = 1,        // Smooth wispy cirrus (平滑卷云，丝状拉伸)
        Billow = 2,        // Billowing turbulent puffs (翻滚浓积云)
        Stratocumulus = 3  // Hybrid layered cloud deck (层积云，大片起伏)
    }

    [Serializable]
    public sealed class CloudDownscaleParameter : VolumeParameter<CloudDownscale>
    {
        public CloudDownscaleParameter(CloudDownscale value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class CloudNoiseTypeParameter : VolumeParameter<CloudNoiseType>
    {
        public CloudNoiseTypeParameter(CloudNoiseType value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Sky/Cloud Settings")]
    public class CloudSettings : VolumeComponent
    {
        [Header("Layer 1 - Low Altitude (Cumulus)")]
        [Tooltip("Noise shape archetype for Layer 1.")]
        public CloudNoiseTypeParameter shapeType = new CloudNoiseTypeParameter(CloudNoiseType.Worley);

        [Tooltip("Cloud coverage fraction for Layer 1.")]
        public ClampedFloatParameter coverage = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Tooltip("Density multiplier for Layer 1.")]
        public ClampedFloatParameter density = new ClampedFloatParameter(1.0f, 0.1f, 3.0f);

        [Tooltip("Noise tiling scale for Layer 1.")]
        public MinFloatParameter scale = new MinFloatParameter(1.0f, 0.1f);

        [Tooltip("Altitude of Layer 1 in meters (used for shadow projection).")]
        public MinFloatParameter altitude = new MinFloatParameter(2000f, 100f);

        [Header("Layer 2 - High Altitude (Cirrus)")]
        [Tooltip("Enable secondary high-altitude cloud layer.")]
        public BoolParameter enableLayer2 = new BoolParameter(true);

        [Tooltip("Noise shape archetype for Layer 2.")]
        public CloudNoiseTypeParameter layer2ShapeType = new CloudNoiseTypeParameter(CloudNoiseType.Perlin);

        [Tooltip("Cloud coverage fraction for Layer 2.")]
        public ClampedFloatParameter layer2Coverage = new ClampedFloatParameter(0.4f, 0.0f, 1.0f);

        [Tooltip("Density multiplier for Layer 2.")]
        public ClampedFloatParameter layer2Density = new ClampedFloatParameter(0.5f, 0.05f, 2.0f);

        [Tooltip("Noise tiling scale for Layer 2.")]
        public MinFloatParameter layer2Scale = new MinFloatParameter(2.5f, 0.1f);

        [Tooltip("Altitude of Layer 2 in meters.")]
        public MinFloatParameter layer2Altitude = new MinFloatParameter(6000f, 2000f);

        [Tooltip("Relative wind speed multiplier for Layer 2.")]
        public ClampedFloatParameter layer2SpeedMultiplier = new ClampedFloatParameter(1.5f, 0.0f, 5.0f);

        [Header("Shape & Details")]
        [Tooltip("High-frequency edge erosion strength (0 = smooth puffy clouds, 1 = wispy shredded clouds).")]
        public ClampedFloatParameter detailErosion = new ClampedFloatParameter(0.55f, 0.0f, 1.0f);

        [Tooltip("Horizon fade softness to prevent clouds abruptly meeting the horizon.")]
        public ClampedFloatParameter horizonFade = new ClampedFloatParameter(0.16f, 0.02f, 0.5f);

        [Header("Lighting & Scattering")]
        [Tooltip("Forward scattering silver lining intensity when looking toward the sun.")]
        public ClampedFloatParameter silverLining = new ClampedFloatParameter(2.5f, 0.0f, 10.0f);

        [Tooltip("Sunlit cloud highlight color.")]
        public ColorParameter cloudColor = new ColorParameter(Color.white, false, false, true);

        [Tooltip("Self-shadowing shade color on the unlit bottom of clouds.")]
        public ColorParameter shadowColor = new ColorParameter(new Color(0.35f, 0.38f, 0.45f, 1f), false, false, true);

        [Header("Shadows & Multi-use")]
        [Tooltip("Shadow attenuation strength cast on ground/objects (0 = no shadow, 1 = maximum shadow).")]
        public ClampedFloatParameter shadowStrength = new ClampedFloatParameter(0.6f, 0.0f, 1.0f);

        [Header("Performance")]
        [Tooltip("Generation resolution scale (Full = 1:1, Half = 1/4 pixels, Quarter = 1/16 pixels).")]
        public CloudDownscaleParameter downscale = new CloudDownscaleParameter(CloudDownscale.Half);

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + shapeType.value.GetHashCode();
                hash = hash * 31 + coverage.value.GetHashCode();
                hash = hash * 31 + density.value.GetHashCode();
                hash = hash * 31 + scale.value.GetHashCode();
                hash = hash * 31 + altitude.value.GetHashCode();
                hash = hash * 31 + enableLayer2.value.GetHashCode();
                hash = hash * 31 + layer2ShapeType.value.GetHashCode();
                hash = hash * 31 + layer2Coverage.value.GetHashCode();
                hash = hash * 31 + layer2Density.value.GetHashCode();
                hash = hash * 31 + layer2Scale.value.GetHashCode();
                hash = hash * 31 + layer2Altitude.value.GetHashCode();
                hash = hash * 31 + layer2SpeedMultiplier.value.GetHashCode();
                hash = hash * 31 + detailErosion.value.GetHashCode();
                hash = hash * 31 + horizonFade.value.GetHashCode();
                hash = hash * 31 + silverLining.value.GetHashCode();
                hash = hash * 31 + cloudColor.value.GetHashCode();
                hash = hash * 31 + shadowColor.value.GetHashCode();
                hash = hash * 31 + shadowStrength.value.GetHashCode();
                hash = hash * 31 + downscale.value.GetHashCode();
                return hash;
            }
        }
    }
}
