Shader "Hidden/Darwin/Moebius Paint Brush"
{
    Properties { _MainTex("Working Texture",2D)="white"{} }
    SubShader
    {
        ZWrite Off 
        ZTest Always 
        Cull Off
        HLSLINCLUDE
        #include "UnityCG.cginc"
        sampler2D _MainTex;
        float4 _BrushCenter, _BrushNormal, _BrushColor;
        float4x4 _PaintObjectToWorld, _PaintWorldToObject;
        float _BrushRadius, _BrushStrength, _BrushFalloff, _PaintValue, _Channel, _Erase;
        struct Attributes {float4 vertex:POSITION;float3 normal:NORMAL;float2 uv:TEXCOORD0;};
        struct Varyings {float4 position:SV_POSITION;float2 uv:TEXCOORD0;float3 world:TEXCOORD1;float3 normal:TEXCOORD2;};
        Varyings PaintVertex(Attributes input)
        {
            Varyings output;
            // Match the editor's border-tolerant picking UVs without modifying the mesh asset.
            float2 paintUV=saturate(input.uv);
            output.position=float4(paintUV*2-1,0,1);
            #if UNITY_UV_STARTS_AT_TOP
            output.position.y=-output.position.y;
            #endif
            // Explicit matrices keep the UV bake independent from Scene GUI/Handles drawing state.
            output.uv=paintUV;output.world=mul(_PaintObjectToWorld,input.vertex).xyz;
            output.normal=normalize(mul((float3x3)transpose(_PaintWorldToObject),input.normal));return output;
        }
        float4 ApplyValue(float4 old,float weight)
        {
            if(_Channel<0)
            {
                if(_Erase>0.5)old.a=lerp(old.a,0,weight);
                else
                {
                    float alpha=old.a*(1-weight)+weight;
                    old.rgb=(old.rgb*old.a*(1-weight)+_BrushColor.rgb*weight)/max(alpha,0.00001);
                    old.a=alpha;
                }
            }
            else
            {
                float value=_Erase>0.5?1:saturate(_PaintValue);
                if(_Channel<0.5)old.r=lerp(old.r,value,weight);
                else if(_Channel<1.5)old.g=lerp(old.g,value,weight);
                else if(_Channel<2.5)old.b=lerp(old.b,value,weight);
                else old.a=lerp(old.a,value,weight);
            }
            return old;
        }
        ENDHLSL
        Pass
        {
            Name "SurfaceBrush"
            HLSLPROGRAM
            #pragma vertex PaintVertex
            #pragma fragment Fragment
            float4 Fragment(Varyings input):SV_Target
            {
                float d=distance(input.world,_BrushCenter.xyz)/max(_BrushRadius,0.00001);
                float weight=(1-smoothstep(1-clamp(_BrushFalloff,0.01,1),1,d))*saturate(_BrushStrength);
                // Leave destination untouched outside the brush, including overlapping UV triangles facing away.
                clip(weight-0.000001);clip(dot(normalize(input.normal),_BrushNormal.xyz)-0.05);
                return ApplyValue(tex2D(_MainTex,input.uv),weight);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ClearSelectedLayer"
            HLSLPROGRAM
            #pragma vertex vert_img
            #pragma fragment ClearFragment
            float4 ClearFragment(v2f_img input):SV_Target {return ApplyValue(tex2D(_MainTex,input.uv),1);}
            ENDHLSL
        }
    }
}
