Shader "Darwin/Moebius Skybox"
{
    Properties
    {
        _ZenithColor("Zenith Color",Color)=(0.39,0.76,0.91,1)
        _MidColor("Mid Sky Color",Color)=(0.47,0.81,0.92,1)
        _LowerColor("Lower Sky Color",Color)=(0.66,0.88,0.90,1)
        _HorizonColor("Horizon Color",Color)=(0.92,0.96,0.82,1)
        _BelowColor("Below-Horizon Color",Color)=(0.80,0.91,0.89,1)
        _HorizonHeight("Horizon Height (Direction Y)",Range(-0.2,0.2))=0
        _BelowFade("Below-Horizon Transition",Range(0.01,1))=0.35
        _LowerTransition("Horizon to Lower Sky",Range(0.01,0.8))=0.12
        _MidTransition("Lower to Mid Sky",Range(0.02,0.9))=0.38
        _ZenithTransition("Mid Sky to Zenith",Range(0.03,0.99))=0.72
        _TransitionSoftness("Transition Softness",Range(0.005,0.4))=0.17
        [Toggle] _EnableClouds("Enable Clouds",Float)=1
        _CloudColor("Cloud Fill Color",Color)=(1,0.98,0.85,1)
        _CloudShadeColor("Cloud Shade Color",Color)=(0.91,0.83,0.71,1)
        _CloudInkColor("Cloud Ink Color",Color)=(0.76,0.67,0.53,1)
        _CloudDensity("Cloud Density (Up to Four Banks)",Range(0,1))=1
        _CloudScale("Cloud Angular Scale",Range(0.2,3))=1.1
        _CloudShapeVariation("Cloud Shape Variation",Range(0,1))=0.5
        _CloudCoverage("Cloud Coverage",Range(0,1))=0.65
        _CloudElevationRange("Cloud Elevation Range (Min/Max Degrees)",Vector)=(3,12,0,0)
        _CloudRotation("Cloud Layout Rotation (Degrees)",Range(0,360))=25
        _CloudEdgeSoftness("Cloud Edge Softness",Range(0.001,0.2))=0.001
        _CloudShadeStrength("Cloud Shade Strength",Range(0,1))=0.6
        _CloudInkStrength("Cloud Ink Strength",Range(0,1))=0.3
        _CloudInkWidth("Cloud Ink Width (Render Pixels)",Range(0,2))=0.65
        _CloudWindDirection("Wind Direction (Azimuth / Elevation)",Vector)=(1,0,0,0)
        _CloudSpeed("Cloud Drift (Degrees / Second)",Range(-1,1))=0.025
        [Toggle] _EnableSun("Enable Sun",Float)=1
        [Toggle] _UseMainLightSun("Follow Main Directional Light",Float)=0
        _SunDirection("Manual Direction Toward Sun (World XYZ)",Vector)=(-0.38,0.24,0.89,0)
        _SunColor("Sun Disc Color",Color)=(1,0.97,0.81,1)
        _SunRadius("Sun Angular Radius (Degrees)",Range(0.05,16))=7.5
        _SunSoftness("Sun Edge Softness (Degrees)",Range(0.001,1))=0.035
        _SunBrightness("Sun Brightness",Range(0,3))=1
        _SunInkColor("Sun Contour Color",Color)=(0.76,0.59,0.49,1)
        _SunInkStrength("Sun Contour Strength",Range(0,1))=0.4
        _SunInkWidth("Sun Contour Width (Render Pixels)",Range(0,2))=0.65
        _SunHazeStrength("Sun Cloud-Shelf Strength",Range(0,1))=0.7
        _SunHazeHeight("Sun Cloud-Shelf Height (Disc Fraction)",Range(-0.9,0.9))=-0.25
        _SunHazeWidth("Sun Cloud-Shelf Width (Disc Fraction)",Range(0.001,0.08))=0.012
        _HaloColor("Halo Color",Color)=(0.98,0.85,0.65,1)
        _HaloRadius("Halo Angular Radius (Degrees)",Range(0.1,45))=12
        _HaloStrength("Halo Strength",Range(0,1))=0
        _SunHorizonInfluence("Sun Horizon Warmth",Range(0,1))=0
        _Exposure("Exposure (Stops)",Range(-4,4))=0
        _Saturation("Saturation",Range(0,2))=1
        [IntRange] _GradientSteps("Gradient Steps (0 = Smooth)",Range(0,16))=0
        _PigmentStrength("Low-Frequency Pigment",Range(0,0.1))=0
        _PigmentScale("Pigment Scale",Range(0.1,16))=5
        _PaperStrength("Angular Paper Texture Strength",Range(0,0.03))=0
        _AnimationPhase("Cloud Animation Phase (Seconds)",Float)=0
        _FogColorMatch("Scene Fog Color Match",Range(0,1))=0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off ZWrite Off ZTest LEqual
        Pass
        {
            Name "MoebiusSkybox"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SkyVertex
            #pragma fragment SkyFragment
            #pragma multi_compile_instancing
            #include "Assets/Rendering/Moebius/Shaders/Includes/Inputs/MoebiusSkyboxInput.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusSkyboxAtmosphere.hlsl"
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusSkyboxClouds.hlsl"
            struct Attributes { float3 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float3 direction:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings SkyVertex(Attributes input)
            {
                Varyings output=(Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                // Unity's standard DrawSkybox supplies a translation-free view matrix.
                output.positionCS=TransformObjectToHClip(input.positionOS);
                output.positionCS.z=UNITY_RAW_FAR_CLIP_VALUE*output.positionCS.w;
                output.direction=input.positionOS;
                return output;
            }
            half4 SkyFragment(Varyings input):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction=normalize(input.direction);
                half3 color=SkyGradient(direction);
                color=SkySun(color,direction);
                color=SkyClouds(color,direction);
                color=SkyPigment(color,direction);
                half luminance=dot(color,half3(0.2126,0.7152,0.0722));
                color=lerp(luminance.xxx,color,clamp(_Saturation,0,2));
                return half4(max(color,0)*exp2(clamp(_Exposure,-4,4)),1);
            }
            ENDHLSL
        }
    }
    CustomEditor "Darwin.Rendering.Editor.MoebiusSkyboxShaderGUI"
    Fallback Off
}
