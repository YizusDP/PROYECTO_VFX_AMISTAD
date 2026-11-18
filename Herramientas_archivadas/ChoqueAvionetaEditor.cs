using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 2 · Ciudad > Godzilla llega + choque de la avioneta (43-46 s)
/// En la toma de Ale caminando (CM5_ALE_CAMINA, 43.0-46.0 s del principal):
///  - GODZILLA camina en el mismo sentido que en su primera toma (−Z, como Memi, hacia Ale): en esta cámara cruza de derecha
///    a izquierda, cerca (≈105 u, delante de la ruina inclinada): se ven sus pies y piernas gigantes cruzando detrás de Ale
///    (el cuerpo sale de cuadro por arriba). Entra por la derecha y sale por la izquierda. Segundo tramo en sus pistas "Godzilla · visible/caminata" (root motion ×1.4).
///    Desaparece en el corte de las 46 s.
///  - La AVIONETA vuelve dañada (estela de humo), entra arriba a la izquierda, pasa frente a la cabeza de Godzilla,
///    cae en picada hacia la cámara girando y se estrella en la calle abierta a la izquierda de Ale, delante de Godzilla,
///    a los 44.85 s, AL PIE DE GODZILLA: se apagan su malla, la estela y el motor.
///    Su vuelo anterior se recorta a las 43.0 s (ya no se veía: iba a z -2350).
///  - CHOQUE_AVIONETA: explosión (copias reducidas de la nuclear: bola de fuego, humo, destello, brasas, onda de polvo
///    + humo negro del derrumbe), destello de luz naranja y después fuego ardiendo con columna de humo
///    (se ve al fondo del encuentro y del saludo).
/// Todo va en el Timeline de la ciudad (grupos "Godzilla" y "Avioneta"). Se puede volver a correr.
/// </summary>
public static class ChoqueAvionetaEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string Carpeta = "Assets/Assets/3d/Avioneta/";
    const float InicioCiudad = 17.5f;
    const float Ini = 43.0f, Fin = 46.0f, Impacto = 44.85f, FinCiudad = 67.5f;   // principal

    // cámara CM5_ALE_CAMINA a los 44.7 s (sigue a Ale; avanza en +Z, siempre mirando a yaw 249)
    static readonly Vector3 Cam = new Vector3(7.2f, 9f, -261.3f);
    const float CamYaw = 249f;

    // Godzilla: camina como en su primera toma (rumbo 180 = -Z, el mismo sentido que Memi, hacia Ale):
    // en esta cámara cruza de derecha a izquierda. Cerca (≈230 u) para que se vean sus piernas y su cuerpo (la cabeza sale de cuadro),
    // sale de detrás del edificio de la derecha y termina con el cuerpo junto a Ale.
    const float GodziDist = 105f, GodziDer = 85f, GodziRumbo = 180f, GodziRapidez = 1.4f;   // delante de la ruina inclinada (140-230 u): se ven sus pies y piernas
    const float VelGodzi = 25.7f;          // u/s de su caminata con root motion a la velocidad de su primera toma (0.3, medido)
    const float ImpactoFrente = 15f;       // la avioneta cae frente a su pie (hacia la cámara)
    // Avioneta: (tiempo principal, al frente, a la derecha(+)/izquierda(-), altura) en coordenadas de cámara
    static readonly (float t, float d, float l, float h)[] Ruta =
    {
        (43.00f, 400f, -330f, 190f),   // fuera de cuadro, arriba a la izquierda
        (43.85f, 230f,  -40f,  80f),   // cruza el cielo por encima de la cabeza de Ale (sin tocarla)
        (44.45f, 160f,   40f,  40f),   // en picada, a la derecha de la cabeza de Ale, hacia el pie de Godzilla
        (44.85f,   0f,    0f,   0f),   // impacto: se calcula al pie de Godzilla (PieGodzilla)
    };
    const float GiroCaida = -540f;      // vueltas sobre su eje al caer (grados)
    const float EscalaExplosion = 0.45f, EscalaFuego = 18f;   // el choque queda a ~90 u de la cámara

    static Vector3 Fwd => Quaternion.Euler(0, CamYaw, 0) * Vector3.forward;
    static Vector3 Der => Quaternion.Euler(0, CamYaw, 0) * Vector3.right;
    static Vector3 EnCamara(float d, float l, float h) { var p = Cam + Fwd * d + Der * l; p.y = h; return p; }
    static Vector3 RumboGodzi => Quaternion.Euler(0, GodziRumbo, 0) * Vector3.forward;
    static Vector3 InicioGodzi => EnCamara(GodziDist, GodziDer, 0f);
    static Vector3 GodziEn(float t) => InicioGodzi + RumboGodzi * VelGodzi * GodziRapidez * (t - Ini);
    static Vector3 PieGodzilla => GodziEn(Impacto) - Fwd * ImpactoFrente;
    static Vector3 PuntoRuta(int i) => i == Ruta.Length - 1 ? PieGodzilla : EnCamara(Ruta[i].d, Ruta[i].l, Ruta[i].h);

    [MenuItem("Tools/Amistad/2 · Ciudad/Godzilla llega + choque de la avioneta (43-46 s)", false, 227)]
    static void Menu()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Choque] Sal de Play."); return; }
        var log = new StringBuilder();
        if (Aplicar(log)) { AssetDatabase.SaveAssets(); Debug.Log("[Choque] Listo. Guarda la escena.\n" + log); }
        else Debug.LogError("[Choque] No se pudo.\n" + log);
    }

    static Transform T(string n) => Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x.name == n && x.gameObject.scene.IsValid());

    public static bool Aplicar(StringBuilder log)
    {
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dir = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.playableAsset == tl);
        var godzi = T("GODZILLA"); var avion = T("AVIONETA"); var nuclear = T("FINAL_NUCLEAR"); var expDerrumbe = T("Humo_Explosion");
        var fuego = T("Fuego_Calle_00"); var columna = T("Columna_01"); var carpeta = T("Ciudad · personajes");
        if (tl == null || dir == null || godzi == null || avion == null || nuclear == null || fuego == null || columna == null)
        { log.AppendLine("Falta algo (Timeline de la ciudad, GODZILLA, AVIONETA, FINAL_NUCLEAR, Fuego_Calle_00 o Columna_01)."); return false; }
        Undo.RecordObjects(new Object[] { tl, dir }, "Choque");
        double c0 = Ini - InicioCiudad, cImp = Impacto - InicioCiudad, cFin = Fin - InicioCiudad, cTot = FinCiudad - InicioCiudad;

        var gVis = tl.GetOutputTracks().OfType<ActivationTrack>().First(x => x.name == "Godzilla · visible");
        var gCam = tl.GetOutputTracks().OfType<AnimationTrack>().First(x => x.name == "Godzilla · caminata");
        var aVis = tl.GetOutputTracks().OfType<ActivationTrack>().First(x => x.name == "Avioneta · visible");
        var aVue = tl.GetOutputTracks().OfType<AnimationTrack>().First(x => x.name == "Avioneta · vuelo");
        var grupoA = aVue.parent as TrackAsset;

        // ---------- limpiar lo de una corrida anterior
        foreach (var tr in new TrackAsset[] { gVis, gCam, aVis, aVue })
            foreach (var c in tr.GetClips().Where(c => c.start >= c0 - 0.01).ToList()) tl.DeleteClip(c);
        foreach (var vieja in tl.GetOutputTracks().Where(x => x.name == "Avioneta · choque" || x.name == "Avioneta · destello").ToList()) tl.DeleteTrack(vieja);

        // ---------- 1. Godzilla llega
        var gClipOrig = gCam.GetClips().OrderBy(c => c.start).First();
        var animG = ((AnimationPlayableAsset)gClipOrig.asset).clip;
        var dirG = RumboGodzi;
        var inicioG = InicioGodzi;
        var tq = Quaternion.Euler(gCam.eulerAngles);
        var cv = gVis.CreateDefaultClip(); cv.start = c0; cv.duration = cFin - c0; cv.displayName = "Godzilla llega";
        var cg = gCam.CreateClip(animG); cg.start = c0; cg.duration = cFin - c0; cg.displayName = "Godzilla llega"; cg.timeScale = gClipOrig.timeScale * GodziRapidez;   // su caminata va a 0.3 (paso pesado)
        var apG = (AnimationPlayableAsset)cg.asset;
        apG.removeStartOffset = true;
        apG.position = Quaternion.Inverse(tq) * (inicioG - gCam.position);
        apG.rotation = Quaternion.Inverse(tq) * Quaternion.LookRotation(dirG, Vector3.up);
        EditorUtility.SetDirty(apG);
        log.AppendLine($"Godzilla: {Ini}-{Fin} s, de {inicioG:F0} a {GodziEn(Fin):F0} (rumbo {GodziRumbo}°, ×{GodziRapidez}); de derecha a izquierda a ≈{GodziDist} u de la cámara.");

        // ---------- 2. Avioneta: recortar el vuelo anterior y vuelo de choque
        foreach (var tr in new TrackAsset[] { aVis, aVue })
            foreach (var c in tr.GetClips().Where(c => c.end > c0 + 0.001).ToList()) c.duration = c0 - c.start;
        var humo = Humo(avion, expDerrumbe != null ? expDerrumbe : nuclear.Find("BOLA_HUMO"));
        var clipChoque = ClipChoque(avion, humo);
        var ca = aVis.CreateDefaultClip(); ca.start = c0; ca.duration = cTot - c0; ca.displayName = "Avioneta (choque)";
        var cc = aVue.CreateClip(clipChoque); cc.start = c0; cc.duration = cTot - c0; cc.displayName = "Choque";
        var apA = (AnimationPlayableAsset)cc.asset; apA.removeStartOffset = false; apA.position = Vector3.zero; apA.rotation = Quaternion.identity;
        EditorUtility.SetDirty(apA);
        log.AppendLine($"Avioneta: vuelo anterior recortado a {Ini} s; entra dañada y se estrella a los {Impacto} s en {PieGodzilla:F0} (al pie de Godzilla).");

        // ---------- 3. Explosión, destello y fuego
        var viejo = T("CHOQUE_AVIONETA"); if (viejo != null) Undo.DestroyObjectImmediate(viejo.gameObject);
        var raiz = new GameObject("CHOQUE_AVIONETA"); Undo.RegisterCreatedObjectUndo(raiz, "Choque");
        if (carpeta != null) raiz.transform.SetParent(carpeta, false);
        var pImp = PieGodzilla; pImp.y = 0f;
        raiz.transform.position = pImp;
        var expl = new GameObject("EXPLOSION"); expl.transform.SetParent(raiz.transform, false); expl.transform.localScale = Vector3.one * EscalaExplosion;
        var partes = new List<GameObject>();
        foreach (var n in new[] { "BOLA_FUEGO", "BOLA_HUMO", "DESTELLO_CENTRO", "BRASAS", "ONDA_POLVO" })
        {
            var src = nuclear.Find(n); if (src == null) continue;
            var cp = Object.Instantiate(src.gameObject, expl.transform); cp.name = n;
            cp.transform.localPosition = src.position - nuclear.position; cp.transform.localRotation = src.localRotation; cp.transform.localScale = src.localScale;
            foreach (var ps in cp.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.startSpeedMultiplier *= EscalaExplosion; m.playOnAwake = false; }
            cp.SetActive(false); partes.Add(cp);
        }
        if (expDerrumbe != null)
        {
            var cp = Object.Instantiate(expDerrumbe.gameObject, raiz.transform); cp.name = "HUMO_NEGRO";
            cp.transform.localPosition = Vector3.up * 10f; cp.transform.localScale = Vector3.one * 0.5f;
            foreach (var ps in cp.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.startSpeedMultiplier *= 0.5f; m.playOnAwake = false; }
            cp.SetActive(false); partes.Add(cp);
        }
        var fu = Object.Instantiate(fuego.gameObject, raiz.transform); fu.name = "FUEGO_RESTOS";
        fu.transform.localPosition = Vector3.zero; fu.transform.localScale = Vector3.one * EscalaFuego;
        foreach (var l in fu.GetComponentsInChildren<Light>(true)) l.lightmapBakeType = LightmapBakeType.Realtime;
        fu.SetActive(false);
        var co = Object.Instantiate(columna.gameObject, raiz.transform); co.name = "COLUMNA_HUMO";
        co.transform.localPosition = Vector3.up * 30f; co.SetActive(false);

        var ctrl = tl.CreateTrack<ControlTrack>(grupoA, "Avioneta · choque");
        foreach (var p in partes) Control(tl, dir, ctrl, p, cImp, cTot - cImp);
        Control(tl, dir, ctrl, fu, cImp + 0.15, cTot - cImp - 0.15);
        Control(tl, dir, ctrl, co, cImp + 0.4, cTot - cImp - 0.4);

        // destello de luz
        var luzGo = new GameObject("LUZ_CHOQUE"); luzGo.transform.SetParent(raiz.transform, false); luzGo.transform.localPosition = Vector3.up * 25f;
        var luz = luzGo.AddComponent<Light>(); luz.type = LightType.Point; luz.color = new Color(1f, 0.62f, 0.28f); luz.range = 650f; luz.intensity = 0f; luz.shadows = LightShadows.None;
        luz.lightmapBakeType = LightmapBakeType.Realtime;
        var animLuz = luzGo.AddComponent<Animator>(); animLuz.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var clipLuz = AssetDatabase.LoadAssetAtPath<AnimationClip>(Carpeta + "Choque_Luz.anim");
        if (clipLuz == null) { clipLuz = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clipLuz, Carpeta + "Choque_Luz.anim"); }
        clipLuz.ClearCurves();
        AnimationUtility.SetEditorCurve(clipLuz, EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity"),
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.05f, 180000f), new Keyframe(0.4f, 70000f), new Keyframe(1.5f, 0f)));
        EditorUtility.SetDirty(clipLuz);
        var pLuz = tl.CreateTrack<AnimationTrack>(grupoA, "Avioneta · destello");
        var cl = pLuz.CreateClip(clipLuz); cl.start = cImp; cl.duration = 1.5; cl.displayName = "Destello";
        dir.SetGenericBinding(pLuz, animLuz);
        raiz.SetActive(true);
        log.AppendLine($"Explosión en {pImp:F0}: bola de fuego, humo, destello, brasas, onda de polvo y humo negro (×{EscalaExplosion}), luz naranja y fuego + columna de humo hasta el final de la ciudad.");

        EditorUtility.SetDirty(tl); EditorUtility.SetDirty(dir);
        EditorSceneManager.MarkSceneDirty(dir.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return true;
    }

    static void Control(TimelineAsset tl, PlayableDirector dir, ControlTrack pista, GameObject go, double ini, double dur)
    {
        var c = pista.CreateClip<ControlPlayableAsset>(); c.start = ini; c.duration = dur; c.displayName = go.name;
        var cp = (ControlPlayableAsset)c.asset;
        var id = GUID.Generate().ToString();
        cp.sourceGameObject = new ExposedReference<GameObject> { exposedName = id };
        dir.SetReferenceValue(id, go);
        cp.active = true; cp.updateParticle = true; cp.updateDirector = false; cp.updateITimeControl = false; cp.searchHierarchy = false;
        cp.postPlayback = ActivationControlPlayable.PostPlaybackState.Inactive;
        cp.particleRandomSeed = (uint)Random.Range(1, 100000);
        EditorUtility.SetDirty(cp);
    }

    /// estela de humo de la avioneta dañada (emite por distancia; la animación del choque la prende y la apaga)
    static ParticleSystem Humo(Transform avion, Transform fuente)
    {
        var viejo = avion.Find("HUMO_AVIONETA"); if (viejo != null) Undo.DestroyObjectImmediate(viejo.gameObject);
        var go = Object.Instantiate(fuente.gameObject, avion); go.name = "HUMO_AVIONETA";
        Undo.RegisterCreatedObjectUndo(go, "Choque");
        foreach (Transform h in go.transform.Cast<Transform>().ToList()) Object.DestroyImmediate(h.gameObject);
        go.transform.localPosition = new Vector3(0f, 0.1f, -1.4f); go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
        go.SetActive(true);
        var ps = go.GetComponent<ParticleSystem>();
        var m = ps.main; m.loop = true; m.duration = 5f; m.startDelay = 0f; m.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.6f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(1f, 4f); m.startSize = new ParticleSystem.MinMaxCurve(7f, 14f);
        m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Local; m.maxParticles = 1500; m.playOnAwake = true;
        m.startColor = new Color(0.32f, 0.29f, 0.27f, 0.9f);
        var em = ps.emission; em.enabled = true; em.SetBursts(new ParticleSystem.Burst[0]); em.rateOverTime = 0f; em.rateOverDistance = 0f;   // la anima el clip
        var sh = ps.shape; sh.enabled = true; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 1.5f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(1, 2.6f)));
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient(); g.SetKeys(new[] { new GradientColorKey(new Color(1f, 0.55f, 0.25f), 0f), new GradientColorKey(Color.white, 0.12f), new GradientColorKey(Color.white, 1f) },
                                         new[] { new GradientAlphaKey(0.95f, 0f), new GradientAlphaKey(0.7f, 0.5f), new GradientAlphaKey(0f, 1f) });
        col.color = g;
        return ps;
    }

    static Vector3 CR(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float u)
    {
        float u2 = u * u, u3 = u2 * u;
        return 0.5f * ((2f * p1) + (-p0 + p2) * u + (2f * p0 - 5f * p1 + 4f * p2 - p3) * u2 + (-p0 + 3f * p1 - 3f * p2 + p3) * u3);
    }

    static Vector3 PosRuta(float t)
    {
        var pts = Enumerable.Range(0, Ruta.Length).Select(PuntoRuta).ToList();
        int n = Ruta.Length;
        int i = 0; while (i < n - 2 && t > Ruta[i + 1].t) i++;
        float u = Mathf.Clamp01((t - Ruta[i].t) / (Ruta[i + 1].t - Ruta[i].t));
        var p0 = i > 0 ? pts[i - 1] : pts[0] * 2f - pts[1];
        var p3 = i + 2 < n ? pts[i + 2] : pts[n - 1] * 2f - pts[n - 2];
        return CR(p0, pts[i], pts[i + 1], p3, u);
    }

    static AnimationClip ClipChoque(Transform avion, ParticleSystem humo)
    {
        var ruta = Carpeta + "Avioneta_Choque.anim";
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (clip == null) { clip = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clip, ruta); }
        clip.ClearCurves();
        var cs = new AnimationCurve[7]; for (int k = 0; k < 7; k++) cs[k] = new AnimationCurve();
        float dt = 1f / 30f, dur = Impacto - Ini;
        Quaternion qPrev = Quaternion.identity; bool primero = true;
        for (float lt = 0f; lt <= dur + 1e-4f; lt += dt)
        {
            float t = Ini + lt;
            var p = PosRuta(t);
            var tan = PosRuta(Mathf.Min(t + 0.03f, Impacto)) - PosRuta(Mathf.Max(t - 0.03f, Ini));
            if (tan.sqrMagnitude < 1e-6f) tan = Vector3.down;
            float u = lt / dur;
            var q = Quaternion.LookRotation(tan.normalized, Vector3.up) * Quaternion.Euler(0, 0, GiroCaida * u * u);
            if (!primero && Quaternion.Dot(q, qPrev) < 0f) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            qPrev = q; primero = false;
            float[] v = { p.x, p.y, p.z, q.x, q.y, q.z, q.w };
            for (int k = 0; k < 7; k++) cs[k].AddKey(lt, v[k]);
        }
        string[] props = { "m_LocalPosition.x", "m_LocalPosition.y", "m_LocalPosition.z", "m_LocalRotation.x", "m_LocalRotation.y", "m_LocalRotation.z", "m_LocalRotation.w" };
        for (int k = 0; k < 7; k++) AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), props[k]), cs[k]);
        AnimationCurve Escalon(float antes, float despues) =>
            new AnimationCurve(new Keyframe(0f, antes, 0, 0), new Keyframe(dur - 0.001f, antes, 0, 0), new Keyframe(dur, despues, 0, 0), new Keyframe(dur + 0.5f, despues, 0, 0));
        void Constante(AnimationCurve c) { for (int k = 0; k < c.length; k++) { AnimationUtility.SetKeyLeftTangentMode(c, k, AnimationUtility.TangentMode.Constant); AnimationUtility.SetKeyRightTangentMode(c, k, AnimationUtility.TangentMode.Constant); } }
        var off = Escalon(1f, 0f); Constante(off);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Hull", typeof(SkinnedMeshRenderer), "m_Enabled"), off);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Propeller", typeof(MeshRenderer), "m_Enabled"), off);
        var vol = Escalon(0.9f, 0f); Constante(vol);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(AudioSource), "m_Volume"), vol);
        var hum = Escalon(0.55f, 0f); Constante(hum);
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(AnimationUtility.CalculateTransformPath(humo.transform, avion), typeof(ParticleSystem), "EmissionModule.rateOverDistance.scalar"), hum);
        EditorUtility.SetDirty(clip);
        return clip;
    }
}
