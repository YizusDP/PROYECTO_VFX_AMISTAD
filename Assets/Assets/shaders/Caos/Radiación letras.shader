// Letras radiactivas (URP).
// Cubre toda la malla con la textura base multiplicada por el color base modificable,
// y suma encima el pulso y las ondas radiactivas HDR para que el Bloom las haga brillar.
Shader "Amistad/Radiación Letras"
{
    Properties
    {
        [MainTexture] _BaseMap ("Textura base", 2D) = "white" {}
        [MainColor] _BaseColor ("Color base", Color) = (1, 1, 1, 1)
        [NoScaleOffset] _Ruido ("Ruido (repetible)", 2D) = "gray" {}
        [HDR] _ColorCentro ("Color radiación", Color) = (0.45, 3.2, 0.35, 1)
        [HDR] _ColorBorde ("Color vetas / borde", Color) = (1.6, 7.0, 0.9, 1)
        _Pulso ("Velocidad del pulso", Float) = 0.8
        _Ondas ("Número de ondas", Float) = 3
        _Escala ("Escala del ruido", Float) = 1.4
        _Intensidad ("Intensidad radiación", Range(0, 3)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "LetrasRadiactivas"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            TEXTURE2D(_Ruido);
            SAMPLER(sampler_Ruido);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half4 _ColorCentro;
                half4 _ColorBorde;
                float _Pulso;
                float _Ondas;
                float _Escala;
                float _Intensidad;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float2 uvBase : TEXCOORD3; float semilla : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.uv = v.uv;
                o.uvBase = TRANSFORM_TEX(v.uv, _BaseMap);
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

                float vetas = smoothstep(0.55, 0.8, n);
                float ondas = 0.5 + 0.5 * sin((r * _Ondas - t * _Pulso) * 6.2831 + n * 3.0);
                float pulso = 0.7 + 0.3 * sin(t * _Pulso * 2.1 + i.semilla * 20.0);

                half4 baseTex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uvBase) * _BaseColor;
                half3 radiacion = (_ColorCentro.rgb * (0.2 + 0.8 * ondas * n) + _ColorBorde.rgb * vetas) * (pulso * _Intensidad);

                half3 col = baseTex.rgb + radiacion;
                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
