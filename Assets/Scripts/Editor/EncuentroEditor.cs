using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > Escena 7: encuentro (46-49 s)
/// Memi y Ale llegan caminando (cada uno entra a cuadro por un lado) y se detienen frente a frente.
///  - Memi: después de su toma sigue caminando a su paso (fuera de cuadro) hasta el punto de encuentro.
///  - Ale: camina a su paso en su toma (20.5-23.5), recorre fuera de cuadro lo que le falta y entra al encuentro a su paso.
///  - Los dos frenan en ~0.6 s; VelocidadAnimator (en VELOCIDAD_MEMI / VELOCIDAD_ALE, con su propia pista) baja la
///    velocidad de su Animator a 0 al mismo tiempo (se congela la caminata mientras no haya animación de "parado").
///  - CM7_ENCUENTRO: cámara fija de lado, plano medio, contrapicada (guion técnico, plano 7).
///  - Todo va en el Timeline de la ciudad (curvas de "MC · recorrido" y "Ale · recorrido"); el corte en el principal.
/// Se puede volver a correr: todos los valores son absolutos.
/// </summary>
public static class EncuentroEditor
{
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const float InicioCiudad = 17.5f;
    const float InicioEscena = 46f, FinEscena = 49f;        // tiempos del principal
    const float Frenado = 0.6f;                              // segundos que tardan en detenerse
    const float FinCaminando = 30.2f;                        // local de la ciudad: empiezan a frenar (= 47.7 del principal)
    const float Separacion = 11f;                            // u entre los dos al detenerse (≈ 1.3 m)
    const float FinTomaMemi = 13.95f;                        // local: último key del recorrido original de Memi
    const float VelMemi = 0.045f, VelAle = 0.045f;           // unidades locales de CITY por s (4.5 u/s)
    const float FinTomaAle = 6f;                             // local: termina la toma de Ale

    // Cámara (unidades de mundo)
    const float DistCam = 19f;          // de lado, a esta distancia de la línea de caminata
    const float AlturaCam = 7f;         // baja: contrapicado
    const float AlturaMirada = 11.5f;   // a qué altura mira (abdomen): deja las cabezas arriba del cuadro
    const float Fov = 40f;
    const float Lado = 1f;              // +1: cámara del lado +X; -1: del lado -X
    const string NombreCam = "CM7_ENCUENTRO";

