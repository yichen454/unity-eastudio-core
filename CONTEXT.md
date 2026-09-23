# CONTEXT.md — EAStudio Core

## Billboard Baker Domain

### Glossary

**Bake Subject**
The set of one or more scene GameObjects (e.g. a mountain mesh group, a composite prop cluster) selected by the user to be captured. A Bake Subject has a world-space **Bounding Volume** computed from the union of all its Renderers.

**Capture Viewpoint**
The world-space position and orientation from which the Bake Subject is photographed. Determined by the **Viewpoint Source** chosen by the user. This is an Editor-controlled snapshot origin, not the scene's runtime Main Camera.

**Viewpoint Source**
How the Capture Viewpoint is resolved:
- *SceneView*: mirrors the active Scene View Editor camera at the moment Bake is triggered.
- *Camera Reference*: uses the Transform and projection of a user-specified Camera GameObject placed in the scene as a design-time stand-in.

**Capture Camera**
A temporary, tool-owned Camera created at the Capture Viewpoint for the duration of the bake. Perspective projection, FOV defined by **Tile Angle**. Created and destroyed programmatically per bake — never persisted in the scene.

**Tile Angle**
The horizontal field-of-view angle of one Capture Camera pass. Typically 60°. The camera is rotated incrementally by this amount around the vertical axis at the Capture Viewpoint to produce each strip of the **Panoramic Strip Texture**.

**Tile Count**
The number of horizontal capture passes needed to cover the **Coverage Angle**. Derived as `ceil(CoverageAngle / TileAngle)`. Each pass produces one tile that is composited into the Panoramic Strip Texture.

**Coverage Angle**
The total horizontal arc angle (in degrees) that the Cylindrical Arc Impostor must span, as measured from the Capture Viewpoint. Computed automatically from the Bake Subject's Bounding Volume subtended angle, with optional padding. Typical range: 60°–180°.

**Panoramic Strip Texture**
The wide-aspect RGBA texture output of one bake, formed by horizontally stitching all Tile renders. Alpha encodes the subject silhouette (transparent background). Stored as a project PNG asset. Width is `TileCount × TileResolution`; height is user-specified.

**Arc Proxy Mesh**
A procedurally generated, curved mesh segment — a vertical strip of a cylinder — placed in the scene to serve as the runtime stand-in for the Bake Subject. Its curvature parameters are:
- *Arc Radius*: the horizontal distance from the Capture Viewpoint to the Bake Subject's bounding centroid.
- *Arc Angle*: matches the Coverage Angle.
- *Arc Height*: derived from the Bake Subject's vertical extent in the Panoramic Strip Texture.
This is NOT a flat Quad. The curvature is what allows the impostor to remain visually accurate as the observer moves horizontally.

**Arc Proxy Placement**
The Arc Proxy Mesh is centred on the Bake Subject's world-space bounding centroid, oriented so its concave face points toward the Capture Viewpoint. The arc's axis of curvature is world-up (Y axis).

**Impostor Material**
An Unlit, alpha-tested URP Material applied to the Arc Proxy Mesh. Samples the Panoramic Strip Texture using UV coordinates baked into the Arc Proxy Mesh vertices. Casts no real-time shadows by default. Supports optional wind sway uniform for vegetation.

**Bake Session**
One complete authoring round-trip: user selects Bake Subject → picks Viewpoint Source → adjusts Coverage Angle and resolution → triggers bake → tool writes Panoramic Strip Texture and creates/updates Arc Proxy Mesh in the scene.

**Bake Artifact Set**
All persistent assets produced by a Bake Session: the Panoramic Strip Texture (.png) and the Arc Proxy Mesh prefab. Named after the Bake Subject's root GameObject.

**LOD Proxy Mode**
How the Arc Proxy Mesh coexists with the original Bake Subject:
- *Replace*: original GameObjects are deactivated (hidden, kept as reference); only the Arc Proxy is active.
- *LOD Group*: original geometry + Arc Proxy wired into Unity's LOD Group. Original at LOD0; Arc Proxy at LOD1+.

**Visual Center**
The screen-space centroid of the projected Bake Subject silhouette within the Capture Camera's frustum. Used to auto-frame the capture so the subject fills the frame with minimal wasted pixels and the Arc Proxy UV mapping is tight.


## Scene Shader Analyzer Domain

### Glossary

**Scene Shader Analyzer**
An Editor utility window (`Tools/EAStudio/Shader/Scene Shader Analyzer`) designed to scan, aggregate, and inspect all Shader assets, active material keywords, and theoretical variant counts referenced across current open scenes.

**Scan Scope**
The full search surface within the active scenes, encompassing:
- All `Renderer` components (active and inactive, including `MeshRenderer`, `SkinnedMeshRenderer`, `ParticleSystemRenderer`, `TrailRenderer`, `LineRenderer`, `SpriteRenderer`, etc.).
- `Terrain` components (terrain material / custom shader).
- `CanvasRenderer` and UI graphics (`Image`, `RawImage`, `Text` using custom materials).
- Scene-level settings: `RenderSettings.skybox`.
- Scene-bound `Volume` components containing materials (e.g. custom post-processing / blit passes).

**Shader Usage Entry**
The aggregated usage record for a single distinct `Shader` asset found in the **Scan Scope**. Contains total material count, referencing renderer count, total theoretical compiled variant count, and a list of scene-active **Material Variant Usages**.

**Theoretical Variant Count**
The total permutation count of compiled shader variants defined on the Shader asset across its subshaders and passes (via `ShaderUtil.GetVariantCount` / `ShaderData`), representing the upper bound of possible variants compiled for the target platform.

**Material Variant Usage**
A distinct combination of active shader keywords (`Material.shaderKeywords`) used by one or more materials sharing the same Shader. Represents an actual variant permutation actively demanded by the scene.

**Variant Keyword Signature**
A deterministic, sorted representation (hash or canonical string) of all local and global keywords enabled on a material. Materials with identical signatures share the same scene variant demand.

**Referencing Target**
A concrete scene object bound to a material or shader:
- A `Renderer` (or `Terrain`, `Graphic`, `Skybox` representation) in the scene hierarchy.
- Clicking a Referencing Target in the Editor Window pings and selects the corresponding GameObject in the Unity Hierarchy and Scene View.

**Keyword Impact Analysis (变体影响分析)**
The mechanism that inspects each active shader keyword across scene materials:
- Categorizes whether a keyword is the root cause of **Variant Splitting** (`IsMultiVariantCause`: active on some materials but inactive on others using the same Shader).
- Correlates each keyword with its affected variant count, material count, and referencing renderer count.
- Enables single-click scene batch selection of all GameObjects enabling a specific keyword.
