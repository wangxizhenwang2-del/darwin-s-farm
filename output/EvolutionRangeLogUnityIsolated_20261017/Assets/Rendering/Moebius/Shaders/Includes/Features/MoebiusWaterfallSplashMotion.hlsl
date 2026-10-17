#ifndef MOEBIUS_WATERFALL_SPLASH_MOTION_INCLUDED
#define MOEBIUS_WATERFALL_SPLASH_MOTION_INCLUDED
float WaterfallTime(){return _Time.y+_AnimationPhase;}
float3 WaterfallDeform(float3 p,float2 uv)
{
    float t=WaterfallTime();
    float pulse=sin(t*_PulseSpeed);
    float amplitude=clamp(_PulseAmplitude,0,0.2);
    float gentle=sin(p.x*_DistortionScale+t*_WobbleSpeed)+0.35*sin(p.z*2.1-t*_WobbleSpeed*0.73);
    if(_SplashMotionRole<0.5)
    {
        // A planted base with a small upward pulse; no whole-mesh bouncing.
        float lift=smoothstep(-0.08,0.65,p.y);
        p.y+=pulse*amplitude*lift;
        p.x+=gentle*_WobbleStrength*lift*0.4;
        p.z+=sin(p.y*2.2+t*_WobbleSpeed*0.65)*_WobbleStrength*lift*0.25;
        return p;
    }
    if(_SplashMotionRole<1.5)
    {
        // Let the crest roll noticeably while keeping the submerged bottom planted.
        float raised=smoothstep(0,0.35,p.y);
        p.xz*=1+pulse*amplitude;
        p.y+=(pulse*amplitude*0.65+gentle*_WobbleStrength*0.7)*raised;
        p.z+=sin(p.x*1.7+t*_WobbleSpeed)*_WobbleStrength*raised*0.5;
        return p;
    }
    if(_SplashMotionRole<2.5)
    {
        // Hold the upstream join and drive the downstream edge, rather than translating the fan.
        float spreading=1-smoothstep(-0.75,0.45,p.z);
        p.x*=1+pulse*amplitude*spreading;
        p.z-=(sin(t*_PulseSpeed+0.6)*amplitude*1.8+gentle*_WobbleStrength*0.55)*spreading;
        p.x+=gentle*_WobbleStrength*spreading*0.4;
        float surface=smoothstep(-0.2,0.55,p.y);
        p.y+=sin(p.x*2.2+p.z*1.7+t*_WobbleSpeed)*_WobbleStrength*surface*0.35;
        return p;
    }
    // Preserve the original motion for previously authored splash materials.
    p.xz*=1+pulse*clamp(_PulseAmplitude,0,0.2);
    p.y*=1+pulse*_PulseAmplitude*0.45;
    float wobble=sin(p.x*_DistortionScale+t*_WobbleSpeed)+0.45*sin(p.y*3.1-t*_WobbleSpeed*0.73);
    p.x+=wobble*_WobbleStrength*0.45;
    p.y+=wobble*_WobbleStrength*0.35+sin(t*_PulseSpeed+1.2)*_PulseAmplitude*0.3;
    p.z+=sin(p.x*2.2+p.y*1.7+t*_WobbleSpeed*0.65)*_WobbleStrength*0.3;
    return p;
}
void WaterfallClip(float2 uv,float3 positionOS) {}
#endif
