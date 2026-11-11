// Letras radiactivas v2 (URP) — "cristal radiactivo" para #AMISTAD.
// Cuerpo verde oscuro + grietas de energía (Voronoi 3D en espacio objeto: no se estira con las UV)
// + brillo interior con profundidad (parallax) en la cara frontal, lados más oscuros con luz toon
// (se lee la extrusión), borde brillante (fresnel) y un destello que recorre la palabra.
// Solo las grietas, el borde y el destello son HDR: el Bloom los hace brillar y el cuerpo queda oscuro (contraste y lectura).
Shader "Amistad/Letras Radiactivas v2"
{
    Properties
    {
        [Header(Colores)]
        _ColorCuerpo ("Cuerpo (oscuro)", Color) = (0.02, 0.07, 0.03, 1)
        _ColorLados ("Lados de la extrusión", Color) = (0.03, 0.11, 0.04, 1)
        [HDR] _ColorNucleo ("Brillo interior", Color) = (0.25, 2.0, 0.18, 1)
        [HDR] _ColorVetas ("Grietas de energía", Color) = (1.6, 6.5, 0.5, 1)
        [HDR] _ColorBorde ("Borde (fresnel)", Color) = (0.7, 4.0, 0.5, 1)
        [Header(Grietas)]
        _EscalaCeldas ("Tamaño de celdas (1/u)", Float) = 0.11
        _GrosorVetas ("Grosor de grietas", Range(0.01, 0.4)) = 0.09
        _Profundidad ("Profundidad del brillo interior (u)", Float) = 9
        _Flujo ("Velocidad del flujo", Float) = 0.12
        [Header(Pulso y destello)]
        _Pulso ("Pulso (Hz)", Float) = 0.45
        _FuerzaPulso ("Fuerza del pulso", Range(0, 1)) = 0.35
        _EjeBarrido ("Eje del destello (mundo)", Vector) = (1, 0, 0, 0)
        _LargoBarrido ("Largo del recorrido (u)", Float) = 700
        _VelocidadBarrido ("Velocidad del destello", Float) = 0.28
        _AnchoBarrido ("Ancho del destello", Range(0.01, 0.5)) = 0.07
        [HDR] _ColorBarrido ("Color del destello", Color) = (2.5, 6.0, 2.0, 1)
        [Header(Borde y luz)]
        _PotenciaBorde ("Finura del borde", Range(0.5, 8)) = 3
        _LuzToon ("Cuánto lo afecta el sol", Range(0, 1)) = 0.6
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            half4 _ColorCuerpo, _ColorLados, _ColorNucleo, _ColorVetas, _ColorBorde, _ColorBarrido;
            float _EscalaCeldas, _GrosorVetas, _Profundidad, _Flujo;
            float _Pulso, _FuerzaPulso, _LargoBarrido, _VelocidadBarrido, _AnchoBarrido;
            float4 _EjeBarrido;
            float _PotenciaBorde, _LuzToon;
        CBUFFER_END
        ENDHLSL

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
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posOS : TEXCOORD0;
                float3 nOS : TEXCOORD1;
                float3 posWS : TEXCOORD2;
                float3 nWS : TEXCOORD3;
                float3 vOS : TEXCOORD4;   // hacia la cámara, en espacio objeto
                float semilla : TEXCOORD5;
                float fog : TEXCOORD6;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.posOS = v.positionOS.xyz;
                o.nOS = v.normalOS;
                o.posWS = p.positionWS;
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.vOS = TransformWorldToObject(GetCameraPositionWS()) - v.positionOS.xyz;
                float3 centro = TransformObjectToWorld(float3(0, 0, 0));
                o.semilla = frac(sin(dot(centro.xz, float2(12.9898, 78.233))) * 43758.5453);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float3 Hash3(float3 p)
            {
                p = float3(dot(p, float3(127.1, 311.7, 74.7)), dot(p, float3(269.5, 183.3, 246.1)), dot(p, float3(113.5, 271.9, 124.6)));
                return frac(sin(p) * 43758.5453);
            }

            // Voronoi 3D animado: x = distancia al centro más cercano, y = F2 - F1 (0 en las grietas)
            float2 Voronoi(float3 p, float t)
            {
                float3 c = floor(p), f = frac(p);
                float f1 = 8.0, f2 = 8.0;
                [unroll] for (int z = -1; z <= 1; z++)
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float3 g = float3(x, y, z);
                    float3 h = Hash3(c + g);
                    float3 r = g + 0.5 + 0.42 * sin(t + 6.2831 * h) - f;
                    float d = dot(r, r);
                    if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                }
                return float2(sqrt(f1), sqrt(f2) - sqrt(f1));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float3 n = normalize(i.nWS);
                float3 v = normalize(GetCameraPositionWS() - i.posWS);
                float3 nOS = normalize(i.nOS);
                float frente = smoothstep(0.55, 0.9, abs(nOS.z));          // la extrusión va en Z: frente/atrás vs lados

                float tf = t * _Flujo * 6.2831;
                float3 q = i.posOS * _EscalaCeldas + i.semilla * 13.0;
                // grietas en la superficie
                float2 vor = Voronoi(q, tf);
                float vetas = 1.0 - smoothstep(0.0, _GrosorVetas, vor.y);
                // brillo interior: otra capa de celdas "más adentro" (parallax hacia la cámara), suave
                float3 vdir = normalize(i.vOS);
                float2 vorIn = Voronoi(q - vdir * _Profundidad * _EscalaCeldas, tf * 0.7 + 1.7);
                float interior = (1.0 - smoothstep(0.0, _GrosorVetas * 3.5, vorIn.y)) * 0.6 + (1.0 - saturate(vorIn.x * 1.4)) * 0.4;

                float pulso = 1.0 - _FuerzaPulso + _FuerzaPulso * (0.5 + 0.5 * sin(t * _Pulso * 6.2831 + i.semilla * 6.0));

                // luz toon (2 bandas) del sol + ambiente
                Light sol = GetMainLight();
                float ndl = dot(n, sol.direction);
                float banda = lerp(0.35, 1.0, smoothstep(0.0, 0.08, ndl));
                half3 luz = lerp(1.0.xxx, sol.color * banda + SampleSH(n) * 0.6, _LuzToon);

                half3 cuerpo = lerp(_ColorLados.rgb, _ColorCuerpo.rgb, frente) * luz;
                half3 emision = 0;
                emision += _ColorNucleo.rgb * interior * frente * pulso;                 // cara: cristal con energía adentro
                emision += _ColorVetas.rgb * vetas * lerp(0.35, 1.0, frente) * pulso;    // grietas (más tenues en los lados)
                // lados: energía que sube
                float sube = frac(i.posOS.y * _EscalaCeldas * 0.5 - t * _Flujo * 2.0 + vor.x * 0.6);
                emision += _ColorNucleo.rgb * (1.0 - frente) * smoothstep(0.85, 1.0, sube) * 0.8 * pulso;
                // borde
                float fres = pow(1.0 - saturate(dot(n, v)), _PotenciaBorde);
                emision += _ColorBorde.rgb * fres;
                // destello que recorre la palabra
                float s = dot(i.posWS, normalize(_EjeBarrido.xyz));
                float ph = frac(s / _LargoBarrido - t * _VelocidadBarrido);
                float barrido = saturate(1.0 - abs(ph - 0.5) / _AnchoBarrido);
                emision += _ColorBarrido.rgb * barrido * barrido * (0.4 + 0.6 * frente);

                half3 col = cuerpo + emision;
                col = MixFog(col, i.fog);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            float4 v(float4 p : POSITION) : SV_POSITION { return TransformObjectToHClip(p.xyz); }
            half f() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex v
            #pragma fragment f
            struct A { float4 p : POSITION; float3 n : NORMAL; };
            struct V { float4 p : SV_POSITION; float3 n : TEXCOORD0; };
            V v(A a) { V o; o.p = TransformObjectToHClip(a.p.xyz); o.n = TransformObjectToWorldNormal(a.n); return o; }
            half4 f(V i) : SV_Target { return half4(NormalizeNormalPerPixel(i.n), 0); }
            ENDHLSL
        }
    }
    FallBack Off
}
