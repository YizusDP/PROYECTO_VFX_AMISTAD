using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Título final épico (chispas y fundido a negro)
/// Le pone acción al título #AMISTAD sobre negro (TITULO_FINAL, 67.5-72.9 s del principal). Tiempos locales del título:
///   0.0 - 0.6   negro; chispas verdes salen de la nada y se juntan donde va a nacer el "#"
///   0.6         ¡el "#" aparece de golpe! (escala con rebote, brillo blanco-verde), estallido de chispas, onda,
///               destello y un golpe de cámara
///   1.45 - 2.3  A-M-I-S-T-A-D aparecen una por una, cada una con su chispazo
///   2.6         golpe de energía en toda la palabra: destello alargado, lluvia de chispas y otro golpe de cámara
///   2.6 - 5.4   brasas verdes que suben
///   4.0 - 5.3   todo se va apagando (las letras pierden brillo y el Volume del fundido baja la exposición) hasta negro
/// Todo es repetible (curvas en un clip + partículas con Control Track y semilla fija): el Timeline se puede adelantar y
/// regresar y el video exportado sale igual. Se puede volver a correr (borra lo anterior).
/// FinalTituloEditor también lo llama al final, así que si se rehace el título no se pierde.
/// </summary>
public static class TituloEpicoEditor
{
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string Carpeta = "Assets/Animaciones/Saludo";
    const float InicioCiudad = 17.5f, IniTitulo = 67.5f, FinCorto = 72.9f;
    const string Pt = " · ";
    const string PrefijoPista = "Título épico" + Pt;

    // tiempos locales del título
    const float TGato = 0.6f, TLetra0 = 1.45f, PasoLetra = 0.14f, TGolpe = 2.6f, IniApagar = 4.2f, FinApagar = 5.3f;

