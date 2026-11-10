// Packs separate source maps into the RGBA channels expected by the surface mask texture.
Shader "Hidden/Game/SurfaceMask"
{
    Properties
    {
        _MetallicMap ("Metallic", 2D) = "white" {}
        _OcclusionMap ("Occlusion", 2D) = "white" {}
        _DetailMap ("Detail mask", 2D) = "white" {}
        _SmoothnessMap ("Smoothness", 2D) = "white" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MetallicMap,
                _OcclusionMap,
                _DetailMap,
                _SmoothnessMap;

            float4 frag(v2f_img input) : SV_Target
            {
                // Output channels: red metallic, green occlusion, blue detail, alpha smoothness.
                return float4(
                    tex2D(_MetallicMap, input.uv).r,
                    tex2D(_OcclusionMap, input.uv).g,
                    tex2D(_DetailMap, input.uv).a,
                    tex2D(_SmoothnessMap, input.uv).a);
            }
            ENDCG
        }
    }
}
