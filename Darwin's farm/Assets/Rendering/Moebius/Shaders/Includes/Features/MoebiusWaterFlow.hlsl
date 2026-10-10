#ifndef MOEBIUS_WATER_FLOW_INCLUDED
#define MOEBIUS_WATER_FLOW_INCLUDED
float2 WaterFlowCoordinates(float2 positionXZ)
{
    float2 p = positionXZ*max(_FlowScale,0.01);
    if (_EnableFlow<=0.5 || _FlowStrength<=0) return p;
    p -= WaterDirection(_FlowDirection.xy)*WaterTime()*_FlowSpeed*saturate(_FlowStrength);
    float2 distortion = float2(PrintNoise(p*0.65),PrintNoise(p*0.65+19.7))-0.5;
    return p+distortion*_FlowDistortion*saturate(_FlowStrength);
}
#endif
