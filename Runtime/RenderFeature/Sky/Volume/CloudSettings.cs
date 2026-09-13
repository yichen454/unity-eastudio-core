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
        [Header("第一层云 - 低空积云 (Layer 1 - Cumulus)")]
        [Tooltip("是否启用第一层主云层。")]
        public BoolParameter enableLayer1 = new BoolParameter(true);

        [Tooltip("第一层基础形态预制噪声类型：Worley(细胞积云，团状饱满)、Billow(翻滚浓积云)、Perlin(平滑卷云)、Value(平滑噪声)、Stratocumulus(起伏层积云)。")]
        public CloudNoiseTypeParameter shapeType = new CloudNoiseTypeParameter(CloudNoiseType.Worley);

        [Tooltip("第一层边缘侵蚀高频细节噪声类型：Worley(蜂窝细胞边缘)、Perlin(扭曲撕裂絮状边缘)。")]
        public CloudDetailTypeParameter detailType = new CloudDetailTypeParameter(CloudDetailType.Worley);

        [Tooltip("可选自定义第一层基础形状噪声贴图。")]
        public TextureParameter customBaseTexture = new TextureParameter(null);

        [Tooltip("可选自定义第一层高频侵蚀细节贴图。")]
        public TextureParameter customDetailTexture = new TextureParameter(null);

        [Tooltip("第一层云层覆盖度（0 = 晴空万里，1 = 密布阴天）。")]
        public ClampedFloatParameter coverage = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Tooltip("第一层云层不透明度 / 密度倍率。")]
        public ClampedFloatParameter density = new ClampedFloatParameter(1.0f, 0.0f, 3.0f);

        [Tooltip("第一层光线步进体积厚度（0 = 平面薄云，数值越大 3D 团块感越深厚）。")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(15.0f, 0.0f, 50.0f);

        [Tooltip("比尔-朗伯吸收系数（控制云层底部和内部自阴影的深浅与立体感）。")]
        public ClampedFloatParameter absorption = new ClampedFloatParameter(1.5f, 0.0f, 5.0f);

        [Tooltip("第一层云层宏观平铺缩放比例。")]
        public MinFloatParameter scale = new MinFloatParameter(1.0f, 0.1f);

        [Tooltip("第一层云层海拔高度（米），用于阴影投影与视差计算。")]
        public MinFloatParameter altitude = new MinFloatParameter(2000f, 100f);

        [Header("第二层云 - 高空卷云 (Layer 2 - Cirrus)")]
        [Tooltip("是否启用第二层高空云。")]
        public BoolParameter enableLayer2 = new BoolParameter(false);

        [Tooltip("第二层基础形态预制噪声类型。")]
        public CloudNoiseTypeParameter layer2ShapeType = new CloudNoiseTypeParameter(CloudNoiseType.Perlin);

        [Tooltip("第二层边缘侵蚀高频细节噪声类型。")]
        public CloudDetailTypeParameter layer2DetailType = new CloudDetailTypeParameter(CloudDetailType.Perlin);

        [Tooltip("可选自定义第二层基础形状贴图。")]
        public TextureParameter layer2CustomBaseTexture = new TextureParameter(null);

        [Tooltip("可选自定义第二层细节侵蚀贴图。")]
        public TextureParameter layer2CustomDetailTexture = new TextureParameter(null);

        [Tooltip("第二层云层覆盖度（0 = 晴空万里，1 = 密布阴天）。")]
        public ClampedFloatParameter layer2Coverage = new ClampedFloatParameter(0.4f, 0.0f, 1.0f);

        [Tooltip("第二层云层不透明度 / 密度倍率。")]
        public ClampedFloatParameter layer2Density = new ClampedFloatParameter(0.5f, 0.0f, 2.0f);

        [Tooltip("第二层光线步进体积厚度。")]
        public ClampedFloatParameter layer2Thickness = new ClampedFloatParameter(10.0f, 0.0f, 50.0f);

        [Tooltip("第二层云层光线吸收率。")]
        public ClampedFloatParameter layer2Absorption = new ClampedFloatParameter(1.0f, 0.0f, 5.0f);

        [Tooltip("第二层云层平铺缩放比例。")]
        public MinFloatParameter layer2Scale = new MinFloatParameter(2.5f, 0.1f);

        [Tooltip("第二层云层海拔高度（米）。")]
        public MinFloatParameter layer2Altitude = new MinFloatParameter(6000f, 2000f);

        [Tooltip("第二层相对于基础风速的移动倍率（通常高空风速更快）。")]
        public ClampedFloatParameter layer2SpeedMultiplier = new ClampedFloatParameter(1.5f, 0.0f, 5.0f);

        [Header("形态细节与地平线 (Shape & Details)")]
        [Tooltip("边缘侵蚀细节强度（使用 pow4 非线性侵蚀，使云边撕裂成羽状絮絮，核心保持饱满）。")]
        public ClampedFloatParameter detailErosion = new ClampedFloatParameter(0.65f, 0.0f, 1.0f);

        [Tooltip("高频细节噪声平铺频率。")]
        public MinFloatParameter detailScale = new MinFloatParameter(3.0f, 0.5f);

        [Tooltip("地平线地球曲率视差透视压缩感。")]
        public ClampedFloatParameter curvature = new ClampedFloatParameter(0.35f, 0.0f, 1.0f);

        [Tooltip("地平线柔和消隐宽度（避免云层与地面相交过于突兀）。")]
        public ClampedFloatParameter horizonFade = new ClampedFloatParameter(0.12f, 0.01f, 0.5f);

        [Header("光照与散射 (Lighting & Scattering)")]
        [Tooltip("逆光银边强度（朝向光源时亨利-格林斯坦前向散射辉光）。")]
        public ClampedFloatParameter silverLining = new ClampedFloatParameter(2.5f, 0.0f, 10.0f);

        [Tooltip("银边光透射宽度。")]
        public ClampedFloatParameter silverLiningWidth = new ClampedFloatParameter(0.15f, 0.01f, 0.5f);

        [Tooltip("内部自阴影光线步进采样步数（推荐 4 步，数值越高内部立体阴影层次越精细）。")]
        public ClampedIntParameter lightmarchSteps = new ClampedIntParameter(4, 1, 8);

        [Tooltip("受日照面云层高光颜色。")]
        public ColorParameter cloudColor = new ColorParameter(Color.white, false, false, true);

        [Tooltip("云层背光面及底部自阴影基调颜色。")]
        public ColorParameter shadowColor = new ColorParameter(new Color(0.35f, 0.38f, 0.45f, 1f), false, false, true);

        [Header("阴影投影 (Shadows & Light Cookie)")]
        [Tooltip("是否启用 Directional Light Cookie 投影，在地面和物体上投射实时移动的云层阴影。")]
        public BoolParameter castShadows = new BoolParameter(false);

        [Tooltip("云层阴影在世界空间中的投影区域尺寸（米）。")]
        public MinFloatParameter shadowArea = new MinFloatParameter(800f, 50f);

        [Tooltip("投射在地面与物体上的阴影衰减浓度（0 = 无阴影，1 = 最深阴影）。")]
        public ClampedFloatParameter shadowStrength = new ClampedFloatParameter(0.6f, 0.0f, 1.0f);

        [Header("性能与画质 (Performance)")]
        [Tooltip("低分辨率云层渲染比例：Full(原画 1:1)、Half(半分辨率 1/2，推荐平衡模式)、Quarter(四分之一 1/4，极限性能)。")]
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
