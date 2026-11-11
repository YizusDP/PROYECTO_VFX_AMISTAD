// Agua que cae de la cascada (corto AMISTAD, 2026-11-11).
// Para la tira "planoCascada": u = ancho (0..1), v = largo (1 arriba, en el borde de la roca; 0 abajo).
//  - Vetas de agua que bajan y aceleran conforme cae (ruido estirado a lo largo).
//  - Espuma en el borde de arriba (donde el agua se dobla) y abajo, donde pega con la poza (por altura en el mundo).
//  - Orillas deshilachadas, fundido suave contra la roca (soft particles) y reflejo del cielo en los costados (fresnel).
//  - Se corta en el nivel de la poza (_NivelAgua): lo que queda debajo no se dibuja.
Shader "Amistad/Cascada Agua"
{
    Properties
    {
        _Ruido ("Ruido", 2D) = "gray" {}
        _ColorAgua ("Color del agua (con luz)", Color) = (0.62, 0.74, 0.78, 1)
        _ColorHondo ("Color del agua (entre vetas)", Color) = (0.16, 0.26, 0.30, 1)
        _ColorEspuma ("Color de la espuma", Color) = (1, 0.97, 0.92, 1)
        _ColorCielo ("Reflejo del cielo", Color) = (1.0, 0.62, 0.38, 1)
        _Velocidad ("Velocidad de caída", Float) = 0.55
        _Vetas ("Escala de las vetas (x ancho, y largo)", Vector) = (6, 1.6, 0, 0)
        _Opacidad ("Opacidad del agua", Range(0, 1)) = 0.8
        _Bordes ("Orillas deshilachadas (ancho en u)", Range(0.01, 0.4)) = 0.12
        _EspumaLabio ("Espuma en el borde de arriba (en v)", Range(0, 0.3)) = 0.08
        _NivelAgua ("Nivel de la poza (Y del mundo)", Float) = 13
        _AltoEspuma ("Alto de la espuma de abajo (u)", Float) = 35
        _Suavidad ("Fundido con la roca (u)", Float) = 6
        [Header(Estilo comic)]
        _Toon ("Estilo comic (0 realista, 1 comic)", Range(0, 1)) = 0.75
        _Umbral ("Corte de la espuma", Range(0, 1)) = 0.35
        _Borde ("Grosor de la orilla de tinta", Range(0, 0.3)) = 0.08
        _ColorBorde ("Color de la orilla de tinta", Color) = (0.1, 0.18, 0.22, 1)
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
            Name "CascadaForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_Ruido);
            SAMPLER(sampler_Ruido);

            CBUFFER_START(UnityPerMaterial)
                float4 _Ruido_ST;
                half4 _ColorAgua;
                half4 _ColorHondo;
                half4 _ColorEspuma;
                half4 _ColorCielo;
                float _Velocidad;
                float4 _Vetas;
                float _Opacidad;
                float _Bordes;
                float _EspumaLabio;
                float _NivelAgua;
                float _AltoEspuma;
                float _Suavidad;
                float _Toon;
                float _Umbral;
                float _Borde;
                half4 _ColorBorde;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 posWS      : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 screenPos  : TEXCOORD3;
                float eyeDepth    : TEXCOORD4;
                float fog         : TEXCOORD5;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.posWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = v.uv;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.eyeDepth = -p.positionVS.z;
                o.fog = ComputeFogFactor(p.positionCS.z);
                return o;
            }

            float R(float2 uv) { return SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, uv).r; }

            half4 frag(Varyings i, bool cara : SV_IsFrontFace) : SV_Target
            {
                float h = i.posWS.y - _NivelAgua;          // altura sobre la poza
                clip(h + 0.5);

                float2 uv = i.uv;
                float t = _Time.y * _Velocidad;
                float caida = 1.0 + (1.0 - uv.y) * 1.5;    // abajo va más rápido

                // vetas: ruido estirado a lo largo que baja (sumar t a v = el dibujo baja)
                float n1 = R(float2(uv.x * _Vetas.x,               uv.y * _Vetas.y        + t * caida));
                float n2 = R(float2(uv.x * _Vetas.x * 2.3 + 0.37,  uv.y * _Vetas.y * 2.2  + t * 1.7 * caida));
                float n3 = R(float2(uv.x * _Vetas.x * 0.5 + 0.71,  uv.y * _Vetas.y * 0.6  + t * 0.6));
                float vetas = saturate(n1 * 0.65 + n2 * 0.55 - 0.35 + (n3 - 0.5) * 0.3);
                float blancas = smoothstep(0.55, 0.85, n2 * 0.6 + n1 * 0.5);

                // orillas deshilachadas
                float borde = min(uv.x, 1.0 - uv.x);
                float orilla = smoothstep(0.0, _Bordes, borde + (n3 - 0.5) * _Bordes * 0.9);

                // espuma del labio (arriba, donde el agua se dobla sobre la roca)
                float labio = smoothstep(1.0 - _EspumaLabio * 2.0, 1.0 - _EspumaLabio * 0.3, uv.y) * saturate(n2 * 1.3);

                // espuma de abajo: crece al acercarse a la poza
                float abajo = saturate(1.0 - h / max(_AltoEspuma, 0.001));
                abajo *= abajo;
                float nb = R(float2(uv.x * 4.0 + 0.2, i.posWS.y * 0.02 + t * 1.3));
                float espumaAbajo = saturate(abajo * (0.6 + nb * 0.9));
                float espuma = saturate(max(labio, espumaAbajo) + blancas * 0.55);

                // luz: sol + ambiente, reflejo del cielo en los costados
                Light luz = GetMainLight();
                float3 n = normalize(i.normalWS) * (cara ? 1.0 : -1.0);
                float3 vdir = normalize(GetWorldSpaceViewDir(i.posWS));
                float ndl = saturate(dot(n, luz.direction)) * 0.5 + 0.5;
                float fres = pow(1.0 - saturate(abs(dot(n, vdir))), 3.0);
                half3 amb = SampleSH(n);
                half3 agua = lerp(_ColorHondo.rgb, _ColorAgua.rgb, vetas);
                half3 c = agua * (luz.color * ndl * 0.8 + amb * 0.7);
                c += _ColorCielo.rgb * fres * 0.35;
                half3 cEsp = _ColorEspuma.rgb * (luz.color * (0.55 + 0.45 * ndl) + amb * 0.6);
                c = lerp(c, cEsp, espuma);

                // fundido con la roca de atrás
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float prof = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float suave = saturate((prof - i.eyeDepth) / max(_Suavidad, 0.001));

                float a = saturate(_Opacidad * (0.55 + 0.6 * vetas) + espuma * 0.9);
                a *= orilla * suave * saturate((h + 0.5) / 2.0);

                // ---------- version comic (como el fuego toon y el aura): tonos planos, cortes nitidos, orilla de tinta
                // Bandas con el ruido grande (n1, n3): el fino haria puntitos. Las rayas blancas si usan n2 (lineas de caida).
                float vT = saturate(n1 * 0.8 + (n3 - 0.5) * 0.4 + 0.1);
                half3 bT = lerp(_ColorHondo.rgb, _ColorAgua.rgb, smoothstep(0.38, 0.41, vT));
                bT = lerp(bT, _ColorAgua.rgb * 1.3 + 0.05, smoothstep(0.64, 0.67, vT));
                float luzT = lerp(0.72, 1.0, smoothstep(0.73, 0.76, ndl));
                half3 cT = bT * (luz.color * luzT * 0.8 + amb * 0.7);
                cT += _ColorCielo.rgb * smoothstep(0.42, 0.45, fres) * 0.25;
                float rayas = smoothstep(0.70, 0.73, n2 * 0.6 + n1 * 0.5);
                float espT = max(labio, espumaAbajo);
                float dentro = smoothstep(_Umbral, _Umbral + 0.03, espT);
                float tinta = smoothstep(_Umbral - _Borde, _Umbral - _Borde + 0.03, espT) * (1.0 - dentro);
                half3 espC = _ColorEspuma.rgb * (luz.color * 0.85 + amb * 0.6);
                cT = lerp(cT, espC, saturate(max(rayas, dentro)));
                float bordeT = borde + (n3 - 0.5) * _Bordes * 0.9;
                float orillaT = smoothstep(_Bordes * 0.5, _Bordes * 0.5 + 0.015, bordeT);
                float lineaLado = orillaT * (1.0 - smoothstep(_Bordes * 0.5 + 0.02, _Bordes * 0.5 + 0.045, bordeT));
                cT = lerp(cT, _ColorBorde.rgb, saturate(max(tinta, lineaLado) * 0.85));
                float aT = saturate(0.82 + 0.18 * max(dentro, rayas)) * orillaT * suave * saturate((h + 0.5) / 2.0);

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
