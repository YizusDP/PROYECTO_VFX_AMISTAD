using System.Linq;
using System.Text;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > Ciudad: Memi de perfil + Ale + recorrer derrumbe
/// Nuevo orden de la ciudad (tiempos del Timeline principal):
///   17.5 - 20.5  CM3_MEMO_PERFIL  Memi camina de perfil; el edificio sigue entero y Godzilla aún no aparece.
///   20.5 - 23.5  CM3_ALE          Ale camina de perfil al otro extremo de la avenida, hacia Memi.
///   23.5 - ...   CM3_MEMO + CM3_GODZILLA, igual que antes (derrumbe y Godzilla), recorrido 6 s.
/// 1) Recorre 'Recorrido' s todo lo del Timeline de la ciudad (clips, recorrido de Memi, dolly de CM3_MEMO),
///    salvo el ambiente (Caos y Fuego, que siguen desde 0). Esto se hace UNA sola vez (detecta si ya se aplicó).
/// 2) Memi camina desde antes: llega a su punto de inicio de siempre justo cuando arranca CM3_MEMO.
/// 3) Cámaras de frente, del pecho a la cabeza: van delante del personaje y retroceden con él (Follow en espacio de mundo,
///    sin girar). A Memi se le ve con el edificio entero de fondo; a Ale con la avenida en llamas.
/// 4) Ale: pistas "Ale · visible" y "Ale · recorrido" en el Timeline de la ciudad (camina hacia Memi sobre su misma línea).
/// 5) GOKU_PRUEBA se baja 5000 u (fuera de cuadro) para que no estorbe.
/// Se puede volver a correr para reacomodar cámaras y Ale (el recorrido de 6 s no se repite).
/// </summary>
public static class CiudadTomasEditor
{
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const float InicioCiudad = 17.5f;        // tiempo del principal donde empieza la ciudad
    const float DurPerfil = 3f;              // toma de Memi de perfil
    const float DurAle = 3f;                 // toma de Ale
    const float Recorrido = DurPerfil + DurAle;

    // Cámaras de frente (unidades de mundo; 1 m = 8.43 u, un personaje mide 17 u, ojos a ~15.8 u)
    const float DistFrente = 12f;            // ≈ 1.4 m delante: con FOV 40 se ve del pecho a la cabeza
    const float AlturaCam = 14.5f;           // un poco abajo de los ojos
    const float Fov = 40f;
    const float Inclinacion = 0f;            // grados hacia abajo (+) o arriba (-)

    // Velocidades en unidades locales de CITY por segundo (CITY ×100: 0.045 = 4.5 u/s)
    const float VelMemi = 0.045f;            // igual a su paso en CM3_MEMO
    const float VelAle = 0.045f;
    const float AleZMundo = -700f;           // dónde va Ale (z de mundo) al empezar su toma: zona de fuego de la avenida

    const string NombrePerfil = "CM3_MEMO_PERFIL";
    const string NombreAle = "CM3_ALE";

