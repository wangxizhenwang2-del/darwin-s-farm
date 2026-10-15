Shader "Hidden/Darwin/Validation Waterfall World Probe"
{
    SubShader
    {
        Pass
        {
            Name "WorldProbe"
            Cull Off ZWrite On
            HLSLPROGRAM
            #pragma vertex Vertex
            #pragma fragment Fragment
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float3 positionOS:POSITION; };
            struct Varyings { float4 positionCS:SV_POSITION;float3 world:TEXCOORD0; };
            Varyings Vertex(Attributes input)
            {
                Varyings output;output.world=TransformObjectToWorld(input.positionOS);
                output.positionCS=TransformWorldToHClip(output.world);return output;
            }
            float4 Fragment(Varyings input):SV_Target{return float4(input.world.xz/32+0.5,0,1);}
            ENDHLSL
        }
    }
}
