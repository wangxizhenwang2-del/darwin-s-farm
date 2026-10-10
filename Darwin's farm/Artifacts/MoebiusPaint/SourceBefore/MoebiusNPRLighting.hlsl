#ifndef MOEBIUS_LIGHTING_INCLUDED
#define MOEBIUS_LIGHTING_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
struct ToonLighting
{
    float ndotl, brightness, shadowMask, deepShadowMask, castShadowMask;
    half3 color;
};
Light GetNPRMainLight(float3 positionWS)
{
    #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
    return GetMainLight(ComputeScreenPos(TransformWorldToHClip(positionWS)));
    #else
    return GetMainLight(TransformWorldToShadowCoord(positionWS));
    #endif
}
half3 ApplyLambertColorRamp(half3 baseColor, float ndotl)
{
    // Explicit spatial coloring takes precedence over the older light-dependent tint.
    if (_EnableSpatialGradient > 0.5 && _SpatialGradientStrength > 0) return baseColor;
    // Material-uniform bypass: disabled/zero-strength returns the original color directly.
    if (_EnableColorRamp <= 0.5 || _RampStrength <= 0) return baseColor;
    // Guard script/animation values as well as inspector input. Never give smoothstep equal/reversed bounds.
    float shadowThreshold = clamp(_RampShadowThreshold,0,0.999);
    float lightThreshold = clamp(_RampLightThreshold,shadowThreshold+0.001,1);
    float rampMask = smoothstep(shadowThreshold,lightThreshold,saturate(ndotl));
    half3 rampColor = lerp(_RampShadowColor.rgb,_RampLightColor.rgb,rampMask);
    half3 rampTint = lerp(half3(1,1,1),rampColor,saturate(_RampStrength));
    return baseColor*rampTint;
}
ToonLighting GetToonLighting(float3 positionWS, half3 normalWS, half3 baseColor, float shadowControl)
{
    Light mainLight = GetNPRMainLight(positionWS);
    ToonLighting result;
    // Saturated NdotL and realtime shadow attenuation drive discrete pastel bands.
    result.ndotl = saturate(dot(normalWS, mainLight.direction));
    baseColor = ApplyLambertColorRamp(baseColor,result.ndotl);
    result.brightness = result.ndotl * mainLight.shadowAttenuation;
    result.castShadowMask = (1 - mainLight.shadowAttenuation) * shadowControl;
    float softness = max(_ShadowSoftness, 0.0001);
    result.shadowMask = (1 - smoothstep(_ShadowThreshold-softness, _ShadowThreshold+softness, result.brightness)) * shadowControl;
    result.deepShadowMask = (1 - smoothstep(_DeepShadowThreshold-softness, _DeepShadowThreshold+softness, result.brightness)) * shadowControl;
    // Separate cast-shadow tint from surface orientation: ink carries the cast shadow,
    // while the receiving surface keeps a light paper-colored base underneath the lines.
    float colorBrightness = result.ndotl * lerp(1, mainLight.shadowAttenuation, _CastShadowTintStrength);
    float colorShadow = (1-smoothstep(_ShadowThreshold-softness, _ShadowThreshold+softness, colorBrightness))*shadowControl;
    float colorDeepShadow = (1-smoothstep(_DeepShadowThreshold-softness, _DeepShadowThreshold+softness, colorBrightness))*shadowControl;
    result.color = lerp(baseColor, baseColor * _ShadowColor.rgb, colorShadow*_SurfaceTintStrength);
    result.color = lerp(result.color, baseColor * _DeepShadowColor.rgb, colorDeepShadow*_SurfaceTintStrength);
    float bright = smoothstep(_BrightThreshold-softness, _BrightThreshold+softness, colorBrightness);
    result.color = lerp(result.color, lerp(baseColor, _HighlightColor.rgb, 0.08), bright*_SurfaceTintStrength);
    if (_UseRamp > 0.5)
        result.color = baseColor * SAMPLE_TEXTURE2D(_LightingRamp, sampler_LightingRamp, float2(colorBrightness, 0.5)).rgb;
    return result;
}
// x: white patch coverage, y: an analytic ink border. Real depth/normals stay untouched.
float2 GetStylizedHighlight(float3 positionWS, half3 normalWS)
{
    if (_HighlightStrength <= 0) return 0;
    Light light = GetNPRMainLight(positionWS);
    float3 halfVector = SafeNormalize(light.direction + GetWorldSpaceNormalizeViewDir(positionWS));
    float3 highlightDirection = SafeNormalize(lerp(light.direction,halfVector,_HighlightViewInfluence));
    // Object-space variation follows moving objects instead of swimming across their surface.
    float3 positionOS = TransformWorldToObject(positionWS);
    float irregularity = sin(positionOS.x * 31 + sin(positionOS.z * 19)) * sin(positionOS.y * 23);
    float value = dot(normalWS, highlightDirection) + irregularity * _HighlightNoiseStrength;
    float threshold = saturate(_HighlightThreshold - _HighlightSize * 0.1);
    float footprint = max(fwidth(value),0.0001);
    float fill = smoothstep(-0.5,0.5,(value-threshold)/footprint);
    float border = 1-smoothstep(max(0,_HighlightInkWidth*0.5-0.5),
        _HighlightInkWidth*0.5+0.5,abs(value-threshold)/footprint);
    border *= step(0.001,_HighlightInkWidth)*_HighlightInkStrength;
    float visibility = _HighlightStrength*light.shadowAttenuation*step(0.05,dot(normalWS,light.direction));
    return float2(fill,border)*visibility;
}
#endif
