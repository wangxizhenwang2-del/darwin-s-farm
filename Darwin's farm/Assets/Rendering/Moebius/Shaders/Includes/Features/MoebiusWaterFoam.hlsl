#ifndef MOEBIUS_WATER_FOAM_INCLUDED
#define MOEBIUS_WATER_FOAM_INCLUDED
// x: cream fill, y: thin ink contour; no scene depth sampling.
float2 WaterFoamMask(float2 positionXZ, float distance)
{
    if (_EnableFoam<=0.5 || _EnableShoreMask<=0.5 || _FoamIntensity<=0) return 0;
    float2 p = positionXZ*max(_FoamNoiseScale,0.05);
    float time = WaterTime()*_FoamSpeed;
    float noise = PrintNoise(p+float2(time,-time*0.37));
    float fineNoise = PrintNoise(p*3.1+float2(-time*0.4,time*0.2));
    float irregular = (noise-0.5)*1.3+(fineNoise-0.5)*0.3;
    float width = max(_FoamWidth,0.01);
    float advance = _EnableShoreMotion>0.5 ? sin(time*1.8+PrintNoise(p*0.3)*3)*_ShoreMotionStrength : 0;
    float edge = distance-width*(1+irregular*_FoamIrregularity+advance);
    float aa = max(fwidth(edge),0.0001);
    float fill = 1-smoothstep(-aa*0.5,aa*0.5,edge);
    float ink = 1-smoothstep(max(0,_FoamInkWidth*0.5-0.5),_FoamInkWidth*0.5+0.5,abs(edge)/aa);
    ink *= _EnableFoamInk>0.5 ? saturate(_FoamInkStrength)*step(0.001,_FoamInkWidth) : 0;
    float patches = 0;
    if (_EnableFoamPatches>0.5 && _FoamPatchStrength>0)
    {
        float2 cell = floor(p*2.2);
        float2 local = frac(p*2.2)-0.5;
        float seed = PrintHash(cell+64.2);
        float radius = 0.065+seed*0.08;
        float d = length(local*float2(0.7,1.8));
        float patchAA = max(fwidth(d),0.001);
        patches = (1-smoothstep(radius-patchAA,radius+patchAA,d))*step(0.76,seed);
        patches *= smoothstep(width*0.8,width*1.3,distance)*(1-smoothstep(width*1.4,width*3.5,distance));
        patches *= _FoamPatchStrength;
    }
    return saturate(float2(max(fill,patches),ink)*_FoamIntensity);
}
#endif
