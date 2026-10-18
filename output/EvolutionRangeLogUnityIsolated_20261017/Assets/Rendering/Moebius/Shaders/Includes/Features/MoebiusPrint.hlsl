#ifndef MOEBIUS_PRINT_INCLUDED
#define MOEBIUS_PRINT_INCLUDED
#include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNoise.hlsl"
float2 GetWobbledUV(float2 uv)
{
    #if defined(_USE_LINE_WOBBLE)
    float time = _Time.y;
    if (_WobbleFrameRate > 0) time = floor(time*_WobbleFrameRate)/_WobbleFrameRate;
    float2 p = uv * float2(_ScaledScreenParams.x/_ScaledScreenParams.y,1) * _LineWobbleScale;
    p += time*_LineWobbleSpeed;
    float2 noise = float2(PrintNoise(p),PrintNoise(p+float2(17.31,42.73))) * 2-1;
    // Pixel-sized offset only moves ink samples; the source image stays undistorted.
    uv += noise*_LineWobbleStrength/_ScaledScreenParams.xy;
    #endif
    return saturate(uv);
}
half3 GetPrintColor(half3 color, float2 screenUV)
{
    half luminance = dot(color,half3(0.2126,0.7152,0.0722));
    color = lerp(luminance.xxx,color,_Saturation);
    color = max((color-0.5)*_Contrast+0.5,0);
    if (_ColorLevels >= 2)
    {
        float levels = max(round(_ColorLevels)-1,1);
        // Quantize in perceptual display space; preserve the artist's material hues.
        color = SRGBToLinear(round(saturate(LinearToSRGB(color))*levels)/levels);
    }
    #if defined(_USE_PAPER)
    float2 aspectUV = screenUV*float2(_ScaledScreenParams.x/_ScaledScreenParams.y,1);
    float paper = SAMPLE_TEXTURE2D(_PaperTexture,sampler_PaperTexture,aspectUV*_PaperScale).r - 0.5;
    paper += (PrintNoise(aspectUV*_PaperScale*120)-0.5)*0.3;
    float grain = PrintHash(floor(screenUV*_ScaledScreenParams.xy))-0.5;
    color *= 1+paper*_PaperStrength+grain*_GrainStrength;
    #endif
    return color;
}
#endif
