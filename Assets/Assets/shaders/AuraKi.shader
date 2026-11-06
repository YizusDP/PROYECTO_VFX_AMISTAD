// Aura de Ki para Goku (URP).
// Se dibuja sobre un "cascarón": una copia de la malla con piel (SkinnedMeshRenderer, mismos huesos) que la
// herramienta Tools > Amistad > Goku: aura Ki y SSJ crea junto a cada malla de Goku_Base y Goku_SSJ.
//   Vértices: se empujan por la normal (grosor) y hacia arriba según el ruido -> lenguas de fuego que suben.
//   Píxeles:  fresnel (junto al cuerpo = más fuerte) roto con el ruido -> silueta nítida, 3 bandas planas de color
//             y una línea en la orilla (mismo estilo caricatura que "Amistad/Fuego VFX").
// _Carga, _Mezcla, _Destello y _TiempoAura NO se editan en el material: los pone el script AuraKi con un
// MaterialPropertyBlock y a su vez los anima el Timeline (pista "Goku · aura Ki"). Así el Timeline se puede
// adelantar y regresar y el aura siempre se ve igual en el mismo segundo.
// El mismo shader sirve para dos materiales: Aura_Llamas (grueso, lenguas largas) y Aura_Brillo (pegado al cuerpo).
Shader "Amistad/Aura Ki"
{
    Properties
    {
        [Header(Los anima el Timeline con el script AuraKi)]
        _Carga ("Carga (0 = apagada, 1 = completa)", Range(0, 1)) = 1
        _Mezcla ("Ki (0) a Super Saiyajin (1)", Range(0, 1)) = 1
        _Destello ("Destello de la transformacion", Range(0, 1)) = 0
        _TiempoAura ("Tiempo (lo pone el script)", Float) = 0

        [Header(Forma)]
        [NoScaleOffset] _Ruido ("Ruido (repetible)", 2D) = "gray" {}
        _Grosor ("Cuanto se separa del cuerpo (u)", Float) = 1.6
        _Llamas ("Largo de las lenguas hacia arriba (u)", Float) = 6
        _EscalaRuido ("Escala del ruido (por unidad)", Float) = 0.09
        _Velocidad ("Velocidad de subida", Float) = 1.4
        _Caida ("Caida desde el cuerpo hacia la orilla", Range(0.5, 6)) = 2.2
        _Interior ("Brillo encima del cuerpo", Range(0, 1)) = 0.2
        _Intensidad ("Intensidad", Range(0, 8)) = 1.4

        [Header(Estilo caricatura)]
        _Umbral ("Silueta: umbral", Range(0.02, 0.9)) = 0.28
        _Borde ("Grosor de la linea de la orilla", Range(0, 0.3)) = 0.06
        _CorteMedio ("Banda media desde", Range(0, 1)) = 0.5
        _CortePunta ("Banda clara desde", Range(0, 1)) = 0.78
        _Cubre ("Cuanto tapa (0 = aditivo)", Range(0, 1)) = 0.25

        [Header(Colores del Ki al cargar)]
        [HDR] _KiOrilla ("Ki: orilla", Color) = (0.25, 0.55, 2.2, 1)
        [HDR] _KiMedio ("Ki: medio", Color) = (0.8, 1.3, 3.2, 1)
        [HDR] _KiCentro ("Ki: centro", Color) = (2.4, 2.8, 3.4, 1)
        [HDR] _KiBorde ("Ki: linea de la orilla", Color) = (0.15, 0.35, 1.6, 1)

        [Header(Colores Super Saiyajin)]
        [HDR] _SSJOrilla ("SSJ: orilla", Color) = (2.4, 0.8, 0.05, 1)
        [HDR] _SSJMedio ("SSJ: medio", Color) = (3.6, 2.3, 0.3, 1)
        [HDR] _SSJCentro ("SSJ: centro", Color) = (4.0, 3.7, 1.8, 1)
        [HDR] _SSJBorde ("SSJ: linea de la orilla", Color) = (1.6, 0.45, 0.02, 1)
        [HDR] _ColorDestello ("Destello", Color) = (6, 6, 5.5, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "AuraForward"
            Tags { "LightMode" = "UniversalForward" }

            // Premultiplicada: el interior suma luz como un aditivo y la línea de la orilla sí se nota
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_Ruido);
            SAMPLER(sampler_Ruido);

            CBUFFER_START(UnityPerMaterial)
                float _Carga;
                float _Mezcla;
                float _Destello;
                float _TiempoAura;
                float _Grosor;
                float _Llamas;
                float _EscalaRuido;
                float _Velocidad;
                float _Caida;
                float _Interior;
                float _Intensidad;
                float _Umbral;
                float _Borde;
                float _CorteMedio;
                float _CortePunta;
                float _Cubre;
                half4 _KiOrilla;
                half4 _KiMedio;
                half4 _KiCentro;
                half4 _KiBorde;
                half4 _SSJOrilla;
                half4 _SSJMedio;
                half4 _SSJCentro;
                half4 _SSJBorde;
                half4 _ColorDestello;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS      : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 rel        : TEXCOORD2;   // posición respecto al origen del objeto (sigue a Goku aunque vuele)
                float subida      : TEXCOORD3;   // cuánto subió el vértice (0..1): las puntas de las lenguas
            };

            // Coordenadas del ruido: relativas al objeto y "enrolladas" alrededor del cuerpo con x+z para que no se
            // estiren de ningún lado. El ruido baja con el tiempo, así las llamas suben.
            float2 CoordRuido(float3 rel, float t)
            {
                return float2(rel.x + rel.z, rel.y) * _EscalaRuido + float2(0.0, -t);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 origen = float3(UNITY_MATRIX_M._m03, UNITY_MATRIX_M._m13, UNITY_MATRIX_M._m23);
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 nWS = normalize(TransformObjectToWorldNormal(v.normalOS));

                float t = _TiempoAura * _Velocidad;
                float n = SAMPLE_TEXTURE2D_LOD(_Ruido, sampler_Ruido, CoordRuido(posWS - origen, t * 0.6), 0).r;

                float fuerza = saturate(_Carga + _Destello * 0.5);
                float forma = lerp(0.55, 1.0, _Mezcla);          // cargando Ki el aura es más delgada que en SSJ
                float arriba = saturate(nWS.y * 0.5 + 0.5);      // 0 = la cara mira abajo, 1 = mira arriba
                arriba *= arriba;

                float empuje = _Grosor * fuerza * forma * (0.6 + 0.8 * n) * (1.0 + _Destello * 0.6);
                float sube = _Llamas * fuerza * forma * n * arriba;
                posWS += nWS * empuje + float3(0.0, sube, 0.0);

                o.positionCS = TransformWorldToHClip(posWS);
                o.posWS = posWS;
                o.normalWS = nWS;
                o.rel = posWS - origen;
                o.subida = n * arriba;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float fuerza = saturate(_Carga + _Destello * 0.5);
                clip(fuerza - 0.001);

                float3 V = GetWorldSpaceNormalizeViewDir(i.posWS);
                float3 N = normalize(i.normalWS);
                float fres = 1.0 - saturate(abs(dot(N, V)));

                float t = _TiempoAura * _Velocidad;
                float n1 = SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, CoordRuido(i.rel, t)).r;
                float n2 = SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, CoordRuido(i.rel * 2.3, t * 1.7) + 0.37).r;

                // 1 junto al cuerpo, 0 en la orilla del cascarón. El ruido grande (n1) rompe la orilla en lenguas;
                // el fino (n2) solo mueve las bandas de color (si cortara la silueta saldrían puntitos sueltos).
                float v = saturate((1.0 - fres) * _Caida);
                float lengua = (v * (0.55 + n1 * 0.9) + i.subida * 0.25) * (0.35 + 0.65 * fuerza);
                float lenguaColor = lengua + (n2 - 0.5) * 0.15;

                float dentro = smoothstep(_Umbral, _Umbral + 0.02, lengua);
                clip(dentro - 0.001);
                float orilla = dentro * (1.0 - smoothstep(_Umbral + _Borde, _Umbral + _Borde + 0.02, lengua));

                half3 cOrilla = lerp(_KiOrilla.rgb, _SSJOrilla.rgb, _Mezcla);
                half3 cMedio  = lerp(_KiMedio.rgb,  _SSJMedio.rgb,  _Mezcla);
                half3 cCentro = lerp(_KiCentro.rgb, _SSJCentro.rgb, _Mezcla);
                half3 cBorde  = lerp(_KiBorde.rgb,  _SSJBorde.rgb,  _Mezcla);

                half3 col = lerp(cOrilla, cMedio, step(_CorteMedio, lenguaColor));
                col = lerp(col, cCentro, step(_CortePunta, lenguaColor));
                col = lerp(col, cBorde, orilla);

                // Encima del cuerpo casi no se ve: si no, Goku quedaría tapado por un color plano
                float sobreCuerpo = 1.0 - smoothstep(0.2, 0.6, fres);
                float a = dentro * lerp(1.0, _Interior, sobreCuerpo);

                // Destello: en el cambio a SSJ todo se va a blanco y tapa al cuerpo (esconde el cambio de modelo)
                col = lerp(col, _ColorDestello.rgb, _Destello);
                a = saturate(a + _Destello * dentro * sobreCuerpo * 0.8);

                col *= _Intensidad * a;
                float tapa = lerp(_Cubre, 0.85, orilla);
                return half4(col, a * lerp(tapa, 1.0, _Destello * 0.5));
            }
            ENDHLSL
        }
    }
    FallBack Off
}
