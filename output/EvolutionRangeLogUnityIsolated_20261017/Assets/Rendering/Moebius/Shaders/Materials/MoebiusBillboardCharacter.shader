Shader "Darwin/Moebius Billboard Character"
{
    Properties
    {
        [MainTexture] _BaseMap("Character PNG (transparent background)", 2D) = "white" {}
        [MainColor] _BaseColor("Tint", Color) = (1,1,1,1)
        _Cutoff("Alpha Cutoff", Range(0.01,1)) = 0.4
        _Brightness("Brightness", Range(0,2)) = 1
        _ShadowStrength("Received Shadow Strength", Range(0,1)) = 0.25
        _ShadowTint("Received Shadow Tint", Color) = (0.8,0.85,0.9,1)
        _PivotY("Local Feet Pivot Y (standard Quad = -0.5)", Float) = -0.5
        [Header(Moebius Illustration Filter)]
        _MoebiusStyleStrength("Style Strength", Range(0,1)) = 0
        _StyleColorLevels("Color Levels", Range(2,12)) = 6
        _StyleSaturation("Color Saturation", Range(0,1.5)) = 0.85
        _StyleInkColor("Illustration Ink", Color) = (0.09,0.065,0.055,1)
        _StyleInkStrength("Internal Ink Strength", Range(0,1)) = 0.35
        _StyleInkWidth("Internal Ink Width (texture pixels)", Range(0,4)) = 1.5
        _StyleInkThreshold("Internal Ink Threshold", Range(0.01,0.5)) = 0.12
        _StyleHatchStrength("Dark Area Hatching", Range(0,1)) = 0.2
        _StyleHatchDensity("Hatching Density", Range(10,300)) = 145
        _StylePaperColor("Paper Tint", Color) = (1,0.96,0.88,1)
        _StylePaperStrength("Paper Grain", Range(0,0.2)) = 0.035
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="TransparentCutout" "Queue"="AlphaTest" }
        Cull Off 
        ZWrite On
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); 
        SAMPLER(sampler_BaseMap);
        float4 _BaseMap_TexelSize;
        float4 _MoebiusBillboardCameraPosition, _MoebiusBillboardCameraForward;
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        half4 _BaseColor, _ShadowTint;
        float _Cutoff, _Brightness, _ShadowStrength, _PivotY;
        half4 _StyleInkColor, _StylePaperColor;
        float _MoebiusStyleStrength, _StyleColorLevels, _StyleSaturation;
        float _StyleInkStrength, _StyleInkWidth, _StyleInkThreshold;
        float _StyleHatchStrength, _StyleHatchDensity, _StylePaperStrength;
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
            float3 positionWS : TEXCOORD1;
            half3 normalWS : TEXCOORD2;
            half fogFactor : TEXCOORD3;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };
        Varyings BillboardVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input,output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 feetWS = TransformObjectToWorld(float3(0,0,0));
            float3 facing = unity_OrthoParams.w > 0.5
                ? -GetViewForwardDir() : GetCameraPositionWS()-feetWS;
            if (_MoebiusBillboardCameraForward.w > 0.5)
                facing = _MoebiusBillboardCameraPosition.w > 0.5
                    ? -_MoebiusBillboardCameraForward.xyz : _MoebiusBillboardCameraPosition.xyz-feetWS;
            facing.y = 0;
            // Exactly overhead has no horizontal direction; use a fixed stable fallback.
            facing = dot(facing,facing) > 0.000001 ? normalize(facing) : float3(0,0,-1);
            float3 worldUp = float3(0,1,0);
            float3 cameraRight = normalize(cross(facing,worldUp));
            // Column lengths preserve positive Transform scale while intentionally ignoring rotation.
            float widthScale = length(float3(unity_ObjectToWorld._m00,unity_ObjectToWorld._m10,unity_ObjectToWorld._m20));
            float heightScale = length(float3(unity_ObjectToWorld._m01,unity_ObjectToWorld._m11,unity_ObjectToWorld._m21));
            output.positionWS = feetWS + cameraRight*input.positionOS.x*widthScale
                + worldUp*(input.positionOS.y-_PivotY)*heightScale;
            output.positionCS = TransformWorldToHClip(output.positionWS);
            output.normalWS = facing;
            output.uv = TRANSFORM_TEX(input.uv,_BaseMap);
            output.fogFactor = ComputeFogFactor(output.positionCS.z);
            return output;
        }
        half4 SampleCharacter(float2 uv)
        {
            half4 character = SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,uv)*_BaseColor;
            clip(character.a-_Cutoff);
            return character;
        }
        ENDHLSL
        Pass
        {
            Name "BillboardForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BillboardVertex
            #pragma fragment CharacterFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Assets/Rendering/Moebius/Shaders/Includes/Features/MoebiusBillboardStyle.hlsl"
            half4 CharacterFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 character = SampleCharacter(input.uv);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                Light mainLight = GetMainLight(ComputeScreenPos(TransformWorldToHClip(input.positionWS)));
                #else
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                #endif
                // Keep illustration colors independent of camera-facing normals; only receive a subtle shadow tint.
                half3 color = ApplyBillboardMoebiusStyle(character,input.uv)*_Brightness*lerp(half3(1,1,1),_ShadowTint.rgb,
                    (1-mainLight.shadowAttenuation)*_ShadowStrength);
                return half4(MixFog(color,input.fogFactor),1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BillboardShadowVertex
            #pragma fragment BillboardShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            float3 _LightDirection;
            float3 _LightPosition;
            Varyings BillboardShadowVertex(Attributes input)
            {
                Varyings output = BillboardVertex(input);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                float3 lightDirection = normalize(_LightPosition-output.positionWS);
                #else
                float3 lightDirection = _LightDirection;
                #endif
                // The quad is double sided; bias toward the lit side of its actual camera-facing plane.
                float3 normalWS = output.normalWS * (dot(output.normalWS,lightDirection) >= 0 ? 1 : -1);
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(output.positionWS,normalWS,lightDirection));
                output.positionCS = ApplyShadowClamping(output.positionCS);
                return output;
            }
            half4 BillboardShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                SampleCharacter(input.uv);
                return 0;
            }
            ENDHLSL
        }
        Pass
        {
            Name "BillboardDepth"
            Tags { "LightMode"="DepthOnly" }
            ColorMask R
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BillboardVertex
            #pragma fragment CharacterDepth
            #pragma multi_compile_instancing
            half4 CharacterDepth(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SampleCharacter(input.uv);
                return input.positionCS.z;
            }
            ENDHLSL
        }
        Pass
        {
            Name "BillboardDepthNormals"
            Tags { "LightMode"="DepthNormalsOnly" }
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex BillboardVertex
            #pragma fragment CharacterNormals
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            half4 CharacterNormals(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                SampleCharacter(input.uv);
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormal = PackNormalOctQuadEncode(normalWS)*0.5+0.5;
                return half4(PackFloat2To888(saturate(octNormal)),0);
                #else
                return half4(normalWS,0);
                #endif
            }
            ENDHLSL
        }
    }
}
