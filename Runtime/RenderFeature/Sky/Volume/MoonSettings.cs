using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public enum MoonPhaseMode
    {
        Automatic = 0, // Automatically calculated based on Sun-Moon angular relationship
        Manual = 1     // Manually controlled via lunarPhase parameter
    }

    [Serializable]
    public sealed class MoonPhaseModeParameter : VolumeParameter<MoonPhaseMode>
    {
        public MoonPhaseModeParameter(MoonPhaseMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Sky/Moon Settings")]
    public class MoonSettings : VolumeComponent
    {
        [Header("月球渲染 (Moon Rendering)")]
        [Tooltip("是否在天空中渲染月亮。")]
        public BoolParameter enableMoon = new BoolParameter(true);

        [Tooltip("月亮视直径大小。")]
        public ClampedFloatParameter moonSize = new ClampedFloatParameter(0.06f, 0.01f, 0.25f);

        [Tooltip("月亮视觉表面亮度倍率。")]
        public MinFloatParameter moonBrightness = new MinFloatParameter(1.2f, 0.0f);

        [Tooltip("月球本体与反射月光的着色基调。")]
        public ColorParameter moonColor = new ColorParameter(new Color(0.92f, 0.95f, 1.0f, 1.0f), false, false, true);

        [Header("月相系统 (Lunar Phase)")]
        [Tooltip("月相计算模式：Automatic(根据太阳-月球天文夹角物理推算) 或 Manual(手动调节月相)。")]
        public MoonPhaseModeParameter phaseMode = new MoonPhaseModeParameter(MoonPhaseMode.Automatic);

        [Tooltip("手动月相调节：0.0/1.0 = 新月(朔月)，0.25 = 上弦月，0.5 = 满月(望月)，0.75 = 下弦月。")]
        public ClampedFloatParameter lunarPhase = new ClampedFloatParameter(0.5f, 0.0f, 1.0f);

        [Tooltip("地照光强度：月球背光阴影面受地球反射阳光照射产生的微弱暗部辉光。")]
        public ClampedFloatParameter earthshine = new ClampedFloatParameter(0.04f, 0.0f, 0.2f);

        [Header("月冕与月晕 (Moon Corona & Halo)")]
        [Tooltip("月亮周围大气前向米氏散射产生的月晕光圈辉光强度。")]
        public ClampedFloatParameter haloIntensity = new ClampedFloatParameter(0.5f, 0.0f, 2.0f);

        [Header("月面贴图 (Moon Texture)")]
        [Tooltip("可选自定义月球表面 2D 贴图。未指定时默认使用内置 NASA 高清月面贴图。")]
        public TextureParameter customMoonTexture = new TextureParameter(null);

        [Header("云层月光照度 (Cloud Moonlight)")]
        [Tooltip("夜间月光穿透并照亮云层的散射强度系数。")]
        public ClampedFloatParameter cloudMoonlightIntensity = new ClampedFloatParameter(0.7f, 0.0f, 3.0f);

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + enableMoon.value.GetHashCode();
                hash = hash * 31 + moonSize.value.GetHashCode();
                hash = hash * 31 + moonBrightness.value.GetHashCode();
                hash = hash * 31 + moonColor.value.GetHashCode();
                hash = hash * 31 + ((int)phaseMode.value).GetHashCode();
                hash = hash * 31 + lunarPhase.value.GetHashCode();
                hash = hash * 31 + earthshine.value.GetHashCode();
                hash = hash * 31 + haloIntensity.value.GetHashCode();
                hash = hash * 31 + (customMoonTexture.value != null ? customMoonTexture.value.GetInstanceID() : 0);
                hash = hash * 31 + cloudMoonlightIntensity.value.GetHashCode();
                return hash;
            }
        }
    }
}
