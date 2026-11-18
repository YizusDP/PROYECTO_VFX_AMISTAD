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
/// Tools > Amistad > 1 · Bosque > Explosión del bosque: a tiempo con el gato + humo y polvo
/// En el plano del gato (CM1_GATO) el gato empieza a correr a los ~7.0 s, pero las "Explosión horizontal" empezaban a
/// los 7.38 / 7.67 s y además quedaban tapadas por los árboles hasta ~8.5 s: el gato reaccionaba antes de la explosión.
///  - Adelanta las dos explosiones a 6.6 / 6.9 s (el gato reacciona ~0.4 s después).
///  - Destello naranja (luz) que ilumina el bosque aunque la bola quede detrás de los árboles.
///  - Humo claro y oscuro que sube en columna por encima de los árboles, onda de polvo y escombros encendidos.
///  - Temblor de cámara en CM1_GATO.
/// Todo en los Timelines del título (CM2_TITULO) y de la cascada (CM1_CASCADA). Se puede volver a correr.
/// </summary>
public static class BosqueExplosionEditor
{
    const float Explota = 6.6f, Explota2 = 6.9f;           // antes 7.38 / 7.67
    const float Fin = 17.5f;
    const string Raiz = "EXPLOSION_BOSQUE_HUMO";
    const string PistaHumo = "Explosión bosque · humo y polvo";
    const string PistaLuz = "Explosión bosque · destello";
    const string PistaTemblor = "Cámara gato · temblor explosión";
    const string Carpeta = "Assets/Animaciones/Bosque";
    static readonly Vector3 PosPolvoCamino = new Vector3(3222f, 10f, 420f);   // camino entre la explosión y la cámara del gato (piso y = 5)
    static readonly (float t, float v)[] Luz = { (0f, 0f), (0.05f, 150000f), (0.4f, 50000f), (1.5f, 10000f), (3f, 0f) };   // a ~310 u: necesita intensidad alta (como LUZ_CHOQUE)
    // humo oscuro (ver Capas): cáscara del tamaño de las bolas de fuego, corrida hacia la cámara
    const float HumoRadio = 140f, HumoRitmo = 70f;
    static readonly Vector2 HumoTam = new Vector2(120f, 220f);
    static readonly Vector3 HumoCorrimiento = new Vector3(0f, 80f, -160f);   // bolas: centros z 530–800, y 50–460 (medido en Play)
    static readonly (float t, float v)[] Temblor = { (0f, 0f), (0.04f, 14f), (0.4f, 6f), (0.75f, 9f), (1.2f, 3f), (2.2f, 0f) };

