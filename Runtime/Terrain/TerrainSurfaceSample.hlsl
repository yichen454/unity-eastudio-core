#ifndef EASTUDIO_TERRAIN_SURFACE_SAMPLE_INCLUDED
#define EASTUDIO_TERRAIN_SURFACE_SAMPLE_INCLUDED

#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

float3 EASTudioDecodeTerrainNormal(float2 encoded)
{
    float2 xy = encoded * 2.0 - 1.0;
    float3 normalWS = float3(xy, 1.0 - abs(xy.x) - abs(xy.y));
    if (normalWS.z < 0.0)
    {
        normalWS.xy = (1.0 - abs(normalWS.yx)) * float2(xy.x >= 0.0 ? 1.0 : -1.0, xy.y >= 0.0 ? 1.0 : -1.0);
    }
    return normalize(normalWS);
}

float EASTudioSampleTerrainHeight(UnityTexture2D heightmap, float2 uv)
{
    uint width, height;
    heightmap.tex.GetDimensions(width, height);
    float2 texel = saturate(uv) * float2(width - 1u, height - 1u);
    int2 first = int2(texel);
    int2 last = min(first + 1, int2(width - 1u, height - 1u));
    float2 blend = frac(texel);
    float a = UnpackHeightmap(heightmap.tex.Load(int3(first, 0)));
    float b = UnpackHeightmap(heightmap.tex.Load(int3(last.x, first.y, 0)));
    float c = UnpackHeightmap(heightmap.tex.Load(int3(first.x, last.y, 0)));
    float d = UnpackHeightmap(heightmap.tex.Load(int3(last, 0)));
    // Match the terrain mesh diagonal from the lower-left to the upper-right height sample.
    return blend.x >= blend.y
        ? a + (b - a) * blend.x + (d - b) * blend.y
        : a + (c - a) * blend.y + (d - c) * blend.x;
}

void TerrainSurfaceSample_float(
    float3 PositionWS,
    UnityTexture2D Surface,
    UnityTexture2D Detail,
    UnityTexture2D Heightmap,
    float3 TerrainPosition,
    float3 TerrainSize,
    float HasDetail,
    out float3 BaseColor,
    out float Smoothness,
    out float3 NormalWS,
    out float Occlusion,
    out float BlendPermission,
    out float HeightWS,
    out float InBounds)
{
    BaseColor = 0.0;
    Smoothness = 0.0;
    NormalWS = float3(0.0, 1.0, 0.0);
    Occlusion = 1.0;
    BlendPermission = 1.0;
    HeightWS = TerrainPosition.y;
    InBounds = 0.0;

    if (TerrainSize.x <= 0.0 || TerrainSize.z <= 0.0) return;

    float2 uv = (PositionWS.xz - TerrainPosition.xz) / TerrainSize.xz;
    InBounds = (uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0) ? 1.0 : 0.0;
    if (InBounds < 0.5) return;

    uint surfaceWidth, surfaceHeight;
    Surface.tex.GetDimensions(surfaceWidth, surfaceHeight);
    float2 surfaceUV = (uv * float2(surfaceWidth - 1u, surfaceHeight - 1u) + 0.5) / float2(surfaceWidth, surfaceHeight);
    float4 surface = SAMPLE_TEXTURE2D(Surface.tex, Surface.samplerstate, surfaceUV);
    BaseColor = surface.rgb;
    Smoothness = surface.a;

    float normalizedHeight = EASTudioSampleTerrainHeight(Heightmap, uv);
    HeightWS = TerrainPosition.y + normalizedHeight * TerrainSize.y * 2.0;

    if (HasDetail > 0.5)
    {
        uint detailWidth, detailHeight;
        Detail.tex.GetDimensions(detailWidth, detailHeight);
        float2 detailUV = (uv * float2(detailWidth - 1u, detailHeight - 1u) + 0.5) / float2(detailWidth, detailHeight);
        float4 detail = SAMPLE_TEXTURE2D(Detail.tex, Detail.samplerstate, detailUV);
        NormalWS = EASTudioDecodeTerrainNormal(detail.rg);
        Occlusion = detail.b;
        BlendPermission = detail.a;
    }
}

void TerrainLitBlend_float(
    float2 ObjectUV,
    float3 PositionWS,
    float3 ObjectNormalWS,
    UnityTexture2D BaseMap,
    float4 BaseColorTint,
    float ObjectSmoothness,
    float BlendDistance,
    float BlendStrength,
    UnityTexture2D TerrainSurface,
    UnityTexture2D TerrainDetail,
    UnityTexture2D TerrainHeightmap,
    float3 TerrainPosition,
    float3 TerrainSize,
    float HasDetail,
    out float3 BaseColor,
    out float Smoothness,
    out float3 NormalWS,
    out float Occlusion)
{
    float2 baseUV = BaseMap.GetTransformedUV(ObjectUV);
    float4 objectColor = SAMPLE_TEXTURE2D(BaseMap.tex, BaseMap.samplerstate, baseUV) * BaseColorTint;
    BaseColor = objectColor.rgb;
    Smoothness = ObjectSmoothness;
    NormalWS = normalize(ObjectNormalWS);
    Occlusion = 1.0;

    float3 terrainColor, terrainNormalWS;
    float terrainSmoothness, terrainOcclusion, blendPermission, terrainHeightWS, inBounds;
    TerrainSurfaceSample_float(PositionWS, TerrainSurface, TerrainDetail, TerrainHeightmap,
        TerrainPosition, TerrainSize, HasDetail, terrainColor, terrainSmoothness, terrainNormalWS,
        terrainOcclusion, blendPermission, terrainHeightWS, inBounds);

    float distance = max(BlendDistance, 1e-4);
    float heightAboveTerrain = max(0.0, PositionWS.y - terrainHeightWS);
    float contact = 1.0 - smoothstep(0.0, distance, heightAboveTerrain);
    float blend = saturate(contact * BlendStrength * blendPermission * inBounds);
    BaseColor = lerp(BaseColor, terrainColor, blend);
    Smoothness = lerp(Smoothness, terrainSmoothness, blend);
    if (HasDetail > 0.5)
    {
        NormalWS = normalize(lerp(NormalWS, terrainNormalWS, blend));
        Occlusion = lerp(Occlusion, terrainOcclusion, blend);
    }
}

#endif
