using System;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > Escena 8: saludo épico (49-65.25 s)
/// Ale y Memi, frente a frente después del encuentro, se dan el saludo tipo Depredador (agarre de antebrazo).
/// La animación viene de Blender (Assets/Animaciones/Saludo/Ale_Saludo.fbx y Memi_Saludo.fbx, mismo esqueleto Mixamo)
/// y saludo_layout.json trae las posiciones relativas y las 5 cámaras del previz:
///   1 FRENTE (49.0)  2 CARA DE ALE en media sombra; la cámara entra detrás de la cabeza de Memo (51.5)
///   3 sale de detrás de la cabeza de Ale y descubre la CARA DE MEMO (53.5)
///   4 CONTRAPICADA, toman impulso (55.5)  5 PICADA mano de Ale (56.97)  6 CONTRAPICADA mano de Memi (57.83)
///   7 CENTRO (58.7)  8 CHOQUE (59.57, contacto 59.7)  9 FINAL picada sobre el agarre + fundido a negro (61.0-65.25)
/// Qué hace (se puede volver a correr; todos los valores son absolutos):
///  - Importa los FBX como Humanoid (raíz horneada en la pose) y agrega el estado "Saludo" a los Animator Controllers.
///  - SALUDO_ALE / SALUDO_MEMI (SaludoControl) con su pista en el Timeline de la ciudad ponen el estado en su segundo.
///  - En el corte de 49 s acomoda a los dos (salto invisible) a la distancia y desfase lateral de la coreografía:
///    agrega keys a las curvas de "Ale · recorrido" y "MC · recorrido" (si vuelves a correr la escena 7, corre esta después).
///  - Crea las cámaras CM8_* con su movimiento (clips) y los cortes en el principal.
///  - Choque: flash, chispas, onda expansiva, estallido de luz detrás, luces de contraluz y dos Volumes (look rojo + impacto).
/// </summary>
public static class SaludoEpicoEditor
{
    const string Carpeta = "Assets/Animaciones/Saludo";
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string FbxAleOrig = "Assets/Assets/3d/Persoanjes/Ale Strut Walking.fbx";
    const string FbxMemiOrig = "Assets/Assets/3d/Persoanjes/Memi Walking.fbx";
    const float InicioCiudad = 17.5f;     // el Timeline de la ciudad arranca a los 17.5 s del principal
    const float Inicio = 49f;             // principal: empieza el saludo (termina CM7_ENCUENTRO)
    const float AspectoPantalla = 16f / 9f;

    static readonly string[] NombresCam = { "CM8_1_FRENTE", "CM8_2_CARA_ALE", "CM8_3_CARA_MEMO", "CM8_4_CONTRAPICADA_IMPULSO", "CM8_5_PICADA_MANO_ALE", "CM8_6_CONTRAPICADA_MANO_MEMI", "CM8_7_CENTRO", "CM8_8_CHOQUE", "CM8_9_FINAL" };
    const float FinEscena = 65.25f;      // termina la ciudad (y el corto)

    [Serializable] class Personaje { public string name; public float[] place; public float yaw_deg; public float hipsH; }
    [Serializable] class Corte { public int frame; public string name; }
    [Serializable] class Cam { public string name; public float lens; public float sensor; public float[] data; }
    [Serializable] class Luz { public string name; public float[] pos; public float[] dir; public int startFrame; public int endFrame; }
    [Serializable] class Layout { public int fps; public int frames; public int hitFrame; public int fadeFrame; public int freezeFrames; public float[] grip; public float[] burst; public Personaje[] chars; public Corte[] cuts; public Cam[] cams; public Luz[] faceLights; }

