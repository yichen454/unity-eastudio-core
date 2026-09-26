using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Shadow
{
    /// <summary>
    /// Volume component that overrides directional light cascade shadows and distance settings based on Volume blending.
    /// All distance and split parameters are configured in meters for artist-friendly authoring.
    /// </summary>
    [Serializable, VolumeComponentMenu("EAStudio/Shadow Settings")]
    [SupportedOnRenderPipeline(typeof(UniversalRenderPipelineAsset))]
    public class ShadowSettings : VolumeComponent, IPostProcessComponent
    {
        [Header("阴影距离 (Shadow Distance)")]
        [Tooltip("主光源与附加光源最大可见阴影距离 (米)。")]
        public MinFloatParameter shadowDistance = new MinFloatParameter(50f, 0f);

        [Header("级联设置 (Cascade Distances in Meters)")]
        [Tooltip("主光源级联阴影数量 (1、2、3 或 4)。")]
        public ClampedIntParameter cascadeCount = new ClampedIntParameter(4, 1, 4);

        [Tooltip("2 级联时的第 1 级距离 (米)。第 2 级自动延伸至最大阴影距离。")]
        public MinFloatParameter cascade2Distance = new MinFloatParameter(15f, 0f);

        [Tooltip("3 级联时的各级距离 (米，X: 第 1 级，Y: 第 2 级)。第 3 级自动延伸至最大阴影距离。")]
        public Vector2Parameter cascade3Distances = new Vector2Parameter(new Vector2(10f, 25f));

        [Tooltip("4 级联时的各级距离 (米，X: 第 1 级，Y: 第 2 级，Z: 第 3 级)。第 4 级自动延伸至最大阴影距离。")]
        public Vector3Parameter cascade4Distances = new Vector3Parameter(new Vector3(6f, 18f, 35f));

        [Tooltip("最后一级级联末端的边缘淡出过渡距离 (米)。")]
        public MinFloatParameter cascadeBorderDistance = new MinFloatParameter(5f, 0f);

        [Header("阴影偏移 (Shadow Bias)")]
        [Tooltip("阴影深度偏移 (Depth Bias)，控制沿光线方向偏移以消除阴影自遮挡斑纹 (Shadow Acne)。")]
        public ClampedFloatParameter shadowDepthBias = new ClampedFloatParameter(1.0f, 0f, 10f);

        [Tooltip("阴影法线偏移 (Normal Bias)，控制沿法线收缩表面以消除走样并避免暗部漏光。")]
        public ClampedFloatParameter shadowNormalBias = new ClampedFloatParameter(1.0f, 0f, 10f);

        /// <summary>
        /// Indicates whether this component has any active overridden parameter.
        /// </summary>
        public bool IsActive()
        {
            return (shadowDistance != null && shadowDistance.overrideState)
                || (cascadeCount != null && cascadeCount.overrideState)
                || (cascade2Distance != null && cascade2Distance.overrideState)
                || (cascade3Distances != null && cascade3Distances.overrideState)
                || (cascade4Distances != null && cascade4Distances.overrideState)
                || (cascadeBorderDistance != null && cascadeBorderDistance.overrideState)
                || (shadowDepthBias != null && shadowDepthBias.overrideState)
                || (shadowNormalBias != null && shadowNormalBias.overrideState);
        }

        [Obsolete("Unused in Unity 6+ but required for IPostProcessComponent backward compatibility.", false)]
        public bool IsTileCompatible() => false;

        public virtual int GetParameterHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (shadowDistance != null ? shadowDistance.value.GetHashCode() : 0);
                hash = hash * 31 + (cascadeCount != null ? cascadeCount.value.GetHashCode() : 0);
                hash = hash * 31 + (cascade2Distance != null ? cascade2Distance.value.GetHashCode() : 0);
                hash = hash * 31 + (cascade3Distances != null ? cascade3Distances.value.GetHashCode() : 0);
                hash = hash * 31 + (cascade4Distances != null ? cascade4Distances.value.GetHashCode() : 0);
                hash = hash * 31 + (cascadeBorderDistance != null ? cascadeBorderDistance.value.GetHashCode() : 0);
                hash = hash * 31 + (shadowDepthBias != null ? shadowDepthBias.value.GetHashCode() : 0);
                hash = hash * 31 + (shadowNormalBias != null ? shadowNormalBias.value.GetHashCode() : 0);
                return hash;
            }
        }
    }
}
