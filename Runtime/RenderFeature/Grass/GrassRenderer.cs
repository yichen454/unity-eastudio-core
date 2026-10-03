using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace EAStudio.Core.RenderFeature.Grass
{
    internal sealed class GrassRenderer : IDisposable
    {
        private sealed class DrawChunk
        {
            internal GrassTerrainCache.Surface Surface;
            internal Vector4 Grid;
            internal int Count, Total, Offset;
            internal TextureHandle Height, Weight, Holes;
        }

        private sealed class GenerateData
        {
            internal ComputeShader Shader;
            internal int ResetKernel, GenerateKernel, ChunkCount, Capacity, IndexCount;
            internal BufferHandle Blades, Arguments;
            internal DrawChunk[] Chunks;
            internal GrassSettings Settings;
            internal Vector3 CameraPosition;
            internal Vector4[] Planes;
            internal int EyeCount;
        }

        private sealed class DrawData
        {
            internal Material Material;
            internal GraphicsBuffer Indices;
            internal BufferHandle IndexHandle, Blades, Arguments;
            internal DrawChunk[] Chunks;
            internal MaterialPropertyBlock Properties;
        }

        // Shared only within the current camera's ContextContainer, never across cameras.
        private sealed class GrassFrameData : ContextItem
        {
            internal DrawData Draw;
            internal GrassSettings Settings;
            public override void Reset()
            {
                Draw = null;
                Settings = null;
            }
        }

        private readonly ComputeShader compute;
        private readonly Material material;
        private readonly int resetKernel, generateKernel;
        private readonly GrassTerrainCache terrains;
        private readonly List<DrawChunk> workingChunks = new List<DrawChunk>();
        private readonly GraphicsBuffer[] indexBuffers = new GraphicsBuffer[7];
        private readonly ProfilingSampler generateSampler = new ProfilingSampler("Grass Generate");
        private readonly ProfilingSampler drawSampler = new ProfilingSampler("Grass Draw");

        internal GrassRenderer(ComputeShader compute, Shader shader)
        {
            this.compute = compute;
            resetKernel = compute.FindKernel("ResetArguments");
            generateKernel = compute.FindKernel("Generate");
            material = CoreUtils.CreateEngineMaterial(shader);
            material.enableInstancing = true;
            terrains = new GrassTerrainCache();
        }

        internal void InvalidateTerrainCache() => terrains.Invalidate();

        internal void RecordGenerate(RenderGraph graph, ContextContainer frameData, UniversalCameraData cameraData,
            GrassSettings settings, int capacity, int chunkLimit, float chunkSize)
        {
            var frame = frameData.GetOrCreate<GrassFrameData>();
            frame.Reset();
            PruneEffectHandles();
            Camera camera = cameraData.camera;
            Vector3 center = camera.transform.position;
            Plane[] left, right = null;
            if (cameraData.xr != null && cameraData.xr.enabled && cameraData.xr.singlePassEnabled)
            {
                left = GeometryUtility.CalculateFrustumPlanes(cameraData.GetProjectionMatrix(0) * cameraData.GetViewMatrix(0));
                right = GeometryUtility.CalculateFrustumPlanes(cameraData.GetProjectionMatrix(1) * cameraData.GetViewMatrix(1));
                center = (cameraData.GetViewMatrix(0).inverse.GetColumn(3) + cameraData.GetViewMatrix(1).inverse.GetColumn(3)) * 0.5f;
            }
            else if (camera.stereoEnabled)
            {
                // Multi Pass still filters against both eyes and uses a common center for thinning.
                Matrix4x4 lv = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
                Matrix4x4 rv = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
                left = GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left) * lv);
                right = GeometryUtility.CalculateFrustumPlanes(camera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right) * rv);
                center = (lv.inverse.GetColumn(3) + rv.inverse.GetColumn(3)) * 0.5f;
            }
            else
                left = GeometryUtility.CalculateFrustumPlanes(cameraData.GetProjectionMatrix() * cameraData.GetViewMatrix());

            List<GrassTerrainCache.Chunk> visible = terrains.Collect(settings, center, camera.cullingMask, left, right, chunkSize, chunkLimit);
            var chunks = workingChunks;
            chunks.Clear();
            int reserved = 0;
            float spacing = 1f / Mathf.Sqrt(settings.Density);
            foreach (var chunk in visible)
            {
                Vector3 localMin = chunk.Bounds.min - chunk.Surface.Position;
                Vector3 localMax = chunk.Bounds.max - chunk.Surface.Position;
                int x = Mathf.CeilToInt(localMin.x / spacing);
                int z = Mathf.CeilToInt(localMin.z / spacing);
                int columns = Mathf.CeilToInt(localMax.x / spacing) - x;
                int rows = Mathf.CeilToInt(localMax.z / spacing) - z;
                int count = (int)Math.Min((long)columns * rows, capacity - reserved);
                if (count <= 0) continue;
                chunks.Add(new DrawChunk { Surface = chunk.Surface, Grid = new Vector4(x, z, columns, spacing), Count = count, Total = columns * rows, Offset = reserved });
                reserved += count;
                if (reserved >= capacity) break;
            }
            if (chunks.Count == 0) return;

            int level = Mathf.Clamp(settings.Level, 1, 7);
            int indexCount = (level * 2 - 1) * 3;
            GraphicsBuffer indices = GetIndexBuffer(level);
            // Graph-owned buffers are isolated per camera/recording and reused by the graph pool.
            BufferHandle blades = graph.CreateBuffer(new BufferDesc(reserved, 48) { name = "Grass Blades" });
            BufferHandle arguments = graph.CreateBuffer(new BufferDesc(chunks.Count * 5, sizeof(uint))
            { name = "Grass Indexed Arguments", target = GraphicsBuffer.Target.Structured | GraphicsBuffer.Target.IndirectArguments });
            BufferHandle indexHandle = graph.ImportBuffer(indices);
            var planes = new Vector4[12];
            for (int i = 0; i < 6; ++i)
            {
                planes[i] = new Vector4(left[i].normal.x, left[i].normal.y, left[i].normal.z, left[i].distance);
                Plane p = right != null ? right[i] : left[i];
                planes[i + 6] = new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance);
            }
            DrawChunk[] snapshot = chunks.ToArray();
            using (var builder = graph.AddComputePass<GenerateData>("Grass Generate", out var data, generateSampler))
            {
                data.Shader = compute;
                data.ResetKernel = resetKernel;
                data.GenerateKernel = generateKernel;
                data.Blades = blades;
                data.Arguments = arguments;
                data.Chunks = snapshot;
                data.ChunkCount = snapshot.Length;
                data.Capacity = reserved;
                data.IndexCount = indexCount;
                data.Settings = settings;
                data.CameraPosition = center;
                data.Planes = planes;
                data.EyeCount = right == null ? 1 : 2;
                builder.UseBuffer(blades, AccessFlags.Write);
                builder.UseBuffer(arguments, AccessFlags.ReadWrite);
                foreach (DrawChunk chunk in snapshot)
                {
                    chunk.Height = graph.ImportTexture(chunk.Surface.Height);
                    chunk.Weight = graph.ImportTexture(chunk.Surface.Weight);
                    chunk.Holes = graph.ImportTexture(chunk.Surface.Holes);
                    builder.UseTexture(chunk.Height, AccessFlags.Read);
                    builder.UseTexture(chunk.Weight, AccessFlags.Read);
                    builder.UseTexture(chunk.Holes, AccessFlags.Read);
                }
                builder.SetRenderFunc((GenerateData d, ComputeGraphContext context) => Generate(d, context));
            }

            frame.Settings = settings;
            frame.Draw = new DrawData
            {
                Material = material, Indices = indices, IndexHandle = indexHandle,
                Blades = blades, Arguments = arguments, Chunks = snapshot,
                Properties = CreateProperties(settings, 0, level)
            };
        }

        internal void RecordDraw(RenderGraph graph, ContextContainer frameData)
        {
            var frame = frameData.GetOrCreate<GrassFrameData>();
            DrawData generated = frame.Draw;
            if (generated == null) return;
            GrassSettings settings = frame.Settings;
            var resources = frameData.Get<UniversalResourceData>();
            using (var builder = graph.AddRasterRenderPass<DrawData>("Grass Draw", out var data, drawSampler))
            {
                data.Material = generated.Material;
                data.Indices = generated.Indices;
                data.IndexHandle = generated.IndexHandle;
                data.Blades = generated.Blades;
                data.Arguments = generated.Arguments;
                data.Chunks = generated.Chunks;
                data.Properties = generated.Properties;
                builder.UseBuffer(data.Blades, AccessFlags.Read);
                builder.UseBuffer(data.Arguments, AccessFlags.Read);
                builder.UseBuffer(data.IndexHandle, AccessFlags.Read);
                // The optional maps are explicitly declared, including the neutral fallback textures.
                RTHandle cloud = GetEffectHandle(settings.CloudTexture);
                RTHandle wave = GetEffectHandle(settings.WaveTexture);
                builder.UseTexture(graph.ImportTexture(cloud), AccessFlags.Read);
                builder.UseTexture(graph.ImportTexture(wave), AccessFlags.Read);
                if (resources.mainShadowsTexture.IsValid()) builder.UseTexture(resources.mainShadowsTexture, AccessFlags.Read);
                builder.UseAllGlobalTextures(true);
                builder.SetRenderAttachment(resources.activeColorTexture, 0, AccessFlags.ReadWrite);
                builder.SetRenderAttachmentDepth(resources.activeDepthTexture, AccessFlags.ReadWrite);
                builder.SetRenderFunc((DrawData d, RasterGraphContext context) =>
                {
                    for (int i = 0; i < d.Chunks.Length; ++i)
                    {
                        MaterialPropertyBlock properties = d.Properties;
                        properties.SetInt("_BladeOffset", d.Chunks[i].Offset);
                        properties.SetBuffer("_GrassBlades", (GraphicsBuffer)d.Blades);
                        // XRPass supplies the SPI instance multiplier. Arguments contain logical blade counts.
                        context.cmd.DrawProceduralIndirect(d.Indices, Matrix4x4.identity, d.Material, 0,
                            MeshTopology.Triangles, d.Arguments, i * 5 * sizeof(uint), properties);
                    }
                });
            }
        }

        private sealed class EffectHandle
        {
            internal RTHandle Handle;
            internal int LastFrame;
        }
        private readonly Dictionary<Texture, EffectHandle> effectHandles = new Dictionary<Texture, EffectHandle>();
        private readonly List<Texture> expiredEffects = new List<Texture>();

        private RTHandle GetEffectHandle(Texture texture)
        {
            if (!effectHandles.TryGetValue(texture, out EffectHandle entry))
            {
                entry = new EffectHandle { Handle = RTHandles.Alloc(texture) };
                effectHandles.Add(texture, entry);
            }
            entry.LastFrame = Time.frameCount;
            return entry.Handle;
        }

        private void PruneEffectHandles()
        {
            expiredEffects.Clear();
            foreach (var pair in effectHandles)
                if (pair.Key == null || Time.frameCount - pair.Value.LastFrame > 120)
                    expiredEffects.Add(pair.Key);
            foreach (Texture texture in expiredEffects)
            {
                effectHandles[texture].Handle.Release();
                effectHandles.Remove(texture);
            }
        }

        private static MaterialPropertyBlock CreateProperties(GrassSettings s, int offset, int level)
        {
            var block = new MaterialPropertyBlock();
            block.SetInt("_BladeOffset", offset);
            block.SetInt("_TopologyLevel", level);
            block.SetVector("_BladeShape", s.Shape);
            block.SetVector("_Wind", s.Wind);
            block.SetVector("_Effects", s.Effects);
            block.SetVector("_Shading", s.Shading);
            block.SetColor("_BottomColor", s.Bottom);
            block.SetColor("_TopColor", s.Top);
            block.SetColor("_ShadowColor", s.Shadow);
            block.SetColor("_WaveColor", s.Wave);
            block.SetTexture("_CloudShadow", s.CloudTexture);
            block.SetTexture("_GrassWave", s.WaveTexture);
            return block;
        }

        private static void Generate(GenerateData d, ComputeGraphContext context)
        {
            var cmd = context.cmd;
            ComputeShader shader = d.Shader;
            cmd.SetComputeIntParam(shader, "_ChunkTotal", d.ChunkCount);
            cmd.SetComputeIntParam(shader, "_IndexCount", d.IndexCount);
            cmd.SetComputeBufferParam(shader, d.ResetKernel, "_Arguments", d.Arguments);
            cmd.DispatchCompute(shader, d.ResetKernel, (d.ChunkCount + 63) / 64, 1, 1);
            cmd.SetComputeBufferParam(shader, d.GenerateKernel, "_Arguments", d.Arguments);
            cmd.SetComputeBufferParam(shader, d.GenerateKernel, "_GrassBlades", d.Blades);
            cmd.SetComputeVectorParam(shader, "_CameraCenter", d.CameraPosition);
            cmd.SetComputeVectorParam(shader, "_Distribution", d.Settings.Distribution);
            cmd.SetComputeVectorParam(shader, "_BladeShape", d.Settings.Shape);
            cmd.SetComputeVectorParam(shader, "_Distance", d.Settings.Distance);
            cmd.SetComputeFloatParam(shader, "_Expansion", d.Settings.Expansion);
            cmd.SetComputeVectorArrayParam(shader, "_FrustumPlanes", d.Planes);
            cmd.SetComputeIntParam(shader, "_EyeCount", d.EyeCount);
            cmd.SetComputeIntParam(shader, "_Capacity", d.Capacity);
            for (int i = 0; i < d.Chunks.Length; ++i)
            {
                DrawChunk chunk = d.Chunks[i];
                var surface = chunk.Surface;
                cmd.SetComputeTextureParam(shader, d.GenerateKernel, "_Heightmap", chunk.Height);
                cmd.SetComputeTextureParam(shader, d.GenerateKernel, "_Alphamap", chunk.Weight);
                cmd.SetComputeTextureParam(shader, d.GenerateKernel, "_Holes", chunk.Holes);
                cmd.SetComputeVectorParam(shader, "_TerrainPosition", surface.Position);
                cmd.SetComputeVectorParam(shader, "_TerrainSize", surface.Size);
                cmd.SetComputeVectorParam(shader, "_Grid", chunk.Grid);
                cmd.SetComputeIntParam(shader, "_LayerChannel", surface.Channel);
                cmd.SetComputeIntParam(shader, "_CandidateCount", chunk.Count);
                cmd.SetComputeIntParam(shader, "_CandidateTotal", chunk.Total);
                cmd.SetComputeIntParam(shader, "_BladeOffset", chunk.Offset);
                cmd.SetComputeIntParam(shader, "_ChunkIndex", i);
                cmd.DispatchCompute(shader, d.GenerateKernel, (chunk.Count + 63) / 64, 1, 1);
            }
        }

        private GraphicsBuffer GetIndexBuffer(int level)
        {
            if (indexBuffers[level - 1] != null) return indexBuffers[level - 1];
            int triangleCount = level * 2 - 1;
            var values = new uint[triangleCount * 3];
            // A tapered strip ending in one tip. Level 3 is five triangles/seven vertices.
            for (int i = 0; i < triangleCount; ++i)
            {
                values[i * 3] = (uint)(i % 2 == 0 ? i : i + 1);
                values[i * 3 + 1] = (uint)(i % 2 == 0 ? i + 1 : i);
                values[i * 3 + 2] = (uint)(i + 2);
            }
            var buffer = new GraphicsBuffer(GraphicsBuffer.Target.Index, values.Length, sizeof(uint));
            buffer.SetData(values);
            indexBuffers[level - 1] = buffer;
            return buffer;
        }

        public void Dispose()
        {
            terrains.Dispose();
            foreach (GraphicsBuffer buffer in indexBuffers) buffer?.Dispose();
            foreach (EffectHandle entry in effectHandles.Values) entry.Handle.Release();
            effectHandles.Clear();
            CoreUtils.Destroy(material);
        }
    }
}
