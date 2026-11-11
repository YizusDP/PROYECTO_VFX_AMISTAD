using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Amistad > 1 · Bosque > Cascada: poza, espuma y shader   (2026-11-11)
/// La cascada caía sobre la ladera (que baja hacia la cámara) y no se veía dónde terminaba el agua.
///  1. Montaña: copia de la malla de mountain_terrain_02 (el FBX no se toca) subdividida en la zona y excavada en una poza
///     elíptica donde cae el agua, con un bordo del lado de abajo de la ladera para que el agua no se salga.
///  2. Cascada: material nuevo con el shader "Amistad/Cascada Agua" (vetas que bajan, espuma arriba y abajo, se corta en
///     el nivel de la poza). El material original (WaterFallMejorado) no se toca.
///  3. Poza: superficie con el shader "Amistad/Agua Poza" (profundidad, espuma de orilla e impacto, ondas, refracción).
///  4. Espuma, gotas y neblina donde pega el agua; claro en la vegetación (VegetacionPiso) para que no salga pasto del agua.
/// Se puede volver a correr. "… : quitar" regresa todo como estaba.
/// </summary>
public static class CascadaPozaEditor
{
    // ---- Poza (mundo) ----
    static readonly Vector2 Centro = new Vector2(3915f, -86f);    // x, z
    const float RadioX = 80f, RadioZ = 125f;                      // elipse (la cascada mide 128 u de ancho en Z)
    const float Nivel = 9f;                                       // superficie del agua (el piso del bosque está en y = 5)
    const float Fondo = 2f;                                       // fondo de la poza (queda debajo del piso del bosque)
    const float Bordo = Nivel + 1f;                               // bordo casi a ras (con +3 se veía como una pared de plato)
    static readonly Vector3 Impacto = new Vector3(3950f, Nivel, -86f);   // donde la tira de la cascada cruza el nivel
    const float MedioAnchoImpacto = 55f;

    const string RutaFbxMontana = "Assets/Assets/3d/Bakcground/CASCADA/mountain_terrain_02.fbx";
    const string RutaMallaPoza = "Assets/Assets/3d/Bakcground/CASCADA/mountain_terrain_02_Poza.asset";
    const string Carpeta = "Assets/Assets/shaders/Cascada";
    const string RutaMatCascada = Carpeta + "/Cascada_Agua.mat";
    const string RutaMatPoza = Carpeta + "/Poza_Agua.mat";
    const string RutaMatEspuma = Carpeta + "/Cascada_Espuma.mat";
    const string RutaMatGotas = Carpeta + "/Cascada_Gotas.mat";
    const string RutaMallaAgua = Carpeta + "/Poza_Superficie.asset";
    const string RutaMatOriginal = "Assets/Assets/shaders/WaterFallMejorado.mat";
    const string RutaRuido = "Assets/Assets/shaders/TexturaS/Ruido_Fuego.png";
    const string RutaHumo = "Assets/Assets/shaders/FuegoVFX/Fuego_Humo.mat";
    const string RutaBrasas = "Assets/Assets/shaders/FuegoVFX/Fuego_Brasas.mat";
    const string NombreRaiz = "POZA_CASCADA";
    const string NombreClaro = "Poza cascada";

