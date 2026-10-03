using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.RenderFeature.Grass
{
    internal sealed class GrassTerrainCache : IDisposable
    {
        internal sealed class Surface : IDisposable
        {
            internal Terrain Terrain;
                        internal Vector3 Position, Size;
            internal RTHandle Height, Weight, Holes;
            internal int Channel;
            internal Bounds HeightBounds;
            internal int Columns, Rows;
            internal readonly Dictionary<Vector2Int, Bounds> Chunks = new Dictionary<Vector2Int, Bounds>();
            public void Dispose()
            {
                Height?.Release();
                Weight?.Release();
                Holes?.Release();
            }
        }

        internal readonly struct Chunk
        {
            internal readonly Surface Surface;
            internal readonly Bounds Bounds;
            internal readonly float DistanceSquared;
            internal readonly int Order;
            internal Chunk(Surface surface, Bounds bounds, Vector3 cameraPosition, int order)
            {
                Surface = surface;
                Bounds = bounds;
                DistanceSquared = bounds.SqrDistance(cameraPosition);
                Order = order;
            }
        }

        private readonly List<Surface> surfaces = new List<Surface>();
        private readonly List<Chunk> visible = new List<Chunk>();
        private TerrainLayer selectedLayer;
        private float nextDiscovery;
        private bool dirty = true;
        private float previousChunkSize;
        private int previousChunkLimit;
        private int discoveryHash;
        private int cachedChunks;

        internal GrassTerrainCache()
        {
            TerrainCallbacks.heightmapChanged += OnHeightChanged;
            TerrainCallbacks.textureChanged += OnTextureChanged;
        }

        internal void Invalidate() => dirty = true;
        private void OnHeightChanged(Terrain terrain, RectInt region, bool synced) => Invalidate();
        private void OnTextureChanged(Terrain terrain, string name, RectInt region, bool synced) => Invalidate();

        internal List<Chunk> Collect(GrassSettings settings, Vector3 cameraPosition, int cullingMask,
            Plane[] left, Plane[] right, float chunkSize, int chunkLimit)
        {
            bool changed = selectedLayer != settings.Layer || previousChunkSize != chunkSize || previousChunkLimit != chunkLimit;
            if (dirty || changed || Time.realtimeSinceStartup >= nextDiscovery)
                Refresh(settings.Layer, chunkSize, chunkLimit, dirty || changed);
            visible.Clear();
            int order = 0;
            foreach (Surface surface in surfaces)
            {
                Terrain terrain = surface.Terrain;
                if (terrain == null || !terrain.isActiveAndEnabled || !terrain.gameObject.activeInHierarchy
                    || (cullingMask & (1 << terrain.gameObject.layer)) == 0)
                    continue;
                float margin = settings.Expansion + 1f / Mathf.Sqrt(settings.Density);
                float radius = settings.Range + margin;
                int firstX = Mathf.Clamp(Mathf.FloorToInt((cameraPosition.x - radius - surface.Position.x) / chunkSize), 0, surface.Columns - 1);
                int lastX = Mathf.Clamp(Mathf.FloorToInt((cameraPosition.x + radius - surface.Position.x) / chunkSize), 0, surface.Columns - 1);
                int firstZ = Mathf.Clamp(Mathf.FloorToInt((cameraPosition.z - radius - surface.Position.z) / chunkSize), 0, surface.Rows - 1);
                int lastZ = Mathf.Clamp(Mathf.FloorToInt((cameraPosition.z + radius - surface.Position.z) / chunkSize), 0, surface.Rows - 1);
                for (int z = firstZ; z <= lastZ; ++z)
                {
                    for (int x = firstX; x <= lastX; ++x)
                    {
                        var key = new Vector2Int(x, z);
                        if (!surface.Chunks.TryGetValue(key, out Bounds original))
                        {
                            float width = Mathf.Min(chunkSize, surface.Size.x - x * chunkSize);
                            float depth = Mathf.Min(chunkSize, surface.Size.z - z * chunkSize);
                            original = new Bounds(surface.Position + new Vector3(x * chunkSize + width * 0.5f,
                                surface.HeightBounds.center.y, z * chunkSize + depth * 0.5f),
                                new Vector3(width, surface.HeightBounds.size.y, depth));
                            // Bounds are cheap to rebuild. Eviction never removes terrain coverage.
                            if (cachedChunks >= chunkLimit)
                            {
                                foreach (Surface cached in surfaces) cached.Chunks.Clear();
                                cachedChunks = 0;
                            }
                            surface.Chunks[key] = original;
                            ++cachedChunks;
                        }
                        Bounds bounds = original;
                        bounds.Expand(margin * 2f);
                        if (bounds.SqrDistance(cameraPosition) > settings.Range * settings.Range) continue;
                        if (!GeometryUtility.TestPlanesAABB(left, bounds)
                            && (right == null || !GeometryUtility.TestPlanesAABB(right, bounds))) continue;
                        visible.Add(new Chunk(surface, original, cameraPosition, order++));
                    }
                }
            }
            visible.Sort((a, b) =>
            {
                int distance = a.DistanceSquared.CompareTo(b.DistanceSquared);
                return distance != 0 ? distance : a.Order.CompareTo(b.Order);
            });
            if (visible.Count > chunkLimit) visible.RemoveRange(chunkLimit, visible.Count - chunkLimit);
            return visible;
        }

        private void Refresh(TerrainLayer layer, float chunkSize, int chunkLimit, bool force)
        {
            Terrain[] active = Terrain.activeTerrains;
            // Discovery is throttled. GPU texture contents are sampled live without CPU extraction.
            int hash = 17;
            unchecked
            {
                foreach (Terrain terrain in active)
                {
                    TerrainData data = terrain.terrainData;
                    hash = hash * 31 + terrain.GetInstanceID();
                    hash = hash * 31 + terrain.transform.position.GetHashCode();
                    if (data == null) continue;
                    hash = hash * 31 + data.GetInstanceID();
                    hash = hash * 31 + data.size.GetHashCode();
                    hash = hash * 31 + data.heightmapResolution;
                    hash = hash * 31 + data.alphamapResolution;
                    hash = hash * 31 + data.heightmapTexture.GetInstanceID();
                    hash = hash * 31 + data.holesTexture.GetInstanceID();
                    foreach (TerrainLayer candidate in data.terrainLayers)
                        hash = hash * 31 + (candidate != null ? candidate.GetInstanceID() : 0);
                    int layerIndex = Array.IndexOf(data.terrainLayers, layer);
                    if (layerIndex >= 0 && layerIndex / 4 < data.alphamapTextureCount)
                        hash = hash * 31 + data.GetAlphamapTexture(layerIndex / 4).GetInstanceID();
                }
            }
            bool rebuild = force || hash != discoveryHash;
            if (rebuild)
            {
                Clear();
                foreach (Terrain terrain in active)
                {
                    TerrainData data = terrain.terrainData;
                    if (data == null) continue;
                    TerrainLayer[] layers = data.terrainLayers;
                    int index = Array.IndexOf(layers, layer);
                    if (index < 0 || index / 4 >= data.alphamapTextureCount) continue;
                    Vector3 size = data.size;
                    if (size.x <= 0f || size.z <= 0f) continue;
                    var surface = new Surface
                    {
                        Terrain = terrain,
                        Position = terrain.transform.position, Size = size, Channel = index % 4,
                        Height = RTHandles.Alloc(data.heightmapTexture),
                        Weight = RTHandles.Alloc(data.GetAlphamapTexture(index / 4)),
                        Holes = RTHandles.Alloc(data.holesTexture)
                    };
                    // Cache grid descriptors and lazily materialize bounds near each camera.
                    surface.HeightBounds = data.bounds;
                    surface.Columns = Mathf.CeilToInt(size.x / chunkSize);
                    surface.Rows = Mathf.CeilToInt(size.z / chunkSize);
                    surfaces.Add(surface);
                }
            }
            discoveryHash = hash;
            selectedLayer = layer;
            previousChunkSize = chunkSize;
            previousChunkLimit = chunkLimit;
            nextDiscovery = Time.realtimeSinceStartup + 0.5f;
            dirty = false;
        }

        private void Clear()
        {
            foreach (Surface surface in surfaces) surface.Dispose();
            surfaces.Clear();
            cachedChunks = 0;
        }

        public void Dispose()
        {
            TerrainCallbacks.heightmapChanged -= OnHeightChanged;
            TerrainCallbacks.textureChanged -= OnTextureChanged;
            Clear();
        }
    }
}
