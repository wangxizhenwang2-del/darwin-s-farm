#ifndef MOEBIUS_WATERFALL_LINES_INCLUDED
#define MOEBIUS_WATERFALL_LINES_INCLUDED
float WaterfallPen(float distance,float footprint,float pixels)
{
    footprint=max(footprint,0.0001);
    return saturate((min(0.2,footprint*max(pixels,0)*0.5)-distance)/footprint+0.5)
        *(1-smoothstep(0.35,0.75,footprint));
}
float3 WaterfallLineMasks(float2 uv)
{
    float2 flow = WaterfallFlowCoordinates(uv);
    float density=max(_VerticalLineDensity,0.1);
    float x=flow.x*density;
    float y=flow.y*max(_FlowScale,0.01);
    float bend = (PrintNoise(float2(flow.x*max(_DistortionScale,0.01),y*0.65))-0.5)*_DistortionStrength;
    bend += sin(y*5.4+flow.x*3)*_DistortionStrength*0.22;
    x+=bend;
    float seed=PrintHash(float2(floor(x),7.1));
    float stroke=WaterfallPen(abs(frac(x)-0.5),fwidth(x),_VerticalLineWidth);
    float rhythm=smoothstep(0.25,0.65,PrintNoise(float2(seed*17,y*0.8)));
    float paleX=x*0.57+sin(y*2.1)*0.07;
    float pale=WaterfallPen(abs(frac(paleX)-0.5),fwidth(paleX),_VerticalLineWidth*1.7);
    pale*=smoothstep(0.35,0.7,PrintNoise(float2(floor(paleX)*2.1,y*0.6+11)));
    float band=smoothstep(0.48,0.6,PrintNoise(float2(flow.x*1.2,y*0.18)));
    return float3(stroke*lerp(0.35,1,rhythm),pale,band);
}
half3 WaterfallColor(float2 uv)
{
    float3 masks=WaterfallLineMasks(uv);
    half3 color=lerp(_BaseColor.rgb,_ShadowColor.rgb,masks.z*_BandStrength);
    color=lerp(color,_FlowLineColor.rgb,masks.y*_FlowLineStrength);
    color=lerp(color,_InkLineColor.rgb,masks.x*_InkLineStrength);
    float lip=smoothstep(0.93,0.99,uv.y);
    float impact=1-smoothstep(0.02,0.16,uv.y);
    float highlight=lip*0.7;
    float curve=0;
    if(_LipStartV<0.999)
    {
        // A narrow pale crest lies on the bend, rather than a bright seam at the flat join.
        curve=saturate((uv.y-_LipStartV)/max(1-_LipStartV,0.001));
        highlight=pow(saturate(sin(curve*PI)),3)*0.55;
    }
    color=lerp(color,_HighlightColor.rgb,max(highlight,impact*0.3));
    if(_LipStartV<0.999)color=lerp(color,_UpstreamColor.rgb,smoothstep(0.65,1,curve));
    if(_DebugView==1)color=half3(frac(WaterfallFlowCoordinates(uv)),0);
    if(_DebugView==2)color=masks.xxx;
    if(_DebugView==3)color=saturate(WaterfallEdgeDistance(uv)*10).xxx;
    return color;
}
#endif
