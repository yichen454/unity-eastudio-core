# Changelog

All notable changes to the `com.eastudio.core` package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.4.0] - 2026-10-02

### Added
- **Procedural Sky Dome Brightness Control**:
  - `ProceduralSky.skyBrightness` (`MinFloatParameter`, default `1.0`, not overridden by default): scales the whole atmospheric dome in-scatter. It also scales the ambient Trilight SH probe, so scene ambient lighting stays consistent with the dome instead of staying bright while the sky dims (same model as Unity's built-in skybox, where exposure drives the Skybox ambient probe). It does not affect the sun disc/corona, the moon, or the night sky HDRI.

### Changed
- **Unified Sky Ambient SH Probe**:
  - `SphericalHarmonicsUtils` now owns the single sky-to-probe convention (real-SH basis, clamped-cosine convolution, gamma decode), and both `ProceduralSkyController` and `HDRISkyController` project through it, so the scene is lit by exactly the sky that is on screen.
  - The procedural dome ambient probe is derived from `ProceduralSkyRadiance`, a CPU mirror of `SkyboxProceduralSky.shader`, replacing the previous hand-tuned `sunLum` proxy that was not tied to the rendered dome. `lightingMultiplier` is no longer folded into the probe: `RenderSettings.ambientIntensity` already scales a Skybox ambient probe, so folding it in squared the multiplier.
  - The HDRI probe now mirrors the visible skybox: the RGBM decode vector is read back from the material (`_Tex_HDR` / `_TexB_HDR`) instead of assumed, and `_Tint` / `_Exposure` are applied as the shader applies them.
  - The resulting probes track Unity's own Skybox ambient bake to within a few percent per coefficient on the sample HDRIs.

### Fixed
- **Procedural Sky Dome Rendering Black**:
  - `_AerosolHaze` no longer multiplies the Rayleigh density. It previously starved the entire dome optical depth (default `0.2` left Rayleigh at ~20% of physical density), which is what blackened the dome at low sun elevations. Density is now decoupled: `_AtmosphereThickness` drives Rayleigh, `_AerosolHaze` drives Mie only (both in-scatter and extinction).
  - The dome scatter source no longer inherits `_SunColor` (light color x intensity). The light-intensity ramp was applied on top of the atmospheric transmittance, double-dimming the sky. The scatter source is now a constant solar irradiance (`SKY_SOLAR_IRRADIANCE`) tinted only by the wavelength-dependent spectral transmittance, matching Unity's built-in `kSUN_BRIGHTNESS` reference. Light color is still used for the sun disc/corona.
  - Rewrote `GetAtmosphereSunTransmittance` against the Norex Fast Sky 2 `GetLightTransmittance` reference. The stale `0.025 / max(y + 0.1, 0.1)` zenith term was removed; spectral extinction is now the single reddening term for both the sun beam and the dome.
  - Moved `_NightSkyMap_HDR` inside the `UnityPerMaterial` CBUFFER to silence an SRP Batcher incompatibility warning.
- **Moon Horizon Transition**:
  - The moon now travels through the same atmospheric spectral transmittance and horizon fade as the sun. Previously it snapped to full brightness the instant its disc cleared the skyline, because `CalcMoon` never sampled the atmosphere at all.
  - `GetAtmosphereTransmittance` (renamed from `GetAtmosphereSunTransmittance`) and the shared `AdjustedLightY` lifted-elevation helper are now the single transmittance path for both bodies, so there is exactly one reddening term. `SkyboxProceduralSky.shader` applies `saturate(transmittance) * saturate(1 - groundBlend * 2)` to `moonFinal`, and the `ProceduralSkyRadiance` CPU mirror applies the identical factor to `MoonHalo`, keeping the ambient probe in sync with the dome.
  - A `sunSize`-style disc fade is deliberately **not** applied to the moon: its apparent radius (`0.028`) is small enough that the whole disc crosses the horizon at once, and such a fade would erase the disc instead of dimming it. The horizon fade is already a smooth transition.

- **Spherical Harmonics Y Rotation (L2 Band)**:
  - `SphericalHarmonicsUtils.RotateY` produced the wrong L2 coefficients `(6, 7, 8)`. Rotating a sky by ~20 degrees accumulated radiance-level error (~0.34), shifting HDRI and night-sky ambient lighting. The rotation is now exact at all angles (verified against brute-force sphere integration, max error 5e-7).
- **Cubemap Face Orientation in Ambient Probe**:
  - The `v` axis was inverted on all six cubemap faces when projecting an HDRI into a probe, which flipped the probe vertically (`L1y` and the `L2` terms `4/5` had the wrong sign). HDRI and night-sky ambient now match the visible skybox.

---

## [1.3.1] - 2026-10-01

### Changed
- **VisualEnvironment Decoupling**:
  - Removed global `windOrientation` and `windSpeed` parameters from `VisualEnvironment.cs`.
- **Cloud Subsystem Clean State**:
  - Maintained `CloudSettings.cs` in a clean minimal slate (`enableClouds` and `downscale`), deferring cloud-specific wind modeling to the upcoming cloud redesign phase.

---

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
  - Re-anchored solar and lunar intensity curves to physical elevation scales, preventing noon blackout ($t=1.0 \rightarrow 1.0$).
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
