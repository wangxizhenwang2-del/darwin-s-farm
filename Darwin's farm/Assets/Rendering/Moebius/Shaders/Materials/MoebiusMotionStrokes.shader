Shader "Darwin/Moebius Motion Strokes"
{
    Properties { _Opacity("Opacity",Range(0,1))=1 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "IllustratedMotion"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off ZTest LEqual
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            float _Opacity;
            CBUFFER_END
            struct Input { float3 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            struct Output { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; };
            Output Vert(Input v) {Output o;o.positionCS=TransformObjectToHClip(v.positionOS);o.uv=v.uv;o.color=v.color;return o;}
            half4 Frag(Output i):SV_Target
            {
                float edge=abs(i.uv.x*2-1),aa=max(fwidth(edge),.001);
                return half4(i.color.rgb,i.color.a*_Opacity*(1-smoothstep(1-aa,1,edge)));
            }
            ENDHLSL
        }
    }
}