    static readonly string[] OrdenLetras = { "#", "A", "M", "I", "S", "T", "A 1", "D" };
    // líquido, cámara y "PRÓXIMAMENTE"
    const float TEscurre = 2.7f, TProx = 2.95f, AnguloCamara = 0f;
    const string RutaShaderGoteo = "Assets/Assets/shaders/Caos/GoteoRadiactivo.shader";
    static readonly string[] Colores = { "_ColorVetas", "_ColorBorde", "_ColorNucleo" };

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Título final épico (chispas y fundido a negro)", false, 315)]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Título épico] Sal de Play primero."); return; }
        var log = new StringBuilder();
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dirC = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.playableAsset == tlC);
        var grupo = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t != null && t.name == "TITULO_FINAL");
        var grupoTl = tlC != null ? tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "9" + Pt + "Título final") : null;
        if (tlC == null || dirC == null || grupo == null || grupoTl == null)
        { Debug.LogError("[Título épico] Falta el Timeline de la ciudad, su Director, TITULO_FINAL o el grupo '9 · Título final'. Corre antes 'Solo: final'."); return; }
        if (Agregar(grupo, tlC, dirC, grupoTl, FinCorto - IniTitulo, log))
        {
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(grupo.gameObject.scene);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/titulo_epico.txt", log.ToString());
            Debug.Log("[Título épico] Listo. Guarda la escena con Ctrl+S.\n" + log);
        }
        else Debug.LogError("[Título épico] No se pudo.\n" + log);
    }

    public static bool Agregar(Transform grupo, TimelineAsset tlC, PlayableDirector dirC, GroupTrack grupoTl, float durT, StringBuilder log)
    {
        // ---------- letras en orden de lectura
        var letras = new List<Transform>();
        foreach (var n in OrdenLetras) { var l = grupo.Find(n); if (l != null) letras.Add(l); }
        if (letras.Count < 2 || letras[0].name != "#") { log.AppendLine("No encuentro las letras (#, A, M, I, S, T, A 1, D) dentro de TITULO_FINAL."); return false; }
        var rends = letras.Select(l => l.GetComponent<Renderer>()).ToList();
        if (rends.Any(r => r == null)) { log.AppendLine("Alguna letra no tiene Renderer."); return false; }
        var mat = rends[0].sharedMaterial;
        var cam = grupo.Find("CM_TITULO_FINAL");
        var haciaCam = cam != null ? -cam.forward : -grupo.forward;
        var bPalabra = rends[0].bounds; foreach (var r in rends) bPalabra.Encapsulate(r.bounds);

        // ---------- limpiar lo anterior
        var viejo = grupo.Find("TITULO_EPICO"); if (viejo != null) Undo.DestroyObjectImmediate(viejo.gameObject);
        foreach (var l in letras)
        {
            var rf = l.Find("REFLEJO"); if (rf != null) Undo.DestroyObjectImmediate(rf.gameObject);   // de la versión con charco
            var gt = l.Find("GOTEO"); if (gt != null) Undo.DestroyObjectImmediate(gt.gameObject);
        }
        foreach (var p in tlC.GetOutputTracks().Where(x => x.name.StartsWith(PrefijoPista)).ToList()) tlC.DeleteTrack(p);

        // ---------- 1. letras: aparecen con rebote y brillo (clip en un Animator del grupo)
        var anim = grupo.GetComponent<Animator>();
        if (anim == null) { anim = grupo.gameObject.AddComponent<Animator>(); Undo.RegisterCreatedObjectUndo(anim, "Título épico"); }
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate; anim.applyRootMotion = false;
        var clip = Clip(Carpeta + "/Titulo_Epico_Letras.anim");
        for (int i = 0; i < letras.Count; i++)
        {
            var l = letras[i];
            bool esGato = i == 0;
            float tAp = esGato ? TGato : TLetra0 + (i - 1) * PasoLetra;
            float pico = esGato ? 1.35f : 1.2f;
            var s = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(tAp, 0f), new Keyframe(tAp + 0.10f, pico), new Keyframe(tAp + 0.20f, 0.93f),
                new Keyframe(tAp + 0.30f, 1f), new Keyframe(TGolpe, 1f), new Keyframe(TGolpe + 0.07f, 1.07f), new Keyframe(TGolpe + 0.25f, 1f), new Keyframe(durT, 1f));
            Lineal(s);
            var ls = l.localScale;
            for (int k = 0; k < 3; k++)
            {
                var c = new AnimationCurve(s.keys.Select(x => new Keyframe(x.time, x.value * ls[k])).ToArray()); Lineal(c);
                clip.SetCurve(l.name, typeof(Transform), "m_LocalScale." + "xyz"[k], c);
            }
            // brillo: f = 0 antes, flash al aparecer, otro flash en el golpe, se apaga al final
            float fAp = esGato ? 3.2f : 2.4f;
            var f = new (float t, float v)[]
            {
                (0f, 1f), (tAp, fAp), (tAp + 0.7f, 1f), (TGolpe - 0.02f, 1f), (TGolpe + 0.05f, 2.3f), (TGolpe + 0.6f, 1f),
                (IniApagar, 1f), (FinApagar, 0.1f), (durT, 0.1f)
            };
            foreach (var prop in Colores)
            {
                if (mat == null || !mat.HasProperty(prop)) continue;
                var b = mat.GetColor(prop);
                var blanco = new Color(1.6f, 2.2f, 1.4f);
                for (int k = 0; k < 4; k++)
                {
                    var c = new AnimationCurve(f.Select(x =>
                    {
                        float extra = Mathf.Max(0f, x.v - 1f);
                        float v = k == 3 ? b.a : b[k] * x.v + blanco[k] * extra;
                        return new Keyframe(x.t, v);
                    }).ToArray());
                    Lineal(c);
                    clip.SetCurve(l.name, typeof(MeshRenderer), $"material.{prop}.{"rgba"[k]}", c);
                }
            }
        }
        EditorUtility.SetDirty(clip);
        var pLetras = tlC.CreateTrack<AnimationTrack>(grupoTl, PrefijoPista + "letras");
        pLetras.trackOffset = TrackOffset.ApplySceneOffsets;
        var tcL = pLetras.CreateClip(clip); tcL.start = IniTitulo - InicioCiudad; tcL.duration = durT; tcL.displayName = "Letras: # y luego AMISTAD";
        if (tcL.asset is AnimationPlayableAsset apL) apL.removeStartOffset = false;
        dirC.SetGenericBinding(pLetras, anim);
        log.AppendLine($"Letras: # a los {TGato} s del título; AMISTAD de {TLetra0} a {TLetra0 + (letras.Count - 2) * PasoLetra:0.00} s; golpe a los {TGolpe} s; se apagan de {IniApagar} a {FinApagar} s.");

        // ---------- 2. partículas
        var raiz = new GameObject("TITULO_EPICO"); Undo.RegisterCreatedObjectUndo(raiz, "Título épico");
        raiz.transform.SetParent(grupo, false);
        var mChispa = MatChispa();
        var mFlash = AssetDatabase.LoadAssetAtPath<Material>(Carpeta + "/Saludo_Estallido.mat");
        var mOnda = AssetDatabase.LoadAssetAtPath<Material>(Carpeta + "/Saludo_Onda.mat");
        if (mChispa == null || mFlash == null || mOnda == null) { log.AppendLine("Faltan materiales (Aura_Motas_SSJ, Saludo_Estallido o Saludo_Onda en Animaciones/Saludo)."); return false; }
        var sistemas = new List<(GameObject go, float t)>();
        Vector3 Frente(Bounds b, float u) => b.center + haciaCam * u;
        float alto = rends[0].bounds.size.y;                     // ~55 u
        var verdeBlanco = new Color(0.85f, 1f, 0.8f, 1f);

        // 2a. chispas que salen de la nada y se juntan en el "#"
        {
            var ps = Sistema(raiz.transform, "CHISPAS_SE_JUNTAN", mChispa, Frente(rends[0].bounds, 10f));
            var m = ps.main; m.duration = 0.45f; m.startLifetime = 0.55f; m.startSpeed = 0f; m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); m.startColor = verdeBlanco;
            var em = ps.emission; em.rateOverTime = 260f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = alto * 1.6f; sh.radiusThickness = 0f;
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
            v.x = new ParticleSystem.MinMaxCurve(0f); v.y = new ParticleSystem.MinMaxCurve(0f); v.z = new ParticleSystem.MinMaxCurve(0f);
            v.radial = new ParticleSystem.MinMaxCurve(-alto * 1.6f / 0.55f);
            Alfa(ps, (0f, 0f), (1f, 0.3f), (1f, 0.85f), (0f, 1f));
            sistemas.Add((ps.gameObject, 0.05f));
        }
        // 2b. el "#" nace: estallido de chispas, destello y onda
        {
            var ps = Sistema(raiz.transform, "GATO_CHISPAS", mChispa, Frente(rends[0].bounds, 12f));
            var m = ps.main; m.duration = 0.3f; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(90f, 260f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.8f); m.gravityModifier = 5f; m.startColor = verdeBlanco;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 320) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 8f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 20f; lim.dampen = 0.09f;
            Alfa(ps, (1f, 0f), (1f, 0.6f), (0f, 1f));
            ColorVida(ps, (0f, new Color(1f, 1f, 1f)), (0.25f, new Color(0.7f, 1f, 0.5f)), (1f, new Color(0.9f, 0.85f, 0.3f)));
            sistemas.Add((ps.gameObject, TGato));

            var fl = Sistema(raiz.transform, "GATO_DESTELLO", mFlash, Frente(rends[0].bounds, 16f));
            var mf = fl.main; mf.duration = 0.2f; mf.loop = false; mf.startLifetime = 0.45f; mf.startSpeed = 0f; mf.startSize = alto * 3.2f;
            mf.startColor = new Color(0.45f, 1f, 0.4f, 1f);
            var ef = fl.emission; ef.rateOverTime = 0f; ef.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            var sf = fl.shape; sf.enabled = false;
            Tamano(fl, new Keyframe(0f, 0.25f), new Keyframe(0.2f, 1f), new Keyframe(1f, 1.3f));
            Alfa(fl, (1f, 0f), (0.6f, 0.3f), (0f, 1f));
            sistemas.Add((fl.gameObject, TGato));

            var on = Sistema(raiz.transform, "GATO_ONDA", mOnda, Frente(rends[0].bounds, 14f));
            var mo = on.main; mo.duration = 0.2f; mo.loop = false; mo.startLifetime = 0.6f; mo.startSpeed = 0f; mo.startSize = alto * 0.5f;
            mo.startColor = new Color(0.4f, 1f, 0.35f, 0.8f);
            var eo = on.emission; eo.rateOverTime = 0f; eo.SetBursts(new[] { new ParticleSystem.Burst(0f, 1), new ParticleSystem.Burst(0.12f, 1) });
            var so = on.shape; so.enabled = false;
            Tamano(on, new Keyframe(0f, 1f, 0f, 22f), new Keyframe(1f, 9f));
            Alfa(on, (1f, 0f), (0.5f, 0.5f), (0f, 1f));
            sistemas.Add((on.gameObject, TGato));
        }
        // 2c. un chispazo por cada letra de AMISTAD
        for (int i = 1; i < letras.Count; i++)
        {
            float tAp = TLetra0 + (i - 1) * PasoLetra;
            var ps = Sistema(raiz.transform, "CHISPAS_" + letras[i].name.Replace(' ', '_'), mChispa, Frente(rends[i].bounds, 10f));
            var m = ps.main; m.duration = 0.2f; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1.1f); m.startSpeed = new ParticleSystem.MinMaxCurve(50f, 150f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 3f); m.gravityModifier = 5f; m.startColor = verdeBlanco;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = rends[i].bounds.size * 0.8f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 15f; lim.dampen = 0.1f;
            Alfa(ps, (1f, 0f), (1f, 0.5f), (0f, 1f));
            ColorVida(ps, (0f, Color.white), (0.3f, new Color(0.7f, 1f, 0.5f)), (1f, new Color(0.9f, 0.85f, 0.3f)));
            sistemas.Add((ps.gameObject, tAp));
        }
        // 2d. golpe de energía en toda la palabra
        {
            var c = bPalabra.center + haciaCam * 14f;
            var fl = Sistema(raiz.transform, "PALABRA_DESTELLO", mFlash, c);
            var mf = fl.main; mf.duration = 0.2f; mf.loop = false; mf.startLifetime = 0.55f; mf.startSpeed = 0f;
            mf.startSize3D = true; mf.startSizeX = bPalabra.size.x * 1.35f; mf.startSizeY = alto * 2.2f; mf.startSizeZ = 1f;
            mf.startColor = new Color(0.4f, 1f, 0.35f, 0.85f);
            var ef = fl.emission; ef.rateOverTime = 0f; ef.SetBursts(new[] { new ParticleSystem.Burst(0f, 2) });
            var sf = fl.shape; sf.enabled = false;
            Tamano(fl, new Keyframe(0f, 0.6f), new Keyframe(0.15f, 1f), new Keyframe(1f, 1.15f));
            Alfa(fl, (0.9f, 0f), (0.5f, 0.3f), (0f, 1f));
            sistemas.Add((fl.gameObject, TGolpe));

            var ps = Sistema(raiz.transform, "PALABRA_CHISPAS", mChispa, c);
            var m = ps.main; m.duration = 0.3f; m.loop = false; m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(80f, 240f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); m.gravityModifier = 5f; m.startColor = verdeBlanco;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 550) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(bPalabra.size.x, alto * 0.8f, 10f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 18f; lim.dampen = 0.09f;
            Alfa(ps, (1f, 0f), (1f, 0.55f), (0f, 1f));
            ColorVida(ps, (0f, Color.white), (0.25f, new Color(0.7f, 1f, 0.5f)), (1f, new Color(0.9f, 0.85f, 0.3f)));
            sistemas.Add((ps.gameObject, TGolpe));

            var br = Sistema(raiz.transform, "BRASAS_SUBEN", mChispa, c - Vector3.up * alto * 0.6f);
            var mb = br.main; mb.duration = durT - TGolpe; mb.loop = false; mb.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.8f); mb.startSpeed = 0f;
            mb.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.2f); mb.startColor = new Color(0.8f, 1f, 0.6f, 1f);
            var eb = br.emission; eb.rateOverTime = 70f;
            var sb = br.shape; sb.shapeType = ParticleSystemShapeType.Box; sb.scale = new Vector3(bPalabra.size.x * 1.2f, alto * 1.4f, 20f);
            var vb = br.velocityOverLifetime; vb.enabled = true; vb.space = ParticleSystemSimulationSpace.World;
            vb.x = new ParticleSystem.MinMaxCurve(-4f, 4f); vb.y = new ParticleSystem.MinMaxCurve(14f, 34f); vb.z = new ParticleSystem.MinMaxCurve(-2f, 2f);
            var nb = br.noise; nb.enabled = true; nb.strength = 8f; nb.frequency = 0.02f; nb.scrollSpeed = 0.3f; nb.quality = ParticleSystemNoiseQuality.Medium;
            Alfa(br, (0f, 0f), (1f, 0.15f), (0.8f, 0.7f), (0f, 1f));
            sistemas.Add((br.gameObject, TGolpe));
        }
        // 2e. líquido radiactivo que escurre de las letras + gotas que caen + "PRÓXIMAMENTE"
        if (!Escurrir(raiz.transform, letras, rends, bPalabra, alto, haciaCam, clip, durT, mChispa, sistemas, log)) return false;

        var pPart = tlC.CreateTrack<ControlTrack>(grupoTl, PrefijoPista + "chispas");
        uint semilla = 501;
        foreach (var (go, t) in sistemas)
        {
            var tc = pPart.CreateDefaultClip();
            tc.start = IniTitulo - InicioCiudad + t; tc.duration = durT - t; tc.displayName = go.name;
            var ca = (ControlPlayableAsset)tc.asset;
            var er = ca.sourceGameObject; er.exposedName = GUID.Generate().ToString(); er.defaultValue = null; ca.sourceGameObject = er;
            dirC.SetReferenceValue(er.exposedName, go);
            ca.updateParticle = true; ca.particleRandomSeed = semilla++; ca.active = true;
            ca.postPlayback = ActivationControlPlayable.PostPlaybackState.Inactive;
            ca.updateDirector = false; ca.updateITimeControl = false; ca.searchHierarchy = false;
            go.SetActive(false);
            EditorUtility.SetDirty(ca);
        }
        log.AppendLine($"Partículas: {sistemas.Count} sistemas en TITULO_FINAL/TITULO_EPICO (pista '{pPart.name}').");

        // ---------- 3. fundido: entra al instante (el negro ya lo da que no haya letras) y se apaga al final
        var clipFund = AssetDatabase.LoadAssetAtPath<AnimationClip>(Carpeta + "/Titulo_Final_Fundido.anim");
        if (clipFund != null)
        {
            var cf = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.08f, 0f), new Keyframe(IniApagar, 0f),
                                        new Keyframe(IniApagar + (FinApagar - IniApagar) * 0.5f, 0.35f), new Keyframe(FinApagar, 1f), new Keyframe(durT, 1f));
            Lineal(cf);
            Undo.RecordObject(clipFund, "Título épico");
            AnimationUtility.SetEditorCurve(clipFund, EditorCurveBinding.FloatCurve("", typeof(Volume), "weight"), cf);
            EditorUtility.SetDirty(clipFund);
            log.AppendLine($"Fundido: se apaga de {IniApagar} a {FinApagar} s del título y queda en negro.");
        }

        // ---------- 4. golpes de cámara al nacer el # y en el golpe de la palabra
        var clipCam = AssetDatabase.LoadAssetAtPath<AnimationClip>(Carpeta + "/Titulo_Final_Camara.anim");
        if (clipCam != null && cam != null)
        {
            Undo.RecordObject(clipCam, "Título épico");
            // Cámara un poco arriba de las letras, mirando ~11° hacia abajo a un punto bajo la palabra: así el
            // charco se ve como superficie (antes estaba abajo y miraba hacia arriba). Repetible: solo usa la
            // distancia horizontal de los extremos del acercamiento.
            Vector3 Pk(int i) { var v = Vector3.zero; for (int k = 0; k < 3; k++) { var cu0 = AnimationUtility.GetEditorCurve(clipCam, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition." + "xyz"[k])); v[k] = i == 0 ? cu0.keys.First().value : cu0.keys.Last().value; } return v; }
            var mira = grupo.InverseTransformPoint(bPalabra.center - Vector3.up * alto * 0.6f);   // la palabra queda arriba: abajo caen las gotas y va el texto
            Vector3 Elevar(Vector3 pl) { var h = pl - mira; h.y = 0f; return mira + h + Vector3.up * h.magnitude * Mathf.Tan(AnguloCamara * Mathf.Deg2Rad); }
            var ini = Elevar(Pk(0)); var fin = Elevar(Pk(1));
            var rotL = Quaternion.LookRotation(mira - fin, Vector3.up);
            for (int k = 0; k < 4; k++)
                AnimationUtility.SetEditorCurve(clipCam, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation." + "xyzw"[k]), AnimationCurve.Constant(0f, durT, rotL[k]));
            cam.localPosition = ini; cam.localRotation = rotL;
            var arriba = grupo.InverseTransformDirection(cam.up); var lado = grupo.InverseTransformDirection(cam.right);
            for (int k = 0; k < 3; k++)
            {
                var bnd = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition." + "xyz"[k]);
                var cu = AnimationUtility.GetEditorCurve(clipCam, bnd);
                if (cu == null) continue;
                // la curva base es un EaseInOut de 2 claves: se reconstruye y se le suman los golpes
                var k0 = cu.keys.First(); var k1 = cu.keys.Last();
                var baseC = AnimationCurve.EaseInOut(k0.time, ini[k], k1.time, fin[k]);
                var nueva = new AnimationCurve();
                for (float t = 0f; t <= durT + 1e-4f; t += 1f / 30f)
                {
                    float g = Golpe(t, TGato, 4.5f) + Golpe(t, TGolpe, 3f);
                    float gl = Golpe(t + 0.017f, TGato, 3f) * 0.6f;
                    nueva.AddKey(t, baseC.Evaluate(t) + arriba[k] * g + lado[k] * gl);
                }
                AnimationUtility.SetEditorCurve(clipCam, bnd, nueva);
            }
            EditorUtility.SetDirty(clipCam);
            log.AppendLine($"Cámara: a la altura de la palabra ({AnguloCamara}°), un poco abajo para que se vean las gotas; golpe al nacer el # y en el golpe de la palabra.");
        }

        EditorUtility.SetDirty(tlC);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return true;
    }

    /// Líquido radiactivo que SALE POR DEBAJO de las letras: en cada punto donde la letra toca la base (patas de la A,
    /// la M, el I...) una mancha de líquido pegada a la orilla de abajo, un hilo que cuelga y se estira y una gota en la
    /// punta que se suelta y cae; luego se forma otra. Geometría (primitivas) con el shader "Amistad/Gota Radiactiva",
    /// animada con curvas en el clip de las letras (repetible). Además el texto "PRÓXIMAMENTE".
    static bool Escurrir(Transform raiz, List<Transform> letras, List<Renderer> rends, Bounds bPalabra, float alto,
                         Vector3 haciaCam, AnimationClip clip, float durT, Material mChispa, List<(GameObject go, float t)> sistemas, StringBuilder log)
    {
        var frente = Vector3.ProjectOnPlane(haciaCam, Vector3.up).normalized;
        var shG = AssetDatabase.LoadAssetAtPath<Shader>(RutaShaderGoteo);
        if (shG == null) { log.AppendLine("Falta el shader " + RutaShaderGoteo); return false; }
        string rutaMatG = Carpeta + "/Titulo_Goteo.mat";
        if (!AssetDatabase.IsValidFolder(Carpeta + "/Goteo")) AssetDatabase.CreateFolder(Carpeta, "Goteo");
        var matG = AssetDatabase.LoadAssetAtPath<Material>(rutaMatG);
        if (matG == null) { matG = new Material(shG); AssetDatabase.CreateAsset(matG, rutaMatG); }
        matG.shader = shG;
        matG.SetFloat("_Margen", 7f); matG.SetFloat("_Espesor", 4.5f); matG.SetFloat("_SepChorros", 7.5f); matG.SetFloat("_Contorno", 0.8f);
        EditorUtility.SetDirty(matG);
        // el líquido ya no se pinta sobre las caras de las letras (versión anterior)
        var matL = rends[0].sharedMaterial;
        if (matL != null && matL.HasProperty("_Escurrido")) { matL.SetFloat("_Escurrido", 0f); EditorUtility.SetDirty(matL); }

        int total = 0;
        for (int li = 0; li < letras.Count; li++)
        {
            var l = letras[li];
            var malla = l.GetComponent<MeshFilter>()?.sharedMesh;
            if (malla == null) continue;
            if (!malla.isReadable && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(malla)) is ModelImporter mi)
            { mi.isReadable = true; mi.SaveAndReimport(); malla = l.GetComponent<MeshFilter>().sharedMesh; log.AppendLine($"Read/Write activado en {mi.assetPath}."); }
            if (malla == null || !malla.isReadable) { log.AppendLine($"OJO: la malla de '{l.name}' no se puede leer (Read/Write)."); continue; }
            var viejo = l.Find("GOTEO"); if (viejo != null) Undo.DestroyObjectImmediate(viejo.gameObject);
            // lado de la letra que ve la cámara (en espacio de la letra)
            float zCam = Mathf.Sign(l.InverseTransformDirection(haciaCam).z);
            float zMax = malla.bounds.max.z;

            // dónde toca la base: triángulos de la cara de abajo (sus 3 vértices en el piso) -> tramos de X que se juntan
            var vs = malla.vertices; var tris = malla.triangles;
            float yMin = vs.Min(v => v.y);
            var ints = new List<(float a, float b)>();
            for (int q = 0; q < tris.Length; q += 3)
            {
                var v0 = vs[tris[q]]; var v1 = vs[tris[q + 1]]; var v2 = vs[tris[q + 2]];
                if (v0.y > yMin + 0.8f || v1.y > yMin + 0.8f || v2.y > yMin + 0.8f) continue;
                ints.Add((Mathf.Min(v0.x, Mathf.Min(v1.x, v2.x)), Mathf.Max(v0.x, Mathf.Max(v1.x, v2.x))));
            }
            var tramos = new List<(float a, float b)>();
            foreach (var it in ints.OrderBy(x => x.a))
            {
                if (tramos.Count > 0 && it.a <= tramos[tramos.Count - 1].b + 0.5f)
                    tramos[tramos.Count - 1] = (tramos[tramos.Count - 1].a, Mathf.Max(tramos[tramos.Count - 1].b, it.b));
                else tramos.Add(it);
            }
            // un rectángulo por tramo (pata de la letra); la forma del líquido la dibuja el shader
            var rnd = new System.Random(1234 + li * 97);
            float R(float a, float b) => a + (float)rnd.NextDouble() * (b - a);
            var verts = new List<Vector3>(); var uv = new List<Vector2>(); var uv2 = new List<Vector2>(); var uv3 = new List<Vector2>(); var idx = new List<int>();
            const float margen = 7f;                              // igual que _Margen del shader
            foreach (var (a, b) in tramos)
            {
                float w = b - a; if (w < 1f) continue;
                float cx = (a + b) * 0.5f, W = w * 0.5f;
                float L = R(alto * 0.3f, alto * 0.55f);            // largo máximo de los chorros (~16-30 u)
                float altoRect = L * 3.4f;
                float tope = yMin + 0.6f;                         // un poquito adentro de la letra: sale de abajo
                float zq = zCam * (zMax + 0.3f);                  // delante de la cara que ve la cámara
                int b0 = verts.Count;
                verts.Add(new Vector3(cx - W - margen, tope, zq)); verts.Add(new Vector3(cx + W + margen, tope, zq));
                verts.Add(new Vector3(cx + W + margen, tope - altoRect, zq)); verts.Add(new Vector3(cx - W - margen, tope - altoRect, zq));
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                var dat = new Vector2(R(0f, 1f), R(0f, 0.45f)); var med = new Vector2(W, L);
                for (int k = 0; k < 4; k++) { uv2.Add(dat); uv3.Add(med); }
                idx.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 });
                total++;
            }
            if (verts.Count == 0) continue;
            string rutaMalla = $"{Carpeta}/Goteo/Goteo_{l.name.Replace(' ', '_').Replace("#", "Gato")}.asset";
            var mg = AssetDatabase.LoadAssetAtPath<Mesh>(rutaMalla);
            if (mg == null) { mg = new Mesh(); AssetDatabase.CreateAsset(mg, rutaMalla); }
            mg.Clear(); mg.name = "Goteo_" + l.name;
            mg.SetVertices(verts); mg.SetUVs(0, uv); mg.SetUVs(1, uv2); mg.SetUVs(2, uv3); mg.SetTriangles(idx, 0);
            mg.RecalculateBounds(); EditorUtility.SetDirty(mg);

            var goteo = new GameObject("GOTEO"); goteo.transform.SetParent(l, false);   // hijo de la letra: aparece y rebota con ella
            goteo.AddComponent<MeshFilter>().sharedMesh = mg;
            var mrG = goteo.AddComponent<MeshRenderer>(); mrG.sharedMaterial = matG;
            mrG.shadowCastingMode = ShadowCastingMode.Off; mrG.receiveShadows = false;
            mrG.lightProbeUsage = LightProbeUsage.Off; mrG.reflectionProbeUsage = ReflectionProbeUsage.Off;
            // tiempo del goteo: negativo antes de empezar (no dibuja nada); se apaga con las letras
            float t0 = TEscurre + li * 0.06f;
            clip.SetCurve(l.name + "/GOTEO", typeof(MeshRenderer), "material._TiempoGoteo", AnimationCurve.Linear(0f, -t0, durT, durT - t0));
            var cB = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(IniApagar, 1f), new Keyframe(FinApagar, 0.08f), new Keyframe(durT, 0.08f));
            Lineal(cB); clip.SetCurve(l.name + "/GOTEO", typeof(MeshRenderer), "material._Brillo", cB);
        }
        log.AppendLine($"Líquido radiactivo: sale por debajo de las letras en {total} tramos (banda espesa + chorros que gotean), desde {TEscurre} s del título.");
        // "PRÓXIMAMENTE" chiquito en blanco, abajo de la palabra (delante de las gotas)
        var fuente = AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>("Assets/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
        // El texto va dentro de un objeto normal: el RectTransform de TMP fuera de un Canvas regresa su X/Y a 0 al
        // entrar a Play, así que la posición la lleva el padre y el texto queda en (0,0,0) local.
        var txtPadre = new GameObject("PROXIMAMENTE"); txtPadre.transform.SetParent(raiz, false);
        var txtGo = new GameObject("TEXTO"); txtGo.transform.SetParent(txtPadre.transform, false);
        var tmp = txtGo.AddComponent<TMPro.TextMeshPro>();
        if (fuente != null) tmp.font = fuente;
        tmp.text = "PR\u00D3XIMAMENTE";
        // En TextMeshPro 3D el tamaño de fuente va en décimas de unidad (las mayúsculas miden ~0.7 de eso).
        // TITULO_FINAL está apagado al correr la herramienta y TMP no genera la malla para medirla: se calcula directo.
        tmp.fontSize = alto * 0.15f / 0.07f;           // mayúsculas ~15% del alto de las letras del título
        tmp.characterSpacing = 28f; tmp.fontStyle = TMPro.FontStyles.Normal;
        tmp.alignment = TMPro.TextAlignmentOptions.Center; tmp.color = Color.white;
        tmp.textWrappingMode = TMPro.TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(bPalabra.size.x, alto * 0.5f);
        var rt = tmp.rectTransform; rt.anchoredPosition3D = Vector3.zero; rt.localRotation = Quaternion.identity; rt.localScale = Vector3.one;
        txtPadre.transform.position = new Vector3(bPalabra.center.x, bPalabra.min.y - alto * 1.25f, bPalabra.center.z) + frente * (bPalabra.extents.z + alto * 0.6f);
        txtPadre.transform.rotation = Quaternion.LookRotation(-frente, Vector3.up);       // el texto de TMP se lee mirando hacia +z
        var tr = txtGo.GetComponent<MeshRenderer>(); tr.shadowCastingMode = ShadowCastingMode.Off; tr.receiveShadows = false;
        tr.sortingOrder = 10;
        // aparece despacio después del golpe (alfa del color de la cara); el fundido general lo apaga al final
        string rutaTxt = "TITULO_EPICO/PROXIMAMENTE/TEXTO";
        for (int k = 0; k < 4; k++)
        {
            AnimationCurve c = k < 3 ? AnimationCurve.Constant(0f, durT, 1f)
                : new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(TProx, 0f), new Keyframe(TProx + 0.6f, 1f), new Keyframe(durT, 1f));
            Lineal(c);
            clip.SetCurve(rutaTxt, typeof(MeshRenderer), "material._FaceColor." + "rgba"[k], c);
        }
        EditorUtility.SetDirty(clip);
        log.AppendLine($"\"PRÓXIMAMENTE\" aparece a los {TProx} s.");
        return true;
    }

    /// Sacudida que se apaga: amplitud (u) * seno rápido * caída exponencial, 0 antes de t0
    static float Golpe(float t, float t0, float amp)
    {
        float d = t - t0; if (d < 0f || d > 0.6f) return 0f;
        return amp * Mathf.Sin(d * 55f) * Mathf.Exp(-d * 8f);
    }

    static AnimationClip Clip(string ruta)
    {
        var c = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (c == null) { c = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(c, ruta); }
        c.ClearCurves(); c.legacy = false;
        return c;
    }

    static void Lineal(AnimationCurve c)
    {
        for (int k = 0; k < c.length; k++)
        {
            AnimationUtility.SetKeyLeftTangentMode(c, k, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(c, k, AnimationUtility.TangentMode.Linear);
        }
    }

    /// Chispa verde (copia del material de motas del aura con colores verdes, aditivo)
    static Material MatChispa()
    {
        const string ruta = Carpeta + "/Titulo_Chispas.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null)
        {
            var src = AssetDatabase.LoadAssetAtPath<Material>("Assets/Assets/shaders/AuraKi/Aura_Motas_SSJ.mat");
            if (src == null) return null;
            m = new Material(src); AssetDatabase.CreateAsset(m, ruta);
        }
        m.SetColor("_ColorBase", new Color(0.4f, 2.4f, 0.3f));
        m.SetColor("_ColorMedio", new Color(1.6f, 5f, 0.9f));
        m.SetColor("_ColorPunta", new Color(5f, 7f, 4f));
        m.SetFloat("_Intensidad", 1f);
        EditorUtility.SetDirty(m);
        return m;
    }

    static ParticleSystem Sistema(Transform padre, string nombre, Material mat, Vector3 posMundo)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false); go.transform.position = posMundo;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        var m = ps.main; m.loop = false; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.maxParticles = 2000;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = ParticleSystemRenderMode.Billboard;
        r.alignment = ParticleSystemRenderSpace.View;
        r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.maxParticleSize = 3f;
        if (mat.shader != null && mat.shader.name == "Amistad/Fuego VFX")
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
              ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent });
        return ps;
    }

    static void Alfa(ParticleSystem ps, params (float a, float t)[] claves)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  claves.Select(c => new GradientAlphaKey(c.a, c.t)).ToArray());
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    /// Color a lo largo de la vida conservando el alfa que ya puso Alfa()
    static void ColorVida(ParticleSystem ps, params (float t, Color c)[] claves)
    {
        var col = ps.colorOverLifetime; col.enabled = true;
        var g = col.color.gradient ?? new Gradient();
        g.SetKeys(claves.Select(c => new GradientColorKey(c.c, c.t)).ToArray(), g.alphaKeys);
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    static void Tamano(ParticleSystem ps, params Keyframe[] k)
    {
        var s = ps.sizeOverLifetime; s.enabled = true;
        s.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(k));
    }
}