    // mapeo Blender (x: de Ale a Memi, y: izquierda de Ale, z: arriba; metros) -> mundo Unity
    static Vector3 M, D, Lft; static float K;
    static Vector3 U(float x, float y, float z) => M + (D * x + Lft * y + Vector3.up * z) * K;
    static Vector3 UDir(float x, float y, float z) => (D * x + Lft * y + Vector3.up * z).normalized;

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Escena 8: saludo épico + final (49-72.9 s)", false, 301)]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Saludo] Sal de Play antes de correr la herramienta."); return; }
        var log = new StringBuilder();

        // ---------- 0. Datos
        var ta = AssetDatabase.LoadAssetAtPath<TextAsset>(Carpeta + "/saludo_layout.json");
        if (ta == null) { Debug.LogError("[Saludo] Falta " + Carpeta + "/saludo_layout.json"); return; }
        var lay = JsonUtility.FromJson<Layout>(ta.text);
        float fps = lay.fps;
        float durSaludo = lay.frames / fps;                       // 7 s
        double tHit = (lay.hitFrame - 1) / fps;                   // segundos desde el inicio del saludo

        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var directores = UnityEngine.Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = directores.FirstOrDefault(d => d.playableAsset == tlP);
        var dirC = directores.FirstOrDefault(d => d.playableAsset == tlC);
        var todos = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var city = T("CITY"); var memi = T("MEMO_PERFIL"); var ale = T("ALE"); var cm3 = T("CM3_MEMO");
        var pistaMemi = tlC != null ? tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.StartsWith("MC") && t.name.Contains("recorrido")) : null;
        var pistaAle = tlC != null ? tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.StartsWith("Ale") && t.name.Contains("recorrido")) : null;
        var pistaCortes = tlP != null ? tlP.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault() : null;
        if (dirP == null || dirC == null || city == null || memi == null || ale == null || cm3 == null || pistaMemi?.infiniteClip == null || pistaAle?.infiniteClip == null || pistaCortes == null)
        { Debug.LogError("[Saludo] Falta algo (directores, CITY, MEMO_PERFIL, ALE, CM3_MEMO o las pistas de recorrido). Corre antes la escena 7 (encuentro)."); return; }
        var animA = ale.GetComponent<Animator>(); var animM = memi.GetComponent<Animator>();
        // el controller ya no va en los personajes (lo quita CuerpoTimelineEditor); si no está, se usa el asset
        var ctrlA = (animA != null ? animA.runtimeAnimatorController as AnimatorController : null) ?? AssetDatabase.LoadAssetAtPath<AnimatorController>(CuerpoTimelineEditor.ControllerAle);
        var ctrlM = (animM != null ? animM.runtimeAnimatorController as AnimatorController : null) ?? AssetDatabase.LoadAssetAtPath<AnimatorController>(CuerpoTimelineEditor.ControllerMemi);
        if (ctrlA == null || ctrlM == null) { Debug.LogError("[Saludo] No encuentro ALE.controller o MEMI.controller."); return; }
        var pjA = lay.chars.First(c => c.name == "ale"); var pjM = lay.chars.First(c => c.name == "memi");

        // ---------- 1. FBX del saludo -> clips humanoides
        var clipA = ImportarClip(Carpeta + "/Ale_Saludo.fbx", "Ale_Saludo", FbxAleOrig, log);
        var clipM = ImportarClip(Carpeta + "/Memi_Saludo.fbx", "Memi_Saludo", FbxMemiOrig, log);
        if (clipA == null || clipM == null) { Debug.LogError("[Saludo] No se pudieron importar los FBX del saludo.\n" + log); return; }

        // ---------- 2. Estado "Saludo" en los controllers
        EstadoSaludo(ctrlA, clipA, log);
        if (ctrlM != ctrlA) EstadoSaludo(ctrlM, clipM, log);

        // ---------- 3. Dónde están parados (después del encuentro) y escala Blender -> Unity
        float t0 = Inicio - InicioCiudad;                           // local de la ciudad
        var bZ = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.z");
        var bX = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x");
        var bY = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y");
        Vector3 Mundo(AnimationTrack p, float x, float z) => city.TransformPoint(p.infiniteClipOffsetPosition + Quaternion.Euler(p.infiniteClipOffsetEulerAngles) * new Vector3(x, 0, z));
        float Ev(AnimationClip c, EditorCurveBinding b, float t) { var cu = AnimationUtility.GetEditorCurve(c, b); return cu != null ? cu.Evaluate(t) : 0f; }
        var paroA = Mundo(pistaAle, Ev(pistaAle.infiniteClip, bX, t0 - 0.1f), Ev(pistaAle.infiniteClip, bZ, t0 - 0.1f));
        var paroM = Mundo(pistaMemi, Ev(pistaMemi.infiniteClip, bX, t0 - 0.1f), Ev(pistaMemi.infiniteClip, bZ, t0 - 0.1f));
        D = paroM - paroA; D.y = 0; D.Normalize();
        Lft = Vector3.Cross(D, Vector3.up).normalized;              // izquierda de Ale (mirando a Memi)
        M = (paroA + paroM) * 0.5f;
        // rotación con la que el Timeline pone a cada uno (offset de su pista de recorrido); la del editor no sirve
        var rotA = city.rotation * Quaternion.Euler(pistaAle.infiniteClipOffsetEulerAngles);
        var rotM = city.rotation * Quaternion.Euler(pistaMemi.infiniteClipOffsetEulerAngles);
        // estimación inicial con el modelo en reposo; luego se mide con el clip real ya retargeteado
        float kA = MedirK(FbxAleOrig, pjA.hipsH) * ale.lossyScale.y;
        float kM = MedirK(FbxMemiOrig, pjM.hipsH) * memi.lossyScale.y;
        Vector3 Obj(Personaje pj, float k) => M + (D * pj.place[0] + Lft * pj.place[1]) * k;
        var objA = Obj(pjA, kA); var objM = Obj(pjM, kM);

        // ---------- 4. Medir en Unity (AnimationMode) y ajustar para que las palmas coincidan
        var guardado = new[] { ale, memi }.Select(t => (t, t.position, t.rotation, t.gameObject.activeSelf)).ToArray();
        try
        {
            foreach (var g in guardado) g.t.gameObject.SetActive(true);
            ale.rotation = rotA; memi.rotation = rotM;
            ale.position = objA; memi.position = objM;
            var m0 = Muestrear(ale, memi, clipA, clipM, 0f);
            kA = m0.hipsA / pjA.hipsH; kM = m0.hipsM / pjM.hipsH;
            log.AppendLine($"Escala medida con el clip (cadera): Ale {kA:F3}, Memi {kM:F3} u/m (estimación en reposo era {MedirK(FbxAleOrig, pjA.hipsH):F3})");
            objA = Obj(pjA, kA); objM = Obj(pjM, kM);
            float tAgarre = (lay.hitFrame + 20 - 1) / fps;
            for (int it = 0; it < 3; it++)
            {
                ale.position = objA; memi.position = objM;
                var m1 = Muestrear(ale, memi, clipA, clipM, tAgarre);
                var err = m1.pm - m1.pa;
                float eD = Vector3.Dot(err, D), eL = Vector3.Dot(err, Lft), eY = err.y;
                float objetivoL = 0.05f * (kA + kM) * 0.5f;           // ~5 cm de grosor de mano entre palmas
                var corr = D * eD + Lft * (eL - objetivoL) + Vector3.up * eY;   // la altura se reparte: uno sube y el otro baja
                log.AppendLine($"  agarre (pasada {it + 1}): palmas separadas {err.magnitude:F2} u (frente {eD:F2}, lado {eL:F2}, alto {eY:F2})");
                if (corr.magnitude < 0.02f) break;
                objA += corr * 0.5f; objM -= corr * 0.5f;
            }
        }
        catch (Exception e) { log.AppendLine("No se pudo medir con AnimationMode (" + e.Message + "); se usa la estimación."); }
        finally
        {
            foreach (var g in guardado) { g.t.SetPositionAndRotation(g.position, g.rotation); g.t.gameObject.SetActive(g.activeSelf); }
        }
        K = (kA + kM) * 0.5f;
        log.AppendLine($"Parados: Ale {paroA}, Memi {paroM} ({Vector3.Distance(paroA, paroM):F2} u). Escala final: Ale {kA:F3}, Memi {kM:F3} u/m");
        log.AppendLine($"Orientación en reproducción (debería ser ~0°): Ale {Vector3.Angle(rotA * Vector3.forward, D):F1}°, Memi {Vector3.Angle(rotM * Vector3.forward, -D):F1}°");

        // acomodo en el corte de 49 s (salto invisible)
        Saltar(pistaAle, city, objA, t0, bX, bY, bZ);
        Saltar(pistaMemi, city, objM, t0, bX, bY, bZ);
        log.AppendLine($"En {Inicio} s: Ale -> {objA}, Memi -> {objM} ({Vector3.Distance(objA, objM):F2} u entre ellos)");

        // ---------- 5. Raíz y control del Animator
        var raiz = Raiz("SALUDO_EPICO", null);
        raiz.position = Vector3.zero; raiz.rotation = Quaternion.identity; raiz.localScale = Vector3.one;
        var grupoC = Grupo(tlC, "8 · Saludo");
        foreach (var (nombre, anim, clip) in new[] { ("ALE", animA, clipA), ("MEMI", animM, clipM) })
        {
            var go = Raiz("SALUDO_" + nombre, raiz).gameObject;
            var an = Obtener<Animator>(go);
            var sc = Obtener<SaludoControl>(go);
            Undo.RecordObject(sc, "Saludo");
            sc.animator = anim; sc.estado = "Saludo"; sc.duracion = clip.length; sc.tiempo = -1f; sc.fundido = 0.6f;
            EditorUtility.SetDirty(sc);
            // el saludo (de pie, quieto) empieza en cuanto se detienen en el encuentro: así no se quedan congelados a media zancada
            float tIni = ParoEncuentro(nombre == "ALE" ? "Ale" : "Memi", t0);
            var curva = tIni < t0 - 0.05f
                ? Curva((0f, -1f), (tIni - 0.001f, -1f), (tIni, 0f), (t0, 0f), (t0 + clip.length, clip.length), (60f, clip.length))
                : Curva((0f, -1f), (t0 - 0.001f, -1f), (t0, 0f), (t0 + clip.length, clip.length), (60f, clip.length));
            log.AppendLine($"{nombre}: pasa de la caminata al saludo a los {tIni + InicioCiudad:F2} s (mezcla 0.6 s)");
            var c = ClipPropiedad($"{Carpeta}/Saludo_{nombre}_tiempo.anim", typeof(SaludoControl), "tiempo", curva);
            ClipEnPista(tlC, dirC, grupoC, (nombre == "ALE" ? "Ale" : "Memi") + " · saludo", an, c, 0, 60);
            if (anim.cullingMode != AnimatorCullingMode.AlwaysAnimate) { Undo.RecordObject(anim, "Saludo"); anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; }
        }

        // ---------- 6. Cámaras (movimiento horneado del previz) y cortes
        // las cámaras del saludo viven en el Timeline de la ciudad (el principal solo tiene Bosque, Título, Ciudad, Cámaras y Audio)
        foreach (var viejoG in tlP.GetRootTracks().OfType<GroupTrack>().Where(g => g.name == "8 · Saludo · cámaras").ToList()) tlP.DeleteTrack(viejoG);
        var grupoP = Grupo(tlC, "8 · Saludo · cámaras");
        Undo.RecordObject(pistaCortes, "Saludo"); Undo.RecordObject(dirP, "Saludo"); Undo.RecordObject(dirC, "Saludo");
        // quitar lo de versiones anteriores (cámaras que ya no existen)
        foreach (var vieja in pistaCortes.GetClips().Where(c => c.displayName.StartsWith("CM8_") && !NombresCam.Contains(c.displayName)).ToList()) tlP.DeleteClip(vieja);
        foreach (var vieja in tlC.GetOutputTracks().Where(tr => tr.parent == grupoP && !NombresCam.Any(n => tr.name == n + " · mov")).ToList()) tlC.DeleteTrack(vieja);
        foreach (var vieja in raiz.Cast<Transform>().Where(ch => ch.name.StartsWith("CM8_") && !NombresCam.Contains(ch.name)).ToList()) Undo.DestroyObjectImmediate(vieja.gameObject);
        for (int i = 0; i < lay.cams.Length && i < NombresCam.Length; i++)
        {
            var bc = lay.cams[i]; var nombre = NombresCam[i];
            var t = Raiz(nombre, raiz);
            var vcam = Obtener<CinemachineCamera>(t.gameObject);
            Undo.RecordObject(vcam, nombre);
            var lens = cm3.GetComponent<CinemachineCamera>().Lens;
            float hfov = 2f * Mathf.Atan(bc.sensor * 0.5f / bc.lens);
            lens.FieldOfView = 2f * Mathf.Atan(Mathf.Tan(hfov * 0.5f) / AspectoPantalla) * Mathf.Rad2Deg;
            lens.NearClipPlane = Mathf.Min(lens.NearClipPlane, 0.1f);
            vcam.Lens = lens; vcam.Priority.Enabled = false;
            vcam.Target.TrackingTarget = null; vcam.Target.LookAtTarget = null;
            foreach (var comp in t.GetComponents<CinemachineComponentBase>()) Undo.DestroyObjectImmediate(comp);
            EditorUtility.SetDirty(vcam);
            var an = Obtener<Animator>(t.gameObject);
            var clip = ClipCamara($"{Carpeta}/{nombre}.anim", bc, fps, t);
            var pista = ClipEnPista(tlC, dirC, grupoP, nombre + " · mov", an, clip, Inicio - InicioCiudad, durSaludo);
            pista.trackOffset = TrackOffset.ApplyTransformOffsets; pista.position = Vector3.zero; pista.eulerAngles = Vector3.zero;

            int f0 = lay.cuts[i].frame; int f1 = i + 1 < lay.cuts.Length ? lay.cuts[i + 1].frame : lay.frames + 1;
            double ini = Inicio + (f0 - 1) / fps, fin = Math.Min(FinEscena, Inicio + (f1 - 1) / fps);
            var tc = pistaCortes.GetClips().FirstOrDefault(c => c.displayName == nombre) ?? pistaCortes.CreateClip<CinemachineShot>();
            tc.start = ini; tc.duration = fin - ini; tc.displayName = nombre;
            var shot = (CinemachineShot)tc.asset; Undo.RecordObject(shot, "Saludo");
            var er = shot.VirtualCamera;
            if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
            er.defaultValue = null; shot.VirtualCamera = er;
            dirP.SetReferenceValue(er.exposedName, vcam);
            EditorUtility.SetDirty(shot);
            log.AppendLine($"{nombre}: {ini:F2}-{fin:F2} s, FOV {lens.FieldOfView:F1}");
        }

        // ---------- 7. Choque: flash, chispas, onda, estallido, contraluces
        var grip = U(lay.grip[0], lay.grip[1], lay.grip[2]);
        double hitLocal = t0 + tHit;
        float s = K / 8f;                                     // tamaños pensados para ~8 u por metro
        var texAnillo = Textura(Carpeta + "/saludo_anillo.png", r => Mathf.Exp(-Mathf.Pow((r - 0.82f) / 0.07f, 2f)));
        var texBrillo = Textura(Carpeta + "/saludo_brillo.png", r => Mathf.Pow(Mathf.Clamp01(1f - r), 2.2f));
        var matChispa = MatAditivo(Carpeta + "/Saludo_Chispa.mat", texBrillo, new Color(1f, 0.85f, 0.6f, 1f));
        var matOnda = MatAditivo(Carpeta + "/Saludo_Onda.mat", texAnillo, new Color(1f, 0.92f, 0.9f, 1f));
        var matBrillo = MatAditivo(Carpeta + "/Saludo_Estallido.mat", texBrillo, new Color(1f, 0.9f, 0.88f, 1f));

        var flash = Raiz("SALUDO_FLASH", raiz); flash.position = grip - UDir(0, 1, 0) * 0.35f * K + Vector3.up * 0.1f * K;
        var luz = Obtener<Light>(flash.gameObject);
        luz.type = LightType.Point; luz.range = 6f * K; luz.color = new Color(1f, 0.92f, 0.85f); luz.intensity = 0f; luz.shadows = LightShadows.None;
        var anFl = Obtener<Animator>(flash.gameObject);
        float h = (float)hitLocal;
        var cFl = ClipPropiedad(Carpeta + "/Saludo_flash.anim", typeof(Light), "m_Intensity",
            Curva((0f, 0f), (h - 0.001f, 0f), (h, 80f), (h + 0.13f, 55f), (h + 0.5f, 6f), (h + 1.0f, 0f), (60f, 0f)));
        ClipEnPista(tlC, dirC, grupoC, "Saludo · flash", anFl, cFl, 0, 60);

        var chispas = Raiz("SALUDO_CHISPAS", raiz); chispas.position = grip;
        Chispas(chispas.gameObject, matChispa, s);
        Control(tlC, dirC, grupoC, "Saludo · chispas", chispas.gameObject, 1.5, hitLocal);
        var onda = Raiz("SALUDO_ONDA", raiz); onda.position = grip;
        Onda(onda.gameObject, matOnda, s);
        Control(tlC, dirC, grupoC, "Saludo · onda", onda.gameObject, 1.0, hitLocal);

        var estallido = Raiz("SALUDO_ESTALLIDO", raiz); estallido.position = U(lay.burst[0], lay.burst[1], lay.burst[2]);
        // resplandor de fondo (rosa) de los primeros planos: con soft particles, para que no se vea su orilla dura
        // donde atraviesa el piso en el plano abierto (copia del material: el original lo usan otros efectos)
        Estallido(estallido.gameObject, NuclearDetalleEditor.MaterialSuave(matBrillo, 25f), s);
        var rimA = Raiz("SALUDO_CONTRALUZ_ALE", raiz); var rimM = Raiz("SALUDO_CONTRALUZ_MEMI", raiz);
        Contraluz(rimA, U(-0.3f, 2.2f, 1.7f), U(pjA.place[0], 0, 1.1f), K);
        Contraluz(rimM, U(0.3f, 2.2f, 1.7f), U(pjM.place[0], 0, 1.1f), K);
        foreach (var (nom, go) in new[] { ("estallido", estallido.gameObject), ("contraluz Ale", rimA.gameObject), ("contraluz Memi", rimM.gameObject) })
            Activar(tlC, dirC, grupoC, "Saludo · " + nom, go, t0, durSaludo);

        // ---------- 8. Look rojo + golpe de exposición en el impacto (Volumes)
        var perfilLook = Perfil(Carpeta + "/Saludo_Look.asset", p =>
        {
            var b = Comp<Bloom>(p); b.intensity.Override(1.6f); b.threshold.Override(1.0f); b.scatter.Override(0.75f);
            var ca = Comp<ColorAdjustments>(p); ca.postExposure.Override(-0.35f); ca.contrast.Override(30f); ca.colorFilter.Override(new Color(1f, 0.62f, 0.66f)); ca.saturation.Override(15f);
            var v = Comp<Vignette>(p); v.intensity.Override(0.38f); v.smoothness.Override(0.45f);
            var ch = Comp<ChromaticAberration>(p); ch.intensity.Override(0.15f);
        });
        var perfilGolpe = Perfil(Carpeta + "/Saludo_Impacto.asset", p =>
        {
            var ca = Comp<ColorAdjustments>(p); ca.postExposure.Override(2.2f);
            var ch = Comp<ChromaticAberration>(p); ch.intensity.Override(1f);
            var ld = Comp<LensDistortion>(p); ld.intensity.Override(-0.25f);
        });
        var volLook = VolumenGlobal(Raiz("SALUDO_VOLUMEN_LOOK", raiz).gameObject, perfilLook, 50);
        var volGolpe = VolumenGlobal(Raiz("SALUDO_VOLUMEN_IMPACTO", raiz).gameObject, perfilGolpe, 60);
        float fin8 = t0 + durSaludo;
        var cLook = ClipPropiedad(Carpeta + "/Saludo_look_peso.anim", typeof(Volume), "weight",
            Curva((0f, 0f), (t0 - 0.001f, 0f), (t0, 1f), (fin8, 1f), (fin8 + 0.001f, 0f), (60f, 0f)));
        ClipEnPista(tlC, dirC, grupoC, "Saludo · look", volLook.GetComponent<Animator>(), cLook, 0, 60);
        var cGolpe = ClipPropiedad(Carpeta + "/Saludo_impacto_peso.anim", typeof(Volume), "weight",
            Curva((0f, 0f), (h - 0.001f, 0f), (h, 1f), (h + 0.13f, 1f), (h + 0.6f, 0f), (60f, 0f)));
        ClipEnPista(tlC, dirC, grupoC, "Saludo · impacto", volGolpe.GetComponent<Animator>(), cGolpe, 0, 60);

        // luces que dejan media cara iluminada en los contraplanos (la otra mitad en negro)
        float iniSombra = float.MaxValue, finSombra = 0f;
        if (lay.faceLights != null)
            foreach (var lz in lay.faceLights)
            {
                var t = Raiz("SALUDO_LUZ_" + lz.name.ToUpper(), raiz);
                var dir = UDir(lz.dir[0], lz.dir[1], lz.dir[2]);
                t.SetPositionAndRotation(U(lz.pos[0], lz.pos[1], lz.pos[2]), Quaternion.LookRotation(dir, Vector3.up));
                var l = Obtener<Light>(t.gameObject); Undo.RecordObject(l, "Saludo");
                l.type = LightType.Spot; l.range = 4f * K; l.spotAngle = 34f; l.innerSpotAngle = 16f;
                l.intensity = 650f; l.color = new Color(1f, 0.93f, 0.85f); l.shadows = LightShadows.Soft;
                EditorUtility.SetDirty(l);
                float a = t0 + (lz.startFrame - 1) / fps, b = t0 + lz.endFrame / fps;
                Activar(tlC, dirC, grupoC, "Saludo · luz " + lz.name, t.gameObject, a, b - a);
                iniSombra = Mathf.Min(iniSombra, a); finSombra = Mathf.Max(finSombra, b);
            }
        if (finSombra > 0f)
        {
            var perfilSombra = Perfil(Carpeta + "/Saludo_Sombra.asset", p =>
            {
                var ca = Comp<ColorAdjustments>(p); ca.postExposure.Override(-2.8f); ca.contrast.Override(60f); ca.saturation.Override(-35f);
                var v = Comp<Vignette>(p); v.intensity.Override(0.5f); v.smoothness.Override(0.5f);
            });
            var volSombra = VolumenGlobal(Raiz("SALUDO_VOLUMEN_SOMBRA", raiz).gameObject, perfilSombra, 55);
            var cSombra = ClipPropiedad(Carpeta + "/Saludo_sombra_peso.anim", typeof(Volume), "weight",
                Curva((0f, 0f), (iniSombra - 0.001f, 0f), (iniSombra, 1f), (finSombra, 1f), (finSombra + 0.001f, 0f), (60f, 0f)));
            ClipEnPista(tlC, dirC, grupoC, "Saludo · sombra", volSombra.GetComponent<Animator>(), cSombra, 0, 60);
        }
        // fundido a negro al final (se queda en negro cuando termina el corto)
        var perfilNegro = Perfil(Carpeta + "/Saludo_Negro.asset", p =>
        {
            var ca = Comp<ColorAdjustments>(p); ca.postExposure.Override(-8f); ca.colorFilter.Override(Color.black);
        });
        var volNegro = VolumenGlobal(Raiz("SALUDO_VOLUMEN_NEGRO", raiz).gameObject, perfilNegro, 80);
        float negro0 = t0 + (lay.fadeFrame - 1) / fps, negro1 = FinEscena - InicioCiudad - 0.35f;
        var cNegro = ClipPropiedad(Carpeta + "/Saludo_negro_peso.anim", typeof(Volume), "weight",
            Curva((0f, 0f), (negro0, 0f), (negro1, 1f), (60f, 1f)));
        ClipEnPista(tlC, dirC, grupoC, "Saludo · fundido a negro", volNegro.GetComponent<Animator>(), cNegro, 0, 60);
        log.AppendLine($"Contraplanos con luz de media cara: {Inicio + iniSombra - t0:F2}-{Inicio + finSombra - t0:F2} s. Fundido a negro: {Inicio + negro0 - t0:F2}-{FinEscena:F2} s");

        var camPrincipal = Camera.main;
        if (camPrincipal == null) { var brain = UnityEngine.Object.FindObjectsByType<CinemachineBrain>(FindObjectsSortMode.None).FirstOrDefault(); if (brain != null) camPrincipal = brain.GetComponent<Camera>(); }
        if (camPrincipal != null)
        {
            var data = camPrincipal.GetUniversalAdditionalCameraData();
            if (!data.renderPostProcessing) { Undo.RecordObject(data, "Saludo"); data.renderPostProcessing = true; EditorUtility.SetDirty(data); log.AppendLine($"{camPrincipal.name}: Post Processing activado"); }
        }
        log.AppendLine($"Choque a los {Inicio + tHit:F2} s del principal (agarre en {grip}); flash, chispas, onda, estallido, contraluces y volumes listos.");

        // ---------- 8b. Caminata y saludo como pistas del Timeline (para verlos en el preview, no solo en Play)
        if (!CuerpoTimelineEditor.Hornear(log)) log.AppendLine("OJO: no se pudo pasar el cuerpo al Timeline (ver arriba).");
        if (!SaludoAjustesEditor.Aplicar(log)) log.AppendLine("OJO: no se pudieron aplicar los ajustes (media cara de Ale, encuadre final).");
        if (!FinalTituloEditor.Aplicar(log)) log.AppendLine("OJO: no se pudo armar el final (alejar, explosión, negro y título).");

        // ---------- 9. Guardar
        foreach (var o in new UnityEngine.Object[] { tlC, tlP, pistaCortes, dirP, dirC, pistaAle.infiniteClip, pistaMemi.infiniteClip }) EditorUtility.SetDirty(o);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dirP.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        Directory.CreateDirectory("Logs");
        File.WriteAllText("Logs/saludo.txt", log.ToString());
        Debug.Log("[Saludo] Listo. Guarda la escena con Ctrl+S y dale Play para verlo (49-65.25 s).\n" + log);
    }

    static TC Obtener<TC>(GameObject go) where TC : Component
    {
        var c = go.GetComponent<TC>();
        if (c == null) c = Undo.AddComponent<TC>(go);
        return c;
    }

    // segundo local (ciudad) en que VelocidadAnimator deja la caminata en 0 (lo escribe la escena 7)
    static float ParoEncuentro(string nombre, float tope)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animaciones/Velocidad_" + nombre + ".anim");
        if (clip == null) return tope;
        var cu = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(VelocidadAnimator), "velocidad"));
        if (cu == null) return tope;
        foreach (var k in cu.keys) if (k.time > 1f && k.value <= 0.001f) return Mathf.Min(k.time, tope);
        return tope;
    }

    // ================================================================= importación / controller
    static AnimationClip ImportarClip(string ruta, string nombreClip, string fbxOriginal, StringBuilder log)
    {
        var mi = AssetImporter.GetAtPath(ruta) as ModelImporter;
        if (mi == null) { log.AppendLine("No encuentro " + ruta); return null; }
        var orig = AssetImporter.GetAtPath(fbxOriginal) as ModelImporter;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.materialImportMode = ModelImporterMaterialImportMode.None;
        mi.importAnimation = true;
        mi.useFileScale = true;
        if (orig != null) mi.globalScale = orig.globalScale;
        mi.SaveAndReimport();
        var plantilla = mi.defaultClipAnimations.FirstOrDefault();
        if (plantilla == null) { log.AppendLine(ruta + ": el FBX no trae animación"); return null; }
        plantilla.name = nombreClip;
        plantilla.loopTime = false;
        plantilla.lockRootRotation = true; plantilla.keepOriginalOrientation = true;
        plantilla.lockRootHeightY = true; plantilla.keepOriginalPositionY = true; plantilla.heightFromFeet = false;
        plantilla.lockRootPositionXZ = true; plantilla.keepOriginalPositionXZ = true;
        mi.clipAnimations = new[] { plantilla };
        mi.SaveAndReimport();
        var av = AssetDatabase.LoadAllAssetsAtPath(ruta).OfType<Avatar>().FirstOrDefault();
        var clip = AssetDatabase.LoadAllAssetsAtPath(ruta).OfType<AnimationClip>().FirstOrDefault(c => c.name == nombreClip);
        log.AppendLine($"{Path.GetFileName(ruta)}: avatar {(av != null && av.isValid && av.isHuman ? "OK" : "¡con problemas!")}, clip {(clip != null ? clip.length.ToString("F2") + " s" : "no encontrado")}");
        return clip;
    }

    static void EstadoSaludo(AnimatorController ctrl, AnimationClip clip, StringBuilder log)
    {
        var sm = ctrl.layers[0].stateMachine;
        var st = sm.states.Select(x => x.state).FirstOrDefault(x => x.name == "Saludo");
        Undo.RecordObject(sm, "Saludo");
        if (st == null) st = sm.AddState("Saludo", new Vector3(300, 260, 0));
        Undo.RecordObject(st, "Saludo");
        st.motion = clip; st.iKOnFeet = true; st.writeDefaultValues = true; st.speed = 1f;
        EditorUtility.SetDirty(st); EditorUtility.SetDirty(ctrl);
        log.AppendLine($"{ctrl.name}: estado 'Saludo' con {clip.name}");
    }

    // pone los dos clips en el segundo t (vista previa del editor, se revierte sola) y mide palmas y cadera
    static (Vector3 pa, Vector3 pm, float hipsA, float hipsM) Muestrear(Transform ale, Transform memi, AnimationClip ca, AnimationClip cm, float t)
    {
        var anA = ale.GetComponent<Animator>(); var anM = memi.GetComponent<Animator>();
        Vector3 Palma(Animator a) => (a.GetBoneTransform(HumanBodyBones.RightHand).position + a.GetBoneTransform(HumanBodyBones.RightMiddleProximal).position) * 0.5f;
        float Cadera(Animator a, Transform r) => a.GetBoneTransform(HumanBodyBones.Hips).position.y - r.position.y;
        bool yaEstaba = AnimationMode.InAnimationMode();
        if (!yaEstaba) AnimationMode.StartAnimationMode();
        try
        {
            AnimationMode.BeginSampling();
            AnimationMode.SampleAnimationClip(ale.gameObject, ca, t);
            AnimationMode.SampleAnimationClip(memi.gameObject, cm, t);
            AnimationMode.EndSampling();
            return (Palma(anA), Palma(anM), Cadera(anA, ale), Cadera(anM, memi));
        }
        finally { if (!yaEstaba) AnimationMode.StopAnimationMode(); }
    }

    // alto de la cadera del modelo en Unity / alto en Blender = unidades Unity por metro de Blender
    static float MedirK(string fbx, float hipsBlender)
    {
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        if (asset == null || hipsBlender <= 0f) return 8f;
        var tmp = (GameObject)UnityEngine.Object.Instantiate(asset);
        tmp.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); tmp.transform.localScale = Vector3.one;
        var hips = tmp.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "mixamorig:Hips");
        float k = hips != null ? hips.position.y / hipsBlender : 8f;
        UnityEngine.Object.DestroyImmediate(tmp);
        return k;
    }

    // agrega un salto (escalón) a las curvas x/z del recorrido en t0 para dejar la raíz en 'mundo'
    static void Saltar(AnimationTrack pista, Transform city, Vector3 mundo, float t0, EditorCurveBinding bX, EditorCurveBinding bY, EditorCurveBinding bZ)
    {
        var clip = pista.infiniteClip;
        var rot = Quaternion.Euler(pista.infiniteClipOffsetEulerAngles);
        var local = Quaternion.Inverse(rot) * (city.InverseTransformPoint(mundo) - pista.infiniteClipOffsetPosition);
        Undo.RecordObject(clip, "Saludo");
        foreach (var (b, v) in new[] { (bX, local.x), (bY, local.y), (bZ, local.z) })
        {
            var cu = AnimationUtility.GetEditorCurve(clip, b) ?? new AnimationCurve(new Keyframe(0f, 0f));
            float previo = cu.Evaluate(t0 - 0.1f);
            var keys = cu.keys.Where(k => k.time < t0 - 0.05f).ToList();
            keys.Add(new Keyframe(t0 - 1f / 60f, previo, 0f, 0f));
            keys.Add(new Keyframe(t0, v, 0f, 0f));
            var nueva = new AnimationCurve(keys.ToArray());
            int n = nueva.length;
            AnimationUtility.SetKeyRightTangentMode(nueva, n - 2, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyLeftTangentMode(nueva, n - 1, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetKeyRightTangentMode(nueva, n - 1, AnimationUtility.TangentMode.Constant);
            AnimationUtility.SetEditorCurve(clip, b, nueva);
        }
        EditorUtility.SetDirty(clip);
    }

    // ================================================================= timeline
    static GroupTrack Grupo(TimelineAsset tl, string nombre)
    {
        var g = tl.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(x => x.name == nombre);
        if (g == null) { Undo.RecordObject(tl, "Saludo"); g = tl.CreateTrack<GroupTrack>(null, nombre); }
        return g;
    }

    static T Pista<T>(TimelineAsset tl, TrackAsset padre, string nombre) where T : TrackAsset, new()
    {
        var p = tl.GetOutputTracks().OfType<T>().FirstOrDefault(x => x.name == nombre);
        Undo.RecordObject(tl, "Saludo");
        if (p == null) p = tl.CreateTrack<T>(padre, nombre);
        foreach (var c in p.GetClips().ToList()) tl.DeleteClip(c);
        return p;
    }

    static AnimationTrack ClipEnPista(TimelineAsset tl, PlayableDirector dir, TrackAsset padre, string nombre, Animator an, AnimationClip clip, double inicio, double dur)
    {
        var p = Pista<AnimationTrack>(tl, padre, nombre);
        var tc = p.CreateClip(clip); tc.start = inicio; tc.duration = dur; tc.displayName = clip.name;
        if (tc.asset is AnimationPlayableAsset apa) apa.removeStartOffset = false;
        dir.SetGenericBinding(p, an);
        EditorUtility.SetDirty(p);
        return p;
    }

    static void Control(TimelineAsset tl, PlayableDirector dir, TrackAsset padre, string nombre, GameObject go, double dur, params double[] inicios)
    {
        var p = Pista<ControlTrack>(tl, padre, nombre);
        foreach (var inicio in inicios)
        {
            var tc = p.CreateDefaultClip(); tc.start = inicio; tc.duration = dur; tc.displayName = go.name;
            var a = (ControlPlayableAsset)tc.asset;
            var er = a.sourceGameObject;
            if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
            er.defaultValue = null; a.sourceGameObject = er;
            a.updateParticle = true; a.active = true; a.updateDirector = false; a.updateITimeControl = false; a.searchHierarchy = false;
            dir.SetReferenceValue(er.exposedName, go);
            EditorUtility.SetDirty(a);
        }
        EditorUtility.SetDirty(p);
    }

    static void Activar(TimelineAsset tl, PlayableDirector dir, TrackAsset padre, string nombre, GameObject go, double inicio, double dur)
    {
        var p = Pista<ActivationTrack>(tl, padre, nombre);
        var tc = p.CreateDefaultClip(); tc.start = inicio; tc.duration = dur; tc.displayName = "Visible";
        p.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
        dir.SetGenericBinding(p, go);
        go.SetActive(false);
        EditorUtility.SetDirty(p);
    }

    // ================================================================= clips
    static AnimationCurve Curva(params (float t, float v)[] k)
    {
        var c = new AnimationCurve(k.Select(x => new Keyframe(x.t, x.v)).ToArray());
        for (int i = 0; i < c.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Linear);
        }
        return c;
    }

    static AnimationClip ClipPropiedad(string ruta, Type tipo, string prop, AnimationCurve curva)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (clip == null) { clip = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clip, ruta); }
        clip.ClearCurves();
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", tipo, prop), curva);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    static AnimationClip ClipCamara(string ruta, Cam bc, float fps, Transform t)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (clip == null) { clip = new AnimationClip { frameRate = fps }; AssetDatabase.CreateAsset(clip, ruta); }
        clip.ClearCurves();
        int n = bc.data.Length / 9;
        var ch = new Keyframe[7][]; for (int j = 0; j < 7; j++) ch[j] = new Keyframe[n];
        Quaternion prev = Quaternion.identity;
        for (int i = 0; i < n; i++)
        {
            var d = bc.data; int o = i * 9;
            var p = U(d[o], d[o + 1], d[o + 2]);
            var q = Quaternion.LookRotation(UDir(d[o + 3], d[o + 4], d[o + 5]), UDir(d[o + 6], d[o + 7], d[o + 8]));
            if (i > 0 && Quaternion.Dot(prev, q) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            prev = q;
            float tt = i / fps;
            float[] v = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
            for (int j = 0; j < 7; j++) ch[j][i] = new Keyframe(tt, v[j]);
            if (i == 0) t.SetPositionAndRotation(p, q);
        }
        string[] props = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        for (int j = 0; j < 7; j++)
        {
            var c = new AnimationCurve(ch[j]);
            for (int i = 0; i < c.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), props[j]), c);
        }
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // ================================================================= objetos de escena
    static Transform Raiz(string nombre, Transform padre)
    {
        Transform t = padre != null ? padre.Find(nombre) : null;
        if (t == null)
        {
            var existente = UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == nombre);
            t = existente;
        }
        if (t == null) { var go = new GameObject(nombre); Undo.RegisterCreatedObjectUndo(go, nombre); t = go.transform; }
        if (padre != null && t.parent != padre) Undo.SetTransformParent(t, padre, "Saludo");
        Undo.RecordObject(t, "Saludo");
        return t;
    }

    static Texture2D Textura(string ruta, Func<float, float> perfil)
    {
        if (!File.Exists(ruta))
        {
            const int N = 256; var tex = new Texture2D(N, N, TextureFormat.RGBA32, false);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float r = new Vector2(x - N / 2f + 0.5f, y - N / 2f + 0.5f).magnitude / (N / 2f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, r >= 1f ? 0f : Mathf.Clamp01(perfil(r))));
                }
            File.WriteAllBytes(ruta, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(ruta);
            var ti = (TextureImporter)AssetImporter.GetAtPath(ruta);
            ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
            ti.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture2D>(ruta);
    }

    static Material MatAditivo(string ruta, Texture2D tex, Color color)
    {
        var sh = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
        m.shader = sh;
        m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", color);
        m.SetFloat("_Surface", 1f); m.SetFloat("_Blend", 2f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_ZWrite", 0f);
        m.SetOverrideTag("RenderType", "Transparent");
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        EditorUtility.SetDirty(m);
        return m;
    }

    static ParticleSystem Sistema(GameObject go)
    {
        var ps = Obtener<ParticleSystem>(go);
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        Undo.RecordObject(ps, "Saludo");
        return ps;
    }

    static Gradient Desvanecer(Color a, Color b, float picoAlfa = 0f)
    {
        var g = new Gradient();
        var alphas = picoAlfa > 0f
            ? new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, picoAlfa), new GradientAlphaKey(0f, 1f) }
            : new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) };
        g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) }, alphas);
        return g;
    }

    static void Chispas(GameObject go, Material mat, float s)
    {
        var ps = Sistema(go);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 1f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.7f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(6f * s, 16f * s);
        main.startSize = new ParticleSystem.MinMaxCurve(0.05f * s, 0.12f * s);
        main.gravityModifier = 1.2f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.maxParticles = 300;
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.95f, 0.85f), new Color(1f, 0.55f, 0.25f));
        var em = ps.emission; em.enabled = true; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.15f * s;
        var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(Desvanecer(Color.white, new Color(1f, 0.5f, 0.2f)));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.04f; r.lengthScale = 1.5f; r.sharedMaterial = mat;
        EditorUtility.SetDirty(ps); EditorUtility.SetDirty(r);
    }

    static void Onda(GameObject go, Material mat, float s)
    {
        var ps = Sistema(go);
        var main = ps.main;
        main.playOnAwake = false; main.loop = false; main.duration = 0.6f;
        main.startLifetime = 0.45f; main.startSpeed = 0f; main.startSize = 16f * s; main.maxParticles = 2;
        main.simulationSpace = ParticleSystemSimulationSpace.World; main.startColor = Color.white;
        var em = ps.emission; em.enabled = true; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
        var sh = ps.shape; sh.enabled = false;
        var sz = ps.sizeOverLifetime; sz.enabled = true;
        sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.04f, 0f, 4f), new Keyframe(1f, 1f, 0.3f, 0f)));
        var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(Desvanecer(Color.white, Color.white));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sharedMaterial = mat; r.maxParticleSize = 5f;
        EditorUtility.SetDirty(ps); EditorUtility.SetDirty(r);
    }

    static void Estallido(GameObject go, Material mat, float s)
    {
        var ps = Sistema(go);
        var main = ps.main;
        main.playOnAwake = true; main.loop = true; main.prewarm = true; main.duration = 1f;
        main.startLifetime = 1f; main.startSpeed = 0f; main.startSize = 70f * s; main.maxParticles = 2;
        main.startColor = new Color(1f, 0.9f, 0.88f, 1f);
        var em = ps.emission; em.enabled = true; em.rateOverTime = 2f; em.SetBursts(new ParticleSystem.Burst[0]);
        var sh = ps.shape; sh.enabled = false;
        var col = ps.colorOverLifetime; col.enabled = true; col.color = new ParticleSystem.MinMaxGradient(Desvanecer(Color.white, Color.white, 0.5f));
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.renderMode = ParticleSystemRenderMode.Billboard; r.sharedMaterial = mat; r.maxParticleSize = 5f;
        EditorUtility.SetDirty(ps); EditorUtility.SetDirty(r);
    }

    static void Contraluz(Transform t, Vector3 pos, Vector3 mira, float k)
    {
        t.SetPositionAndRotation(pos, Quaternion.LookRotation(mira - pos, Vector3.up));
        var l = Obtener<Light>(t.gameObject);
        Undo.RecordObject(l, "Saludo");
        l.type = LightType.Spot; l.range = 5f * k; l.spotAngle = 70f; l.innerSpotAngle = 35f;
        l.intensity = 220f; l.color = Color.white; l.shadows = LightShadows.None;
        EditorUtility.SetDirty(l);
    }

    // ================================================================= volumes
    static VolumeProfile Perfil(string ruta, Action<VolumeProfile> llenar)
    {
        var p = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ruta);
        if (p == null) { p = ScriptableObject.CreateInstance<VolumeProfile>(); AssetDatabase.CreateAsset(p, ruta); }
        llenar(p);
        EditorUtility.SetDirty(p);
        return p;
    }

    static T Comp<T>(VolumeProfile p) where T : VolumeComponent
    {
        if (p.TryGet<T>(out var c)) return c;
        c = p.Add<T>(true);
        c.name = typeof(T).Name;
        AssetDatabase.AddObjectToAsset(c, p);
        return c;
    }

    static Volume VolumenGlobal(GameObject go, VolumeProfile perfil, float prioridad)
    {
        var v = Obtener<Volume>(go);
        Undo.RecordObject(v, "Saludo");
        v.isGlobal = true; v.priority = prioridad; v.sharedProfile = perfil; v.weight = 0f;
        if (go.GetComponent<Animator>() == null) Undo.AddComponent<Animator>(go);
        EditorUtility.SetDirty(v);
        return v;
    }
}
