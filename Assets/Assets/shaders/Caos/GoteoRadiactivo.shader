// Líquido radiactivo que sale por DEBAJO de las letras del título #AMISTAD (URP, sin luz).
// Un rectángulo por cada "pata" de la letra (tramo donde toca la base). En el pixel se dibuja con distancias (SDF):
//   - una banda espesa pegada a la base, con la orilla de abajo ondulada (el líquido que se sale),
//   - varios chorros gruesos que nacen de la banda (unión suave: parece una sola masa que se derrite),
//   - cada chorro se alarga, se le hincha la gota de la punta, la suelta (cae con gravedad) y vuelve a crecer.
// Estilo caricatura: contorno oscuro, relleno neón (HDR, lo hace brillar el Bloom) y centro más claro (brillo espeso).
// Datos en los vértices: uv = posición en el rectángulo (v = 0 arriba), uv2 = (semilla, retraso), uv3 = (medio ancho
// de la pata, largo máximo de los chorros) en unidades. _TiempoGoteo y _Brillo los anima el Timeline: el resultado es
// el mismo en el mismo segundo aunque se adelante o regrese.
Shader "Amistad/Goteo Radiactivo"
{
    Properties
    {
        _TiempoGoteo ("Segundos desde que empieza (lo anima el Timeline)", Float) = 0
        _Brillo ("Brillo (lo baja el fundido)", Range(0, 2)) = 1
        _Gravedad ("Gravedad de las gotas (u/s2)", Float) = 150
        _Margen ("Espacio a los lados del tramo (u)", Float) = 7
        _Espesor ("Espesor de la banda (u)", Float) = 4.5
        _SepChorros ("Separación de los chorros (u)", Float) = 7.5
        [HDR] _ColorLiquido ("Relleno", Color) = (1.2, 6.2, 0.3, 1)
        [HDR] _ColorCentro ("Centro claro", Color) = (3.0, 9.0, 1.6, 1)
        _ColorContorno ("Contorno", Color) = (0.0, 0.09, 0.01, 1)
        _Contorno ("Grosor del contorno (u)", Float) = 0.8
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Goteo"
            Tags { "LightMode" = "UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _TiempoGoteo, _Brillo, _Gravedad, _Margen, _Espesor, _SepChorros, _Contorno;
                half4 _ColorLiquido, _ColorCentro, _ColorContorno;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float2 uv3 : TEXCOORD2; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float2 dat : TEXCOORD1; float2 med : TEXCOORD2; };

            V vert(A a)
            {
                V o;
                o.pos = TransformObjectToHClip(a.pos.xyz);
                o.uv = a.uv; o.dat = a.uv2; o.med = a.uv3;
                return o;
            }

            float H1(float n) { return frac(sin(n * 91.3458) * 47453.5453); }

            // unión suave de dos distancias (k = qué tan "pegajosa" es la unión)
            float SMin(float a, float b, float k)
            {
                float h = saturate(0.5 + 0.5 * (b - a) / k);
                return lerp(b, a, h) - k * h * (1.0 - h);
            }

            // tubo vertical que va de y0 a y1 y se adelgaza de a0 a a1
            float Tubo(float2 p, float y0, float y1, float a0, float a1)
            {
                float k = saturate((p.y - y0) / max(y1 - y0, 0.01));
                float hw = lerp(a0, a1, sqrt(k));
                return max(abs(p.x) - hw, max(y0 - p.y, p.y - y1));
            }

            half4 frag(V i) : SV_Target
            {
                float semilla = i.dat.x, retraso = i.dat.y;
                float W = i.med.x, Lmax = i.med.y;
                float altoRect = Lmax * 3.4;
                float2 p = float2((i.uv.x - 0.5) * 2.0 * (W + _Margen), i.uv.y * altoRect);  // x: 0 al centro; y hacia abajo

                float tg = _TiempoGoteo - retraso;
                clip(tg);

                // banda que se sale por la base: crece al empezar y su orilla de abajo ondula despacio
                float sale = saturate(tg / 0.45);
                float ola = 1.0 * sin(p.x * 0.5 + semilla * 6.0 + tg * 1.7) + 0.5 * sin(p.x * 1.3 - tg * 2.3);
                float esp = _Espesor * sale + ola * sale;
                float d = max(abs(p.x) - (W + 1.2 * sale), max(-p.y, p.y - esp));
                d = max(d, -p.y);

                // chorros
                int n = clamp((int)floor(2.0 * W / _SepChorros), 1, 6);
                float dGotas = 1e5;
                [loop] for (int k = 0; k < 6; k++)
                {
                    if (k >= n) break;
                    float s = semilla * 13.7 + k * 3.17;
                    float cx = -W + (k + 0.5) * (2.0 * W / n) + (H1(s) - 0.5) * 0.4 * (2.0 * W / n);
                    float Lk = Lmax * lerp(0.45, 1.0, H1(s + 1.0));
                    float P = lerp(1.1, 2.0, H1(s + 2.0));
                    float wk = lerp(1.8, 3.0, H1(s + 3.0)) * min(1.0, W / 3.0 + 0.4);
                    float t = tg - 0.15 - H1(s + 4.0) * 0.7;
                    if (t <= 0.0) continue;
                    float c = floor(t / P), f = frac(t / P);
                    const float suelta = 0.8;
                    float Lmin = Lk * 0.45;
                    float crece = 1.0 - pow(1.0 - saturate(f / suelta), 2.0);
                    float L = f < suelta ? lerp(c < 0.5 ? 0.0 : Lmin, Lk, crece)
                                         : lerp(Lmin, Lk * 0.6, (f - suelta) / (1.0 - suelta));
                    float rb = wk * lerp(1.05, 1.5, f < suelta ? crece : 0.0);
                    float2 q = p - float2(cx, 0.0);
                    float dk = Tubo(q, 0.0, esp + L, wk * 1.15, wk * 0.65);
                    dk = SMin(dk, length(q - float2(0.0, esp + L)) - rb, wk * 0.9);   // gota de la punta
                    d = SMin(d, dk, 2.6);
                    // gota que cae (la de este ciclo si ya se soltó, si no la del anterior)
                    float tc = f >= suelta ? (f - suelta) * P : (c >= 1.0 ? (f + 1.0 - suelta) * P : -1.0);
                    if (tc >= 0.0)
                    {
                        float yd = esp + Lk + rb + 0.5 * _Gravedad * tc * tc;
                        float2 g = q - float2(0.0, yd);
                        g.y *= 0.82;                                      // se alarga un poco al caer
                        dGotas = min(dGotas, length(g) - wk * 1.1);
                    }
                }
                d = min(d, dGotas);

                clip(-d);
                half3 col = _ColorLiquido.rgb;
                col = lerp(col, _ColorCentro.rgb, step(d, -_Contorno * 2.6));   // centro claro (líquido espeso)
                col = lerp(col, _ColorContorno.rgb, step(-_Contorno, d));       // contorno oscuro
                return half4(col * _Brillo, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
