Shader "Civilization/Debug Grid"
{
    Properties { _GridSize ("Grid Size", Float) = 120 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            float _GridSize;

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 grid : TEXCOORD0; };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.grid = input.uv * _GridSize;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 pixels = max(fwidth(input.grid), 0.001);
                float2 distanceToLine = abs(frac(input.grid + 0.5) - 0.5) / pixels;
                float gridCoverage = 1.0 - smoothstep(0.15, 0.9, min(distanceToLine.x, distanceToLine.y));
                return half4(0.65, 0.9, 1.0, gridCoverage * 0.48);
            }
            ENDHLSL
        }
    }
}
