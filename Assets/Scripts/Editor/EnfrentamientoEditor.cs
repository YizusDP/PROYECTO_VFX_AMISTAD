using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Solo: enfrentamiento (mirada fija, puños, viento)
/// CM8_1_FRENTE (49.0-51.5 s): antes estaban parados sin hacer nada (Memi veía a un lado, Ale no veía a los ojos).
///  - MIRADA_FIJA (script MiradaFija): se miran fijamente a los ojos (cuello y cabeza, solo en Play).
///  - Puños: override "· puños" (máscara de los dedos de las dos manos) que cierra las manos poco a poco.
///  - Acercamiento: el FOV de CM8_1_FRENTE baja de 42 a 30 durante la toma (pista "CM8_1 · acercamiento").
///  - Viento: ráfaga de polvo a ras de piso que pasa entre los dos hacia la cámara, con brasas.
/// Se puede volver a correr. La Escena 8 rehace "· cuerpo": después correr esta.
/// </summary>
public static class EnfrentamientoEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string Carpeta = "Assets/Animaciones/Saludo";
    const float InicioCiudad = 17.5f;
    const float Ini = 49.0f, Fin = 51.5f;                       // principal
    const float IniPunos = 49.15f, EntradaPunos = 0.7f;
    const float FovIni = 42f, FovFin = 30f;
    const string PistaFov = "CM8_1 · acercamiento", PistaViento = "Enfrentamiento · viento";

    // puño: (dedo, 1, 2, 3, spread)
    static readonly (string dedo, float s1, float s2, float s3, float spread)[] Puno =
    {
        ("Thumb", -0.5f, -0.6f, -0.6f, 0f), ("Index", -0.9f, -0.9f, -0.8f, 0f), ("Middle", -0.95f, -0.9f, -0.8f, 0f),
        ("Ring", -0.95f, -0.9f, -0.8f, 0f), ("Little", -0.95f, -0.9f, -0.8f, 0f),
    };

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Solo: enfrentamiento (mirada fija, puños, viento)")]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Enfrentamiento] Sal de Play primero."); return; }
        var log = new StringBuilder();
        if (Aplicar(log)) { AssetDatabase.SaveAssets(); Debug.Log("[Enfrentamiento] Listo.\n" + log); }
        else Debug.LogError("[Enfrentamiento] No se pudo.\n" + log);
    }

    public static bool Aplicar(StringBuilder log)
    {
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dirs = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirC = dirs.FirstOrDefault(d => d.playableAsset == tl);
        var dirP = dirs.FirstOrDefault(d => d.name == "Director_P1_Bosque");
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var ale = todos.FirstOrDefault(t => t.name == "ALE" && t.parent != null && t.parent.name == "ALE_RAIZ")?.GetComponent<Animator>();
        var memi = todos.FirstOrDefault(t => t.name == "MEMO_PERFIL" && t.parent != null && t.parent.name == "MEMO_PERFIL_RAIZ")?.GetComponent<Animator>();
        var saludo = todos.FirstOrDefault(t => t.name == "SALUDO_EPICO");
        if (tl == null || dirC == null || dirP == null || ale == null || memi == null || saludo == null) { log.AppendLine("Falta el Timeline de la ciudad, el principal, ALE, MEMO_PERFIL o SALUDO_EPICO."); return false; }
        Undo.RecordObject(tl, "Enfrentamiento");

        // ---------- 1. mirada fija (script en Play)
        foreach (var v in todos.Where(t => t != null && t.name == "MIRADA_FIJA").ToList()) Undo.DestroyObjectImmediate(v.gameObject);
        var mf = new GameObject("MIRADA_FIJA"); Undo.RegisterCreatedObjectUndo(mf, "Enfrentamiento");
        mf.transform.SetParent(saludo.parent, false);   // fuera de SALUDO_EPICO (que se guarda apagado)
        var mirada = mf.AddComponent<MiradaFija>();
        mirada.director = dirP; mirada.ale = ale; mirada.memi = memi;
        mirada.inicio = Ini - 0.1f; mirada.llegada = Ini + 0.4f; mirada.fin = Fin;
        log.AppendLine($"Mirada fija de {Ini} a {Fin} s (cuello {mirada.pesoCuello}, cabeza 1).");

        // ---------- 2. puños
        string rutaMask = Carpeta + "/Mascara_Dedos.mask";
        var mask = AssetDatabase.LoadAssetAtPath<AvatarMask>(rutaMask);
        if (mask == null) { mask = new AvatarMask(); AssetDatabase.CreateAsset(mask, rutaMask); }
        for (int i = 0; i < (int)AvatarMaskBodyPart.LastBodyPart; i++)
        { var p = (AvatarMaskBodyPart)i; mask.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.LeftFingers || p == AvatarMaskBodyPart.RightFingers || p == AvatarMaskBodyPart.LeftLeg || p == AvatarMaskBodyPart.RightLeg); }
        // ojo: con dos overrides encimados ("· piernas de pie" y este) Unity deja de aplicar el de piernas: por eso este
        // también lleva las piernas de pie (mismas curvas que Piernas_DePie.anim) y su máscara incluye las piernas.
        mask.transformCount = 0; EditorUtility.SetDirty(mask);
        // el clip es una COPIA de Piernas_DePie.anim (conserva sus ajustes de clip; uno creado desde cero dejaba las piernas
        // en la pose de caminata) con los dedos agregados
        string rutaClip = Carpeta + "/Enfrentamiento_Punos.anim";
        AssetDatabase.DeleteAsset(rutaClip);
        AssetDatabase.CopyAsset(Carpeta + "/Piernas_DePie.anim", rutaClip);
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(rutaClip);
        float durP = Fin - IniPunos;
        foreach (var lado in new[] { "LeftHand", "RightHand" })
            foreach (var d in Puno)
            {
                void M(string prop, float v) => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), $"{lado}.{d.dedo}.{prop}"),
                    new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(EntradaPunos, v), new Keyframe(durP, v)));   // se cierran poco a poco
                M("1 Stretched", d.s1); M("2 Stretched", d.s2); M("3 Stretched", d.s3); M("Spread", d.spread);
            }
        EditorUtility.SetDirty(clip);
        foreach (var nombreCuerpo in new[] { "Ale · cuerpo", "Memi · cuerpo" })
        {
            var cuerpo = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == nombreCuerpo && !(t.parent is AnimationTrack));
            if (cuerpo == null) { log.AppendLine($"No encontré '{nombreCuerpo}'."); continue; }
            string nombre = nombreCuerpo.Replace("cuerpo", "puños");
            foreach (var vieja in cuerpo.GetChildTracks().Where(c => c.name == nombre).ToList()) tl.DeleteTrack(vieja);
            // "· piernas de pie" se corta durante los puños (este override ya lleva las piernas): dos overrides encimados
            // en el mismo personaje no se combinan bien en Timeline (las piernas regresaban a la caminata)
            var piernas = cuerpo.GetChildTracks().FirstOrDefault(c => c.name == nombreCuerpo.Replace("cuerpo", "piernas de pie")) as AnimationTrack;
            if (piernas != null)
            {
                var cl = piernas.GetClips().OrderBy(c => c.start).ToList();
                if (cl.Count > 0)
                {
                    double a0 = cl.First().start, a1 = cl.Last().end; double eIn = cl.First().easeInDuration, eOut = cl.Last().easeOutDuration;
                    var asset = ((AnimationPlayableAsset)cl.First().asset).clip; string dn = cl.First().displayName;
                    foreach (var c in cl) tl.DeleteClip(c);
                    double corte0 = IniPunos - InicioCiudad, corte1 = Fin - InicioCiudad;
                    var ca1 = piernas.CreateClip(asset); ca1.start = a0; ca1.duration = corte0 - a0; ca1.easeInDuration = eIn; ca1.displayName = dn;
                    var ca2 = piernas.CreateClip(asset); ca2.start = corte1; ca2.duration = a1 - corte1; ca2.easeOutDuration = eOut; ca2.displayName = dn;
                    foreach (var c in new[] { ca1, ca2 }) if (c.asset is AnimationPlayableAsset apx) { apx.removeStartOffset = false; apx.applyFootIK = false; }
                    EditorUtility.SetDirty(piernas);
                }
            }
            var pista = tl.CreateTrack<AnimationTrack>(cuerpo, nombre);
            pista.avatarMask = mask; pista.applyAvatarMask = true;
            var tc = pista.CreateClip(clip); tc.start = IniPunos - InicioCiudad; tc.duration = durP; tc.easeInDuration = 0; tc.displayName = "Puños (y piernas de pie)";
            if (tc.asset is AnimationPlayableAsset ap) { ap.removeStartOffset = false; ap.applyFootIK = false; }   // sin IK de pies (si no, recoloca los pies)
            EditorUtility.SetDirty(pista);
        }
        log.AppendLine($"Puños de {IniPunos} a {Fin} s (se cierran en {EntradaPunos} s).");

        // ---------- 3. acercamiento (FOV)
        var cam = todos.FirstOrDefault(t => t.name == "CM8_1_FRENTE");
        foreach (var x in tl.GetOutputTracks().Where(x => x.name == PistaFov || x.name == PistaViento).ToList()) tl.DeleteTrack(x);
        var grupo = tl.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "8 · Saludo");
        if (cam != null)
        {
            var an = cam.GetComponent<Animator>(); if (an == null) an = Undo.AddComponent<Animator>(cam.gameObject);
            string rutaFov = Carpeta + "/CM8_1_Acercamiento.anim";
            var cf = AssetDatabase.LoadAssetAtPath<AnimationClip>(rutaFov);
            if (cf == null) { cf = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(cf, rutaFov); }
            cf.ClearCurves();
            AnimationUtility.SetEditorCurve(cf, EditorCurveBinding.FloatCurve("", typeof(CinemachineCamera), "Lens.FieldOfView"), AnimationCurve.EaseInOut(0f, FovIni, Fin - Ini, FovFin));
            EditorUtility.SetDirty(cf);
            var pf = tl.CreateTrack<AnimationTrack>(grupo, PistaFov);
            var tcf = pf.CreateClip(cf); tcf.start = Ini - InicioCiudad; tcf.duration = Fin - Ini; tcf.displayName = "Acercamiento";
            if (tcf.asset is AnimationPlayableAsset apf) apf.removeStartOffset = false;
            pf.trackOffset = TrackOffset.ApplySceneOffsets;
            dirC.SetGenericBinding(pf, an);
            EditorUtility.SetDirty(pf);
            log.AppendLine($"CM8_1_FRENTE: FOV {FovIni} -> {FovFin} durante la toma.");
        }

        // ---------- 4. viento con polvo y brasas entre los dos
        foreach (var v in todos.Where(t => t != null && t.name == "VIENTO_ENFRENTAMIENTO").ToList()) Undo.DestroyObjectImmediate(v.gameObject);
        // en edición los personajes y la cámara no están en su pose de esa toma: se usa el punto del agarre (FINAL_NUCLEAR,
        // justo entre los dos) y la dirección medida en Play (CM8_1_FRENTE está en x ≈ 10-16 mirando hacia -X)
        var nuc = todos.FirstOrDefault(t => t.name == "FINAL_NUCLEAR");
        var medio = nuc != null ? nuc.position : new Vector3(-18.8f, 0f, -251f); medio.y = 0f;
        var haciaCam = Vector3.right;
        var viento = new GameObject("VIENTO_ENFRENTAMIENTO"); Undo.RegisterCreatedObjectUndo(viento, "Enfrentamiento");
        viento.transform.SetParent(saludo, false);
        viento.transform.position = medio - haciaCam * 45f;          // detrás de ellos; avanza hacia la cámara
        viento.transform.rotation = Quaternion.LookRotation(haciaCam, Vector3.up);
        var sistemas = Viento(viento.transform);
        var pv = tl.CreateTrack<ControlTrack>(grupo, PistaViento);
        foreach (var go in sistemas)
        {
            var tc = pv.CreateDefaultClip(); tc.start = Ini - 0.6f - InicioCiudad; tc.duration = Fin - Ini + 0.6f; tc.displayName = go.name;
            var ca = (ControlPlayableAsset)tc.asset;
            var er = ca.sourceGameObject; if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
            er.defaultValue = null; ca.sourceGameObject = er;
            ca.updateParticle = true; ca.particleRandomSeed = (uint)(go.name.GetHashCode() & 0x7fffffff); ca.active = true;
            ca.updateDirector = false; ca.updateITimeControl = false; ca.searchHierarchy = false;
            dirC.SetReferenceValue(er.exposedName, go);
            go.SetActive(false);
            EditorUtility.SetDirty(ca);
        }
        EditorUtility.SetDirty(pv);
        log.AppendLine($"Viento: polvo a ras de piso y brasas que pasan entre los dos hacia la cámara ({Ini - 0.6f}-{Fin} s).");

        EditorUtility.SetDirty(tl);
        EditorSceneManager.MarkSceneDirty(dirC.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return true;
    }

    static ParticleSystem Sistema(Transform padre, string nombre, Material mat)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World; m.maxParticles = 2000;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; r.sortMode = ParticleSystemSortMode.Distance;
        if (mat != null && mat.shader != null && mat.shader.name.Contains("FuegoVFX"))
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
              ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent });
        return ps;
    }

    static Gradient Grad(params (float t, Color c)[] k)
    {
        var g = new Gradient();
        g.SetKeys(k.Select(x => new GradientColorKey(new Color(x.c.r, x.c.g, x.c.b), x.t)).ToArray(), k.Select(x => new GradientAlphaKey(x.c.a, x.t)).ToArray());
        return g;
    }

    static GameObject[] Viento(Transform raiz)
    {
        var ceniza = AssetDatabase.LoadAssetAtPath<Material>("Assets/Assets/shaders/FuegoVFX/Fuego_Ceniza.mat");
        var brasas = AssetDatabase.LoadAssetAtPath<Material>("Assets/Assets/shaders/FuegoVFX/Fuego_Brasas.mat");
        var lista = new List<GameObject>();
        {
            var ps = Sistema(raiz, "POLVO", ceniza);
            var m = ps.main; m.duration = 2.6f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(2.5f, 6f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 20f), new Keyframe(0.3f, 70f), new Keyframe(1f, 35f)));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(9f, 1.5f, 6f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-2f, 2f); vel.y = new ParticleSystem.MinMaxCurve(0.3f, 1.8f); vel.z = new ParticleSystem.MinMaxCurve(18f, 28f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 2.2f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(0.55f, 0.42f, 0.33f, 0f)), (0.2f, new Color(0.5f, 0.4f, 0.32f, 0.45f)), (1f, new Color(0.45f, 0.37f, 0.3f, 0f)));
            ps.transform.localPosition = Vector3.up * 0.8f; lista.Add(ps.gameObject);
        }
        if (brasas != null)
        {
            var ps = Sistema(raiz, "BRASAS", brasas);
            var m = ps.main; m.duration = 2.6f; m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f); m.startColor = new Color(1f, 0.55f, 0.2f, 1f);
            var em = ps.emission; em.rateOverTime = 40f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(14f, 10f, 8f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-3f, 3f); vel.y = new ParticleSystem.MinMaxCurve(0.5f, 3f); vel.z = new ParticleSystem.MinMaxCurve(16f, 26f);
            var noise = ps.noise; noise.enabled = true; noise.strength = 1.5f; noise.frequency = 0.6f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 0.8f, 0.4f, 0f)), (0.15f, new Color(1, 0.6f, 0.2f, 1f)), (1f, new Color(0.8f, 0.2f, 0.05f, 0f)));
            ps.transform.localPosition = Vector3.up * 6f; lista.Add(ps.gameObject);
        }
        return lista.ToArray();
    }
}
