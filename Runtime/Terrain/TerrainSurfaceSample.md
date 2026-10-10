# Terrain Surface Shader Graph Custom Function

Use a **Custom Function** node in **File** mode. Select `TerrainSurfaceSample.hlsl`, set **Name** to `TerrainSurfaceSample`, and set **Precision** to **Float**. This function samples textures in the fragment stage; do not place it in a vertex-stage path.

Create these Shader Graph properties with the exact **Reference** names below, then connect their property nodes to the matching function inputs. They must be material properties so `TerrainSurfaceProvider` can bind them.

| Property reference | Shader Graph type | Function input |
| --- | --- | --- |
| `_TerrainSurface` | Texture2D | `Surface` |
| `_TerrainDetail` | Texture2D | `Detail` |
| `_TerrainHeightmap` | Texture2D | `Heightmap` |
| `_TerrainPosition` | Vector3 | `TerrainPosition` |
| `_TerrainSize` | Vector3 | `TerrainSize` |
| `_TerrainHasDetail` | Float | `HasDetail` |

Add `PositionWS` as a Vector3 input and connect a **Position** node set to **World** space. Add these output ports in this order:

| Port | Type | Meaning |
| --- | --- | --- |
| `BaseColor` | Vector3 | Linear unlit terrain base color |
| `Smoothness` | Float | Surface alpha |
| `NormalWS` | Vector3 | World-space Detail normal, or world up without Detail |
| `Occlusion` | Float | Detail material AO, or 1 without Detail |
| `BlendPermission` | Float | Detail alpha, or 1 without Detail |
| `HeightWS` | Float | Native Terrain height in world units |
| `InBounds` | Float | 1 within Terrain XZ, otherwise 0 |

Set each port's precision to Float. The native heightmap uses Unity's platform-specific `UnpackHeightmap` path and interpolates the same two heightmap triangles as Unity Terrain before applying the native two-times height scale. Bilinear interpolation can move the contact line away from the rendered Terrain between vertices. This avoids reducing height to an 8-bit surface channel. `NormalWS` is already in world space; transform it if the target Shader Graph normal slot expects tangent space.

The Provider sets `_TerrainHasDetail` to 0 or 1 when it refreshes its explicitly assigned materials. When Detail is absent, the function does not sample it. The function returns neutral outputs outside the Terrain XZ footprint. Generated materials must belong to only one Terrain; assigning the same material to two Providers makes the last refresh overwrite the first Terrain's bindings.

## Lit Blend Sample

`LitBlendTerrain.shadergraph` is a ready-to-use URP Lit graph that calls `TerrainLitBlend_float` from the same HLSL file. `LitBlendTerrainSample.mat` uses the graph with a white Base Map, white Base Color, 0.37 world-unit blend distance, and full blend strength. Adjust object smoothness and blend distance for the mesh material.

1. Assign the sample material to a mesh that intersects or sits close to one Terrain. Give that Terrain a baked Surface and, optionally, a baked Detail.
2. Add the material to that Terrain's `TerrainSurfaceProvider` Generated Materials list. Use a separate material instance for each Terrain; the Provider writes the Terrain textures, native heightmap, position, size, and Detail presence into the material.
3. Set `_BaseMap` and `_BaseColor` for the mesh appearance. `_ObjectSmoothness` controls its unblended smoothness. `_TerrainBlendDistance` is the vertical world-space distance over which the Terrain appearance fades in; `_TerrainBlendStrength` scales the effect from 0 to 1.

The graph writes blended Base Color, Smoothness, World Normal, and Occlusion to the Lit target. Every mesh face uses the same height-based blend weight: pixels at or below Terrain height receive full contact weight, while pixels above it fade to zero over `_TerrainBlendDistance`. The final blend is also scaled by `_TerrainBlendStrength` and Detail blend permission. Mesh normal direction does not affect the weight. Without Detail, the graph retains the mesh world normal and AO 1. Terrain data is sampled only inside the owning Terrain's XZ bounds. This basic example does not blend metallic, emission, alpha, or a mesh normal map. The Provider-owned texture fields should not be edited on the material directly.

The contact height comes from the full-resolution native heightmap. Unity Terrain can render a simplified mesh when its Complexity Limit (Heightmap Pixel Error) allows it. At a high limit, the visible Terrain surface can differ from the sampled height even when world coordinates and heightmap decoding are correct. For a close-up contact example, lower the Terrain's Complexity Limit until the visible mesh matches the contact line. The Grass sample scene currently stores a Heightmap Pixel Error of 50. Matching the camera-dependent simplified mesh exactly would require its selected patch LOD and interpolation, which this sample does not receive; a larger blend distance only hides the mismatch and broadens the color band.
