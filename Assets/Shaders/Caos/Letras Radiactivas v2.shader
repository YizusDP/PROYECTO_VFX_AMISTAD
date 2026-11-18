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
        [Header(Liquido que escurre solo en el titulo final)]
        _Escurrido ("Cuánto ha escurrido (0-1)", Range(0, 1)) = 0
        _BrilloEscurrido ("Brillo del líquido", Range(0, 2)) = 1
        _YMinOS ("Base de la letra (objeto)", Float) = 0
        _YMaxOS ("Tope de la letra (objeto)", Float) = 55
        _AnchoColumna ("Separación de los chorros (objeto)", Float) = 6
        [HDR] _ColorLiquido ("Líquido: brillo", Color) = (1.3, 6.5, 0.35, 1)
        _ColorLiquidoOscuro ("Líquido: contorno", Color) = (0.0, 0.1, 0.01, 1)
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
            float _Escurrido, _BrilloEscurrido, _YMinOS, _YMaxOS, _AnchoColumna;
            half4 _ColorLiquido, _ColorLiquidoOscuro;
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

                // ---------- líquido radiactivo que escurre desde arriba de la letra ----------
                // Chorros por columnas (en X del objeto): cada uno baja hasta un largo al azar; con _Escurrido = 1 los
                // más largos llegan a la base y gotean (las gotas que caen son partículas). Gota redonda en la punta.
                if (_Escurrido > 0.001)
                {
                    float alto = max(1.0, _YMaxOS - _YMinOS);
                    float d = saturate((_YMaxOS - i.posOS.y) / alto);              // 0 arriba, 1 abajo
                    float cel = i.posOS.x / _AnchoColumna + i.semilla * 7.0;
                    float id = floor(cel);
                    float fx = frac(cel) - 0.5;
                    float3 r = Hash3(float3(id, i.semilla * 31.0, 4.7));
                    float retraso = r.y * 0.35;
                    float largo = lerp(0.25, 1.08, r.x) * saturate((_Escurrido - retraso) / (1.0 - retraso));
                    float ancho = lerp(0.24, 0.4, r.z);
                    float adelgaza = 1.0 - 0.35 * saturate(d / max(largo, 0.01));
                    float w = ancho * adelgaza;
                    float chorro = step(abs(fx), w) * step(d, largo);
                    float chorroIn = step(abs(fx), w * 0.7) * step(d, largo - 0.01);
                    // gota en la punta (redonda en unidades del objeto)
                    float2 gp = float2(fx * _AnchoColumna, (d - largo) * alto);
                    float rg = ancho * _AnchoColumna * 1.3;
                    float hay = step(0.02, largo);
                    float gota = step(length(gp), rg) * hay;
                    float gotaIn = step(length(gp), rg * 0.72) * hay;
                    // capa que cubre la parte de arriba de la letra
                    float capa = step(d, 0.08 * saturate(_Escurrido * 3.0));
                    float capaIn = step(d, 0.08 * saturate(_Escurrido * 3.0) - 0.015);
                    // solo en la cara de enfrente/atrás y arriba (en los lados las columnas saldrían como rayas)
                    float caras = saturate(frente + step(0.5, nOS.y));
                    float liq = saturate(chorro + gota + capa) * caras;
                    float dentro = saturate(chorroIn + gotaIn + capaIn);
                    // estilo caricatura: contorno oscuro + relleno neón + brillo que baja por el centro
                    float centro = 1.0 - saturate(abs(fx) / max(w * 0.7, 0.01));
                    float baja = step(0.82, frac(d * 5.0 - t * 1.1 + r.x * 3.0)) * chorroIn;
                    half3 liqCol = lerp(_ColorLiquidoOscuro.rgb, _ColorLiquido.rgb, dentro);
                    liqCol = lerp(liqCol, _ColorLiquido.rgb * 1.6 + 0.6, saturate(step(0.55, centro) * chorroIn * 0.6 + baja + step(length(gp - float2(-rg * 0.3, rg * 0.3)), rg * 0.25) * gotaIn));
                    col = lerp(col, liqCol * _BrilloEscurrido, liq);
                }

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
