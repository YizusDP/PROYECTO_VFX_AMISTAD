using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 1 · Bosque > Gato: acicalarse, susto y carrera
/// Rehace la actuación del gato (Kitty_001) en la toma CM1_GATO:
///   0 – 6.72 s   sentado acicalándose: levanta la pata derecha y se la lame, pausa a mirar y sigue lamiendo.
///   6.72 – 7.52  susto: 0.12 s después de la explosión (6.6 s) da un brinco con el lomo arqueado, orejas hacia atrás,
///                cola esponjada y la cabeza volteando hacia la explosión; cae agazapado y voltea al frente.
///   7.42 – 17.5  carrera (run de ithappy) con la velocidad del clip ajustada al avance real del gato: las patas no patinan.
/// Las animaciones nuevas se hicieron en Blender sobre el mismo rig (Blender/Gato_animaciones.blend) y se exportan a
/// Assets/Animaciones/Gato/Kitty_Gato_Acciones.fbx. Esta herramienta las copia a .anim, arma la pista del Kitty en el
/// Timeline del bosque (CM1_CASCADATimeline, que controla el Timeline principal) y ajusta el recorrido del objeto 'cat'
/// (quieto hasta 7.4 s, arranca y acelera). Apaga el Play On Awake del Director propio del gato (catTimeline): antes
/// corría con su propio reloj y no seguía al cabezal del Timeline principal) y le quita al objeto 'cat' el controller viejo.
/// Se puede volver a correr: reemplaza la pista y los .anim.
/// </summary>
public static class GatoAnimacionEditor
{
    const string FbxAcciones = "Assets/Animaciones/Gato/Kitty_Gato_Acciones.fbx";
    const string FbxKitty = "Assets/ithappy/Animals_FREE/Meshes/Kitty_001.fbx";
    const string Carpeta = "Assets/Animaciones/Gato";
    const string PistaNombre = "Gato · actuación (Kitty)";
    const string NombreKitty = "GATO_KITTY";

    // Tiempos (s del Timeline principal = CM1_CASCADATimeline)
    const float Susto = 6.72f;          // explosión a los 6.6 s; el gato reacciona 0.12 s después
    const float DurSusto = 0.8f;        // 24 cuadros a 30 fps
    const float MezclaRun = 0.1f;
    const float Fin = 17.5f;
    const float CuadroEnlace = 205f / 30f;   // cuadro de Gato_Acicalarse que es igual al primer cuadro de Gato_Susto

    // Recorrido del objeto 'cat' (curva m_LocalPosition.x de la pista "Gato"; en el mundo z = 420 + x)
    const float XQuieto = 6.8f, Arranca = 7.4f, Crucero = 7.8f, TFin = 10.5f, XFin = -232.4f;

