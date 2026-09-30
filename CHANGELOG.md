# Changelog

All notable changes to the `com.eastudio.core` package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.3.0] - 2026-09-30

### Added
- **TimeOfDay (TOD) Architecture Overhaul**:
  - `CelestialLightingMode`: Introduced `Single` (default) and `Dual` modes. Single mode dynamically redirects a single directional light between Sun and Moon, eliminating cascaded shadow map passes and additional light overhead in URP mobile/XR pipelines.
  - `CelestialOrbitMode`: Added dual trajectory engines — `Realistic` (astronomical Keplerian/elevation equations with latitude and seasonal axial tilt) and `Custom` (artistic East-to-West polar orbit passing exact zenith at noon).
  - Time progression modifiers: Added `dayLengthModifier` and `nightLengthModifier` for independent day and night speeds.
  - Decoupled celestial metrics & events: Exposed `SolarTime` (0~1), `LunarTime` (0~1), `IsNight`, and lifecycle hooks `OnHourPassed`, `OnDayPassed`, and `OnDayNightTransition`.
  - Celestial sphere matrix: Injected `_CelestialStarsMatrix` into global shaders for dynamic night sky rotation.

### Fixed
- **Moon Visual Geometry & Lunar Phase Decoupling**:
  - Fixed an issue where single-light nighttime redirected light transforms caused `ProceduralSkyController` to mistake the moon for the sun, rendering a cyan solar disc at the moon location and displacing the moon underground.
  - Decoupled physical visual sky directions (`CurrentSunDirection`, `CurrentMoonDirection`) from lighting tracking transforms.
  - Re-anchored solar and lunar intensity curves to physical elevation scales, preventing noon blackout ($t=1.0 ightarrow 1.0$).
  - Added self-healing `ValidateCurves()` to repair legacy inverted curves on `Awake`/`OnValidate`.
  - Added parameter override defaults to `MoonSettings` VolumeComponent.

---

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
