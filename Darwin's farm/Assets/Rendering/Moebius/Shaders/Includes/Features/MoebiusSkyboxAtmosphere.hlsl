#ifndef MOEBIUS_SKYBOX_ATMOSPHERE_INCLUDED
#define MOEBIUS_SKYBOX_ATMOSPHERE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
float SkyTransition(float altitude, float position, float softness)
{
    float value = smoothstep(position-softness,position+softness,altitude);
    if (_GradientSteps >= 2)
    {
        float levels = max(round(_GradientSteps)-1,1);
        float scaled = value*levels;
        float aa = max(fwidth(scaled),0.0001);
        value = (floor(scaled)+smoothstep(0.5-aa,0.5+aa,frac(scaled)))/levels;
    }
    return saturate(value);
}
half3 SkyHorizonColor()
{
    return lerp(_HorizonColor.rgb,unity_FogColor.rgb,saturate(_FogColorMatch));
}
half3 SkyGradient(float3 direction)
{
    float h = direction.y;
    float horizon = clamp(_HorizonHeight,-0.2,0.2);
    float lower = clamp(_LowerTransition,horizon+0.01,0.8);
    float middle = clamp(_MidTransition,lower+0.01,0.9);
    float upper = clamp(_ZenithTransition,middle+0.01,0.99);
    float softness = max(_TransitionSoftness,0.001);
    half3 horizonColor = SkyHorizonColor();
    half3 color = lerp(_BelowColor.rgb,horizonColor,
        smoothstep(horizon-max(_BelowFade,0.001),horizon,h));
    color = lerp(color,_LowerColor.rgb,SkyTransition(h,lower,softness));
    color = lerp(color,_MidColor.rgb,SkyTransition(h,middle,softness));
    color = lerp(color,_ZenithColor.rgb,SkyTransition(h,upper,softness));
    return color;
}
half3 SkyPigment(half3 color,float3 direction)
{
    // Smooth projections of a unit vector are continuous at every longitude and pole.
    // Static angular pigment follows the sky, not the camera position or frame number.
    if (_PigmentStrength > 0)
    {
        float scale = max(_PigmentScale,0.1);
        float pigment = (PrintNoise(direction.xy*scale+8.7)+PrintNoise(direction.yz*scale+31.2))*0.5-0.5;
        color *= 1+pigment*clamp(_PigmentStrength,0,0.1);
    }
    if (_PaperStrength > 0)
    {
        float paper = PrintNoise(direction.xz*85+direction.y*17.3)-0.5;
        color *= 1+paper*clamp(_PaperStrength,0,0.03);
    }
    return color;
}
float3 SkySunDirection()
{
    float3 direction = _SunDirection.xyz;
    if (_UseMainLightSun > 0.5)
    {
        float3 mainDirection = GetMainLight().direction;
        if (dot(mainDirection,mainDirection)>0.0001) direction=mainDirection;
    }
    return dot(direction,direction)>0.0001 ? normalize(direction) : normalize(float3(-0.4,0.45,0.8));
}
half3 SkySun(half3 color,float3 direction)
{
    if (_EnableSun <= 0.5) return color;
    float3 sun = SkySunDirection();
    // Chord distance avoids acos and remains well behaved at the disc centre.
    float distance = length(direction-sun);
    float radius = 2*sin(radians(clamp(_SunRadius,0.05,16))*0.5);
    float aa = max(fwidth(distance),radians(max(_SunSoftness,0.001)));
    float disc = 1-smoothstep(radius-aa,radius+aa,distance);
    float haloRadius = max(2*sin(radians(clamp(_HaloRadius,0.1,45))*0.5),radius+aa);
    float halo = 1-smoothstep(radius,haloRadius,distance);
    halo *= halo*saturate(_HaloStrength);
    float horizon = exp2(-abs(direction.y-_HorizonHeight)*18);
    float warmth = pow(saturate(dot(direction,sun)),8)*horizon*saturate(_SunHorizonInfluence);
    color = lerp(color,_HaloColor.rgb,saturate(halo+warmth));
    // A below-horizon sun is occluded by the illustrated horizon rather than showing through it.
    disc *= smoothstep(_HorizonHeight-0.01,_HorizonHeight+0.01,direction.y);
    float inkWidth=max(fwidth(distance),0.00001)*max(_SunInkWidth,0);
    float rim=(1-smoothstep(inkWidth,inkWidth+max(fwidth(distance),0.00001),abs(distance-radius)))*saturate(_SunInkStrength);
    // Wash the lower disc back into the atmosphere, like a printed sun behind a thin cloud shelf.
    float split=sun.y+radius*clamp(_SunHazeHeight,-0.9,0.9);
    float lower=1-smoothstep(split-aa,split+aa,direction.y);
    half3 discColor=_SunColor.rgb*max(_SunBrightness,0);
    discColor=lerp(discColor,color,lower*saturate(_SunHazeStrength));
    discColor=lerp(discColor,_SunInkColor.rgb,rim*lerp(1,0.35,lower));
    color=lerp(color,discColor,disc);
    float2 horizontal=direction.xz*rsqrt(max(dot(direction.xz,direction.xz),0.0001));
    float2 sunHorizontal=sun.xz*rsqrt(max(dot(sun.xz,sun.xz),0.0001));
    float facing=dot(horizontal,sunHorizontal);
    float x=dot(horizontal,float2(sunHorizontal.y,-sunHorizontal.x))/max(radius,0.001);
    float ribbonField=abs(direction.y-split+sin(x*2.2)*radius*0.003)-radius*clamp(_SunHazeWidth,0.001,0.08);
    float ribbonAA=max(fwidth(ribbonField),0.0001);
    float ends=smoothstep(-3.3,-2.1,x)*(1-smoothstep(2.3,3.6,x));
    float ribbon=(1-smoothstep(-ribbonAA,ribbonAA,ribbonField))*ends*step(0.2,facing);
    return lerp(color,_CloudColor.rgb,ribbon*saturate(_SunHazeStrength));
}
#endif
