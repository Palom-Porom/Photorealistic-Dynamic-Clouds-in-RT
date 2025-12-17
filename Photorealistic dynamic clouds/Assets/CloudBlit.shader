Shader "Hidden/CloudBlit"
{
    Properties {}

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

    TEXTURE2D(_Source);
    SAMPLER(sampler_Source);

    struct Attributes { uint vertexID : SV_VertexID; };
    struct Varyings { float4 positionCS : SV_Position; float2 uv : TEXCOORD0; };

    Varyings Vert(Attributes v)
    {
        Varyings o;
        o.positionCS = GetFullScreenTriangleVertexPosition(v.vertexID);
        o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
        return o;
    }

    float4 Frag(Varyings i) : SV_Target
    {
        return float4(_Source.Sample(sampler_Source, i.uv).rgb, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
}
