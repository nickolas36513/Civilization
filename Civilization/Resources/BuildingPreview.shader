Shader "Civilization/Building Preview"
{
    Properties { _Tint ("Tint", Color) = (0.74, 0.82, 0.84, 0.38) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
            CBUFFER_END

            struct Varyings { float4 positionCS : SV_POSITION; half shade : TEXCOORD0; };

            Varyings Vert(float4 positionOS : POSITION, float3 normalOS : NORMAL)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(positionOS.xyz);
                half3 normalWS = TransformObjectToWorldNormal(normalOS);
                output.shade = 0.72h + 0.28h * saturate(dot(normalWS, half3(-0.35h, 0.82h, 0.45h)));
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return half4(_Tint.rgb * input.shade, _Tint.a);
            }
            ENDHLSL
        }
    }
}
