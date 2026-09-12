using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    public class SkyRenderPass : ScriptableRenderPass
    {
        private class PassData
        {
            public Material material;
        }

        private Material m_Material;

        public SkyRenderPass()
        {
            profilingSampler = new ProfilingSampler("Volume HDRISky");
            renderPassEvent = RenderPassEvent.BeforeRenderingSkybox;
        }

        public void Setup(Material material)
        {
            m_Material = material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            if (m_Material == null)
                return;

            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

            if (!resourceData.activeColorTexture.IsValid())
                return;

            using (var builder = renderGraph.AddRasterRenderPass<PassData>("Volume Sky Pass", out var passData, profilingSampler))
            {
                passData.material = m_Material;

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0, AccessFlags.Write);

                if (resourceData.activeDepthTexture.IsValid())
                {
                    builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Read);
                }

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                {
                    context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
                });
            }
        }
    }
}
