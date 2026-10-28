using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using BlendMode = UnityEngine.Rendering.BlendMode;

/// <summary>
/// Menú Tools > Amistad > Crear fuego VFX en CITY
///  1. Crea/actualiza los materiales del shader "Amistad/Fuego VFX".
///  2. Arma el prefab Fuego_VFX (llamas, base, brasas, humo, calor y luz que parpadea) a escala 1.
///  3. Lo coloca donde estaban los quads "flama*" de CITY/Escena, con su mismo tamaño, y apaga los quads.
///  4. Agrega ceniza y chispas en el aire, un Volume local con más bloom y una pista de control en el Timeline.
/// Se puede volver a correr: reemplaza lo que creó la vez anterior.
/// </summary>
public static class FuegoVFXEditor
{
    const string Carpeta = "Assets/Assets/shaders/FuegoVFX";
    const string RutaRuido = "Assets/Assets/shaders/TexturaS/Ruido_Fuego.png";
    const string RutaPrefab = Carpeta + "/Fuego_VFX.prefab";
    const string RutaPerfil = Carpeta + "/Volumen_Ciudad.asset";
    const string NombreRaiz = "VFX_CIUDAD";

    static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position,
        ParticleSystemVertexStream.Color,
        ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.StableRandomX,
        ParticleSystemVertexStream.AgePercent
    };

    [MenuItem("Tools/Amistad/Crear fuego VFX en CITY")]
    static void Crear()
    {
        var escena = SceneManager.GetActiveScene();
        var todos = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        var city = todos.FirstOrDefault(t => t.name == "CITY");
        if (city == null) { EditorUtility.DisplayDialog("Fuego VFX", "No encontré el objeto CITY en la escena abierta.", "OK"); return; }
        var shader = Shader.Find("Amistad/Fuego VFX");
        if (shader == null) { EditorUtility.DisplayDialog("Fuego VFX", "No encontré el shader 'Amistad/Fuego VFX'.", "OK"); return; }

        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets/Assets/shaders", "FuegoVFX");
        var ruido = AssetDatabase.LoadAssetAtPath<Texture2D>(RutaRuido);

        // ---------- 1. Materiales ----------
        var matLlamas = CrearMaterial(shader, "Fuego_Llamas", 0, BlendMode.One, BlendMode.One, 3010, ruido, m =>
        {
            m.SetColor("_ColorBase", new Color(1.4f, 0.18f, 0.02f));
            m.SetColor("_ColorMedio", new Color(3.6f, 0.95f, 0.08f));
            m.SetColor("_ColorPunta", new Color(4.5f, 2.4f, 0.6f));
            m.SetFloat("_Intensidad", 0.28f);
            m.SetFloat("_Ancho", 0.6f);
            m.SetFloat("_Distorsion", 0.3f);
            m.SetFloat("_Erosion", 1.1f);
        });
        var matBrasas = CrearMaterial(shader, "Fuego_Brasas", 1, BlendMode.One, BlendMode.One, 3020, ruido, m =>
        {
            m.SetColor("_ColorMedio", new Color(6.0f, 1.6f, 0.2f));
            m.SetColor("_ColorPunta", new Color(10.0f, 5.0f, 1.5f));
        });
        var matHumo = CrearMaterial(shader, "Fuego_Humo", 2, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 3000, ruido, m =>
        {
            m.SetColor("_ColorBase", new Color(0.10f, 0.085f, 0.075f));
        });
        var matCeniza = CrearMaterial(shader, "Fuego_Ceniza", 2, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 3000, ruido, m =>
        {
            m.SetColor("_ColorBase", new Color(0.45f, 0.42f, 0.40f));
            m.SetFloat("_EscalaRuido", 2.5f);
        });
        var matCalor = CrearMaterial(shader, "Fuego_Calor", 3, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, 2990, ruido, m =>
        {
            m.SetFloat("_FuerzaCalor", 0.02f);
        });

        // ---------- 2. Prefab a escala 1 (1 unidad = altura del fuego) ----------
        var prefab = CrearPrefab(matLlamas, matBrasas, matHumo, matCalor);

        // ---------- 3. Colocar en lugar de los quads ----------
        foreach (var previo in todos.Where(t => t != null && t.name == NombreRaiz && t.IsChildOf(city)).ToList())
            if (previo != null) Undo.DestroyObjectImmediate(previo.gameObject);
        todos = todos.Where(t => t != null).ToList();   // quitar lo que se acaba de borrar

        var raiz = new GameObject(NombreRaiz);
        Undo.RegisterCreatedObjectUndo(raiz, "Fuego VFX");
        raiz.transform.SetParent(city, false);
        // Cancelamos la escala 100 de CITY para trabajar en unidades de mundo
        var lc = city.lossyScale;
        raiz.transform.localScale = new Vector3(1f / lc.x, 1f / lc.y, 1f / lc.z);
        raiz.transform.localPosition = Vector3.zero;
        raiz.transform.localRotation = Quaternion.identity;

        var contFuegos = new GameObject("FUEGOS");
        contFuegos.transform.SetParent(raiz.transform, false);

        float sueloY = city.position.y;
        var quads = todos.Where(t => t.name.StartsWith("flama") && t.IsChildOf(city) && t.GetComponent<MeshRenderer>() != null).ToList();
        int n = 0;
        foreach (var q in quads)
        {
            float alto = Mathf.Max(20f, q.position.y + q.lossyScale.y * 0.5f - sueloY);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, contFuegos.transform);
            inst.name = "Fuego_" + q.name.Replace(" ", "_").Replace("(", "").Replace(")", "");
            inst.transform.position = new Vector3(q.position.x, sueloY, q.position.z);
            inst.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            inst.transform.localScale = Vector3.one * alto;
            Undo.RecordObject(q.gameObject, "Apagar quad de flama");
            q.gameObject.SetActive(false);
            n++;
        }

        // ---------- 4a. Ceniza y chispas en el aire ----------
        var memo = todos.FirstOrDefault(t => t.name == "MEMO_PERFIL");
        float altoPersonaje = 20f;
        if (memo != null)
        {
            var rs = memo.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0) { var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); altoPersonaje = Mathf.Clamp(b.size.y, 1f, 500f); }
        }
        var rCity = city.GetComponent<Renderer>();
        Bounds bCity = rCity != null ? rCity.bounds : new Bounds(city.position, new Vector3(1000, 0, 1000));
        CrearAmbiente(raiz.transform, bCity, altoPersonaje, matCeniza, matBrasas);

        // ---------- 4b. Volume local con más bloom solo en la ciudad ----------
        CrearVolumen(raiz.transform, bCity);

        // ---------- 4c. Pista de control en el Timeline de la ciudad ----------
        string infoTimeline = AgregarPistaTimeline(todos, raiz);

        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeGameObject = raiz;
        Debug.Log($"Fuego VFX: {n} fuegos creados (los quads quedaron desactivados). Personaje de referencia = {altoPersonaje:0.#} u. {infoTimeline}");
    }

    // ================= Materiales =================
    static Material CrearMaterial(Shader sh, string nombre, float modo, BlendMode src, BlendMode dst, int cola, Texture ruido, System.Action<Material> extra)
    {
        string ruta = Carpeta + "/" + nombre + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
        else m.shader = sh;
        m.SetFloat("_Modo", modo);
        m.SetFloat("_SrcBlend", (float)src);
        m.SetFloat("_DstBlend", (float)dst);
        if (ruido != null) m.SetTexture("_Ruido", ruido);
        extra?.Invoke(m);
        m.renderQueue = cola;
        EditorUtility.SetDirty(m);
        return m;
    }

    // ================= Prefab =================
    static GameObject CrearPrefab(Material llamas, Material brasas, Material humo, Material calor)
    {
        var root = new GameObject("Fuego_VFX");

        // Llamas principales: miran a la cámara (billboard vertical), así nunca se ven de canto
        var ps = Sistema(root.transform, "Llamas", llamas, ParticleSystemRenderMode.VerticalBillboard);
        {
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 0.8f);
            m.startRotation = new ParticleSystem.MinMaxCurve(-0.14f, 0.14f);
            m.maxParticles = 80;
            var e = ps.emission; e.rateOverTime = 34f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 5f; s.radius = 0.3f; s.rotation = new Vector3(-90f, 0f, 0f);
            Tamano(ps, new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.4f));
            Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(0.75f, 0.7f), new GradientAlphaKey(0f, 1f));
            Ruido(ps, 0.08f, 2f, 0.6f);
        }

        // Base: brillo pegado al suelo
        ps = Sistema(root.transform, "Base", llamas, ParticleSystemRenderMode.VerticalBillboard);
        {
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.7f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.15f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.8f, 1.1f);
            m.startColor = new Color(1f, 1f, 1f, 0.45f);
            m.maxParticles = 20;
            var e = ps.emission; e.rateOverTime = 6f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 0f; s.radius = 0.3f; s.rotation = new Vector3(-90f, 0f, 0f);
            Tamano(ps, new Keyframe(0f, 0.8f), new Keyframe(1f, 1f));
            Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f));
        }

        // Brasas que suben girando
        ps = Sistema(root.transform, "Brasas", brasas, ParticleSystemRenderMode.Billboard);
        {
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.02f);
            m.maxParticles = 50;
            var e = ps.emission; e.rateOverTime = 14f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 20f; s.radius = 0.2f; s.rotation = new Vector3(-90f, 0f, 0f);
            Alfa(ps, new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f));
            Ruido(ps, 0.35f, 1f, 0.5f);
            Velocidad(ps, 0f, 0.2f, 0f);
        }

        // Humo oscuro que sale por encima de las llamas
        ps = Sistema(root.transform, "Humo", humo, ParticleSystemRenderMode.Billboard);
        {
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.4f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.7f, 1.2f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.maxParticles = 40;
            var e = ps.emission; e.rateOverTime = 5f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 10f; s.radius = 0.25f; s.rotation = new Vector3(-90f, 0f, 0f); s.position = new Vector3(0f, 0.9f, 0f);
            Tamano(ps, new Keyframe(0f, 0.6f), new Keyframe(1f, 2.2f));
            Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.2f), new GradientAlphaKey(0f, 1f));
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            Ruido(ps, 0.1f, 1f, 0.3f);
            Velocidad(ps, 0.08f, 0f, 0f);
        }

        // Distorsión de calor (refracta el fondo por encima del fuego)
        ps = Sistema(root.transform, "Calor", calor, ParticleSystemRenderMode.Billboard);
        {
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 2.5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.25f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 1.6f);
            m.maxParticles = 10;
            var e = ps.emission; e.rateOverTime = 2.5f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 5f; s.radius = 0.2f; s.rotation = new Vector3(-90f, 0f, 0f); s.position = new Vector3(0f, 0.9f, 0f);
            Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f));
        }

        // Luz naranja que parpadea e ilumina las ruinas
        var goLuz = new GameObject("Luz");
        goLuz.transform.SetParent(root.transform, false);
        goLuz.transform.localPosition = new Vector3(0f, 0.45f, 0f);
        var luz = goLuz.AddComponent<Light>();
        luz.type = LightType.Point;
        luz.color = new Color(1f, 0.55f, 0.2f);
        luz.shadows = LightShadows.None;
        goLuz.AddComponent<LuzParpadeo>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, RutaPrefab);
        Object.DestroyImmediate(root);
        return prefab;
    }

    static ParticleSystem Sistema(Transform padre, string nombre, Material mat, ParticleSystemRenderMode modo)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.duration = 3f;
        m.loop = true;
        m.prewarm = true;
        m.playOnAwake = true;
        m.startColor = Color.white;
        m.simulationSpace = ParticleSystemSimulationSpace.Local;
        m.scalingMode = ParticleSystemScalingMode.Hierarchy;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = modo;
        r.alignment = ParticleSystemRenderSpace.View;
        r.maxParticleSize = 3f;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.SetActiveVertexStreams(Streams);
        return ps;
    }

    static void Tamano(ParticleSystem ps, params Keyframe[] k)
    {
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(k));
    }

    static void Alfa(ParticleSystem ps, params GradientAlphaKey[] a)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, a);
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    static void Ruido(ParticleSystem ps, float fuerza, float frecuencia, float desplazamiento)
    {
        var n = ps.noise;
        n.enabled = true;
        n.strength = fuerza;
        n.frequency = frecuencia;
        n.scrollSpeed = desplazamiento;
        n.damping = true;
        n.quality = ParticleSystemNoiseQuality.Medium;
    }

    static void Velocidad(ParticleSystem ps, float x, float y, float z)
    {
        var v = ps.velocityOverLifetime;
        v.enabled = true;
        v.space = ParticleSystemSimulationSpace.Local;
        v.x = new ParticleSystem.MinMaxCurve(x);
        v.y = new ParticleSystem.MinMaxCurve(y);
        v.z = new ParticleSystem.MinMaxCurve(z);
    }

    // ================= Ambiente: ceniza que cae y chispas que suben =================
    static void CrearAmbiente(Transform raiz, Bounds bCity, float h, Material ceniza, Material brasas)
    {
        var go = new GameObject("AMBIENTE_CENIZA");
        go.transform.SetParent(raiz, false);
        float altura = Mathf.Max(h * 12f, 150f);
        go.transform.position = new Vector3(bCity.center.x, bCity.center.y + altura * 0.5f, bCity.center.z);

        var ps = Sistema(go.transform, "Ceniza", ceniza, ParticleSystemRenderMode.Billboard);
        {
            var m = ps.main;
            m.scalingMode = ParticleSystemScalingMode.Local;
            m.duration = 14f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(8f, 14f);
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(h * 0.04f, h * 0.09f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.maxParticles = 2500;
            var e = ps.emission; e.rateOverTime = 180f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(bCity.size.x, altura, bCity.size.z);
            Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.15f), new GradientAlphaKey(0.8f, 0.85f), new GradientAlphaKey(0f, 1f));
            Velocidad(ps, h * 0.05f, -h * 0.08f, 0f);
            Ruido(ps, h * 0.1f, 0.02f, 0.2f);
            var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-1f, 1f);
        }

        ps = Sistema(go.transform, "Chispas", brasas, ParticleSystemRenderMode.Billboard);
        {
            var m = ps.main;
            m.scalingMode = ParticleSystemScalingMode.Local;
            m.duration = 8f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 8f);
            m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(h * 0.008f, h * 0.02f);
            m.maxParticles = 600;
            var e = ps.emission; e.rateOverTime = 45f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(bCity.size.x, altura, bCity.size.z);
            Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.1f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f));
            Velocidad(ps, h * 0.03f, h * 0.15f, 0f);
            Ruido(ps, h * 0.2f, 0.03f, 0.3f);
        }
    }

    // ================= Volume local (solo la ciudad) =================
    static void CrearVolumen(Transform raiz, Bounds bCity)
    {
        var perfil = AssetDatabase.LoadAssetAtPath<VolumeProfile>(RutaPerfil);
        if (perfil == null)
        {
            perfil = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(perfil, RutaPerfil);
        }
        T Comp<T>() where T : VolumeComponent
        {
            if (!perfil.TryGet<T>(out var c))
            {
                c = perfil.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, perfil);
            }
            return c;
        }
        var bloom = Comp<Bloom>();
        bloom.active = true;
        bloom.intensity.Override(0.45f);
        bloom.threshold.Override(1f);
        bloom.scatter.Override(0.75f);
        bloom.tint.Override(new Color(1f, 0.85f, 0.7f));
        var ca = Comp<ColorAdjustments>();
        ca.active = true;
        ca.contrast.Override(12f);
        ca.saturation.Override(8f);
        ca.colorFilter.Override(new Color(1f, 0.96f, 0.92f));
        var vig = Comp<Vignette>();
        vig.active = true;
        vig.intensity.Override(0.3f);
        vig.smoothness.Override(0.4f);
        EditorUtility.SetDirty(perfil);
        AssetDatabase.SaveAssets();

        var go = new GameObject("VOLUMEN_CIUDAD");
        go.transform.SetParent(raiz, false);
        float alto = 800f;
        go.transform.position = new Vector3(bCity.center.x, bCity.center.y + alto * 0.5f - 50f, bCity.center.z);
        var box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(bCity.size.x + 200f, alto, bCity.size.z + 200f);
        var vol = go.AddComponent<Volume>();
        vol.isGlobal = false;
        vol.blendDistance = 100f;
        vol.priority = 10f;
        vol.sharedProfile = perfil;
    }

    // ================= Timeline: pista de control para ver el fuego al mover el cabezal =================
    // Se pone en el Timeline maestro (Director_P1_Bosque), alineada con el clip que lanza la ciudad.
    static string AgregarPistaTimeline(List<Transform> todos, GameObject raiz)
    {
        // Limpiar pistas viejas en ambos timelines
        foreach (var nombre in new[] { "Director_P1_Bosque", "Director_P2_Ciudad" })
        {
            var d = todos.FirstOrDefault(t => t.name == nombre)?.GetComponent<PlayableDirector>();
            var ta = d != null ? d.playableAsset as TimelineAsset : null;
            if (ta == null) continue;
            foreach (var vieja in ta.GetOutputTracks().OfType<ControlTrack>().Where(t => t.name == "Fuego VFX").ToList())
                ta.DeleteTrack(vieja);
            EditorUtility.SetDirty(ta);
        }

        var director = todos.FirstOrDefault(t => t.name == "Director_P1_Bosque")?.GetComponent<PlayableDirector>();
        var tl = director != null ? director.playableAsset as TimelineAsset : null;
        if (tl == null) return "No encontré el Timeline de Director_P1_Bosque (agrega una Control Track a mano si quieres).";

        // Buscar cuándo empieza la ciudad dentro del timeline maestro
        double inicio = 0, fin = tl.duration;
        foreach (var tr in tl.GetOutputTracks().OfType<ControlTrack>())
            foreach (var c in tr.GetClips())
            {
                var a = c.asset as ControlPlayableAsset;
                var fuente = a != null ? a.sourceGameObject.Resolve(director) : null;
                if (fuente != null && fuente.name == "Director_P2_Ciudad") { inicio = c.start; fin = c.end; }
            }
        inicio = System.Math.Max(0.0, inicio - 3.0);   // 3 s antes, para que el fuego ya esté prendido

        Undo.RecordObject(tl, "Pista de fuego");
        var pista = tl.CreateTrack<ControlTrack>(null, "Fuego VFX");
        var clip = pista.CreateClip<ControlPlayableAsset>();
        clip.start = inicio;
        clip.duration = System.Math.Max(1.0, fin - inicio);
        clip.displayName = "VFX ciudad";
        var cpa = (ControlPlayableAsset)clip.asset;
        cpa.sourceGameObject.exposedName = GUID.Generate().ToString();
        Undo.RecordObject(director, "Pista de fuego");
        director.SetReferenceValue(cpa.sourceGameObject.exposedName, raiz);
        cpa.updateParticle = true;
        cpa.active = false;
        cpa.updateDirector = false;
        cpa.updateITimeControl = false;
        cpa.searchHierarchy = true;   // "Control Children": busca los Particle Systems dentro de VFX_CIUDAD
        EditorUtility.SetDirty(tl);
        EditorUtility.SetDirty(director);
        AssetDatabase.SaveAssets();
        director.RebuildGraph();
        // Refrescar la ventana de Timeline (por reflexión para no depender del ensamblado del editor de Timeline)
        var tEditor = System.Type.GetType("UnityEditor.Timeline.TimelineEditor, Unity.Timeline.Editor");
        var tRazon = System.Type.GetType("UnityEditor.Timeline.RefreshReason, Unity.Timeline.Editor");
        if (tEditor != null && tRazon != null)
            try { tEditor.GetMethod("Refresh")?.Invoke(null, new object[] { System.Enum.Parse(tRazon, "ContentsAddedOrRemoved") }); } catch { }
        return $"Pista 'Fuego VFX' en {tl.name} de {inicio:0.#} s a {fin:0.#} s.";
    }
}