    [MenuItem("Tools/Amistad/1 · Bosque/Cascada: poza, espuma y shader", false, 120)]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Cascada] Sal de Play primero."); return; }
        var log = new StringBuilder();
        if (Aplicar(log)) { AssetDatabase.SaveAssets(); Debug.Log("[Cascada] Listo.\n" + log); }
        else Debug.LogError("[Cascada] No se pudo.\n" + log);
    }

    [MenuItem("Tools/Amistad/1 · Bosque/Cascada: poza, espuma y shader : quitar", false, 121)]
    static void Quitar()
    {
        var todos = Todos();
        var montana = todos.FirstOrDefault(t => t.name == "mountain_terrain_02");
        if (montana != null)
        {
            var mf = montana.GetComponent<MeshFilter>(); var orig = MallaOriginal();
            if (mf != null && orig != null) { Undo.RecordObject(mf, "Quitar poza"); mf.sharedMesh = orig; }
        }
        var cas = todos.FirstOrDefault(t => t.name == "Cascada");
        var matO = AssetDatabase.LoadAssetAtPath<Material>(RutaMatOriginal);
        if (cas != null && matO != null) { var r = cas.GetComponent<Renderer>(); Undo.RecordObject(r, "Quitar poza"); r.sharedMaterial = matO; }
        foreach (var t in todos.Where(t => t != null && t.name == NombreRaiz).ToList()) Undo.DestroyObjectImmediate(t.gameObject);
        ClaroVegetacion(null, 0f, null);
        EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        Debug.Log("[Cascada] Quitado: montaña, material de la cascada y vegetación como antes.");
    }

    static List<Transform> Todos() =>
        EditorSceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();

    static Mesh MallaOriginal() =>
        AssetDatabase.LoadAllAssetsAtPath(RutaFbxMontana).OfType<Mesh>().FirstOrDefault(m => m.name == "mountain_terrain_02");

    public static bool Aplicar(StringBuilder log)
    {
        var todos = Todos();
        var montana = todos.FirstOrDefault(t => t.name == "mountain_terrain_02");
        var cascada = todos.FirstOrDefault(t => t.name == "Cascada" && t.GetComponent<MeshRenderer>() != null);
        var padre = todos.FirstOrDefault(t => t.name == "OBJETOS_ESCENA");
        var orig = MallaOriginal();
        var ruido = AssetDatabase.LoadAssetAtPath<Texture2D>(RutaRuido);
        var humo = AssetDatabase.LoadAssetAtPath<Material>(RutaHumo);
        var brasas = AssetDatabase.LoadAssetAtPath<Material>(RutaBrasas);
        var shCascada = Shader.Find("Amistad/Cascada Agua");
        var shPoza = Shader.Find("Amistad/Agua Poza");
        if (montana == null || cascada == null || padre == null || orig == null || ruido == null || humo == null || brasas == null || shCascada == null || shPoza == null)
        { log.AppendLine("Falta la montaña, la cascada, OBJETOS_ESCENA, el FBX, el ruido, materiales del fuego o los shaders."); return false; }
        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets/Assets/shaders", "Cascada");

        // ---------- 1. montaña con la poza ----------
        var malla = Excavar(orig, montana.localToWorldMatrix, log);
        var asset = AssetDatabase.LoadAssetAtPath<Mesh>(RutaMallaPoza);
        if (asset == null) { asset = new Mesh(); AssetDatabase.CreateAsset(asset, RutaMallaPoza); }
        Copiar(malla, asset);
        asset.name = "mountain_terrain_02_Poza";
        asset.UploadMeshData(false);
        EditorUtility.SetDirty(asset);
        var mfM = montana.GetComponent<MeshFilter>();
        Undo.RecordObject(mfM, "Poza cascada"); mfM.sharedMesh = asset;
        log.AppendLine($"Montaña: {orig.vertexCount} -> {asset.vertexCount} vértices; poza en ({Centro.x}, {Centro.y}) {RadioX * 2}x{RadioZ * 2} u, agua a y={Nivel}.");

        // ---------- 2. material de la cascada ----------
        var mc = MatShader(RutaMatCascada, shCascada);
        mc.SetTexture("_Ruido", ruido);
        mc.SetFloat("_NivelAgua", Nivel);
        EditorUtility.SetDirty(mc);
        var rc = cascada.GetComponent<MeshRenderer>();
        // (2026-11-11) el usuario prefiere la caída original: se queda WaterFallMejorado. Cascada_Agua.mat queda como alternativa.
        var matOrig = AssetDatabase.LoadAssetAtPath<Material>(RutaMatOriginal);
        if (matOrig != null) { Undo.RecordObject(rc, "Poza cascada"); rc.sharedMaterial = matOrig; }

        // ---------- 3. superficie de la poza ----------
        foreach (var t in todos.Where(t => t != null && t.name == NombreRaiz).ToList()) Undo.DestroyObjectImmediate(t.gameObject);
        var raiz = new GameObject(NombreRaiz); Undo.RegisterCreatedObjectUndo(raiz, "Poza cascada");
        raiz.transform.SetParent(padre, false);
        raiz.transform.position = new Vector3(Centro.x, Nivel, Centro.y);
        raiz.transform.rotation = Quaternion.identity;
        PonerEscalaMundo(raiz.transform, Vector3.one);

        var mp = MatShader(RutaMatPoza, shPoza);
        mp.SetTexture("_Ruido", ruido);
        mp.SetVector("_Impacto", new Vector4(Impacto.x, Impacto.y, Impacto.z, MedioAnchoImpacto));
        // agua más oscura: a ras (la cámara la ve a ~11°) el reflejo del cielo la dejaba color durazno
        mp.SetColor("_ColorBajo", new Color(0.22f, 0.4f, 0.42f, 1f));
        mp.SetColor("_ColorHondo", new Color(0.07f, 0.19f, 0.22f, 1f));
        mp.SetColor("_ColorCielo", new Color(0.8f, 0.52f, 0.32f, 1f));
        mp.SetFloat("_Brillo", 140f);
        mp.SetFloat("_AnchoOrilla", 1.0f);
        mp.SetFloat("_RadioEspuma", 26f);
        mp.SetFloat("_Profundidad", 6f);
        mp.SetVector("_Ondas", new Vector4(0.18f, 3f, 0.2f, 120f));
        EditorUtility.SetDirty(mp);
        var sup = MallaSuperficie();
        var aSup = AssetDatabase.LoadAssetAtPath<Mesh>(RutaMallaAgua);
        if (aSup == null) { aSup = new Mesh(); AssetDatabase.CreateAsset(aSup, RutaMallaAgua); }
        Copiar(sup, aSup); aSup.name = "Poza_Superficie"; aSup.UploadMeshData(false); EditorUtility.SetDirty(aSup);
        var agua = new GameObject("AGUA");
        agua.transform.SetParent(raiz.transform, false);
        PonerEscalaMundo(agua.transform, Vector3.one);
        agua.AddComponent<MeshFilter>().sharedMesh = aSup;
        var ra = agua.AddComponent<MeshRenderer>(); ra.sharedMaterial = mp;
        ra.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; ra.receiveShadows = false;

        // ---------- 4. espuma, gotas y neblina ----------
        var mEsp = new Material(humo) { name = "Cascada_Espuma" };
        mEsp.SetColor("_ColorBase", new Color(0.86f, 0.84f, 0.8f, 1f));
        mEsp.SetFloat("_EscalaRuido", 1.6f); mEsp.SetFloat("_SuavidadSuelo", 6f);
        // estilo cómic como el resto del corto (modo humo con _Toon: silueta nítida y orilla gris azulada, no negra)
        mEsp.SetFloat("_Toon", 0.75f); mEsp.SetFloat("_Umbral", 0.3f); mEsp.SetFloat("_Borde", 0.1f);
        mEsp.SetFloat("_CortePunta", 0.6f); mEsp.SetColor("_ColorBorde", new Color(0.64f, 0.68f, 0.72f, 1f));
        mEsp = Guardar(mEsp, RutaMatEspuma);
        var mGot = new Material(brasas) { name = "Cascada_Gotas" };
        mGot.SetColor("_ColorMedio", new Color(0.55f, 0.6f, 0.62f, 1f));
        mGot.SetColor("_ColorPunta", new Color(1.1f, 1.08f, 1.02f, 1f));
        mGot.SetFloat("_Intensidad", 0.8f);
        mGot = Guardar(mGot, RutaMatGotas);
        Espuma(raiz.transform, mEsp);
        Neblina(raiz.transform, mEsp);
        Gotas(raiz.transform, mGot);
        log.AppendLine("Espuma, gotas y neblina en el impacto " + Impacto);

        // ---------- 5. claro en la vegetación ----------
        log.AppendLine(ClaroVegetacion(raiz.transform, Mathf.Max(RadioX, RadioZ) * 1.0f, log));

        EditorSceneManager.MarkSceneDirty(montana.gameObject.scene);
        return true;
    }

    // ================================================================= montaña
    static float Rumbo(float ang) => 1f + 0.08f * Mathf.Sin(3f * ang + 0.7f) + 0.05f * Mathf.Sin(5f * ang + 2.1f);   // orilla irregular

    static float RadioNormal(Vector3 w)
    {
        float dx = (w.x - Centro.x) / RadioX, dz = (w.z - Centro.y) / RadioZ;
        float r = Mathf.Sqrt(dx * dx + dz * dz);
        return r * Rumbo(Mathf.Atan2(dz, dx));
    }

    static float S(float a, float b, float x) { float t = Mathf.Clamp01((x - a) / (b - a)); return t * t * (3f - 2f * t); }

    static float Perfil(float r, float yOrig)
    {
        if (r >= 1.8f) return yOrig;
        if (r < 0.55f) return Fondo;
        if (r < 0.85f) return Mathf.Lerp(Fondo, Bordo, S(0.55f, 0.85f, r));
        if (r < 1.0f) return Bordo;
        return Mathf.Lerp(Bordo, yOrig, S(1.0f, 1.8f, r));   // loma larga hasta el terreno original
    }

    class Datos
    {
        public List<Vector3> v = new List<Vector3>(), n = new List<Vector3>();
        public List<Vector4> tg = new List<Vector4>();
        public List<Vector2> uv = new List<Vector2>(), uv2 = new List<Vector2>();
        public List<Color> col = new List<Color>();
        public List<int> tri = new List<int>();
    }

    static Mesh Excavar(Mesh src, Matrix4x4 M, StringBuilder log)
    {
        var d = new Datos();
        src.GetVertices(d.v); src.GetNormals(d.n); src.GetTangents(d.tg); src.GetUVs(0, d.uv); src.GetUVs(1, d.uv2); src.GetColors(d.col);
        d.tri.AddRange(src.triangles);
        bool hayN = d.n.Count == d.v.Count, hayT = d.tg.Count == d.v.Count, hayUV = d.uv.Count == d.v.Count, hayUV2 = d.uv2.Count == d.v.Count, hayC = d.col.Count == d.v.Count;
        var Mi = M.inverse;

        // subdividir dos veces los triángulos de la zona (la malla tiene ~22 u entre vértices)
        for (int it = 0; it < 2; it++)
        {
            var medio = new Dictionary<long, int>();
            var nuevos = new List<int>();
            int nSub = 0;
            int Medio(int a, int b)
            {
                long k = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (medio.TryGetValue(k, out int m)) return m;
                m = d.v.Count;
                d.v.Add((d.v[a] + d.v[b]) * 0.5f);
                if (hayN) d.n.Add((d.n[a] + d.n[b]).normalized);
                if (hayT) d.tg.Add((d.tg[a] + d.tg[b]) * 0.5f);
                if (hayUV) d.uv.Add((d.uv[a] + d.uv[b]) * 0.5f);
                if (hayUV2) d.uv2.Add((d.uv2[a] + d.uv2[b]) * 0.5f);
                if (hayC) d.col.Add((d.col[a] + d.col[b]) * 0.5f);
                medio[k] = m; return m;
            }
            for (int t = 0; t < d.tri.Count; t += 3)
            {
                int a = d.tri[t], b = d.tri[t + 1], c = d.tri[t + 2];
                float r = Mathf.Min(RadioNormal(M.MultiplyPoint3x4(d.v[a])), Mathf.Min(RadioNormal(M.MultiplyPoint3x4(d.v[b])), RadioNormal(M.MultiplyPoint3x4(d.v[c]))));
                if (r > 2.0f) { nuevos.Add(a); nuevos.Add(b); nuevos.Add(c); continue; }
                int ab = Medio(a, b), bc = Medio(b, c), ca = Medio(c, a);
                nuevos.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
                nSub++;
            }
            d.tri = nuevos;
            log.AppendLine($"  subdivisión {it + 1}: {nSub} triángulos partidos en 4");
        }

        // excavar
        var tocado = new bool[d.v.Count];
        int nT = 0;
        for (int i = 0; i < d.v.Count; i++)
        {
            var w = M.MultiplyPoint3x4(d.v[i]);
            float r = RadioNormal(w);
            if (r >= 1.8f) continue;
            w.y = Perfil(r, w.y);
            d.v[i] = Mi.MultiplyPoint3x4(w);
            tocado[i] = true; nT++;
        }
        // normales nuevas solo en lo que se movió (el resto conserva las del FBX)
        if (hayN)
        {
            var acc = new Vector3[d.v.Count];
            for (int t = 0; t < d.tri.Count; t += 3)
            {
                int a = d.tri[t], b = d.tri[t + 1], c = d.tri[t + 2];
                if (!tocado[a] && !tocado[b] && !tocado[c]) continue;
                var fn = Vector3.Cross(d.v[b] - d.v[a], d.v[c] - d.v[a]);
                acc[a] += fn; acc[b] += fn; acc[c] += fn;
            }
            // vértices repetidos en la misma posición (costuras de UV): misma normal
            var porPos = new Dictionary<Vector3Int, Vector3>();
            Vector3Int K(Vector3 p) => new Vector3Int(Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f));
            for (int i = 0; i < d.v.Count; i++) if (tocado[i]) { var k = K(d.v[i]); porPos[k] = (porPos.TryGetValue(k, out var s) ? s : Vector3.zero) + acc[i]; }
            for (int i = 0; i < d.v.Count; i++)
                if (tocado[i]) { var s = porPos[K(d.v[i])]; if (s.sqrMagnitude > 1e-12f) d.n[i] = s.normalized; }
        }
        log.AppendLine($"  vértices movidos: {nT}");

        var m2 = new Mesh { indexFormat = d.v.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
        m2.SetVertices(d.v);
        if (hayN) m2.SetNormals(d.n);
        if (hayT) m2.SetTangents(d.tg);
        if (hayUV) m2.SetUVs(0, d.uv);
        if (hayUV2) m2.SetUVs(1, d.uv2);
        if (hayC) m2.SetColors(d.col);
        m2.SetTriangles(d.tri, 0);
        m2.RecalculateBounds();
        return m2;
    }

    static void Copiar(Mesh de, Mesh a)
    {
        a.Clear();
        a.indexFormat = de.indexFormat;
        a.vertices = de.vertices;
        if (de.normals.Length > 0) a.normals = de.normals;
        if (de.tangents.Length > 0) a.tangents = de.tangents;
        if (de.uv.Length > 0) a.uv = de.uv;
        if (de.uv2.Length > 0) a.uv2 = de.uv2;
        if (de.colors.Length > 0) a.colors = de.colors;
        a.subMeshCount = de.subMeshCount;
        for (int s = 0; s < de.subMeshCount; s++) a.SetTriangles(de.GetTriangles(s), s);
        a.RecalculateBounds();
    }

    // Superficie: elipse un poco más grande que la orilla (r = 0.82; la orilla queda en r ≈ 0.76), en coordenadas locales
    static Mesh MallaSuperficie()
    {
        const int seg = 96, anillos = 8;
        var v = new List<Vector3> { Vector3.zero };
        var tri = new List<int>();
        for (int k = 1; k <= anillos; k++)
        {
            float f = 0.82f * k / anillos;
            for (int s = 0; s < seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                float rr = f / Rumbo(a);
                v.Add(new Vector3(Mathf.Cos(a) * rr * RadioX, 0f, Mathf.Sin(a) * rr * RadioZ));
            }
        }
        for (int s = 0; s < seg; s++) { int a = 1 + s, b = 1 + (s + 1) % seg; tri.Add(0); tri.Add(b); tri.Add(a); }
        for (int k = 1; k < anillos; k++)
            for (int s = 0; s < seg; s++)
            {
                int a = 1 + (k - 1) * seg + s, b = 1 + (k - 1) * seg + (s + 1) % seg;
                int c = 1 + k * seg + s, e = 1 + k * seg + (s + 1) % seg;
                tri.AddRange(new[] { a, b, c, b, e, c });
            }
        var m = new Mesh();
        m.SetVertices(v);
        m.SetNormals(Enumerable.Repeat(Vector3.up, v.Count).ToList());
        m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        return m;
    }

    // ================================================================= materiales y partículas
    static Material MatShader(string ruta, Shader sh)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
        else if (m.shader != sh) m.shader = sh;
        return m;
    }

    static Material Guardar(Material nuevo, string ruta)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { AssetDatabase.CreateAsset(nuevo, ruta); return nuevo; }
        m.CopyPropertiesFromMaterial(nuevo); m.shader = nuevo.shader; EditorUtility.SetDirty(m);
        return m;
    }

    static void PonerEscalaMundo(Transform t, Vector3 escala)
    {
        var p = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(escala.x / p.x, escala.y / p.y, escala.z / p.z);
    }

    static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
    { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
      ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent };

    static ParticleSystem Sistema(Transform padre, string nombre, Material mat, Vector3 pos)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false);
        PonerEscalaMundo(go.transform, Vector3.one);
        go.transform.position = pos;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = true; m.prewarm = true; m.playOnAwake = true; m.duration = 5f;
        m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.maxParticles = 2000;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.Distance; r.maxParticleSize = 3f;
        r.SetActiveVertexStreams(Streams);
        return ps;
    }

    static Gradient Grad(params (float t, Color c)[] k)
    {
        var g = new Gradient();
        g.SetKeys(k.Select(x => new GradientColorKey(new Color(x.c.r, x.c.g, x.c.b), x.t)).ToArray(),
                  k.Select(x => new GradientAlphaKey(x.c.a, x.t)).ToArray());
        return g;
    }

    // bocanadas de espuma que brotan a lo largo de la línea del impacto
    static void Espuma(Transform raiz, Material mat)
    {
        var ps = Sistema(raiz, "ESPUMA_IMPACTO", mat, Impacto + Vector3.up * 2f);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f); m.startSpeed = new ParticleSystem.MinMaxCurve(8f, 22f);
        m.startSize = new ParticleSystem.MinMaxCurve(22f, 40f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = 0.25f;
        var em = ps.emission; em.rateOverTime = 35f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(10f, 2f, MedioAnchoImpacto * 2f);
        sh.rotation = Vector3.zero; sh.randomDirectionAmount = 0.5f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-14f, 4f); vel.y = new ParticleSystem.MinMaxCurve(4f, 14f); vel.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
        var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 10f; lim.dampen = 0.08f;
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.9f));
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
        var col = ps.colorOverLifetime; col.enabled = true;
        col.color = Grad((0f, new Color(1, 1, 1, 0f)), (0.1f, new Color(1, 1, 1, 0.75f)), (0.5f, new Color(0.95f, 0.95f, 0.95f, 0.45f)), (1f, new Color(0.9f, 0.9f, 0.9f, 0f)));
    }

    // neblina que sube y se va hacia la cámara
    static void Neblina(Transform raiz, Material mat)
    {
        var ps = Sistema(raiz, "NEBLINA", mat, Impacto + new Vector3(-15f, 8f, 0f));
        var m = ps.main; m.duration = 8f; m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 6.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
        m.startSize = new ParticleSystem.MinMaxCurve(55f, 90f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        var em = ps.emission; em.rateOverTime = 5f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(30f, 10f, MedioAnchoImpacto * 1.8f);
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-6f, -1f); vel.y = new ParticleSystem.MinMaxCurve(4f, 9f); vel.z = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f);
        var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.7f, 1, 1.8f));
        var rot = ps.rotationOverLifetime; rot.enabled = true; rot.z = new ParticleSystem.MinMaxCurve(-0.2f, 0.2f);
        var col = ps.colorOverLifetime; col.enabled = true;
        col.color = Grad((0f, new Color(1, 1, 1, 0f)), (0.25f, new Color(1, 1, 1, 0.14f)), (1f, new Color(1, 1, 1, 0f)));
    }

    // gotitas que salen disparadas del impacto y caen
    static void Gotas(Transform raiz, Material mat)
    {
        var ps = Sistema(raiz, "GOTAS", mat, Impacto + Vector3.up * 1f);
        var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(25f, 55f);
        m.startSize = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); m.gravityModifier = 3f;
        var em = ps.emission; em.rateOverTime = 160f;
        var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Box; sh.scale = new Vector3(6f, 1f, MedioAnchoImpacto * 2f);
        sh.randomDirectionAmount = 0.6f;
        var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
        vel.x = new ParticleSystem.MinMaxCurve(-25f, 5f); vel.y = new ParticleSystem.MinMaxCurve(15f, 35f); vel.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
        var col = ps.colorOverLifetime; col.enabled = true;
        col.color = Grad((0f, new Color(1, 1, 1, 1f)), (0.7f, new Color(1, 1, 1, 0.8f)), (1f, new Color(1, 1, 1, 0f)));
    }

    // claro en la vegetación del piso (VegetacionPiso.claros): quita el pasto que saldría del agua
    static string ClaroVegetacion(Transform centro, float radio, StringBuilder log)
    {
        var tipo = System.Type.GetType("VegetacionPiso, Assembly-CSharp");
        if (tipo == null) return "No encontré VegetacionPiso.";
        var veg = Object.FindObjectsByType(tipo, FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault();
        if (veg == null) return "No hay VegetacionPiso en la escena.";
        var lista = tipo.GetField("claros").GetValue(veg) as System.Collections.IList;
        var tc = tipo.GetNestedType("Claro");
        Undo.RecordObject(veg, "Claro poza");
        for (int i = lista.Count - 1; i >= 0; i--)
            if ((string)tc.GetField("nombre").GetValue(lista[i]) == NombreClaro) lista.RemoveAt(i);
        if (centro != null)
        {
            var c = System.Activator.CreateInstance(tc);
            tc.GetField("nombre").SetValue(c, NombreClaro);
            tc.GetField("centro").SetValue(c, centro);
            tc.GetField("radio").SetValue(c, radio);
            lista.Add(c);
        }
        EditorUtility.SetDirty(veg);
        tipo.GetMethod("Regenerar")?.Invoke(veg, null);
        return centro != null ? $"Claro '{NombreClaro}' en la vegetación (radio {radio:0} u)." : "Claro de la poza quitado de la vegetación.";
    }
}
