Shader "Darwin/Population Water Sprite"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _WaterSpriteLine ("Sprite immersion line", Float) = -10000
        _WaterHeight ("Water surface height", Float) = -10000
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Silhouette Cutoff", Range(0,1)) = 0.5
        [HideInInspector] _RendererColor ("Renderer Color", Color) = (1,1,1,1)
        [HideInInspector] _AlphaTex ("External Alpha", 2D) = "white" {}
        [HideInInspector] _EnableExternalAlpha ("Enable External Alpha", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="AlphaTest" "RenderType"="TransparentCutout" "CanUseSpriteAtlas"="True" }
        Cull Off
        ZWrite On
        ZTest LEqual
        Blend Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/Shaders/2D/Include/Core2D.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half _Cutoff;
            float _WaterHeight;
            float _WaterSpriteLine;
        CBUFFER_END
        struct Attributes
        {
            float3 positionOS : POSITION;
            float3 normalOS : NORMAL;
            half4 color : COLOR;
            float2 uv : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            half3 normalWS : TEXCOORD1;
            float worldHeight : TEXCOORD2;
            float spriteHeight : TEXCOORD3;
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings SpriteVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            SetUpSpriteInstanceProperties();
            input.positionOS = UnityFlipSprite(input.positionOS, unity_SpriteProps.xy);
            output.positionCS = TransformObjectToHClip(input.positionOS);
            output.spriteHeight = input.positionOS.y;
            output.worldHeight = TransformObjectToWorld(input.positionOS).y;
            output.uv = input.uv;
            output.color = input.color * _Color * unity_SpriteColor;
            output.normalWS = TransformObjectToWorldNormal(input.normalOS);
            return output;
        }
        half4 SampleSilhouette(Varyings input)
        {
            half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * input.color;
            clip(color.a - _Cutoff);
            clip(input.worldHeight - _WaterHeight);
            clip(input.spriteHeight - _WaterSpriteLine);
            color.rgb = lerp(color.rgb * half3(.7,.9,.95), color.rgb, saturate((input.worldHeight - _WaterHeight) * 12));
            return color;
        }
        half4 SpriteColor(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            half4 color = SampleSilhouette(input);
            // The animal body is solid; texture alpha cuts out only its shape.
            return half4(color.rgb, 1);
        }
        half4 SpriteDepth(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            SampleSilhouette(input);
            return 0;
        }
        half4 SpriteNormal(Varyings input) : SV_Target
        {
            UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
            SampleSilhouette(input);
            float3 normalWS = normalize(input.normalWS);
            #if defined(_GBUFFER_NORMALS_OCT)
                float2 oct = saturate(PackNormalOctQuadEncode(normalWS) * 0.5 + 0.5);
                return half4(PackFloat2To888(oct), 0);
            #else
                return half4(normalWS, 0);
            #endif
        }
        ENDHLSL

        Pass
        {
            Name "Forward"
            Tags { "LightMode"="UniversalForward" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SpriteVertex
            #pragma fragment SpriteColor
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SpriteVertex
            #pragma fragment SpriteDepth
            #pragma multi_compile_instancing
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SpriteVertex
            #pragma fragment SpriteNormal
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            ENDHLSL
        }
    }
}
