// Rayos de Ki (URP) para los LineRenderer que genera el script AuraKi.
// El LineRenderer da uv.x a lo largo del rayo (0..1) y uv.y a lo ancho (0..1); el color del vértice trae
// el color del rayo y en alpha cuánto le queda de vida (lo pone el script).
// Estilo caricatura: núcleo blanco de corte nítido + una banda de color + un halo suave muy débil.
// Además el rayo "se enciende" desde el origen hasta la punta (_Recorre) y parpadea a lo largo.
Shader "Amistad/Rayo Ki"
{
    Properties
    {
        [HDR] _ColorNucleo ("Núcleo", Color) = (6, 6, 7, 1)
        [HDR] _ColorBrillo ("Banda de color", Color) = (0.6, 1.6, 6, 1)
        _Nucleo ("Ancho del núcleo", Range(0.05, 0.9)) = 0.28
        _Banda ("Ancho de la banda de color", Range(0.1, 1)) = 0.7
        _Halo ("Halo suave", Range(0, 1)) = 0.25
        _Intensidad ("Intensidad", Range(0, 8)) = 1.5
        _Parpadeo ("Parpadeo a lo largo", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "RayoForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorNucleo;
                half4 _ColorBrillo;
                float _Nucleo;
                float _Banda;
                float _Halo;
                float _Intensidad;
                float _Parpadeo;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color       : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color       : COLOR;
                float2 uv         : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float vida = i.color.a;                       // 1 = recién nacido, 0 = se apaga
                float d = abs(i.uv.y * 2.0 - 1.0);            // 0 en el centro del rayo, 1 en el borde

                // Al nacer se dibuja del origen a la punta en el primer 30 % de su vida
                float recorre = saturate((1.0 - vida) / 0.3);
                float encendido = step(i.uv.x, recorre + 0.001);

                // El núcleo adelgaza al apagarse
                float nucleoAncho = _Nucleo * lerp(0.4, 1.0, vida);
                float nucleo = step(d, nucleoAncho);
                float banda = step(d, _Banda * lerp(0.6, 1.0, vida));
                float halo = (1.0 - smoothstep(0.0, 1.0, d)) * _Halo;

                // Parpadeo a lo largo: tramos que bajan de brillo (deterministas: dependen de la vida, no del reloj)
                float tramos = frac(sin(floor(i.uv.x * 7.0) * 12.9898 + floor(vida * 12.0) * 78.233) * 43758.5453);
                float parpadeo = lerp(1.0, step(0.3, tramos), _Parpadeo);

                half3 c = _ColorBrillo.rgb * max(banda, halo) * i.color.rgb;
                c = lerp(c, _ColorNucleo.rgb, nucleo);
                c *= _Intensidad * encendido * parpadeo * saturate(vida * 2.0);
                return half4(c, 0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
