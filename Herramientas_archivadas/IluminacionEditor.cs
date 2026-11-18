using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tools > Amistad > 4 · Iluminación  (aplica lo de la Clase 16: luz horneada + probes)
///
///  Qué se hornea y por qué:
///   - GI horneado (Progressive GPU, 2 rebotes, oclusión ambiental, denoiser) solo en lo que NO se mueve:
///     ruinas (EDIFICIOS + CIUDAD_AMPLIADA), pisos de la ciudad, piso y montañas del bosque.
///     Da la luz del cielo que entra entre las ruinas, el rebote entre superficies y el oscurecimiento en rincones.
///   - Mixed lighting en modo "Baked Indirect": el SOL se queda Realtime (el Timeline lo gira: bosque, ciudad, media cara de Ale;
///     una sombra horneada se quedaría fija). Los FUEGOS y CHARCOS radiactivos de la ciudad pasan a Mixed:
///     su luz directa sigue en tiempo real (con el parpadeo) y su rebote naranja/verde queda horneado en piso y ruinas.
///   - Lo que se mueve (derrumbes, Godzilla, avioneta, Goku, polvo) y los árboles del bosque (son cientos y pequeños)
///     reciben la luz horneada por LIGHT PROBES (rejillas SONDAS_CIUDAD / SONDAS_BOSQUE colocadas sobre las superficies).
///   - Reflection probes horneados en la zona del saludo y en el bosque (reflejos del entorno real en vez del cielo).
///   - Los personajes (toon Unlit) no cambian: el horneado mejora todo lo que los rodea.
///  Presupuesto: ~2 lightmaps de 2048 (EXR por Git LFS); la resolución se calcula sola por área.
/// Se puede volver a correr. "Quitar horneado" deja todo como antes (luces Realtime, sin estáticos, sin lightmaps).
/// </summary>
public static class IluminacionEditor
{
    const string Carpeta = "Assets/Iluminacion";
    const string RutaSettings = Carpeta + "/Amistad_Iluminacion.lighting";
    const float TexelesObjetivo = 5.5e6f;         // ~2 mapas de 2048 llenos al ~65%
    const float ResMin = 0.02f, ResMax = 0.6f;
    const float IntensidadIndirecta = 1.8f, AlbedoBoost = 1.25f;
    const float DistanciaSombrasFuera = 50f, DistanciaSombrasCiudad = 400f;
    const float RadioCerca = 900f;                // u: alrededor de la acción, más detalle en el lightmap
    const float EscalaLejos = 0.25f, EscalaPisoLejos = 0.12f;
    const float TamArbol = 300f;                  // u: en el bosque, lo más chico que esto va por probes (árboles)
    static readonly Vector3[] CentrosAccion = { new Vector3(-19, 0, -251), new Vector3(5, 0, 140), new Vector3(0, 0, -60), new Vector3(3222, 5, 255), new Vector3(3420, 60, -60) };

