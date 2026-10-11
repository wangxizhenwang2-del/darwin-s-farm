Shader "Darwin/Moebius Waterfall"
{
    Properties
    {
        _BaseColor("Base Color",Color)=(0.39,0.68,0.70,1)
        _ShadowColor("Band Color",Color)=(0.28,0.53,0.61,1)
        _HighlightColor("Lip Highlight Color",Color)=(0.91,0.95,0.84,1)
        _FlowLineColor("Flow Line Color",Color)=(0.83,0.91,0.80,1)
        _InkLineColor("Ink Line Color",Color)=(0.20,0.36,0.41,1)
        _SheetSize("Sheet Size (Local Width / Height)",Vector)=(5,6,0,0)
        _FlowSpeed("Downward Flow Speed (Local Metres / Second)",Range(0,6))=1.4
        _FlowScale("Vertical Pattern Scale",Range(0.1,4))=1
        _DistortionStrength("Line Distortion",Range(0,0.5))=0.15
        _DistortionScale("Distortion Scale",Range(0.1,8))=1.2
        _VerticalLineDensity("Vertical Lines / Local Metre",Range(1,24))=9
        _VerticalLineWidth("Ink Width (Render Pixels)",Range(0.35,2))=0.65
        _FlowLineStrength("Pale Flow Line Strength",Range(0,1))=0.75
        _InkLineStrength("Ink Line Strength",Range(0,1))=0.65
        _BandStrength("Broad Band Strength",Range(0,1))=0.32
        _EdgeBreakupStrength("Edge Cutout (Width Fraction)",Range(0,0.15))=0.015
        _EdgeBreakupScale("Edge Noise Scale",Range(0.1,8))=1.8
        _SheetWobbleStrength("Sheet Wobble (Local Metres)",Range(0,0.2))=0.035
        _SheetWobbleSpeed("Sheet Wobble Speed",Range(0,4))=0.8
        [Toggle] _ReceiveWaterShadows("Receive Main Light Shadows",Float)=1
        _WaterShadowColor("Shadow Ink Color",Color)=(0.19,0.32,0.34,1)
        _WaterShadowStrength("Shadow Ink Strength",Range(0,1))=0.55
        _WaterShadowDensity("Shadow Hatch Density (Lines / Metre)",Range(1,24))=8
        _AnimationPhase("Animation Phase (Seconds)",Float)=0
        [Enum(Composite,0,Flow,1,Lines,2,Edge,3)] _DebugView("Debug View",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        ZWrite On ZTest LEqual Blend One Zero Cull Off
        HLSLINCLUDE
        #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallInput.hlsl"
        #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallPasses.hlsl"
        ENDHLSL
        Pass
        {
            Name "WaterfallForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterfallVertex
            #pragma fragment WaterfallFragment
            #pragma multi_compile_instancing
             #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
             #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
             #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallShadows.hlsl"

            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusWaterfallLines.hlsl"
            half4 WaterfallFragment(WaterfallVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                WaterfallClip(input.uv,input.positionOS);
                half3 color=WaterfallColor(input.uv);
                if(_DebugView==0)color=ApplyWaterfallCastShadow(color,input.positionWS,normalize(input.normalWS));
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
            #pragma vertex WaterfallVertex
            #pragma fragment WaterfallDepthFragment
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex WaterfallVertex
            #pragma fragment WaterfallNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 WaterfallNormalsFragment(WaterfallVaryings input, FRONT_FACE_TYPE face:FRONT_FACE_SEMANTIC):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                WaterfallClip(input.uv,input.positionOS);
                float3 normal=normalize(input.normalWS)*IS_FRONT_VFACE(face,1,-1);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct=PackNormalOctQuadEncode(normal);
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
            #pragma vertex WaterfallShadowVertex
            #pragma fragment WaterfallShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection,_LightPosition;
            WaterfallVaryings WaterfallShadowVertex(WaterfallAttributes input)
            {
                WaterfallVaryings output=WaterfallVertex(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 direction=normalize(_LightPosition-output.positionWS);
                #else
                float3 direction=_LightDirection;
                #endif
                output.positionCS=TransformWorldToHClip(ApplyShadowBias(output.positionWS,output.normalWS,direction));
                output.positionCS=ApplyShadowClamping(output.positionCS);
                return output;
            }
            half4 WaterfallShadowFragment(WaterfallVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                WaterfallClip(input.uv,input.positionOS);
                return 0;
            }
            ENDHLSL
        }
    }
    CustomEditor "Darwin.Rendering.Editor.MoebiusWaterfallShaderGUI"
    Fallback Off
}

