Shader "Darwin/Moebius Volcano Ink Outline"
{
    Properties
    {
        _InkColor("Ink Color", Color) = (0.055,0.065,0.085,1)
        _Width("World Outline Width", Range(0.001,0.05)) = 0.012
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry-1" }
        Pass
        {
            Name "VolcanoInkOutline"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Front
            ZWrite On
            ZTest LEqual
            HLSLPROGRAM
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _InkColor;
            float _Width;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings OutlineVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 world = TransformObjectToWorld(input.positionOS.xyz);
                world += normalize(TransformObjectToWorldNormal(input.normalOS)) * _Width;
                output.positionCS = TransformWorldToHClip(world);
                return output;
            }
            half4 OutlineFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                return _InkColor;
            }
            ENDHLSL
        }
    }
    Fallback Off
}
