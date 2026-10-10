Shader "Hidden/Darwin/Water Validation Depth"
{
    SubShader { Pass {
        ZTest Always ZWrite Off Cull Off
        HLSLPROGRAM
        #pragma vertex vert_img
        #pragma fragment frag
        #include "UnityCG.cginc"
        sampler2D _ProbeDepth;
        float4 frag(v2f_img input) : SV_Target { return tex2D(_ProbeDepth,input.uv).rrrr; }
        ENDHLSL
    } }
}
