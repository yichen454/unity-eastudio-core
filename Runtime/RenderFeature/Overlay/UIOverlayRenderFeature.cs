using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;
using System.Collections.Generic;

public class UIOverlayRenderFeature : ScriptableRendererFeature
{
    class UIOverlayPass : ScriptableRenderPass
    {
        private LayerMask m_LayerMask;

        private List<ShaderTagId> m_ShaderTagIdList = new List<ShaderTagId>();

        public UIOverlayPass(LayerMask layerMask)
        {
            m_LayerMask = layerMask;
        }

        private class PassData
        {
            public RendererListHandle rendererListHandle;
        }

        private void InitRendererLists(ContextContainer frameData, ref PassData passData, RenderGraph renderGraph, CullingResults cullingResults)
        {
            // Access the relevant frame data from the Universal Render Pipeline.
            UniversalRenderingData universalRenderingData = frameData.Get<UniversalRenderingData>();
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();

            var sortFlags = cameraData.defaultOpaqueSortFlags;
            RenderQueueRange renderQueueRange = RenderQueueRange.transparent;
            FilteringSettings filterSettings = new FilteringSettings(renderQueueRange, m_LayerMask);

            ShaderTagId[] forwardOnlyShaderTagIds = new ShaderTagId[]
            {
                new ShaderTagId("SRPDefaultUnlit"), // Legacy shaders (do not have a gbuffer pass) are considered forward-only for backward compatibility.
            };

            m_ShaderTagIdList.Clear();

            foreach (ShaderTagId sid in forwardOnlyShaderTagIds)
                m_ShaderTagIdList.Add(sid);

            DrawingSettings drawSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagIdList, universalRenderingData, cameraData, lightData, sortFlags);

            var param = new RendererListParams(cullingResults, drawSettings, filterSettings);

            passData.rendererListHandle = renderGraph.CreateRendererList(param);
        }

        static void ExecutePass(PassData data, RasterGraphContext context)
        {
            context.cmd.DrawRendererList(data.rendererListHandle);
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            string passName = "UIOverlay Render Pass";
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            CullContextData cullContextData = frameData.Get<CullContextData>();

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out var passData))
            {
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();

                if (!cameraData.camera.TryGetCullingParameters(out var cullingParameters))
                    return;
                cullingParameters.cullingMask = (uint)m_LayerMask.value;
                CullingResults cullingResults = cullContextData.Cull(ref cullingParameters);

                InitRendererLists(frameData, ref passData, renderGraph, cullingResults);

                if (!passData.rendererListHandle.IsValid())
                    return;

                builder.UseRendererList(passData.rendererListHandle);

                builder.SetRenderAttachment(resourceData.activeColorTexture, 0);

                builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => ExecutePass(data, context));
            }
        }
    }

    UIOverlayPass m_ScriptablePass;
    public LayerMask m_LayerMask;

    public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;


    /// <inheritdoc/>
    public override void Create()
    {
        m_ScriptablePass = new UIOverlayPass(m_LayerMask)
        {
            renderPassEvent = renderPassEvent,
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        Camera camera = renderingData.cameraData.camera;
        if (camera != null && !camera.name.Contains("Reflection"))
            renderer.EnqueuePass(m_ScriptablePass);
    }
}
