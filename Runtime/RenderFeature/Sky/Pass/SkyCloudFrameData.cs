using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Frame-persistent RenderGraph ContextItem to pass cloud resources across passes
    /// (e.g. from LowResPass to CompositePass, Volumetric Light, or Cloud Shadows).
    /// </summary>
    public class SkyCloudFrameData : ContextItem
    {
        public TextureHandle cloudTexture;
        public bool hasClouds;

        public override void Reset()
        {
            cloudTexture = TextureHandle.nullHandle;
            hasClouds = false;
        }
    }
}
