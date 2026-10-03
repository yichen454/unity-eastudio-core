Shader "Hidden/EAStudio/GrassBlade"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        Pass
        {
            Name "Grass"
            Cull Off
            ZWrite On
            ZTest LEqual
            Blend Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "GrassBladeData.hlsl"

            StructuredBuffer<GrassBladeData> _GrassBlades;
            TEXTURE2D(_CloudShadow); SAMPLER(sampler_CloudShadow);
            TEXTURE2D(_GrassWave); SAMPLER(sampler_GrassWave);
            CBUFFER_START(UnityPerMaterial)
            float4 _BladeShape, _Wind, _Effects, _Shading;
            float4 _BottomColor, _TopColor, _ShadowColor, _WaveColor;
            int _BladeOffset, _TopologyLevel;
            CBUFFER_END

            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 blade : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                uint logicalInstance = input.instanceID;
                #if UNITY_ANY_INSTANCING_ENABLED
                    logicalInstance = unity_InstanceID;
                #endif
                GrassBladeData blade = _GrassBlades[(uint)_BladeOffset + logicalInstance];
                uint tip = (uint)_TopologyLevel * 2u;
                float t = input.vertexID == tip ? 1.0 : (float)(input.vertexID / 2u) / (float)_TopologyLevel;
                float side = input.vertexID == tip ? 0.0 : (input.vertexID % 2u == 0u ? -0.5 : 0.5);
                float3 up = blade.normalWidth.xyz;
                float3 facing = float3(blade.facingRandom.x, 0.0, blade.facingRandom.y);
                float3 across = normalize(cross(up, facing));
                float random = blade.facingRandom.z;
                float phase = dot(blade.rootHeight.xz, _Wind.xy) * _Effects.x + _Time.y * _Wind.w + random * TWO_PI;
                float gust = 0.5 + 0.5 * sin(phase);
                float3 displacement = float3(_Wind.x, 0.0, _Wind.y) * _Wind.z * gust + facing * _BladeShape.w;
                // Quadratic curve keeps roots fixed; blade width follows the geometric silhouette.
                float3 position = blade.rootHeight.xyz + up * blade.rootHeight.w * t
                    + displacement * blade.rootHeight.w * t * t
                    + across * (side * (1.0 - t) * blade.normalWidth.w);
                float3 tangent = up + 2.0 * displacement * t;
                output.positionWS = position;
                output.positionCS = TransformWorldToHClip(position);
                output.normalWS = normalize(cross(across, tangent));
                output.blade = float3(t, random, ComputeFogFactor(output.positionCS.z));
                return output;
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 effectUV = input.positionWS.xz / max(_Effects.y, 0.01);
                half cloud = SAMPLE_TEXTURE2D(_CloudShadow, sampler_CloudShadow, effectUV).r;
                half wave = SAMPLE_TEXTURE2D(_GrassWave, sampler_GrassWave, effectUV + _Time.y * _Wind.xy * 0.01).r * _Effects.w;
                half3 color = lerp(_BottomColor.rgb, _TopColor.rgb, saturate(input.blade.x + _Shading.x));
                color = lerp(color, _WaveColor.rgb, wave);
                color *= 1.0 - input.blade.y * 0.2;
                half3 normal = normalize(input.normalWS) * IS_FRONT_VFACE(frontFace, 1.0, -1.0);
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN)
                    float4 shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light light = GetMainLight(shadowCoord);
                half diffuse = saturate(dot(normal, light.direction) * 0.5 + 0.5);
                half attenuation = min(light.shadowAttenuation, lerp(1.0, cloud, _Effects.z));
                color *= lerp(_ShadowColor.rgb, half3(1.0, 1.0, 1.0), attenuation);
                color *= lerp(half3(1.0, 1.0, 1.0), light.color * (0.5 + 0.5 * diffuse), _Shading.y);
                return half4(MixFog(color, input.blade.z), 1.0);
            }
            ENDHLSL
        }
    }
}
