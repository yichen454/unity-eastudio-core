# Changelog

All notable changes to the `com.eastudio.core` package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.4.1] - 2026-10-02

### Changed
- **Cloud Layer Naming & HDRP Alignment**: 
  - Renamed `CloudSettings` to `CloudLayer` to strictly align with HDRP. Included `[MovedFrom]` to preserve existing volume profiles.
  - Replaced screen-relative `downscale` with absolute fixed `resolution` to stabilize performance budget across XR and high-DPI displays.
- **Moon Decoupling**:
  - Removed orphaned `cloudMoonlightIntensity` from `MoonSettings.cs`. Cloud scattering intensity is now fully governed by the Cloud Layer system itself.


## [1.4.1] - 2026-10-02

### Changed
- **Cloud Layer Naming & HDRP Alignment**: 
  - Renamed  to  to strictly align with HDRP. Included  to preserve existing volume profiles.
  - Replaced screen-relative  with absolute fixed  to stabilize performance budget across XR and high-DPI displays.
- **Moon Decoupling**:
  - Removed orphaned  from . Cloud scattering intensity is now fully governed by the Cloud Layer system itself.


### Added
- **`Fade` on the Cloud Layer, for the transition into the skyline**:
  - The horizon transition used to be a private heuristic, `fadeBand = lerp(0.12, 0.02, saturate(altitude / 6000))`, and it only ran when `upperHemisphereOnly` was on. It is now the `Fade` control: the band's width in degrees of elevation above the horizon, `0`-`45`, default `5`. `CloudRenderPass` converts it to the shader's `sin(elevation)` units so the artist authors it against the skyline. The default reproduces the old `2000 m` band (`sin(5 deg) = 0.087`, against the old `0.0867`), so the calibrated look is unchanged; `0` is a hard edge, which is the state the control exists to fix.
  - **`Upper Hemisphere Only` is what decides whether the deck draws below the skyline**, which is HDRP's reading of the same switch: checked, the deck is culled at the horizon and `Fade` shapes the band above it; unchecked, the deck is allowed to hang below and `Fade` has nothing to soften. The lower half of an equirectangular hemisphere map is the upper half mirrored rather than new data, so a checked layer has nothing to show down there anyway. The previously shipped scene ran with the flag unchecked, which is why clouds hung below the horizon.
  - Measured with the camera level and pointed at the sun's azimuth at `18:30`, through a high-priority test `Volume`: mean luminance by band above the skyline.

    | `Upper Hemisphere Only` / `Fade` | `0..+1 deg` | `+1..+5 deg` | `+5..+10 deg` |
    | --- | --- | --- | --- |
    | checked / `5` | `107.41` | `87.11` | `64.31` |
    | checked / `20` | `109.32` | `99.80` | `73.43` |
    | unchecked / `5` | `23.64` | `67.27` | `64.26` |

    Unchecked, a dark cloud band sits right on the skyline (`23.64`) and the deck runs past it. Checked at `Fade = 5` that band is gone (`107.41` is the bare dusk sky) and by `+5..+10 deg` the deck is back to the unchecked value (`64.31` against `64.26`) -- the band working exactly as authored. At `Fade = 20` the deck is still dissolving at `+10 deg` (`73.43`), as a five-times wider band must.
  - **A/B a `VolumeComponent` through an overlay `Volume`, not by writing to `VolumeManager.instance.stack`.** The stack is re-merged from the profiles on every `Camera.Render()`, so a one-shot write to it is silently reverted before the pass reads it: a checked/unchecked comparison written that way renders both frames with the same settings and the difference seen between them is just wind scroll. An earlier revision of this entry quoted numbers taken that way and they were meaningless.

