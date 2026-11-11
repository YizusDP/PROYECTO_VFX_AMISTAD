using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// Uso único (2026-11-08): deja el Timeline principal solo con Bosque, Título, Ciudad, Cámaras y Audio
/// (los grupos "8 · Saludo · cámaras" y "9 · Título final" pasan al Timeline de la ciudad) y ordena la jerarquía en carpetas.
/// Después de usarlo se archiva en Herramientas_archivadas/.
public static class OrdenarUnaVezEditor
{
    const string TlPrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TlCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const double FinCorto = 72.9, IniTitulo = 67.5;

    public static string Ejecutar()
    {
        var log = new StringBuilder();
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TlPrincipal);
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TlCiudad);
        var dirs = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = dirs.First(d => d.playableAsset == tlP);
        var dirC = dirs.First(d => d.playableAsset == tlC);
        Undo.RecordObjects(new Object[] { tlP, tlC, dirP, dirC }, "Ordenar Timeline");

        var ctrlCiudad = tlP.GetOutputTracks().OfType<ControlTrack>().First(t => t.name == "3 · Ciudad");
        var clipCiudad = ctrlCiudad.GetClips().OrderBy(c => c.start).First();
        double off = clipCiudad.start;
        log.AppendLine($"Ciudad empieza a los {off} s del principal.");

        // ---------- 1. mover grupos 8 y 9 al Timeline de la ciudad
        TrackAsset grupoTituloC = null;
        foreach (var nombreG in new[] { "8 · Saludo · cámaras", "9 · Título final" })
        {
            var gP = tlP.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == nombreG);
            if (gP == null) { log.AppendLine($"(no hay '{nombreG}' en el principal)"); continue; }
            foreach (var viejo in tlC.GetRootTracks().OfType<GroupTrack>().Where(g => g.name == nombreG).ToList()) tlC.DeleteTrack(viejo);
            var gC = tlC.CreateTrack<GroupTrack>(null, nombreG);
            if (nombreG.StartsWith("9")) grupoTituloC = gC;
            foreach (var src in gP.GetChildTracks().ToList())
            {
                var dst = (TrackAsset)tlC.CreateTrack(src.GetType(), gC, src.name);
                dst.muted = src.muted;
                if (src is AnimationTrack sa && dst is AnimationTrack da)
                {
                    da.trackOffset = sa.trackOffset; da.position = sa.position; da.rotation = sa.rotation;
                    da.applyAvatarMask = sa.applyAvatarMask; da.avatarMask = sa.avatarMask;
                }
                if (src is ActivationTrack sAct && dst is ActivationTrack dAct)
                    dAct.postPlaybackState = nombreG.StartsWith("9") ? ActivationTrack.PostPlaybackState.Inactive : sAct.postPlaybackState;
                foreach (var c in src.GetClips())
                {
                    TimelineClip n;
                    if (src is AnimationTrack) n = ((AnimationTrack)dst).CreateClip(((AnimationPlayableAsset)c.asset).clip);
                    else n = dst.CreateDefaultClip();
                    if (c.asset is AnimationPlayableAsset && n.asset is AnimationPlayableAsset) EditorUtility.CopySerialized(c.asset, n.asset);
                    n.start = c.start - off; n.duration = c.duration; n.clipIn = c.clipIn; n.timeScale = c.timeScale;
                    n.displayName = c.displayName; n.easeInDuration = c.easeInDuration; n.easeOutDuration = c.easeOutDuration;
                    foreach (var prop in new[] { "preExtrapolationMode", "postExtrapolationMode" })
                    {
                        var pi = typeof(TimelineClip).GetProperty(prop, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        var set = pi?.GetSetMethod(true);
                        if (set != null) set.Invoke(n, new[] { pi.GetValue(c) });
                    }
                    if (src is ActivationTrack && nombreG.StartsWith("8"))   // SALUDO_EPICO: desde que empieza la ciudad hasta el título
                    { n.start = 0; n.duration = IniTitulo - off; }
                }
                var b = dirP.GetGenericBinding(src);
                if (b != null) dirC.SetGenericBinding(dst, b);
                dirP.ClearGenericBinding(src);
                EditorUtility.SetDirty(dst);
                log.AppendLine($"  {nombreG} / {src.name} ({src.GetType().Name}, {src.GetClips().Count()} clips, muted={src.muted}) -> ciudad, ligado a {(b ? b.name : "-")}");
            }
            foreach (var ch in gP.GetChildTracks().ToList()) tlP.DeleteTrack(ch);
            tlP.DeleteTrack(gP);
        }

        // ---------- 2. la ciudad llega al final del corto; lo demás de la ciudad se corta al empezar el título
        clipCiudad.duration = FinCorto - clipCiudad.start;
        EditorUtility.SetDirty(ctrlCiudad);
        if (grupoTituloC != null) log.AppendLine(FinalTituloEditor.RecortarCiudad(tlC, grupoTituloC, IniTitulo - off));
        if (tlC.durationMode == TimelineAsset.DurationMode.FixedLength && tlC.fixedDuration < FinCorto - off)
        { tlC.fixedDuration = FinCorto - off; log.AppendLine($"Duración del Timeline de la ciudad -> {tlC.fixedDuration:0.##} s"); }
        log.AppendLine($"Clip '3 · Ciudad': {clipCiudad.start}-{clipCiudad.end} s.");
        log.AppendLine("Principal ahora: " + string.Join(", ", tlP.GetRootTracks().Select(t => t.name)));
        EditorUtility.SetDirty(tlP); EditorUtility.SetDirty(tlC); EditorUtility.SetDirty(dirP); EditorUtility.SetDirty(dirC);

        // ---------- 3. jerarquía en carpetas (vacías, en el origen: nada se mueve en el mundo)
        var escena = dirP.gameObject.scene;
        var raices = escena.GetRootGameObjects().ToDictionary(g => g.name, g => g);
        var plan = new List<(string carpeta, string padre, string[] hijos)>
        {
            ("0 · GENERAL", null, new[] { "Main Camera", "Directional Light", "Global Volume", "AudioSource", "Director_P1_Bosque" }),
            ("1 · BOSQUE", null, new[] { "BOSQUE", "CM1_CASCADA", "CM1_GATO", "FX_Disolvencia" }),
            ("2 · TÍTULO", null, new[] { "CM2_TITULO" }),
            ("3 · CIUDAD", null, new[] { "CITY", "FX_PasoNegro" }),
            ("Ciudad · cámaras", "3 · CIUDAD", new[] { "CM3_MEMO_PERFIL", "CM3_ALE", "CM3_AVIONETA", "CM4_PrimeraPersona", "CM4_SigueGoku", "CM5_ALE_CAMINA", "CM7_ENCUENTRO" }),
            ("Ciudad · personajes", "3 · CIUDAD", new[] { "GOKU", "GODZILLA", "AVIONETA" }),
            ("Ciudad · saludo y final", "3 · CIUDAD", new[] { "SALUDO_EPICO", "TITULO_FINAL" }),
            ("Ciudad · sin uso (pistas silenciadas)", "3 · CIUDAD", new[] { "VELOCIDAD_MEMI", "VELOCIDAD_ALE" }),
        };
        var carpetas = new Dictionary<string, Transform>();
        int orden = 0;
        foreach (var (carpeta, padre, hijos) in plan)
        {
            var t = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).FirstOrDefault(x => x.name == carpeta);
            if (t == null) { var go = new GameObject(carpeta); Undo.RegisterCreatedObjectUndo(go, "Ordenar jerarquía"); t = go.transform; }
            if (padre != null) Undo.SetTransformParent(t, carpetas[padre], "Ordenar jerarquía");
            t.localPosition = Vector3.zero; t.localRotation = Quaternion.identity; t.localScale = Vector3.one;
            if (padre == null) t.SetSiblingIndex(orden++);
            carpetas[carpeta] = t;
            int i = 0;
            foreach (var h in hijos)
            {
                if (!raices.TryGetValue(h, out var go)) { log.AppendLine($"  (no encontré '{h}' en la raíz)"); continue; }
                var antes = go.transform.position;
                Undo.SetTransformParent(go.transform, t, "Ordenar jerarquía");
                go.transform.SetSiblingIndex(i++);
                if ((go.transform.position - antes).sqrMagnitude > 1e-4f) log.AppendLine($"  ¡OJO! {h} se movió en el mundo");
            }
            if (padre != null) t.SetSiblingIndex(plan.Where(p => p.padre == padre).ToList().FindIndex(p => p.carpeta == carpeta) + (padre == "3 · CIUDAD" ? 2 : 0));
        }
        var sueltos = escena.GetRootGameObjects().Where(g => !carpetas.ContainsKey(g.name)).Select(g => g.name).ToList();
        log.AppendLine("Jerarquía: " + string.Join(", ", escena.GetRootGameObjects().Select(g => g.name)) + (sueltos.Count > 0 ? "  | sueltos: " + string.Join(", ", sueltos) : ""));

        EditorSceneManager.MarkSceneDirty(escena);
        AssetDatabase.SaveAssets();
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return log.ToString();
    }
}
