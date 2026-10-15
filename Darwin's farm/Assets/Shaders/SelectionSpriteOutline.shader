Shader "DarwinFarm/SelectionSpriteOutline"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite", 2D) = "white" {}
        _OutlineColor ("Outline", Color) = (0.2, 1, 1, 1)
        _Width ("Width in texture pixels", Float) = 2
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" "CanUseSpriteAtlas"="True" }
        Cull Off ZWrite Off ZTest LEqual
        Offset -1, -1
        Blend SrcAlpha OneMinusSrcAlpha
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_TexelSize;
                float4 _OutlineColor;
                float _Width;
            CBUFFER_END
            Varyings vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SetUpSpriteInstanceProperties();
                Varyings o; o.positionCS = TransformObjectToHClip(UnityFlipSprite(input.positionOS.xyz, unity_SpriteProps.xy));
                o.uv = input.uv; o.color = input.color * unity_SpriteColor; return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                float a = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).a;
                float2 d = max(abs(ddx(i.uv)) + abs(ddy(i.uv)), _MainTex_TexelSize.xy) * _Width;
                float inside = 1;
                [unroll] for (int x = -1; x <= 1; x++)
                    [unroll] for (int y = -1; y <= 1; y++)
                        inside = min(inside, SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv + float2(x,y)*d).a);
                half4 color = _OutlineColor;
                color.a *= a * (1-inside) * i.color.a;
                return color;
            }
            ENDHLSL
        }
    }
}
