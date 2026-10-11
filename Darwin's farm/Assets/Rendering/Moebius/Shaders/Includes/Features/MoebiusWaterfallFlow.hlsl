#ifndef MOEBIUS_WATERFALL_FLOW_INCLUDED
#define MOEBIUS_WATERFALL_FLOW_INCLUDED
float WaterfallTime() { return _Time.y + _AnimationPhase; }
float3 WaterfallDeform(float3 p, float2 uv)
{
    float t = WaterfallTime()*_SheetWobbleSpeed;
    float height = max(_SheetSize.y,0.01);
    float fade = saturate(1-pow(abs(p.y/height*2),4));
    p.x += sin(p.y*1.7+t)*_SheetWobbleStrength*fade;
    p.z += sin(p.y*2.1+p.x*1.3-t*0.8)*_SheetWobbleStrength*fade;
    return p;
}
float WaterfallEdgeDistance(float2 uv)
{
    float y = uv.y*max(_SheetSize.y,0.01)*max(_EdgeBreakupScale,0.01);
    float t = WaterfallTime()*_FlowSpeed*0.25;
    float left = PrintNoise(float2(2.3,y+t))*clamp(_EdgeBreakupStrength,0,0.15);
    float right = PrintNoise(float2(8.7,y+t))*clamp(_EdgeBreakupStrength,0,0.15);
    // Keep the upstream join full-width while retaining the hand-drawn sides below it.
    if(_LipStartV<0.999)
    {
        float join=1-smoothstep(0.65,1,saturate((uv.y-_LipStartV)/max(1-_LipStartV,0.001)));
        left*=join;right*=join;
    }
    return min(uv.x-left,1-uv.x-right);
}
void WaterfallClip(float2 uv,float3 positionOS) { clip(WaterfallEdgeDistance(uv)); }
float2 WaterfallFlowCoordinates(float2 uv)
{
    // UV V increases upwards: positive time makes visible marks move downwards.
    // Curved meshes use arc-length UVs; their path is longer than their vertical height.
    float length=_SurfaceLength>0?max(_SurfaceLength,0.01):max(_SheetSize.y,0.01);
    return float2(uv.x*max(_SheetSize.x,0.01),uv.y*length+WaterfallTime()*_FlowSpeed);
}
#endif
