#ifndef MOEBIUS_SKYBOX_CLOUDS_INCLUDED
#define MOEBIUS_SKYBOX_CLOUDS_INCLUDED
float SkyEllipse(float2 p,float2 center,float2 radii)
{
    return (length((p-center)/radii)-1)*min(radii.x,radii.y);
}
float SkyCloudUnion(float a,float b,float softness)
{
    float h=saturate(0.5+0.5*(b-a)/softness);
    return lerp(b,a,h)-softness*h*(1-h);
}
float SkyBillow(float2 p,float2 center,float2 radii)
{
    float2 q=(p-center)/radii;
    float radius=length(q);
    float2 u=q/max(radius,0.0001);
    // Mixed angular frequencies make unequal rounded scallops around an authored
    // mass; they do not replace its silhouette with a thresholded noise field.
    float2 c2=float2(u.x*u.x-u.y*u.y,2*u.x*u.y);
    float2 c3=float2(c2.x*u.x-c2.y*u.y,c2.x*u.y+c2.y*u.x);
    float2 c5=float2(c3.x*c2.x-c3.y*c2.y,c3.x*c2.y+c3.y*c2.x);
    float2 c7=float2(c5.x*c2.x-c5.y*c2.y,c5.x*c2.y+c5.y*c2.x);
    float2 c11=float2(c7.x*(c2.x*c2.x-c2.y*c2.y)-c7.y*(2*c2.x*c2.y),
        c7.x*(2*c2.x*c2.y)+c7.y*(c2.x*c2.x-c2.y*c2.y));
    float scallop=(0.035+0.045*c7.x+0.023*c11.y+0.018*c3.y)
        *lerp(0.4,1.4,saturate(_CloudShapeVariation));
    return (radius-1-scallop)*min(radii.x,radii.y);
}
float SkyCloudShape(float2 p,float seed,out float shadeField,out float deepField)
{
    float variation=saturate(_CloudShapeVariation);
    // Warp the drawing coordinates gently instead of thresholding noisy distances.
    // This keeps scalloped edges connected and avoids tiny holes / detached specks.
    float2 original=p;
    p.x+=(PrintNoise(original*float2(7,5)+seed)-0.5)*0.025*variation;
    p.y+=(PrintNoise(original*float2(8,6)+seed+8)-0.5)*0.035*variation;
    // A tall off-centre crown, a lower shoulder and a thin trailing skirt.
    // These unequal overlapping billows define the drawing before noise is applied.
    // Seed changes the posture of each bank, rather than randomly rebuilding it in time.
    float posture=sin(seed*1.73)*variation;
    float2 crown=float2(0.27+posture*0.12,0.48+posture*0.12);
    float shape=SkyBillow(p,crown,float2(0.25,0.49));
    shape=SkyCloudUnion(shape,SkyBillow(p,crown+float2(-0.06,0.33),float2(0.17,0.26)),0.018);
    shape=SkyCloudUnion(shape,SkyBillow(p,crown+float2(-0.16,0.42),float2(0.09,0.16)),0.012);
    shape=SkyCloudUnion(shape,SkyBillow(p,crown+float2(-0.04,0.58),float2(0.105,0.13)),0.012);
    shape=SkyCloudUnion(shape,SkyBillow(p,crown+float2(0.10,0.43),float2(0.09,0.14)),0.012);
    shape=SkyCloudUnion(shape,SkyBillow(p,crown+float2(0.18,0.16),float2(0.16,0.26)),0.018);
    shape=SkyCloudUnion(shape,SkyBillow(p,crown+float2(-0.23,0.02),float2(0.16,0.26)),0.018);
    shape=SkyCloudUnion(shape,SkyBillow(p,crown+float2(0.28,-0.15),float2(0.18,0.25)),0.018);
    // Secondary tier. The smaller edge puffs create shoulders and notches, instead
    // of merely corrugating one large ellipse with high-frequency noise.
    shape=SkyCloudUnion(shape,SkyBillow(p,float2(-0.24,0.20),float2(0.27,0.32)),0.020);
    shape=SkyCloudUnion(shape,SkyBillow(p,float2(-0.24,0.43),float2(0.16,0.19)),0.015);
    shape=SkyCloudUnion(shape,SkyBillow(p,float2(-0.46,0.25),float2(0.14,0.18)),0.015);
    shape=SkyCloudUnion(shape,SkyBillow(p,float2(-0.59,0.03),float2(0.18,0.21)),0.018);
    shape=SkyCloudUnion(shape,SkyBillow(p,float2(0.72,0.04),float2(0.23,0.23)),0.018);
    shape=SkyCloudUnion(shape,SkyBillow(p,float2(0.88,-0.06),float2(0.17,0.13)),0.014);
    // Keep the belly solid even when the seed lifts the crown away from its skirt.
    // It is covered by the designed edge lobes, so it cannot produce a straight base.
    shape=SkyCloudUnion(shape,SkyEllipse(p,float2(0.10,0.01),float2(0.73,0.29)),0.020);
    // Overlapping lower billows retain a shallow, uneven underside. There is no
    // full-width ellipse or straight clipping plane to flatten all of their bases.
    float skirt=SkyBillow(p,float2(-0.62,-0.12),float2(0.29,0.12));
    skirt=SkyCloudUnion(skirt,SkyBillow(p,float2(-0.26,-0.11),float2(0.32,0.20)),0.018);
    skirt=SkyCloudUnion(skirt,SkyBillow(p,float2(0.17,-0.10),float2(0.32,0.16)),0.018);
    skirt=SkyCloudUnion(skirt,SkyBillow(p,float2(0.54,-0.12),float2(0.29,0.12)),0.018);
    shape=SkyCloudUnion(shape,skirt,0.018);
    shape=SkyCloudUnion(shape,SkyBillow(p,float2(-0.96,-0.14),float2(0.20,0.065)),0.012);
    // Front-facing volumes overlap the taller backlit crown. The two warm flat
    // color shapes follow the billow hierarchy, leaving broad, clean lit areas.
    shadeField=SkyCloudUnion(skirt,
        SkyBillow(p,float2(-0.34,-0.01),float2(0.24,0.16)),0.018);
    shadeField=SkyCloudUnion(shadeField,SkyBillow(p,crown+float2(0.14,-0.36),float2(0.25,0.23)),0.018);
    shadeField=SkyCloudUnion(shadeField,SkyBillow(p,crown+float2(0.03,-0.22),float2(0.12,0.17)),0.014);
    shadeField=SkyCloudUnion(shadeField,SkyBillow(p,crown+float2(0.22,-0.17),float2(0.12,0.13)),0.014);
    shadeField=SkyCloudUnion(shadeField,SkyBillow(p,float2(0.73,-0.02),float2(0.25,0.15)),0.015);
    deepField=SkyCloudUnion(SkyBillow(p,float2(-0.47,-0.16),float2(0.20,0.09)),
        SkyBillow(p,float2(0.04,-0.17),float2(0.25,0.08)),0.014);
    return shape;
}
half3 SkyClouds(half3 color,float3 direction)
{
    if (_EnableClouds<=0.5 || _CloudDensity<=0) return color;
    float time=_Time.y+_AnimationPhase;
    float2 wind=_CloudWindDirection.xy;
    wind=dot(wind,wind)>0.0001?normalize(wind):float2(1,0);
    float low=clamp(_CloudElevationRange.x,2,60), high=clamp(_CloudElevationRange.y,low+1,70);
    float2 horizontal=direction.xz*rsqrt(max(dot(direction.xz,direction.xz),0.0001));
    // Four sparse, composed banks. Height is relative to the world horizon, rather than
    // a tilted tangent plane: their bases stay horizontal as the camera turns.
    [unroll] for(int i=0;i<4;i++)
    {
        float presence=smoothstep(i/4.0,(i+0.25)/4.0,saturate(_CloudDensity));
        if(presence<=0) continue;
        float seed=PrintHash(float2(i,7.1))*19;
        float azimuth=radians(i*90+_CloudRotation+(PrintHash(float2(i,2))-0.5)*12+time*_CloudSpeed*wind.x);
        float elevation=radians(lerp(low,high,PrintHash(float2(i,4)))+sin(time*_CloudSpeed*0.0174533)*wind.y*4);
        float sa,ca;sincos(azimuth,sa,ca);
        float facing=dot(horizontal,float2(sa,ca));
        float width=lerp(0.23,0.34,PrintHash(float2(i,12)))*clamp(_CloudScale,0.2,2)*lerp(0.75,1.2,saturate(_CloudCoverage));
        float height=width*lerp(0.64,0.84,PrintHash(float2(i,16)));
        float2 p=float2(dot(horizontal,float2(ca,-sa))/width,(direction.y-sin(elevation))/height);
        float shadeField,deepField;
        float2 shapePoint=p;shapePoint.x*=(i & 1)==0?1:-1;
        float field=SkyCloudShape(shapePoint,seed,shadeField,deepField);
        float aa=max(fwidth(field),max(_CloudEdgeSoftness,0.0001));
        // Evaluate derivatives before the per-pixel hemisphere mask. Divergent loop exits
        // would make fwidth undefined and can produce unstable cloud edges on some GPUs.
        float visibility=presence*step(0.15,facing)*smoothstep(_HorizonHeight,_HorizonHeight+0.015,direction.y);
        float mask=(1-smoothstep(-aa,aa,field))*visibility;
        float shadeAA=max(fwidth(shadeField),0.002),deepAA=max(fwidth(deepField),0.002);
        float shade=(1-smoothstep(-shadeAA,shadeAA,shadeField))*0.65;
        shade+=(1-smoothstep(-deepAA,deepAA,deepField))*0.35;
        shade*=saturate(_CloudShadeStrength);
        half3 cloud=lerp(_CloudColor.rgb,_CloudShadeColor.rgb,shade);
        if(_CloudInkStrength>0)
        {
            float footprint=max(fwidth(field),0.0001);
            float inkWidth=footprint*max(_CloudInkWidth,0)*0.5;
            float outer=saturate((inkWidth+footprint*0.5-abs(field))/footprint);
            float ink=outer*saturate(_CloudInkStrength);
            cloud=lerp(cloud,_CloudInkColor.rgb,ink);
            // A short upper fold on the front volume, with faded ends. Never
            // outline every interior ellipse or close a circle inside the cloud.
            float shadePixel=max(fwidth(shadeField),0.0001);
            float crease=saturate(0.5+max(_CloudInkWidth,0)*0.5-abs(shadeField)/shadePixel);
            crease*=smoothstep(0.02,0.12,shapePoint.y);
            crease*=1-smoothstep(0.24,0.55,abs(shapePoint.x-0.30));
            cloud=lerp(cloud,_CloudInkColor.rgb,crease*saturate(_CloudInkStrength)*0.5);
        }
        color=lerp(color,cloud,mask);
        // Two light, uneven horizontal pen strokes beneath each bank, with open ends.
        float extent=1-smoothstep(0.65,1.25,abs(p.x+0.38));
        float stroke=abs(p.y+0.34+sin(p.x*2+seed)*0.008);
        float pixel=max(fwidth(p.y),0.0001);
        float shelf=saturate(0.8-stroke/pixel)*extent*visibility;
        color=lerp(color,_CloudColor.rgb,shelf*0.8);
        float underline=abs(p.y+0.35+sin(p.x*2+seed)*0.008);
        float underlineMask=saturate(0.55-underline/pixel)*extent*visibility;
        color=lerp(color,_CloudInkColor.rgb,underlineMask*_CloudInkStrength*0.45);
    }
    return color;
}
#endif
