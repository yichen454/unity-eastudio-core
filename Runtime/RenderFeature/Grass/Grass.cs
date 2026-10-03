using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Grass
{
    [Serializable]
    public sealed class TerrainLayerParameter : VolumeParameter<TerrainLayer>
    {
        public TerrainLayerParameter(TerrainLayer value, bool overrideState = false) : base(value, overrideState) { }
        public override void Interp(TerrainLayer from, TerrainLayer to, float t) => value = t > 0f ? to : from;
    }

    /// <summary>Camera configuration. TerrainLayer weights define the world-space grass footprint.</summary>
    [Serializable, VolumeComponentMenu("EAStudio/Grass")]
    [VolumeRequiresRendererFeatures(typeof(GrassRenderFeature))]
    public sealed class Grass : VolumeComponent, IPostProcessComponent
    {
        [Tooltip("Grass is off unless this toggle is explicitly overridden and enabled.")]
        public BoolParameter enable = new BoolParameter(false);
        public TerrainLayerParameter terrainLayer = new TerrainLayerParameter(null);
        [Tooltip("Candidate blades per square meter before TerrainLayer weighting and distance thinning.")]
        public ClampedFloatParameter density = new ClampedFloatParameter(16f, 0f, 256f);
        public ClampedFloatParameter minimumLayerWeight = new ClampedFloatParameter(0.1f, 0f, 1f);
        public MinFloatParameter displayRange = new MinFloatParameter(60f, 0f);
        [Tooltip("Distance at which thinning and camera-facing orientation begin.")]
        public MinFloatParameter thinningStart = new MinFloatParameter(15f, 0f);
        public ClampedFloatParameter distantDensity = new ClampedFloatParameter(0.15f, 0.01f, 1f);
        public ClampedFloatParameter distantWidth = new ClampedFloatParameter(3f, 1f, 8f);

        [Header("Blade")]
        [Tooltip("Levels 1-7. Level 3 produces five triangles per blade.")]
        public NoInterpClampedIntParameter topologyLevel = new NoInterpClampedIntParameter(3, 1, 7);
        public ClampedFloatParameter bladeHeight = new ClampedFloatParameter(0.5f, 0.01f, 4f);
        public ClampedFloatParameter bladeWidth = new ClampedFloatParameter(0.04f, 0.001f, 0.5f);
        public ClampedFloatParameter heightVariation = new ClampedFloatParameter(0.3f, 0f, 1f);
        public ClampedFloatParameter bend = new ClampedFloatParameter(0.2f, 0f, 1f);

        [Header("Color")]
        public ColorParameter bottomColor = new ColorParameter(new Color(0.12f, 0.25f, 0.05f));
        public ColorParameter topColor = new ColorParameter(new Color(0.5f, 0.7f, 0.15f));
        public ColorParameter shadowColor = new ColorParameter(new Color(0.4f, 0.5f, 0.35f));
        public ColorParameter waveColor = new ColorParameter(new Color(0.7f, 0.8f, 0.3f));
        public ClampedFloatParameter colorBias = new ClampedFloatParameter(0f, -1f, 1f);
        public ClampedFloatParameter lightIntensity = new ClampedFloatParameter(1f, 0f, 1f);

        [Header("Wind")]
        public ClampedFloatParameter windDirection = new ClampedFloatParameter(45f, 0f, 360f);
        public ClampedFloatParameter windStrength = new ClampedFloatParameter(0.35f, 0f, 2f);
        public MinFloatParameter windSpeed = new MinFloatParameter(1f, 0f);
        public MinFloatParameter windFrequency = new MinFloatParameter(0.1f, 0f);

        [Header("Optional World-Space Effects")]
        public Texture2DParameter cloudShadow = new Texture2DParameter(null);
        public Texture2DParameter grassWave = new Texture2DParameter(null);
        [Tooltip("Repeating texture scale in world meters.")]
        public MinFloatParameter effectTileSize = new MinFloatParameter(100f, 0.01f);
        public ClampedFloatParameter cloudShadowStrength = new ClampedFloatParameter(0.5f, 0f, 1f);
        public ClampedFloatParameter grassWaveStrength = new ClampedFloatParameter(0.5f, 0f, 1f);

        public bool IsActive()
        {
            if (!active || !enable.overrideState || !enable.value || terrainLayer.value == null
                || density.value <= 0f || displayRange.value <= 0f
                || !Is2DTexture(cloudShadow.value) || !Is2DTexture(grassWave.value)) return false;
            foreach (VolumeParameter parameter in parameters)
            {
                if (parameter is FloatParameter number && !IsFinite(number.value)) return false;
                if (parameter is ColorParameter color && (!IsFinite(color.value.r) || !IsFinite(color.value.g)
                    || !IsFinite(color.value.b) || !IsFinite(color.value.a))) return false;
            }
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool Is2DTexture(Texture texture) => texture == null || texture.dimension == TextureDimension.Tex2D;

        public bool IsTileCompatible() => false;
    }
}
