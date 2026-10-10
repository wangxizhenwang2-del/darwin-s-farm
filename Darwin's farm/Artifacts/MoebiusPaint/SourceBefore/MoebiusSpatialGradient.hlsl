#ifndef MOEBIUS_SPATIAL_GRADIENT_INCLUDED
#define MOEBIUS_SPATIAL_GRADIENT_INCLUDED
half3 ApplySpatialColorGradient(half3 baseColor, half3 albedoSample, float3 positionWS)
{
    if(_EnableSpatialGradient <= 0.5 || _SpatialGradientStrength <= 0) return baseColor;
    float3 axis = _SpatialGradientPointB.xyz-_SpatialGradientPointA.xyz;
    float lengthSquared = dot(axis,axis);
    if(lengthSquared <= 0.000001) return baseColor;
    float t = dot(positionWS-_SpatialGradientPointA.xyz,axis)/lengthSquared;
    float width = clamp(_SpatialGradientWidth,0.01,1);
    float mask = smoothstep(0.5-width*0.5,0.5+width*0.5,saturate(t));
    half3 gradientColor = lerp(_SpatialGradientColorA.rgb,_SpatialGradientColorB.rgb,mask);
    // Shared color field replaces the material color at strength 1, preserving the albedo texture.
    // Multiplying each material's original BaseColor would retain its hard color discontinuity.
    return lerp(baseColor,albedoSample*gradientColor,saturate(_SpatialGradientStrength));
}
#endif
