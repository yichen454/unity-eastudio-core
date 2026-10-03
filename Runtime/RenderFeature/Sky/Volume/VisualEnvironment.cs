using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Sky
{
    public enum SkyType
    {
        None = 0,
        HDRI = 1,
        Procedural = 2
    }

    public enum SkyAmbientMode
    {
        Realtime = 0,
        OnChanged = 1,
        Off = 2
    }

    public enum CloudType
    {
        None = 0,
        CloudLayer = 1,
        Volumetric = 2
    }

    [Serializable]
    public sealed class SkyTypeParameter : VolumeParameter<SkyType>
    {
        public SkyTypeParameter(SkyType value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class SkyAmbientModeParameter : VolumeParameter<SkyAmbientMode>
    {
        public SkyAmbientModeParameter(SkyAmbientMode value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable]
    public sealed class CloudTypeParameter : VolumeParameter<CloudType>
    {
        public CloudTypeParameter(CloudType value, bool overrideState = false) : base(value, overrideState) { }
    }

    [Serializable, VolumeComponentMenu("EAStudio/Sky/Visual Environment")]
    public class VisualEnvironment : VolumeComponent
    {
        [Tooltip("天空渲染类型：None(无)、HDRI(全景贴图)、Procedural(物理大气程序化天空)。")]
        public SkyTypeParameter skyType = new SkyTypeParameter(SkyType.HDRI);

        [Tooltip("环境光探针 (SH 球谐) 评估模式：Realtime(实时每帧同步)、OnChanged(参数变化时更新)、Off(关闭)。")]
        public SkyAmbientModeParameter skyAmbientMode = new SkyAmbientModeParameter(SkyAmbientMode.Realtime);

        [Header("环境光照 (Environment Lighting)")]
        [Tooltip("环境光照（漫反射球谐 / Ambient SH）线性强度倍率，对标 Lighting 窗口中的 Environment Lighting -> Intensity Multiplier。")]
        public MinFloatParameter lightingMultiplier = new MinFloatParameter(1f, 0f);

        [Header("云层系统 (Clouds)")]
        [Tooltip("云层渲染技术：None(关闭)、CloudLayer(分层云 / Cloud Layer)。")]
        public CloudTypeParameter cloudType = new CloudTypeParameter(CloudType.None);
    }
}
