using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    [Serializable]
    public sealed class CubemapParameter : VolumeParameter<Cubemap>
    {
        public CubemapParameter(Cubemap value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("Sky/HDRI Sky")]
    public class HDRISky : SkySettings
    {
        [Tooltip("HDRI 立方体贴图 (Cubemap)。")]
        public CubemapParameter hdriSky = new CubemapParameter(null);

        [Tooltip("天空盒绕 Y 轴的水平旋转角度（0-360度）。")]
        public ClampedFloatParameter rotation = new ClampedFloatParameter(0f, 0f, 360f);

        [Tooltip("天空盒颜色色调叠加乘数。默认值 #808080（对齐 Unity 原生 Skybox 标准灰）。")]
        public ColorParameter tint = new ColorParameter(new Color(0.5f, 0.5f, 0.5f, 1f), false, false, true);

        public override int GetParameterHashCode()
        {
            unchecked
            {
                int hash = base.GetParameterHashCode();
                hash = hash * 31 + (hdriSky.value != null ? hdriSky.value.GetInstanceID() : 0);
                hash = hash * 31 + rotation.value.GetHashCode();
                hash = hash * 31 + tint.value.GetHashCode();
                return hash;
            }
        }
    }
}