    [MenuItem("Tools/Amistad/Escena 7: encuentro (46-49 s)")]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Encuentro] Sal de Play antes de correr la herramienta."); return; }
        var log = new StringBuilder();
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var directores = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = directores.FirstOrDefault(d => d.playableAsset == tlP);
        var dirC = directores.FirstOrDefault(d => d.playableAsset == tlC);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var city = T("CITY"); var memi = T("MEMO_PERFIL"); var ale = T("ALE"); var cm3 = T("CM3_MEMO");
        var pistaMemi = tlC != null ? tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.StartsWith("MC") && t.name.Contains("recorrido")) : null;
        var pistaAle = tlC != null ? tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.StartsWith("Ale") && t.name.Contains("recorrido")) : null;
        var pistaCortes = tlP != null ? tlP.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault() : null;
        if (dirP == null || dirC == null || city == null || memi == null || ale == null || cm3 == null || pistaMemi?.infiniteClip == null || pistaAle?.infiniteClip == null || pistaCortes == null)
        { Debug.LogError("[Encuentro] Falta algo (directores, CITY, MEMO_PERFIL, ALE, CM3_MEMO o las pistas de recorrido). Corre antes 'Ciudad: Memi de perfil + Ale'."); return; }

        // Animator siempre activo: con el culling por defecto, si ninguna cámara ve la malla deja de mover los huesos,
        // el cuerpo queda en la pose del modelo (cadera en el piso, medio enterrado) y ya no se vuelve a ver.
        foreach (var p in new[] { memi, ale })
        {
            var an = p.GetComponent<Animator>();
            if (an != null && an.cullingMode != AnimatorCullingMode.AlwaysAnimate) { Undo.RecordObject(an, "Encuentro"); an.cullingMode = AnimatorCullingMode.AlwaysAnimate; EditorUtility.SetDirty(an); log.AppendLine($"{p.name}: Animator en Always Animate"); }
        }

        float escala = city.lossyScale.z;
        float tParo = FinCaminando + Frenado;                       // local: ya detenidos
        float tEntrada = InicioEscena - InicioCiudad;              // local: empieza la escena

        // ---------- 1. Memi ----------
        var clipM = pistaMemi.infiniteClip;
        var bZ = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.z");
        var cz = AnimationUtility.GetEditorCurve(clipM, bZ);
        var keys = cz.keys.Where(k => k.time <= FinTomaMemi + 0.01f).ToList();
        var kFin = keys[keys.Count - 1]; kFin.outTangent = VelMemi; keys[keys.Count - 1] = kFin;
        float memiFrena = kFin.value + VelMemi * (FinCaminando - kFin.time);
        float memiParo = memiFrena + VelMemi * Frenado * 0.5f;
        keys.Add(new Keyframe(FinCaminando, memiFrena, VelMemi, VelMemi));
        keys.Add(new Keyframe(tParo, memiParo, 0f, 0f));
        Undo.RecordObject(clipM, "Encuentro");
        AnimationUtility.SetEditorCurve(clipM, bZ, new AnimationCurve(keys.ToArray()));
        Velocidad(memi, clipM, "Memi", tlC, dirC);

        // Punto donde se detiene Memi (mundo)
        Vector3 MundoMemi(float zClip) => city.TransformPoint(pistaMemi.infiniteClipOffsetPosition + Quaternion.Euler(pistaMemi.infiniteClipOffsetEulerAngles) * new Vector3(0, 0, zClip));
        var paroMemi = MundoMemi(memiParo);
        var dirMemi = (MundoMemi(memiParo) - MundoMemi(memiParo - 0.1f)); dirMemi.y = 0; dirMemi.Normalize();
        log.AppendLine($"Memi camina hasta {paroMemi} y se detiene a los {InicioCiudad + tParo:F2} s");

        // ---------- 2. Ale: se detiene frente a Memi ----------
        var paroAle = paroMemi + dirMemi * Separacion;                      // Ale viene de frente a Memi
        var clipA = pistaAle.infiniteClip;
        var origenA = city.TransformPoint(pistaAle.infiniteClipOffsetPosition);
        var dirA = city.TransformDirection(Quaternion.Euler(pistaAle.infiniteClipOffsetEulerAngles) * Vector3.forward); dirA.y = 0; dirA.Normalize();
        float aleParo = Vector3.Dot(paroAle - origenA, dirA) / escala;     // en unidades del clip
        float aleFrena = aleParo - VelAle * Frenado * 0.5f;
        float aleEntra = aleFrena - VelAle * (FinCaminando - tEntrada);
        float aleFinToma = VelAle * FinTomaAle;
        if (aleEntra <= aleFinToma) { Debug.LogError("[Encuentro] Ale quedaría caminando hacia atrás; revisa AleZMundo en CiudadTomasEditor."); return; }
        var curvaA = new AnimationCurve(
            new Keyframe(0f, 0f, VelAle, VelAle),
            new Keyframe(FinTomaAle, aleFinToma, VelAle, VelAle),
            new Keyframe(tEntrada, aleEntra, VelAle, VelAle),
            new Keyframe(FinCaminando, aleFrena, VelAle, VelAle),
            new Keyframe(tParo, aleParo, 0f, 0f));
        Undo.RecordObject(clipA, "Encuentro");
        AnimationUtility.SetEditorCurve(clipA, bZ, curvaA);
        Velocidad(ale, clipA, "Ale", tlC, dirC);
        float vFuera = (aleEntra - aleFinToma) / (tEntrada - FinTomaAle) * escala;
        log.AppendLine($"Ale se detiene en {paroAle} (a {Separacion} u de Memi). Fuera de cuadro recorre a {vFuera:F1} u/s.");

        // ---------- 3. La ciudad dura lo suficiente; el fuego sigue ----------
        Undo.RecordObject(tlP, "Encuentro"); Undo.RecordObject(tlC, "Encuentro");
        var pistaCiudad = tlP.GetOutputTracks().OfType<ControlTrack>().FirstOrDefault(t => t.name.Contains("Ciudad"));
        if (pistaCiudad != null) foreach (var c in pistaCiudad.GetClips()) if (c.end < 65.25) { Undo.RecordObject(pistaCiudad, "Encuentro"); c.duration = 65.25 - c.start; }
        foreach (var p in tlC.GetOutputTracks().OfType<ControlTrack>().Where(t => t.name.Contains("Fuego")))
            foreach (var c in p.GetClips()) if (c.end < 60) { Undo.RecordObject(p, "Encuentro"); c.duration = 60 - c.start; }

        // ---------- 4. Cámara ----------
        var medio = (paroMemi + paroAle) * 0.5f;
        var lateral = Vector3.Cross(Vector3.up, dirMemi).normalized;      // perpendicular a la línea de caminata
        if (Vector3.Dot(lateral, Vector3.right) * Lado < 0) lateral = -lateral;
        var posCam = medio + lateral * DistCam + Vector3.up * AlturaCam;
        var mira = medio + Vector3.up * AlturaMirada;
        var t = T(NombreCam);
        if (t == null) { var go = new GameObject(NombreCam); Undo.RegisterCreatedObjectUndo(go, NombreCam); t = go.transform; }
        Undo.RecordObject(t, NombreCam);
        t.SetPositionAndRotation(posCam, Quaternion.LookRotation(mira - posCam, Vector3.up));
        t.localScale = Vector3.one;
        var vcam = t.GetComponent<CinemachineCamera>(); if (vcam == null) vcam = Undo.AddComponent<CinemachineCamera>(t.gameObject);
        Undo.RecordObject(vcam, NombreCam);
        var lens = cm3.GetComponent<CinemachineCamera>().Lens; lens.FieldOfView = Fov;
        vcam.Lens = lens; vcam.Priority.Enabled = false;
        vcam.Target.TrackingTarget = null; vcam.Target.LookAtTarget = null;
        foreach (var c in t.GetComponents<CinemachineComponentBase>()) Undo.DestroyObjectImmediate(c);
        EditorUtility.SetDirty(vcam);
        log.AppendLine($"{NombreCam} en {posCam}, mirando a {mira} (contrapicado {Vector3.Angle(Vector3.ProjectOnPlane(mira - posCam, Vector3.up), mira - posCam):F0}°)");

        // ---------- 5. Corte en el principal ----------
        Undo.RecordObject(pistaCortes, "Encuentro"); Undo.RecordObject(dirP, "Encuentro");
        var clip = pistaCortes.GetClips().FirstOrDefault(c => c.displayName == NombreCam) ?? pistaCortes.CreateClip<CinemachineShot>();
        clip.start = InicioEscena; clip.duration = FinEscena - InicioEscena; clip.displayName = NombreCam;
        var shot = (CinemachineShot)clip.asset;
        Undo.RecordObject(shot, "Encuentro");
        var er = shot.VirtualCamera;
        if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
        er.defaultValue = null; shot.VirtualCamera = er;
        dirP.SetReferenceValue(er.exposedName, vcam);
        EditorUtility.SetDirty(shot);
        log.AppendLine($"Corte {NombreCam}: {InicioEscena}-{FinEscena} s");

        EditorUtility.SetDirty(clipM); EditorUtility.SetDirty(clipA); EditorUtility.SetDirty(tlC); EditorUtility.SetDirty(tlP);
        EditorUtility.SetDirty(pistaCortes); EditorUtility.SetDirty(dirP); EditorUtility.SetDirty(dirC);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dirP.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        System.IO.File.WriteAllText("Logs/encuentro.txt", log.ToString());
        Debug.Log("[Encuentro] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }

    // Velocidad del Animator en un objeto aparte (VELOCIDAD_<nombre>) con su propia pista y su propio clip.
    // OJO: no meter esta curva en el clip del recorrido: en un humanoide eso hace que el Timeline ignore la caminata.
    static void Velocidad(Transform personaje, AnimationClip clipRecorrido, string nombre, TimelineAsset tl, PlayableDirector dir)
    {
        // 1) Quitar la curva vieja del clip del recorrido y el componente del personaje
        var bVieja = EditorCurveBinding.FloatCurve("", typeof(VelocidadAnimator), "velocidad");
        if (AnimationUtility.GetEditorCurve(clipRecorrido, bVieja) != null) { Undo.RecordObject(clipRecorrido, "Encuentro"); AnimationUtility.SetEditorCurve(clipRecorrido, bVieja, null); }
        var viejo = personaje.GetComponent<VelocidadAnimator>();
        if (viejo != null) Undo.DestroyObjectImmediate(viejo);

        // 2) Objeto controlador
        var nombreObj = "VELOCIDAD_" + nombre.ToUpper();
        var go = GameObject.Find(nombreObj);
        if (go == null) { go = new GameObject(nombreObj); Undo.RegisterCreatedObjectUndo(go, "Encuentro"); }
        var anim = go.GetComponent<Animator>(); if (anim == null) anim = Undo.AddComponent<Animator>(go);
        var va = go.GetComponent<VelocidadAnimator>(); if (va == null) va = Undo.AddComponent<VelocidadAnimator>(go);
        Undo.RecordObject(va, "Encuentro");
        va.animator = personaje.GetComponent<Animator>(); va.velocidad = 1f;
        EditorUtility.SetDirty(va);

        // 3) Clip y pista
        var ruta = "Assets/Animaciones/Velocidad_" + nombre + ".anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (clip == null) { clip = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clip, ruta); }
        var curva = new AnimationCurve(
            new Keyframe(0f, 1f, 0f, 0f),
            new Keyframe(FinCaminando, 1f, 0f, 0f),
            new Keyframe(FinCaminando + Frenado, 0f, 0f, 0f),
            new Keyframe(60f, 0f, 0f, 0f));
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(VelocidadAnimator), "velocidad"), curva);
        EditorUtility.SetDirty(clip);
        var nombrePista = nombre + " · velocidad";
        var pista = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == nombrePista) ?? tl.CreateTrack<AnimationTrack>(null, nombrePista);
        foreach (var c in pista.GetClips().ToList()) tl.DeleteClip(c);
        var tc = pista.CreateClip(clip); tc.start = 0; tc.duration = 60; tc.displayName = "Velocidad " + nombre;
        dir.SetGenericBinding(pista, anim);
        EditorUtility.SetDirty(pista);
    }
}
