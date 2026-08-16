using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature
{
    public class RenderingLayerDepthPrepassFeature : ScriptableRendererFeature
    {
        public enum DepthResolutionScale
        {
            Full = 1,
            Half = 2,
            Quarter = 4,
            Eighth = 8
        }

        [System.Serializable]
        public class PassSettings
        {
            public RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingPrePasses;
            public string shaderPassTag = "DepthOnly";

            [Tooltip("选择用于过滤的渲染图层（支持多选，通过位掩码过滤）。")]
            public int renderingLayerMask = 2;

            [Tooltip("深度图的分分辨率缩放选项（用于 GPU 遮挡剔除优化）。")]
            public DepthResolutionScale resolutionScale = DepthResolutionScale.Full;
        }

        public PassSettings settings = new PassSettings();
        private RenderingLayerDepthPrepass m_DepthPrepass;

        public override void Create()
        {
            m_DepthPrepass = new RenderingLayerDepthPrepass(settings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera != null && !camera.name.Contains("Reflection"))
                renderer.EnqueuePass(m_DepthPrepass);
        }

        // ----------------------------------------------------------------------------------
        // Render Graph Pass 实现
        // ----------------------------------------------------------------------------------
        private class RenderingLayerDepthPrepass : ScriptableRenderPass
        {
            private readonly PassSettings m_Settings;
            private readonly ShaderTagId m_ShaderTagId;
            private FilteringSettings m_FilteringSettings;
            private readonly int m_CustomDepthTextureShaderId;

            private class PassData
            {
                public RendererListHandle rendererList;
            }

            public RenderingLayerDepthPrepass(PassSettings settings)
            {
                m_Settings = settings;
                renderPassEvent = settings.renderPassEvent;
                m_ShaderTagId = new ShaderTagId(settings.shaderPassTag);

                m_FilteringSettings = new FilteringSettings(RenderQueueRange.opaque, -1);
                m_CustomDepthTextureShaderId = Shader.PropertyToID("_CameraDepthTexture");

                profilingSampler = new ProfilingSampler(nameof(RenderingLayerDepthPrepass));
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
                UniversalLightData lightData = frameData.Get<UniversalLightData>();

                // 1. 克隆一份相机原生的 RenderTextureDescriptor
                // 内部已自动处理 XR 对应的 dimension (Tex2DArray)、msaaSamples、vrSlices 等参数
                RenderTextureDescriptor depthDesc = cameraData.cameraTargetDescriptor;

                // 2. 修改宽高分辨率
                float scaleFactor = 1.0f / (int)m_Settings.resolutionScale;
                depthDesc.width = Mathf.Max(1, (int)(depthDesc.width * scaleFactor));
                depthDesc.height = Mathf.Max(1, (int)(depthDesc.height * scaleFactor));

                // 强制确保格式为纯深度图
                depthDesc.colorFormat = RenderTextureFormat.Depth;
                depthDesc.depthBufferBits = 24;
                depthDesc.msaaSamples = 1;

                // 3. 直接通过 RenderTextureDescriptor 构造 TextureDesc
                TextureDesc graphTextureDesc = new TextureDesc(depthDesc)
                {
                    name = "CustomDepthPrepassTexture"
                };

                TextureHandle depthTexture = renderGraph.CreateTexture(graphTextureDesc);
                // 跨 Feature 共享数据
                CustomDepthContextData customData = frameData.GetOrCreate<CustomDepthContextData>();
                customData.customDepthTexture = depthTexture;
                customData.width = depthDesc.width;
                customData.height = depthDesc.height;

                // 4. 配置绘制与过滤参数
                var sortFlags = cameraData.defaultOpaqueSortFlags;
                DrawingSettings drawingSettings = RenderingUtils.CreateDrawingSettings(m_ShaderTagId, renderingData, cameraData, lightData, sortFlags);
                drawingSettings.perObjectData = PerObjectData.None;
                m_FilteringSettings.renderingLayerMask = (uint)m_Settings.renderingLayerMask;

                // 5. 构建 Raster Render Pass
                using (var builder = renderGraph.AddRasterRenderPass<PassData>("RenderingLayerDepthPrepass", out var passData, profilingSampler))
                {
                    var param = new RendererListParams(renderingData.cullResults, drawingSettings, m_FilteringSettings);
                    passData.rendererList = renderGraph.CreateRendererList(param);
                    builder.UseRendererList(passData.rendererList);

                    builder.SetRenderAttachmentDepth(depthTexture, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(depthTexture, m_CustomDepthTextureShaderId);

                    // 6. XR 适配
                    builder.AllowGlobalStateModification(true);

                    if (cameraData.xr.enabled)
                    {
                        builder.EnableFoveatedRasterization(cameraData.xr.supportsFoveatedRendering);
                        builder.SetExtendedFeatureFlags(ExtendedFeatureFlags.MultiviewRenderRegionsCompatible);
                    }

                    // 7. 提交到 GPU 渲染队列
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.ClearRenderTarget(true, false, Color.clear, 1.0f, 0);
                        context.cmd.DrawRendererList(data.rendererList);
                    });
                }
            }
        }
    }
}
