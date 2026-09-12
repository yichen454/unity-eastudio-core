#ifndef EASTUDIO_CLOUD_SHADOWS_INCLUDED
#define EASTUDIO_CLOUD_SHADOWS_INCLUDED

// =========================================================================================
// EAStudio Cloud Global Interface
// Reusable sampling utility for ground cloud shadows, prop lighting, and volumetric god rays
// =========================================================================================

TEXTURE2D(_CloudTexture);
// Note: sampler_LinearClamp is already provided globally by Core.hlsl / GlobalSamplers.hlsl

CBUFFER_START(EAStudioCloudShadowParams)
    float4 _CloudShadowParams; // x: Altitude, y: Scale, z: Strength, w: Enabled (1 or 0)
    float4 _CloudGlobalWindOffset;
    float4 _CloudGlobalSunDirection;
CBUFFER_END

/// <summary>
/// Sample the global real-time cloud shadow factor at any world space position.
/// Returns a multiplier in [1 - strength, 1.0] (1.0 = fully lit, lower = inside cloud shadow).
/// </summary>
half SampleCloudShadow(float3 positionWS)
{
    if (_CloudShadowParams.w < 0.5)
        return 1.0;

    float altitude = _CloudShadowParams.x;
    float scale = _CloudShadowParams.y;
    float strength = _CloudShadowParams.z;

    float3 sunDir = _CloudGlobalSunDirection.xyz;
    if (abs(sunDir.y) < 0.05)
        sunDir.y = 0.05 * (sunDir.y >= 0.0 ? 1.0 : -1.0);

    // Project world position upwards along sun light direction onto the cloud altitude plane
    float distToCloud = max(0.0, altitude - positionWS.y);
    float2 shadowWorldXZ = positionWS.xz - (sunDir.xz / sunDir.y) * distToCloud;

    // Convert to dome UV mapping
    float2 uv = shadowWorldXZ * scale * 0.0005 + _CloudGlobalWindOffset.xy;

    // Sample cloud opacity from Alpha channel using URP global linear sampler
    half cloudOpacity = SAMPLE_TEXTURE2D_LOD(_CloudTexture, sampler_LinearClamp, frac(uv), 0).a;

    // Attenuate light: 1.0 = no shadow, 1.0 - strength = full cloud shadow
    return lerp(1.0, 1.0 - strength, cloudOpacity);
}

#endif
