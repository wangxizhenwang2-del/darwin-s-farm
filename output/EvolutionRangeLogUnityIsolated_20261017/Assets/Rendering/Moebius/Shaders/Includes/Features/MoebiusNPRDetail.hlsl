#ifndef MOEBIUS_DETAIL_INCLUDED
#define MOEBIUS_DETAIL_INCLUDED
float2 DotHash(float2 cell)
{
    float3 p = frac(float3(cell.xyx)*float3(0.1031,0.1030,0.0973));
    p += dot(p,p.yzx+33.33);
    return frac((p.xx+p.yz)*p.zy);
}
float StipplePattern(float2 uv)
{
    float2 cell = floor(uv);
    float2 random = DotHash(cell);
    float2 center = 0.2+random*0.6;
    float distanceToDot = length(frac(uv)-center);
    float footprint = max(length(fwidth(uv)),0.001);
    float radius = lerp(0.035,0.08,random.y);
    float dotMask = 1-smoothstep(radius-footprint,radius+footprint,distanceToDot);
    return dotMask * step(0.6,random.x) * (1-smoothstep(0.3,0.8,footprint));
}
float GetStippleMask(float3 positionWS, float3 normalWS, float2 screenUV, float brightness)
{
    float pattern;
    #if defined(_USE_TRIPLANAR)
    float3 weights = pow(abs(normalWS),4);
    weights /= max(weights.x+weights.y+weights.z,0.0001);
    pattern = dot(float3(StipplePattern(positionWS.yz*_StippleScale),
        StipplePattern(positionWS.xz*_StippleScale),StipplePattern(positionWS.xy*_StippleScale)),weights);
    #else
    pattern = StipplePattern(screenUV*float2(_ScaledScreenParams.x/_ScaledScreenParams.y,1)*_StippleScale);
    #endif
    return pattern * smoothstep(_StippleThreshold,_StippleThreshold+0.1,1-brightness) * _StippleStrength;
}
#endif
