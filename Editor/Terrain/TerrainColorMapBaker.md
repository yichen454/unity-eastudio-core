# Terrain Surface Baker

Open **Tools > EAStudio > Terrain > Surface Baker**. One recipe bakes a required Surface texture and, when **Bake Detail (Optional)** is enabled, a Detail texture. The source must use stock URP 17.3 Terrain/Lit in a Linear color-space project. Existing `*_ColorMapBake.asset` recipes retain their original Surface PNG path and GUID; new `*_SurfaceBake.asset` recipes create a sibling `*_Surface.png` and optional `*_Surface_Detail.png`.

## Workflow

1. Select a Terrain and choose **Use Selected Terrain**, or assign persistent TerrainData and an optional stock Terrain/Lit material.
2. Select width and height, and enable **Bake Detail (Optional)** only when consumers need terrain normals and material AO.
3. Save the recipe under `Assets/`, validate, preview, then bake. Rebaking preserves the output GUIDs. A failed or cancelled operation retains the previous verified outputs.
4. Add `TerrainSurfaceProvider` to the corresponding Terrain. In its Inspector, select the matching up-to-date recipe and choose **Assign Verified Outputs**. Assign only generated materials owned by that Terrain to the Provider's material list.

## Channel contract

| Output | Channels | Import |
| --- | --- | --- |
| Surface | RGB: unlit Terrain Base Color; A: smoothness | sRGB RGB, linear alpha, RGBA8, bilinear, clamp, mipmaps |
| Detail (optional) | RG: octahedrally encoded world-space final shading normal; B: material AO; A: blend permission (currently 1) | linear RGBA8, point, clamp, no mipmaps |

Both outputs use the Terrain-local endpoint grid: pixel `(x,y)` represents UV `(x/(width-1), y/(height-1))`. At runtime, sample the same grid with `(uv*(dimensions-1)+0.5)/dimensions`. Detail uses point filtering because filtering octahedral coordinates across a fold changes normal direction; consumers needing smooth normals must decode neighboring texels and blend the decoded vectors. The Detail bake combines Terrain geometry normals and TerrainLayer normal maps. Native TerrainData height is not packed into either output.

The evaluator supports stock Terrain/Lit control weights, tiling/remaps, density blending, four-layer height blending, mask maps, and add-pass contribution thresholds. It does not bake lighting, shadows, fog, holes, or world translation. Custom terrain shaders and runtime TerrainLayer painting require separate support.

## Verification

The isolated Unity 6000.3.10f1 Metal host passes 19 Editor baker tests, including optional Detail output, flat and layer-normal cases, mask smoothness/AO, source freshness, rebake GUID preservation and dual-output failure rollback. Stock forward-albedo comparisons from the earlier color-only baker cover RGB, not the new smoothness/normal/AO channels. Vulkan/XR Player output and live main-project Inspector behavior remain unverified.