    [MenuItem("Tools/Amistad/4 · Iluminación/Hornear (prepara + hornea en segundo plano)", false, 400)]
    static void MenuHornear()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Iluminación] Sal de Play."); return; }
        var log = new StringBuilder();
        Preparar(log);
        Debug.Log("[Iluminación] Preparado. Horneando en segundo plano...\n" + log);
        Lightmapping.BakeAsync();
    }

    [MenuItem("Tools/Amistad/4 · Iluminación/Solo preparar (sin hornear)", false, 401)]
    static void MenuPreparar() { var log = new StringBuilder(); Preparar(log); Debug.Log("[Iluminación]\n" + log); }

    [MenuItem("Tools/Amistad/4 · Iluminación/Quitar horneado (volver a todo en tiempo real)", false, 412)]
    static void MenuQuitar() { var log = new StringBuilder(); Quitar(log); Debug.Log("[Iluminación]\n" + log); }

    // ------------------------------------------------------------------ selección
    static Transform T(string nombre) => Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == nombre);

    static bool EsLit(Renderer r) => r.sharedMaterials.Length > 0 && r.sharedMaterials.All(m => m != null && m.shader != null && m.shader.name == "Universal Render Pipeline/Lit");
    static bool Visible(Renderer r) => r.enabled && r.gameObject.activeInHierarchy;

    /// lightmap: renderers que reciben lightmap; probes: contribuyen al GI pero se iluminan con probes (árboles)
    public static void Seleccion(out List<MeshRenderer> lightmap, out List<MeshRenderer> probes)
    {
        lightmap = new List<MeshRenderer>(); probes = new List<MeshRenderer>();
        var city = T("CITY");
        if (city != null)
        {
            var raizCity = city.GetComponent<MeshRenderer>();
            if (raizCity != null && Visible(raizCity) && EsLit(raizCity)) lightmap.Add(raizCity);
            foreach (var n in new[] { "EDIFICIOS", "Escena", "CIUDAD_AMPLIADA" })
            {
                var t = city.Find(n); if (t == null) continue;
                lightmap.AddRange(t.GetComponentsInChildren<MeshRenderer>(true).Where(r => Visible(r) && EsLit(r) && r.GetComponentInParent<Animator>() == null));
            }
        }
        var bosque = T("BOSQUE");
        if (bosque != null)
        {
            var piso = bosque.Find("Piso_FOREST");
            if (piso != null) lightmap.AddRange(piso.GetComponentsInChildren<MeshRenderer>(true).Where(r => Visible(r) && EsLit(r)));
            var obj = bosque.Find("OBJETOS_ESCENA");
            if (obj != null)
                foreach (Transform c in obj)
                {
                    if (c.name == "TITULO" || c.name == "Cascada") continue;   // letras animadas / agua animada
                    foreach (var r in c.GetComponentsInChildren<MeshRenderer>(true).Where(r => Visible(r) && EsLit(r) && r.GetComponentInParent<Animator>() == null))
                    {
                        var s = r.bounds.size;
                        if (Mathf.Max(s.x, s.z) >= TamArbol) lightmap.Add(r); else probes.Add(r);
                    }
                }
        }
    }

    static float AreaMundo(MeshRenderer r)
    {
        var mf = r.GetComponent<MeshFilter>(); if (mf == null || mf.sharedMesh == null) return 0f;
        var m = mf.sharedMesh; var v = m.vertices; var tr = m.triangles; var M = r.transform.localToWorldMatrix;
        double a = 0;
        for (int i = 0; i < tr.Length; i += 3)
        {
            var p0 = M.MultiplyPoint3x4(v[tr[i]]); var p1 = M.MultiplyPoint3x4(v[tr[i + 1]]); var p2 = M.MultiplyPoint3x4(v[tr[i + 2]]);
            a += Vector3.Cross(p1 - p0, p2 - p0).magnitude * 0.5;
        }
        return (float)a;
    }

    static float DistAccion(Bounds b) => CentrosAccion.Min(c => Mathf.Sqrt(b.SqrDistance(c)));

    // ------------------------------------------------------------------ preparar
    public static void Preparar(StringBuilder log)
    {
        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets", "Iluminacion");
        Seleccion(out var lm, out var pr);
        log.AppendLine($"Estáticos con lightmap: {lm.Count} · árboles por probes: {pr.Count}");

        // 1) UV2 (lightmap UVs) en los FBX que lo necesitan
        var rutas = lm.Select(r => r.GetComponent<MeshFilter>()?.sharedMesh).Where(m => m != null).Select(AssetDatabase.GetAssetPath).Where(p => p.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)).Distinct().ToList();
        foreach (var p in rutas)
        {
            var imp = AssetImporter.GetAtPath(p) as ModelImporter;
            if (imp != null && !imp.generateSecondaryUV) { imp.generateSecondaryUV = true; imp.SaveAndReimport(); log.AppendLine("UV2 generadas: " + p); }
        }
        Seleccion(out lm, out pr);   // tras reimportar, los renderers siguen siendo los mismos objetos de escena

        // 2) estáticos (Contribute GI + Reflection Probe Static), escala en lightmap según cercanía a la acción
        double texeles = 0; var escalas = new Dictionary<MeshRenderer, float>(); var areas = new Dictionary<MeshRenderer, float>();
        foreach (var r in lm)
        {
            float d = DistAccion(r.bounds);
            bool piso = r.GetComponent<MeshFilter>()?.sharedMesh?.name == "Plane";
            float esc = d <= RadioCerca ? 1f : (piso ? EscalaPisoLejos : EscalaLejos);
            escalas[r] = esc; areas[r] = AreaMundo(r);
            texeles += areas[r] * esc * esc;
        }
        float res = Mathf.Clamp(Mathf.Sqrt((float)(TexelesObjetivo / System.Math.Max(1.0, texeles))), ResMin, ResMax);
        log.AppendLine($"Área ponderada {texeles / 1e6:0.0} M u² → resolución {res:0.000} texeles/u (cerca de la acción: 1 texel cada {1f / res:0.0} u)");
        foreach (var r in lm) Estatico(r, ReceiveGI.Lightmaps, escalas[r]);
        foreach (var r in pr) Estatico(r, ReceiveGI.LightProbes, 1f);

        // 3) lighting settings (como LightCastillo de la clase, ajustado a la escala del corto)
        var ls = AssetDatabase.LoadAssetAtPath<LightingSettings>(RutaSettings);
        if (ls == null) { ls = new LightingSettings { name = "Amistad_Iluminacion" }; AssetDatabase.CreateAsset(ls, RutaSettings); }
        ls.bakedGI = true; ls.realtimeGI = false;
        ls.mixedBakeMode = MixedLightingMode.IndirectOnly;
        ls.lightmapper = LightingSettings.Lightmapper.ProgressiveGPU;
        ls.lightmapResolution = res; ls.lightmapMaxSize = 2048; ls.lightmapPadding = 4;
        ls.directionalityMode = LightmapsMode.NonDirectional;
        ls.lightmapCompression = LightmapCompression.NormalQuality;
        ls.ao = true; ls.aoMaxDistance = 25f; ls.aoExponentIndirect = 1f; ls.aoExponentDirect = 0f;
        // exterior abierto: con intensidad indirecta 1 el rebote casi no se nota (en la clase era un interior cerrado);
        // 1.8 + albedo 1.25 hace que el rebote cálido del atardecer y de los fuegos rellene las sombras en vez de dejarlas negras
        ls.indirectScale = IntensidadIndirecta; ls.albedoBoost = AlbedoBoost;
        ls.maxBounces = 2; ls.directSampleCount = 32; ls.indirectSampleCount = 256; ls.environmentSampleCount = 256;
        ls.filteringMode = LightingSettings.FilterMode.Auto;
        ls.denoiserTypeDirect = ls.denoiserTypeIndirect = ls.denoiserTypeAO = LightingSettings.DenoiserType.OpenImage;
        ls.prioritizeView = false;
        EditorUtility.SetDirty(ls);
        Lightmapping.lightingSettings = ls;
        log.AppendLine("Lighting Settings: " + RutaSettings + $" (Baked GI, Baked Indirect, GPU, 2 rebotes, indirecta {IntensidadIndirecta}, albedo {AlbedoBoost}, AO 25 u, máx 2048, no direccional)");

        // 4) luces: sol Realtime; fuegos y charcos de la ciudad Mixed (salvo los que alcanzarían el bosque)
        var bosque = T("BOSQUE");
        Bounds bb = new Bounds(bosque != null ? bosque.position : Vector3.zero, Vector3.zero);
        if (bosque != null) foreach (var r in bosque.GetComponentsInChildren<MeshRenderer>()) if (r.enabled) bb.Encapsulate(r.bounds);
        int mixtas = 0;
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            Undo.RecordObject(l, "Iluminación");
            bool caos = l.GetComponentsInParent<Transform>(true).Any(x => x.name == "FUEGOS" || x.name == "RADIACTIVO");
            var e = bb; e.Expand(l.range * 2f);
            bool tocaBosque = bosque != null && e.Contains(l.transform.position);
            var modo = l.type != LightType.Directional && caos && !tocaBosque ? LightmapBakeType.Mixed : LightmapBakeType.Realtime;
            if (l.lightmapBakeType != modo) { l.lightmapBakeType = modo; EditorUtility.SetDirty(l); }
            if (modo == LightmapBakeType.Mixed) mixtas++;
        }
        log.AppendLine($"Luces Mixed (fuegos y charcos): {mixtas}; el sol y lo demás Realtime.");

        // 5) light probes y reflection probes
        log.AppendLine(Sondas("SONDAS_CIUDAD", "3 · CIUDAD", new Rect(-450, -700, 900, 1200), 40f, lm, new[] { 4f, 25f, 90f }));
        log.AppendLine(Sondas("SONDAS_BOSQUE", "1 · BOSQUE", new Rect(2950, -250, 750, 850), 40f, lm.Concat(pr).ToList(), new[] { 4f, 25f, 90f }));
        log.AppendLine(Reflejo("REFLEJO_SALUDO", "3 · CIUDAD", new Vector3(-19, 60, -200), new Vector3(1000, 260, 1100)));
        log.AppendLine(SombrasCiudadObj());
        log.AppendLine(Reflejo("REFLEJO_BOSQUE", "1 · BOSQUE", new Vector3(3300, 80, 200), new Vector3(1200, 400, 1200)));

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
    }

    static void Estatico(MeshRenderer r, ReceiveGI recibe, float escala)
    {
        var go = r.gameObject;
        var f = GameObjectUtility.GetStaticEditorFlags(go) | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
        if (GameObjectUtility.GetStaticEditorFlags(go) != f) { Undo.RecordObject(go, "Iluminación"); GameObjectUtility.SetStaticEditorFlags(go, f); }
        Undo.RecordObject(r, "Iluminación");
        r.receiveGI = recibe;
        r.scaleInLightmap = escala;
        EditorUtility.SetDirty(r);
    }

    /// Rejilla de light probes sobre las superficies (colliders temporales en lo estático para encontrar piso/techos)
    static string Sondas(string nombre, string carpeta, Rect area, float paso, List<MeshRenderer> superficies, float[] alturas)
    {
        var temporales = new List<MeshCollider>();
        foreach (var r in superficies)
            if (r.GetComponent<Collider>() == null) { var mc = r.gameObject.AddComponent<MeshCollider>(); mc.hideFlags = HideFlags.DontSave; temporales.Add(mc); }
        Physics.SyncTransforms();
        var pts = new List<Vector3>();
        try
        {
            for (float x = area.xMin; x <= area.xMax; x += paso)
                for (float z = area.yMin; z <= area.yMax; z += paso)
                {
                    var hits = Physics.RaycastAll(new Vector3(x, 3000f, z), Vector3.down, 6000f, ~0, QueryTriggerInteraction.Ignore)
                        .OrderByDescending(h => h.point.y).ToList();
                    if (hits.Count == 0) hits.Add(new RaycastHit { point = new Vector3(x, 0f, z) });
                    var usados = new List<float>();
                    foreach (var h in hits.Take(3))
                        foreach (var a in alturas)
                        {
                            var p = h.point + Vector3.up * a;
                            if (usados.Any(u => Mathf.Abs(u - p.y) < paso * 0.4f)) continue;
                            if (Physics.CheckSphere(p, 1.5f, ~0, QueryTriggerInteraction.Ignore)) continue;   // dentro de algo
                            pts.Add(p); usados.Add(p.y);
                        }
                }
        }
        finally { foreach (var mc in temporales) Object.DestroyImmediate(mc); }
        var t = T(nombre);
        if (t == null) { var go = new GameObject(nombre); Undo.RegisterCreatedObjectUndo(go, nombre); t = go.transform; }
        var padre = T(carpeta); if (padre != null && t.parent != padre) Undo.SetTransformParent(t, padre, nombre);
        t.position = Vector3.zero; t.rotation = Quaternion.identity; t.localScale = Vector3.one;
        var g = t.GetComponent<LightProbeGroup>(); if (g == null) g = Undo.AddComponent<LightProbeGroup>(t.gameObject);
        Undo.RecordObject(g, nombre);
        g.probePositions = pts.Select(p => t.InverseTransformPoint(p)).ToArray();
        EditorUtility.SetDirty(g);
        return $"{nombre}: {pts.Count} light probes (rejilla de {paso} u sobre piso/techos a {string.Join("/", alturas)} u).";
    }

    static string Reflejo(string nombre, string carpeta, Vector3 centro, Vector3 tam)
    {
        var t = T(nombre);
        if (t == null) { var go = new GameObject(nombre); Undo.RegisterCreatedObjectUndo(go, nombre); t = go.transform; }
        var padre = T(carpeta); if (padre != null && t.parent != padre) Undo.SetTransformParent(t, padre, nombre);
        t.position = centro; t.rotation = Quaternion.identity; t.localScale = Vector3.one;
        var rp = t.GetComponent<ReflectionProbe>(); if (rp == null) rp = Undo.AddComponent<ReflectionProbe>(t.gameObject);
        Undo.RecordObject(rp, nombre);
        rp.mode = ReflectionProbeMode.Baked; rp.size = tam; rp.center = Vector3.zero; rp.resolution = 256; rp.hdr = true;
        rp.boxProjection = false; rp.blendDistance = 50f; rp.importance = 1; rp.nearClipPlane = 1f; rp.farClipPlane = 6000f;
        EditorUtility.SetDirty(rp);
        return $"{nombre}: reflection probe horneado {tam.x:0}×{tam.y:0}×{tam.z:0} u.";
    }

    /// Sombras largas (400 u) solo mientras está activo Director_P2_Ciudad; el URP se queda en 50 para el bosque y el título.
    static string SombrasCiudadObj()
    {
        var dir = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(x => x.name == "Director_P2_Ciudad");
        if (dir == null) return "OJO: no encontré Director_P2_Ciudad (sombras de la ciudad sin cambiar).";
        var t = dir.Find("SOMBRAS_CIUDAD");
        if (t == null) { var go = new GameObject("SOMBRAS_CIUDAD"); Undo.RegisterCreatedObjectUndo(go, "Sombras"); t = go.transform; t.SetParent(dir, false); }
        var c = t.GetComponent<SombrasCiudad>(); if (c == null) c = Undo.AddComponent<SombrasCiudad>(t.gameObject);
        Undo.RecordObject(c, "Sombras"); c.distancia = DistanciaSombrasCiudad; EditorUtility.SetDirty(c);
        var urp = GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
        if (urp != null && !dir.gameObject.activeInHierarchy && urp.shadowDistance != DistanciaSombrasFuera) { Undo.RecordObject(urp, "Sombras"); urp.shadowDistance = DistanciaSombrasFuera; EditorUtility.SetDirty(urp); }
        return $"Sombras del sol: {DistanciaSombrasFuera} u en bosque/título (URP) y {DistanciaSombrasCiudad} u durante la ciudad (SOMBRAS_CIUDAD).";
    }

    // ------------------------------------------------------------------ quitar
    public static void Quitar(StringBuilder log)
    {
        Lightmapping.Clear(); Lightmapping.ClearLightingDataAsset();
        Seleccion(out var lm, out var pr);
        foreach (var r in lm.Concat(pr))
        {
            Undo.RecordObject(r.gameObject, "Quitar"); Undo.RecordObject(r, "Quitar");
            GameObjectUtility.SetStaticEditorFlags(r.gameObject, GameObjectUtility.GetStaticEditorFlags(r.gameObject) & ~(StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic));
            r.receiveGI = ReceiveGI.Lightmaps;   // valor por defecto
        }
        foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (l.lightmapBakeType != LightmapBakeType.Realtime) { Undo.RecordObject(l, "Quitar"); l.lightmapBakeType = LightmapBakeType.Realtime; }
        foreach (var n in new[] { "SONDAS_CIUDAD", "SONDAS_BOSQUE", "REFLEJO_SALUDO", "REFLEJO_BOSQUE", "SOMBRAS_CIUDAD" }) { var t = T(n); if (t != null) Undo.DestroyObjectImmediate(t.gameObject); }
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        log.AppendLine($"Horneado quitado: {lm.Count + pr.Count} objetos ya no son estáticos, luces en Realtime, probes borrados.");
    }
}