    [MenuItem("Tools/Amistad/Ciudad: Memi de perfil + Ale + recorrer derrumbe")]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Ciudad tomas] Sal de Play antes de correr la herramienta."); return; }
        var log = new StringBuilder();
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        if (tlP == null || tlC == null) { Debug.LogError("[Ciudad tomas] No encontré los Timelines"); return; }
        var directores = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = directores.FirstOrDefault(d => d.playableAsset == tlP);
        var dirC = directores.FirstOrDefault(d => d.playableAsset == tlC);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var city = T("CITY"); var memi = T("MEMO_PERFIL"); var ale = T("ALE"); var cm3 = T("CM3_MEMO");
        if (dirP == null || dirC == null || city == null || memi == null || ale == null || cm3 == null)
        { Debug.LogError("[Ciudad tomas] Falta algo: directores, CITY, MEMO_PERFIL, ALE o CM3_MEMO"); return; }
        var pistaMemi = tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.Contains("recorrido") && t.name.StartsWith("MC"));
        var pistaCortes = tlP.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault();
        if (pistaMemi == null || pistaMemi.infiniteClip == null || pistaCortes == null)
        { Debug.LogError("[Ciudad tomas] No encontré la pista 'MC · recorrido' o la de Cortes de cámara"); return; }

        Undo.RecordObject(tlC, "Ciudad tomas"); Undo.RecordObject(tlP, "Ciudad tomas");
        Undo.RecordObject(dirC, "Ciudad tomas"); Undo.RecordObject(dirP, "Ciudad tomas");
        bool yaAplicado = pistaCortes.GetClips().Any(c => c.displayName == NombrePerfil);

        // ---------- 1. Recorrer todo lo de la ciudad ----------
        if (!yaAplicado)
        {
            foreach (var pista in tlC.GetOutputTracks())
            {
                Undo.RecordObject(pista, "Ciudad tomas");
                if (pista is AnimationTrack at && at.infiniteClip != null)
                {
                    RecorrerCurvas(at.infiniteClip, Recorrido, at == pistaMemi);
                    log.AppendLine($"Curvas de '{pista.name}' +{Recorrido} s");
                }
                if (pista is ControlTrack)
                {
                    // Ambiente (Caos, Fuego): sigue desde 0; el fuego dura lo mismo que antes respecto al final
                    foreach (var c in pista.GetClips()) if (pista.name.Contains("Fuego")) { c.duration += Recorrido; log.AppendLine($"'{c.displayName}' dura +{Recorrido} s"); }
                    continue;
                }
                foreach (var c in pista.GetClips()) { c.start += Recorrido; log.AppendLine($"Clip '{c.displayName}' ({pista.name}) -> {c.start:F2} s locales"); }
                EditorUtility.SetDirty(pista);
            }
            // Principal: la ciudad dura más y sus cortes se recorren
            var pistaCiudad = tlP.GetOutputTracks().OfType<ControlTrack>().FirstOrDefault(t => t.name.Contains("Ciudad"));
            if (pistaCiudad != null) { Undo.RecordObject(pistaCiudad, "Ciudad tomas"); foreach (var c in pistaCiudad.GetClips()) c.duration += Recorrido; EditorUtility.SetDirty(pistaCiudad); }
            Undo.RecordObject(pistaCortes, "Ciudad tomas");
            foreach (var c in pistaCortes.GetClips().Where(c => c.start >= InicioCiudad - 0.01).OrderByDescending(c => c.start))
            { c.start += Recorrido; log.AppendLine($"Corte '{c.displayName}' -> {c.start:F2} s"); }
        }
        else log.AppendLine("El recorrido de 6 s ya estaba aplicado: solo se reacomodan cámaras y Ale.");

        // ---------- 2. Geometría: línea de Memi y lado del edificio ----------
        var offMemi = pistaMemi.infiniteClipOffsetPosition;
        var lineaMemi = city.TransformPoint(offMemi);                 // donde arranca CM3_MEMO
        var derrumbe = Object.FindObjectsByType<DerrumbeHorneado>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        Vector3 edificio = derrumbe != null ? Centro(derrumbe.transform) : (T("ruim.008") != null ? Centro(T("ruim.008")) : lineaMemi + Vector3.left);
        var dirMemi = city.TransformDirection(Quaternion.Euler(pistaMemi.infiniteClipOffsetEulerAngles) * Vector3.forward); dirMemi.y = 0; dirMemi.Normalize();
        log.AppendLine($"Línea de Memi x={lineaMemi.x:F1}, camina hacia {dirMemi}; edificio en {edificio}");

        // ---------- 3. Cámaras de perfil ----------
        var lens = cm3.GetComponent<CinemachineCamera>().Lens;
        lens.FieldOfView = Fov;
        float dirZ = Mathf.Sign(lineaMemi.z - AleZMundo); if (dirZ == 0) dirZ = 1;
        var camPerfil = CamaraFrente(NombrePerfil, memi, dirMemi, lens, todos);
        var camAle = CamaraFrente(NombreAle, ale, new Vector3(0, 0, dirZ), lens, todos);

        // ---------- 4. Ale caminando hacia Memi ----------
        float zInicioMundo = AleZMundo - dirZ * VelAle * city.lossyScale.z * DurPerfil;   // en t=0 local, para estar en AleZMundo al empezar su toma
        var inicioAle = city.InverseTransformPoint(new Vector3(lineaMemi.x, lineaMemi.y, zInicioMundo));
        inicioAle.y = offMemi.y;
        var grupo = tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(t => t.name == "Ale") ?? tlC.CreateTrack<GroupTrack>(null, "Ale");
        var pistaVis = grupo.GetChildTracks().OfType<ActivationTrack>().FirstOrDefault() ?? tlC.CreateTrack<ActivationTrack>(grupo, "Ale · visible");
        if (!pistaVis.GetClips().Any())   // si ya tiene clips (CM4 los parte), no se tocan
        { var cVis = pistaVis.CreateDefaultClip(); cVis.start = 0; cVis.duration = 60; cVis.displayName = "Ale visible"; }
        var pistaAle = grupo.GetChildTracks().OfType<AnimationTrack>().FirstOrDefault() ?? tlC.CreateTrack<AnimationTrack>(grupo, "Ale · recorrido");
        Undo.RecordObject(pistaAle, "Ciudad tomas");
        if (pistaAle.infiniteClip == null) pistaAle.CreateInfiniteClip("Ale recorrido");
        var clipAle = pistaAle.infiniteClip;
        SetCurva(clipAle, "m_LocalPosition.x", new AnimationCurve(new Keyframe(0, 0), new Keyframe(60, 0)));
        SetCurva(clipAle, "m_LocalPosition.y", new AnimationCurve(new Keyframe(0, 0), new Keyframe(60, 0)));
        // Si la escena 7 (EncuentroEditor) ya ajustó el recorrido de Ale, no se pisa
        var zAle = AnimationUtility.GetEditorCurve(clipAle, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.z"));
        if (zAle == null || zAle.length <= 2)
            SetCurva(clipAle, "m_LocalPosition.z", new AnimationCurve(new Keyframe(0, 0, VelAle, VelAle), new Keyframe(60, VelAle * 60, VelAle, VelAle)));
        SetCurva(clipAle, "m_LocalRotation.x", new AnimationCurve(new Keyframe(0, 0), new Keyframe(60, 0)));
        SetCurva(clipAle, "m_LocalRotation.y", new AnimationCurve(new Keyframe(0, 0), new Keyframe(60, 0)));
        SetCurva(clipAle, "m_LocalRotation.z", new AnimationCurve(new Keyframe(0, 0), new Keyframe(60, 0)));
        SetCurva(clipAle, "m_LocalRotation.w", new AnimationCurve(new Keyframe(0, 1), new Keyframe(60, 1)));
        pistaAle.trackOffset = TrackOffset.ApplyTransformOffsets;
        pistaAle.infiniteClipOffsetPosition = inicioAle;
        pistaAle.infiniteClipOffsetEulerAngles = new Vector3(0, dirZ > 0 ? 0 : 180, 0);
        var animAle = ale.GetComponent<Animator>();
        if (animAle != null) { Undo.RecordObject(animAle, "Ciudad tomas"); animAle.applyRootMotion = false; }
        dirC.SetGenericBinding(pistaVis, ale.gameObject);
        dirC.SetGenericBinding(pistaAle, animAle);
        EditorUtility.SetDirty(grupo); EditorUtility.SetDirty(pistaVis); EditorUtility.SetDirty(pistaAle); EditorUtility.SetDirty(clipAle);
        log.AppendLine($"Ale: camina hacia {(dirZ > 0 ? "+Z" : "-Z")} sobre x={lineaMemi.x:F1}; en z={AleZMundo} al empezar su toma ({VelAle * city.lossyScale.z:F1} u/s)");

        // ---------- 5. Cortes nuevos en el principal ----------
        Undo.RecordObject(pistaCortes, "Ciudad tomas");
        Corte(pistaCortes, dirP, NombrePerfil, camPerfil, InicioCiudad, DurPerfil);
        Corte(pistaCortes, dirP, NombreAle, camAle, InicioCiudad + DurPerfil, DurAle);
        EditorUtility.SetDirty(pistaCortes);
        log.AppendLine($"Cortes: {NombrePerfil} {InicioCiudad:F1}-{InicioCiudad + DurPerfil:F1} | {NombreAle} -{InicioCiudad + Recorrido:F1} | CM3_MEMO desde {InicioCiudad + Recorrido:F1}");

        // ---------- 6. Godzilla en la raíz ----------
        // Sus offsets de Timeline (pista "Godzilla · caminata") están en coordenadas de mundo. Si queda dentro de CITY (×100)
        // aparece a 14 km. Se regresa a la raíz con escala 1 (mismo tamaño que tenía: 0.01 × 100).
        var gz = T("GODZILLA");
        if (gz != null && gz.parent != null)
        {
            Undo.SetTransformParent(gz, null, "Ciudad tomas");
            Undo.RecordObject(gz, "Ciudad tomas");
            gz.localScale = Vector3.one;
            var pistaGz = tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.Contains("Godzilla") && t.name.Contains("caminata"));
            if (pistaGz != null) { gz.position = pistaGz.position; gz.rotation = pistaGz.rotation; }
            log.AppendLine("GODZILLA estaba dentro de otro objeto; se regresó a la raíz con escala 1.");
        }

        // ---------- 7. Goku fuera de cuadro ----------
        var goku = T("GOKU_PRUEBA");
        if (goku != null && goku.position.y > -1000f)
        {
            Undo.RecordObject(goku, "Ciudad tomas");
            log.AppendLine($"GOKU_PRUEBA estaba en {goku.position}; se bajó 5000 u (para regresarlo: sumar 5000 a su Y).");
            goku.position += Vector3.down * 5000f;
        }

        EditorUtility.SetDirty(tlC); EditorUtility.SetDirty(tlP); EditorUtility.SetDirty(dirC); EditorUtility.SetDirty(dirP);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dirP.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        System.IO.Directory.CreateDirectory("Logs");
        System.IO.File.WriteAllText("Logs/ciudad_tomas.txt", log.ToString());
        Debug.Log("[Ciudad tomas] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }

    // Mueve todas las curvas 'dt' s. Para el recorrido de Memi agrega la caminata previa en Z (llega a su inicio en dt).
    static void RecorrerCurvas(AnimationClip clip, float dt, bool caminataPrevia)
    {
        Undo.RecordObject(clip, "Ciudad tomas");
        foreach (var b in AnimationUtility.GetCurveBindings(clip))
        {
            var c = AnimationUtility.GetEditorCurve(clip, b);
            if (c == null || c.length == 0) continue;
            var keys = c.keys;
            for (int i = 0; i < keys.Length; i++) keys[i].time += dt;
            if (caminataPrevia && b.propertyName == "m_LocalPosition.z")
            {
                var k0 = keys[0];
                k0.inTangent = VelMemi; k0.inWeight = 1f / 3f;
                keys[0] = k0;
                var previa = new Keyframe(0f, k0.value - VelMemi * dt, VelMemi, VelMemi);
                keys = new[] { previa }.Concat(keys).ToArray();
            }
            var nueva = new AnimationCurve(keys) { preWrapMode = c.preWrapMode, postWrapMode = c.postWrapMode };
            AnimationUtility.SetEditorCurve(clip, b, nueva);
        }
        EditorUtility.SetDirty(clip);
    }

    static void SetCurva(AnimationClip clip, string prop, AnimationCurve curva)
        => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), prop), curva);

    static Vector3 Centro(Transform t)
    {
        var rs = t.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return t.position;
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        return b.center;
    }

    static CinemachineCamera CamaraFrente(string nombre, Transform objetivo, Vector3 dirCaminata, LensSettings lens, Transform[] todos)
    {
        var t = todos.FirstOrDefault(x => x != null && x.name == nombre);
        if (t == null)
        {
            var go = new GameObject(nombre);
            Undo.RegisterCreatedObjectUndo(go, nombre);
            t = go.transform;
        }
        Undo.RecordObject(t, nombre);
        t.localScale = Vector3.one;
        var offset = dirCaminata * DistFrente + Vector3.up * AlturaCam;          // delante del personaje
        var yaw = Quaternion.LookRotation(-dirCaminata, Vector3.up).eulerAngles.y; // mira hacia atrás, al personaje
        t.rotation = Quaternion.Euler(Inclinacion, yaw, 0f);
        t.position = objetivo.position + offset;
        var vcam = t.GetComponent<CinemachineCamera>();
        if (vcam == null) vcam = Undo.AddComponent<CinemachineCamera>(t.gameObject);
        Undo.RecordObject(vcam, nombre);
        vcam.Lens = lens;
        vcam.Priority.Enabled = false;
        vcam.Target.TrackingTarget = objetivo;
        vcam.Target.CustomLookAtTarget = false;
        foreach (var c in t.GetComponents<CinemachineComponentBase>().Where(c => !(c is CinemachineFollow))) Undo.DestroyObjectImmediate(c);
        var follow = t.GetComponent<CinemachineFollow>();
        if (follow == null) follow = Undo.AddComponent<CinemachineFollow>(t.gameObject);
        Undo.RecordObject(follow, nombre);
        follow.FollowOffset = offset;
        var ts = follow.TrackerSettings;
        ts.BindingMode = BindingMode.WorldSpace;
        ts.PositionDamping = Vector3.zero;
        follow.TrackerSettings = ts;
        EditorUtility.SetDirty(vcam); EditorUtility.SetDirty(follow);
        return vcam;
    }

    static void Corte(TrackAsset pista, PlayableDirector dir, string nombre, CinemachineCamera cam, double inicio, double dur)
    {
        var clip = pista.GetClips().FirstOrDefault(c => c.displayName == nombre) ?? pista.CreateClip<CinemachineShot>();
        clip.start = inicio; clip.duration = dur; clip.displayName = nombre;
        var shot = (CinemachineShot)clip.asset;
        Undo.RecordObject(shot, "Ciudad tomas");
        var er = shot.VirtualCamera;
        if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
        er.defaultValue = null;
        shot.VirtualCamera = er;
        dir.SetReferenceValue(er.exposedName, cam);
        EditorUtility.SetDirty(shot);
    }
}
