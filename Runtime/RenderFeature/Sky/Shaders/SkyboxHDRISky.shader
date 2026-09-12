Shader "Skybox/EAStudio/HDRISky"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (.5, .5, .5, 1)
        [Gamma] _Exposure ("Exposure", Float) = 0.0
        _Multiplier ("Multiplier", Float) = 1.0
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            TEXTURECUBE(_Tex);
            SAMPLER(sampler_Tex);
            half4 _Tex_HDR;

            TEXTURECUBE(_TexB);
            SAMPLER(sampler_TexB);
            half4 _TexB_HDR;

            CBUFFER_START(UnityPerMaterial)
                float4 _Tint;
                float _Exposure;
                float _Multiplier;
                float _Rotation;
                float _BlendWeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 texcoord : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.texcoord = input.positionOS.xyz;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
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
                col *= exp2(_Exposure) * _Multiplier;

                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
