using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Scripting.APIUpdating;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Resolution divisor relative to the camera target.
    /// </summary>
    public enum CloudResolution
    {
        [InspectorName("Full Resolution")]
        Full = 1,
        [InspectorName("Half Resolution")]
        Half = 2,
        [InspectorName("Quarter Resolution")]
        Quarter = 4,
        [InspectorName("Eighth Resolution")]
        Eighth = 8,
    }

    /// <summary>
    /// Distortion mode used to simulate cloud movement.
    /// The member names and the <see cref="InspectorNameAttribute"/> on <see cref="Procedural"/> are kept
    /// identical to HDRP so the Inspector reads the same.
    /// </summary>
    public enum CloudDistortionMode
    {
        /// <summary>No distortion.</summary>
        None,
        /// <summary>Procedural distortion.</summary>
        [InspectorName("Horizontal")]
        Procedural,
    }

    /// <summary>
    /// HDRP-aligned 2D cloud layer: an equirectangular cloud map with per-channel opacity weights and a
    /// short raymarching loop for pseudo-volumetric self-shadowing.
    /// </summary>
    /// <remarks>
    /// Field names, Inspector names, order, section headers and indentation mirror HDRP's Cloud Layer
    /// (<c>com.unity.render-pipelines.high-definition</c> 17.3.0, <c>Runtime/Sky/CloudSystem/CloudLayer</c>),
    /// so the two Inspectors read the same. Unity's volume editor supplies the nesting through
    /// <see cref="VolumeComponent.Indent"/>; HDRP additionally uses a custom editor only to pick a dynamic
    /// "Layer A"/"Layer B" label, which a static <see cref="HeaderAttribute"/> covers while the component
    /// supports a single layer. Rows HDRP shows that have no implementation here (Layers, Cast Shadows and
    /// the ground-projected Cloud Shadows group) are deliberately not declared rather than shipped as
    /// dead controls.
    /// </remarks>
    [Serializable, VolumeComponentMenu("EAStudio/Sky/Cloud Layer")]
    [MovedFrom(true, "EAStudio.Core.RenderFeature.Sky", "EAStudio.Core.Runtime", "CloudSettings")]
    public class CloudLayer : VolumeComponent
    {
        [Tooltip("Controls the global opacity of the cloud layer.")]
        public ClampedFloatParameter opacity = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [Tooltip("Check this box if the cloud layer covers only the upper part of the sky.")]
        public BoolParameter upperHemisphereOnly = new BoolParameter(true);

        [Tooltip("Cloud texture resolution relative to each camera target dimension.")]
        public EnumParameter<CloudResolution> resolution = new EnumParameter<CloudResolution>(CloudResolution.Half);

        [Header("Layer A")]
        [Tooltip("Specify the texture used to render the clouds (in LatLong layout).")]
        public Texture2DParameter cloudMap = new Texture2DParameter(null);

        [Tooltip("Opacity multiplier for the red channel.")]
        [Indent(1)] public ClampedFloatParameter opacityR = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        [Tooltip("Opacity multiplier for the green channel.")]
        [Indent(1)] public ClampedFloatParameter opacityG = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        [Tooltip("Opacity multiplier for the blue channel.")]
        [Indent(1)] public ClampedFloatParameter opacityB = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        [Tooltip("Opacity multiplier for the alpha channel.")]
        [Indent(1)] public ClampedFloatParameter opacityA = new ClampedFloatParameter(0.0f, 0.0f, 1.0f);

        [Tooltip("Altitude of the cloud layer in meters. Sets how early the deck dissolves into the horizon: a low deck fades over a wider band.")]
        public MinFloatParameter altitude = new MinFloatParameter(2000.0f, 0.0f);

        [InspectorName("Fade")]
        [Tooltip("Width of the band above the horizon over which the cloud layer dissolves into the sky, in degrees of elevation. The deck is never drawn below the horizon; 0 gives it a hard edge.")]
        public ClampedFloatParameter horizonFade = new ClampedFloatParameter(5.0f, 0.0f, 45.0f);

        [Tooltip("Sets the rotation of the clouds (in degrees).")]
        public ClampedFloatParameter rotation = new ClampedFloatParameter(0.0f, 0.0f, 360.0f);

        [Tooltip("Specifies the color used to tint the clouds.")]
        public ColorParameter tint = new ColorParameter(Color.white, false, false, true);

        [InspectorName("Exposure Compensation")]
        [Tooltip("Sets the exposure compensation of the clouds in EV.")]
        public FloatParameter exposure = new FloatParameter(0.0f);

        [InspectorName("Wind")]
        [Tooltip("Distortion mode used to simulate cloud movement.")]
        public EnumParameter<CloudDistortionMode> distortionMode = new EnumParameter<CloudDistortionMode>(CloudDistortionMode.Procedural);

        [InspectorName("Orientation")]
        [Tooltip("Controls the orientation of the wind relative to the X world vector. On an equirectangular layer only the component along the wrapping longitude axis is representable, so a cross-wind has no visible motion.")]
        [Indent(1)] public ClampedFloatParameter scrollOrientation = new ClampedFloatParameter(0.0f, 0.0f, 360.0f);

        [InspectorName("Speed")]
        [Tooltip("Sets the wind speed in meters per second.")]
        [Indent(1)] public MinFloatParameter scrollSpeed = new MinFloatParameter(20.0f, 0.0f);

        [InspectorName("Raymarching")]
        [Tooltip("Simulates cloud self-shadowing using raymarching.")]
        public BoolParameter lighting = new BoolParameter(true);

        [Tooltip("Number of raymarching steps.")]
        [Indent(1)] public ClampedIntParameter steps = new ClampedIntParameter(6, 2, 32);

        [InspectorName("Density")]
        [Tooltip("Density of the cloud layer. Drives the extinction of the self-shadowing raymarch, so higher values darken the cloud bases more.")]
        [Indent(1)] public ClampedFloatParameter thickness = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Tooltip("Controls the influence of the ambient probe on the cloud layer volume. A lower value will suppress the ambient light and produce darker clouds overall.")]
        [Indent(1)] public ClampedFloatParameter ambientProbeDimmer = new ClampedFloatParameter(1.0f, 0.0f, 1.0f);

        protected override void OnEnable()
        {
            base.OnEnable();
            switch ((int)resolution.value)
            {
                case 256:
                    resolution.value = CloudResolution.Eighth;
                    break;
                case 512:
                    resolution.value = CloudResolution.Quarter;
                    break;
                case 1024:
                    resolution.value = CloudResolution.Half;
                    break;
                case 2048:
                case 4096:
                case 8192:
                    resolution.value = CloudResolution.Full;
                    break;
            }
        }

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + opacity.value.GetHashCode();
                hash = hash * 31 + upperHemisphereOnly.value.GetHashCode();
                hash = hash * 31 + resolution.value.GetHashCode();
                hash = hash * 31 + (cloudMap.value != null ? cloudMap.value.GetInstanceID() : 0);
                hash = hash * 31 + opacityR.value.GetHashCode();
                hash = hash * 31 + opacityG.value.GetHashCode();
                hash = hash * 31 + opacityB.value.GetHashCode();
                hash = hash * 31 + opacityA.value.GetHashCode();
                hash = hash * 31 + altitude.value.GetHashCode();
                hash = hash * 31 + horizonFade.value.GetHashCode();
                hash = hash * 31 + rotation.value.GetHashCode();
                hash = hash * 31 + tint.value.GetHashCode();
                hash = hash * 31 + exposure.value.GetHashCode();
                hash = hash * 31 + distortionMode.value.GetHashCode();
                hash = hash * 31 + scrollOrientation.value.GetHashCode();
                hash = hash * 31 + scrollSpeed.value.GetHashCode();
                hash = hash * 31 + lighting.value.GetHashCode();
                hash = hash * 31 + steps.value.GetHashCode();
                hash = hash * 31 + thickness.value.GetHashCode();
                hash = hash * 31 + ambientProbeDimmer.value.GetHashCode();
                return hash;
            }
        }
    }
}
