#ifndef MOEBIUS_WATER_RIPPLES_INCLUDED
#define MOEBIUS_WATER_RIPPLES_INCLUDED
float WaterRippleStroke(float2 p, float2 cell, float2 pixelDX, float2 pixelDY, float density)
{
    // Cell identity belongs to a persistent stroke, never to the currently sampled pixel.
    float2 local = p-(cell+0.5);
    if (any(abs(local)>0.75)) return 0;
    float seed = PrintHash(cell);
    float center = (PrintHash(cell+31.8)-0.5)*0.46;
    float bend = sin(local.x*9+seed*6.28)*_RippleDistortion*0.09;
    float bendSlope = cos(local.x*9+seed*6.28)*_RippleDistortion*0.81;
    // Per-stroke continuous shape motion, independent of flow/drift and without frame reseeding.
    if (_RippleWobbleStrength>0)
    {
        float phase = WaterTime()*max(_RippleWobbleSpeed,0)*(0.85+seed*0.3)+seed*6.28;
        float amplitude = clamp(_RippleWobbleStrength,0,0.06);
        float a = local.x*13+phase;
        float b = local.x*21-phase*0.73+seed*17;
        bend += amplitude*(0.65*sin(a)+0.35*sin(b));
        // Match the animated curve's derivative so subpixel coverage remains stable.
        bendSlope += amplitude*(8.45*cos(a)+7.35*cos(b));
    }
    float penDistance = local.y-center-bend;
    float aa = max(abs(pixelDX.y-bendSlope*pixelDX.x)+abs(pixelDY.y-bendSlope*pixelDY.x),0.0001);
    float width = max(_RippleWidth,0.001)*density;
    // Integrate the thin stroke across the pixel footprint. Its coverage transfers
    // continuously to adjacent pixels, even when the physical line is subpixel.
    float low = penDistance-aa*0.5, high = penDistance+aa*0.5;
    float stroke = saturate((min(high,width*0.5)-max(low,-width*0.5))/aa);
    float length = clamp(_RippleLength,0.05,0.92)*(0.55+seed*0.45);
    float endAA = max(abs(pixelDX.x)+abs(pixelDY.x),0.0001);
    stroke *= 1-smoothstep(length*0.5-endAA,length*0.5+endAA,abs(local.x));
    return stroke*step(0.38,seed);
}
float WaterRippleMask(float2 flow)
{
    if (_EnableRipples<=0.5 || _RippleStrength<=0) return 0;
    float2 direction = WaterDirection(_FlowDirection.xy);
    float2 p = float2(dot(flow,direction),dot(flow,float2(-direction.y,direction.x)));
    p.y -= WaterTime()*_RippleSpeed;
    float density = max(_RippleDensity,0.05);
    p *= float2(density*0.6,density);
    float2 pixelDX = ddx(p), pixelDY = ddy(p);
    float2 cell = floor(p-0.5);
    // Evaluate neighboring persistent strokes: their antialiasing footprint may
    // cross a cell boundary without disappearing or switching seed at that boundary.
    float stroke = WaterRippleStroke(p,cell,pixelDX,pixelDY,density);
    stroke = max(stroke,WaterRippleStroke(p,cell+float2(1,0),pixelDX,pixelDY,density));
    stroke = max(stroke,WaterRippleStroke(p,cell+float2(0,1),pixelDX,pixelDY,density));
    stroke = max(stroke,WaterRippleStroke(p,cell+1,pixelDX,pixelDY,density));
    float visibility = 1-smoothstep(0.12,0.5,max(abs(pixelDX.x)+abs(pixelDY.x),abs(pixelDX.y)+abs(pixelDY.y)));
    return stroke*visibility*saturate(_RippleStrength);
}
#endif
