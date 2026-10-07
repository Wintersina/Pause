// Skin palette remap for the engine exhaust (ExhaustRemap.cs sets these per
// renderer through a MaterialPropertyBlock; ExhaustRemap.RemapColor is the
// CPU mirror the tests check).
//
// The exhaust atlas is drawn in five flat colours per ship (outer, mid,
// core, dark, accent). Each texel is matched to those five by an
// inverse-distance weight (sharp: an exact palette texel takes its own
// shift, a filtered edge between two bands a blend of both), then moved by
// the weighted shift. Texels far from every palette colour (stray
// transparent fringe) fade out of the remap. _ExOn 0 returns the colour
// untouched, so a material without a property block draws as before.
#ifndef PAUSE_EXHAUST_REMAP_INCLUDED
#define PAUSE_EXHAUST_REMAP_INCLUDED

float _ExOn;
float4 _ExSrc0, _ExSrc1, _ExSrc2, _ExSrc3, _ExSrc4;
float4 _ExShift0, _ExShift1, _ExShift2, _ExShift3, _ExShift4;

// Fade-out distance (RGB) from the nearest palette colour.
#define EXHAUST_REMAP_REACH 0.45

float ExhaustRemapWeight(float3 c, float3 src, inout float nearest)
{
    float3 d = c - src;
    float d2 = dot(d, d);
    nearest = min(nearest, d2);
    float k = d2 + 1e-4;
    return 1.0 / (k * k);
}

float3 ExhaustRemap(float3 c)
{
    if (_ExOn < 0.5) return c;
    float nearest = 4.0;
    float w0 = ExhaustRemapWeight(c, _ExSrc0.rgb, nearest);
    float w1 = ExhaustRemapWeight(c, _ExSrc1.rgb, nearest);
    float w2 = ExhaustRemapWeight(c, _ExSrc2.rgb, nearest);
    float w3 = ExhaustRemapWeight(c, _ExSrc3.rgb, nearest);
    float w4 = ExhaustRemapWeight(c, _ExSrc4.rgb, nearest);
    float3 shift = w0 * _ExShift0.rgb + w1 * _ExShift1.rgb + w2 * _ExShift2.rgb
                 + w3 * _ExShift3.rgb + w4 * _ExShift4.rgb;
    float sum = w0 + w1 + w2 + w3 + w4;
    float reach = saturate(1.0 - sqrt(nearest) / EXHAUST_REMAP_REACH);
    return saturate(c + shift * (reach / sum));
}

#endif
