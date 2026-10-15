Shader "Darwin/Moebius Volcano Lava Flow"
{
    Properties
    {
        [NoScaleOffset] _FlowInk ("Sparse flowing brush mask", 2D) = "white" {}
        [NoScaleOffset] _LakeFlowField ("Baked dual-inlet lake field", 2D) = "black" {}
        _CoolEdge ("Cooling edge", Color) = (0.72,0.16,0.065,1)
        _Orange ("Orange body", Color) = (1,0.35,0.055,1)
        _HotOrange ("Hot orange", Color) = (1,0.62,0.13,1)
        _Core ("Yellow core", Color) = (1,0.87,0.34,1)
        _InkColor ("Flowing brush ink", Color) = (0.72,0.22,0.07,1)
        _FlowSpeed ("Flow speed", Range(-1,1)) = 0.08
        _FlowTiling ("Flow repeats along path", Range(0.5,8)) = 3
        _BrushStrength ("Brush strength", Range(0,1)) = 0.3
        _BandWobble ("Static band bend", Range(0,0.15)) = 0.035
        _CoreWidth ("Yellow core width", Range(0.05,0.7)) = 0.25
        _LakeMode ("Lake dual-inlet field", Float) = 0
        _EmissionStrength ("Subtle warmth", Range(0,0.5)) = 0.08
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        // The baked outer sheet inherits the FBX winding; render both sides at bends and forks.
        Cull Off ZWrite On ZTest LEqual
        Pass
        {
            Name "LavaForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex LavaVertex
            #pragma fragment LavaFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_FlowInk);
            SAMPLER(sampler_FlowInk);
            TEXTURE2D(_LakeFlowField);
            SAMPLER(sampler_LakeFlowField);
            CBUFFER_START(UnityPerMaterial)
                half4 _CoolEdge, _Orange, _HotOrange, _Core, _InkColor;
                float _FlowSpeed, _FlowTiling, _BrushStrength, _BandWobble;
                float _CoreWidth, _LakeMode, _EmissionStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings LavaVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }
            half4 LavaFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.uv;
                if (_LakeMode > 0.5)
                {
                    // RG contains the two distances from the baked contact-area centroids.
                    // B is the angular flow coordinate of the nearer inlet.
                    half4 field = SAMPLE_TEXTURE2D(_LakeFlowField, sampler_LakeFlowField, uv);
                    float nearDistance = min(field.r, field.g);
                    float lakeBand = saturate(nearDistance + sin(uv.x * 12.1 + uv.y * 9.7) * _BandWobble * 0.25);
                    half3 lakeColor = lerp(_Core.rgb, _HotOrange.rgb, smoothstep(0.08, 0.25, lakeBand));
                    lakeColor = lerp(lakeColor, _Orange.rgb, smoothstep(0.37, 0.62, lakeBand));
                    lakeColor = lerp(lakeColor, _CoolEdge.rgb, smoothstep(0.73, 0.94, lakeBand));
                    float2 lakeInkUV = float2(field.b, nearDistance * _FlowTiling - _Time.y * _FlowSpeed);
                    half lakeInk = SAMPLE_TEXTURE2D(_FlowInk, sampler_FlowInk, lakeInkUV).r;
                    lakeColor = lerp(lakeColor, _InkColor.rgb, (1 - lakeInk) * _BrushStrength);
                    return half4(lakeColor * (1 + _EmissionStrength), 1);
                }

                float crossFlow = frac(uv.x);
                float downstream = uv.y;

                // Keep the four broad color bands stationary. Only sparse ink moves.
                float bend = sin(downstream * 7.5 + crossFlow * 4.1) * 0.65
                           + sin(downstream * 14.7 - crossFlow * 3.2) * 0.35;
                float d = abs(crossFlow - 0.5 - bend * _BandWobble) * 2;
                half3 color = lerp(_Core.rgb, _HotOrange.rgb, smoothstep(_CoreWidth - 0.04, _CoreWidth + 0.04, d));
                color = lerp(color, _Orange.rgb, smoothstep(0.48, 0.60, d));
                color = lerp(color, _CoolEdge.rgb, smoothstep(0.78, 0.94, d));

                float2 inkUV = float2(crossFlow, downstream * _FlowTiling - _Time.y * _FlowSpeed);
                half ink = SAMPLE_TEXTURE2D(_FlowInk, sampler_FlowInk, inkUV).r;
                color = lerp(color, _InkColor.rgb, (1 - ink) * _BrushStrength * smoothstep(0.22, 0.55, d));
                color *= 1 + _EmissionStrength;
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
