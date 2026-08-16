# Changelog

All notable changes to the `com.eastudio.core` package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0] - 2026-08-16

### Added
- **URP Render Features**:
  - `RenderingLayerDepthPrepassFeature` & `CustomDepthContextData` for custom Depth PrePass filtering by rendering layers.
  - `UIOverlayRenderFeature` for scene and UI overlay rendering passes.
- **Scene Warmup Pipeline**:
  - `SceneWarmup` core management and asynchronous warmup routines.
  - `EnableFlow` with frame-time budget control to prevent stutter during multi-object activation.
  - `SceneReference` serialized scene reference helper.
  - `SceneWarmupTrack`, `SceneWarmupClip`, and `SceneWarmupMixerBehaviour` for Timeline-driven scene warming.
- **Timeline Extensions**:
  - `TimelineTrigger` event dispatching framework (`Track`, `Clip`, `Behaviour`, `Mixer`).
  - `TimelineTriggerEditor` custom Inspector with automatic clip-list synchronization.
- **Common & Runtime Utils**:
  - `AndroidPermissionUtils` for Android 11+ Manage All Files Access (`MANAGE_ALL_FILES_ACCESS_PERMISSION`).
  - `BoneVisualizer` for visual skeleton debugging in scene view.
  - `DisableRendererCulling` component.
- **Editor Productivity Tools**:
  - `AssetFixerWindow` (`Tools/EAStudio/资产/场景资产整理与修复`) for model and texture dependency fixing.
  - `ResourceOrganizerWindow` (`Tools/EAStudio/资产/场景资源归类整理`) for automated model-centric asset migration.
  - `LightmapAnalyzer` (`Tools/EAStudio/光照/光照贴图使用分析`) for lightmap usage and GI statistics.
  - `TerrainMergeTool` (`Tools/EAStudio/地形/合并选中地形`) for multi-terrain heightmap and alphamap blending.
  - `TerrainTreeConverterWindow` (`Tools/EAStudio/地形/地形树转实体 GameObject`) for terrain tree baking.
  - `TextureEditorWindow` (`Tools/EAStudio/贴图/贴图编辑器`) GPU-accelerated texture channel packing and procedural noise synthesis via `ChannelMerge.compute` and `NoiseGen.compute`.

### Changed
- Refactored and modularized all editor tools into domain-specific folders (`Assets/`, `Lighting/`, `Terrain/`, `Texture/Shaders/`).
- Standardized namespaces under `EAStudio.Core.Editor` and `EAStudio.Core.Runtime`.
- Unified all editor menu commands under the categorized Chinese hierarchy `Tools/EAStudio/...`.
- Updated package dependency declarations for `com.unity.render-pipelines.universal` and `com.unity.timeline`.

---

## [0.1.0] - 2026-08-16

### Added
- Initial package scaffolding and assembly definition files (`EAStudio.Core.Runtime.asmdef`, `EAStudio.Core.Editor.asmdef`).
- Package manifest and repository configurations.
