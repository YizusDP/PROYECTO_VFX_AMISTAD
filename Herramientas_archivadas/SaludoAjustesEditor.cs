using System.IO;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Solo: media cara de Ale + tomas de las manos
/// Ajustes de la Escena 8 hechos a mano después de ver el corte (la Escena 8 la corre sola al final):
///  - Media cara de Ale (CM8_2, 51.5-53.5 s): el shader toon de los personajes es Unlit y solo usa la dirección de la luz
///    principal; en el saludo esa luz le llega a Ale por la espalda y su cara quedaba negra (las luces de cara ya no le afectan).
///    Durante esa toma la luz principal gira de lado (una mitad de la cara iluminada, la otra en sombra) y en el corte regresa.
///    Pista "Luz principal · media cara Ale" en el grupo "8 · Saludo" del Timeline de la ciudad.
///  - Encuadre final (CM8_9, 62-65.25 s): lente más cerrado para que no se vean las piernas.
///  - Tomas de las manos (CM8_5 mano de Ale 56.97-57.83, CM8_6 mano de Memi 57.83-58.70): la cámara ve la palma de frente
///    (se calcula hacia dónde mira la palma en ese segundo del saludo), esquiva el cuerpo del otro y sigue a la mano.
///    Se silencia su pista de movimiento del previz ("· mov"); el fondo ya no es el piso con los pies.
/// Se puede volver a correr.
/// </summary>
public static class SaludoAjustesEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string RutaClipLuz = "Assets/Animaciones/Saludo/Saludo_LuzMediaCara.anim";
    const float InicioCiudad = 17.5f;
    const float IniToma = 51.5f, FinToma = 53.5f;       // CM8_2_CARA_ALE (principal)
    // Ale mira hacia +Z (hacia Memi) en el saludo. Con este shader la cara se ilumina donde la normal apunta hacia el "forward"
    // de la luz: luz casi lateral (+X) y un poco hacia atrás -> la mitad +X de la cara iluminada, la otra en sombra.
    static readonly Vector3 DireccionLuzMediaCara = new Vector3(1f, -0.12f, -0.22f);
    // palma de Ale (CM8_5): misma idea; la luz apunta (casi horizontal) en la dirección en que mira la palma,
    // que es hacia la cámara de palma. Solo durante esa toma; en el corte regresa.
    const float IniPalmaAle = 56.97f, FinPalmaAle = 57.83f;
    const float InclinacionLuzPalma = -0.12f;
    const float FovFinal = 24f;                          // CM8_9_FINAL (el previz traía 35.1)
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const float InicioSaludo = 49f;                      // principal: segundo 0 del clip del saludo
    // cámara de palma: (cámara, personaje, pista de recorrido, segundo del principal a medir, distancia, altura, FOV)
    static readonly (string cam, string pj, string pista, string otro, string pistaOtro, float t, float dist, float alto, float fov)[] TomasPalma =
    {
        ("CM8_5_PICADA_MANO_ALE",        "ALE",         "Ale", "MEMO_PERFIL", "MC",  57.40f, 7.5f,  1.5f, 30f),
        ("CM8_6_CONTRAPICADA_MANO_MEMI", "MEMO_PERFIL", "MC",  "ALE",         "Ale", 58.25f, 7.5f, -0.5f, 30f),
    };

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Solo: media cara de Ale + tomas de las manos", false, 313)]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Saludo ajustes] Sal de Play antes de correr la herramienta."); return; }
        var log = new StringBuilder();
        if (Aplicar(log))
        {
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/saludo_ajustes.txt", log.ToString());
            Debug.Log("[Saludo ajustes] Listo. Guarda la escena con Ctrl+S.\n" + log);
        }
        else Debug.LogError("[Saludo ajustes] No se pudo.\n" + log);
    }

    public static bool Aplicar(StringBuilder log)
    {
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dir = tl != null ? Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.playableAsset == tl) : null;
        if (dir == null) { log.AppendLine("No encuentro el Timeline de la ciudad o su director en la escena."); return false; }
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);

        // ---------- luz de media cara
        var sol = Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(l => l.type == LightType.Directional).OrderByDescending(l => l == RenderSettings.sun).ThenByDescending(l => l.intensity).FirstOrDefault();
        if (sol == null) { log.AppendLine("No hay luz direccional en la escena."); return false; }
        var t = sol.transform;
        var anim = t.GetComponent<Animator>();
        if (anim == null) anim = Undo.AddComponent<Animator>(t.gameObject);
        anim.runtimeAnimatorController = null; anim.applyRootMotion = false; anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        float a = IniToma - InicioCiudad, b = FinToma - InicioCiudad, fin = 60f;
        var rotOrig = t.localRotation;
        var rotNueva = Quaternion.LookRotation(DireccionLuzMediaCara.normalized, Vector3.up);
        if (t.parent != null) rotNueva = Quaternion.Inverse(t.parent.rotation) * rotNueva;
        // dirección de la palma de Ale = hacia donde está su cámara de palma (ya armada por CamaraPalma)
        var segPalma = todos.FirstOrDefault(x => x.name == "CM8_5_PICADA_MANO_ALE")?.GetComponent<CinemachineFollow>();
        Quaternion? rotPalma = null;
        if (segPalma != null && segPalma.FollowOffset.sqrMagnitude > 0.01f)
        {
            var dp = segPalma.FollowOffset; dp.y = 0; dp = dp.normalized; dp.y = InclinacionLuzPalma;
            var rp = Quaternion.LookRotation(dp.normalized, Vector3.up);
            if (t.parent != null) rp = Quaternion.Inverse(t.parent.rotation) * rp;
            rotPalma = rp;
        }
        float pa = IniPalmaAle - IniToma, pb = FinPalmaAle - IniToma;
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RutaClipLuz);
        if (clip == null) { clip = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clip, RutaClipLuz); }
        clip.ClearCurves();
        string[] comp = { "x", "y", "z", "w" };
        for (int i = 0; i < 4; i++)
        {
            var cu = rotPalma.HasValue
                ? new AnimationCurve(new Keyframe(0f, rotNueva[i]), new Keyframe(b - a, rotOrig[i]), new Keyframe(pa, rotPalma.Value[i]), new Keyframe(pb, rotOrig[i]), new Keyframe(fin - a, rotOrig[i]))
                : new AnimationCurve(new Keyframe(0f, rotNueva[i]), new Keyframe(b - a, rotOrig[i]), new Keyframe(fin - a, rotOrig[i]));
            for (int k = 0; k < cu.length; k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(cu, k, AnimationUtility.TangentMode.Constant);
                AnimationUtility.SetKeyRightTangentMode(cu, k, AnimationUtility.TangentMode.Constant);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation." + comp[i]), cu);
        }
        EditorUtility.SetDirty(clip);

        Undo.RecordObject(tl, "Saludo ajustes");
        var grupo = tl.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "8 · Saludo") ?? tl.CreateTrack<GroupTrack>(null, "8 · Saludo");
        string nombre = "Luz principal · media cara Ale";
        var pista = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(p => p.name == nombre) ?? tl.CreateTrack<AnimationTrack>(grupo, nombre);
        foreach (var c in pista.GetClips().ToList()) tl.DeleteClip(c);
        // la rotación del clip es absoluta y la posición se queda donde está la luz
        pista.trackOffset = TrackOffset.ApplyTransformOffsets;
        pista.position = t.localPosition; pista.eulerAngles = Vector3.zero;
        var tc = pista.CreateClip(clip);
        tc.start = a; tc.duration = fin - a; tc.displayName = "Media cara Ale (CM8_2)";
        if (tc.asset is AnimationPlayableAsset apa) { apa.removeStartOffset = false; apa.position = Vector3.zero; apa.rotation = Quaternion.identity; EditorUtility.SetDirty(apa); }
        dir.SetGenericBinding(pista, anim);
        EditorUtility.SetDirty(pista);
        log.AppendLine($"{sol.name}: de {IniToma} a {FinToma} s gira a {rotNueva.eulerAngles} (media cara de Ale) y en el corte regresa a {rotOrig.eulerAngles}.");
        if (rotPalma.HasValue) log.AppendLine($"{sol.name}: de {IniPalmaAle} a {FinPalmaAle} s gira a {rotPalma.Value.eulerAngles} (palma de Ale en CM8_5).");

        // ---------- encuadre final más cerrado
        var cam = todos.FirstOrDefault(x => x.name == "CM8_9_FINAL")?.GetComponent<CinemachineCamera>();
        if (cam != null)
        {
            Undo.RecordObject(cam, "Saludo ajustes");
            var lens = cam.Lens; float antes = lens.FieldOfView; lens.FieldOfView = FovFinal; cam.Lens = lens;
            EditorUtility.SetDirty(cam);
            log.AppendLine($"CM8_9_FINAL: FOV {antes:0.0} -> {FovFinal:0.0} (no se ven las piernas).");
        }
        else log.AppendLine("No encuentro CM8_9_FINAL (corre antes la Escena 8).");

        // ---------- tomas de las palmas
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var city = todos.FirstOrDefault(x => x.name == "CITY");
        foreach (var tp in TomasPalma) CamaraPalma(tp, tl, tlP, city, todos, log);
        if (tlP != null) EditorUtility.SetDirty(tlP);

        EditorUtility.SetDirty(tl);
        EditorSceneManager.MarkSceneDirty(dir.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return true;
    }

    static void CamaraPalma((string cam, string pj, string pista, string otro, string pistaOtro, float t, float dist, float alto, float fov) tp,
                            TimelineAsset tlC, TimelineAsset tlP, Transform city, Transform[] todos, StringBuilder log)
    {
        var camT = todos.FirstOrDefault(x => x.name == tp.cam);
        var vcam = camT != null ? camT.GetComponent<CinemachineCamera>() : null;
        var pj = todos.FirstOrDefault(x => x.name == tp.pj);
        var otro = todos.FirstOrDefault(x => x.name == tp.otro);
        var anim = pj != null ? pj.GetComponent<Animator>() : null;
        var ctrl = AssetDatabase.LoadAssetAtPath<UnityEditor.Animations.AnimatorController>(tp.pj == "ALE" ? CuerpoTimelineEditor.ControllerAle : CuerpoTimelineEditor.ControllerMemi);
        var saludo = ctrl != null ? ctrl.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == "Saludo")?.motion as AnimationClip : null;
        AnimationTrack Rec(string pref) => tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(x => !(x.parent is AnimationTrack) && x.name.StartsWith(pref) && x.name.Contains("recorrido"));
        var rec = Rec(tp.pista); var recO = Rec(tp.pistaOtro);
        var mano = anim != null ? anim.GetBoneTransform(HumanBodyBones.RightHand) : null;
        if (vcam == null || anim == null || saludo == null || rec?.infiniteClip == null || recO?.infiniteClip == null || city == null || mano == null || otro == null)
        { log.AppendLine($"{tp.cam}: falta algo (cámara, personaje, saludo o recorrido); no se cambió."); return; }

        // dónde está cada personaje en ese segundo (pista de recorrido) y hacia dónde mira
        float local = tp.t - InicioCiudad;
        Vector3 Pos(AnimationTrack r)
        {
            float E(string p) { var cu = AnimationUtility.GetEditorCurve(r.infiniteClip, EditorCurveBinding.FloatCurve("", typeof(Transform), p)); return cu != null ? cu.Evaluate(local) : 0f; }
            return city.TransformPoint(r.infiniteClipOffsetPosition + Quaternion.Euler(r.infiniteClipOffsetEulerAngles) * new Vector3(E("m_LocalPosition.x"), E("m_LocalPosition.y"), E("m_LocalPosition.z")));
        }
        var posPj = Pos(rec); var posOtro = Pos(recO);
        var rotPj = city.rotation * Quaternion.Euler(rec.infiniteClipOffsetEulerAngles);

        // palma de la mano derecha alrededor de ese segundo del saludo (relativa al personaje; promedio de 3 muestras)
        Vector3 relPalma = Vector3.zero, relNormal = Vector3.zero, palmaEnMano = Vector3.zero;
        bool activo = pj.gameObject.activeSelf; bool ya = AnimationMode.InAnimationMode();
        try
        {
            pj.gameObject.SetActive(true);
            if (!ya) AnimationMode.StartAnimationMode();
            Transform B(HumanBodyBones b, HumanBodyBones alt) => anim.GetBoneTransform(b) ?? anim.GetBoneTransform(alt);
            var idx = B(HumanBodyBones.RightIndexProximal, HumanBodyBones.RightMiddleProximal);
            var med = B(HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightIndexProximal);
            var men = B(HumanBodyBones.RightLittleProximal, HumanBodyBones.RightRingProximal);
            var inv = Quaternion.Inverse(pj.rotation);
            foreach (float dt in new[] { -0.25f, 0f, 0.25f })
            {
                AnimationMode.BeginSampling(); AnimationMode.SampleAnimationClip(pj.gameObject, saludo, tp.t + dt - InicioSaludo); AnimationMode.EndSampling();
                var h = mano.position;
                var centro = med != null ? Vector3.Lerp(h, med.position, 0.6f) : h;
                var n = idx != null && men != null ? Vector3.Cross(men.position - h, idx.position - h).normalized : -pj.up;
                relPalma += inv * (centro - pj.position) / 3f; relNormal += inv * n;
                if (dt == 0f) palmaEnMano = mano.InverseTransformPoint(centro);
            }
        }
        finally { if (!ya) AnimationMode.StopAnimationMode(); pj.gameObject.SetActive(activo); }
        var manoMundo = posPj + rotPj * relPalma;
        var normal = (rotPj * relNormal).normalized;
        normal.y = Mathf.Clamp(normal.y, -0.5f, 0.5f); normal.Normalize();     // ni desde arriba ni desde abajo del todo

        // punto guía en el centro de la palma (hijo de la mano): ahí mira y lo sigue la cámara
        var guia = mano.Find("PALMA_GUIA");
        if (guia == null) { var g = new GameObject("PALMA_GUIA"); Undo.RegisterCreatedObjectUndo(g, "Saludo ajustes"); guia = g.transform; guia.SetParent(mano, false); }
        Undo.RecordObject(guia, "Saludo ajustes");
        guia.localPosition = palmaEnMano; guia.localRotation = Quaternion.identity;

        // de frente a la palma; si cae dentro de un cuerpo, se gira alrededor de la mano
        float Libre(Vector3 c) => Mathf.Min(Dist2D(c, posOtro), Dist2D(c, posPj));
        Vector3 mejor = Vector3.zero; float mejorLibre = -1f; float giroUsado = 0f;
        foreach (float g in new[] { 0f, 35f, -35f, 60f, -60f, 90f, -90f })
        {
            var c = manoMundo + Quaternion.Euler(0, g, 0) * normal * tp.dist + Vector3.up * tp.alto;
            float lib = Libre(c);
            if (lib > 3.2f) { mejor = c; giroUsado = g; mejorLibre = lib; break; }
            if (lib > mejorLibre) { mejor = c; giroUsado = g; mejorLibre = lib; }
        }

        Undo.RecordObject(vcam, "Saludo ajustes");
        foreach (var c in camT.GetComponents<CinemachineComponentBase>().Where(c => !(c is CinemachineFollow) && !(c is CinemachineHardLookAt)).ToList()) Undo.DestroyObjectImmediate(c);
        var follow = camT.GetComponent<CinemachineFollow>(); if (follow == null) follow = Undo.AddComponent<CinemachineFollow>(camT.gameObject);
        var look = camT.GetComponent<CinemachineHardLookAt>(); if (look == null) look = Undo.AddComponent<CinemachineHardLookAt>(camT.gameObject);
        Undo.RecordObject(follow, "Saludo ajustes");
        follow.FollowOffset = mejor - manoMundo;
        var ts = follow.TrackerSettings; ts.BindingMode = Unity.Cinemachine.TargetTracking.BindingMode.WorldSpace; ts.PositionDamping = Vector3.zero; follow.TrackerSettings = ts;
        vcam.Target.TrackingTarget = guia; vcam.Target.LookAtTarget = guia;
        var lens = vcam.Lens; lens.FieldOfView = tp.fov; vcam.Lens = lens;
        camT.position = mejor; camT.LookAt(manoMundo);
        EditorUtility.SetDirty(vcam); EditorUtility.SetDirty(follow); EditorUtility.SetDirty(look);

        // el movimiento del previz ya no manda en esta cámara
        foreach (var tlx in new[] { tlC, tlP })
            if (tlx != null) foreach (var mov in tlx.GetOutputTracks().Where(x => x.name == tp.cam + " \u00B7 mov"))
                if (!mov.muted) { mov.muted = true; EditorUtility.SetDirty(mov); }

        log.AppendLine($"{tp.cam}: cámara de palma ({tp.pj}, centro de la palma derecha, normal {normal}), giro {giroUsado:0}°, a {mejorLibre:0.0} u del cuerpo más cercano, FOV {tp.fov:0}; sigue a la mano. Pista '· mov' silenciada.");
    }

    static float Dist2D(Vector3 a, Vector3 b) { a.y = 0; b.y = 0; return Vector3.Distance(a, b); }
}
