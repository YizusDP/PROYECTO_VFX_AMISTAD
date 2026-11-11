using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 1 · Bosque > Kitty reemplaza al gato (CM1)
/// Cambia el gato viejo de CM1 (cat.fbx: malla estática sin huesos) por Kitty_001 (ithappy, con huesos y animaciones).
///   - El Kitty va como HIJO del objeto 'cat': hereda el movimiento que ya hace catTimeline (quieto, avanza poquito,
///     sale disparado a los 7.75 s). No se toca ese movimiento.
///   - La malla vieja (grp1) se desactiva: no se dibuja ni cuesta nada. El objeto 'cat' se queda (su Director mueve al Kitty).
///   - Mismo tamaño (alto) y lugar que el gato viejo, mirando hacia donde miraba el viejo (-X local de 'cat').
///   - Se quitan los scripts de juego del prefab (CharacterController, CreatureMover, MovePlayerInput) y el controller.
///   - En catTimeline, pista "Kitty · animación": idle (voltea a la cámara cerca de 'Mira') -> run cuando arranca la carrera.
///     La velocidad del run se ajusta a la velocidad de la carrera del gato para que las patas no patinen.
/// Se puede volver a correr: borra y vuelve a crear al Kitty y su pista.
/// </summary>
public static class GatoKittyEditor
{
    const string KittyPrefab = "Assets/ithappy/Animals_FREE/Prefabs/Kitty_001.prefab";
    const string KittyFbx = "Assets/ithappy/Animals_FREE/Meshes/Kitty_001.fbx";
    const string GatoFbx = "Assets/Assets/3d/Persoanjes/cat.fbx";
    const string CatTimeline = "Assets/Animaciones/catTimeline.playable";
    const string NombreKitty = "GATO_KITTY";
    const string PistaNombre = "Kitty · animación";

    // Tiempos (s, iguales al principal: catTimeline arranca con la escena)
    const float Mira = 6.9f;          // la cámara se detiene frente al gato: aquí voltea la cabeza (idle, ~5.9 s del clip)
    const float LookEnIdle = 5.9f;    // momento del clip idle en que voltea
    const float Arranca = 7.75f;      // empieza la carrera en catTimeline
    const float Mezcla = 0.3f;        // blend idle -> run
    const float FinCarrera = 10.13f;  // termina el tramo rápido
    const float Fin = 17.5f;

