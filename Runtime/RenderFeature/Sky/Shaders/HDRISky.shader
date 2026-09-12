Shader "Hidden/EAStudio/HDRISky"
{
    Properties
    {
        _CubemapA ("Cubemap A", Cube) = "_Skybox" {}
        _CubemapB ("Cubemap B", Cube) = "_Skybox" {}
        _BlendWeight ("Blend Weight", Range(0, 1)) = 0.0
        _SkyRotation ("Rotation", Float) = 0.0
        _Exposure ("Exposure", Float) = 0.0
        _Multiplier ("Multiplier", Float) = 1.0
        _Tint ("Tint", Color) = (1, 1, 1, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Background"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Background"
        }

        Pass
        {
            Name "HDRISkyPass"
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/EntityLighting.hlsl"

            TEXTURECUBE(_CubemapA);
            SAMPLER(sampler_CubemapA);
            half4 _CubemapA_HDR;

            TEXTURECUBE(_CubemapB);
            SAMPLER(sampler_CubemapB);
            half4 _CubemapB_HDR;

            CBUFFER_START(UnityPerMaterial)
                float _BlendWeight;
                float _SkyRotation;
                float _Exposure;
                float _Multiplier;
                float4 _Tint;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID, UNITY_RAW_FAR_CLIP_VALUE);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.positionCS.xy / _ScaledScreenParams.xy;
                float3 worldPos = ComputeWorldSpacePosition(uv, UNITY_RAW_FAR_CLIP_VALUE, UNITY_MATRIX_I_VP);
                float3 worldDir = normalize(worldPos - _WorldSpaceCameraPos.xyz);

                float rad = radians(_SkyRotation);
                float s, c;
                sincos(rad, s, c);
                float3 rotatedDir;
                rotatedDir.x = worldDir.x * c + worldDir.z * s;
                rotatedDir.y = worldDir.y;
                rotatedDir.z = -worldDir.x * s + worldDir.z * c;

                half4 rawA = SAMPLE_TEXTURECUBE_LOD(_CubemapA, sampler_CubemapA, rotatedDir, 0);
                half3 colA = DecodeHDREnvironment(rawA, _CubemapA_HDR);

                half4 rawB = SAMPLE_TEXTURECUBE_LOD(_CubemapB, sampler_CubemapB, rotatedDir, 0);
                half3 colB = DecodeHDREnvironment(rawB, _CubemapB_HDR);

                half3 finalCol = lerp(colA, colB, _BlendWeight);

                finalCol *= exp2(_Exposure) * _Multiplier * _Tint.rgb;

                return half4(finalCol, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