    [MenuItem("Tools/Amistad/1 · Bosque/Gato: acicalarse, susto y carrera", false, 102)]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Gato] Sal de Play."); return; }
        var log = new StringBuilder();

        // ---------- 1. Clips de Blender -> .anim ----------
        var imp = (ModelImporter)AssetImporter.GetAtPath(FbxAcciones);
        if (imp == null) { Debug.LogError("[Gato] Falta " + FbxAcciones); return; }
        if (imp.animationType != ModelImporterAnimationType.Generic)
        { imp.animationType = ModelImporterAnimationType.Generic; imp.avatarSetup = ModelImporterAvatarSetup.NoAvatar; imp.SaveAndReimport(); }
        var original = AssetDatabase.LoadAllAssetsAtPath(FbxKitty).OfType<AnimationClip>().ToList();
        var idleOrig = original.First(c => c.name == "Kitty_001_idle");
        // Escala: Blender puede escribir el FBX en otra unidad (cm vs m). Se compara un hueso que nunca se mueve
        // (posición local del muslo = largo de su padre) con el FBX original de ithappy y se corrige la escala de importación.
        var fuente = AssetDatabase.LoadAllAssetsAtPath(FbxAcciones).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        float refMuslo = ValorHueso(idleOrig, "/thigh.R", "m_LocalPosition.y"), mioMuslo = fuente.Count > 0 ? ValorHueso(fuente[0], "/thigh.R", "m_LocalPosition.y") : 0f;
        if (refMuslo > 0f && mioMuslo > 0f && Mathf.Abs(refMuslo / mioMuslo - 1f) > 0.01f)
        {
            imp.globalScale *= refMuslo / mioMuslo; imp.SaveAndReimport();
            fuente = AssetDatabase.LoadAllAssetsAtPath(FbxAcciones).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
            log.AppendLine($"Escala de importación del FBX corregida a {imp.globalScale:0.###} (unidades de Blender)");
        }
        var run = original.First(c => c.name == "Kitty_001_run");
        var acical = CopiarClip(fuente.FirstOrDefault(c => c.name.EndsWith("Gato_Acicalarse")), idleOrig, "Gato_Acicalarse", log);
        var susto = CopiarClip(fuente.FirstOrDefault(c => c.name.EndsWith("Gato_Susto")), idleOrig, "Gato_Susto", log);
        if (acical == null || susto == null) { Debug.LogError("[Gato] El FBX no trae Gato_Acicalarse / Gato_Susto"); return; }
        // pose sentada: cadera al piso y patas traseras dobladas (ver GatoSentadoCorreccion)
        var kittyTmp = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == NombreKitty);
        if (kittyTmp != null)
        {
            log.AppendLine(GatoSentadoCorreccion.Aplicar(kittyTmp.gameObject, acical, t => 1f));
            log.AppendLine(GatoSentadoCorreccion.Aplicar(kittyTmp.gameObject, susto, t => 1f - Mathf.SmoothStep(0f, 1f, t / 0.1f)));
        }

        // ---------- 2. Objetos ----------
        var kitty = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == NombreKitty);
        var cm1 = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.name == "CM1_CASCADA");
        if (kitty == null || cm1 == null) { Debug.LogError("[Gato] Falta GATO_KITTY o el Director CM1_CASCADA"); return; }
        var tl = cm1.playableAsset as TimelineAsset;
        var an = kitty.GetComponent<Animator>();
        var cat = kitty.parent;

        // ---------- 3. Recorrido del gato (pista "Gato" sobre 'cat') ----------
        var pistaGato = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == "Gato");
        float vCrucero = (XQuieto - XFin) / ((Crucero - Arranca) * 0.5f + (TFin - Crucero));
        if (pistaGato != null && pistaGato.infiniteClip != null)
        {
            Undo.RecordObject(pistaGato.infiniteClip, "Gato");
            var b = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x");
            var k = new[]
            {
                new Keyframe(0f, XQuieto, 0, 0),
                new Keyframe(Arranca, XQuieto, 0, 0),
                new Keyframe(Crucero, XQuieto - vCrucero * (Crucero - Arranca) * 0.5f, -vCrucero, -vCrucero),
                new Keyframe(TFin, XFin, -vCrucero, -vCrucero),
                new Keyframe(11.05f, XFin - vCrucero * 0.55f, -vCrucero, -vCrucero),
            };
            for (int i = 0; i < k.Length; i++) { k[i].weightedMode = WeightedMode.None; }
            var curva = new AnimationCurve(k);
            AnimationUtility.SetEditorCurve(pistaGato.infiniteClip, b, curva);
            log.AppendLine($"Recorrido: quieto hasta {Arranca} s, acelera hasta {Crucero} s y sigue a {vCrucero:0.0} u/s hasta {TFin} s");
        }
        else log.AppendLine("AVISO: no encontré la pista 'Gato' con su clip grabado; el recorrido no se tocó.");

        // ---------- 4. Velocidad del run para que las patas no patinen ----------
        float vRun = VelocidadClip(kitty.gameObject, run) * kitty.lossyScale.x;
        float escalaRun = vRun > 0.01f ? Mathf.Clamp(vCrucero / vRun, 0.6f, 2.5f) : 1f;
        log.AppendLine($"Run del Kitty a velocidad 1 ≈ {vRun:0.0} u/s -> velocidad del clip {escalaRun:0.00}");

        // ---------- 5. Pista del Kitty en CM1_CASCADATimeline ----------
        Undo.RecordObject(tl, "Gato"); Undo.RecordObject(cm1, "Gato");
        var pista = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == PistaNombre) ?? tl.CreateTrack<AnimationTrack>(null, PistaNombre);
        foreach (var c in pista.GetClips().ToList()) tl.DeleteClip(c);
        pista.trackOffset = TrackOffset.ApplySceneOffsets;
        var cA = pista.CreateClip(acical); cA.displayName = "Sentado, se lame la pata";
        cA.start = 0; cA.clipIn = CuadroEnlace - Susto; cA.duration = Susto;
        var cS = pista.CreateClip(susto); cS.displayName = "Susto (brinco y voltea)";
        cS.start = Susto; cS.duration = DurSusto; cS.blendOutDuration = MezclaRun;
        var cR = pista.CreateClip(run); cR.displayName = "Carrera";
        cR.start = Susto + DurSusto - MezclaRun; cR.duration = Fin - cR.start; cR.timeScale = escalaRun; cR.blendInDuration = MezclaRun;
        cm1.SetGenericBinding(pista, an);
        an.runtimeAnimatorController = null; an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        log.AppendLine($"Pista '{PistaNombre}': acicalarse 0-{Susto} (clipIn {cA.clipIn:0.000}), susto {Susto}-{Susto + DurSusto}, carrera {cR.start:0.00}-{Fin} x{escalaRun:0.00}");

        // ---------- 6. Director propio del gato: ya no arranca solo ----------
        var dirGato = cat.GetComponent<PlayableDirector>();
        if (dirGato != null && dirGato.playOnAwake)
        {
            Undo.RecordObject(dirGato, "Gato"); dirGato.playOnAwake = false;
            log.AppendLine("Director 'cat' (catTimeline): Play On Awake apagado; ahora todo el gato sale del Timeline principal.");
        }

        // El Animator de 'cat' traía el controller viejo del gato (cat 1): en Play mueve al gato por su cuenta en cuanto
        // el Timeline se pausa o se salta (capturas, scrub). La pista "Gato" no lo necesita.
        var anCat = cat.GetComponent<Animator>();
        if (anCat != null && anCat.runtimeAnimatorController != null)
        {
            Undo.RecordObject(anCat, "Gato");
            log.AppendLine($"Animator de 'cat': se quitó el controller viejo '{anCat.runtimeAnimatorController.name}' (lo mueve solo el Timeline).");
            anCat.runtimeAnimatorController = null; anCat.applyRootMotion = false;
            EditorUtility.SetDirty(anCat);
        }

        EditorUtility.SetDirty(tl); EditorUtility.SetDirty(pista); if (pistaGato) EditorUtility.SetDirty(pistaGato.infiniteClip);
        EditorUtility.SetDirty(cm1); EditorUtility.SetDirty(an);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(kitty.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        System.IO.Directory.CreateDirectory("Logs/gato");
        System.IO.File.WriteAllText("Logs/gato/gato_animacion.txt", log.ToString());
        Debug.Log("[Gato] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }

    static float ValorHueso(AnimationClip c, string finRuta, string prop)
    {
        var b = AnimationUtility.GetCurveBindings(c).FirstOrDefault(x => x.path.EndsWith(finRuta) && x.propertyName == prop);
        return b.path == null ? 0f : Mathf.Abs(AnimationUtility.GetEditorCurve(c, b).Evaluate(0f));
    }

    /// Copia el clip del FBX a un .anim. Las curvas del objeto Kitty_001_rig (la rotación de ejes que agrega Blender)
    /// se reemplazan por las del idle original, para que todos los clips del gato dejen el rig igual.
    static AnimationClip CopiarClip(AnimationClip src, AnimationClip referencia, string nombre, StringBuilder log)
    {
        if (src == null) return null;
        string ruta = $"{Carpeta}/{nombre}.anim";
        var dst = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (dst == null) { dst = new AnimationClip(); AssetDatabase.CreateAsset(dst, ruta); }
        dst.ClearCurves(); dst.frameRate = src.frameRate;
        bool EsRaiz(string p) => p == "" || p == "Kitty_001_rig";
        int n = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(src))
        {
            if (EsRaiz(b.path)) continue;
            AnimationUtility.SetEditorCurve(dst, b, AnimationUtility.GetEditorCurve(src, b)); n++;
        }
        foreach (var b in AnimationUtility.GetCurveBindings(referencia))
        {
            if (!EsRaiz(b.path)) continue;
            float v = AnimationUtility.GetEditorCurve(referencia, b).Evaluate(0f);
            AnimationUtility.SetEditorCurve(dst, b, AnimationCurve.Constant(0f, src.length, v));
        }
        var s = AnimationUtility.GetAnimationClipSettings(dst); s.loopTime = false; AnimationUtility.SetAnimationClipSettings(dst, s);
        EditorUtility.SetDirty(dst);
        log.AppendLine($"{nombre}.anim: {src.length:0.00} s, {n} curvas de huesos");
        return dst;
    }

    /// Velocidad (unidades del Kitty a escala 1) a la que avanzaría el cuerpo con este clip en el lugar:
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
