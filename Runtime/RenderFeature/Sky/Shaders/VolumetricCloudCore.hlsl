#ifndef EASTUDIO_VOLUMETRIC_CLOUD_CORE_INCLUDED
#define EASTUDIO_VOLUMETRIC_CLOUD_CORE_INCLUDED

#ifndef PI
#define PI 3.14159265359
#endif

#ifndef TWO_PI
#define TWO_PI 6.28318530718
#endif

#ifndef INV_PI
#define INV_PI 0.31830988618
#endif

float smootherstep(float a, float b, float x)
{
    x = saturate((x - a) / max(0.0001, b - a));
    return x * x * x * (x * (x * 6.0 - 15.0) + 10.0);
}

float hash11(float n)
{
    return frac(sin(n) * 43758.5453);
}

float hash12(float2 p)
{
    return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453123);
}

float hash13(float3 p)
{
    return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453123);
}

// -------------------------------------------------------------------
// Enviro 3 Exact Remap Function
// -------------------------------------------------------------------
float RemapEnviro(float org_val, float org_min, float org_max, float new_min, float new_max)
{
    return new_min + saturate(((org_val - org_min) / max(0.00001, (org_max - org_min))) * (new_max - new_min));
}

// -------------------------------------------------------------------
// Enviro 3 Exact Phase Functions
// -------------------------------------------------------------------
float HenryGreensteinNorm(float cosTheta, float g)
{
    float g2 = g * g;
    return (1.0 - g2) / (4.0 * PI * pow(max(1.0 + g2 - 2.0 * g * cosTheta, 0.0001), 1.5));
}

float DualLobePhase(float cosTheta, float forwardG, float backwardG, float blendWeight)
{
    float hgForward = HenryGreensteinNorm(cosTheta, forwardG);
    float hgBackward = HenryGreensteinNorm(cosTheta, backwardG);
    return lerp(hgBackward, hgForward, blendWeight);
}

// -------------------------------------------------------------------
// Spherical Atmosphere Shell Ray Tracing
// -------------------------------------------------------------------
bool RayTraceSphere(float3 center, float3 rd, float3 ro, float radius, out float t1, out float t2)
{
    float3 p = ro - center;
    float b = dot(p, rd);
    float c = dot(p, p) - (radius * radius);
    float d = b * b - c;
    if (d >= 0.0)
    {
        float sqrtD = sqrt(d);
        t1 = -b - sqrtD;
        t2 = -b + sqrtD;
        return true;
    }
    t1 = 0.0;
    t2 = 0.0;
    return false;
}

bool ResolveRayStartEnd(float3 ro, float3 rd, float3 center, float innerRadius, float outerRadius, out float tStart, out float tEnd)
{
    tStart = 0.0;
    tEnd = 0.0;

    float ot1, ot2, it1, it2;
    bool outHit = RayTraceSphere(center, rd, ro, outerRadius, ot1, ot2);
    if (!outHit || ot2 <= 0.0)
        return false;

    bool inHit = RayTraceSphere(center, rd, ro, innerRadius, it1, it2);

    if (inHit)
    {
        if (it1 * it2 < 0.0) // Camera is below cloud deck
        {
            tStart = max(it2, 0.0);
            tEnd = ot2;
        }
        else if (ot1 * ot2 < 0.0) // Camera is inside cloud deck
        {
            tStart = 0.0;
            tEnd = (it2 > 0.0) ? it1 : ot2;
        }
        else // Camera is above cloud deck
        {
            if (ot1 < 0.0) return false;
            tStart = ot1;
            tEnd = it1;
        }
    }
    else
    {
        // Grazing ray through cloud layer above horizon
        tStart = max(ot1, 0.0);
        tEnd = ot2;
    }

    return (tEnd > tStart);
}

float CalculateHeightFraction(float3 pos, float3 center, float innerRadius, float thickness)
{
    float dist = length(pos - center);
    return saturate((dist - innerRadius) / max(thickness, 1.0));
}

// -------------------------------------------------------------------
// Enviro 3 Exact Vertical Profile & Height Gradients
// -------------------------------------------------------------------
float4 GetHeightGradientEnviro(float cloudType)
{
    // x,y = bottom smoothstep, z,w = top smoothstep
    const float4 CloudGradient1 = float4(0.0, 0.07, 0.08, 0.15); // Strato
    const float4 CloudGradient2 = float4(0.0, 0.20, 0.42, 0.60); // Cumulus
    const float4 CloudGradient3 = float4(0.0, 0.08, 0.75, 0.98); // Nimbus

    float a = 1.0 - saturate(cloudType * 2.0);          // 0→0.5 strato
    float b = 1.0 - abs(cloudType - 0.5) * 2.0;         // around 0.5 cumulus
    float c = saturate(cloudType - 0.5) * 2.0;          // 0.5→1 nimbus

    return CloudGradient1 * a + CloudGradient2 * b + CloudGradient3 * c;
}

