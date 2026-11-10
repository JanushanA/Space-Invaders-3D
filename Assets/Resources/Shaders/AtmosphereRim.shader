// Creates a transparent atmospheric glow at the planet silhouette, weighted toward its sunlit side.
Shader "Game/AtmosphereRim"
{
    Properties
    {
        _Color ("Scattering colour", Color) = (.16, .40, .72, .7)
        _SunDirection ("Direction toward sun", Vector) = (-.5, .6, -.65, 0)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "HDRenderPipeline"
            "Queue" = "Transparent+20"
            "RenderType" = "Transparent"
        }

        Pass
        {
            Tags
            {
                "LightMode" = "ForwardOnly"
            }
            Blend SrcAlpha One
            Cull Back
            ZWrite Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariables.hlsl"
            #include "Packages/com.unity.render-pipelines.high-definition/Runtime/ShaderLibrary/ShaderVariablesFunctions.hlsl"

            // Keep material properties grouped for the render pipeline's material buffer.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _SunDirection;
            CBUFFER_END

            struct Attributes
            {
                float3 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            float4 Frag(Varyings input) : SV_Target
            {
                float3 normal = normalize(input.normalWS);
                float3 viewDirection = GetWorldSpaceNormalizeViewDir(input.positionWS);

                // The surface becomes more transparent when viewed head-on and brighter near its edge.
                float rimOpacity = pow(saturate(1 - dot(normal, viewDirection)), 4.5);

                // Smooth the day/night boundary while retaining a faint glow on the dark side.
                float sunlitAmount = smoothstep(-.35, .55, dot(normal, normalize(_SunDirection.xyz)));
                return float4(_Color.rgb * (sunlitAmount * 1.4 + .06), rimOpacity * _Color.a);
            }

            ENDHLSL
        }
    }
}
