// Shader de partículas para fuego (URP).
// Modos:
//   0 = Fuego  : llama procedural con ruido, rampa de color HDR y erosión según la edad (aditivo)
//   1 = Brasa  : punto brillante que parpadea (aditivo)
//   2 = Humo   : bocanada suave con ruido (alpha blend)
//   3 = Calor  : distorsión de calor usando la Opaque Texture (alpha blend, se dibuja antes del fuego)
// Todas las partículas usan "soft particles": se funden con la geometría en vez de cortarse.
// Requiere en el Particle System Renderer los Custom Vertex Streams:
//   Position, Color, UV, StableRandom.x, AgePercent   (=> TEXCOORD0.xy = uv, .z = azar, .w = edad)
Shader "Amistad/Fuego VFX"
{
    Properties
    {
        [Enum(Fuego,0,Brasa,1,Humo,2,Calor,3)] _Modo ("Modo", Float) = 0
        [NoScaleOffset] _Ruido ("Ruido (repetible)", 2D) = "gray" {}
        [HDR] _ColorBase ("Color base / humo", Color) = (2.0, 0.25, 0.02, 1)
        [HDR] _ColorMedio ("Color medio", Color) = (6.0, 1.5, 0.12, 1)
        [HDR] _ColorPunta ("Color punta", Color) = (10.0, 6.0, 2.2, 1)
        _Intensidad ("Intensidad", Range(0, 5)) = 1
        _Velocidad ("Velocidad del ruido", Float) = 1.2
        _EscalaRuido ("Escala del ruido", Float) = 1
        _Distorsion ("Distorsión de la llama", Range(0, 0.6)) = 0.22
        _Ancho ("Ancho de la llama", Range(0.1, 1)) = 0.85
        _Erosion ("Erosión con la edad", Range(0, 1.5)) = 0.85
        _SuavidadSuelo ("Fundido con la geometría (unidades)", Float) = 15
        _FuerzaCalor ("Fuerza de la distorsión de calor", Range(0, 0.1)) = 0.02
        [HideInInspector] _SrcBlend ("_SrcBlend", Float) = 1
        [HideInInspector] _DstBlend ("_DstBlend", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "FuegoForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            TEXTURE2D(_Ruido);
            SAMPLER(sampler_Ruido);

            CBUFFER_START(UnityPerMaterial)
                half4 _ColorBase;
                half4 _ColorMedio;
                half4 _ColorPunta;
                float _Modo;
                float _Intensidad;
                float _Velocidad;
                float _EscalaRuido;
                float _Distorsion;
                float _Ancho;
                float _Erosion;
                float _SuavidadSuelo;
                float _FuerzaCalor;
                float _SrcBlend;
                float _DstBlend;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color       : COLOR;
                float4 uv         : TEXCOORD0;   // xy = uv, z = azar estable, w = edad (0..1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color       : COLOR;
                float4 uv         : TEXCOORD0;
                float4 screenPos  : TEXCOORD1;
                float eyeDepth    : TEXCOORD2;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.screenPos = ComputeScreenPos(p.positionCS);
                o.eyeDepth = -p.positionVS.z;
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            float Ruido(float2 uv)
            {
                return SAMPLE_TEXTURE2D(_Ruido, sampler_Ruido, uv).r;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.uv.xy;
                float azar = i.uv.z;
                float edad = i.uv.w;
                float t = _Time.y * _Velocidad;

                // Soft particles: distancia entre la partícula y lo que ya está dibujado detrás
                float2 suv = i.screenPos.xy / i.screenPos.w;
                float profEscena = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float suave = saturate((profEscena - i.eyeDepth) / max(_SuavidadSuelo, 0.001));

                int modo = (int)round(_Modo);

                // ---------- CALOR: refracta lo que hay detrás ----------
                if (modo == 3)
                {
                    float n1 = Ruido(uv * 1.3 * _EscalaRuido + float2(azar * 7.0, -t * 0.5));
                    float n2 = Ruido(uv * 2.9 * _EscalaRuido + float2(-azar * 3.0, -t * 0.8));
                    float2 desplazamiento = float2(n1 - 0.5, n2 - 0.5);
                    float r = length(uv - 0.5) * 2.0;
                    float mascara = saturate(1.0 - r);
                    mascara *= mascara * i.color.a * suave;
                    half3 fondo = SampleSceneColor(suv + desplazamiento * _FuerzaCalor * mascara);
                    // alpha = máscara: en las orillas no cambia nada (la Opaque Texture viene reducida y se notaría un cuadro)
                    return half4(fondo, saturate(mascara * 2.0));
                }

                // ---------- BRASA: punto brillante que parpadea ----------
                if (modo == 1)
                {
                    float r = length(uv - 0.5) * 2.0;
                    float punto = saturate(1.0 - r);
                    punto *= punto;
                    float parpadeo = 0.55 + 0.45 * sin(_Time.y * 11.0 + azar * 40.0);
                    half3 c = lerp(_ColorMedio.rgb, _ColorPunta.rgb, punto);
                    c *= punto * parpadeo * i.color.rgb * i.color.a * _Intensidad * suave;
                    return half4(c, punto * i.color.a);
                }

                // ---------- HUMO: bocanada suave (alpha blend) ----------
                if (modo == 2)
                {
                    float r = length(uv - 0.5) * 2.0;
                    float n1 = Ruido(uv * 0.9 * _EscalaRuido + float2(azar * 9.1, -t * 0.15));
                    float n2 = Ruido(uv * 2.1 * _EscalaRuido + float2(-azar * 4.7, -t * 0.25));
                    float bocanada = saturate(1.0 - r) * saturate(n1 * 0.85 + n2 * 0.5 - 0.3);
                    bocanada = smoothstep(0.0, 0.55, bocanada);
                    half3 c = _ColorBase.rgb * i.color.rgb;
                    return half4(c, bocanada * i.color.a * suave);
                }

                // ---------- FUEGO: llama procedural ----------
                float2 p = uv;
                float n1 = Ruido(float2(p.x * 1.1, p.y * 0.8) * _EscalaRuido + float2(azar * 5.3, -t));
                float n2 = Ruido(float2(p.x * 2.4, p.y * 1.9) * _EscalaRuido + float2(azar * 2.1 + 0.37, -t * 1.8));
                float n = n1 * 0.65 + n2 * 0.35;

                // La distorsión crece hacia la punta, como en una flama real
                p.x += (n - 0.5) * _Distorsion * (0.25 + p.y * 1.2);

                // Forma de lágrima: ancha abajo, delgada arriba
                float ancho = _Ancho * 0.5 * pow(saturate(1.0 - p.y), 0.7) + 0.015;
                float dx = abs(p.x - 0.5);
                float forma = 1.0 - smoothstep(ancho * 0.45, ancho, dx);
                forma *= smoothstep(0.0, 0.1, p.y) * (1.0 - smoothstep(0.7, 1.0, p.y));

                // Erosión: con la edad la llama se deshace en lenguas
                float fuego = forma * (0.45 + n * 1.0);
                fuego = saturate((fuego - edad * _Erosion) * 3.0);

                // Rampa de color de "cuerpo negro": rojo -> naranja -> amarillo blanco
                half3 c = lerp(_ColorBase.rgb, _ColorMedio.rgb, smoothstep(0.05, 0.45, fuego));
                c = lerp(c, _ColorPunta.rgb, smoothstep(0.6, 1.0, fuego));
                float a = fuego * i.color.a * suave;
                c *= i.color.rgb * _Intensidad * a;
                return half4(c, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
