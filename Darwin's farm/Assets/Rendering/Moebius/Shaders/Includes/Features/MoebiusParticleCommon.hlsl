#ifndef MOEBIUS_PARTICLE_COMMON_INCLUDED
#define MOEBIUS_PARTICLE_COMMON_INCLUDED
float ParticleBoundaryFade(float3 positionWS)
{
    float2 nearEdge = min(positionWS.xz - _BoundsXZ.xy, _BoundsXZ.zw - positionWS.xz);
    return smoothstep(0, max(_BoundaryFade, 0.001), min(nearEdge.x, nearEdge.y));
}
float ParticleIntersectionFade(ParticleVaryings input)
{
    if (_DepthFade < 0.5) return 1;
    float depth = SampleSceneDepth(GetNormalizedScreenSpaceUV(input.positionCS));
    float eyeDepth;
    if (unity_OrthoParams.w > 0.5)
    {
        #if UNITY_REVERSED_Z
        depth = 1 - depth;
        #endif
        eyeDepth = lerp(_ProjectionParams.y, _ProjectionParams.z, depth);
    }
    else eyeDepth = LinearEyeDepth(depth, _ZBufferParams);
    return saturate((eyeDepth - input.eyeDepth) / max(_IntersectionDistance, 0.001));
}
float2 ParticleCellUV(float2 uv)
{
    return frac(uv * float2(_AtlasColumns, _AtlasRows));
}
float2 ClampParticleCell(float2 uv, float2 offset)
{
    float2 count = max(float2(_AtlasColumns, _AtlasRows), 1);
    float2 cell = floor(uv * count);
    return (cell + clamp(frac(uv * count) + offset, 0.008, 0.992)) / count;
}
half ParticleOpacity(ParticleVaryings input)
{
    return saturate(input.color.a * _Tint.a * _Opacity) * ParticleBoundaryFade(input.positionWS);
}
#endif