float GradientStepEnviro(float a, float4 g)
{
    return smoothstep(g.x, g.y, a) - smoothstep(g.z, g.w, a);
}

float CloudVerticalShapingEnviro(float heightNorm, float bottomCtrl, float midCtrl, float topCtrl, float ramp)
{
    float2 bottomRange = float2(-0.1, 0.25); 
    float2 midRange    = float2(0.20, 0.45);
    float2 topRange    = float2(0.40, 0.80);

    float bottom = smoothstep(bottomRange.x, bottomRange.y, heightNorm) *
                   (1.0 - smoothstep(bottomRange.x + ramp, bottomRange.y + ramp, heightNorm));

    float mid    = smoothstep(midRange.x, midRange.y, heightNorm) *
                   (1.0 - smoothstep(midRange.x + ramp, midRange.y + ramp, heightNorm));

    float top    = smoothstep(topRange.x, topRange.y, heightNorm) *
                   (1.0 - smoothstep(topRange.x + ramp, topRange.y + ramp, heightNorm));

    float sum = bottom + mid + top + 1e-5;
    bottom /= sum;
    mid    /= sum;
    top    /= sum;

    float bias = bottomCtrl * bottom + midCtrl * mid + topCtrl * top;
    float envelope = 1.0 + bias * 0.5;

    // Bottom Round
    float edgeRound = 1.0 - exp(-heightNorm * 8.0); 
    envelope *= lerp(1.0, edgeRound, 0.9 * bottom);

    // Subtle round-top modifier
    float edgeRoundTop = 1.0 - exp(-(1.0 - heightNorm) * 8.0);
    envelope *= lerp(1.0, edgeRoundTop, 1.5 * top);

    if (heightNorm > 0.90)  
        envelope *= smoothstep(1.0, 0.88, heightNorm);

    return max(0.0, envelope);
}

// -------------------------------------------------------------------
// Enviro 3 Exact Energy / Lighting Model
// -------------------------------------------------------------------
struct EnviroLightParameters
{
    float scatteringCoef;        // e.g. 1.0 - 5.0
    float silverLiningIntensity; // e.g. 1.42
    float silverLiningSpread;    // e.g. 0.5
    float edgeHighlightStrength; // e.g. 0.0 - 1.0
    float multiScatterStrength;  // e.g. 0.54
    float multiScatterFalloff;   // e.g. 0.20
    float ambientFloor;          // e.g. 0.19
    float lightAbsorb;           // e.g. 0.61
    float exposure;              // e.g. 0.53 - 1.0
};

float SampleEnviroEnergy(
    float cosTheta,
    float sunHeight,
    float tau,
    EnviroLightParameters p)
{
    float sigma_t = max(p.lightAbsorb, 0.0005);
    float opticalD = sigma_t * tau;
    float horizonFactor = smoothstep(0.0, 0.3, sunHeight);

    // 1. Direct scattering
    float T = exp(-pow(opticalD, 1.05));
    float g = 0.9 - p.silverLiningSpread;
    float hgForward = HenryGreensteinNorm(cosTheta, g);
    float hgIso = 0.25 * INV_PI;
    float phase = lerp(hgIso + hgForward, hgForward, p.edgeHighlightStrength);
    float adaptiveDirect = lerp(p.silverLiningIntensity * 2.0, p.silverLiningIntensity * 0.5, horizonFactor);
    float direct = T * phase * p.scatteringCoef * adaptiveDirect * 5.0;

    // 2. Multi-scattering (indirect subsurface glow)
    float msBase = 1.0 - exp(-opticalD * 0.5);
    float adaptiveFalloff = lerp(p.multiScatterFalloff * 1.5, p.multiScatterFalloff, horizonFactor);
    float msFalloff = exp(-opticalD * adaptiveFalloff);
    float adaptiveScattering = lerp(p.multiScatterStrength * 4.0, p.multiScatterStrength, horizonFactor);
    float msEnergy = adaptiveScattering * msBase * msFalloff;
    float phaseMS = 0.25 * INV_PI;
    float indirect = msEnergy * phaseMS * p.scatteringCoef * 5.0;

    // 3. Backlit forward scatter boost
    float backlit = smoothstep(0.6, 1.0, cosTheta);
    direct *= lerp(1.0, 2.0, backlit);

    // 4. Ambient floor
    float ambientFloor = p.ambientFloor * (1.0 - exp(-opticalD * 0.1));

    // Combine
    float energy = direct + indirect + ambientFloor;
    return max(energy * p.exposure, 0.0);
}

#endif // EASTUDIO_VOLUMETRIC_CLOUD_CORE_INCLUDED
