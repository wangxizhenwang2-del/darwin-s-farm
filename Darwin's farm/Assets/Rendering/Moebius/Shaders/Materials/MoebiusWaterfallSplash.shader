Shader "Darwin/Moebius Waterfall Splash"
{
    Properties
    {
        _SplashColorA("Splash Fill Color",Color)=(0.84,0.93,0.91,1)
        _SplashColorB("Secondary Fill Color",Color)=(0.67,0.81,0.87,1)
        _InkLineColor("Ink Line Color",Color)=(0.28,0.40,0.45,1)
        _LayerBlendStrength("Secondary Color Strength",Range(0,1))=0.32
        [Enum(BackLift,0,MainMass,1,SurfaceSpread,2,Legacy,3)] _SplashMotionRole("Splash Motion Role",Float)=3
        _PulseSpeed("Breathing Speed",Range(0,4))=1
        _PulseAmplitude("Breathing Amplitude",Range(0,0.2))=0.045
        _WobbleStrength("Wobble (Local Metres)",Range(0,0.2))=0.045
        _WobbleSpeed("Wobble Speed",Range(0,4))=1.1
        _DistortionScale("Distortion Scale",Range(0.1,8))=2
        _InternalPatternStrength("Internal Color Variation",Range(0,1))=0.15
        _SplashInkWidth("Silhouette Ink Width (Render Pixels)",Range(0,2))=0.7
        _SplashInkStrength("Silhouette Ink Strength",Range(0,1))=0.45
        [Toggle] _ReceiveWaterShadows("Receive Main Light Shadows",Float)=1
        _WaterShadowColor("Shadow Ink Color",Color)=(0.19,0.32,0.34,1)
        _WaterShadowStrength("Shadow Ink Strength",Range(0,1))=0.55
        _WaterShadowDensity("Shadow Hatch Density (Lines / Metre)",Range(1,24))=8
        _AnimationPhase("Animation Phase (Seconds)",Float)=0
        [Enum(Composite,0,FillMask,1,Normals,2,Motion,3)] _DebugView("Debug View",Float)=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        ZWrite On ZTest LEqual Blend One Zero Cull Back
        HLSLINCLUDE
        #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusWaterfallSplashInput.hlsl"
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

            half4 WaterfallFragment(WaterfallVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normal=normalize(input.normalWS);
                // Illustrated pale tops and cool bases; do not shade each lobe like a glossy ellipsoid.
                float shade=1-smoothstep(-0.45,0.95,input.positionOS.y);
                float variation=smoothstep(0.4,0.55,PrintNoise(input.positionOS.xy*1.4));
                half3 color=lerp(_SplashColorA.rgb,_SplashColorB.rgb,saturate(shade*_LayerBlendStrength+variation*_InternalPatternStrength));
                float rim=abs(dot(normal,GetWorldSpaceNormalizeViewDir(input.positionWS)));
                float ink=(1-smoothstep(0,max(max(fwidth(rim),0.001)*_SplashInkWidth,0.0001),rim))*step(0.001,_SplashInkWidth);
                color=lerp(color,_InkLineColor.rgb,ink*_SplashInkStrength);
                if(_DebugView==1)color=shade.xxx;
                if(_DebugView==0)color=ApplyWaterfallCastShadow(color,input.positionWS,normal);
                if(_DebugView==2)color=normal*0.5+0.5;
                if(_DebugView==3)color=(sin(WaterfallTime()*_PulseSpeed)*0.5+0.5).xxx;
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

