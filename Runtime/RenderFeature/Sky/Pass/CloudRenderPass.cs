using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Sky
{
    /// <summary>
    /// Foundation RenderGraph pass for downscaled cloud generation.
    /// Provides low-resolution off-screen RTHandle creation and global binding.
    /// </summary>
    public class CloudRenderPass
    {
        private const string k_GeneratorShader = "Hidden/EAStudio/CloudGenerator";

        private Shader m_GeneratorShader;
        private Material m_GeneratorMaterial;

        private int m_DownscaleFactor = 2;
        private static readonly int s_CloudTextureID = Shader.PropertyToID("_CloudTexture");

        private readonly LowResPass m_LowResPass;
        private RenderPassEvent m_RenderPassEvent = RenderPassEvent.BeforeRenderingSkybox;

        public LowResPass LowRes => m_LowResPass;
        public RenderPassEvent RenderPassEvent => m_RenderPassEvent;
        public int DownscaleFactor => m_DownscaleFactor;

        public void UpdateRenderPassEvent(RenderPassEvent renderPassEvent)
        {
            m_RenderPassEvent = renderPassEvent;
            if (m_LowResPass != null)
                m_LowResPass.renderPassEvent = renderPassEvent;
        }

        private Shader m_GeneratorShaderOverride;

        public CloudRenderPass(Shader generatorShader = null, RenderPassEvent renderPassEvent = RenderPassEvent.BeforeRenderingSkybox)
        {
            m_GeneratorShaderOverride = generatorShader;
            m_RenderPassEvent = renderPassEvent;
            m_LowResPass = new LowResPass(this);
        }

        public void SetShaderOverride(Shader generatorShader)
        {
            if (m_GeneratorShaderOverride != generatorShader)
            {
                m_GeneratorShaderOverride = generatorShader;
                CoreUtils.Destroy(m_GeneratorMaterial);
                m_GeneratorMaterial = null;
                m_GeneratorShader = null;
            }
        }

        private bool EnsureMaterial()
        {
            if (m_GeneratorMaterial != null)
                return true;

            m_GeneratorShader = m_GeneratorShaderOverride != null
                ? m_GeneratorShaderOverride
                : Shader.Find(k_GeneratorShader);

            if (m_GeneratorShader == null)
            {
                Debug.LogError($"[CloudRenderPass] Cannot find shader '{k_GeneratorShader}'.");
                return false;
            }

            m_GeneratorMaterial = CoreUtils.CreateEngineMaterial(m_GeneratorShader);
            return m_GeneratorMaterial != null;
        }

        public void Setup(VisualEnvironment visualEnv, CloudLayer cloudLayer)
        {
            if (!EnsureMaterial() || visualEnv == null || cloudLayer == null)
                return;

            m_DownscaleFactor = Mathf.Max(1, (int)cloudLayer.downscale.value);
        }

        public class LowResPass : ScriptableRenderPass
        {
            private readonly CloudRenderPass m_Parent;

            public LowResPass(CloudRenderPass parent)
            {
                m_Parent = parent;
                renderPassEvent = m_Parent.m_RenderPassEvent;
            }

            private class PassData
            {
                public Material material;
                public TextureHandle cloudTexture;
            }

            public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
            {
                if (m_Parent.m_GeneratorMaterial == null)
                    return;

                UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
                UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
                RenderTextureDescriptor desc = cameraData.cameraTargetDescriptor;

                // Downscale resolution computation
                int factor = m_Parent.m_DownscaleFactor;
                desc.width = Mathf.Max(1, desc.width / factor);
                desc.height = Mathf.Max(1, desc.height / factor);
                desc.depthBufferBits = 0;
                desc.msaaSamples = 1;

                TextureDesc textureDesc = new TextureDesc(desc.width, desc.height)
                {
                    colorFormat = GraphicsFormat.R16G16B16A16_SFloat,
                    depthBufferBits = 0,
                    msaaSamples = MSAASamples.None,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp,
                    name = "_CloudTexture"
                };

                TextureHandle cloudTex = renderGraph.CreateTexture(textureDesc);

                SkyCloudFrameData cloudFrameData = frameData.GetOrCreate<SkyCloudFrameData>();
                cloudFrameData.cloudTexture = cloudTex;
                cloudFrameData.hasClouds = true;

                using (var builder = renderGraph.AddRasterRenderPass<PassData>("Generate Clouds (LowRes)", out var passData))
                {
                    passData.material = m_Parent.m_GeneratorMaterial;
                    passData.cloudTexture = cloudTex;

                    if (resourceData != null && resourceData.cameraDepthTexture.IsValid())
                    {
                        builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
                    }

                    builder.SetRenderAttachment(cloudTex, 0, AccessFlags.Write);
                    builder.SetGlobalTextureAfterPass(cloudTex, s_CloudTextureID);

                    builder.SetRenderFunc(static (PassData data, RasterGraphContext context) =>
                    {
                        context.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3, 1);
                    });
                }
            }
        }

        public void Dispose()
        {
            CoreUtils.Destroy(m_GeneratorMaterial);
            m_GeneratorMaterial = null;
        }
    }
}
