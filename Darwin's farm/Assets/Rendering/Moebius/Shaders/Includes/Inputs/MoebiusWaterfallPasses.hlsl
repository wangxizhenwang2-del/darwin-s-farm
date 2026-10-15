#ifndef MOEBIUS_WATERFALL_PASSES_INCLUDED
#define MOEBIUS_WATERFALL_PASSES_INCLUDED
// Every pass uses the same deformation and cutout, including the outline's depth/normal inputs.
struct WaterfallAttributes
{
    float4 positionOS:POSITION;
    float3 normalOS:NORMAL;
    float2 uv:TEXCOORD0;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};
struct WaterfallVaryings
{
    float4 positionCS:SV_POSITION;
    float3 positionWS:TEXCOORD0;
    float3 normalWS:TEXCOORD1;
    float2 uv:TEXCOORD2;
    float3 positionOS:TEXCOORD3;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};
WaterfallVaryings WaterfallVertex(WaterfallAttributes input)
{
    WaterfallVaryings output=(WaterfallVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input,output);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
    float3 p=WaterfallDeform(input.positionOS.xyz,input.uv);
    float3 n=normalize(input.normalOS);
    float3 tangent=normalize(cross(abs(n.y)<0.9?float3(0,1,0):float3(1,0,0),n));
    float3 bitangent=cross(n,tangent);
    float3 a=WaterfallDeform(input.positionOS.xyz+tangent*0.01,input.uv)-p;
    float3 b=WaterfallDeform(input.positionOS.xyz+bitangent*0.01,input.uv)-p;
    output.normalWS=TransformObjectToWorldNormal(normalize(cross(a,b)));
    output.positionWS=TransformObjectToWorld(p);
    output.positionCS=TransformWorldToHClip(output.positionWS);
    output.uv=input.uv;output.positionOS=p;
    return output;
}
half4 WaterfallDepthFragment(WaterfallVaryings input):SV_Target
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    WaterfallClip(input.uv,input.positionOS);
    return input.positionCS.z;
}
#endif
