// Draws HDRP particles and beams with a material-selected blend mode and optional soft edges.
Shader "Game/SpaceEffect"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _MainTex ("Texture", 2D) = "white" {}
        _DstBlend ("Destination Blend", Float) = 1
        _SoftEdge ("Soft beam profile", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Tags
            {
                "LightMode" = "ForwardOnly"
            }
            Blend SrcAlpha [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"

            // Keep material properties grouped for the render pipeline's material buffer.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float _SoftEdge;
            CBUFFER_END

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Attributes
            {
                float3 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformWorldToHClip(TransformObjectToWorld(input.positionOS));
                output.color = input.color;
                output.uv = input.uv * _MainTex_ST.xy + _MainTex_ST.zw;
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float4 effectColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv) * _Color * input.color;

                // Fade across the beam width and at both ends of its texture coordinates.
                float beamProfile = pow(saturate(1 - abs(input.uv.y * 2 - 1)), 2);
                beamProfile *= smoothstep(0, .06, input.uv.x) * smoothstep(0, .06, 1 - input.uv.x);

                // A zero soft-edge setting leaves the texture and tint alpha unchanged.
                effectColor.a *= lerp(1, beamProfile, _SoftEdge);
                return effectColor;
            }

            ENDHLSL
        }
    }
}
