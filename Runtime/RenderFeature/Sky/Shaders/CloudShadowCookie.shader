Shader "Hidden/EAStudio/CloudShadowCookie"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            Name "CloudShadowCookiePass"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _CloudWindOffset;
                float _Coverage;
                float _RevInvCoverage;
                float _ShadowStrength;
                float _Scale;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = GetFullScreenTriangleVertexPosition(input.vertexID);
                output.uv = GetFullScreenTriangleTexCoord(input.vertexID);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                // Top-down orthographic light space projection with wind animation
                float2 uv = input.uv * _Scale + _CloudWindOffset.xy;
                float rawNoise = SAMPLE_TEXTURE2D(_BaseTex, sampler_LinearRepeat, uv).r;

                // Cloud coverage & density
                float density = max(0.0, rawNoise - _Coverage) * _RevInvCoverage;
                density = smoothstep(0.0, 0.6, density);

                // Directional Light Cookie: 1.0 = fully lit, (1 - shadowStrength) = in cloud shadow
                float shadow = saturate(lerp(1.0, 1.0 - _ShadowStrength, density));

                return float4(shadow, shadow, shadow, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
