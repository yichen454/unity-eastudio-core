using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    [Serializable, VolumeComponentMenu("Sky/Procedural Sky")]
    public class ProceduralSky : SkySettings
    {
        [Header("太阳与日冕 (Sun & Corona)")]
        [Tooltip("太阳本体光盘视直径大小。")]
        public ClampedFloatParameter sunSize = new ClampedFloatParameter(0.04f, 0.0f, 0.2f);

        [Tooltip("太阳在天空盒上的视觉表面与日冕亮度倍率（1.0 为默认基准亮度，调大更耀眼，调小更柔和）。")]
        public MinFloatParameter sunBrightness = new MinFloatParameter(1.0f, 0.0f);

        [Tooltip("太阳光晕聚合度指数（米氏前向散射衰减率，数值越大光晕越收拢紧实）。")]
        public ClampedFloatParameter sunConvergence = new ClampedFloatParameter(8f, 1f, 30f);

        [Header("大气物理散射 (Atmosphere Physics)")]
        [Tooltip("大气层厚度与瑞利散射密度倍率。")]
        public ClampedFloatParameter atmosphereThickness = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);

        [Tooltip("臭氧层吸收倍率。控制黄昏夕阳时的深紫/洋红晚霞过渡（0 为干旱黄昏橘色，1 为地球真实物理值，2+ 为魔幻异星深紫）。")]
        public ClampedFloatParameter ozoneAbsorption = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);

        [Tooltip("地平线附近气溶胶（水汽、灰尘）雾霾散射密度。")]
        public ClampedFloatParameter aerosolHaze = new ClampedFloatParameter(1.0f, 0.1f, 5.0f);

        [Header("色彩与地表过渡 (Colors & Transitions)")]
        [Tooltip("天穹整体色调偏向调节。")]
        public ColorParameter skyTint = new ColorParameter(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);

        [Tooltip("下半球地面环境底色。")]
        public ColorParameter groundColor = new ColorParameter(new Color(0.369f, 0.349f, 0.341f, 1f), false, false, true);

        [Tooltip("地平线地表与天空的大气消隐过渡宽度（平滑避免生硬切边）。")]
        public ClampedFloatParameter groundFade = new ClampedFloatParameter(0.25f, 0.02f, 1.0f);

        [Tooltip("太阳落入地平线后的夜空物理深空底色（未指定夜空 HDRI 时自动降级使用）。")]
        public ColorParameter nightSkyColor = new ColorParameter(new Color(0.02f, 0.03f, 0.06f, 1f), false, false, true);

        [Header("夜空与星空 HDRI (Night Sky & Stars HDRI)")]
        [Tooltip("可选夜空星辰 HDRI Cubemap（例如银河、星空全景图）。随落日平滑淡入。")]
        public CubemapParameter nightSkyMap = new CubemapParameter(null);

        [Tooltip("夜空 HDRI 星空贴图的曝光强度。")]
        public MinFloatParameter nightExposure = new MinFloatParameter(1.0f, 0.0f);

        [Tooltip("夜空星空贴图绕 Y 轴的水平旋转角度（0-360度）。")]
        public ClampedFloatParameter nightRotation = new ClampedFloatParameter(0.0f, 0.0f, 360.0f);

        public override int GetParameterHashCode()
        {
            unchecked
            {
                int hash = base.GetParameterHashCode();
                hash = hash * 31 + sunSize.value.GetHashCode();
                hash = hash * 31 + sunBrightness.value.GetHashCode();
                hash = hash * 31 + sunConvergence.value.GetHashCode();
                hash = hash * 31 + atmosphereThickness.value.GetHashCode();
                hash = hash * 31 + ozoneAbsorption.value.GetHashCode();
                hash = hash * 31 + aerosolHaze.value.GetHashCode();
                hash = hash * 31 + skyTint.value.GetHashCode();
                hash = hash * 31 + groundColor.value.GetHashCode();
                hash = hash * 31 + groundFade.value.GetHashCode();
                hash = hash * 31 + nightSkyColor.value.GetHashCode();
                hash = hash * 31 + (nightSkyMap.value != null ? nightSkyMap.value.GetInstanceID() : 0);
                hash = hash * 31 + nightExposure.value.GetHashCode();
                hash = hash * 31 + nightRotation.value.GetHashCode();
                return hash;
            }
        }
    }
}
