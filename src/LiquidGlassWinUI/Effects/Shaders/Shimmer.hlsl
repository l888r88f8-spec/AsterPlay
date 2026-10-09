// Optional pointer-following reflection for LiquidGlassBrush.
//
// This is a separate flattened pass after LiquidGlass.hlsl. The separation is a
// correctness requirement, not an overlay workaround: DWM silently discards the
// main liquid-glass fragment when extra reflection math pushes that fragment past
// its internal shader-linking budget. Only brushes with IsShimmerEnabled=true
// instantiate this pass.

Texture2D texture0;
SamplerState sampler0;

cbuffer ShimmerConstants : register(b0)
{
    float ShimmerX;          // offset  0: normalized local x [0, 1]
    float ShimmerY;          // offset  4: normalized local y [0, 1]
    float ShimmerStrength;   // offset  8: reflection intensity [0, 1]
    float ShimmerRadius;     // offset 12: horizontal radius in logical px
    float Dpr;               // offset 16: physical px per logical px
    float3 _pad;             // offset 20..28
};

export float4 FlattenSource(float4 sample0) { return sample0; }

float4 ShimmerCore(float2 uv, float4 samplerDataExt)
{
    float4 source = texture0.Sample(sampler0, uv);

    // The source is premultiplied. A transparent pixel must remain transparent,
    // otherwise this post-process would become a rectangular visual overlay.
    if (source.a <= 0.0001 || ShimmerStrength <= 0.0001)
        return source;

    float2 sizePx = max(samplerDataExt.xy, float2(1.0, 1.0));
    float radiusPx = max(ShimmerRadius * max(Dpr, 0.01), 1.0);
    float2 deltaPx = (uv - float2(ShimmerX, ShimmerY)) * sizePx;

    // Elliptical area light driven by one continuous falloff. Keeping the center
    // and halo on the same curve prevents a visible ring where separately shaped
    // core/halo/glint layers used to meet, and remains comfortably below DWM's
    // shader-linking budget.
    float2 haloUv = deltaPx / float2(radiusPx, radiusPx * 0.76);
    float haloDistance2 = dot(haloUv, haloUv);
    float falloff = saturate(1.0 - haloDistance2 / 1.65);
    float smoothBloom = falloff * falloff * (0.38 + 0.62 * falloff);
    float reflection = saturate(ShimmerStrength * 0.92 * smoothBloom);

    // White in premultiplied-alpha space is alpha.xxx, not 1.xxx.
    source.rgb = lerp(
        source.rgb,
        float3(source.a, source.a, source.a),
        reflection);
    return source;
}

export float4 PSBody(float2 uv, float4 ext)   { return ShimmerCore(uv, ext); }
export float4 PSBodyCC(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyCW(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyCM(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyWC(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyWW(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyWM(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyMC(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyMW(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyMM(float2 uv, float4 ext) { return ShimmerCore(uv, ext); }
export float4 PSBodyC(float2 uv, float4 ext)  { return ShimmerCore(uv, ext); }
export float4 PSBodyW(float2 uv, float4 ext)  { return ShimmerCore(uv, ext); }
export float4 PSBodyM(float2 uv, float4 ext)  { return ShimmerCore(uv, ext); }
