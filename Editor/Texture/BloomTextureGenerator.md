# Bloom Texture Generator

Open **Tools > EAStudio > 贴图 > Bloom Texture Generator**.

1. Select a straight-alpha Default, Sprite or GUI color texture. The tool reads its currently imported pixels without changing its importer or requiring Read/Write.
2. Set Threshold, Soft Knee, Intensity, Radius, Tint, Extra Padding, Output Mode and Export Exposure.
3. Live Preview refreshes automatically about 200 ms after the last parameter change. Inspect Composite, RGB and Alpha against checkerboard, black, white or a custom background. Disable Live Preview to work manually with Refresh Preview.
4. Adjust Preview Height (120–480 pixels, default 260) to keep the image within a bounded area. Click **Export Transparent PNG** and choose an existing folder under Assets after the current preview finishes refreshing.

## Canvas and transparency

Actual alpha values below one identify a transparent source; simply having an alpha channel does not. Transparent sources expand automatically by Radius + 1 pixels on every side when bloom has nonzero spatial spread. The original pixels stay centered without scaling. The extra pixel leaves a completely transparent outer border. Extra Padding adds further canvas space.

Opaque Composite output keeps its original dimensions and alpha one unless Extra Padding is added. Black pixels are never interpreted as transparency. A visible halo reaching the output edge triggers a warning. Halo Only output expands regardless of source opacity.

Radius is the exact finite support of a normalized, three-stage separable box blur, approximating a Gaussian profile. Three integer radii sum to Radius. With Radius zero there is no spatial spread and no automatic expansion. Transparent hidden RGB never contributes to bloom. Bloom coverage outside the original silhouette makes the halo visible through alpha blending.

The displayed Source UV Rect identifies the unchanged source image within the expanded output. Replacing a particle texture with a larger padded image changes its apparent size on a quad with unchanged UVs.

## Color and material contract

Processing uses linear floating-point, alpha-weighted RGB. The tool explicitly decodes sRGB data when the actual GPU format does not perform that conversion. Tint is authored as an sRGB color. Threshold and Soft Knee use linear brightness. Exposure uses EV and affects both source and bloom. PNG RGB is explicitly sRGB encoded; alpha is linear and straight (not premultiplied).

For linear bounded energy P, output alpha is max(source coverage, max(P)), or max(P) for Halo Only. Output RGB is P / alpha, encoded to sRGB. This makes decoded RGB times alpha reconstruct the bounded visible energy. Original alpha bytes are not guaranteed after adding bloom; fully opaque source pixels remain opaque. PNG is LDR: over-range energy clips to one, and a warning shows the affected pixel fraction. Faint tails may disappear during 8-bit quantization.

Use **SrcAlpha / OneMinusSrcAlpha** for ordinary transparency or **SrcAlpha / One** for additive effects. Do not multiply sampled RGB by alpha again before these blend states. **One / One** and premultiplied blend states require a different input convention. Alpha clipping removes soft tails.

Export imports RGBA32 with sRGB enabled, alpha from input, no alpha dilation, no compression, no mipmaps, no NPOT scaling and Clamp wrapping. Replacing an existing output requires confirmation and preserves its GUID; it resets platform texture overrides. Source overwrite is rejected. A failed export restores the previous image and metadata. Future compression or mip generation can change soft edges.

## Limits and verification

Requires compute shaders and RGBAFloat random-write textures. Radius supports 0–256 pixels; Extra Padding 0–4096 pixels per side; Exposure -16–16 EV. Working-memory estimate is capped at 512 MiB, including GPU buffers and CPU image staging. Unsupported dimensions are rejected instead of cropping or scaling. Source RGB must be finite and within +/-65504. Resident mip dimensions must match the imported texture dimensions. Input must use straight alpha; premultiplied input is outside this version's contract.

Bloom generation remains full resolution. Live refresh is debounced and runs without progress dialogs; manual refresh can be cancelled between dispatches. The display image is capped at 512 pixels on its longest side and fitted proportionally into the bounded preview area. Large images and radii can still delay the Editor because generation and GPU readback are synchronous. Preview, tests and compute behavior require verification on the target Unity Editor/GPU. Standalone C# API compilation is not proof of shader or visual correctness. Local tests are under Tests/Editor/Bloom (this package ignores Tests in Git).
