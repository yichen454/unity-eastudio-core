Shader "Skybox/EAStudio/HDRISky"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (.5, .5, .5, 1)
        [Gamma] _Exposure ("Exposure", Float) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0.0
        [NoScaleOffset] _Tex ("Cubemap A (HDR)", Cube) = "grey" {}
        [NoScaleOffset] _TexB ("Cubemap B (HDR)", Cube) = "grey" {}
        _BlendWeight ("Blend Weight", Range(0, 1)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            Name "SkyboxPass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            #if !defined(UNITY_COLORSPACE_GAMMA)
            #define unity_ColorSpaceDouble half4(4.59479380, 4.59479380, 4.59479380, 2.0)
            #else
            #define unity_ColorSpaceDouble half4(2.0, 2.0, 2.0, 2.0)
            #endif

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);
            half4 _Tex_HDR;

            TEXTURECUBE(_TexB);
            SAMPLER(sampler_TexB);
            half4 _TexB_HDR;

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Exposure;
                float _Rotation;
                float _BlendWeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.texcoord = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 dir = normalize(input.texcoord);

                float rad = radians(_Rotation);
                float s, c;
                sincos(rad, s, c);
                float3 rotatedDir;
                rotatedDir.x = dir.x * c + dir.z * s;
                rotatedDir.y = dir.y;
                rotatedDir.z = -dir.x * s + dir.z * c;

                half4 rawTexA = SAMPLE_TEXTURECUBE_LOD(_Tex, sampler_Tex, rotatedDir, 0);
                half3 col = DecodeHDREnvironment(rawTexA, _Tex_HDR);

                if (_BlendWeight > 0.001)
                {
                    half4 rawTexB = SAMPLE_TEXTURECUBE_LOD(_TexB, sampler_TexB, rotatedDir, 0);
                    half3 colB = DecodeHDREnvironment(rawTexB, _TexB_HDR);
                    col = lerp(col, colB, _BlendWeight);
                }

                // Standard Unity Skybox tint scaling: #808080 (0.5) * unity_ColorSpaceDouble is neutral 1.0
                col = col * _Tint.rgb * unity_ColorSpaceDouble.rgb;

                // Pure linear exposure multiplier (1.0 = normal, 2.0 = 2x brighter)
                col *= _Exposure;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
