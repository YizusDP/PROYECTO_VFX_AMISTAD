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
using BlendMode = UnityEngine.Rendering.BlendMode;

/// <summary>
/// Tools > Amistad > Ciudad: caos (fuego, polvo, suciedad, radiación)
/// Llena la ciudad ampliada de caos, por zonas sobre la avenida por donde camina Ale (X = X0, hacia -Z):
///   ZONA FUEGO (z -470 a -1100): escombros ardiendo a la orilla de la calle y edificios en llamas (prefab Fuego_VFX).
///   ZONA POLVO (z -900 a -1400): nube espesa, polvo rasante y polvo que cae de los edificios.
///   ZONA RADIACTIVA (z -1300 a -2000): charcos con shader propio "Amistad/Charco Radiactivo", motas, vapor y luz verde.
///   TODA LA CIUDAD: escombros (piezas del derrumbe), manchas de hollín (shader "Amistad/Mancha Suelo", blend multiplicativo),
///   incendios lejanos con columnas de humo gigantes.
/// Todo queda en CITY/CAOS_CIUDAD y se controla con la pista "Caos ciudad" del Timeline de la ciudad.
/// También agranda VOLUMEN_CIUDAD (bloom/color de la ciudad) para que cubra la ciudad ampliada.
/// Se puede volver a correr (semilla fija).
/// </summary>
public static class CiudadCaosEditor
{
    const string NombreRaiz = "CAOS_CIUDAD";
    const string Carpeta = "Assets/Assets/shaders/Caos";
    const string RutaFuego = "Assets/Assets/shaders/FuegoVFX/Fuego_Toon.prefab";   // estilo caricatura (FuegoToonEditor); antes Fuego_VFX.prefab
    const string RutaHumo = "Assets/Assets/shaders/FuegoVFX/Fuego_Humo.mat";
    const string RutaRuido = "Assets/Assets/shaders/TexturaS/Ruido_Fuego.png";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string NombrePista = "Caos ciudad";
    const float X0 = -29f;      // centro de la avenida (mundo)
    const float Carril = 14f;   // medio ancho que se deja libre para que caminen los personajes
    const int Semilla = 777;

    static System.Random rng;
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static int Lado() => rng.Next(2) == 0 ? -1 : 1;

