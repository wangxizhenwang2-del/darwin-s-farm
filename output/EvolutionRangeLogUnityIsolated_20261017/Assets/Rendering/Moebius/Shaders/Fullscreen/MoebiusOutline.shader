Shader "Hidden/Darwin/Moebius Outline"
{
    Properties
    {
        _DepthThreshold("Relative Depth Threshold", Range(0.001,0.1)) = 0.012
        _NormalThreshold("Normal Difference Threshold", Range(0.01,2)) = 0.8
        _NormalEdgeStrength("Interior Crease Ink (silhouettes remain full)", Range(0,1)) = 0.25
        _PenPressure("Outline Pen Pressure Variation", Range(0,0.5)) = 0.14
        _OutlineThickness("Outline Radius (render pixels)", Range(0.25,4)) = 0.75
        _OutlineStrength("Ink Strength", Range(0,1)) = 1
        _OutlineColor("Ink Color", Color) = (0.075,0.053,0.048,1)
        [Toggle(_USE_LINE_WOBBLE)] _UseWobble("Line Wobble", Float) = 0
        _LineWobbleStrength("Wobble (render pixels)", Range(0,1)) = 0.16
        _LineWobbleScale("Wobble Frequency", Float) = 65
        _LineWobbleSpeed("Wobble Speed (0 = static)", Float) = 0
        _WobbleFrameRate("Animation Steps Per Second (0 = continuous)", Float) = 8
        _Saturation("Saturation", Range(0,2)) = 0.95
        _Contrast("Contrast", Range(0.5,1.5)) = 1
        _ColorLevels("Color Levels (0 disables)", Range(0,64)) = 0
        [Toggle(_USE_PAPER)] _UsePaper("Paper", Float) = 0
        _PaperTexture("Paper Texture (neutral 0.5)", 2D) = "gray" {}
        _PaperScale("Paper Scale", Float) = 4
        _PaperStrength("Paper Strength", Range(0,0.1)) = 0.018
        _GrainStrength("Procedural Grain", Range(0,0.05)) = 0.004
        [Enum(Composite,0,CameraDepth,1,CameraNormal,2,DepthEdge,3,NormalEdge,4,FinalOutline,5)] _DebugMode("Outline Debug", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment InkFragment
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma shader_feature_local_fragment _USE_LINE_WOBBLE
            #pragma shader_feature_local_fragment _USE_PAPER
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"
            TEXTURE2D(_PaperTexture); SAMPLER(sampler_PaperTexture);
            CBUFFER_START(UnityPerMaterial)
            float4 _OutlineColor;
            float _DepthThreshold, _NormalThreshold, _OutlineThickness, _OutlineStrength;
            float _NormalEdgeStrength, _PenPressure;
            float _LineWobbleStrength, _LineWobbleScale, _LineWobbleSpeed, _WobbleFrameRate;
            float _Saturation, _Contrast, _ColorLevels, _PaperScale, _PaperStrength, _GrainStrength, _DebugMode;
            CBUFFER_END
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusPrint.hlsl"
            float EyeDepth(float raw)
            {
                // LinearEyeDepth is perspective-only; orthographic cameras need a direct near/far lerp.
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                    raw = 1 - raw;
                    #endif
                    return lerp(_ProjectionParams.y, _ProjectionParams.z, raw);
                }
                return LinearEyeDepth(raw, _ZBufferParams);
            }
            float IsSky(float raw)
            {
                #if UNITY_REVERSED_Z
                return step(raw, 0.000001);
                #else
                return step(0.999999, raw);
                #endif
            }
            float SoftEdge(float value, float threshold)
            {
                return smoothstep(threshold, threshold * 1.6, value);
            }
            void GetInkEdges(float2 uv, out float depthEdge, out float normalEdge, out float centerDepth, out float3 centerNormal)
            {
                // Cardinal stencil: five depth + five normal samples, no wide Sobel kernel.
                float2 paperUV = uv*float2(_ScaledScreenParams.x/_ScaledScreenParams.y,1);
                float pressure = 1+(PrintNoise(paperUV*47)-0.5)*2*_PenPressure;
                float2 radius = _OutlineThickness*pressure / _ScaledScreenParams.xy;
                float2 leftUV = saturate(uv - float2(radius.x,0));
                float2 rightUV = saturate(uv + float2(radius.x,0));
                float2 downUV = saturate(uv - float2(0,radius.y));
                float2 upUV = saturate(uv + float2(0,radius.y));
                float raw = SampleSceneDepth(uv);
                float l = SampleSceneDepth(leftUV), r = SampleSceneDepth(rightUV);
                float d = SampleSceneDepth(downUV), u = SampleSceneDepth(upUV);
                centerDepth = EyeDepth(raw);
                // Second differences reject continuous depth slopes on flat surfaces.
                float difference = (abs(EyeDepth(l)+EyeDepth(r)-2*centerDepth) + abs(EyeDepth(d)+EyeDepth(u)-2*centerDepth)) / max(centerDepth,0.01);
                float sky = IsSky(raw);
                float silhouette = max(max(abs(sky-IsSky(l)),abs(sky-IsSky(r))),max(abs(sky-IsSky(d)),abs(sky-IsSky(u))));
                depthEdge = max(SoftEdge(difference,_DepthThreshold) * (1-sky), silhouette);
                centerNormal = SampleSceneNormals(uv);
                float normalDifference = max(max(length(centerNormal-SampleSceneNormals(leftUV)),length(centerNormal-SampleSceneNormals(rightUV))),
                    max(length(centerNormal-SampleSceneNormals(downUV)),length(centerNormal-SampleSceneNormals(upUV))));
                normalEdge = SoftEdge(normalDifference,_NormalThreshold) * (1-sky);
            }
            half4 InkFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 screenUV = input.positionCS.xy / _ScaledScreenParams.xy;
                float depthEdge, normalEdge, depth;
                float3 normalWS;
                GetInkEdges(GetWobbledUV(screenUV),depthEdge,normalEdge,depth,normalWS);
                // Hard low-poly normals should not draw every triangle as a full black wireframe.
                float outlineMask = saturate(max(depthEdge,normalEdge*_NormalEdgeStrength) * _OutlineStrength);
                half4 source = SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,input.texcoord);
                half3 color = GetPrintColor(source.rgb,screenUV);
                color = lerp(color,_OutlineColor.rgb,outlineMask);
                if (_DebugMode == 1) color = saturate(depth / _ProjectionParams.z).xxx;
                if (_DebugMode == 2) color = normalWS * 0.5 + 0.5;
                if (_DebugMode == 3) color = depthEdge.xxx;
                if (_DebugMode == 4) color = normalEdge.xxx;
                if (_DebugMode == 5) color = (1-outlineMask).xxx;
                return half4(color,source.a);
            }
            ENDHLSL
        }
    }
}
