#ifndef MOEBIUS_WATERFALL_FOAM_INCLUDED
#define MOEBIUS_WATERFALL_FOAM_INCLUDED
float2 ImpactFlowDirection()
{
    float2 direction=_ImpactFlowDirection.xy;
    return dot(direction,direction)>0.0001?normalize(direction):float2(0,-1);
}
// A capsule distance follows the full width of the falling sheet, not an arbitrary UV ellipse.
float ImpactSegmentDistance(float2 worldXZ,float2 drift)
{
    float2 a=_ImpactPointA.xz+drift,b=_ImpactPointB.xz+drift;
    float2 ab=b-a,ap=worldXZ-a;
    float u=saturate(dot(ap,ab)/max(dot(ab,ab),0.0001));
    return length(ap-ab*u);
}
float ImpactAgeEnvelope(float age,float lifetime)
{
    float fade=clamp(_ImpactLifetimeFade,0.05,0.8)*lifetime;
    float birth=min(0.45,lifetime*0.15);
    return smoothstep(0,birth,age)*(1-smoothstep(lifetime-fade,lifetime,age));
}
float ImpactDistanceEnvelope(float distance)
{
    float maximum=max(_ImpactMaxDistance,0.1);
    return 1-smoothstep(maximum*(1-clamp(_ImpactDistanceFade,0.05,0.9)),maximum,distance);
}
float2 WaterfallImpactMasks(float3 positionWS)
{
    if(_EnableWaterfallImpact<0.5)return 0;
    float time=_Time.y+_ImpactPhase;
    float2 world=positionWS.xz,center=(_ImpactPointA.xz+_ImpactPointB.xz)*0.5;
    float2 p=world-center;
    float distance=ImpactSegmentDistance(world,0);
    float radius=max(_ImpactRadius,0.05);
    // This boundary lies beyond the fully faded fields, so no active pen edge crosses it.
    if(distance>max(_ImpactMaxDistance,radius*1.5)+0.7)return 0;
    float strength=saturate(_ImpactTurbulence);
    float2 flow=ImpactFlowDirection();
    float2 advected=p*max(_ImpactNoiseScale,0.05)-flow*time*max(_ImpactFoamSpeed,0)*0.28;
    float coarse=PrintNoise(advected);
    float rolling=PrintNoise(advected*1.7+float2(8.3,-time*0.09));
    float edge=distance-radius+(coarse-0.5)*radius*strength;
    float edgeAA=max(fwidth(edge),0.002);
    float zone=1-smoothstep(-edgeAA,edgeAA,edge);
    float threshold=lerp(0.78,0.3,saturate(_ImpactFoamDensity));
    float foamField=coarse*0.7+rolling*0.3;
    float foamAA=max(fwidth(foamField),0.01);
    float islands=smoothstep(threshold-foamAA,threshold+foamAA,foamField);
    float core=1-smoothstep(radius*0.15,radius*0.75,distance);
    float foam=zone*max(islands,core*smoothstep(0.43,0.56,rolling))*strength;
    float ink=saturate(1-abs(foamField-threshold)/max(foamAA*1.3,0.001))*zone*islands*_ImpactFoamInkStrength*strength;
    float interval=max(_ImpactSpawnInterval,0.1);
    int count=(int)clamp(round(_ImpactRippleCount),1,8);
    // The oldest wave is invisible before leaving the bounded queue, even with conflicting settings.
    float lifetime=min(max(_ImpactLifetime,0.2),interval*count);
    float latest=floor(time/interval);
    float ripples=0;
    [unroll] for(int i=0;i<8;i++)
    {
        if(i<count)
        {
            float id=latest-i;
            float age=time-id*interval;
            float envelope=ImpactAgeEnvelope(age,lifetime);
            float seed=PrintHash(float2(id,4.7))*37;
            float2 drift=flow*age*0.08;
            float radialDistance=ImpactSegmentDistance(world,drift);
            float waveRadius=radius*0.28+age*max(_ImpactRippleSpeed,0);
            float2 noisePoint=p*max(_ImpactRippleNoiseScale,0.05)+float2(seed,seed*0.73);
            float distortion=(PrintNoise(noisePoint)-0.5)*2*clamp(_ImpactDistortion,0,0.6);
            float field=radialDistance-waveRadius+distortion;
            float footprint=max(fwidth(field),0.001);
            float spatialFade=ImpactDistanceEnvelope(radialDistance);
            float arcScale=lerp(0.35,1.1,saturate(_ImpactArcVariation));
            float arcNoise=PrintNoise(p*arcScale+float2(seed*0.37,seed+3.9));
            float cutoff=lerp(0.08,0.72,saturate(_ImpactArcBreakup));
            float arcs=smoothstep(cutoff-0.09,cutoff+0.09,arcNoise);
            float variedWidth=lerp(0.65,1.3,PrintNoise(noisePoint*0.6+11.3));
            float width=max(_ImpactRippleWidth,0.001)*variedWidth*lerp(0.25,1,envelope*spatialFade);
            float coverage=min(width/footprint,saturate((width*0.5-abs(field))/footprint+0.5));
            coverage*=envelope*spatialFade*arcs*(1-smoothstep(0.12,0.45,footprint));
            ripples=max(ripples,coverage*saturate(_ImpactRippleIntensity));
        }
    }
    return float2(saturate(foam),saturate(max(ripples,ink)));
}
half3 ApplyWaterfallImpact(half3 waterColor,float3 positionWS)
{
    if(_EnableWaterfallImpact<0.5)return waterColor;
    float2 masks=WaterfallImpactMasks(positionWS);
    half3 color=lerp(waterColor,_ImpactFoamColor.rgb,masks.x);
    return lerp(color,_ImpactRippleColor.rgb,masks.y);
}
#endif