    static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position,
        ParticleSystemVertexStream.Color,
        ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.StableRandomX,
        ParticleSystemVertexStream.AgePercent
    };

    [MenuItem("Tools/Amistad/Ciudad: caos (fuego, polvo, suciedad, radiación)")]
    static void Crear()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Caos] Sal de Play primero."); return; }
        var city = GameObject.Find("CITY");
        if (city == null) { Debug.LogError("[Caos] No encontré CITY."); return; }

        var fuegoPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RutaFuego);
        var matHumo = AssetDatabase.LoadAssetAtPath<Material>(RutaHumo);
        var ruido = AssetDatabase.LoadAssetAtPath<Texture2D>(RutaRuido);
        var shFuego = Shader.Find("Amistad/Fuego VFX");
        var shMancha = Shader.Find("Amistad/Mancha Suelo");
        var shCharco = Shader.Find("Amistad/Charco Radiactivo");
        if (fuegoPrefab == null || matHumo == null || shFuego == null || shMancha == null || shCharco == null)
        {
            Debug.LogError($"[Caos] Falta algo: prefab fuego {fuegoPrefab != null}, humo {matHumo != null}, shader fuego {shFuego != null}, mancha {shMancha != null}, charco {shCharco != null}");
            return;
        }

        var previo = city.transform.Find(NombreRaiz);
        if (previo != null) Undo.DestroyObjectImmediate(previo.gameObject);
        rng = new System.Random(Semilla);
        var log = new StringBuilder();

        // ---------- Materiales ----------
        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets/Assets/shaders", "Caos");
        var matPolvo = Mat(shFuego, "Caos_Polvo", 3000, m =>
        {
            m.SetFloat("_Modo", 2f); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetColor("_ColorBase", new Color(0.62f, 0.52f, 0.42f)); m.SetFloat("_EscalaRuido", 1.3f); m.SetFloat("_Velocidad", 0.5f);
            m.SetFloat("_SuavidadSuelo", 10f); if (ruido != null) m.SetTexture("_Ruido", ruido);
        });
        var matMotas = Mat(shFuego, "Caos_MotasRadiactivas", 3020, m =>
        {
            m.SetFloat("_Modo", 1f); m.SetFloat("_SrcBlend", (float)BlendMode.One); m.SetFloat("_DstBlend", (float)BlendMode.One);
            m.SetColor("_ColorMedio", new Color(0.5f, 4f, 0.4f)); m.SetColor("_ColorPunta", new Color(2.5f, 10f, 1.5f));
            m.SetFloat("_Intensidad", 1f); m.SetFloat("_SuavidadSuelo", 2f); if (ruido != null) m.SetTexture("_Ruido", ruido);
        });
        var matVapor = Mat(shFuego, "Caos_VaporRadiactivo", 3005, m =>
        {
            m.SetFloat("_Modo", 2f); m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            m.SetColor("_ColorBase", new Color(0.35f, 1.6f, 0.3f)); m.SetFloat("_EscalaRuido", 1.6f); m.SetFloat("_Velocidad", 0.8f);
            m.SetFloat("_SuavidadSuelo", 4f); if (ruido != null) m.SetTexture("_Ruido", ruido);
        });
        var matMancha = Mat(shMancha, "Caos_Mancha", -1, m => { if (ruido != null) m.SetTexture("_Ruido", ruido); });
        var matCharco = Mat(shCharco, "Caos_Charco", -1, m => { if (ruido != null) m.SetTexture("_Ruido", ruido); });
        AssetDatabase.SaveAssets();

        // ---------- Raíz (unidades de mundo) ----------
        var raiz = new GameObject(NombreRaiz);
        raiz.transform.SetParent(city.transform, false);
        raiz.transform.localScale = Vector3.one / city.transform.localScale.x;
        var gFuego = Hijo(raiz.transform, "FUEGOS");
        var gHumo = Hijo(raiz.transform, "COLUMNAS_HUMO");
        var gPolvo = Hijo(raiz.transform, "POLVO");
        var gSuc = Hijo(raiz.transform, "SUCIEDAD");
        var gEsc = Hijo(gSuc, "ESCOMBROS");
        var gMan = Hijo(gSuc, "MANCHAS");
        var gRad = Hijo(raiz.transform, "RADIACTIVO");
        var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

        var amp = city.transform.Find("CIUDAD_AMPLIADA");
        var bloquesAv = Hijos(amp, "AVENIDA_ALE");
        var bloquesLejos = Hijos(amp, "ANILLO_CERCA").Concat(Hijos(amp, "ANILLO_LEJOS")).ToList();
        if (amp == null) log.AppendLine("OJO: no hay CIUDAD_AMPLIADA (corre primero 'Ciudad: ampliar').");

        // ================= FUEGO =================
        var fuegosCerca = new List<(Vector3 p, float alto)>();
        int nF = 0;
        foreach (float z in new[] { -500f, -585f, -690f, -770f, -860f, -960f, -1120f })
        {
            var p = new Vector3(X0 + Lado() * R(48f, 80f), 0f, z + R(-25f, 25f));
            float alto = R(22f, 45f);
            Fuego(fuegoPrefab, gFuego, $"Fuego_Calle_{nF++:00}", p, alto, true);
            fuegosCerca.Add((p, alto));
        }
        foreach (var b in bloquesAv.Where(q => AABB(q.gameObject).center.z > -1500f).OrderBy(_ => rng.Next()).Take(5))
        {
            var bb = AABB(b.gameObject);
            float lado = Mathf.Sign(bb.center.x - X0);
            var p = new Vector3(Mathf.Lerp(bb.center.x, X0 + lado * 60f, 0.45f), 0f, bb.center.z + R(-40f, 40f));
            float alto = R(110f, 190f);
            Fuego(fuegoPrefab, gFuego, $"Fuego_Edificio_{nF++:00}", p, alto, true);
            fuegosCerca.Add((p, alto * 0.4f));
        }
        int nH = 0;
        foreach (var b in bloquesLejos.OrderBy(_ => rng.Next()).Take(8))
        {
            var bb = AABB(b.gameObject);
            var p = new Vector3(bb.center.x + R(-60f, 60f), 0f, bb.center.z + R(-60f, 60f));
            Fuego(fuegoPrefab, gFuego, $"Fuego_Lejos_{nF++:00}", p, R(160f, 300f), false);
            Columna(gHumo, $"Columna_{nH++:00}", p, R(140f, 230f), matHumo);
        }
        // un par de columnas extra al fondo de la avenida (se ven en el contraplano de Ale)
        foreach (var p in new[] { new Vector3(X0 + 380f, 0f, -2600f), new Vector3(X0 - 520f, 0f, -3000f), new Vector3(X0 + 60f, 0f, -3400f) })
        {
            Fuego(fuegoPrefab, gFuego, $"Fuego_Lejos_{nF++:00}", p, R(200f, 320f), false);
            Columna(gHumo, $"Columna_{nH++:00}", p, R(180f, 260f), matHumo);
        }
        log.AppendLine($"Fuegos: {nF} · columnas de humo: {nH}");

        // ================= POLVO =================
        {
            var ps = Sistema(gPolvo, "Polvo_Rasante", matPolvo, new Vector3(X0, 2f, -1150f));
            var m = ps.main;
            m.duration = 10f; m.startLifetime = new ParticleSystem.MinMaxCurve(9f, 14f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(10f, 24f); m.maxParticles = 300;
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new Color(1f, 1f, 1f, 0.22f);
            var e = ps.emission; e.rateOverTime = 24f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(220f, 3f, 800f);
            Alfa(ps, (0f, 0f), (1f, 0.25f), (1f, 0.75f), (0f, 1f));
            Tamano(ps, new Keyframe(0f, 0.7f), new Keyframe(1f, 1.4f));
            Velocidad(ps, 5f, 0.3f, 1f);
            Ruido(ps, 4f, 0.03f, 0.2f);
        }
        {
            var ps = Sistema(gPolvo, "Polvo_Nube_Zona", matPolvo, new Vector3(X0, 55f, -1200f));
            var m = ps.main;
            m.duration = 12f; m.startLifetime = new ParticleSystem.MinMaxCurve(12f, 18f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(50f, 100f); m.maxParticles = 140;
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.85f, 0.8f, 0.12f), new Color(1f, 0.95f, 0.88f, 0.2f));
            var e = ps.emission; e.rateOverTime = 8f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(320f, 60f, 520f);
            Alfa(ps, (0f, 0f), (1f, 0.2f), (1f, 0.7f), (0f, 1f));
            Tamano(ps, new Keyframe(0f, 0.8f), new Keyframe(1f, 1.3f));
            Velocidad(ps, 3f, 0.5f, 0.5f);
            Ruido(ps, 3f, 0.02f, 0.15f);
        }
        int nC = 0;
        foreach (var b in bloquesAv.Where(q => { var zq = AABB(q.gameObject).center.z; return zq < -800f && zq > -1700f; }).Take(6))
        {
            var bb = AABB(b.gameObject);
            float lado = Mathf.Sign(bb.center.x - X0);
            float bordeX = lado > 0 ? bb.min.x : bb.max.x;
            var p = new Vector3(bordeX - lado * 4f, R(50f, 100f), Mathf.Clamp(bb.center.z + R(-60f, 60f), bb.min.z + 20f, bb.max.z - 20f));
            var ps = Sistema(gPolvo, $"Polvo_Cae_{nC++:00}", matPolvo, p);
            var m = ps.main;
            m.duration = 6f; m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 5f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f); m.gravityModifier = 0.25f;
            m.startSize = new ParticleSystem.MinMaxCurve(5f, 12f); m.maxParticles = 60;
            m.startColor = new Color(1f, 0.95f, 0.9f, 0.55f);
            var e = ps.emission; e.rateOverTime = 9f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 15f; s.radius = 6f; s.rotation = new Vector3(90f, 0f, 0f);
            Alfa(ps, (0f, 0f), (1f, 0.15f), (0.6f, 0.6f), (0f, 1f));
            Tamano(ps, new Keyframe(0f, 0.5f), new Keyframe(1f, 3f));
            Ruido(ps, 1.5f, 0.1f, 0.3f);
        }
        log.AppendLine($"Polvo: rasante, nube de zona y {nC} chorros que caen de edificios");

        // ================= SUCIEDAD: escombros =================
        var derrumbe = city.transform.Find("DERRUMBE_ruim008");
        var piezas = derrumbe != null
            ? derrumbe.GetComponentsInChildren<MeshFilter>(true).Where(m => m.name.StartsWith("pieza") && m.sharedMesh != null).ToList()
            : new List<MeshFilter>();
        int nE = 0;
        if (piezas.Count > 0)
        {
            for (int i = 0; i < 170; i++)
            {
                var src = piezas[rng.Next(piezas.Count)];
                float z = R(-2100f, -230f);
                float dx = Lado() * R(Carril, 140f);
                float objetivo = rng.NextDouble() < 0.12 ? R(14f, 26f) : R(3f, 12f);
                var go = new GameObject($"Escombro_{i:000}");
                go.transform.SetParent(gEsc, false);
                go.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
                var mr = go.AddComponent<MeshRenderer>();
                var srcR = src.GetComponent<MeshRenderer>();
                if (srcR != null) mr.sharedMaterials = srcR.sharedMaterials;
                float tamMesh = Mathf.Max(0.01f, src.sharedMesh.bounds.size.magnitude);
                go.transform.localScale = Vector3.one * (objetivo / tamMesh);
                go.transform.rotation = Quaternion.Euler(R(0f, 360f), R(0f, 360f), R(0f, 360f));
                go.transform.position = Vector3.zero;
                var bb = AABB(go);
                go.transform.position = new Vector3(X0 + dx - bb.center.x, -bb.min.y - objetivo * 0.15f, z - bb.center.z);
                // no tapar el carril de los personajes
                var bb2 = AABB(go);
                if (bb2.max.x > X0 - Carril && bb2.min.x < X0 + Carril) go.transform.position += new Vector3(Mathf.Sign(dx) * (Carril + bb2.extents.x - Mathf.Abs(bb2.center.x - X0)), 0f, 0f);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                nE++;
            }
        }
        else log.AppendLine("OJO: no encontré piezas del derrumbe para los escombros.");

        // ================= SUCIEDAD: manchas de hollín =================
        int nM = 0;
        foreach (var f in fuegosCerca)
        {
            float tam = Mathf.Clamp(f.alto * R(1.6f, 2.4f), 25f, 110f);
            Plano(gMan, $"Mancha_Fuego_{nM++:00}", quad, matMancha, f.p + new Vector3(R(-5f, 5f), 0f, R(-5f, 5f)), tam, 0.06f + nM * 0.001f);
        }
        for (int i = 0; i < 50; i++)
        {
            var p = new Vector3(X0 + R(-110f, 110f), 0f, R(-2000f, -200f));
            Plano(gMan, $"Mancha_{nM++:00}", quad, matMancha, p, R(12f, 55f), 0.06f + nM * 0.001f);
        }
        log.AppendLine($"Suciedad: {nE} escombros, {nM} manchas de hollín");

        // ================= RADIACTIVO =================
        var charcos = new (float z, int lado, float r)[]
        {
            (-545f, 1, 14f), (-735f, -1, 18f), (-1040f, 1, 22f), (-1240f, -1, 16f), (-1350f, 1, 11f),
            (-1480f, 1, 26f), (-1690f, -1, 20f), (-1830f, -1, 12f), (-1960f, 1, 30f)
        };
        int nR = 0;
        foreach (var c in charcos)
        {
            var p = new Vector3(X0 + c.lado * (c.r * 0.6f + R(20f, 35f)), 0f, c.z + R(-20f, 20f));
            var g = Hijo(gRad, $"Charco_{nR++:00}");
            g.position = p;
            Plano(g, "Superficie", quad, matCharco, p, c.r * 2f, 0.1f);

            var motas = Sistema(g, "Motas", matMotas, p + Vector3.up * 0.5f);
            {
                var m = motas.main;
                m.duration = 5f; m.startLifetime = new ParticleSystem.MinMaxCurve(3f, 6f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 4f);
                m.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.6f); m.maxParticles = 150;
                var e = motas.emission; e.rateOverTime = c.r * 1.4f;
                var s = motas.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 0f; s.radius = c.r * 0.7f; s.rotation = new Vector3(-90f, 0f, 0f);
                Alfa(motas, (0f, 0f), (1f, 0.2f), (1f, 0.7f), (0f, 1f));
                Tamano(motas, new Keyframe(0f, 1f), new Keyframe(1f, 0.4f));
                Ruido(motas, 1.5f, 0.2f, 0.4f);
            }
            var vapor = Sistema(g, "Vapor", matVapor, p + Vector3.up * 0.5f);
            {
                var m = vapor.main;
                m.duration = 6f; m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 4f);
                m.startSize = new ParticleSystem.MinMaxCurve(c.r * 0.5f, c.r * 0.9f); m.maxParticles = 25;
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.startColor = new Color(1f, 1f, 1f, 0.3f);
                var e = vapor.emission; e.rateOverTime = 1.6f;
                var s = vapor.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 5f; s.radius = c.r * 0.5f; s.rotation = new Vector3(-90f, 0f, 0f);
                Alfa(vapor, (0f, 0f), (1f, 0.25f), (0f, 1f));
                Tamano(vapor, new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f));
                Ruido(vapor, 1f, 0.1f, 0.2f);
            }
            var luzGo = new GameObject("Luz");
            luzGo.transform.SetParent(g, false);
            luzGo.transform.position = p + Vector3.up * 4f;
            var luz = luzGo.AddComponent<Light>();
            luz.type = LightType.Point;
            luz.color = new Color(0.35f, 1f, 0.25f);
            luz.range = c.r * 7f;
            luz.shadows = LightShadows.None;
            var lr = luzGo.AddComponent<LuzRadiactiva>();
            lr.intensidad = 5f * c.r * c.r;
        }
        {
            var ps = Sistema(gRad, "Motas_Aire", matMotas, new Vector3(X0, 35f, -1600f));
            var m = ps.main;
            m.duration = 8f; m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 10f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.8f, 2.2f); m.maxParticles = 400;
            var e = ps.emission; e.rateOverTime = 30f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(360f, 70f, 900f);
            Alfa(ps, (0f, 0f), (1f, 0.15f), (1f, 0.8f), (0f, 1f));
            Velocidad(ps, 1.5f, 1f, 0f);
            Ruido(ps, 3f, 0.05f, 0.3f);
        }
        log.AppendLine($"Radiactivo: {nR} charcos (shader + motas + vapor + luz) y motas en el aire");

        Undo.RegisterCreatedObjectUndo(raiz, "Caos ciudad");

        // ---------- Volumen de postpro de la ciudad: que cubra la ciudad ampliada ----------
        var vol = city.transform.Find("VFX_CIUDAD/VOLUMEN_CIUDAD");
        if (vol != null)
        {
            var box = vol.GetComponent<BoxCollider>();
            Undo.RecordObject(vol, "Volumen ciudad");
            if (box != null) Undo.RecordObject(box, "Volumen ciudad");
            vol.position = new Vector3(-1800f, vol.position.y, -300f);
            if (box != null) box.size = new Vector3(5400f, box.size.y, 9000f);
            log.AppendLine("VOLUMEN_CIUDAD ahora cubre x -4500..900, z -4800..4200");
        }

        // ---------- Timeline ----------
        log.AppendLine(PistaTimeline(city.transform, raiz));

        EditorSceneManager.MarkSceneDirty(city.scene);

        // ---------- Capturas (simula 6 s las partículas para que se vean) ----------
        foreach (var ps in raiz.GetComponentsInChildren<ParticleSystem>(true))
            if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null)
                ps.Simulate(6f, true, true);
        CapturasCaos(city.transform);

        Directory.CreateDirectory("Logs/ciudad_ampliada");
        File.WriteAllText("Logs/ciudad_ampliada/caos.txt", log.ToString());
        Debug.Log("[Caos] " + log.ToString().Replace("\n", " | "));
    }

    [MenuItem("Tools/Amistad/Ciudad: capturas del caos")]
    static void SoloCapturas()
    {
        var city = GameObject.Find("CITY");
        if (city == null) return;
        var raiz = city.transform.Find(NombreRaiz);
        if (raiz != null)
            foreach (var ps in raiz.GetComponentsInChildren<ParticleSystem>(true))
                if (ps.transform.parent == null || ps.transform.parent.GetComponent<ParticleSystem>() == null)
                    ps.Simulate(6f, true, true);
        CapturasCaos(city.transform);
    }

    static void CapturasCaos(Transform city)
    {
        var nb = city.Find("Director_P2_Ciudad/NIEBLA_CIUDAD");
        var niebla = nb != null ? nb.GetComponent<NieblaCiudad>() : null;
        Directory.CreateDirectory("Logs/ciudad_ampliada");
        foreach (var f in Directory.GetFiles("Logs/ciudad_ampliada", "caos_*.png")) File.Delete(f);
        Quaternion Mira(Vector3 a, Vector3 b) => Quaternion.LookRotation(b - a, Vector3.up);
        var c1 = new Vector3(X0 + 10f, 14f, -420f);
        CiudadAmpliadaEditor.Capturar("caos_1_zona_fuego.png", c1, Mira(c1, new Vector3(X0, 25f, -1100f)), 45f, true, niebla);
        var c2 = new Vector3(X0, 16f, -880f);
        CiudadAmpliadaEditor.Capturar("caos_2_zona_polvo.png", c2, Mira(c2, new Vector3(X0, 25f, -1500f)), 45f, true, niebla);
        var c3 = new Vector3(X0, 14f, -1300f);
        CiudadAmpliadaEditor.Capturar("caos_3_zona_radiactiva.png", c3, Mira(c3, new Vector3(X0, 15f, -2000f)), 45f, true, niebla);
        var c4 = new Vector3(X0, 14f, -170f);
        CiudadAmpliadaEditor.Capturar("caos_4_telefoto_avenida.png", c4, Mira(c4, new Vector3(X0, 45f, -2500f)), 20f, true, niebla);
        var c5 = new Vector3(X0, 14f, -1000f);
        CiudadAmpliadaEditor.Capturar("caos_5_mirando_a_Memi.png", c5, Mira(c5, new Vector3(X0, 30f, -200f)), 40f, true, niebla);
        var c6 = new Vector3(X0 + 400f, 600f, -2400f);
        CiudadAmpliadaEditor.Capturar("caos_6_aerea.png", c6, Mira(c6, new Vector3(X0, 0f, -900f)), 50f, true, niebla);
    }

    // ================= Timeline =================
    static string PistaTimeline(Transform city, GameObject raiz)
    {
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dirT = city.Find("Director_P2_Ciudad");
        var director = dirT != null ? dirT.GetComponent<PlayableDirector>() : null;
        if (tl == null || director == null) return "OJO: no encontré el Timeline de la ciudad; no se creó la pista.";
        Undo.RecordObject(tl, "Pista caos");
        foreach (var vieja in tl.GetOutputTracks().OfType<ControlTrack>().Where(t => t.name == NombrePista).ToList())
            tl.DeleteTrack(vieja);
        var pista = tl.CreateTrack<ControlTrack>(null, NombrePista);
        var clip = pista.CreateClip<ControlPlayableAsset>();
        clip.start = 0;
        clip.duration = 60;
        clip.displayName = "Caos (fuego, polvo, radiación)";
        var cpa = (ControlPlayableAsset)clip.asset;
        cpa.sourceGameObject.exposedName = GUID.Generate().ToString();
        Undo.RecordObject(director, "Pista caos");
        director.SetReferenceValue(cpa.sourceGameObject.exposedName, raiz);
        cpa.updateParticle = true;
        cpa.active = false;
        cpa.updateDirector = false;
        cpa.updateITimeControl = false;
        cpa.searchHierarchy = true;
        EditorUtility.SetDirty(tl);
        EditorUtility.SetDirty(director);
        AssetDatabase.SaveAssets();
        return $"Pista '{NombrePista}' en {tl.name} (0 a 60 s locales de la ciudad)";
    }

    // ================= Utilidades =================
    static Material Mat(Shader sh, string nombre, int cola, System.Action<Material> extra)
    {
        string ruta = Carpeta + "/" + nombre + ".mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
        else m.shader = sh;
        extra?.Invoke(m);
        m.renderQueue = cola;
        EditorUtility.SetDirty(m);
        return m;
    }

    static GameObject Fuego(GameObject prefab, Transform padre, string nombre, Vector3 pos, float alto, bool conLuz)
    {
        var f = (GameObject)PrefabUtility.InstantiatePrefab(prefab, padre);
        f.name = nombre;
        f.transform.position = pos;
        f.transform.rotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
        f.transform.localScale = Vector3.one * alto;
        if (!conLuz) foreach (var l in f.GetComponentsInChildren<Light>(true)) l.enabled = false;
        return f;
    }

    static void Columna(Transform padre, string nombre, Vector3 pos, float ancho, Material mat)
    {
        var ps = Sistema(padre, nombre, mat, pos + Vector3.up * ancho * 0.3f);
        var m = ps.main;
        m.duration = 10f;
        m.startLifetime = new ParticleSystem.MinMaxCurve(14f, 20f);
        m.startSpeed = new ParticleSystem.MinMaxCurve(ancho * 0.12f, ancho * 0.2f);
        m.startSize = new ParticleSystem.MinMaxCurve(ancho * 0.6f, ancho);
        m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.7f, 0.68f, 1f), new Color(1f, 0.95f, 0.9f, 1f));
        m.maxParticles = 70;
        var e = ps.emission; e.rateOverTime = 4f;
        var s = ps.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 8f; s.radius = ancho * 0.25f; s.rotation = new Vector3(-90f, 0f, 0f);
        Tamano(ps, new Keyframe(0f, 0.5f), new Keyframe(1f, 3.2f));
        Alfa(ps, (0f, 0f), (0.85f, 0.12f), (0.6f, 0.6f), (0f, 1f));
        Velocidad(ps, ancho * 0.05f, 0f, ancho * 0.015f);
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.15f, 0.15f);
        Ruido(ps, ancho * 0.08f, 0.02f, 0.1f);
        ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 5f;
    }

    static void Plano(Transform padre, string nombre, Mesh quad, Material mat, Vector3 pos, float tam, float altura)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.AddComponent<MeshFilter>().sharedMesh = quad;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = mat;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        go.transform.position = pos + Vector3.up * altura;
        go.transform.rotation = Quaternion.Euler(90f, R(0f, 360f), 0f);
        go.transform.localScale = new Vector3(tam, tam, 1f);
    }

    static ParticleSystem Sistema(Transform padre, string nombre, Material mat, Vector3 posicion)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.position = posicion;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main;
        m.loop = true;
        m.prewarm = true;
        m.playOnAwake = true;
        m.startColor = Color.white;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.scalingMode = ParticleSystemScalingMode.Local;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
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

    static void Alfa(ParticleSystem ps, params (float a, float t)[] claves)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  claves.Select(c => new GradientAlphaKey(c.a, c.t)).ToArray());
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
        v.space = ParticleSystemSimulationSpace.World;
        v.x = new ParticleSystem.MinMaxCurve(x);
        v.y = new ParticleSystem.MinMaxCurve(y);
        v.z = new ParticleSystem.MinMaxCurve(z);
    }

    static Transform Hijo(Transform padre, string nombre)
    {
        var g = new GameObject(nombre);
        g.transform.SetParent(padre, false);
        return g.transform;
    }

    static List<Transform> Hijos(Transform raiz, string nombre)
    {
        var t = raiz != null ? raiz.Find(nombre) : null;
        return t != null ? t.Cast<Transform>().ToList() : new List<Transform>();
    }

    static Bounds AABB(GameObject raiz)
    {
        bool hay = false; var b = new Bounds();
        foreach (var mf in raiz.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            var mb = mf.sharedMesh.bounds; var m = mf.transform.localToWorldMatrix;
            for (int i = 0; i < 8; i++)
            {
                var s = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                var w = m.MultiplyPoint3x4(mb.center + Vector3.Scale(mb.extents, s));
                if (!hay) { b = new Bounds(w, Vector3.zero); hay = true; } else b.Encapsulate(w);
            }
        }
        return b;
    }
}
