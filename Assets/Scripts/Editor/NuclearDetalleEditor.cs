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
/// Tools > Amistad > 3 · Encuentro, saludo y final > Solo: detalle de la explosión nuclear (humo que los traga + temblor)
/// Agrega capas a FINAL_NUCLEAR (las crea FinalTituloEditor.Nuclear) y las pone en el Timeline de la ciudad:
///  - DET_CONDENSACION  nube de condensación (Wilson): cascarón blanco que se abre alrededor de la bola de fuego.
///  - DET_ESCOMBROS     pedazos de ruina que salen volando con estela incandescente.
///  - DET_FUEGO_SUELO   el piso alrededor se prende después de la onda.
///  - DET_FALDA_POLVO   falda de polvo que sube desde la base del hongo.
///  - DET_ANILLO_TALLO  anillo de condensación alrededor del tallo.
///  - DET_TRAGA         pared de humo a ras de piso que rueda hacia afuera y se traga a Ale y Memi (~65.5 s).
///  - DET_LENTE         humo que llega hasta la cámara justo antes del corte a negro (66.4 s).
///  - Temblor de cámara: CinemachineBasicMultiChannelPerlin (6D Shake) en CM8_9_FINAL, con la ganancia animada
///    (golpe en la detonación, segundo golpe cuando llega la onda, retumbe hasta el negro). Pista "Final · temblor".
/// Se puede volver a correr (borra lo suyo primero). FinalTituloEditor la llama sola al rehacer la explosión.
/// </summary>
public static class NuclearDetalleEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string Carpeta = "Assets/Animaciones/Saludo";
    const float InicioCiudad = 17.5f;
    const float Explosion = 63.4f, FinClips = 67.5f;
    const string Pt = " · ";
    const string PistaDetalle = "Final · explosión detalle";
    const string PistaTemblor = "Final · temblor";

    // temblor (segundos desde la detonación, ganancia sobre el perfil 6D Shake)
    static readonly (float t, float v)[] Temblor =
    {
        (0f, 0f), (0.03f, 48f), (0.3f, 24f), (0.9f, 12f),    // golpe del destello  (48 ≈ ±3-6° de cabeceo)
        (1.25f, 12f), (1.4f, 42f), (2.0f, 22f),               // llega la onda
        (2.6f, 18f), (3.0f, 20f), (3.2f, 0f), (4.1f, 0f)      // retumbe hasta el negro (66.4 = 3.0 s)
    };

    const float FrecuenciaTemblor = 0.55f;   // < 1 = sacudidas más lentas y amplias (el 6D Shake a 1 es vibración fina)

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Solo: detalle de la explosión nuclear (humo y temblor)")]
    static void Menu()
    {
        var log = new StringBuilder();
        var nuc = Buscar("FINAL_NUCLEAR");
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dirC = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.playableAsset == tlC);
        if (nuc == null || tlC == null || dirC == null) { Debug.LogError("[Nuclear detalle] Falta FINAL_NUCLEAR, el Timeline de la ciudad o su Director. Corre primero 'Solo: final'."); return; }
        var grupoC = tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "8" + Pt + "Saludo");
        Agregar(nuc, tlC, dirC, grupoC, log);
        EditorUtility.SetDirty(tlC);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(nuc.gameObject.scene);
        Debug.Log("[Nuclear detalle]\n" + log);
    }

    static Transform Buscar(string nombre) =>
        Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == nombre && t.gameObject.scene.IsValid());

    public static void Agregar(Transform nuc, TimelineAsset tlC, PlayableDirector dirC, TrackAsset grupoC, StringBuilder log)
    {
        // limpia lo anterior
        foreach (Transform h in nuc.Cast<Transform>().Where(x => x.name.StartsWith("DET_")).ToList()) Undo.DestroyObjectImmediate(h.gameObject);
        foreach (var vieja in tlC.GetOutputTracks().Where(x => x.name == PistaDetalle || x.name == PistaTemblor).ToList()) tlC.DeleteTrack(vieja);

        var cam = Buscar("CM8_9_FINAL");
        var haciaCam = Vector3.right;
        if (cam != null) { haciaCam = cam.position - nuc.position; haciaCam.y = 0; haciaCam.Normalize(); }
        float distCam = cam != null ? Vector3.Distance(new Vector3(cam.position.x, 0, cam.position.z), nuc.position) : 136f;

        var sistemas = Capas(nuc, haciaCam, distCam, log);
        if (sistemas == null) return;

        double ini = Explosion - InicioCiudad, dur = FinClips - Explosion;
        var pista = tlC.CreateTrack<ControlTrack>(grupoC, PistaDetalle);
        foreach (var go in sistemas)
        {
            var tc = pista.CreateDefaultClip(); tc.start = ini; tc.duration = dur; tc.displayName = go.name;
            var ca = (ControlPlayableAsset)tc.asset;
            var er = ca.sourceGameObject; if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
            er.defaultValue = null; ca.sourceGameObject = er;
            ca.updateParticle = true; ca.particleRandomSeed = (uint)(go.name.GetHashCode() & 0x7fffffff); ca.active = true;
            ca.updateDirector = false; ca.updateITimeControl = false; ca.searchHierarchy = false;
            dirC.SetReferenceValue(er.exposedName, go);
            go.SetActive(false);
            EditorUtility.SetDirty(ca);
        }
        EditorUtility.SetDirty(pista);
        log.AppendLine($"{sistemas.Length} capas nuevas en FINAL_NUCLEAR ({string.Join(", ", sistemas.Select(s => s.name))}).");

        // ---------- temblor de cámara
        if (cam != null)
        {
            var perlin = cam.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (perlin == null) perlin = Undo.AddComponent<CinemachineBasicMultiChannelPerlin>(cam.gameObject);
            Undo.RecordObject(perlin, "Temblor");
            perlin.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>("Packages/com.unity.cinemachine/Presets/Noise/6D Shake.asset");
            perlin.AmplitudeGain = 0f; perlin.FrequencyGain = FrecuenciaTemblor;
            EditorUtility.SetDirty(perlin);
            var an = cam.GetComponent<Animator>();
            var clip = Clip($"{Carpeta}/Final_Temblor.anim", typeof(CinemachineBasicMultiChannelPerlin), "AmplitudeGain", Temblor);
            var pt = tlC.CreateTrack<AnimationTrack>(grupoC, PistaTemblor);
            var tca = pt.CreateClip(clip); tca.start = ini; tca.duration = dur; tca.displayName = "Final_Temblor";
            if (tca.asset is AnimationPlayableAsset ap) ap.removeStartOffset = false;
            pt.trackOffset = TrackOffset.ApplySceneOffsets;
            dirC.SetGenericBinding(pt, an);
            EditorUtility.SetDirty(pt);
            log.AppendLine("Temblor en CM8_9_FINAL (6D Shake): golpe de 48 en la detonación, 42 cuando llega la onda (+1.4 s), retumbe hasta el negro.");
        }
        else log.AppendLine("No encontré CM8_9_FINAL: sin temblor.");
    }

    // ================================================================= capas
    static Material Mat(string ruta) => AssetDatabase.LoadAssetAtPath<Material>(ruta);

    static ParticleSystem Sistema(Transform padre, string nombre, Material mat, ParticleSystemRenderMode modo = ParticleSystemRenderMode.Billboard)
    {
        var go = new GameObject(nombre); Undo.RegisterCreatedObjectUndo(go, "Nuclear detalle");
        go.transform.SetParent(padre, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.maxParticles = 4000;
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

    static Material MatEscombro()
    {
        string ruta = Carpeta + "/Final_Escombro.mat";
        var m = Mat(ruta);
        if (m != null) return m;
        var sh = Shader.Find("Universal Render Pipeline/Lit");
        m = new Material(sh) { name = "Final_Escombro" };
        m.SetColor("_BaseColor", new Color(0.09f, 0.08f, 0.075f));
        m.SetFloat("_Smoothness", 0.1f);
        AssetDatabase.CreateAsset(m, ruta);
        return m;
    }

    static GameObject[] Capas(Transform nuc, Vector3 haciaCam, float distCam, StringBuilder log)
    {
        var ceniza = Mat("Assets/Assets/shaders/FuegoVFX/Fuego_Ceniza.mat");
        var llamas = Mat("Assets/Assets/shaders/FuegoVFX/Fuego_Llamas.mat");
        var estallido = Mat(Carpeta + "/Saludo_Estallido.mat");
        if (ceniza == null || llamas == null || estallido == null) { log.AppendLine("Faltan Fuego_Ceniza, Fuego_Llamas o Saludo_Estallido."); return null; }
        var lista = new List<GameObject>();
        Color Polvo = new Color(0.42f, 0.33f, 0.26f, 1f), Hollin = new Color(0.2f, 0.16f, 0.14f, 1f);

        // nube de condensación (Wilson): cascarón blanco que se abre muy rápido alrededor de la bola de fuego
        {
            var ps = Sistema(nuc, "DET_CONDENSACION", ceniza);
            var m = ps.main; m.duration = 0.2f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.75f); m.startSpeed = new ParticleSystem.MinMaxCurve(170f, 230f);
            m.startSize = new ParticleSystem.MinMaxCurve(16f, 26f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0.1f, 320) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 18f; sh.radiusThickness = 0f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 20f; lim.dampen = 0.12f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 2.2f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 1, 1, 0f)), (0.12f, new Color(1, 0.95f, 0.88f, 0.3f)), (0.5f, new Color(0.9f, 0.85f, 0.8f, 0.12f)), (1f, new Color(0.9f, 0.88f, 0.85f, 0f)));
            ps.transform.localPosition = Vector3.up * 18f; lista.Add(ps.gameObject);
        }
        // escombros que salen volando con estela incandescente
        {
            var ps = Sistema(nuc, "DET_ESCOMBROS", MatEscombro(), ParticleSystemRenderMode.Mesh);
            var r = ps.GetComponent<ParticleSystemRenderer>();
            var cubo = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            r.mesh = cubo; r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; r.trailMaterial = estallido;
            r.SetActiveVertexStreams(new List<ParticleSystemVertexStream> { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Normal, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV });
            var m = ps.main; m.duration = 0.3f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(90f, 210f);
            m.startSize3D = true;
            m.startSizeX = new ParticleSystem.MinMaxCurve(0.4f, 1.8f); m.startSizeY = new ParticleSystem.MinMaxCurve(0.3f, 1.2f); m.startSizeZ = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
            m.startRotation3D = true;
            m.startRotationX = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.startRotationY = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.startRotationZ = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            m.gravityModifier = 7f; m.startColor = Color.white;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0.03f, 110), new ParticleSystem.Burst(0.12f, 50) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 55f; sh.radius = 10f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var rol = ps.rotationOverLifetime; rol.enabled = true; rol.separateAxes = true;
            rol.x = new ParticleSystem.MinMaxCurve(-6f, 6f); rol.y = new ParticleSystem.MinMaxCurve(-6f, 6f); rol.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            var tr = ps.trails; tr.enabled = true; tr.ratio = 0.35f; tr.lifetime = new ParticleSystem.MinMaxCurve(0.08f, 0.14f); tr.minVertexDistance = 1f;
            tr.widthOverTrail = new ParticleSystem.MinMaxCurve(0.6f, AnimationCurve.Linear(0, 1f, 1, 0f)); tr.sizeAffectsWidth = true; tr.dieWithParticles = true;
            tr.inheritParticleColor = false;
            tr.colorOverLifetime = new ParticleSystem.MinMaxGradient(Grad((0f, new Color(1f, 0.75f, 0.35f, 1f)), (0.5f, new Color(1f, 0.35f, 0.08f, 0.7f)), (1f, new Color(0.5f, 0.1f, 0f, 0f))));
            var col = ps.collision; col.enabled = true; col.type = ParticleSystemCollisionType.Planes;
            ps.transform.localPosition = Vector3.up * 6f; lista.Add(ps.gameObject);
            // plano de choque = el piso (y = 0)
            var piso = new GameObject("DET_ESCOMBROS_PISO"); Undo.RegisterCreatedObjectUndo(piso, "Nuclear detalle");
            piso.transform.SetParent(ps.transform, false); piso.transform.position = new Vector3(nuc.position.x, 0.5f, nuc.position.z);
            col.SetPlane(0, piso.transform); col.dampen = 0.6f; col.bounce = 0.25f; col.lifetimeLoss = 0.1f;
        }
        // el piso alrededor se prende (anillo de fuego que deja la onda)
        {
            var ps = Sistema(nuc, "DET_FUEGO_SUELO", llamas);
            var m = ps.main; m.duration = 3.4f; m.startDelay = 0.35f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.3f); m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 12f);
            m.startSize = new ParticleSystem.MinMaxCurve(7f, 16f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.6f;
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 260f), new Keyframe(0.5f, 180f), new Keyframe(1f, 90f)));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 70f; sh.radiusThickness = 0.75f; sh.rotation = new Vector3(90f, 0f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(10f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1, 0.6f)));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 0.9f, 0.6f, 1)), (0.5f, new Color(1, 0.5f, 0.15f, 0.8f)), (1f, new Color(0.7f, 0.15f, 0.05f, 0f)));
            ps.transform.localPosition = Vector3.up * 1f; lista.Add(ps.gameObject);
        }
        // falda de polvo que sube desde la base del hongo
        {
            var ps = Sistema(nuc, "DET_FALDA_POLVO", ceniza);
            var m = ps.main; m.duration = 2.4f; m.startDelay = 0.3f; m.startLifetime = new ParticleSystem.MinMaxCurve(3.5f, 4.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(18f, 38f);
            m.startSize = new ParticleSystem.MinMaxCurve(26f, 44f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.12f;
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 160f, 1, 50f));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 70f; sh.radius = 22f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 6f; lim.dampen = 0.05f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.8f, 1, 2.4f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(0.9f, 0.5f, 0.25f, 0f)), (0.12f, new Color(0.6f, 0.42f, 0.3f, 0.75f)), (0.6f, new Color(Polvo.r, Polvo.g, Polvo.b, 0.6f)), (1f, new Color(Polvo.r, Polvo.g, Polvo.b, 0f)));
            lista.Add(ps.gameObject);
        }
        // anillo de condensación alrededor del tallo (gira y se abre)
        {
            var ps = Sistema(nuc, "DET_ANILLO_TALLO", ceniza);
            var m = ps.main; m.duration = 1.0f; m.startDelay = 0.9f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 2.8f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(14f, 24f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = 150f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 20f; sh.radiusThickness = 0.05f; sh.rotation = new Vector3(90f, 0f, 0f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f); vel.y = new ParticleSystem.MinMaxCurve(6f); vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0.9f); vel.radial = new ParticleSystem.MinMaxCurve(9f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 0.95f, 0.9f, 0f)), (0.25f, new Color(0.62f, 0.5f, 0.42f, 0.32f)), (0.7f, new Color(0.42f, 0.35f, 0.3f, 0.2f)), (1f, new Color(0.35f, 0.3f, 0.27f, 0f)));
            ps.transform.localPosition = Vector3.up * 75f; lista.Add(ps.gameObject);
        }
        // pared de humo a ras de piso que rueda hacia afuera y se traga a Ale y Memi
        {
            var ps = Sistema(nuc, "DET_TRAGA", ceniza);
            var m = ps.main; m.duration = 2.0f; m.startDelay = 0.9f; m.startLifetime = new ParticleSystem.MinMaxCurve(4.5f, 6f); m.startSpeed = new ParticleSystem.MinMaxCurve(55f, 90f);
            m.startSize = new ParticleSystem.MinMaxCurve(30f, 55f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.04f;
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 420f, 1, 240f));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 18f; sh.radiusThickness = 0.3f; sh.rotation = new Vector3(90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 12f; lim.dampen = 0.035f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0f, 0f); vel.y = new ParticleSystem.MinMaxCurve(4f, 12f); vel.z = new ParticleSystem.MinMaxCurve(0f, 0f);   // los 3 en el mismo modo
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.8f, 1, 3.0f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(0.95f, 0.55f, 0.25f, 0f)), (0.08f, new Color(0.55f, 0.38f, 0.27f, 0.95f)), (0.5f, new Color(0.36f, 0.29f, 0.24f, 0.95f)), (1f, new Color(Hollin.r, Hollin.g, Hollin.b, 0.7f)));
            ps.transform.localPosition = Vector3.up * 6f; lista.Add(ps.gameObject);
        }
        // humo que alcanza la cámara justo antes del corte a negro
        {
            var ps = Sistema(nuc, "DET_LENTE", ceniza);
            var m = ps.main; m.duration = 1.2f; m.startDelay = 1.9f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.0f, 3.0f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(35f, 70f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 90f, 1, 220f));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(160f, 50f, distCam * 0.55f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(-6f, 6f); vel.y = new ParticleSystem.MinMaxCurve(2f, 10f); vel.z = new ParticleSystem.MinMaxCurve(35f, 55f);
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 1.8f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(0.5f, 0.38f, 0.3f, 0f)), (0.25f, new Color(0.42f, 0.33f, 0.27f, 0.9f)), (1f, new Color(Hollin.r, Hollin.g, Hollin.b, 0.95f)));
            ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 3f;
            ps.transform.position = nuc.position + haciaCam * (distCam * 0.45f) + Vector3.up * 25f;
            ps.transform.rotation = Quaternion.LookRotation(haciaCam, Vector3.up);
            lista.Add(ps.gameObject);
        }
        return lista.ToArray();
    }

    static AnimationClip Clip(string ruta, System.Type tipo, string prop, (float t, float v)[] k)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (clip == null) { clip = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clip, ruta); }
        clip.ClearCurves();
        var cu = new AnimationCurve(k.Select(x => new Keyframe(x.t, x.v)).ToArray());
        for (int i = 0; i < cu.length; i++) { AnimationUtility.SetKeyLeftTangentMode(cu, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(cu, i, AnimationUtility.TangentMode.Linear); }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", tipo, prop), cu);
        EditorUtility.SetDirty(clip);
        return clip;
    }
}
