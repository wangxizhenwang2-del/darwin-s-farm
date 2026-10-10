#ifndef MOEBIUS_BILLBOARD_STYLE_INCLUDED
#define MOEBIUS_BILLBOARD_STYLE_INCLUDED

float BillboardLuminance(half3 color)
{
    return dot(color, half3(0.2126, 0.7152, 0.0722));
}

float BillboardPaperHash(float2 pixel)
{
    float3 p = frac(float3(pixel.xyx) * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float BillboardInkDifference(half4 center, float2 uv)
{
    half4 neighbor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
    // Transparent RGB padding must not become an internal black edge.
    return abs(BillboardLuminance(center.rgb) - BillboardLuminance(neighbor.rgb))
        * step(_Cutoff, neighbor.a);
}

half3 ApplyBillboardMoebiusStyle(half4 character, float2 uv)
{
    [branch] if (_MoebiusStyleStrength <= 0) return character.rgb;
    float luminance = BillboardLuminance(character.rgb);
    half3 color = lerp(luminance.xxx, character.rgb, _StyleSaturation);
    float levels = max(_StyleColorLevels, 2);
    float band = floor(saturate(luminance) * (levels - 1) + 0.5) / (levels - 1);
    // Blend luminance bands instead of separately quantizing RGB hues.
    color *= lerp(1, clamp(band / max(luminance, 0.001), 0.2, 2), 0.38);
    color = lerp(color, color * _StylePaperColor.rgb, 0.18);

    float2 offset = _BaseMap_TexelSize.xy * max(_StyleInkWidth, 0);
    float edge = max(max(BillboardInkDifference(character, uv + float2(offset.x, 0)),
                         BillboardInkDifference(character, uv - float2(offset.x, 0))),
                     max(BillboardInkDifference(character, uv + float2(0, offset.y)),
                         BillboardInkDifference(character, uv - float2(0, offset.y))));
    float ink = smoothstep(_StyleInkThreshold, _StyleInkThreshold + 0.06, edge) * _StyleInkStrength;

    // UV-anchored lines and paper remain attached to the illustration when the camera moves.
    float hatchCoordinate = (uv.x + uv.y * 0.65) * max(_StyleHatchDensity, 1);
    float distanceToLine = abs(frac(hatchCoordinate) - 0.5);
    float hatchAA = max(fwidth(hatchCoordinate), 0.001);
    float hatchLine = 1 - smoothstep(0.07, 0.07 + hatchAA, distanceToLine);
    float darkMask = 1 - smoothstep(0.15, 0.55, luminance);
    float hatch = hatchLine * darkMask * _StyleHatchStrength;
    color = lerp(color, _StyleInkColor.rgb, saturate(ink + hatch));
    float paper = BillboardPaperHash(floor(uv * _BaseMap_TexelSize.zw)) - 0.5;
    color *= 1 + paper * _StylePaperStrength;
    return lerp(character.rgb, max(color, 0), saturate(_MoebiusStyleStrength));
}

#endif
