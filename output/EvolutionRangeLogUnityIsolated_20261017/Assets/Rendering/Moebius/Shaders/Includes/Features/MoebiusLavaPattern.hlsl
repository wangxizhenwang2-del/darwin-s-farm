#ifndef MOEBIUS_LAVA_PATTERN_INCLUDED
#define MOEBIUS_LAVA_PATTERN_INCLUDED
struct LavaPattern
{
    float crust,molten,contour,channelHeat,variation;
    float2 stylePosition;
};
LavaPattern GetLavaPattern(float2 positionXZ)
{
    float size=max(_CrustScale,0.1),time=WaterTime();
    float motion=saturate(_CrustDeformationStrength);
    float2 p=(positionXZ-WaterDirection(_FlowDirection.xy)*time*_CrustDriftSpeed)/size;
    float2 warpTime=motion*time*float2(0.037,-0.026);
    float2 warp=float2(PrintNoise(p*0.72+warpTime),PrintNoise(p*0.72+warpTime+19.7))-0.5;
    p+=warp*max(_CrustDistortion,0)*1.5;
    float2 cell=floor(p),bestOffset=0;
    float first=100,second=100,bestSeed=0;
    // Bounded, deterministically animated sites keep the 3x3 nearest-neighbour search valid.
    // Different islands get independent phases; no random frame seeds or mask resets.
    [unroll] for(int y=-1;y<=1;y++)
    [unroll] for(int x=-1;x<=1;x++)
    {
        float2 id=cell+float2(x,y);
        float seed=PrintHash(id+float2(7.3,3.1));
        float2 jitter=float2(seed,PrintHash(id+23.7));
        float2 wobble=sin(time*float2(0.17+seed*0.10,0.13+seed*0.08)+jitter*6.283185)*motion*0.14;
        float2 delta=id+0.5+(jitter-0.5)*0.72+wobble-p;
        float distanceSquared=dot(delta,delta);
        if(distanceSquared<first)
        {
            second=first;first=distanceSquared;bestOffset=delta;bestSeed=seed;
        }
        else second=min(second,distanceSquared);
    }
    float gap=max(sqrt(second)-sqrt(first),0);
    float threshold=lerp(0.34,0.018,saturate(_CrustCoverage))+max(_CrackWidth,0)/size;
    threshold*=lerp(0.78,1.22,bestSeed);
    float aa=max(fwidth(gap),0.0001);
    float softness=max(max(_CrackSoftness,0)/size,aa);
    LavaPattern result;
    result.crust=smoothstep(threshold-softness,threshold+softness,gap);
    result.molten=1-result.crust;
    result.channelHeat=1-saturate(gap/max(threshold,0.001));
    float distanceToEdge=abs(gap-threshold)/aa;
    float width=max(_ContourWidth,0)*0.5;
    result.contour=(1-smoothstep(max(width-0.5,0),width+0.5,distanceToEdge))*step(0.001,_ContourWidth);
    result.contour*=1-smoothstep(0.12,0.4,aa);
    result.variation=saturate(bestSeed*0.7+PrintNoise(p*0.6+5.1)*0.3);
    // Pen marks follow the nearest drifting island; orientation stays fixed per island ID.
    result.stylePosition=bestOffset*size+float2(bestSeed*11.1,bestSeed*7.3);
    return result;
}
#endif
