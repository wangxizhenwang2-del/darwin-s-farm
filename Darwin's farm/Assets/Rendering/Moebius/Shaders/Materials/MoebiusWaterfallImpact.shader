Shader "Darwin/Moebius Waterfall Impact"
{
    Properties
    {
        _SurfaceColor("Surface Color (Match Lower Water)",Color)=(0.49,0.72,0.74,1)
        _FoamColor("Foam Color",Color)=(0.91,0.95,0.84,1)
        _InkLineColor("Ink Line Color",Color)=(0.24,0.38,0.40,1)
        _FoamWidth("Foam Band Width (Radius Fraction)",Range(0.01,0.8))=0.18
        _FoamNoiseScale("Foam Noise Scale",Range(0.1,8))=3
        _FoamNoiseStrength("Foam Edge Irregularity",Range(0,0.4))=0.26
        _FoamMotionSpeed("Foam Shape Speed",Range(0,3))=0.7
        _FoamEdgeInkWidth("Foam Edge Ink Width (Render Pixels)",Range(0,2))=0.65
        _FoamEdgeInkStrength("Foam Edge Ink Strength",Range(0,1))=0.55
        _OutflowSpeed("Outward Ripple Speed",Range(0,3))=0.5
        _RippleDensity("Ripple Bands / Radius",Range(1,12))=4
        _RippleInkWidth("Ripple Ink Width (Render Pixels)",Range(0.35,2))=0.65
        _RippleStrength("Ripple Strength",Range(0,1))=0.65
        [Toggle] _EnableWaterfallImpact("Enable Waterfall Impact",Float)=1
        _ImpactPointA("Impact Line Start (World)",Vector)=(-2.9,0,1.7,0)
        _ImpactPointB("Impact Line End (World)",Vector)=(2.9,0,1.7,0)
        _ImpactFlowDirection("Outflow Direction (World XZ)",Vector)=(0,-1,0,0)
        _ImpactRadius("Impact Radius / Half Depth (Metres)",Range(0.1,4))=2.1
        _ImpactTurbulence("Turbulence Strength",Range(0,1))=0.95
        _ImpactNoiseScale("Turbulence Noise Scale",Range(0.1,4))=0.85
        _ImpactFoamDensity("Foam Density",Range(0,1))=0.68
        _ImpactFoamSpeed("Foam Motion Speed",Range(0,3))=0.65
        _ImpactFoamColor("Impact Foam Color",Color)=(0.9,0.97,0.96,1)
        _ImpactFoamInkStrength("Foam Ink Strength",Range(0,0.5))=0.12
        _ImpactRippleSpeed("Ripple Speed (Metres / Second)",Range(0,3))=0.9
        _ImpactSpawnInterval("Ripple Spawn Interval (Seconds)",Range(0.1,5))=1.1
        _ImpactLifetime("Ripple Lifetime (Seconds)",Range(0.2,15))=6.6
        _ImpactMaxDistance("Ripple Max Distance (Metres)",Range(0.5,15))=6
        [IntRange] _ImpactRippleCount("Ripple Count (Maximum 8)",Range(1,8))=6
        _ImpactRippleWidth("Ripple Width (Metres)",Range(0.005,0.15))=0.065
        _ImpactDistortion("Ripple Distortion (Metres)",Range(0,0.6))=0.2
        _ImpactRippleNoiseScale("Ripple Noise Scale",Range(0.1,4))=0.7
        _ImpactArcBreakup("Arc Breakup",Range(0,1))=0.58
        _ImpactArcVariation("Arc Length Variation",Range(0,1))=0.55
        _ImpactRippleColor("Ripple Color",Color)=(0.86,0.95,0.95,1)
        _ImpactRippleIntensity("Ripple Intensity",Range(0,1))=0.85
        _ImpactDistanceFade("Distance Fade (Range Fraction)",Range(0.05,0.9))=0.4
        _ImpactLifetimeFade("Lifetime Fade (Life Fraction)",Range(0.05,0.8))=0.35
        _ImpactPhase("Impact Phase (Seconds)",Float)=0
        _AnimationPhase("Animation Phase (Seconds)",Float)=0
        [Enum(Composite,0,Foam,1,Ink,2,Outflow,3)] _DebugView("Debug View",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        ZWrite On ZTest LEqual Blend One Zero Cull Off
        HLSLINCLUDE
        #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallImpactInput.hlsl"
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

            half4 WaterfallFragment(WaterfallVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                WaterfallClip(input.uv,input.positionOS);
                float2 masks=WaterfallImpactMasks(input.positionWS);
                half3 color=ApplyWaterfallImpact(_SurfaceColor.rgb,input.positionWS);
                if(_DebugView==1)color=masks.xxx;
                if(_DebugView==2)color=masks.yyy;
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

