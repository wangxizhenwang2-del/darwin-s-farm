#ifndef MOEBIUS_LAVA_FLOW_INCLUDED
#define MOEBIUS_LAVA_FLOW_INCLUDED
float WaterTime() { return _Time.y+_AnimationPhase; }
float2 WaterDirection(float2 value)
{
    float magnitude=dot(value,value);
    return magnitude>0.0001?value*rsqrt(magnitude):float2(0,-1);
}
// Adapt the existing water advection utility; speed remains world metres/second.
#define _EnableFlow 1
#define _FlowStrength 1
#define _FlowScale max(_FlowDetailScale,0.01)
#define _FlowSpeed (_LavaFlowSpeed*max(_FlowDetailScale,0.01))
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterFlow.hlsl"
#undef _EnableFlow
#undef _FlowStrength
#undef _FlowScale
#undef _FlowSpeed
struct LavaFlow
{
    float heat,streak;
    float2 coordinates;
};
LavaFlow GetLavaFlow(float2 positionXZ)
{
    LavaFlow result;
    float2 p=WaterFlowCoordinates(positionXZ);
    float2 direction=WaterDirection(_FlowDirection.xy),side=float2(-direction.y,direction.x);
    float2 flow=float2(dot(p,side),dot(p,direction));
    float broad=PrintNoise(flow*float2(1.1,0.45));
    float secondary=PrintNoise(flow*float2(2.7,0.85)+float2(12.3,7.9));
    float bend=(PrintNoise(flow*float2(0.65,0.3)+28)-0.5)*_FlowDistortion;
    float streakDistance=abs(sin((flow.x+bend)*6.283185+sin(flow.y*1.3)*0.3));
    float aa=max(fwidth(streakDistance),0.01);
    result.streak=(1-smoothstep(0.10-aa,0.22+aa,streakDistance))*smoothstep(0.32,0.65,secondary);
    result.heat=saturate(0.55+(broad-0.5)*0.55+(secondary-0.5)*0.2+result.streak*0.16);
    result.coordinates=p;
    return result;
}
#endif
