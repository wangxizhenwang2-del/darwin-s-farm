#ifndef MOEBIUS_WATER_WAVES_INCLUDED
#define MOEBIUS_WATER_WAVES_INCLUDED
// x: height in world metres, yz: analytic dHeight/dWorldXZ.
float3 WaterWave(float2 p, float2 direction, float length, float speed, float amplitude, float phaseOffset)
{
    float frequency = TWO_PI/max(length,0.2);
    float phase = dot(p,direction)*frequency-WaterTime()*speed+phaseOffset;
    return float3(sin(phase)*amplitude,cos(phase)*amplitude*frequency*direction);
}
float3 WaterWaveShape(float2 p)
{
    if (_EnableWaves<=0.5 || _WaveHeight<=0) return 0;
    float2 direction = WaterDirection(_WaveDirection.xy);
    float2 secondary = float2(direction.x*0.6-direction.y*0.8,direction.x*0.8+direction.y*0.6);
    float3 wave = WaterWave(p,direction,_WaveLength,_WaveSpeed,_WaveHeight*0.6,0);
    wave += WaterWave(p,secondary,_WaveLength*0.57,_WaveSpeed*0.83,_WaveHeight*0.27,1.8);
    wave += WaterWave(p,float2(-direction.y,direction.x),_WaveLength*1.37,_WaveSpeed*0.61,_WaveHeight*0.13,3.7);
    if (_EnableShoreMask>0.5)
    {
        // Same attenuation in every pass; shore stays attached to the baked mean water level.
        float3 shore = WaterShoreData(p);
        float distance = shore.x;
        float fadeWidth = max(_ShoreWaveFade,0.05);
        float fade = smoothstep(0,fadeWidth,distance);
        float t = saturate(distance/fadeWidth);
        float fadeDerivative = 6*t*(1-t)/fadeWidth;
        wave.yz = wave.yz*fade + wave.x*fadeDerivative*shore.yz;
        wave.x *= fade;
    }
    // Only connected-river instances set these widths. Existing water defaults to zero.
    // Flatten the last few centimetres of the upper surface into the curved waterfall lip.
    if (any(_RiverWaveFadeWidths > 0))
    {
        float2 local = TransformWorldToObject(float3(p.x,GetObjectToWorldMatrix()._m13,p.y)).xz;
        float4 distances = float4(_RiverWaveFadeRect.w-local.y, _RiverWaveFadeRect.z-local.x,
            local.y-_RiverWaveFadeRect.y, local.x-_RiverWaveFadeRect.x);
        float2 axisX = TransformObjectToWorldDir(float3(1,0,0),true).xz;
        float2 axisZ = TransformObjectToWorldDir(float3(0,0,1),true).xz;
        [unroll] for (int edge=0; edge<4; edge++) if (_RiverWaveFadeWidths[edge]>0)
        {
            float width=max(_RiverWaveFadeWidths[edge],0.01);
            float t=saturate(distances[edge]/width);
            float fade=t*t*(3-2*t), derivative=6*t*(1-t)/width;
            float2 gradient=edge==0?-axisZ:edge==1?-axisX:edge==2?axisZ:axisX;
            wave.yz=wave.yz*fade+wave.x*derivative*gradient;
            wave.x*=fade;
        }
    }
    return wave;
}
struct WaterAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS : NORMAL;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct WaterVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    half3 normalWS : TEXCOORD1;
    float waveHeight : TEXCOORD2;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
WaterVaryings WaterVertex(WaterAttributes input)
{
    WaterVaryings output = (WaterVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input,output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    float3 p = TransformObjectToWorld(input.positionOS.xyz);
    float3 wave = WaterWaveShape(p.xz);
    p.y += wave.x;
    float3 normal = TransformObjectToWorldNormal(input.normalOS);
    output.normalWS = normalize(float3(normal.x-wave.y*normal.y,normal.y,normal.z-wave.z*normal.y));
    output.positionWS = p;
    output.positionCS = TransformWorldToHClip(p);
    output.waveHeight = wave.x;
    return output;
}
#endif