    [MenuItem("Tools/Amistad/1 · Bosque/Explosión del bosque: a tiempo con el gato + humo y polvo")]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Explosión bosque] Sal de Play primero."); return; }
        var log = new StringBuilder();
        if (Aplicar(log)) { AssetDatabase.SaveAssets(); Debug.Log("[Explosión bosque] Listo.\n" + log); }
        else Debug.LogError("[Explosión bosque] No se pudo.\n" + log);
    }

    static PlayableDirector Director(string nombre) =>
        Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.name == nombre);

    public static bool Aplicar(StringBuilder log)
    {
        var dirT = Director("CM2_TITULO"); var dirC = Director("CM1_CASCADA");
        var tlT = dirT != null ? dirT.playableAsset as TimelineAsset : null;
        var tlC = dirC != null ? dirC.playableAsset as TimelineAsset : null;
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var bosque = todos.FirstOrDefault(t => t.name == "BOSQUE");
        var exp = todos.FirstOrDefault(t => t.name == "Explosión horizontal");
        if (tlT == null || tlC == null || bosque == null || exp == null) { log.AppendLine("Falta CM2_TITULO, CM1_CASCADA, BOSQUE o la Explosión horizontal."); return false; }
        System.IO.Directory.CreateDirectory(Carpeta);

        // ---------- 1. adelantar las explosiones
        Undo.RecordObject(tlT, "Explosión bosque");
        foreach (var (pista, t) in new[] { ("Explosión horizontal", Explota), ("Explosión horizontal (1)", Explota2) })
        {
            var tr = tlT.GetOutputTracks().FirstOrDefault(x => x.name == pista);
            var c = tr?.GetClips().FirstOrDefault();
            if (c == null) { log.AppendLine($"No encontré la pista '{pista}'."); continue; }
            double antes = c.start; c.start = t; c.duration = Fin - t;
            EditorUtility.SetDirty(tr);
            log.AppendLine($"{pista}: {antes:0.00} -> {t:0.00} s");
        }

        // ---------- 2. humo, polvo, escombros y destello
        var vieja = bosque.Find(Raiz); if (vieja != null) Undo.DestroyObjectImmediate(vieja.gameObject);
        foreach (var x in tlT.GetOutputTracks().Where(x => x.name == PistaHumo || x.name == PistaLuz).ToList()) tlT.DeleteTrack(x);
        foreach (var x in tlC.GetOutputTracks().Where(x => x.name == PistaTemblor).ToList()) tlC.DeleteTrack(x);

        var raiz = new GameObject(Raiz); Undo.RegisterCreatedObjectUndo(raiz, "Explosión bosque");
        raiz.transform.SetParent(bosque, true); raiz.transform.position = exp.position; raiz.transform.localScale = Vector3.one;
        var sistemas = Capas(raiz.transform, log);
        if (sistemas == null) return false;
        var pH = tlT.CreateTrack<ControlTrack>(null, PistaHumo);
        foreach (var go in sistemas)
        {
            var tc = pH.CreateDefaultClip(); tc.start = Explota; tc.duration = Fin - Explota; tc.displayName = go.name;
            var ca = (ControlPlayableAsset)tc.asset;
            var er = ca.sourceGameObject; if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
            er.defaultValue = null; ca.sourceGameObject = er;
            ca.updateParticle = true; ca.particleRandomSeed = (uint)(go.name.GetHashCode() & 0x7fffffff); ca.active = true;
            ca.updateDirector = false; ca.updateITimeControl = false; ca.searchHierarchy = false;
            dirT.SetReferenceValue(er.exposedName, go);
            go.SetActive(false);
            EditorUtility.SetDirty(ca);
        }
        EditorUtility.SetDirty(pH);

        // luz del destello
        var luzGo = new GameObject("DESTELLO_LUZ"); luzGo.transform.SetParent(raiz.transform, false); luzGo.transform.localPosition = Vector3.up * 40f;
        var luz = luzGo.AddComponent<Light>(); luz.type = LightType.Point; luz.range = 900f; luz.color = new Color(1f, 0.6f, 0.25f); luz.intensity = 0f; luz.shadows = LightShadows.None;
        var anL = luzGo.AddComponent<Animator>(); anL.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var clipL = Clip($"{Carpeta}/Explosion_Bosque_Luz.anim", "", typeof(Light), "m_Intensity", Luz);
        var pL = tlT.CreateTrack<AnimationTrack>(null, PistaLuz);
        var tcl = pL.CreateClip(clipL); tcl.start = Explota; tcl.duration = Luz[Luz.Length - 1].t; tcl.displayName = "Destello";
        if (tcl.asset is AnimationPlayableAsset apl) apl.removeStartOffset = false;
        pL.trackOffset = TrackOffset.ApplySceneOffsets;
        dirT.SetGenericBinding(pL, anL);
        EditorUtility.SetDirty(pL);
        log.AppendLine($"Humo, polvo, escombros ({sistemas.Length} capas) y destello en {raiz.transform.position}, de {Explota} a {Fin} s.");

        // ---------- 3. temblor de cámara en CM1_GATO: DESACTIVADO. Esa cámara usa CinemachineSplineDollyLookAtTargets y con
        //            cualquier ruido de Perlin (ganancia > 0) gira ~105° de golpe. Se quitan el Perlin y la pista si existían.
        var cam = todos.FirstOrDefault(t => t != null && t.name == "CM1_GATO");   // t != null: la lista trae el humo viejo ya borrado
        if (cam != null) { var pv = cam.GetComponent<CinemachineBasicMultiChannelPerlin>(); if (pv != null) Undo.DestroyObjectImmediate(pv); }
        if (false)
        {
            var perlin = cam.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (perlin == null) perlin = Undo.AddComponent<CinemachineBasicMultiChannelPerlin>(cam.gameObject);
            Undo.RecordObject(perlin, "Explosión bosque");
            perlin.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>("Packages/com.unity.cinemachine/Presets/Noise/6D Shake.asset");
            perlin.AmplitudeGain = 0f; perlin.FrequencyGain = 0.8f;
            EditorUtility.SetDirty(perlin);
            var an = cam.GetComponent<Animator>();
            if (an == null) an = Undo.AddComponent<Animator>(cam.gameObject);
            var clipT = Clip($"{Carpeta}/Explosion_Bosque_Temblor.anim", "", typeof(CinemachineBasicMultiChannelPerlin), "AmplitudeGain", Temblor);
            Undo.RecordObject(tlC, "Explosión bosque");
            var pT = tlC.CreateTrack<AnimationTrack>(null, PistaTemblor);
            var tct = pT.CreateClip(clipT); tct.start = Explota; tct.duration = Temblor[Temblor.Length - 1].t; tct.displayName = "Temblor";
            if (tct.asset is AnimationPlayableAsset apt) apt.removeStartOffset = false;
            pT.trackOffset = TrackOffset.ApplySceneOffsets;
            dirC.SetGenericBinding(pT, an);
            EditorUtility.SetDirty(pT);
            log.AppendLine("Temblor en CM1_GATO con la explosión.");
        }
        EditorUtility.SetDirty(tlT); EditorUtility.SetDirty(tlC);
        EditorSceneManager.MarkSceneDirty(bosque.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return true;
    }

    static AnimationClip Clip(string ruta, string path, System.Type tipo, string prop, (float t, float v)[] k)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (clip == null) { clip = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clip, ruta); }
        clip.ClearCurves();
        var cu = new AnimationCurve(k.Select(x => new Keyframe(x.t, x.v)).ToArray());
        for (int i = 0; i < cu.length; i++) { AnimationUtility.SetKeyLeftTangentMode(cu, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(cu, i, AnimationUtility.TangentMode.Linear); }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(path, tipo, prop), cu);
        EditorUtility.SetDirty(clip);
        return clip;
    }

    // ================================================================= capas
    static Material Mat(string ruta) => AssetDatabase.LoadAssetAtPath<Material>(ruta);

    static ParticleSystem Sistema(Transform padre, string nombre, Material mat, ParticleSystemRenderMode modo = ParticleSystemRenderMode.Billboard)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.maxParticles = 3000;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = modo;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.Distance; r.maxParticleSize = 5f;
        if (mat != null && mat.shader != null && mat.shader.name.Contains("FuegoVFX"))
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
              ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent });
        return ps;
    }

    static Gradient Grad(params (float t, Color c)[] k)
    {
        var g = new Gradient();
        g.SetKeys(k.Select(x => new GradientColorKey(new Color(x.c.r, x.c.g, x.c.b), x.t)).ToArray(),
                  k.Select(x => new GradientAlphaKey(x.c.a, x.t)).ToArray());
        return g;
    }

    static GameObject[] Capas(Transform raiz, StringBuilder log)
    {
        var ceniza = Mat("Assets/Assets/shaders/FuegoVFX/Fuego_Ceniza.mat");
        var humoOsc = Mat("Assets/Assets/shaders/FuegoVFX/Fuego_Humo.mat");
        var brasas = Mat("Assets/Assets/shaders/FuegoVFX/Fuego_Brasas.mat");
        var brillo = Mat("Assets/Animaciones/Saludo/Saludo_Estallido.mat");
        if (ceniza == null || humoOsc == null || brasas == null || brillo == null) { log.AppendLine("Faltan materiales de humo/brasas/brillo."); return null; }
        var lista = new List<GameObject>();

        // destello (aditivo, enorme y breve: se ve por encima de los árboles)
        {
            var ps = Sistema(raiz, "DESTELLO", brillo);
            var m = ps.main; m.duration = 0.3f; m.startLifetime = 0.7f; m.startSpeed = 0f; m.startSize = 260f; m.startColor = new Color(1f, 0.8f, 0.45f, 1f);
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 0.95f, 0.8f, 1)), (0.25f, new Color(1, 0.6f, 0.25f, 0.7f)), (1f, new Color(1, 0.3f, 0.1f, 0)));
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.4f, 1, 1.3f));
            ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 20f;
            ps.transform.localPosition = Vector3.up * 20f; lista.Add(ps.gameObject);
        }
        // humo oscuro de la explosión. Las bolas de "Explosión horizontal" miden ~380 u y son de recorte alfa (opacas): un humo
        // más chico o en el centro queda tapado. Por eso sale de una cáscara del tamaño de las bolas, corrida hacia la cámara
        // (las cámaras del gato y del título miran hacia +Z), va saliendo mientras las bolas se apagan, sube y se deshace con
        // turbulencia (antes se juntaba en una bola oscura que flotaba encima del título). (2026-11-11)
        {
            var ps = Sistema(raiz, "HUMO_OSCURO", humoOsc);
            var m = ps.main; m.duration = 3f; m.startDelay = 0.1f; m.startLifetime = new ParticleSystem.MinMaxCurve(5f, 7f); m.startSpeed = new ParticleSystem.MinMaxCurve(20f, 50f);
            m.startSize = new ParticleSystem.MinMaxCurve(HumoTam.x, HumoTam.y); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.05f; m.maxParticles = 400;
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 8f), new Keyframe(0.35f, HumoRitmo), new Keyframe(1f, 15f)));
            em.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 12) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = HumoRadio; sh.radiusThickness = 0.35f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 12f; lim.dampen = 0.05f;
            var nz = ps.noise; nz.enabled = true; nz.strength = 10f; nz.frequency = 0.004f; nz.scrollSpeed = 0.25f; nz.octaveCount = 2; nz.quality = ParticleSystemNoiseQuality.Medium;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.9f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1f, 0.5f, 0.2f, 0f)), (0.07f, new Color(0.5f, 0.3f, 0.2f, 0.95f)), (0.35f, new Color(0.24f, 0.2f, 0.19f, 0.95f)), (0.75f, new Color(0.22f, 0.19f, 0.18f, 0.7f)), (1f, new Color(0.2f, 0.18f, 0.17f, 0f)));
            ps.transform.position = raiz.position + HumoCorrimiento;
            lista.Add(ps.gameObject);
        }
        // columna de humo que sigue subiendo (se ve por encima de los árboles hasta el título)
        {
            var ps = Sistema(raiz, "HUMO_COLUMNA", ceniza);
            var m = ps.main; m.duration = 4f; m.startDelay = 0.2f; m.startLifetime = new ParticleSystem.MinMaxCurve(7f, 10f); m.startSpeed = new ParticleSystem.MinMaxCurve(25f, 45f);
            m.startSize = new ParticleSystem.MinMaxCurve(60f, 110f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.06f;
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 70f, 1, 12f));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 18f; sh.radius = 25f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 14f; lim.dampen = 0.03f;
            // turbulencia y giro: sin esto las partículas viejas se juntaban arriba en un disco gris (visible en el título)
            var nz = ps.noise; nz.enabled = true; nz.strength = 8f; nz.frequency = 0.004f; nz.scrollSpeed = 0.2f; nz.octaveCount = 2; nz.quality = ParticleSystemNoiseQuality.Medium;
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.25f, 0.25f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.8f, 1, 3.2f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(0.9f, 0.55f, 0.3f, 0f)), (0.1f, new Color(0.55f, 0.47f, 0.42f, 0.75f)), (0.5f, new Color(0.42f, 0.38f, 0.36f, 0.5f)), (1f, new Color(0.4f, 0.37f, 0.35f, 0f)));
            lista.Add(ps.gameObject);
        }
        // onda de polvo (anillo horizontal que se abre rápido)
        {
            var ps = Sistema(raiz, "POLVO_ONDA", ceniza);
            var m = ps.main; m.duration = 0.4f; m.startDelay = 0.05f; m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(150f, 220f);
            m.startSize = new ParticleSystem.MinMaxCurve(50f, 90f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 500) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 20f; sh.radiusThickness = 0f; sh.rotation = new Vector3(90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 8f; lim.dampen = 0.07f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 2.8f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1f, 0.6f, 0.3f, 0.85f)), (0.2f, new Color(0.55f, 0.43f, 0.32f, 0.75f)), (1f, new Color(0.45f, 0.38f, 0.3f, 0f)));
            ps.transform.localPosition = Vector3.down * 25f; lista.Add(ps.gameObject);
        }
        // ola de polvo a ras de piso que avanza por el camino hacia la cámara (detrás del gato que viene corriendo)
        {
            var ps = Sistema(raiz, "POLVO_CAMINO", ceniza);
            var m = ps.main; m.duration = 1.6f; m.startDelay = 0.6f; m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 4f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(25f, 45f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = 130f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(120f, 8f, 80f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-6f, 6f); vel.y = new ParticleSystem.MinMaxCurve(2f, 8f); vel.z = new ParticleSystem.MinMaxCurve(-45f, -25f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 2.2f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(0.6f, 0.48f, 0.36f, 0f)), (0.2f, new Color(0.55f, 0.45f, 0.35f, 0.6f)), (1f, new Color(0.5f, 0.42f, 0.34f, 0f)));
            ps.transform.position = PosPolvoCamino;
            lista.Add(ps.gameObject);
        }
        // escombros encendidos que salen volando y caen
        {
            var ps = Sistema(raiz, "ESCOMBROS", brasas);
            var m = ps.main; m.duration = 0.4f; m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f); m.startSpeed = new ParticleSystem.MinMaxCurve(110f, 260f);
            m.startSize = new ParticleSystem.MinMaxCurve(3f, 7f); m.gravityModifier = 2.5f; m.startColor = new Color(1f, 0.6f, 0.2f, 1f);
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0.03f, 350) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 20f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 0.9f, 0.6f, 1)), (0.5f, new Color(1, 0.5f, 0.15f, 1)), (1f, new Color(0.5f, 0.1f, 0f, 0f)));
            lista.Add(ps.gameObject);
        }
        return lista.ToArray();
    }
}
