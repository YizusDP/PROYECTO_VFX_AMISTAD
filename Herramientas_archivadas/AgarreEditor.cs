using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Solo: agarre (dedos que envuelven la mano, sin atravesarse)
/// En los saludos de Blender, al agarrarse los dedos de la mano derecha traen valores fuera de rango
/// (separación del medio -2.3 y del anular +2.2, pulgar -3.0): se abrían de lado y atravesaban la mano del otro.
/// Pista override "· dedos agarre" (máscara solo de los dedos derechos) en "Ale · cuerpo" y "Memi · cuerpo" con una pose
/// de agarre: dedos juntos y doblados (envuelven el dorso de la mano del otro). Desde que se juntan las manos hasta la
/// detonación. Se puede volver a correr. La Escena 8 rehace "· cuerpo": después correr esta.
/// </summary>
public static class AgarreEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string Carpeta = "Assets/Animaciones/Saludo";
    const float InicioCiudad = 17.5f;
    const float Ini = 59.2f, Fin = 63.45f, Entrada = 0.3f;   // principal

    // pose de agarre (músculos humanoides: -1 = doblado, 1 = estirado; Spread: separación lateral)
    // (dedo, 1 Stretched, 2 Stretched, 3 Stretched, Spread)
    static readonly (string dedo, float s1, float s2, float s3, float spread)[] Pose =
    {
        ("Thumb",  -0.6f, -0.3f, -0.3f, -0.2f),
        ("Index",  -0.55f, -0.65f, -0.5f, -0.15f),
        ("Middle", -0.6f, -0.7f, -0.5f, 0f),
        ("Ring",   -0.65f, -0.7f, -0.5f, 0.1f),
        ("Little", -0.7f, -0.7f, -0.5f, 0.25f),
    };

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Solo: agarre (dedos sin atravesarse)")]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Agarre] Sal de Play primero."); return; }
        var log = new StringBuilder();
        if (Aplicar(log)) { AssetDatabase.SaveAssets(); Debug.Log("[Agarre] Listo.\n" + log); }
        else Debug.LogError("[Agarre] No se pudo.\n" + log);
    }

    public static bool Aplicar(StringBuilder log)
    {
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dir = tl != null ? Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.playableAsset == tl) : null;
        if (dir == null) { log.AppendLine("Falta el Timeline de la ciudad o su director."); return false; }

        // máscara: solo los dedos de la mano derecha
        string rutaMask = Carpeta + "/Mascara_DedosDerecha.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(rutaMask);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, rutaMask); }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++) mask.SetHumanoidBodyPartActive((AvatarMaskBodyPart)i, (AvatarMaskBodyPart)i == AvatarMaskBodyPart.RightFingers);
        mask.transformCount = 0;
        EditorUtility.SetDirty(mask);

        // clip con la pose (constante)
        string rutaClip = Carpeta + "/Agarre_Dedos.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(rutaClip);
        if (clip == null) { clip = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clip, rutaClip); }
        clip.ClearCurves();
        float dur = Fin - Ini;
        void Musculo(string prop, float v) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), prop), AnimationCurve.Constant(0f, dur, v));
        foreach (var p in Pose)
        {
            Musculo($"RightHand.{p.dedo}.1 Stretched", p.s1);
            Musculo($"RightHand.{p.dedo}.2 Stretched", p.s2);
            Musculo($"RightHand.{p.dedo}.3 Stretched", p.s3);
            Musculo($"RightHand.{p.dedo}.Spread", p.spread);
        }
        EditorUtility.SetDirty(clip);

        Undo.RecordObject(tl, "Agarre");
        foreach (var nombreCuerpo in new[] { "Ale · cuerpo", "Memi · cuerpo" })
        {
            var cuerpo = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == nombreCuerpo && !(t.parent is AnimationTrack));
            if (cuerpo == null) { log.AppendLine($"No encontré '{nombreCuerpo}'."); continue; }
            string nombre = nombreCuerpo.Replace("cuerpo", "dedos agarre");
            foreach (var vieja in cuerpo.GetChildTracks().Where(c => c.name == nombre).ToList()) tl.DeleteTrack(vieja);
            var pista = tl.CreateTrack<AnimationTrack>(cuerpo, nombre);
            pista.avatarMask = mask; pista.applyAvatarMask = true;
            var tc = pista.CreateClip(clip);
            tc.start = Ini - InicioCiudad; tc.duration = dur; tc.easeInDuration = Entrada; tc.displayName = "Agarre (dedos)";
            if (tc.asset is AnimationPlayableAsset ap) { ap.removeStartOffset = false; ap.applyFootIK = false; }   // sin IK de pies (si no, recoloca los pies)
            EditorUtility.SetDirty(pista);
            log.AppendLine($"{nombre}: {Ini}-{Fin} s (entra en {Entrada} s).");
        }
        EditorUtility.SetDirty(tl);
        EditorSceneManager.MarkSceneDirty(dir.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return true;
    }
}
