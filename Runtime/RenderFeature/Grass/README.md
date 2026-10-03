# Terrain Grass

A Unity 6 / URP 17 RenderGraph renderer for opaque geometric grass on active Unity Terrains.

## Setup

1. Add `GrassRenderFeature` to the Universal Renderer used by the camera. Its compute and blade shaders resolve automatically from this package's Resources folder.
2. Add `EAStudio/Grass` to a Volume Profile that affects the camera.
3. Override `Enable` and turn it on. Override `Terrain Layer` and select the painted TerrainLayer asset.
4. Override density, range, shape, colors and wind as needed. Density is candidate blades per square meter before layer weights and distance thinning.

The feature alone does not enable grass. No effective enabling override, missing TerrainLayer, zero density/range, inactive Volume, or invalid shader inputs produce no grass work. Local Volume bounds configure the camera; they do not clip the grass footprint. A Global Volume can affect Terrains in other loaded scenes.

Each Terrain resolves the selected asset independently, including alphamap indices beyond the first RGBA texture. Terrain heights, alphamaps and holes are sampled directly on the GPU. Terrain GameObject culling layers are respected. Height variation reduces the configured maximum height. Level 3 uses five triangles/seven vertices per blade.

## Capacity and lifetime

- `Maximum Candidates` bounds candidate dispatches and instance storage for each camera. Nearby visible chunks reserve their slots first. A partially funded chunk samples its whole area; under saturation the configured density cannot be maintained.
- `Maximum Chunks` limits submitted visible chunks and cached chunk bounds. Eviction rebuilds bounds without permanently removing terrain coverage.
- `Chunk Size` trades CPU/draw overhead against culling and budget granularity.

World-space candidate identities are independent of the camera. Distance thinning uses a stable rank with a short-height transition, shared eye-center distance and distant width compensation. Chunk ordering is coarse front-to-back; GPU compaction does not sort blades within a chunk.

Terrain discovery/layer mapping checks are throttled to 0.5 seconds; terrain height/texture callbacks trigger refreshes. Call `GrassRenderFeature.InvalidateTerrainCache()` after external edits that do not notify Unity. No CPU height/alphamap extraction runs per frame.

Instance/argument buffers belong to each RenderGraph recording. Persistent materials, index buffers, terrain texture wrappers and callbacks are released when the feature is recreated or disposed. Optional effect texture wrappers are cached and stale entries are pruned during active rendering.

## Rendering and verification boundary

Grass generation runs at `BeforeRenderingOpaques`. Per-camera ContextContainer data carries its buffers/settings to drawing at `AfterRenderingOpaques`. Grass draws after opaques with depth testing/writing, double-sided silhouette triangles and no alpha clipping/blending. The shader receives main-light shadows and fog; optional world-space cloud-shadow and grass-wave textures use the configured tile size. Grass does not cast shadows.

The stereo shader uses Unity's logical instance index and per-eye transform macros. Indirect counts contain logical blades; URP owns Single Pass Instanced multiplication. Culling retains the union of both eye frusta, including Multi Pass.

Desktop Metal compute and RenderGraph drawing have been verified. Target headset Multiview, Single Pass Instanced and Multi Pass rendering, Player shader variants, and GPU performance still require device verification. No overdraw or speedup measurement is claimed.

Local verification tests in the git-ignored `Tests/Editor` folder can be run as `EAStudio.Core.Tests` in the Unity Test Runner's EditMode tab. They cover Volume activation/mask/local bounds and native Terrain compute behavior. GPU tests require compute and indirect-buffer support; tests are not included in the distributed package.

The ToonSample-Unity reference license is preserved in `ThirdPartyNotices.txt`.
