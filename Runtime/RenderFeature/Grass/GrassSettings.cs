using UnityEngine;

namespace EAStudio.Core.RenderFeature.Grass
{
    // Immutable values captured from the effective camera stack before graph execution.
    internal sealed class GrassSettings
    {
        internal readonly TerrainLayer Layer;
        internal readonly float Density, Range, Expansion;
        internal readonly int Level;
        internal readonly Vector4 Distribution, Shape, Distance, Wind, Effects, Shading;
        internal readonly Color Bottom, Top, Shadow, Wave;
        internal readonly Texture CloudTexture, WaveTexture;

        internal GrassSettings(Grass grass)
        {
            Layer = grass.terrainLayer.value;
            Density = grass.density.value;
            Range = grass.displayRange.value;
            Level = grass.topologyLevel.value;
            Distribution = new Vector4(Density, grass.minimumLayerWeight.value, grass.displayRange.value, grass.thinningStart.value);
            Shape = new Vector4(grass.bladeHeight.value, grass.bladeWidth.value, grass.heightVariation.value, grass.bend.value);
            Distance = new Vector4(grass.distantDensity.value, grass.distantWidth.value, 0f, 0f);
            float angle = grass.windDirection.value * Mathf.Deg2Rad;
            Wind = new Vector4(Mathf.Cos(angle), Mathf.Sin(angle), grass.windStrength.value, grass.windSpeed.value);
            Effects = new Vector4(grass.windFrequency.value, grass.effectTileSize.value,
                grass.cloudShadowStrength.value, grass.grassWaveStrength.value);
            Shading = new Vector4(grass.colorBias.value, grass.lightIntensity.value, 0f, 0f);
            Bottom = grass.bottomColor.value;
            Top = grass.topColor.value;
            Shadow = grass.shadowColor.value;
            Wave = grass.waveColor.value;
            CloudTexture = grass.cloudShadow.value != null ? grass.cloudShadow.value : Texture2D.whiteTexture;
            WaveTexture = grass.grassWave.value != null ? grass.grassWave.value : Texture2D.blackTexture;
            // Height variation only reduces height. Width compensation, bend and wind are all bounded.
            Expansion = Shape.x * (1f + Shape.w + Wind.z) + Shape.y * Distance.y;
        }
    }
}
