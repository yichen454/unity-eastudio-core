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
        [Header("形态与覆盖 (Shape & Coverage)")]
        [Tooltip("是否启用体积云渲染。")]
        public BoolParameter enableClouds = new BoolParameter(true);

        [Tooltip("云层基础形态预制噪声：Worley(积云)、Billow(浓积云)、Perlin(卷云)、Stratocumulus(层积云)。")]
        public CloudNoiseTypeParameter shapeType = new CloudNoiseTypeParameter(CloudNoiseType.Worley);

        [Tooltip("边缘侵蚀细节噪声类型。")]
        public CloudDetailTypeParameter detailType = new CloudDetailTypeParameter(CloudDetailType.Worley);

        [Tooltip("可选自定义基础形状贴图。")]
        public TextureParameter customBaseTexture = new TextureParameter(null);

        [Tooltip("可选自定义高频细节贴图。")]
        public TextureParameter customDetailTexture = new TextureParameter(null);

        [Tooltip("全局云层覆盖度（0 = 晴空万里，1 = 密布阴天）。")]
        public ClampedFloatParameter coverage = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Tooltip("云层物理密度倍率（控制云体实体浓度与遮光能力）。")]
        public ClampedFloatParameter density = new ClampedFloatParameter(1.0f, 0.1f, 5.0f);

        [Tooltip("云层宏观世界平铺缩放。")]
        public MinFloatParameter scale = new MinFloatParameter(1.0f, 0.05f);

        [Tooltip("云底起始海拔高度（米）。")]
        public MinFloatParameter altitude = new MinFloatParameter(2000f, 100f);

        [Tooltip("云体物理垂直厚度（米，控制从云底到云顶的真实物理厚度，数值越大立体团块感越深厚）。")]
        public ClampedFloatParameter thickness = new ClampedFloatParameter(2500f, 100f, 8000f);

        [Tooltip("云型插值（0 = Stratus 扁平层云，1 = Cumulus 蓬松积云塔）。")]
        public ClampedFloatParameter cloudType = new ClampedFloatParameter(0.85f, 0.0f, 1.0f);

        [Tooltip("高频细节侵蚀强度（数值越大边缘撕裂絮状感越明显）。")]
        public ClampedFloatParameter detailErosion = new ClampedFloatParameter(0.45f, 0.0f, 1.0f);

        [Tooltip("高频侵蚀细节缩放倍率。")]
        public ClampedFloatParameter detailScale = new ClampedFloatParameter(4.0f, 1.0f, 16.0f);

        [Tooltip("云底平整圆润度（数值越大底部过渡越平实自然）。")]
        public ClampedFloatParameter bottomRoundness = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Tooltip("云顶柔软消散度（数值越大云顶过渡越蓬松散漫）。")]
        public ClampedFloatParameter topSoftness = new ClampedFloatParameter(0.3f, 0.05f, 0.8f);

        [Header("光照与多次散射 (Lighting & Scattering)")]
        [Tooltip("太阳/月亮直射光照强度倍率。")]
        public ClampedFloatParameter sunLightIntensity = new ClampedFloatParameter(1.0f, 0.0f, 3.0f);

        [Tooltip("比尔-朗伯光线消光吸收系数（控制云体内部光线吸收速率与自阴影深浅，推荐 0.5 ~ 1.5）。")]
        public ClampedFloatParameter absorption = new ClampedFloatParameter(0.6f, 0.05f, 3.0f);

        [Tooltip("立体自阴影对比度强化（0 = 柔和漫反射，1 = 标准物理阴影，2~3 = 极具戏剧性的立体凹凸背光阴影）。")]
        public ClampedFloatParameter selfShadowStrength = new ClampedFloatParameter(1.0f, 0.0f, 3.0f);

        [Tooltip("糖粉散射效应（Powder/Sugar Effect，照亮极薄微水滴边缘，避免云体外缘发暗）。")]
        public ClampedFloatParameter powderEffect = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Tooltip("银边前向散射强度（逆光看太阳时边缘强烈的金/白银边）。")]
        public ClampedFloatParameter silverLiningIntensity = new ClampedFloatParameter(1.42f, 0.0f, 3.0f);

        [Tooltip("银边角向扩散范围（数值越小边缘锐利，越大扩散范围越宽）。")]
        public ClampedFloatParameter silverLiningSpread = new ClampedFloatParameter(0.5f, 0.05f, 0.8f);

        [Tooltip("背光透射强化（背向光源时的边缘光晕增强）。")]
        public ClampedFloatParameter backlitStrength = new ClampedFloatParameter(0.5f, 0.0f, 1.5f);

        [Tooltip("多重散射强度（模拟光子在云体内无数次折射产生的通透温暖内发光，根除阴影区死黑）。")]
        public ClampedFloatParameter multiScattering = new ClampedFloatParameter(0.55f, 0.0f, 2.0f);

        [Tooltip("多重散射随厚度衰减速度。")]
        public ClampedFloatParameter multiScatterFalloff = new ClampedFloatParameter(0.20f, 0.05f, 2.0f);

        [Tooltip("环境底光保底强度（模拟深层厚云内部捕获的环境天光，防止浓云背光面死黑）。")]
        public ClampedFloatParameter ambientFloor = new ClampedFloatParameter(0.20f, 0.0f, 1.0f);

        [Tooltip("云层背光面及底部自阴影基调颜色。")]
        public ColorParameter shadowColor = new ColorParameter(new Color(0.42f, 0.45f, 0.52f, 1f), false, false, true);

        [Tooltip("云层固有主色调。")]
        public ColorParameter cloudColor = new ColorParameter(Color.white, false, false, true);

        [Header("光线步进与性能 (Raymarching & Performance)")]
        [Tooltip("天顶视线最小步数（仰视垂直穿透云层时，光程短，使用低步数节能）。")]
        public ClampedIntParameter minSteps = new ClampedIntParameter(32, 8, 96);

        [Tooltip("平视地平线最大步数（平视/斜穿大厚度云层时，光程长，使用高步数保证层次连贯细腻）。")]
        public ClampedIntParameter maxSteps = new ClampedIntParameter(64, 16, 128);

        [Tooltip("光向自阴影采样步数（推荐 4~6 步）。")]
        public ClampedIntParameter lightmarchSteps = new ClampedIntParameter(5, 1, 8);

        [Tooltip("启用空旷空间粗细跳跃加速（无云区域双倍步长跳过，大幅节约 GPU 算力）。")]
        public BoolParameter enableEmptySpaceSkipping = new BoolParameter(true);

        [Tooltip("启用场景深度穿插阻挡（当远山或高空建筑物穿入云层时，在表面截断步进，呈现正确的空间穿插）。")]
        public BoolParameter enableDepthBlending = new BoolParameter(true);

        [Tooltip("地平线边缘淡出范围（控制云层在地平线附近融入远雾的柔和过渡宽度，数值越大地平线过渡越平缓宽阔）。")]
        public ClampedFloatParameter horizonFade = new ClampedFloatParameter(0.08f, 0.005f, 0.35f);

        [Tooltip("地平线淡出起始仰角偏移（控制云层开始消隐的地平线垂直高度，负值代表允许穿透至地平线以下）。")]
        public ClampedFloatParameter horizonFadeStart = new ClampedFloatParameter(-0.01f, -0.05f, 0.1f);

        [Tooltip("低分辨率云层渲染比例：Full(原画 1:1)、Half(半分辨率 1/2，推荐平衡模式)、Quarter(四分之一 1/4，极限性能)。")]
        public CloudDownscaleParameter downscale = new CloudDownscaleParameter(CloudDownscale.Half);

        [Header("阴影投影 (Shadows & Light Cookie)")]
        [Tooltip("是否启用 Directional Light Cookie 投影，在地面和物体上投射实时移动的云层阴影。")]
        public BoolParameter castShadows = new BoolParameter(false);

        [Tooltip("云层阴影在世界空间中的投影区域尺寸（米）。")]
        public MinFloatParameter shadowArea = new MinFloatParameter(800f, 50f);

        [Tooltip("投射在地面与物体上的阴影衰减浓度（0 = 无阴影，1 = 最深阴影）。")]
        public ClampedFloatParameter shadowStrength = new ClampedFloatParameter(0.6f, 0.0f, 1.0f);

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + enableClouds.value.GetHashCode();
                hash = hash * 31 + shapeType.value.GetHashCode();
                hash = hash * 31 + detailType.value.GetHashCode();
                hash = hash * 31 + customBaseTexture.value.GetHashCode();
                hash = hash * 31 + customDetailTexture.value.GetHashCode();
                hash = hash * 31 + coverage.value.GetHashCode();
                hash = hash * 31 + density.value.GetHashCode();
                hash = hash * 31 + scale.value.GetHashCode();
                hash = hash * 31 + altitude.value.GetHashCode();
                hash = hash * 31 + thickness.value.GetHashCode();
                hash = hash * 31 + cloudType.value.GetHashCode();
                hash = hash * 31 + detailErosion.value.GetHashCode();
                hash = hash * 31 + detailScale.value.GetHashCode();
                hash = hash * 31 + bottomRoundness.value.GetHashCode();
                hash = hash * 31 + topSoftness.value.GetHashCode();

                hash = hash * 31 + sunLightIntensity.value.GetHashCode();
                hash = hash * 31 + absorption.value.GetHashCode();
                hash = hash * 31 + selfShadowStrength.value.GetHashCode();
                hash = hash * 31 + powderEffect.value.GetHashCode();
                hash = hash * 31 + silverLiningIntensity.value.GetHashCode();
                hash = hash * 31 + silverLiningSpread.value.GetHashCode();
                hash = hash * 31 + backlitStrength.value.GetHashCode();
                hash = hash * 31 + multiScattering.value.GetHashCode();
                hash = hash * 31 + multiScatterFalloff.value.GetHashCode();
                hash = hash * 31 + ambientFloor.value.GetHashCode();
                hash = hash * 31 + shadowColor.value.GetHashCode();
                hash = hash * 31 + cloudColor.value.GetHashCode();

                hash = hash * 31 + minSteps.value.GetHashCode();
                hash = hash * 31 + maxSteps.value.GetHashCode();
                hash = hash * 31 + lightmarchSteps.value.GetHashCode();
                hash = hash * 31 + enableEmptySpaceSkipping.value.GetHashCode();
                hash = hash * 31 + enableDepthBlending.value.GetHashCode();
                hash = hash * 31 + horizonFade.value.GetHashCode();
                hash = hash * 31 + horizonFadeStart.value.GetHashCode();
                hash = hash * 31 + downscale.value.GetHashCode();

                hash = hash * 31 + castShadows.value.GetHashCode();
                hash = hash * 31 + shadowArea.value.GetHashCode();
                hash = hash * 31 + shadowStrength.value.GetHashCode();
                return hash;
            }
        }
    }
}
