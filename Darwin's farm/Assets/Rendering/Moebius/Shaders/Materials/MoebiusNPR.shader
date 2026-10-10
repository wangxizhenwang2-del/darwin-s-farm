Shader "Darwin/Moebius NPR"
{
    Properties
    {
        _BaseMap("Base Map", 2D) = "white" {}
        _BaseColor("Base Color", Color) = (0.79,0.83,0.69,1)
        _ShadowColor("Shadow Tint", Color) = (0.79,0.83,0.88,1)
        _DeepShadowColor("Deep Shadow Tint", Color) = (0.72,0.75,0.80,1)
        _HighlightColor("Highlight Tint", Color) = (1,0.99,0.94,1)
        _ShadowThreshold("Shadow Threshold", Range(0,1)) = 0.45
        _DeepShadowThreshold("Deep Shadow Threshold", Range(0,1)) = 0.12
        _BrightThreshold("Bright Threshold", Range(0,1)) = 0.8
        _ShadowSoftness("Band Softness", Range(0.001,0.2)) = 0.015
        [Toggle] _UseRamp("Use Lighting Ramp", Float) = 0
        _LightingRamp("Lighting Ramp (clamp)", 2D) = "white" {}
        _ControlMap("Control: R Hatch G Detail B Highlight A Shadow", 2D) = "white" {}
        [Toggle] _UseVertexColor("Vertex: R Hatch G Highlight B Shadow", Float) = 0
        [Enum(Composite,0,BaseColor,1,WorldNormal,2,NdotL,3,Shadow,4,DeepShadow,5,HatchR,6,HatchG,7,HatchB,8,CombinedHatch,9,Stipple,10,Highlight,11,CastShadow,12)] _DebugMode("Surface Debug", Float) = 0
        _InkColor("Hatch Ink", Color) = (0.12,0.10,0.09,1)
        [Toggle(_USE_HATCHING)] _UseHatching("Hatching", Float) = 0
        [Toggle(_USE_TRIPLANAR)] _UseTriplanar("World Triplanar (otherwise screen)", Float) = 0
        _HatchTexture("Packed RGB Hatch (white paper / dark lines)", 2D) = "white" {}
        [Toggle] _UseHatchTexture("Use Packed Texture (otherwise procedural)", Float) = 0
        _HatchScale("Hatch Tiles (screen height / world unit)", Float) = 125
        _HatchStrength("Hatch Strength", Range(0,1)) = 0.65
        _HatchContrast("Hatch Contrast", Range(0.2,4)) = 1
        _HatchThreshold("First Hatch Lighting Threshold", Range(0,1)) = 0.65
        _HatchRotation("Hatch Rotation Degrees", Range(-180,180)) = -25
        _HatchLineWidth("Pen Width (render pixels)", Range(0.35,2)) = 1
        _HatchIrregularity("Hand Drawn Stroke Variation", Range(0,1)) = 0.65
        _HatchCrossStrength("Second Stroke Strength", Range(0,1)) = 0.3
        _HatchThirdStrength("Deepest Shade Third Stroke Strength", Range(0,1)) = 0.12
        _PaperLift("Illustration Paper Lift", Range(0,0.3)) = 0.07
        _SurfaceTintStrength("Surface Solid Shading Strength", Range(0,1)) = 0.12
        _CastShadowHatchStrength("Cast Shadow Hatching (independent of Hatching toggle)", Range(0,1)) = 0.9
        _CastShadowTintStrength("Cast Shadow Solid Tint (0 = ink only, 1 = full tint)", Range(0,1)) = 0.2
        [Toggle(_USE_STIPPLE)] _UseStipple("Stippling", Float) = 0
        _StippleStrength("Stipple Strength", Range(0,1)) = 0.12
        _StippleScale("Stipple Tiles", Float) = 110
        _StippleThreshold("Stipple Darkness Threshold", Range(0,1)) = 0.8
        _HighlightThreshold("Highlight Threshold", Range(0,1)) = 0.96
        _HighlightSize("Highlight Size", Range(0,1)) = 0.1
        _HighlightStrength("Highlight Strength", Range(0,1)) = 0.85
        _HighlightNoiseStrength("Highlight Irregularity", Range(0,0.1)) = 0.012
        _HighlightViewInfluence("Highlight View Influence (0 = light anchored)", Range(0,1)) = 0
        _HighlightInkWidth("Highlight Border Width (render pixels)", Range(0,2)) = 0.7
        _HighlightInkStrength("Highlight Border Ink", Range(0,1)) = 0.8
        [Header(Moebius Lambert Color Ramp)]
        [Toggle] _EnableColorRamp("Enable Color Ramp", Float) = 0
        _RampShadowColor("Shadow Ramp Color", Color) = (0.94,0.92,1,1)
        _RampLightColor("Light Ramp Color", Color) = (1,0.98,0.94,1)
        [MoebiusRampThreshold(0)] _RampShadowThreshold("Shadow Threshold", Range(0,1)) = 0.4
        [MoebiusRampThreshold(1)] _RampLightThreshold("Light Threshold", Range(0,1)) = 0.55
        _RampStrength("Ramp Strength", Range(0,1)) = 0.35
        [Header(Moebius Spatial Color Gradient)]
        [Toggle] _EnableSpatialGradient("Enable Spatial Color Gradient", Float) = 0
        _SpatialGradientColorA("Color A", Color) = (0.95,0.52,0.38,1)
        _SpatialGradientColorB("Color B", Color) = (0.64,0.63,0.88,1)
        _SpatialGradientPointA("Point A (World Position)", Vector) = (-1,0,0,0)
        _SpatialGradientPointB("Point B (World Position)", Vector) = (1,0,0,0)
        _SpatialGradientWidth("Transition Width (fraction of A to B)", Range(0.01,1)) = 1
        _SpatialGradientStrength("Spatial Gradient Strength", Range(0,1)) = 1
        [Header(Moebius Painted Color)]
        [Toggle] _UsePaintColor("Use Painted Color", Float) = 0
        [NoScaleOffset] _PaintColorMap("Paint Color (RGB / Coverage A)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusNPRInput.hlsl"
        ENDHLSL
        Pass
        {
            Name "NPRForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex NPRVertex
            #pragma fragment NPRFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _USE_HATCHING
            #pragma shader_feature_local_fragment _USE_TRIPLANAR
            #pragma shader_feature_local_fragment _USE_STIPPLE
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNPRLighting.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNPRHatching.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusNPRDetail.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusSpatialGradient.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusPaintColor.hlsl"
            half4 NPRFragment(NPRVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 normalWS = normalize(input.normalWS);
                half3 albedoSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb;
                half3 baseColor = albedoSample * _BaseColor.rgb;
                baseColor = ApplySpatialColorGradient(baseColor,albedoSample,input.positionWS);
                baseColor = ApplyPaintedColor(baseColor,input.uv);
                // A little exposed paper lifts dark swatches without replacing the artist's palette.
                baseColor = lerp(baseColor,_HighlightColor.rgb,_PaperLift);
                half4 control = SAMPLE_TEXTURE2D(_ControlMap, sampler_ControlMap, input.uv);
                half3 vertexControl = lerp(half3(1,1,1), input.color.rgb, _UseVertexColor);
                ToonLighting toon = GetToonLighting(input.positionWS, normalWS, baseColor, control.a * vertexControl.b);
                half3 color = toon.color;
                float3 hatchLayers = 0;
                float hatchMask = 0;
                #if defined(_USE_HATCHING)
                hatchLayers = GetHatchLayers(input.positionWS,normalWS,input.positionCS.xy/_ScaledScreenParams.xy);
                hatchMask = GetHatchMask(hatchLayers,lerp(1,toon.brightness,control.a*vertexControl.b),control.r*control.g*vertexControl.r);
                // Cast shadows use their own progressive recipe below.
                hatchMask *= 1-toon.castShadowMask;
                #else
                // Uniform material branch keeps derivatives outside per-pixel shadow branches.
                if (_CastShadowHatchStrength > 0)
                    hatchLayers = GetHatchLayers(input.positionWS,normalWS,input.positionCS.xy/_ScaledScreenParams.xy);
                #endif
                float castHatchMask = GetCastShadowHatchMask(hatchLayers,toon.castShadowMask,control.r*control.g*vertexControl.r);
                hatchMask = 1-(1-hatchMask)*(1-castHatchMask);
                color = lerp(color,_InkColor.rgb,hatchMask);
                float stippleMask = 0;
                #if defined(_USE_STIPPLE)
                stippleMask = GetStippleMask(input.positionWS,normalWS,input.positionCS.xy/_ScaledScreenParams.xy,toon.brightness)*control.g*control.a*vertexControl.b;
                color = lerp(color,_InkColor.rgb,stippleMask);
                #endif
                float2 highlight = GetStylizedHighlight(input.positionWS,normalWS)*control.b*vertexControl.g;
                float highlightMask = highlight.x;
                color = lerp(color,_HighlightColor.rgb,saturate(highlightMask));
                color = lerp(color,_InkColor.rgb,saturate(highlight.y));
                if (_DebugMode == 1) color = baseColor;
                if (_DebugMode == 2) color = normalWS * 0.5 + 0.5;
                if (_DebugMode == 3) color = toon.ndotl.xxx;
                if (_DebugMode == 4) color = toon.shadowMask.xxx;
                if (_DebugMode == 5) color = toon.deepShadowMask.xxx;
                if (_DebugMode == 6) color = hatchLayers.rrr;
                if (_DebugMode == 7) color = hatchLayers.ggg;
                if (_DebugMode == 8) color = hatchLayers.bbb;
                if (_DebugMode == 9) color = hatchMask.xxx;
                if (_DebugMode == 10) color = stippleMask.xxx;
                if (_DebugMode == 11) color = highlightMask.xxx;
                if (_DebugMode == 12) color = toon.castShadowMask.xxx;
                return half4(color,1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0
            HLSLPROGRAM
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex NPRVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            half4 DepthFragment(NPRVaryings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.universal/Shaders/DepthNormalsPass.hlsl"
            ENDHLSL
        }
    }
}
