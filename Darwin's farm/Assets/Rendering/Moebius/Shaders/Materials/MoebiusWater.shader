Shader "Darwin/Moebius Water"
{
    Properties
    {
        _WaterColor("Water Color", Color) = (0.49,0.72,0.74,1)
        [Toggle] _EnableVariation("Enable Color Variation", Float) = 0
        _ColorVariation("Color Variation Strength", Range(0,0.12)) = 0.025
        _VariationScale("Variation Scale", Float) = 0.35
        [Toggle] _EnableFlow("Enable Flow", Float) = 1
        _FlowDirection("Flow Direction (World XZ)", Vector) = (0.2,1,0,0)
        _FlowSpeed("Flow Speed", Range(-2,2)) = 0.5
        _FlowScale("Flow Scale", Range(0.05,4)) = 1
        _FlowDistortion("Flow Distortion", Range(0,1)) = 0.2
        _FlowStrength("Flow Strength", Range(0,1)) = 1
        [Toggle] _EnableWaves("Enable Waves", Float) = 1
        _WaveHeight("Wave Height (World Metres)", Range(0,0.5)) = 0.055
        _WaveLength("Wave Length (World Metres)", Range(0.2,20)) = 3.2
        _WaveSpeed("Wave Speed", Range(-3,3)) = 1.4
        _WaveDirection("Wave Direction (World XZ)", Vector) = (0.4,1,0,0)
        [Toggle] _EnableRipples("Enable Ripples", Float) = 1
        _RippleColor("Ripple Ink Color", Color) = (0.25,0.39,0.40,1)
        _RippleStrength("Ripple Strength", Range(0,1)) = 0.65
        _RippleDensity("Ripple Density", Range(0.1,6)) = 1.5
        _RippleLength("Ripple Length (Cell Fraction)", Range(0.05,0.92)) = 0.65
        _RippleWidth("Ripple Width", Range(0.002,0.05)) = 0.018
        _RippleSpeed("Ripple Drift Speed", Range(-1,1)) = 0.12
        _RippleDistortion("Ripple Distortion", Range(0,1)) = 0.45
        _RippleWobbleStrength("Ripple Wobble Strength", Range(0,0.06)) = 0.035
        _RippleWobbleSpeed("Ripple Wobble Speed", Range(0,8)) = 3
        [Toggle] _EnableFoam("Enable Shoreline Foam", Float) = 1
        _FoamColor("Foam Color", Color) = (0.94,0.95,0.81,1)
        _FoamWidth("Foam Width (World Metres)", Range(0.02,2)) = 0.38
        _FoamIntensity("Foam Intensity", Range(0,1)) = 1
        _FoamSpeed("Foam Animation Speed", Range(-1,1)) = 0.28
        _FoamNoiseScale("Foam Noise Scale", Range(0.1,8)) = 2
        _FoamIrregularity("Foam Irregularity", Range(0,1)) = 0.9
        [Toggle] _EnableFoamInk("Enable Foam Ink Contour", Float) = 1
        _FoamInkColor("Foam Ink Color", Color) = (0.23,0.33,0.31,1)
        _FoamInkStrength("Foam Ink Strength", Range(0,1)) = 0.65
        _FoamInkWidth("Foam Ink Width (Render Pixels)", Range(0,2)) = 0.75
        [Toggle] _EnableFoamPatches("Enable Near-Shore Foam Patches", Float) = 1
        _FoamPatchStrength("Foam Patch Strength", Range(0,1)) = 0.8
        [Toggle] _EnableShoreMotion("Enable Shore Advance and Retreat", Float) = 1
        _ShoreMotionStrength("Shore Motion Strength", Range(0,0.5)) = 0.13
        [Toggle] _EnableShoreMask("Use Baked Shoreline", Float) = 0
        [NoScaleOffset] _ShorelineMask("Shore Distance (R), Gradient (GB)", 2D) = "white" {}
        _ShoreWorldRect("Shore Bounds (Min XZ / Size XZ)", Vector) = (-8,-8,16,16)
        _ShoreDistanceRange("Baked Distance Range (Metres)", Float) = 4
        _ShoreWaveFade("Shore Wave Fade (Metres)", Range(0.05,4)) = 1
        [Toggle] _ReceiveWaterShadows("Receive Main Light Shadows", Float) = 1
        _WaterShadowColor("Shadow Ink Color", Color) = (0.19,0.32,0.34,1)
        _WaterShadowStrength("Shadow Ink Strength", Range(0,1)) = 0.65
        _WaterShadowDensity("Shadow Hatch Density (Lines / Metre)", Range(1,24)) = 8
        [Enum(Composite,0,Flow,1,WaveHeight,2,ShoreDistance,3,Foam,4,Normals,5)] _DebugView("Debug View", Float) = 0
        _AnimationPhase("Animation Phase Offset (Seconds)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        ZWrite On ZTest LEqual Blend One Zero Cull Back
        HLSLINCLUDE
        #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterInput.hlsl"
        #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterWaves.hlsl"
        ENDHLSL
        Pass
        {
            Name "WaterForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterFlow.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterRipples.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterFoam.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterShadows.hlsl"
            half4 WaterFragment(WaterVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 flow = WaterFlowCoordinates(input.positionWS.xz);
                half3 color = _WaterColor.rgb;
                if (_EnableVariation>0.5 && _ColorVariation>0)
                    color *= 1+(PrintNoise(flow*max(_VariationScale,0.01))-0.5)*_ColorVariation;
                float ripples = WaterRippleMask(flow);
                color = lerp(color,_RippleColor.rgb,ripples);
                float shoreDistance = (_EnableFoam>0.5 || _DebugView==3) ? WaterShoreDistance(input.positionWS.xz) : max(_ShoreDistanceRange,0.01);
                float2 foam = WaterFoamMask(input.positionWS.xz,shoreDistance);
                color = lerp(color,_FoamColor.rgb,foam.x);
                color = lerp(color,_FoamInkColor.rgb,foam.y);
                color = ApplyWaterCastShadow(color,input.positionWS);
                if (_DebugView==1) color = half3(frac(flow*0.15),0.5);
                if (_DebugView==2) color = (input.waveHeight/max(_WaveHeight,0.001)*0.5+0.5).xxx;
                if (_DebugView==3) color = saturate(shoreDistance/max(_ShoreDistanceRange,0.01)).xxx;
                if (_DebugView==4) color = foam.xxx;
                if (_DebugView==5) color = normalize(input.normalWS)*0.5+0.5;
                return half4(color,1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterDepthFragment
            #pragma multi_compile_instancing
            half4 WaterDepthFragment(WaterVaryings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterVertex
            #pragma fragment WaterNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 WaterNormalsFragment(WaterVaryings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normal = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = PackNormalOctQuadEncode(normal);
                return half4(PackFloat2To888(saturate(oct*0.5+0.5)),0);
                #else
                return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterShadowVertex
            #pragma fragment WaterShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection, _LightPosition;
            WaterVaryings WaterShadowVertex(WaterAttributes input)
            {
                WaterVaryings output = WaterVertex(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 direction = normalize(_LightPosition-output.positionWS);
                #else
                float3 direction = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS,output.normalWS,direction));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                return output;
            }
            half4 WaterShadowFragment(WaterVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }
    }
    CustomEditor "Darwin.Rendering.Editor.MoebiusWaterShaderGUI"
    Fallback Off
}
