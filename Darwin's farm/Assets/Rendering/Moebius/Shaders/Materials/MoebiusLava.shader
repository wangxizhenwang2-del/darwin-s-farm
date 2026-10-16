Shader "Darwin/Moebius Lava"
{
    Properties
    {
        _CrustColor("Crust Color",Color)=(0.27,0.21,0.30,1)
        _SecondaryCrustColor("Secondary Crust Color",Color)=(0.40,0.28,0.32,1)
        _CrustCoverage("Crust Coverage",Range(0,1))=0.8
        _CrustScale("Island Size (World Metres)",Range(0.3,6))=1.8
        _CrustDistortion("Boundary Distortion",Range(0,1))=0.65
        _CrackWidth("Extra Crack Width (World Metres)",Range(0,0.5))=0.12
        _CrackSoftness("Boundary Softness (World Metres)",Range(0,0.12))=0.025
        _FlowDirection("Flow Direction (World XZ)",Vector)=(0.25,-1,0,0)
        _LavaFlowSpeed("Molten Flow Speed (Metres / Second)",Range(-1,1))=0.28
        _FlowDistortion("Flow Distortion",Range(0,1))=0.32
        _FlowDetailScale("Flow Detail Scale",Range(0.1,3))=0.65
        _CrustDriftSpeed("Crust Drift Speed (Metres / Second)",Range(-0.2,0.2))=0.035
        _CrustDeformationStrength("Crust Deformation",Range(0,1))=0.45
        _CoolLavaColor("Cooling Boundary Color",Color)=(0.52,0.10,0.055,1)
        _WarmLavaColor("Molten Orange",Color)=(1,0.34,0.06,1)
        [HDR] _HotLavaColor("Hot Region Color",Color)=(1,0.82,0.32,1)
        _HeatContrast("Heat Contrast",Range(0.5,3))=1.35
        _ColorBanding("Color Banding",Range(0,1))=0.8
        _HotRegionThreshold("Hot Region Threshold",Range(0.4,0.95))=0.72
        [Toggle] _EnableEmission("Enable Emission",Float)=1
        _EmissionIntensity("Emission Intensity",Range(0,3))=0.22
        _InkColor("Ink Color",Color)=(0.06,0.038,0.06,1)
        _ContourWidth("Crust Contour Width (Render Pixels)",Range(0,2))=0.85
        _ContourStrength("Crust Contour Strength",Range(0,1))=0.7
        [Toggle] _UseHatching("Crust Hatching",Float)=1
        _HatchStrength("Hatch Strength",Range(0,1))=0.42
        _HatchScale("Hatch Lines / World Metre",Range(1,24))=10
        _HatchRotation("Hatch Rotation Degrees",Range(-180,180))=-25
        _HatchLineWidth("Hatch Pen Width (Render Pixels)",Range(0.35,2))=0.7
        _HatchIrregularity("Hand Drawn Variation",Range(0,1))=0.65
        _HatchCrossStrength("Cross Hatch Strength",Range(0,1))=0.18
        _StippleStrength("Crust Stipple Strength",Range(0,1))=0.1
        _StippleScale("Stipple Tiles / World Metre",Range(1,50))=18
        _NPRLightingInfluence("Crust NPR Lighting Influence",Range(0,1))=0.65
        [Toggle] _ReceiveLavaShadows("Receive Main Light Shadows",Float)=1
        _LavaShadowColor("Shadow Ink Color",Color)=(0.10,0.075,0.12,1)
        _LavaShadowStrength("Shadow Ink Strength",Range(0,1))=0.75
        _LavaShadowDensity("Shadow Hatch Density (Lines / Metre)",Range(1,24))=8
        [Toggle] _UseControlMap("Use Control Map",Float)=0
        [NoScaleOffset] _ControlMap("Control: R Hatch G Detail B Highlight A Shadow",2D)="white"{}
        [Toggle] _UseVertexColor("Vertex: R Hatch G Highlight B Shadow",Float)=0
        _AnimationPhase("Animation Phase (Seconds)",Float)=0
        [Enum(Darwin.Rendering.Editor.MoebiusLavaDebugView)] _DebugView("Debug View",Float)=0
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Cull Back ZWrite On ZTest LEqual Blend Off
        HLSLINCLUDE
        #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusLavaInput.hlsl"
        ENDHLSL
        Pass
        {
            Name "LavaForward"
            Tags {"LightMode"="UniversalForwardOnly"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LavaVertex
            #pragma fragment LavaFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusLavaFlow.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusLavaPattern.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusLavaLighting.hlsl"
            half4 LavaFragment(LavaVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                LavaPattern pattern=GetLavaPattern(input.positionWS.xz);
                LavaFlow flow=GetLavaFlow(input.positionWS.xz);
                return half4(ShadeMoebiusLava(input,pattern,flow),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags {"LightMode"="DepthOnly"}
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LavaVertex
            #pragma fragment LavaDepth
            #pragma multi_compile_instancing
            half4 LavaDepth(LavaVaryings input):SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags {"LightMode"="DepthNormalsOnly"}
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex LavaVertex
            #pragma fragment LavaNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            half4 LavaNormals(LavaVaryings input):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 normal=normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                return half4(PackFloat2To888(saturate(PackNormalOctQuadEncode(normal)*0.5+0.5)),0);
                #else
                return half4(normal,0);
                #endif
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags {"LightMode"="ShadowCaster"}
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/Shaders/ShadowCasterPass.hlsl"
            ENDHLSL
        }
    }
    CustomEditor "Darwin.Rendering.Editor.MoebiusLavaShaderGUI"
    Fallback Off
}
