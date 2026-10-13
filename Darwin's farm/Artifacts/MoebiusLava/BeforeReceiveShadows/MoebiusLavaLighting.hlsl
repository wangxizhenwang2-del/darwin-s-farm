#ifndef MOEBIUS_LAVA_LIGHTING_INCLUDED
#define MOEBIUS_LAVA_LIGHTING_INCLUDED
// Shared NPR adapter: lava uses a heat palette, with fixed restrained crust-light bands.
// The existing module is included unchanged; unused ramp/specular paths compile away.
#define _EnableSpatialGradient 0
#define _SpatialGradientStrength 0
#define _EnableColorRamp 0
#define _RampStrength 0
#define _RampShadowThreshold 0.4
#define _RampLightThreshold 0.55
#define _RampShadowColor half4(1,1,1,1)
#define _RampLightColor half4(1,1,1,1)
#define _ShadowColor half4(0.76,0.80,0.86,1)
#define _DeepShadowColor half4(0.64,0.67,0.76,1)
#define _HighlightColor half4(0.56,0.48,0.42,1)
#define _ShadowThreshold 0.45
#define _DeepShadowThreshold 0.12
#define _BrightThreshold 0.8
#define _ShadowSoftness 0.025
#define _SurfaceTintStrength (_NPRLightingInfluence*0.3)
#define _CastShadowTintStrength 0.25
#define _UseRamp 0
TEXTURE2D(_LightingRamp); SAMPLER(sampler_LightingRamp);
#define _HighlightStrength 0
#define _HighlightThreshold 0.96
#define _HighlightSize 0
#define _HighlightNoiseStrength 0
#define _HighlightViewInfluence 0
#define _HighlightInkWidth 0
#define _HighlightInkStrength 0
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNPRLighting.hlsl"
#define _UseHatchTexture 0
#define _HatchContrast 1
#define _HatchThreshold 0.72
#define _HatchThirdStrength 0.08
#define _CastShadowHatchStrength 0.7
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNPRHatching.hlsl"
#define _StippleThreshold 0.45
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNPRDetail.hlsl"
half3 ShadeMoebiusLava(LavaVaryings input,LavaPattern pattern,LavaFlow flow)
{
    half4 control=1;
    if(_UseControlMap>0.5)control=SAMPLE_TEXTURE2D(_ControlMap,sampler_ControlMap,input.uv);
    half3 vertex=lerp(half3(1,1,1),input.color.rgb,saturate(_UseVertexColor));
    half3 crust=lerp(_CrustColor.rgb,_SecondaryCrustColor.rgb,smoothstep(0.3,0.75,pattern.variation)*0.65);
    ToonLighting toon=GetToonLighting(input.positionWS,normalize(input.normalWS),crust,control.a*vertex.b);
    crust=toon.color;
    float hatch=0,stipple=0;
    if(_UseHatching>0.5)
    {
        float3 layers=SampleHatchLayers(pattern.stylePosition*max(_HatchScale,0.1));
        // Cooled rock retains a first pen layer in light; shadows progressively cross-hatch it.
        float brightness=lerp(0.42,toon.brightness*0.65,saturate(_NPRLightingInfluence));
        hatch=GetHatchMask(layers,brightness,control.r*control.g*vertex.r);
        hatch=max(hatch,GetCastShadowHatchMask(layers,toon.castShadowMask,control.r*control.g*vertex.r)*_HatchStrength);
        crust=lerp(crust,_InkColor.rgb,saturate(hatch));
    }
    if(_StippleStrength>0)
    {
        stipple=StipplePattern(pattern.stylePosition*max(_StippleScale,0.1))*_StippleStrength*control.g;
        crust=lerp(crust,_InkColor.rgb,saturate(stipple));
    }
    float heat=saturate((lerp(flow.heat,pattern.channelHeat,0.52)-0.5)*max(_HeatContrast,0.01)+0.5);
    float aa=max(fwidth(heat),0.018);
    float bands=smoothstep(0.25-aa,0.25+aa,heat)*0.45+smoothstep(0.72-aa,0.72+aa,heat)*0.55;
    float artisticHeat=lerp(heat,bands,saturate(_ColorBanding));
    float warm=smoothstep(0.1,0.5,artisticHeat);
    float hot=smoothstep(clamp(_HotRegionThreshold,0.1,0.98)-aa,clamp(_HotRegionThreshold,0.1,0.98)+aa,artisticHeat);
    half3 molten=lerp(_CoolLavaColor.rgb,_WarmLavaColor.rgb,warm);
    molten=lerp(molten,_HotLavaColor.rgb,hot);
    // Sparse gold tongues have a distinct advection speed from the crust boundary.
    molten=lerp(molten,_HotLavaColor.rgb,flow.streak*0.15*control.b*vertex.g);
    half3 color=lerp(molten,crust,pattern.crust);
    color=lerp(color,_InkColor.rgb,pattern.contour*saturate(_ContourStrength)*control.g);
    if(_EnableEmission>0.5&&_EmissionIntensity>0)
        color+=_HotLavaColor.rgb*max(_EmissionIntensity,0)*hot*pattern.molten*control.b*vertex.g;
    if(_DebugView==1)color=pattern.crust.xxx;
    if(_DebugView==2)color=pattern.molten.xxx;
    if(_DebugView==3)color=heat.xxx;
    if(_DebugView==4)color=half3(frac(flow.coordinates*0.25),flow.streak);
    if(_DebugView==5)color=(hatch*pattern.crust).xxx;
    if(_DebugView==6)color=(stipple*pattern.crust).xxx;
    if(_DebugView==7)color=pattern.contour.xxx;
    return color;
}
#endif
