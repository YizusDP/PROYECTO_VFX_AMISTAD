// Mancha de hollín / suciedad en el piso (URP).
// Quad plano sobre el suelo que OSCURECE lo que hay debajo con blend multiplicativo:
//   resultado = colorShader * colorFondo   (Blend DstColor Zero)
// La forma es procedural: círculo deformado con ruido, así cada mancha se ve distinta.
// La variación sale de la posición del objeto en el mundo (no hace falta un material por mancha).
Shader "Amistad/Mancha Suelo"
{
    Properties
    {
        [NoScaleOffset] _Ruido ("Ruido (repetible)", 2D) = "gray" {}
        _Color ("Color de la mancha", Color) = (0.18, 0.13, 0.10, 1)
        _Fuerza ("Fuerza", Range(0, 1)) = 0.85
        _Escala ("Escala del ruido", Float) = 1.2
        _Borde ("Suavidad del borde", Range(0.05, 1)) = 0.45
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-60" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Mancha"
            Tags { "LightMode" = "UniversalForward" }
            Blend DstColor Zero
            ZWrite Off
            Cull Off
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Ruido);
            SAMPLER(sampler_Ruido);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Fuerza;
                float _Escala;
                half _Borde;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float semilla : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv;
                float3 centro = TransformObjectToWorld(float3(0, 0, 0));
                o.semilla = frac(sin(dot(centro.xz, float2(12.9898, 78.233))) * 43758.5453);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float R(float2 uv) { return SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, uv).r; }

            half4 frag(Varyings i) : SV_Target
            {
                float2 c = i.uv * 2.0 - 1.0;
                float r = length(c);
                float2 suv = i.uv * _Escala + i.semilla * 5.7;
                float n = R(suv) * 0.6 + R(suv * 2.7 + 0.31) * 0.4;
                float borde = r + (n - 0.5) * 0.9;                 // contorno irregular
                float m = 1.0 - smoothstep(1.0 - _Borde, 1.0, borde);
                m *= lerp(0.55, 1.0, R(suv * 5.1 + 0.7));          // manchado por dentro
                half3 col = lerp(half3(1, 1, 1), _Color.rgb, saturate(m * _Fuerza));
                col = MixFogColor(col, half3(1, 1, 1), i.fog);      // con niebla tiende a 1 (no oscurece)
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
