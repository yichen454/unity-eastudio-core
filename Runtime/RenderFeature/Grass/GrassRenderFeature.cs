using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Grass
{
    /// <summary>Global rendering resources. Grass activation belongs to the camera's Grass Volume.</summary>
    public sealed class GrassRenderFeature : ScriptableRendererFeature
    {
        [SerializeField] private ComputeShader generationShader;
        [SerializeField] private Shader bladeShader;
        [SerializeField, Range(1024, 4194304), Tooltip("Maximum candidate slots per camera, prioritized by nearby chunk.")]
        private int maximumCandidates = 262144;
        [SerializeField, Range(64, 65536), Tooltip("Maximum visible chunks submitted per camera, prioritized by distance.")]
        private int maximumChunks = 8192;
        [SerializeField, Range(4f, 64f)] private float chunkSize = 16f;

        private GrassRenderer grassRenderer;
        private GrassPass generatePass;
        private GrassPass drawPass;
        private bool diagnosticIssued;

        public override void Create()
        {
            grassRenderer?.Dispose();
            grassRenderer = null;
            generatePass = null;
            drawPass = null;
            diagnosticIssued = false;
            if (generationShader == null) generationShader = Resources.Load<ComputeShader>("EAStudio/Grass/GrassGenerate");
            if (bladeShader == null) bladeShader = Resources.Load<Shader>("EAStudio/Grass/GrassBlade");
            if (generationShader != null && generationShader.HasKernel("ResetArguments") && generationShader.HasKernel("Generate")
                && bladeShader != null && bladeShader.isSupported
                && SystemInfo.supportsComputeShaders && SystemInfo.supportsIndirectArgumentsBuffer)
            {
                grassRenderer = new GrassRenderer(generationShader, bladeShader);
                generatePass = new GrassPass(this, true);
                drawPass = new GrassPass(this, false);
            }
        }

        /// <summary>Refresh terrain discovery, layer mappings and bounds after external terrain edits.</summary>
        public void InvalidateTerrainCache() => grassRenderer?.InvalidateTerrainCache();

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            Camera camera = renderingData.cameraData.camera;
            if (camera == null || camera.cameraType == CameraType.Preview) return;
            Grass grass = VolumeManager.instance.stack?.GetComponent<Grass>();
            if (grass == null || !grass.IsActive()) return;
            if (generatePass == null || drawPass == null)
            {
                if (!diagnosticIssued)
                {
                    Debug.LogWarning("GrassRenderFeature requires valid ResetArguments/Generate compute kernels, a supported blade shader, and compute + indirect drawing support.", this);
                    diagnosticIssued = true;
                }
                return;
            }
            renderer.EnqueuePass(generatePass);
            renderer.EnqueuePass(drawPass);
        }

        protected override void Dispose(bool disposing)
        {
            grassRenderer?.Dispose();
            grassRenderer = null;
            generatePass = null;
            drawPass = null;
        }

        private sealed class GrassPass : ScriptableRenderPass
        {
            private readonly GrassRenderFeature owner;
            private readonly bool generate;

            internal GrassPass(GrassRenderFeature owner, bool generate)
            {
                this.owner = owner;
                this.generate = generate;
                renderPassEvent = generate ? RenderPassEvent.BeforeRenderingOpaques : RenderPassEvent.AfterRenderingOpaques;
            }

            public override void RecordRenderGraph(UnityEngine.Rendering.RenderGraphModule.RenderGraph renderGraph, ContextContainer frameData)
            {
                if (owner.grassRenderer == null) return;
                if (!generate)
                {
                    owner.grassRenderer.RecordDraw(renderGraph, frameData);
                    return;
                }
                var cameraData = frameData.Get<UniversalCameraData>();
                Grass grass = VolumeManager.instance.stack?.GetComponent<Grass>();
                if (grass == null || !grass.IsActive()) return;
                owner.grassRenderer.RecordGenerate(renderGraph, frameData, cameraData, new GrassSettings(grass),
                    Mathf.Clamp(owner.maximumCandidates, 1024, 4194304), Mathf.Clamp(owner.maximumChunks, 64, 65536),
                    Mathf.Clamp(owner.chunkSize, 4f, 64f));
            }
        }
    }
}
