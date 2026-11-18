// Amistad/Personaje Toon — piel, ropa y pelo de Ale y Memi.
// Reemplaza al TOON SHADER Borde (Unlit): aquel calculaba la luz con la dirección del sol y nada más, así que ignoraba
// el color del sol, las sombras, la luz de los fuegos y las light probes, y el especular era un punto blanco duro: por eso
// se veía "de plástico". Este es un toon iluminado:
//   - Sol con sombras reales, corte toon suave y sombra de color (no gris).
//   - Piel: banda cálida en el borde luz/sombra y algo de luz que traspasa en contraluz (subsurface falso).
//     La piel se detecta sola por el color de la textura (tonos cafés/naranja saturados), o se fuerza con _MascaraPiel.
//   - Luz ambiente de las light probes (el rebote naranja de la ciudad) y luces puntuales de los fuegos con el mismo corte toon.
//   - Brillo especular ancho y tenue (piel), o banda de brillo en el pelo; teñido del color de la luz.
//   - Borde de luz (Fresnel × _EdgeColour) como el shader anterior, más fuerte del lado iluminado.
//   - Oclusión ambiental horneada (opcional) y una línea de tinta (casco invertido) del color de la textura oscurecido.
Shader "Amistad/Personaje Toon"
{
    Properties
    {
        [Header(Color)]
        _MainTex ("Textura", 2D) = "white" {}
        _Color ("Tinte", Color) = (1,1,1,1)
        [Toggle] _UsarUV2 ("Textura en el segundo UV (textura repintada)", Float) = 0
        _AOTex ("Oclusión horneada (R)", 2D) = "white" {}
        _AOFuerza ("Fuerza de la oclusión (textura)", Range(0,1)) = 0.7
        _AOVertice ("Oclusión por vértice (horneada en la malla)", Range(0,1)) = 0
        _Grano ("Grano de pintura", Range(0,0.2)) = 0.035

        [Header(Luz y sombra toon)]
        _Umbral ("Corte luz/sombra", Range(-1,1)) = 0.0
        _Suavidad ("Suavidad del corte", Range(0.001,0.5)) = 0.07
        _ColorSombra ("Color de la sombra", Color) = (0.62,0.45,0.52,1)
        _LuzAlta ("Tono de luz alta (forma pintada)", Range(0,0.5)) = 0.14
        _UmbralAlto ("Corte de la luz alta", Range(0,1)) = 0.55
        _Nucleo ("Sombra núcleo (tercer tono)", Range(0,0.6)) = 0.22
        _UmbralNucleo ("Corte de la sombra núcleo", Range(-1,0)) = -0.4
        _Relleno ("Luz del sol en la sombra", Range(0,1)) = 0.35
        _IntensidadSol ("Intensidad máxima del sol", Range(0,3)) = 1.0
        [Toggle] _InvertirSol ("Sol invertido (como el TOON SHADER: ilumina el lado contrario al sol)", Float) = 1
        _Ambiente ("Luz ambiente (probes)", Range(0,2)) = 0.45
        _SatAmbiente ("Saturación del ambiente", Range(0,1)) = 0.5
        _LucesExtra ("Luces de fuego", Range(0,3)) = 0.35
        _SombrasRecibidas ("Sombras recibidas", Range(0,1)) = 1.0

        [Header(Piel)]
        _MascaraPiel ("Piel: 0 nada, 0.5 auto, 1 todo", Range(0,1)) = 0.5
        _ColorSSS ("Color bajo la piel", Color) = (1.0,0.32,0.18,1)
        _SSSFuerza ("Banda cálida en el corte", Range(0,2)) = 0.55
        _SSSAncho ("Ancho de la banda", Range(0.01,0.8)) = 0.28
        _Traspaso ("Luz que traspasa (contraluz)", Range(0,2)) = 0.35

        [Header(Brillo)]
        _Especular ("Brillo", Range(0,1)) = 0.12
        _Aspereza ("Aspereza (ancho del brillo)", Range(0.05,1)) = 0.55
        _BandaPelo ("Banda de brillo de pelo", Range(0,1)) = 0
        _MateRopa ("Brillo en ropa (0 = tela mate)", Range(0,1)) = 0.08
        _BandasLuces ("Tonos de la luz de los fuegos (0 = degradado)", Range(0,6)) = 3

        [Header(Borde de luz)]
        _EdgeColour ("Color del borde", Color) = (0.6,0.24,0.1,1)
        _CantidadBorde ("Grosor (potencia Fresnel)", Range(0.5,12)) = 5
        _BordeLuz ("Borde siguiendo la luz", Range(0,2)) = 0.6

        [Header(Linea de tinta)]
        _GrosorLinea ("Grosor de la línea", Range(0,6)) = 1.4
        _ColorLinea ("Color de la línea (× textura)", Color) = (0.22,0.12,0.1,1)
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" "Queue"="Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST; half4 _Color; half _UsarUV2; float4 _AOTex_ST; half _AOFuerza; half _AOVertice; half _Grano;
            half _Umbral; half _Suavidad; half4 _ColorSombra; half _LuzAlta; half _UmbralAlto; half _Nucleo; half _UmbralNucleo; half _Relleno; half _IntensidadSol; half _InvertirSol; half _Ambiente; half _SatAmbiente; half _LucesExtra; half _SombrasRecibidas;
            half _MascaraPiel; half4 _ColorSSS; half _SSSFuerza; half _SSSAncho; half _Traspaso;
            half _Especular; half _Aspereza; half _BandaPelo; half _MateRopa; half _BandasLuces;
            half4 _EdgeColour; half _CantidadBorde; half _BordeLuz;
            half _GrosorLinea; half4 _ColorLinea;
        CBUFFER_END
        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        TEXTURE2D(_AOTex); SAMPLER(sampler_AOTex);
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile _ _LIGHT_LAYERS
            #pragma multi_compile _ LIGHTMAP_SHADOW_MIXING
            #pragma multi_compile _ SHADOWS_SHADOWMASK
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; half4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2; float3 positionOS : TEXCOORD3; half fog : TEXCOORD4; half aoV : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v); UNITY_TRANSFER_INSTANCE_ID(v, o);
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(_UsarUV2 > 0.5 ? v.uv2 : v.uv, _MainTex);
                o.positionOS = v.positionOS.xyz;
                o.aoV = lerp(1.0, v.color.r, _AOVertice);
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
            float Ruido(float3 x)
            {
                float3 i = floor(x), f = frac(x); f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(lerp(Hash(i), Hash(i + float3(1,0,0)), f.x), lerp(Hash(i + float3(0,1,0)), Hash(i + float3(1,1,0)), f.x), f.y),
                            lerp(lerp(Hash(i + float3(0,0,1)), Hash(i + float3(1,0,1)), f.x), lerp(Hash(i + float3(0,1,1)), Hash(i + float3(1,1,1)), f.x), f.y), f.z);
            }

            // Tonos de piel de la textura: naranja/café (tono 0–0.13), saturados y no muy oscuros. Excluye líneas, ropa negra y dientes.
            half MascaraPielAuto(half3 c)
            {
                half mx = max(c.r, max(c.g, c.b)), mn = min(c.r, min(c.g, c.b));
                half sat = (mx - mn) / max(mx, 1e-4);
                half tono = 0;
                if (mx - mn > 1e-4) { tono = (mx == c.r) ? (c.g - c.b) / (mx - mn) : ((mx == c.g) ? 2 + (c.b - c.r) / (mx - mn) : 4 + (c.r - c.g) / (mx - mn)); tono = frac(tono / 6.0 + 1.0); }
                half enTono = smoothstep(0.17, 0.12, tono) * smoothstep(0.93, 0.98, tono + (tono < 0.5 ? 1.0 : 0.0));
                return enTono * smoothstep(0.18, 0.3, sat) * smoothstep(0.12, 0.25, mx);
            }

            // Corte toon: luz (0..1) para un N·L dado.
            half Toon(half ndl) { return smoothstep(_Umbral - _Suavidad, _Umbral + _Suavidad, ndl); }

            half3 LuzToon(Light luz, half atenuacion, half3 N, half3 V, half3 albedo, half piel, half ao, out half3 brillo)
            {
                half ndl = dot(N, luz.direction);
                half sombra = lerp(1.0, atenuacion, _SombrasRecibidas);
                half t = Toon(ndl) * smoothstep(0.25, 0.75, sombra);
                // difusa: lado iluminado + relleno de color en la sombra
                half3 dif = luz.color * (t + (1.0 - t) * _Relleno * _ColorSombra.rgb);
                // piel: banda cálida justo en el corte y traspaso en contraluz
                half banda = saturate(1.0 - abs(ndl - _Umbral) / _SSSAncho) * (0.35 + 0.65 * sombra);
                half3 sss = luz.color * _ColorSSS.rgb * banda * _SSSFuerza * piel;
                half traspaso = pow(saturate(dot(V, -luz.direction)), 3.0) * pow(1.0 - saturate(dot(N, V)), 2.0) * _Traspaso * piel;
                sss += luz.color * _ColorSSS.rgb * traspaso;
                // brillo: Blinn ancho y tenue con corte suave (no un punto blanco)
                half3 H = SafeNormalize(luz.direction + V);
                half ndh = saturate(dot(N, H));
                half exponente = exp2(lerp(9.0, 2.0, _Aspereza));
                half sp = pow(ndh, exponente);
                sp = smoothstep(0.35, 0.75, sp) * 0.6 + sp * 0.4;
                half3 tinte = lerp(1.0, saturate(albedo * 2.2), 0.55 * piel + 0.25);
                brillo = luz.color * tinte * sp * t * _Especular * ao * lerp(_MateRopa, 1.0, piel);
                return albedo * (dif * ao + sss);
            }

            half4 frag(Varyings i, bool frente : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv) * _Color;
                half3 albedo = tex.rgb;
                half3 N = normalize(i.normalWS); if (!frente) N = -N;
                half3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                half ao = lerp(1.0, SAMPLE_TEXTURE2D(_AOTex, sampler_AOTex, i.uv).r, _AOFuerza) * i.aoV;

                // grano de pintura: variación suave de color en el espacio del modelo (no se mueve con la cámara)
                half g = Ruido(i.positionOS * 180.0) * 0.6 + Ruido(i.positionOS * 47.0) * 0.4;
                albedo *= 1.0 + (g - 0.5) * 2.0 * _Grano;

                half pielAuto = MascaraPielAuto(tex.rgb);
                half piel = _MascaraPiel < 0.5 ? pielAuto * (_MascaraPiel * 2.0) : lerp(pielAuto, 1.0, (_MascaraPiel - 0.5) * 2.0);

                float4 shadowCoord = TransformWorldToShadowCoord(i.positionWS);
                half4 shadowMask = half4(1,1,1,1);
                Light sol = GetMainLight(shadowCoord, i.positionWS, shadowMask);
                sol.color = sol.color / max(1.0, max(sol.color.r, max(sol.color.g, sol.color.b))) * _IntensidadSol;   // respeta el tono, no quema
                // El TOON SHADER original usaba el nodo Main Light Direction de Shader Graph, que da la dirección en la que VIAJA
                // la luz (-L): iluminaba el lado contrario al sol, y las rotaciones del sol en el Timeline (media cara en sombra,
                // palma de Ale) se ajustaron así. Se conserva esa convención para no deshacer esos planos.
                if (_InvertirSol > 0.5) sol.direction = -sol.direction;
                half3 brillo;
                half3 col = LuzToon(sol, sol.shadowAttenuation, N, V, albedo, piel, ao, brillo);
                // tres tonos como en el dibujo: forma de luz alta (más clara, del color de la piel) y sombra núcleo
                half ndlSol = dot(N, sol.direction);
                half alto = smoothstep(_UmbralAlto - 0.025, _UmbralAlto + 0.025, ndlSol) * _LuzAlta;
                half nucleo = smoothstep(_UmbralNucleo + 0.04, _UmbralNucleo - 0.04, ndlSol) * _Nucleo;
                col *= (1.0 + alto) * (1.0 - nucleo);
                half3 brilloTotal = brillo;

                // ambiente: light probes (rebote horneado de los fuegos y el cielo)
                half3 amb = SampleSH(N);
                amb = lerp(dot(amb, half3(0.299, 0.587, 0.114)).xxx, amb, _SatAmbiente) * _Ambiente;
                col += albedo * amb * ao * lerp(_ColorSombra.rgb, 1.0, 0.5);

                // luces puntuales (fuegos, destellos)
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = i.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(i.positionCS);
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
                {
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                    half3 b; col += LuzToon(l, l.shadowAttenuation, N, V, albedo, piel, ao, b) * _LucesExtra; brilloTotal += b * _LucesExtra;
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light l = GetAdditionalLight(lightIndex, i.positionWS, shadowMask);
                    l.color *= l.distanceAttenuation;
                    l.color = l.color / (1.0 + max(l.color.r, max(l.color.g, l.color.b)));   // fuegos muy intensos: se comprimen, no saturan
                    if (_BandasLuces > 0.5)
                    {
                        // la caída de la luz en tonos planos (como pintura), no en degradado liso
                        half a = max(l.color.r, max(l.color.g, l.color.b));
                        half x = a * _BandasLuces;
                        // redondea hacia abajo: la luz tenue de un fuego lejano no debe subir a un tono completo
                        // (rellenaba el lado en sombra y borraba la media cara oscura)
                        half aq = (floor(x) + smoothstep(0.72, 0.85, frac(x))) / _BandasLuces;
                        l.color *= aq / max(a, 1e-4);
                    }
                    half3 b; col += LuzToon(l, l.shadowAttenuation, N, V, albedo, piel, ao, b) * _LucesExtra; brilloTotal += b * _LucesExtra;
                LIGHT_LOOP_END
                #endif

                col += brilloTotal;
                // banda de pelo: anillo de brillo que sigue a la vista
                if (_BandaPelo > 0)
                {
                    half ndv = saturate(dot(N, V));
                    half anillo = smoothstep(0.55, 0.7, ndv) * smoothstep(0.92, 0.8, ndv) * (0.6 + 0.4 * Ruido(i.positionOS * float3(400, 40, 400)));
                    col += sol.color * anillo * _BandaPelo * 0.25 * smoothstep(-0.1, 0.3, dot(N, sol.direction));
                }

                // borde de luz (Fresnel); más fuerte del lado que mira a la luz
                half fres = pow(1.0 - saturate(dot(N, V)), _CantidadBorde);  // sol.direction ya viene con la convención elegida
                half haciaLuz = saturate(dot(N, sol.direction) * 0.5 + 0.5);
                col += _EdgeColour.rgb * fres * lerp(1.0, haciaLuz * 2.0, saturate(_BordeLuz)) * (1.0 + max(0, _BordeLuz - 1.0))
                       * lerp(0.35, 1.0, piel);   // en la ropa el borde es más tenue: si no, la tela negra se ve de látex

                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }

        // Línea de tinta: casco invertido, grosor constante en pantalla
        Pass
        {
            Name "Linea"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Cull Front
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings vert(Attributes v)
            {
                Varyings o = (Varyings)0; UNITY_SETUP_INSTANCE_ID(v);
                float4 cs = TransformObjectToHClip(v.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(v.normalOS);
                float2 nCS = normalize(mul((float3x3)UNITY_MATRIX_VP, nWS).xy + 1e-5);
                float2 px = nCS * _GrosorLinea * 2.0 / _ScreenParams.xy;   // píxeles -> NDC
                cs.xy += px * cs.w * (_GrosorLinea > 0.001 ? 1 : 0);
                // empuja el casco un poco hacia atrás: así no asoma por las costuras de la malla (rayas en cuello y pecho)
                // y solo queda la línea en la silueta
                #if UNITY_REVERSED_Z
                    cs.z -= 0.0015 * cs.w;
                #else
                    cs.z += 0.0015 * cs.w;
                #endif
                o.positionCS = cs; o.uv = TRANSFORM_TEX(_UsarUV2 > 0.5 ? v.uv2 : v.uv, _MainTex);
                o.fog = ComputeFogFactor(cs.z);
                return o;
            }
            half4 frag(Varyings i) : SV_Target
            {
                clip(_GrosorLinea - 0.001);
                half3 c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb * _Color.rgb * _ColorLinea.rgb;
                return half4(MixFog(c, i.fog), 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
            float3 _LightDirection; float3 _LightPosition;
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes v) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(v);
                float3 p = TransformObjectToWorld(v.positionOS.xyz); float3 n = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 L = normalize(_LightPosition - p);
                #else
                    float3 L = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(p, n, L));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                return cs;
            }
            half4 frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            float4 vert(Attributes v) : SV_POSITION { UNITY_SETUP_INSTANCE_ID(v); return TransformObjectToHClip(v.positionOS.xyz); }
            half frag() : SV_Target { return 0; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; };
            Varyings vert(Attributes v) { Varyings o; UNITY_SETUP_INSTANCE_ID(v); o.positionCS = TransformObjectToHClip(v.positionOS.xyz); o.normalWS = TransformObjectToWorldNormal(v.normalOS); return o; }
            half4 frag(Varyings i) : SV_Target { return half4(NormalizeNormalPerPixel(i.normalWS), 0); }
            ENDHLSL
        }
    }
}