    [MenuItem("Tools/Amistad/1 · Bosque/Kitty reemplaza al gato (CM1)", false, 101)]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Kitty] Sal de Play."); return; }
        var log = new StringBuilder();
        var fuente = AssetDatabase.LoadAssetAtPath<GameObject>(GatoFbx);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KittyPrefab);
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(CatTimeline);
        var cat = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == fuente);
        if (cat == null || prefab == null || tl == null) { Debug.LogError("[Kitty] Falta el gato viejo (cat.fbx), Kitty_001.prefab o catTimeline"); return; }
        var dir = cat.GetComponent<PlayableDirector>();
        if (dir == null || dir.playableAsset != tl) { Debug.LogError("[Kitty] El objeto 'cat' no tiene su Director con catTimeline"); return; }
        var viejo = cat.Find("grp1");
        if (viejo == null) { Debug.LogError("[Kitty] No encuentro la malla vieja grp1"); return; }

        // ---------- 1. Medidas de la malla vieja (con ella visible) ----------
        Undo.RecordObject(viejo.gameObject, "Kitty");
        viejo.gameObject.SetActive(true);
        var rv = viejo.GetComponent<Renderer>();
        var bV = rv.bounds;
        float piso = bV.min.y, alto = bV.size.y;

        // ---------- 2. Kitty (se recrea) ----------
        var previo = cat.Find(NombreKitty);
        if (previo != null) Undo.DestroyObjectImmediate(previo.gameObject);
        var k = (GameObject)PrefabUtility.InstantiatePrefab(prefab, cat.gameObject.scene);
        Undo.RegisterCreatedObjectUndo(k, "Kitty");
        PrefabUtility.UnpackPrefabInstance(k, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        k.name = NombreKitty;
        foreach (var c in k.GetComponents<MonoBehaviour>().Reverse()) Object.DestroyImmediate(c); // MovePlayerInput y luego CreatureMover (RequireComponent)
        var cc = k.GetComponent<CharacterController>(); if (cc != null) Object.DestroyImmediate(cc);
        k.transform.SetParent(cat, false);
        k.transform.localPosition = Vector3.zero;
        k.transform.localRotation = Quaternion.LookRotation(Vector3.left, Vector3.up);   // mira a -X local de 'cat' (hacia donde miraba el viejo)
        k.transform.localScale = Vector3.one;
        var rk = k.GetComponentInChildren<SkinnedMeshRenderer>();
        float altoK = rk.bounds.size.y;
        float escala = alto / altoK / cat.lossyScale.y;
        k.transform.localScale = Vector3.one * escala;
        var bK = rk.bounds;
        var mover = new Vector3(bV.center.x - bK.center.x, piso - bK.min.y, bV.center.z - bK.center.z);   // mismo centro y piso que el viejo
        k.transform.position += mover;
        var an = k.GetComponent<Animator>();
        an.runtimeAnimatorController = null; an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        rk.updateWhenOffscreen = true;
        log.AppendLine($"Kitty: escala {escala:0.0} (alto {alto:0.0}), local {k.transform.localPosition}, rot local {k.transform.localEulerAngles}");

        viejo.gameObject.SetActive(false);
        log.AppendLine("Malla vieja grp1 desactivada (el objeto 'cat' y su catTimeline siguen moviendo al Kitty).");

        // ---------- 3. Velocidades: carrera del gato vs. paso del clip run ----------
        var clips = AssetDatabase.LoadAllAssetsAtPath(KittyFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        var idle = clips.First(c => c.name.EndsWith("idle")); var run = clips.First(c => c.name.EndsWith("run"));
        var pista0 = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == "Animation Track");
        float vCarrera = 0f;
        if (pista0 != null && pista0.infiniteClip != null)
        {
            var cx = AnimationUtility.GetEditorCurve(pista0.infiniteClip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"));
            if (cx != null) vCarrera = Mathf.Abs(cx.Evaluate(FinCarrera) - cx.Evaluate(Arranca)) / (FinCarrera - Arranca) * (cat.parent ? cat.parent.lossyScale.x : 1f);
        }
        float vRun = VelocidadClip(k, run) * escala * cat.lossyScale.x;
        float velRun = vRun > 0.01f && vCarrera > 0 ? Mathf.Clamp(vCarrera / vRun, 0.6f, 2.5f) : 1f;
        log.AppendLine($"Carrera del gato ≈ {vCarrera:0.0} u/s; run del Kitty a velocidad 1 ≈ {vRun:0.0} u/s -> velocidad del clip {velRun:0.00}");

        // ---------- 4. Pista en catTimeline ----------
        Undo.RecordObject(tl, "Kitty"); Undo.RecordObject(dir, "Kitty");
        var pista = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == PistaNombre) ?? tl.CreateTrack<AnimationTrack>(null, PistaNombre);
        foreach (var c in pista.GetClips().ToList()) tl.DeleteClip(c);
        pista.trackOffset = TrackOffset.ApplySceneOffsets;
        var cI = pista.CreateClip(idle);
        cI.start = 0; cI.duration = Arranca + Mezcla; cI.displayName = "Idle (voltea a la cámara)";
        cI.clipIn = Mathf.Repeat(LookEnIdle - Mira, idle.length);      // el giro de cabeza cae en 'Mira'
        cI.blendOutDuration = Mezcla;
        var cR = pista.CreateClip(run);
        cR.start = Arranca; cR.duration = Fin - Arranca; cR.displayName = "Run";
        cR.timeScale = velRun; cR.blendInDuration = Mezcla;
        dir.SetGenericBinding(pista, an);
        log.AppendLine($"Pista '{PistaNombre}': idle 0-{Arranca + Mezcla} (clipIn {cI.clipIn:0.00}), run {Arranca}-{Fin} x{velRun:0.00}");

        EditorUtility.SetDirty(tl); EditorUtility.SetDirty(pista); EditorUtility.SetDirty(dir);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(cat.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        System.IO.Directory.CreateDirectory("Logs/gato");
        System.IO.File.WriteAllText("Logs/gato/kitty_reemplazo.txt", log.ToString());
        Debug.Log("[Kitty] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }

    /// Velocidad (en unidades del Kitty a escala 1) a la que avanzaría el cuerpo con este clip en el lugar:
    /// mediana de la velocidad hacia atrás de las patas apoyadas (cerca del piso) + avance del hueso Root.
    static float VelocidadClip(GameObject k, AnimationClip clip)
    {
        var copia = Object.Instantiate(k); copia.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            copia.transform.SetParent(null); copia.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); copia.transform.localScale = Vector3.one;
            var patas = copia.GetComponentsInChildren<Transform>(true).Where(t => t.name.StartsWith("toe.") && !t.name.EndsWith("_end") || t.name.StartsWith("front_foot.") && !t.name.EndsWith("_end")).ToList();
            var root = copia.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Root");
            int n = Mathf.Max(8, Mathf.RoundToInt(clip.length * 60));
            var pos = new List<Vector3[]>(); var rootZ = new List<float>();
            for (int i = 0; i <= n; i++)
            {
                clip.SampleAnimation(copia, clip.length * i / n);
                copia.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                pos.Add(patas.Select(p => p.position).ToArray());
                rootZ.Add(root ? root.position.z : 0f);
            }
            float dt = clip.length / n;
            float yMin = pos.SelectMany(a => a).Min(p => p.y), yMax = pos.SelectMany(a => a).Max(p => p.y);
            var vels = new List<float>();
            for (int i = 1; i <= n; i++)
                for (int j = 0; j < patas.Count; j++)
                    if (pos[i][j].y < yMin + 0.15f * (yMax - yMin)) vels.Add(-(pos[i][j].z - pos[i - 1][j].z) / dt);
            vels.Sort();
            float vPatas = vels.Count > 0 ? vels[vels.Count / 2] : 0f;
            float vRoot = (rootZ[n] - rootZ[0]) / clip.length;
            return Mathf.Max(0f, vPatas + vRoot);
        }
        finally { Object.DestroyImmediate(copia); }
    }
}
