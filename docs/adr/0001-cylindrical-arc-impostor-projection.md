# ADR-0001: Cylindrical Arc Impostor as the Projection Model for the Billboard Baker

**Status**: Accepted  
**Date**: 2026-09-22  
**Context**: Billboard Baker tool design for `com.eastudio.core` Editor module.

## Context

The Billboard Baker tool must capture distant scene subjects (mountain ranges, large composite prop clusters) from a user-defined viewpoint and produce a runtime-efficient stand-in that remains visually convincing as the observer moves horizontally.

Three projection models were considered:

1. **Orthographic Flat Quad** — Captures with an orthographic camera; output is a single flat texture on a Quad. Eliminates near-far distortion but produces obvious "cardboard" parallax artefacts when the observer moves sideways, because the flat proxy cannot approximate the subject's horizontal angular spread.

2. **Perspective Flat Quad** — Captures with a perspective camera; output is a single flat texture on a Quad. More natural for close-range props (<50 m) but introduces significant edge distortion for subjects subtending >30° of horizontal angle, which is the norm for mountain ranges.

3. **Cylindrical Arc Impostor** — Captures with a perspective camera rotated in multiple Tile passes around the Capture Viewpoint; tiles are stitched into a wide Panoramic Strip Texture. The runtime proxy is a curved Arc Proxy Mesh whose radius matches the capture distance. The concave surface naturally compensates for the observer's lateral movement without requiring per-frame shader reprojection.

## Decision

Use the **Cylindrical Arc Impostor** model as the sole projection output of the Billboard Baker.

The Arc Proxy Mesh is generated procedurally at bake time from:
- `ArcRadius` = horizontal distance from Capture Viewpoint to Bake Subject centroid
- `ArcAngle` = Coverage Angle (auto-derived + user padding)
- `ArcHeight` = vertical world-space extent of the Bake Subject

UV coordinates are baked into the Arc Proxy Mesh vertices at generation time so the Impostor Material is a trivially simple Unlit sampler with no reprojection math.

Flat Quad output (orthographic or perspective) is not offered as a mode. If a subject subtends fewer than ~15° horizontally, the cylindrical arc degenerates to near-flat geometry and the difference is imperceptible.

## Consequences

**Positive**
- No cardboard parallax artefact for wide subjects (mountains subtending 90°–150°).
- Single material, single draw call per impostor at runtime.
- UV baking at generation time keeps the Impostor Material shader trivially simple (no shader-side reprojection).
- Scales correctly from small props (~15° arc) to full horizon panoramas (180° arc) without a mode switch.

**Negative / Trade-offs**
- Arc Proxy Mesh is not a Unity primitive; it must be generated procedurally and saved as a Mesh asset. This adds mesh generation complexity to the Editor tool.
- Bake time increases linearly with Tile Count (multi-pass capture). For a 120° coverage at 60° Tile Angle, this is 2 passes — acceptable.
- The Arc Proxy Mesh is only accurate when viewed from within a reasonable distance of the original Capture Viewpoint. Subjects visible from radically different azimuths (e.g. a mountain seen from both north and south) require separate bake sessions from each viewpoint.

## Alternatives Considered and Rejected

**Spherical Impostor (Amplify/SpeedTree style)**: Captures from many angles and selects the correct frame at runtime based on camera azimuth + elevation. Highly accurate for isolated objects (trees, rocks) but requires a complex runtime shader and significantly more texture memory. Overkill for large terrain features that are only ever seen from one general direction.

**Flat Orthographic Quad**: Simple to implement but fundamentally cannot represent a subject spanning >30° without visible warping and cardboard feel. Rejected as the primary mode.

