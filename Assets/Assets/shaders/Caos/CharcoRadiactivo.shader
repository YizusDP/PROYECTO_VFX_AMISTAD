// Charco radiactivo (URP).
// Quad plano en el suelo, aditivo y con color HDR (> 1) para que el Bloom lo haga brillar.
//  - Contorno irregular con ruido (cada charco distinto según su posición en el mundo).
//  - Ondas que salen del centro y un pulso general de intensidad (_Pulso).
//  - Borde más brillante, como líquido que se evapora.
// Con niebla se apaga (aditivo hacia negro) para que los charcos lejanos no brillen de más.
Shader "Amistad/Charco Radiactivo"
{
    Properties
    {
        [NoScaleOffset] _Ruido ("Ruido (repetible)", 2D) = "gray" {}
        [HDR] _ColorCentro ("Color centro", Color) = (0.45, 3.2, 0.35, 1)
        [HDR] _ColorBorde ("Color borde", Color) = (1.6, 7.0, 0.9, 1)
        _Pulso ("Velocidad del pulso", Float) = 0.8
        _Ondas ("Número de ondas", Float) = 3
        _Escala ("Escala del ruido", Float) = 1.4
        _Intensidad ("Intensidad", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-40" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Charco"
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off
            Offset -2, -2

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Ruido);
            SAMPLER(sampler_Ruido);

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorCentro;
                half4 _ColorBorde;
                float _Pulso;
                float _Ondas;
                float _Escala;
                float _Intensidad;
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
                float t = _Time.y;
                float2 c = i.uv * 2.0 - 1.0;
                float r = length(c);
                float2 suv = i.uv * _Escala + i.semilla * 7.3;
                float n1 = R(suv + float2(t * 0.03, t * 0.05));
                float n2 = R(suv * 2.3 - float2(t * 0.06, -t * 0.02));
                float n = n1 * 0.6 + n2 * 0.4;

                float borde = r + (n - 0.5) * 0.55;
                float charco = 1.0 - smoothstep(0.62, 0.8, borde);
                float rim = smoothstep(0.42, 0.72, borde) * charco;
                float ondas = 0.5 + 0.5 * sin((r * _Ondas - t * _Pulso) * 6.2831 + n * 3.0);
                float pulso = 0.7 + 0.3 * sin(t * _Pulso * 2.1 + i.semilla * 20.0);

                half3 col = _ColorCentro.rgb * (0.3 + 0.7 * ondas * n) + _ColorBorde.rgb * rim;
                col *= charco * pulso * _Intensidad;
                col = MixFogColor(col, half3(0, 0, 0), i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