- **HDRP-Architecture 2D Cloud Layer**:
  - `CloudLayer` now carries HDRP's parameter set under HDRP's own names: `opacity`, `upperHemisphereOnly`, `resolution`, `cloudMap` with per-channel opacity weights (`opacityR/G/B/A`, defaulting to red on and green/blue/alpha off for the shipped four-channel map), `altitude`, `horizonFade` (Inspector `Fade`, this package's addition -- HDRP has no Fade on the 2D layer), `rotation`, `tint`, `exposure`, `distortionMode` (Wind), `scrollOrientation` / `scrollSpeed`, `lighting` (Raymarching), `steps`, `thickness` (Density) and `ambientProbeDimmer`.
  - `CloudGenerator.shader` replaces the transparent stub: equirectangular projection of each low-res texel's view ray onto the cloud map, multi-channel opacity blending, a bounded Beer-Lambert self-shadow march along `_SunDirection`, and an altitude-driven horizon fadeout. Output is premultiplied `half4(color * density, density)` for the existing skybox composite.
  - `CloudRenderPass` pushes the volume state each frame, resolves the built-in `DefaultCloudMap` when no map is assigned, accumulates the wind scroll in world metres, and binds the camera inverse projection / camera-to-world matrices for exact view rays.
  - Cloud in-scatter is driven by `TimeOfDay` sun/moon radiance, so the deck tracks the sky through the day/night ramp and sunset warmth; `exposureCompensation` (EV) is the artist's brightness knob.

- **`Moon Rise Fade` on Moon Settings, for the moon emerging at the horizon**:
  - The moon's disc appeared the frame its centre cleared the skyline. The only fade it carried was `saturate(1 - groundBlend * 2)`, and `groundBlend` is a *screen-space* term derived from the view ray: above the skyline it is `0`, so a disc one degree up rendered at full strength no matter how low the moon itself was. Atmospheric transmittance reddens and trims the disc but does not soften the appearance either.
  - `riseFade` (Inspector `Moon Rise Fade`) is the transition's width in degrees of the moon's **own** elevation, `0`-`45`, default `10`; `ProceduralSkyController` converts it once per frame to the shader's `sin(elevation)` units, exactly as it does for the cloud layer's `Fade`. The shader band is `smoothstep(0, max(_MoonRiseFade, 1e-5), AdjustedLightY(moonDir))` -- the same lifted elevation the transmittance already uses -- so the band starts at the moon's horizon and `0` is a hard edge. The mask fades with the disc, because the disc replaces the sky and a dimmed-but-still-masked moon would punch a hole where its disc is.
  - The default is `10` degrees rather than the cloud layer's `5`. The disc is `3.2 deg` wide at the default `moonSize`, so a moon that has "just appeared" is already several degrees up: measured at `4.2 deg` elevation, a `5 deg` band leaves the disc at `0.95` of full strength, i.e. no visible change, where `10` takes it to `0.41`. A `5 deg` band only reads as a transition in the last degree or two before the skyline.
  - Measured by differencing a moon-on and a moon-off render, which isolates the moon's own contribution from the sky behind it, with the camera aimed at `CurrentMoonDirection` so the disc is centred and `Wind` set to `None` so the deck does not drift between the two. Ratio of the faded disc to the unfaded one at the same elevation:

    | moon elevation | `Fade = 5` | `Fade = 10` | `Fade = 20` | expected at `10` |
    | --- | --- | --- | --- | --- |
    | `-0.25 deg` | `0.046` | `0.012` | `0.004` | `0.000` |
    | `+0.88 deg` | `0.104` | `0.028` | `0.007` | `0.022` |
    | `+2.01 deg` | `0.330` | `0.095` | `0.027` | `0.106` |
    | `+4.23 deg` | `0.949` | `0.412` | `0.131` | `0.388` |
    | `+5.87 deg` | `1.000` | `0.654` | `0.217` | `0.633` |
    | `+8.03 deg` | `1.000` | `0.902` | `0.345` | `0.900` |
    | `+10.15 deg` | `1.000` | `1.000` | `0.474` | `1.000` |
    | `+13.76 deg` | `1.000` | `1.000` | `0.769` | `1.000` |

    `Fade = 10` tracks the `smoothstep` to within a few hundredths. Unfaded, the same disc is already at full strength below the skyline, which is what the eye caught.
  - The CPU mirror (`ProceduralSkyRadiance`) carries the same band, so the baked ambient probe still reproduces the dome.

### Fixed
- **The moon was switched on instead of rising: the single key light teleported at dusk**:
  - With `CelestialLightingMode.Single` the one directional light is redirected from the sun to the moon by a bare `IsNight` test (`sunDir.y < -0.01`), so the swap landed between two frames at a point where the light was still lit. Measured with the default curve: at 19:00 the forward vector was `(0.886, 0.006, -0.464)`, the intensity `0.0788` and the colour the sunset ember `(1.00, 0.25, 0.08)`; at 19:02 it was `(-0.877, -0.129, 0.462)`, `0.0219` and pure white. A 173 degree teleport plus a colour jump is what the eye reads as the moon being switched on rather than rising.
  - The handover is now a fade **through zero**: the key light dims to nothing, swaps direction and colour there, then comes back up, so the swap happens where nothing is lit. It rides on `moonNightMask`, the weight the moon's own intensity already uses (`0` while the sun is up, `1` once night has fallen), so no second ramp or tunable is introduced. Both bodies are near zero inside that band anyway -- the sun's curve has run out and the moon's mask has not opened -- so the dip costs almost no light.
  - Measured after the change: the 173 degree flip now lands at `19:10` with the light's intensity at `0.0005` and `enabled = false` on the surrounding frames, against `0.0788 -> 0.0219` while lit before. The moon's own intensity curve was already smooth (checked frame by frame across `18:20`-`19:85`: no step, largest `0.0063` of a `0.25` maximum) and the dome's moon disc already carries an atmospheric transmittance, so this swap was the only hard cut in the moon's *light*. It was not the only hard cut in the moon's *appearance*: the disc itself had no rise fade at all, which is the `Moon Rise Fade` entry above.
- **Cloud Layer ignored the atmosphere: dusk clouds stayed a uniform salmon sheet and night clouds stayed uniformly lit**:
  - The deck's light was the raw sun/moon radiance, so it kept its daytime hue and a large part of its brightness while the sky around it went dark. HDRP multiplies the 2D cloud layer's sun colour by PhysicallyBasedSky's `EvaluateSunColorAttenuation`, and the same term is what makes a low sun's clouds embers: the column a low sun's light crosses extinguishes blue and green long before red. `ProceduralSkyRadiance.SunTransmittance` exposes the dome's own transmittance on the sun's path, and `CloudRenderPass` now filters the sun colour and the deck's fill with it. Under an `HDRI` sky there is no atmosphere to evaluate, so the light passes through untouched — matching HDRP, which only attenuates under PhysicallyBasedSky.
  - The fill a cloud base receives comes back off the ground and the lower atmosphere, i.e. it is the same beam after one more leg, so it is filtered identically. Left unfiltered it kept a neutral grey while the key light turned red, which desaturated the deck exactly when it should be at its most saturated. Measured at 18:39: green/red of the deck's total light went `0.28 -> 0.24` and blue/red `0.20 -> 0.10`, against `0.12` / `0.06` measured off HDRP's reference frame; noon moves by 2.5% so the calibrated daylight look is unchanged.
  - **The moon marched the sun's light path.** The moon term fed `CloudLuminance` the optical depth computed along `_SunDirection`, so at night — when the sun is below the deck's horizon and its ray finds no cloud to extinguish — the moon's `exp(-opticalDepth)` collapsed to `1` and every texel inside the moon's phase lobe came out at the same brightness. The deck was lit uniformly with no cloud shape in it, and no amount of tuning `Density` could bring the shape back. The march is now `CloudLightOpticalDepth`, called once per body; a body below the deck's horizon is skipped instead of marched.
  - Verified at 21:00 and 00:00 against the pre-change captures: the deck now reads as dark, moon-facing cloud with the shape of the map in it, instead of a flat sheet brighter than the sky it hides.

- **Cloud Layer read as a thin decal, not a shell with thickness**:
  - Every optical depth was taken from a **constant** chord of 1000 m, so the deck was equally dense at every view angle. HDRP takes it from a sphere intersection: the layer is a shell `800 m` deep riding a planet, and the chord grows from 800 m straight up to **29 km at the skyline** (1,000 m altitude, `R = 6371 km`). That 36x ratio is what makes a grazed deck turn solid while an overhead one stays translucent, and it is the single largest structural difference between a shell and a card.
  - `CloudShellChord` now intersects the shell per ray, using the `(a-b)(a+b)` form of `sqrt(a^2-b^2)` because at 6.37e6 the direct form spends nearly all of float32's mantissa on `a^2`. `CloudRenderPass` pushes `_CloudPlanetRadius` and `_CloudDeckThickness` (800 m, HDRP's `_HighestAltitude - _LowestAltitude`).
  - The scattering point is now placed at `0.5 * (1 - coverage)` of the way into the shell, matching HDRP's `currentPositionWS`, so a solid texel is shaded at its base and a wispy one near the middle. Combined with the chord this is what puts a lit rim under a dark top.
  - The light's own path is HDRP's `ExitCloudVolume` distance from that point, capped at 500 m, replacing an invented `0.5 * chord * coverage * (cos + sqrt(cos^2 + 3))` frustum that is not in HDRP at all.
  - The self-shadow march now steps in **metres** along `_SunDirection` and re-projects each sample, which is literally HDRP's `EvaluateSunLuminance`, instead of rotating a fixed 9-degree arc in direction space. Each sample applies HDRP's `GetDensity` band test (a sample only occludes when it falls inside `range * localThickness` of the shell's midplane); `sunOpticalDepth = stepSize * extinction * sigmaT`. The `kSunMarchSweep` constant is gone with the arc it measured.
  - Verified against HDRP's own units: `HDCamera.Set` does `radius = visualEnv.planetRadius.value * 1000`, so `_PlanetaryRadius` is 6,378,100 m and `altitude` (2000 m) is added in the same scale — the chord table above is what HDRP actually computes for the reference scene's defaults.
  - Measured at noon from one fixed camera: cloud-region p90 rose 109 -> 122 and the horizon band became a continuous deck instead of separated wisps, matching HDRP's solid skyline band.

- **Cloud Layer went black at sunset (the deck was gated on the observer's horizon, not its own)**:
  - The direct term was scaled by `sunUp = saturate(_SunDirection.y * 2.0)`, a hard fade to zero at the astronomical horizon. A deck at `altitude` rides a *smaller* sphere than the ground, so its own horizon dips below the observer's and the sun keeps lighting it after local sunset: at the default 2000 m the dip is 1.4 degrees. That lingering light is the entire reason sunset clouds glow, and the fade was extinguishing it — at 18:30 the deck received 10% of the sun, so dusk rendered as black silhouettes against a dim sky.
  - The gate is now the deck's own horizon, `saturate((sunDir.y - cosHorizon) * kSunHorizonSoftness)` with `cosHorizon = -sqrt(1 - (R / (R + altitude))^2)` computed in `CloudRenderPass` (HDRP's `ComputeCosineOfHorizonAngle` at the deck's radius) and pushed as `_CloudSunHorizonCos`. The ramp spans about one degree of sun elevation, so the terminator is soft instead of a step. At noon the term is unchanged at `1`, so the calibrated daylight deck does not move.
  - The moon shares the gate: a 2D layer has one terminator, and the moon lights the deck from slightly below the horizon too.
  - Verified by a time sweep from noon to night: the deck is lit through the whole sunset, peaks in warm orange around 18:30-18:50 with dark tops and glowing undersides, and goes dark only once the sun is 1.4 degrees below the skyline. Noon is unchanged.

- **Cloud Layer self-shadowing was numerically dead (deck rendered as a flat white stencil)**:
  - Four defects in the pseudo-volumetric march kept the shading term between `0.005` and `0.013` even at full strength, so the deck never showed lit tops or shaded bases. It now spans a usable range in directional patches, verified by rendering the shadow term itself as greyscale.
  - Optical depth is now driven by the **mean density along the reach** instead of `density * distance`. The march runs in panorama units (~`1e-2` per step), so the old product made Beer-Lambert attenuate by about one percent.
  - The march direction no longer uses the wind-scrolled uv. `_CloudWindOffset` slides the panorama past the camera and belongs in the sampled texel only; leaving it in the projected uv made the shadow trail rotate as the wind accumulated.
  - The march no longer walks a straight line in texture space. (It has since become HDRP's metre-based sun ray; see the shell entry above.) The old straight-line `toSun` delta and its `kUvAspect` metric are gone.

- **Cloud Layer sampling (vertical tear across the sky)**:
  - The deck was projected with a tangent-plane / altitude-shell mapping, but `DefaultCloudMap` is an equirectangular panorama (it wraps in `u` and does not wrap in `v`). The projection folded where the view ray grazed the deck, collapsing the sampled uv to a few texels on one side of the fold and jumping on the other, which rendered as a fixed vertical hard edge with a flat smear beside it.
  - The generator now projects each low-res texel's view ray directly (`u = atan2(dir.x, dir.z) / 2pi + 0.5`, `v = asin(dir.y) / (pi/2)`, `rotation` spinning the ray about the zenith). `v = 0` is the horizon and `v = 1` is the zenith, matching the shipped map's layout; the complement would place the featureless haze overhead and the detailed cumulus at the skyline, which reads as an upside-down deck. The `planetRadius` / shell-intersection code and its `_CloudPlanetRadius` uniform are removed. Verified in the Game view: no tear, no fold, a temporary lat/long grid renders evenly spaced longitudes converging at the zenith, and the deck shows sunlit cumulus tops overhead over a ~55% broken sky.

- **Cloud Layer self-shadow march tore a hard seam down the sky at the sun's antipode**:
  - The march walked a straight line in texture space, using the shortest longitude delta wrapped onto `[-0.5, 0.5)`. Azimuth is a circle, so that delta flips sign exactly at the sun's antipode: on one side the march sampled east, on the other west, and the shading stepped discontinuously along the whole antipodal meridian. Measured in the Game view, the seam landed at azimuth `279.2` degrees against a predicted `279.35` for the sun (`az 99.23`) — a match inside a tenth of a degree.
  - The march re-projects every step through `DirectionToCloudUV` rather than adding a uv delta, which is what HDRP does when it marches in world space. A direction is unique everywhere, so the branch disappears. The `kUvAspect` metric and the `toSun` / `toSunRaw` deltas are gone with it.
  - Verified by scanning for the largest vertical luminance step in the sky region: the antipode column dropped from `1258` (with the seam) to `310`, i.e. below the neighbouring cloud detail (`300`-`430`), and the seam stayed gone at camera yaw offsets of -10 and +10 degrees where it had measured `1144` and `1588`.

- **Cloud Layer had no volume: it rendered as a flat stencil.** The lighting model was inverted and missing its dominant term.
  - **The Henyey-Greenstein phase function was absent entirely.** It is the single largest contributor to a cloud reading as volume: HDRP sums a forward lobe (`g = 0.7`) and a weaker backwards lobe (`g = -0.3`), so a texel aimed at the sun scatters roughly 25x more than one aimed across it. Without it the whole deck was shaded by one number and could only ever look flat.
  - **The extinction response was backwards.** Optical depth was driven by the accumulated cloud density and multiplied *the whole direct term*, so the denser a texel was the darker it got. HDRP instead computes the light that survives the sun ray and scales it by `1 - T`, the deck's own transmittance, so a dense core is a *solid lit mass* and a wisp is see-through. That inversion is what made the deck read as a shadow map painted onto the sky.
  - **Multi-scattering was missing.** HDRP evaluates its scattering with two octaves at a `0.75` re-entry weight. The second octave is what keeps an optically thick core from collapsing to black; with a single octave the cloud bases went to near-zero and the deck looked cut out.
  - **The ambient term was a hardcoded `half3(0.05, 0.06, 0.08)`.** HDRP samples its ambient probe in the **down** direction — a cloud base is lit by whatever is below it — which `ambientProbeDimmer` then scales. The layer now evaluates the same sky probe `ProceduralSkyController` bakes each frame, so the shaded side tracks the sky instead of sitting at a fixed grey, and `ambientProbeDimmer` finally does what its name says.
  - `Density` (`thickness`) now maps to HDRP's extinction coefficient, `sigmaT = Density * (0.1 - 0.005) + 0.005`, and drives both optical depths; `Steps` subdivides the sun march. Both are live controls now instead of near-inert ones.

### Changed
- **Cloud Layer Inspector now reads 1:1 with HDRP's**:
  - Field names, labels, order, section headers and indentation were re-aligned with HDRP 17.3.0's Cloud Layer, so the two Inspectors can be read side by side: `Opacity / Upper Hemisphere Only / Resolution / Layer A / Cloud Map / Opacity R-G-B-A / Altitude / Rotation / Tint / Exposure Compensation / Wind / Orientation / Speed / Raymarching / Steps / Density / Ambient Probe Dimmer`, with `Orientation`+`Speed`, `Opacity R-G-B-A` and `Steps`+`Density`+`Ambient Probe Dimmer` one indent level in. The nesting uses core-RP's `[Header]` and `[Indent]` attributes the volume editor already renders, so no custom `VolumeComponentEditor` is shipped.
  - `Wind` is now HDRP's `CloudDistortionMode` (`None` / `Horizontal`), `Raymarching` is HDRP's `lighting` bool, and `Density` is HDRP's `thickness` (default `0.5`, was `0` — the raymarch was effectively off). `Exposure Compensation` is HDRP's `exposure`.
  - Rows HDRP shows that are **not** declared here, because the feature behind them does not exist: `Layers` (Single/Double — the component renders one layer), `Cast Shadows`, and the `Cloud Shadows` group (`Shadow Multiplier` / `Shadow Tint` / `Shadow Resolution` / `Shadow Size`, which drive the ground-projected cookie pipeline removed in `0f3bd18`). They are omitted rather than shipped as controls that do nothing.
  - The cloud-base shadow tint/intensity fields are gone. HDRP has no such knob on the layer: its raymarch is a pure Beer-Lambert transmittance that multiplies the **sun term only**, leaving the ambient term (which arrives from every direction) intact, so a shadowed base falls back to the ambient probe instead of going black. The shader now does the same, and `ambientProbeDimmer` scales that ambient term.
  - `enableClouds` is gone: activation is `VisualEnvironment.cloudType == CloudLayer`, matching HDRP, which has no second per-component switch. `CloudDistortionMode.None` now freezes the wind drift instead of being ignored.

- **Cloud Layer wind and horizon fade**:
  - Wind now drifts along the panorama's wrapping longitude axis only, so `windOrientation` is projected onto that axis; a cross-wind has no representable motion on an equirectangular layer. The accumulated offset is a scalar in metres, wrapped at `k_CloudMapTileSizeMeters` (now defined as the distance of one full revolution).
  - `altitude` no longer participates in the projection (a shell only exists for a planar deck) and instead sets the horizon fade band, `lerp(0.12, 0.02, saturate(altitude / 6000))`, so a low deck dissolves over a wider band and a high deck stays visible closer to the skyline.

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
