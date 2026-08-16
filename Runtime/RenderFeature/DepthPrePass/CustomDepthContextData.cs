using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
namespace EA.RenderFeature.DepthPrePass
{
    public class CustomDepthContextData : ContextItem
    {
        public TextureHandle customDepthTexture = TextureHandle.nullHandle;
        public int width = 1;
        public int height = 1;

        public override void Reset()
        {
            customDepthTexture = TextureHandle.nullHandle;
            width = 1;
            height = 1;
        }
    }
}