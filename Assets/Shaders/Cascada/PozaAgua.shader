// Superficie de la poza al pie de la cascada (corto AMISTAD, 2026-11-11).
//  - Color según lo hondo (profundidad de la escena bajo la superficie): orilla clara, centro oscuro.
//  - Refracción del fondo (Opaque Texture) movida por las ondas.
//  - Espuma en la orilla (donde el agua es delgada) y alrededor del impacto de la cascada (un segmento a lo largo de Z).
//  - Ondas que salen del impacto + ruido que se mueve; reflejo del cielo (fresnel) y brillo del sol.
Shader "Amistad/Agua Poza"
{
    Properties
    {
        _Ruido ("Ruido", 2D) = "gray" {}
        _ColorBajo ("Color en lo bajito", Color) = (0.36, 0.52, 0.50, 1)
        _ColorHondo ("Color en lo hondo", Color) = (0.05, 0.13, 0.15, 1)
        _ColorEspuma ("Color de la espuma", Color) = (1, 0.97, 0.92, 1)
        _ColorCielo ("Reflejo del cielo", Color) = (1.0, 0.6, 0.35, 1)
        _Profundidad ("Profundidad para el color hondo (u)", Float) = 10
        _AnchoOrilla ("Ancho de la espuma de la orilla (u)", Float) = 3
        _Impacto ("Impacto (xyz mundo, w = medio largo en Z)", Vector) = (3950, 13, -86, 55)
        _RadioEspuma ("Radio de la espuma del impacto (u)", Float) = 40
        _Ondas ("Ondas (frecuencia, velocidad, fuerza, alcance u)", Vector) = (0.18, 3, 0.2, 120)
        _Refraccion ("Refracción", Float) = 0.03
        _Brillo ("Brillo del sol (exponente)", Float) = 80
        [Header(Estilo comic)]
        _Toon ("Estilo comic (0 realista, 1 comic)", Range(0, 1)) = 0.75
        _Umbral ("Corte de la espuma", Range(0, 1)) = 0.35
        _Borde ("Grosor de la orilla de tinta", Range(0, 0.3)) = 0.1
        _ColorBorde ("Color de la orilla de tinta", Color) = (0.06, 0.14, 0.17, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PozaForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On   // tapa la parte de la cascada que queda bajo el agua (la tira original llega hasta y = -620)
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_Ruido);
            SAMPLER(sampler_Ruido);

            CBUFFER_START(UnityPerMaterial)
                float4 _Ruido_ST;
                half4 _ColorBajo;
                half4 _ColorHondo;
                half4 _ColorEspuma;
                half4 _ColorCielo;
                float _Profundidad;
                float _AnchoOrilla;
                float4 _Impacto;
                float _RadioEspuma;
                float4 _Ondas;
                float _Refraccion;
                float _Brillo;
                float _Toon;
                float _Umbral;
                float _Borde;
                half4 _ColorBorde;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 posWS      : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float eyeDepth    : TEXCOORD2;
                float fog         : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.posWS = p.positionWS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.eyeDepth = -p.positionVS.z;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float R(float2 uv) { return SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, uv).r; }

            half4 frag(Varyings i) : SV_Target
            {
                float t = _Time.y;
                float2 p = i.posWS.xz;
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float profEscena = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float grosor = max(profEscena - i.eyeDepth, 0.0);   // agua a lo largo de la vista

                // distancia al impacto (segmento a lo largo de Z: la cascada mide ~128 u de ancho)
                float2 cerca = float2(_Impacto.x, clamp(p.y, _Impacto.z - _Impacto.w, _Impacto.z + _Impacto.w));
                float2 dif = p - cerca;
                float d = length(dif);
                float2 sale = dif / max(d, 0.001);

                // ondas que salen del impacto + ruido
                float fase = d * _Ondas.x - t * _Ondas.y;
                float alcance = saturate(1.0 - d / max(_Ondas.w, 0.001));
                float2 grad = sale * cos(fase) * alcance * _Ondas.z;
                float n1 = R(p * 0.012 + float2(t * 0.03, t * 0.02));
                float n2 = R(p * 0.027 - float2(t * 0.025, -t * 0.035));
                float n3 = R(p * 0.07 + float2(-t * 0.08, t * 0.06));
                float2 nxz = (float2(n1, n2) - 0.5) * 0.22 + (float2(n3, R(p * 0.09 - t * 0.07)) - 0.5) * 0.14 + grad * 0.5;   // suave: a ras, mucha inclinación rompía el reflejo en franjas
                float3 n = normalize(float3(-nxz.x, 1.0, -nxz.y));

                // refracción del fondo (sin tomar cosas que estén delante del agua)
                float2 off = nxz * _Refraccion * saturate(grosor / 4.0);
                float profOff = LinearEyeDepth(SampleSceneDepth(suv + off), _ZBufferParams);
                if (profOff < i.eyeDepth) off = 0;
                half3 fondo = SampleSceneColor(suv + off);

                float hondo = saturate(grosor / max(_Profundidad, 0.001));
                half3 colAgua = lerp(_ColorBajo.rgb, _ColorHondo.rgb, hondo);
                half3 c = lerp(fondo * _ColorBajo.rgb * 1.2, colAgua, saturate(hondo * 0.9 + 0.55));   // poco fondo visible: la poza es bajita (piso en y=5)

                // reflejo del cielo y brillo del sol
                Light luz = GetMainLight();
                float3 v = normalize(GetWorldSpaceViewDir(i.posWS));
                float fres = 0.04 + 0.96 * pow(1.0 - saturate(dot(n, v)), 5.0);
                half3 cielo = _ColorCielo.rgb + SampleSH(reflect(-v, n)) * 0.25;
                c = lerp(c, cielo, fres * 0.35);
                float3 hv = normalize(luz.direction + v);
                c += luz.color * pow(saturate(dot(n, hv)), _Brillo) * 1.5;

                // espuma: orilla, impacto y anillos tenues
                float nf = R(p * 0.05 + float2(t * 0.05, 0.0));
                float nf2 = R(p * 0.11 - sale * t * 0.4);
                float espOrilla = saturate((1.0 - saturate(grosor / max(_AnchoOrilla, 0.001))) * 1.2 - nf * 0.7) * 0.7;
                float espImpacto = saturate(1.0 - d / max(_RadioEspuma, 0.001));
                espImpacto = saturate(espImpacto * 1.6 * (0.5 + nf2) - 0.15);
                float anillos = smoothstep(0.8, 1.0, sin(fase)) * alcance * 0.12 * nf;
                float espuma = saturate(espOrilla + espImpacto + anillos);
                half3 cEsp = _ColorEspuma.rgb * (luz.color * 0.6 + SampleSH(float3(0, 1, 0)) * 0.7);
                c = lerp(c, cEsp, espuma);

                float a = saturate(grosor / 0.8);   // orilla suave

                // ---------- version comic: 3 tonos de agua planos, reflejo en franja, brillos recortados,
                // espuma con corte nitido y orilla de tinta, linea de tinta en la orilla de la poza
                half3 medio = lerp(_ColorBajo.rgb, _ColorHondo.rgb, 0.3);
                half3 cT = lerp(_ColorBajo.rgb * 1.15, medio, smoothstep(0.25, 0.28, hondo));
                cT = lerp(cT, lerp(_ColorBajo.rgb, _ColorHondo.rgb, 0.6), smoothstep(0.7, 0.73, hondo));   // lo hondo sin llegar a negro
                float aRas = 1.0 - saturate(v.y);                                             // solo el angulo de la camara: tono parejo
                cT = lerp(cT, _ColorCielo.rgb, smoothstep(0.6, 0.9, aRas) * 0.12);
                float spec = pow(saturate(dot(n, hv)), _Brillo);
                cT += luz.color * smoothstep(0.45, 0.5, spec) * 0.9;
                float espT = saturate(espOrilla + espImpacto);
                float dentro = smoothstep(_Umbral, _Umbral + 0.03, espT);
                float tinta = smoothstep(_Umbral - _Borde, _Umbral - _Borde + 0.03, espT) * (1.0 - dentro);
                float anillosT = smoothstep(0.94, 0.96, sin(fase)) * alcance * step(0.45, nf);
                cT = lerp(cT, cEsp, saturate(max(dentro, anillosT * 0.8)));
                float lineaOrilla = 1.0 - smoothstep(0.35, 0.6, grosor);
                cT = lerp(cT, _ColorBorde.rgb, saturate(max(tinta, lineaOrilla) * 0.85));
                float aT = smoothstep(0.15, 0.3, grosor);

                c = lerp(c, cT, _Toon);
                a = lerp(a, aT, _Toon);
                c = MixFog(c, i.fog);
                return half4(c, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
