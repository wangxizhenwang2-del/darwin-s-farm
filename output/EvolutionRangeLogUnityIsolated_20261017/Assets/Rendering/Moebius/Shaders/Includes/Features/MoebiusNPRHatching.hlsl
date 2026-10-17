#ifndef MOEBIUS_HATCHING_INCLUDED
#define MOEBIUS_HATCHING_INCLUDED
TEXTURE2D(_HatchTexture); SAMPLER(sampler_HatchTexture);
float2 RotateHatchUV(float2 uv, float angle)
{
    float s = 0, c = 1;
    sincos(radians(angle),s,c);
    return float2(c*uv.x-s*uv.y,s*uv.x+c*uv.y);
}
float HatchHash(float p)
{
    p = frac(p*0.1031); p *= p+33.33; p *= p+p;
    return frac(p);
}
float InkStripe(float2 uv)
{
    // Each line has its own offset/pressure. All variation is spatial, never per-frame noise.
    float row = floor(uv.y+0.5);
    float seed = HatchHash(row+13.7);
    float bend = 0.065*sin(uv.x*0.72+seed*6.283185)
        +0.022*sin(uv.x*2.3+row*1.7)+(seed-0.5)*0.16;
    float coordinate = uv.y + bend*_HatchIrregularity;
    float distanceToLine = abs(frac(coordinate+0.5)-0.5);
    // Measure derivatives on the continuous coordinate, not the row hash (which jumps between lines).
    float footprint = max(fwidth(uv.y),0.0001);
    float pressure = 1+_HatchIrregularity*(0.23*sin(uv.x*0.46+row*2.1)+(seed-0.5)*0.25);
    float halfWidth = min(0.2,0.5*_HatchLineWidth*footprint*pressure);
    float coverage = saturate((halfWidth-distanceToLine)/footprint+0.5);
    // Rare short lifts of the pen, mostly preserving continuous long strokes.
    float along = uv.x*0.13+seed*7;
    float segment = floor(along);
    float gap = smoothstep(0.90,0.94,frac(along))*(1-smoothstep(0.97,0.995,frac(along)));
    gap *= step(0.82,HatchHash(segment+row*37.1))*_HatchIrregularity;
    return coverage*(1-gap)*(1-smoothstep(0.38,0.75,footprint));
}
float3 SampleHatchLayers(float2 uv)
{
    uv = RotateHatchUV(uv,_HatchRotation);
    float3 layers = float3(0,0,0);
    if (_UseHatchTexture > 0.5)
        layers = saturate(1-SAMPLE_TEXTURE2D(_HatchTexture,sampler_HatchTexture,uv).rgb);
    else
        layers = float3(InkStripe(uv),InkStripe(RotateHatchUV(uv,68)+float2(11.3,7.1)),InkStripe(RotateHatchUV(uv,-38)));
    return pow(saturate(layers),max(_HatchContrast,0.01));
}
float3 GetTriplanarHatch(float3 positionWS, float3 normalWS)
{
    // Sharpen and normalize normal weights to avoid mesh UV stretching.
    float3 weights = pow(abs(normalWS),4);
    weights /= max(weights.x+weights.y+weights.z,0.0001);
    return SampleHatchLayers(positionWS.yz*_HatchScale)*weights.x
         + SampleHatchLayers(positionWS.xz*_HatchScale)*weights.y
         + SampleHatchLayers(positionWS.xy*_HatchScale)*weights.z;
}
float3 GetHatchLayers(float3 positionWS, float3 normalWS, float2 screenUV)
{
    #if defined(_USE_TRIPLANAR)
    return GetTriplanarHatch(positionWS,normalWS);
    #else
    float2 aspectUV = screenUV * float2(_ScaledScreenParams.x/_ScaledScreenParams.y,1);
    return SampleHatchLayers(aspectUV*_HatchScale);
    #endif
}
float3 GetHatchWeights(float brightness)
{
    // Three progressively deeper bands, with antialiased transitions rather than hard switches.
    // Threshold remains the artist's first-band control; the other bands follow its proportions.
    float softness = max(0.035,fwidth(brightness));
    float3 thresholds = _HatchThreshold*float3(1,2.0/3.0,1.0/3.0);
    return (1-smoothstep(thresholds-softness,thresholds+softness,brightness))
        *float3(1,_HatchCrossStrength,_HatchThirdStrength);
}
float CombineHatchLayers(float3 layers,float3 weights)
{
    float3 ink = saturate(layers*weights);
    return 1-(1-ink.r)*(1-ink.g)*(1-ink.b);
}
float GetHatchMask(float3 layers, float brightness, float artistMask)
{
    return saturate(CombineHatchLayers(layers,GetHatchWeights(brightness))*_HatchStrength*1.2)*artistMask;
}
float GetCastShadowHatchMask(float3 layers, float castShadowMask, float artistMask)
{
    // Cast-shadow penumbra stays single-direction; additional ink builds up in the core.
    float3 weights = float3(1,smoothstep(0.65,0.95,castShadowMask)*_HatchCrossStrength,
        smoothstep(0.9,1,castShadowMask)*_HatchThirdStrength);
    float ink = CombineHatchLayers(layers,weights);
    return ink*castShadowMask*_CastShadowHatchStrength*artistMask;
}
#endif
