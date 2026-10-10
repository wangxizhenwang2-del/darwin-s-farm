#ifndef MOEBIUS_PAINT_COLOR_INCLUDED
#define MOEBIUS_PAINT_COLOR_INCLUDED

TEXTURE2D(_PaintColorMap); SAMPLER(sampler_PaintColorMap);

// Only the visible base color participates. All existing NPR masks remain independent.
half3 ApplyPaintedColor(half3 baseColor, float2 uv)
{
    [branch] if (_UsePaintColor < 0.5) return baseColor;
    half4 paint = SAMPLE_TEXTURE2D(_PaintColorMap, sampler_PaintColorMap, uv);
    if (paint.a <= 0) return baseColor;
    return lerp(baseColor,paint.rgb,saturate(paint.a));
}

#endif
