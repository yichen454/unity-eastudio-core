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
        Billow = 1,        // Billowing turbulent puffs (翻滚浓积云)
        Perlin = 2,        // Smooth wispy cirrus (平滑卷云，丝状拉伸)
        Value = 3,         // Smooth value noise (平滑噪声)
        Stratocumulus = 4  // Hybrid layered cloud deck (层积云，大片起伏)
    }

    public enum CloudDetailType
    {
        Worley = 0,        // Cellular erosion
        Perlin = 1         // Warped wispy erosion
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

    [Serializable]
    public sealed class CloudDetailTypeParameter : VolumeParameter<CloudDetailType>
    {
        public CloudDetailTypeParameter(CloudDetailType value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Sky/Cloud Settings")]
    public class CloudSettings : VolumeComponent
    {
        [Header("Layer 1 - Low Altitude (Cumulus)")]
        [Tooltip("Enable primary cloud deck.")]
        public BoolParameter enableLayer1 = new BoolParameter(true);

        [Tooltip("Pre-baked noise shape archetype for Layer 1.")]
        public CloudNoiseTypeParameter shapeType = new CloudNoiseTypeParameter(CloudNoiseType.Worley);

        [Tooltip("High-frequency detail noise type for Layer 1 edge erosion.")]
        public CloudDetailTypeParameter detailType = new CloudDetailTypeParameter(CloudDetailType.Worley);

        [Tooltip("Optional custom base noise texture override for Layer 1.")]
        public TextureParameter customBaseTexture = new TextureParameter(null);

        [Tooltip("Optional custom detail noise texture override for Layer 1.")]
        public TextureParameter customDetailTexture = new TextureParameter(null);

        [Tooltip("Cloud coverage fraction for Layer 1 (0 = completely clear, 1 = overcast).")]
        public ClampedFloatParameter coverage = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Tooltip("Opacity / Density multiplier for Layer 1.")]
        public ClampedFloatParameter density = new ClampedFloatParameter(1.0f, 0.0f, 3.0f);

        [Tooltip("Volumetric vertical thickness for light marching self-shadowing (0 = flat, higher = thick 3D puffs).")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(15.0f, 0.0f, 50.0f);

        [Tooltip("Beer-Lambert light absorption exponent (controls depth and darkness of underside shadows).")]
        public ClampedFloatParameter absorption = new ClampedFloatParameter(1.5f, 0.0f, 5.0f);

        [Tooltip("Noise tiling scale for Layer 1.")]
        public MinFloatParameter scale = new MinFloatParameter(1.0f, 0.1f);

        [Tooltip("Altitude of Layer 1 in meters (used for shadow projection).")]
        public MinFloatParameter altitude = new MinFloatParameter(2000f, 100f);

        [Header("Layer 2 - High Altitude (Cirrus)")]
        [Tooltip("Enable secondary high-altitude cloud layer.")]
        public BoolParameter enableLayer2 = new BoolParameter(false);

        [Tooltip("Pre-baked noise shape archetype for Layer 2.")]
        public CloudNoiseTypeParameter layer2ShapeType = new CloudNoiseTypeParameter(CloudNoiseType.Perlin);

        [Tooltip("High-frequency detail noise type for Layer 2.")]
        public CloudDetailTypeParameter layer2DetailType = new CloudDetailTypeParameter(CloudDetailType.Perlin);

        [Tooltip("Optional custom base noise texture override for Layer 2.")]
        public TextureParameter layer2CustomBaseTexture = new TextureParameter(null);

        [Tooltip("Optional custom detail noise texture override for Layer 2.")]
        public TextureParameter layer2CustomDetailTexture = new TextureParameter(null);

        [Tooltip("Cloud coverage fraction for Layer 2.")]
        public ClampedFloatParameter layer2Coverage = new ClampedFloatParameter(0.4f, 0.0f, 1.0f);

        [Tooltip("Opacity / Density multiplier for Layer 2.")]
        public ClampedFloatParameter layer2Density = new ClampedFloatParameter(0.5f, 0.0f, 2.0f);

        [Tooltip("Volumetric vertical thickness for Layer 2.")]
        public ClampedFloatParameter layer2Thickness = new ClampedFloatParameter(10.0f, 0.0f, 50.0f);

        [Tooltip("Beer-Lambert light absorption exponent for Layer 2.")]
        public ClampedFloatParameter layer2Absorption = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);

        [Tooltip("Noise tiling scale for Layer 2.")]
        public MinFloatParameter layer2Scale = new MinFloatParameter(2.5f, 0.1f);

        [Tooltip("Altitude of Layer 2 in meters.")]
        public MinFloatParameter layer2Altitude = new MinFloatParameter(6000f, 2000f);

        [Tooltip("Relative wind speed multiplier for Layer 2.")]
        public ClampedFloatParameter layer2SpeedMultiplier = new ClampedFloatParameter(1.5f, 0.0f, 5.0f);

        [Header("Shape & Details")]
        [Tooltip("High-frequency edge erosion strength using pow4 falloff (0 = smooth, 1 = wispy fibrous edges).")]
        public ClampedFloatParameter detailErosion = new ClampedFloatParameter(0.65f, 0.0f, 1.0f);

        [Tooltip("Detail noise frequency scale.")]
        public MinFloatParameter detailScale = new MinFloatParameter(3.0f, 0.5f);

        [Tooltip("Earth curvature parallax perspective compression near horizon.")]
        public ClampedFloatParameter curvature = new ClampedFloatParameter(0.35f, 0.0f, 1.0f);

        [Tooltip("Horizon fade softness to prevent clouds abruptly meeting the horizon.")]
        public ClampedFloatParameter horizonFade = new ClampedFloatParameter(0.12f, 0.01f, 0.5f);

        [Header("Lighting & Scattering")]
        [Tooltip("Henyey-Greenstein forward scattering silver lining intensity when looking toward the sun.")]
        public ClampedFloatParameter silverLining = new ClampedFloatParameter(2.5f, 0.0f, 10.0f);

        [Tooltip("Silver lining rim width.")]
        public ClampedFloatParameter silverLiningWidth = new ClampedFloatParameter(0.15f, 0.01f, 0.5f);

        [Tooltip("Number of lightmarching ray steps for 3D self-shadowing (higher = more accurate volume depth).")]
        public ClampedIntParameter lightmarchSteps = new ClampedIntParameter(4, 1, 8);

        [Tooltip("Sunlit cloud highlight color.")]
        public ColorParameter cloudColor = new ColorParameter(Color.white, false, false, true);

        [Tooltip("Self-shadowing shade color on the unlit bottom of clouds.")]
        public ColorParameter shadowColor = new ColorParameter(new Color(0.35f, 0.38f, 0.45f, 1f), false, false, true);

        [Header("Shadows & Light Cookie")]
        [Tooltip("Enable Directional Light Cookie cloud shadows on scene objects and terrain.")]
        public BoolParameter castShadows = new BoolParameter(false);

        [Tooltip("Coverage area size (in meters) of the cloud shadow cookie projection.")]
        public MinFloatParameter shadowArea = new MinFloatParameter(800f, 50f);

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
                hash = hash * 31 + enableLayer1.value.GetHashCode();
                hash = hash * 31 + shapeType.value.GetHashCode();
                hash = hash * 31 + detailType.value.GetHashCode();
                hash = hash * 31 + customBaseTexture.value.GetHashCode();
                hash = hash * 31 + customDetailTexture.value.GetHashCode();
                hash = hash * 31 + coverage.value.GetHashCode();
                hash = hash * 31 + density.value.GetHashCode();
                hash = hash * 31 + thickness.value.GetHashCode();
                hash = hash * 31 + absorption.value.GetHashCode();
                hash = hash * 31 + scale.value.GetHashCode();
                hash = hash * 31 + altitude.value.GetHashCode();
                hash = hash * 31 + curvature.value.GetHashCode();

                hash = hash * 31 + enableLayer2.value.GetHashCode();
                hash = hash * 31 + layer2ShapeType.value.GetHashCode();
                hash = hash * 31 + layer2DetailType.value.GetHashCode();
                hash = hash * 31 + layer2CustomBaseTexture.value.GetHashCode();
                hash = hash * 31 + layer2CustomDetailTexture.value.GetHashCode();
                hash = hash * 31 + layer2Coverage.value.GetHashCode();
                hash = hash * 31 + layer2Density.value.GetHashCode();
                hash = hash * 31 + layer2Thickness.value.GetHashCode();
                hash = hash * 31 + layer2Absorption.value.GetHashCode();
                hash = hash * 31 + layer2Scale.value.GetHashCode();
                hash = hash * 31 + layer2Altitude.value.GetHashCode();
                hash = hash * 31 + layer2SpeedMultiplier.value.GetHashCode();

                hash = hash * 31 + detailErosion.value.GetHashCode();
                hash = hash * 31 + detailScale.value.GetHashCode();
                hash = hash * 31 + horizonFade.value.GetHashCode();
                hash = hash * 31 + silverLining.value.GetHashCode();
                hash = hash * 31 + silverLiningWidth.value.GetHashCode();
                hash = hash * 31 + lightmarchSteps.value.GetHashCode();
                hash = hash * 31 + cloudColor.value.GetHashCode();
                hash = hash * 31 + shadowColor.value.GetHashCode();
                hash = hash * 31 + castShadows.value.GetHashCode();
                hash = hash * 31 + shadowArea.value.GetHashCode();
                hash = hash * 31 + shadowStrength.value.GetHashCode();
                hash = hash * 31 + downscale.value.GetHashCode();
                return hash;
            }
        }
    }
}
