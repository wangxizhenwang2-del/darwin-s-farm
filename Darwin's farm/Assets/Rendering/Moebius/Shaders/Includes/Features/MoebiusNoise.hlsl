#ifndef MOEBIUS_NOISE_INCLUDED
#define MOEBIUS_NOISE_INCLUDED
// Shared deterministic print noise. No camera coordinates or random frame seeds.
float PrintHash(float2 p)
{
    float3 v = frac(float3(p.xyx)*0.1031);
    v += dot(v,v.yzx+33.33);
    return frac((v.x+v.y)*v.z);
}
float PrintNoise(float2 p)
{
    float2 cell = floor(p), f = frac(p);
    f = f*f*(3-2*f);
    return lerp(lerp(PrintHash(cell),PrintHash(cell+float2(1,0)),f.x),
        lerp(PrintHash(cell+float2(0,1)),PrintHash(cell+1),f.x),f.y);
}
#endif
