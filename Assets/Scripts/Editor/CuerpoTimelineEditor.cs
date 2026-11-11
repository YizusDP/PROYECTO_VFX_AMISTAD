using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Solo: cuerpo de Ale y Memi en el Timeline (preview)
/// Antes la caminata y el saludo salían del Animator Controller (VelocidadAnimator + SaludoControl), que solo corre en Play:
/// en el preview del Timeline los personajes se deslizaban por el recorrido en pose base (brazos abiertos).
/// Esta herramienta pasa ese movimiento a pistas del Timeline de la ciudad, así se ve igual en el preview y en Play
/// y se puede revisar cuadro por cuadro:
///  - Armado padre/hijo: crea ALE_RAIZ / MEMO_PERFIL_RAIZ (vacío con Animator genérico) y mete al personaje adentro.
///    La pista "· recorrido" ahora mueve al padre (mismas curvas, mismo lugar); el personaje queda en (0,0,0) del padre.
///  - "<nombre> · cuerpo" (en el mismo grupo que el recorrido, ligada al Animator del personaje): la caminata del controller
///    en tramos según Velocidad_<nombre>.anim y el saludo según Saludo_<NOMBRE>_tiempo.anim, con la misma mezcla (fundido).
///  - Silencia (mute) las pistas viejas "<nombre> · velocidad" y "<nombre> · saludo" y quita el Animator Controller del personaje
///    (en Play el controller se evalúa después del Timeline y le ganaba). Los assets ALE.controller / MEMI.controller se quedan:
///    de ahí se leen la caminata y el saludo.
/// Se puede volver a correr. La Escena 8 la llama sola al final (si cambias el encuentro o el saludo, corre la 8 y listo).
/// Corre con el Preview del Timeline apagado la primera vez (cuando todavía no existe el padre).
/// </summary>
public static class CuerpoTimelineEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string CarpetaSaludo = "Assets/Animaciones/Saludo";
    public const string ControllerAle = "Assets/Animaciones/ALE.controller";
    public const string ControllerMemi = "Assets/Animaciones/MEMI.controller";
    const float Fin = 60f;            // las pistas viejas duraban 60 s (local de la ciudad)
    const float Paso = 0.1f;          // resolución de los tramos cuando cambia la velocidad
    // Piernas de pie: la animación de Blender trae a Ale con las piernas muy abiertas y a Memi con las rodillas juntas
    // mientras están parados frente a frente. Hasta el impulso, las piernas toman una postura de pie normal
    // (torso, brazos y cabeza siguen siendo el saludo) y en el impulso pasan a la animación original.
    // Ale: igual que Memi, su saludo trae las rodillas del lado contrario (izq -71°, der +67°) hasta el segundo ~7.0 (der)
    // y ~7.25 (izq). Con la mezcla vieja (6.6 s, 0.7 s) la rodilla izquierda giraba ~127° y regresaba de golpe a los 56.27 s.
    // (antes: 6.6 / 0.7)
    const float ImpulsoSaludo = 7.3f;    // segundo del saludo en que las piernas pasan a la animación original
    const float FundidoPiernas = 0.5f;   // mezcla de las piernas de pie a las del saludo
    // Memi: en su saludo de Blender las rodillas apuntan hacia atrás (-164° respecto al pie) hasta el segundo ~7.25 y ahí
    // voltean de golpe. Mezclar antes hacía que la rodilla izquierda girara ~190° en la contrapicada (55.6-56.3 s del principal).
    // Sus piernas de pie duran hasta que su saludo ya trae las rodillas bien, y la mezcla es corta (solo ~60° de giro).
    const float ImpulsoSaludoMemi = 7.35f, FundidoPiernasMemi = 0.5f;
    const string RutaPiernas = CarpetaSaludo + "/Piernas_DePie.anim";
    const string RutaMascaraPiernas = CarpetaSaludo + "/Mascara_Piernas.mask";
    static readonly string Pt = " · ";   // " · "

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Solo: cuerpo de Ale y Memi en el Timeline (preview)", false, 312)]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Cuerpo] Sal de Play antes de correr la herramienta."); return; }
        var log = new StringBuilder();
        if (Hornear(log))
        {
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/cuerpo.txt", log.ToString());
            Debug.Log("[Cuerpo] Listo. Guarda la escena con Ctrl+S. Ya puedes mover el cursor del Timeline y ver la caminata y el saludo.\n" + log);
        }
        else Debug.LogError("[Cuerpo] No se pudo.\n" + log);
    }

    /// <summary>Hace todo el trabajo. Devuelve false si falta algo (el detalle va en el log).</summary>
    public static bool Hornear(StringBuilder log)
    {
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        if (tl == null) { log.AppendLine("No encuentro " + TimelineCiudad); return false; }
        var dir = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.playableAsset == tl);
        if (dir == null) { log.AppendLine("No hay un PlayableDirector con el Timeline de la ciudad en la escena abierta."); return false; }
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);

        bool ok = true;
        ok &= Personaje(tl, dir, T("ALE"), "Ale", "ALE", "Ale", ControllerAle, log);
        ok &= Personaje(tl, dir, T("MEMO_PERFIL"), "MC", "MEMI", "Memi", ControllerMemi, log);

        EditorUtility.SetDirty(tl);
        EditorSceneManager.MarkSceneDirty(dir.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return ok;
    }

    static bool Personaje(TimelineAsset tl, PlayableDirector dir, Transform pj, string prefijoPista, string clave, string nombre, string rutaController, StringBuilder log)
    {
        if (pj == null) { log.AppendLine($"{nombre}: no encuentro su objeto en la escena."); return false; }
        var anim = pj.GetComponent<Animator>();
        if (anim == null) { log.AppendLine($"{nombre}: no tiene Animator."); return false; }
        // el controller ya no va en el personaje (ver abajo); de él solo se leen los clips
        var ctrl = (anim.runtimeAnimatorController as AnimatorController) ?? AssetDatabase.LoadAssetAtPath<AnimatorController>(rutaController);
        if (ctrl == null) { log.AppendLine($"{nombre}: no encuentro su Animator Controller ({rutaController})."); return false; }
        var sm = ctrl.layers[0].stateMachine;
        var caminata = sm.defaultState != null ? sm.defaultState.motion as AnimationClip : null;
        var saludo = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == "Saludo")?.motion as AnimationClip;
        if (caminata == null) { log.AppendLine($"{nombre}: el estado por defecto de {ctrl.name} no tiene clip."); return false; }

        var recorrido = tl.GetOutputTracks().OfType<AnimationTrack>()
            .FirstOrDefault(t => !(t.parent is AnimationTrack) && t.name.StartsWith(prefijoPista) && t.name.Contains("recorrido"));
        if (recorrido == null) { log.AppendLine($"{nombre}: no encuentro la pista '{prefijoPista}{Pt}recorrido'."); return false; }

        // curvas que antes leían los scripts
        var vel = Curva("Assets/Animaciones/Velocidad_" + nombre + ".anim", typeof(VelocidadAnimator), "velocidad");
        var tiempo = Curva($"{CarpetaSaludo}/Saludo_{clave}_tiempo.anim", typeof(SaludoControl), "tiempo");
        var sc = Object.FindObjectsByType<SaludoControl>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(s => s.animator == anim);
        float fundido = sc != null ? sc.fundido : 0.6f;

        Undo.RecordObject(tl, "Cuerpo en Timeline");

        // ---- armado padre/hijo: el recorrido mueve al padre, el Timeline anima el cuerpo del hijo
        var raiz = Raiz(pj, log);
        if (raiz == null) return false;
        var animRaiz = raiz.GetComponent<Animator>();
        dir.SetGenericBinding(recorrido, animRaiz);

        // pistas override de la versión anterior (con máscara): ya no se usan
        foreach (var vieja in recorrido.GetChildTracks().Where(t => t.name.StartsWith(nombre + Pt + "cuerpo")).ToList()) tl.DeleteTrack(vieja);

        var pista = Pista(tl, recorrido.parent as TrackAsset, nombre + Pt + "cuerpo");
        dir.SetGenericBinding(pista, anim);

        // ---- saludo: tramos según la curva de tiempo (se calculan primero para saber cuándo termina la caminata)
        var tramosSal = new System.Collections.Generic.List<(float a, float b, float pend, float v)>();
        if (saludo != null && tiempo != null)
        {
            var k = tiempo.keys.OrderBy(x => x.time).ToArray();
            for (int i = 0; i + 1 < k.Length; i++)
            {
                float a = k[i].time, b = Mathf.Min(k[i + 1].time, Fin);
                if (k[i].value < 0f || k[i + 1].value < 0f || b - a < 0.0005f) continue;
                tramosSal.Add((a, b, (k[i + 1].value - k[i].value) / (b - a), k[i].value));
            }
        }
        float tIni = tramosSal.Count > 0 ? tramosSal[0].a : Fin;
        float finCaminata = Mathf.Min(Fin, tIni + fundido);          // se traslapa con el saludo: Timeline mezcla los dos

        // ---- caminata
        // un tramo que empiece cuando ya entró el saludo quedaría encimado por completo (Timeline no lo mezcla bien):
        // se quita y el tramo anterior se alarga hasta el final de la mezcla
        var tramosCam = Tramos(vel).Where(x => x.a < tIni - 0.0005f).ToList();
        if (tramosCam.Count > 0) { var u = tramosCam[tramosCam.Count - 1]; tramosCam[tramosCam.Count - 1] = (u.a, finCaminata, u.s); }
        double fase = 0; int nCam = 0;
        foreach (var (a0, b0, s) in tramosCam)
        {
            float a = a0, b = Mathf.Min(b0, finCaminata);
            if (b - a < 0.0005f) continue;
            var tc = pista.CreateClip(caminata);
            tc.start = a; tc.duration = b - a;
            tc.timeScale = Mathf.Max(s, 0.0001f);      // 0 -> mínimo de Timeline (1/1000): queda congelada
            tc.clipIn = fase % caminata.length;
            tc.displayName = s < 0.0015f ? "Caminata (parado)" : (Mathf.Abs(s - 1f) < 0.001f ? "Caminata" : $"Caminata x{s:0.00}");
            Ajustes(tc, AnimationPlayableAsset.LoopMode.On, pieIK: false);
            fase += tc.timeScale * tc.duration;
            nCam++;
        }
        log.AppendLine($"{nombre}: caminata '{caminata.name}' en {nCam} tramos (según {(vel != null ? "Velocidad_" + nombre : "velocidad constante 1")}).");

        // ---- saludo
        int nSal = 0;
        foreach (var (a, b, pend, v) in tramosSal)
        {
            var tc = pista.CreateClip(saludo);
            tc.start = a; tc.duration = b - a;
            bool quieto = pend < 0.0015f;
            tc.timeScale = quieto ? 0.0001f : pend;
            double inicio = v;
            if (quieto && inicio > saludo.length - 0.05f) inicio = Mathf.Max(0f, saludo.length - 0.05f);   // pose final sostenida
            tc.clipIn = inicio;
            tc.displayName = quieto ? $"Saludo (pose {v:0.0} s)" : "Saludo";
            Ajustes(tc, AnimationPlayableAsset.LoopMode.Off, pieIK: false);   // con IK de pies se agachaban (los objetivos de pie del FBX de Blender no coinciden)
            nSal++;
        }
        // ---- piernas de pie (override solo de piernas) desde que entra el saludo hasta el impulso
        foreach (var vieja in pista.GetChildTracks().ToList()) tl.DeleteTrack(vieja);
        var tSal = tramosSal.FirstOrDefault(x => x.pend >= 0.0015f);
        if (nSal > 0 && tSal.b > tSal.a)
        {
            bool esMemi = nombre.StartsWith("Memi");
            float impulso = esMemi ? ImpulsoSaludoMemi : ImpulsoSaludo, fundidoP = esMemi ? FundidoPiernasMemi : FundidoPiernas;
            float fin = Mathf.Min(Fin, tSal.a + (impulso - tSal.v) + fundidoP);
            var pPier = tl.CreateTrack<AnimationTrack>(pista, nombre + Pt + "piernas de pie");
            pPier.avatarMask = MascaraPiernas();
            pPier.applyAvatarMask = true;
            var tc = pPier.CreateClip(PiernasDePie());
            tc.start = tIni; tc.duration = fin - tIni;
            tc.displayName = "Piernas de pie";
            Ajustes(tc, AnimationPlayableAsset.LoopMode.On, pieIK: false);
            tc.easeInDuration = fundido;
            tc.easeOutDuration = fundidoP;
            EditorUtility.SetDirty(pPier);
            log.AppendLine($"{nombre}: piernas de pie de {tIni + 17.5:0.00} a {fin + 17.5:0.00} s del principal (luego las del saludo para el impulso).");
        }

        if (nSal > 0) log.AppendLine($"{nombre}: saludo '{saludo.name}' en {nSal} tramos, entra a los {tIni + 17.5:0.00} s del principal con mezcla de {fundido:0.0} s.");
        else log.AppendLine($"{nombre}: sin saludo ({(saludo == null ? "no hay estado 'Saludo' en " + ctrl.name : "falta Saludo_" + clave + "_tiempo.anim")}).");

        // ---- silenciar el método viejo
        foreach (var vieja in tl.GetOutputTracks().Where(t => t.name == nombre + Pt + "velocidad" || t.name == nombre + Pt + "saludo"))
        {
            if (!vieja.muted) { vieja.muted = true; EditorUtility.SetDirty(vieja); }
            log.AppendLine($"{nombre}: pista vieja '{vieja.name}' silenciada.");
        }

        // ---- quitar el controller del personaje: en Play el Animator Controller se evalúa después del Timeline y le gana
        //      en todo el cuerpo (se veía caminando siempre). El Timeline ya hace todo; el asset del controller no se toca.
        if (anim.runtimeAnimatorController != null)
        {
            Undo.RecordObject(anim, "Cuerpo en Timeline");
            anim.runtimeAnimatorController = null;
            EditorUtility.SetDirty(anim);
            if (PrefabUtility.IsPartOfPrefabInstance(anim)) PrefabUtility.RecordPrefabInstancePropertyModifications(anim);
            log.AppendLine($"{nombre}: se quitó el Animator Controller de {pj.name} (el asset {ctrl.name} se queda; lo usan las herramientas).");
        }

        EditorUtility.SetDirty(pista);
        return true;
    }

    // tramos [a, b) con velocidad casi constante (las rampas se parten en pasos de 0.1 s)
    static System.Collections.Generic.List<(float a, float b, float s)> Tramos(AnimationCurve vel)
    {
        var lista = new System.Collections.Generic.List<(float a, float b, float s)>();
        if (vel == null) { lista.Add((0f, Fin, 1f)); return lista; }
        for (float t = 0f; t < Fin - 1e-4f; t += Paso)
        {
            float a = t, b = Mathf.Min(t + Paso, Fin);
            float s = (vel.Evaluate(a) + 4f * vel.Evaluate((a + b) * 0.5f) + vel.Evaluate(b)) / 6f;   // promedio (Simpson)
            s = Mathf.Max(0f, s);
            if (s < 0.0015f) s = 0f;
            int u = lista.Count - 1;
            if (u >= 0 && Mathf.Abs(lista[u].s - s) < 0.002f) lista[u] = (lista[u].a, b, lista[u].s);
            else lista.Add((a, b, s));
        }
        return lista;
    }

    static AnimationTrack Pista(TimelineAsset tl, TrackAsset padre, string nombre)
    {
        var p = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == nombre && !(t.parent is AnimationTrack))
                ?? tl.CreateTrack<AnimationTrack>(padre, nombre);
        foreach (var c in p.GetClips().ToList()) tl.DeleteClip(c);
        p.avatarMask = null;
        p.applyAvatarMask = false;
        p.trackOffset = TrackOffset.ApplyTransformOffsets;    // offset cero: el personaje se queda en (0,0,0) de su padre
        p.position = Vector3.zero; p.eulerAngles = Vector3.zero;
        p.muted = false;
        return p;
    }

    // <personaje>_RAIZ: padre vacío con Animator genérico que mueve la pista de recorrido
    static Transform Raiz(Transform pj, StringBuilder log)
    {
        string nombre = pj.name + "_RAIZ";
        Transform raiz = pj.parent != null && pj.parent.name == nombre ? pj.parent : null;
        if (raiz == null)
        {
            if (AnimationMode.InAnimationMode())
            { log.AppendLine($"{pj.name}: apaga el Preview del Timeline y vuelve a correr la herramienta (hay que crear {nombre})."); return null; }
            var go = new GameObject(nombre);
            Undo.RegisterCreatedObjectUndo(go, "Cuerpo en Timeline");
            raiz = go.transform;
            raiz.SetParent(pj.parent, false);
            raiz.SetSiblingIndex(pj.GetSiblingIndex());
            raiz.localPosition = pj.localPosition; raiz.localRotation = pj.localRotation; raiz.localScale = Vector3.one;
            Undo.SetTransformParent(pj, raiz, "Cuerpo en Timeline");
            Undo.RecordObject(pj, "Cuerpo en Timeline");
            pj.localPosition = Vector3.zero; pj.localRotation = Quaternion.identity;
            log.AppendLine($"{pj.name}: creado {nombre} (el recorrido mueve al padre; {pj.name} queda adentro en 0,0,0).");
        }
        var an = raiz.GetComponent<Animator>();
        if (an == null) an = Undo.AddComponent<Animator>(raiz.gameObject);
        an.runtimeAnimatorController = null; an.avatar = null;
        an.applyRootMotion = false; an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        EditorUtility.SetDirty(an);
        return raiz;
    }

    // postura de pie: piernas casi rectas, pies un poco separados (valores de músculo del humanoide)
    static AnimationClip PiernasDePie()
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(RutaPiernas);
        if (c == null) { c = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(c, RutaPiernas); }
        var valores = new (string m, float v)[]
        {
            ("Upper Leg Front-Back", 0.56f), ("Upper Leg In-Out", 0.12f), ("Upper Leg Twist In-Out", 0f),
            ("Lower Leg Stretch", 0.86f), ("Lower Leg Twist In-Out", 0f),
            ("Foot Up-Down", 0.05f), ("Foot Twist In-Out", 0f), ("Toes Up-Down", 0f),
        };
        foreach (var lado in new[] { "Left ", "Right " })
            foreach (var (m, v) in valores)
                AnimationUtility.SetEditorCurve(c, EditorCurveBinding.FloatCurve("", typeof(Animator), lado + m), AnimationCurve.Constant(0f, 1f, v));
        var ajustes = AnimationUtility.GetAnimationClipSettings(c); ajustes.loopTime = true; AnimationUtility.SetAnimationClipSettings(c, ajustes);
        EditorUtility.SetDirty(c);
        return c;
    }

    static AvatarMask MascaraPiernas()
    {
        var m = AssetDatabase.LoadAssetAtPath<AvatarMask>(RutaMascaraPiernas);
        if (m == null) { m = new AvatarMask(); AssetDatabase.CreateAsset(m, RutaMascaraPiernas); }
        for (var p = AvatarMaskBodyPart.Root; p < AvatarMaskBodyPart.LastBodyPart; p++)
            m.SetHumanoidBodyPartActive(p, p == AvatarMaskBodyPart.LeftLeg || p == AvatarMaskBodyPart.RightLeg);
        m.transformCount = 0;
        EditorUtility.SetDirty(m);
        return m;
    }

    static void Ajustes(TimelineClip tc, AnimationPlayableAsset.LoopMode loop, bool pieIK)
    {
        if (tc.asset is AnimationPlayableAsset apa)
        {
            apa.loop = loop;
            apa.applyFootIK = pieIK;
            apa.removeStartOffset = false;   // true le resta al humanoide su altura inicial: la cadera baja y el IK dobla las rodillas
            EditorUtility.SetDirty(apa);
        }
    }

    static AnimationCurve Curva(string ruta, System.Type tipo, string prop)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        return clip != null ? AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve("", tipo, prop)) : null;
    }
}
