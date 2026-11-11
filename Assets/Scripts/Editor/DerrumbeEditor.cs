using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;
using BlendMode = UnityEngine.Rendering.BlendMode;

/// <summary>
/// Tools > Amistad > 2 · Ciudad > Derrumbar edificio (ruim.008)
///  1. Parte la malla del edificio en sus piezas sueltas (losas y columnas).
///  2. Simula con física un derrumbe progresivo: fallan las columnas de planta baja del lado hacia donde
///     ya se inclina el edificio, el bloque de arriba cae y se ladea, y al golpear rompe el siguiente piso.
///  3. Graba ("hornea") la simulación en un archivo de datos y la reproduce con DerrumbeHorneado.
///  4. Crea polvo y chispas en los momentos y lugares de los impactos, y una pista "Derrumbe" en el Timeline.
/// Se puede volver a correr: reemplaza lo que creó la vez anterior.
/// </summary>
public static class DerrumbeEditor
{
    const string Carpeta = "Assets/Assets/3d/Bakcground/city-ruins-environment/Derrumbe";
    const string NombreShader = "Amistad/Fuego VFX";
    const string RutaRuido = "Assets/Assets/shaders/TexturaS/Ruido_Fuego.png";
    const string RutaBrasas = "Assets/Assets/shaders/FuegoVFX/Fuego_Brasas.mat";
    const string NombrePista = "Derrumbe";

    // ---- Ajustes ----
    const int CapaSim = 31;               // capa libre que se usa solo durante la simulación
    const float DT = 1f / 240f;           // paso de física
    const float DuracionSim = 7f;         // segundos simulados
    const float FPS = 30f;                // cuadros grabados por segundo
    const float AlturaMC = 2.0f;          // metros que mide el personaje de referencia (MEMO_PERFIL)
    const double InicioEnTimeline = 18.8; // segundo del Timeline maestro en que empieza el clip (primer edificio)
    // Para otros edificios (los fija la herramienta "Derrumbar edificio seleccionado"):
    static double s_inicio = InicioEnTimeline;     // segundo del principal
    static Vector3? s_refVista = null;              // hacia dónde cae (la cámara que lo ve); null = Memi
    static bool s_enCiudad = false;                 // pista en el Timeline de la ciudad (no en el principal)
    static bool s_explosion = false;                // explosión antes de caer
    const float VelocidadClip = 0.7f;     // < 1 = cámara lenta (se cambia en el clip: Speed Multiplier)
    const float InicioFalla = 0.15f;      // segundos de calma antes de que fallen las columnas
    const float GiroInicial = 0.35f;      // rad/s con que se ladea el bloque al fallar la planta baja
    const float GiroExtra = 0.12f;        // rad/s extra en cada piso que cae
    const float GiroDireccion = 25f;      // grados que se gira la caída (que va hacia la cámara) hacia la derecha de cámara.
                                          // Así cae por el hueco entre las dos paredes de fuego, delante de MC.
    const float VidaPolvo = 8f;           // segundos extra para que el polvo se disipe
    const int Semilla = 20261021;

    static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position,
        ParticleSystemVertexStream.Color,
        ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.StableRandomX,
        ParticleSystemVertexStream.AgePercent
    };

    class Pieza
    {
        public List<int>[] tris;
        public Bounds b;
        public bool losa, columna, dinamica, enMarco, apoyada;
        public float h;
        public int piso = -1, losaIndice = -1;
        public GameObject go;
        public Collider col;
        public Rigidbody rb;
        public float masa;
        public Vector3 pos0;
        public Vector3 vPrev;
    }

    struct Evento { public float t; public Vector3 pos; public float e; public int tipo; public Vector3 tam; }

    [MenuItem("Tools/Amistad/2 · Ciudad/Derrumbar edificio (ruim.008)", false, 204)]
    static void Hornear()
    {
        var escena = SceneManager.GetActiveScene();
        var todos = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);

        Transform edificio = Selection.activeTransform;
        if (edificio == null || !edificio.name.StartsWith("ruim") || edificio.GetComponent<MeshFilter>() == null) edificio = T("ruim.008");
        var city = T("CITY");
        if (edificio == null || city == null) { EditorUtility.DisplayDialog("Derrumbe", "No encontré ruim.008 o CITY en la escena abierta.", "OK"); return; }
        var mf = edificio.GetComponent<MeshFilter>();
        var mrSrc = edificio.GetComponent<MeshRenderer>();
        var src = mf != null ? mf.sharedMesh : null;
        if (src == null || mrSrc == null) { EditorUtility.DisplayDialog("Derrumbe", edificio.name + " no tiene malla.", "OK"); return; }
        var mats = mrSrc.sharedMaterials;

        string nombre = edificio.name.Replace(".", "");
        if (edificio.parent != null && edificio.parent != city) nombre += "_" + edificio.parent.name;   // nombres únicos: hay varios ruim.008
        string nombreRaiz = "DERRUMBE_" + nombre;
        var log = new StringBuilder();
        Random.InitState(Semilla);
        float R(float a, float b) => Random.Range(a, b);

        // ---------- Escala del mundo ----------
        float alturaPersonaje = 17f;
        var memo = T("MEMO_PERFIL");
        if (memo != null)
        {
            var rs = memo.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0) { var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); alturaPersonaje = Mathf.Clamp(bb.size.y, 1f, 500f); }
        }
        float uPorMetro = alturaPersonaje / AlturaMC;
        float g = 9.81f * uPorMetro;
        float suelo = city.position.y;
        var plano = T("Plane");
        if (plano != null && plano.IsChildOf(city)) suelo = plano.position.y;
        log.AppendLine($"Personaje {alturaPersonaje:0.0} u = {AlturaMC} m -> {uPorMetro:0.00} u/m, gravedad {g:0.0} u/s², suelo y={suelo:0.00}");

        // ---------- Borrar lo anterior ----------
        foreach (var prev in todos.Where(t => t != null && t.name == nombreRaiz).ToList())
            if (prev != null) Undo.DestroyObjectImmediate(prev.gameObject);
        todos = todos.Where(t => t != null).ToList();
        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets/Assets/3d/Bakcground/city-ruins-environment", "Derrumbe");
        string rutaMallas = $"{Carpeta}/{nombre}_piezas.asset";
        string rutaDatos = $"{Carpeta}/{nombre}_derrumbe.bytes";
        AssetDatabase.DeleteAsset(rutaMallas);
        AssetDatabase.DeleteAsset(rutaDatos);

        // ---------- 1. Malla en coordenadas de mundo ----------
        var v = src.vertices; var nrm = src.normals; var tan = src.tangents; var uv = src.uv; var uv2 = src.uv2; var colr = src.colors;
        var M = edificio.localToWorldMatrix;
        var w = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) w[i] = M.MultiplyPoint3x4(v[i]);
        bool hayN = nrm.Length == v.Length, hayT = tan.Length == v.Length, hayUV = uv.Length == v.Length, hayUV2 = uv2.Length == v.Length, hayC = colr.Length == v.Length;
        var wn = hayN ? nrm.Select(n => M.MultiplyVector(n).normalized).ToArray() : null;
        float signo = M.determinant < 0 ? -1f : 1f;
        var wt = hayT ? tan.Select(t => { var d = M.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized; return new Vector4(d.x, d.y, d.z, t.w * signo); }).ToArray() : null;

        // ---------- 2. Islas (piezas sueltas) ----------
        int nSub = src.subMeshCount;
        var trisSub = new int[nSub][];
        for (int s = 0; s < nSub; s++) trisSub[s] = src.GetTriangles(s);
        var clave = new Dictionary<Vector3Int, int>();
        var id = new int[w.Length];
        const float q = 0.001f;
        for (int i = 0; i < w.Length; i++)
        {
            var k = new Vector3Int(Mathf.RoundToInt(w[i].x / q), Mathf.RoundToInt(w[i].y / q), Mathf.RoundToInt(w[i].z / q));
            if (!clave.TryGetValue(k, out id[i])) { id[i] = clave.Count; clave[k] = id[i]; }
        }
        var uf = new int[clave.Count];
        for (int i = 0; i < uf.Length; i++) uf[i] = i;
        int F(int x) { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; }
        void U(int a, int b) { a = F(a); b = F(b); if (a != b) uf[b] = a; }
        for (int s = 0; s < nSub; s++)
            for (int t = 0; t < trisSub[s].Length; t += 3) { U(id[trisSub[s][t]], id[trisSub[s][t + 1]]); U(id[trisSub[s][t]], id[trisSub[s][t + 2]]); }
        var porRaiz = new Dictionary<int, Pieza>();
        var piezas = new List<Pieza>();
        for (int s = 0; s < nSub; s++)
        {
            var tr = trisSub[s];
            for (int t = 0; t < tr.Length; t += 3)
            {
                int r = F(id[tr[t]]);
                if (!porRaiz.TryGetValue(r, out var p))
                {
                    p = new Pieza { tris = new List<int>[nSub], b = new Bounds(w[tr[t]], Vector3.zero) };
                    for (int k = 0; k < nSub; k++) p.tris[k] = new List<int>();
                    porRaiz[r] = p; piezas.Add(p);
                }
                for (int j = 0; j < 3; j++) { p.tris[s].Add(tr[t + j]); p.b.Encapsulate(w[tr[t + j]]); }
            }
        }

        // ---------- 3. Clasificar: marco (losas + columnas) vs escombro ----------
        foreach (var p in piezas)
        {
            var s = p.b.size;
            p.columna = s.y > 4f && s.y > 1.5f * Mathf.Max(s.x, s.z);
            p.losa = s.x > 50f && s.z > 50f && s.y < 30f;
        }
        var losas = piezas.Where(p => p.losa).ToList();
        if (losas.Count < 2) { EditorUtility.DisplayDialog("Derrumbe", "No encontré losas en " + edificio.name + " (esta herramienta es para edificios de losas y columnas).", "OK"); return; }
        var techo = losas.OrderByDescending(p => p.b.max.y).First();
        var rect = techo.b; rect.Expand(new Vector3(24f, 0f, 24f));
        foreach (var p in piezas)
        {
            var c = p.b.center;
            p.enMarco = c.x >= rect.min.x && c.x <= rect.max.x && c.z >= rect.min.z && c.z <= rect.max.z;
        }
        var losasMarco = losas.Where(p => p.enMarco).ToList();
        var columnas = piezas.Where(p => p.enMarco && p.columna).ToList();

        // Eje del edificio (ya viene inclinado): promedio de la dirección de las columnas
        Vector3 eje = Vector3.zero;
        foreach (var c in columnas)
        {
            Vector3 arriba = Vector3.zero, abajo = Vector3.zero; int na = 0, nb = 0;
            foreach (var lista in c.tris) foreach (var idx in lista)
                { if (w[idx].y > c.b.center.y) { arriba += w[idx]; na++; } else { abajo += w[idx]; nb++; } }
            if (na > 0 && nb > 0) eje += (arriba / na - abajo / nb).normalized;
        }
        eje = eje.sqrMagnitude > 1e-6f ? eje.normalized : Vector3.up;
        if (eje.y < 0f) eje = -eje;
        var base0 = losasMarco.OrderBy(p => p.b.center.y).First();
        foreach (var p in piezas) p.h = Vector3.Dot(p.b.center - base0.b.center, eje);
        losasMarco = losasMarco.OrderBy(p => p.h).ToList();
        for (int i = 0; i < losasMarco.Count; i++) losasMarco[i].losaIndice = i;
        float[] hs = losasMarco.Select(p => p.h).ToArray();
        int m = losasMarco.Count - 1;

        foreach (var p in piezas)
        {
            if (!p.enMarco || p == base0 || (p.losa && p.losaIndice < 0)) { p.dinamica = false; continue; }
            if (p.losa) { p.dinamica = true; continue; }
            bool tocaSuelo = p.b.min.y < suelo + 0.8f;
            if (!p.columna && (tocaSuelo || p.h < 0f)) { p.dinamica = false; continue; }
            p.dinamica = true;
            int k = 0;
            for (int j = 0; j <= m; j++) if (hs[j] < p.h) k = j;
            p.piso = k;
        }
        if (!piezas.Any(p => p.dinamica)) { EditorUtility.DisplayDialog("Derrumbe", "No quedaron piezas para derrumbar.", "OK"); return; }
        var bMarco = piezas.First(p => p.dinamica).b;
        foreach (var p in piezas) if (p.dinamica) bMarco.Encapsulate(p.b);
        Vector3 centroMarco = bMarco.center;

        // Piezas grandes recargadas en el edificio (p. ej. una losa caída apoyada atrás): caen junto con él
        var bApoyo = bMarco; bApoyo.Expand(8f);
        foreach (var p in piezas)
            if (!p.dinamica && !p.enMarco && p.b.max.y > hs[Mathf.Min(2, m)] && p.b.Intersects(bApoyo))
            { p.dinamica = true; p.apoyada = true; p.piso = 0; }

        var dinamicas = piezas.Where(p => p.dinamica).OrderBy(p => p.h).ToList();
        var estaticas = piezas.Where(p => !p.dinamica).ToList();
        var porPiso = new List<Pieza>[m + 1];
        for (int k = 0; k <= m; k++) porPiso[k] = dinamicas.Where(p => !p.losa && p.piso == k).ToList();

        // Dirección de caída: hacia la cámara, girada un poco a la derecha (por el hueco entre las paredes de fuego)
        Vector3 inclin = Vector3.ProjectOnPlane(eje, Vector3.up);
        Vector3 refV = s_refVista ?? (memo != null ? memo.position : centroMarco - Vector3.forward);
        Vector3 vista = Vector3.ProjectOnPlane(centroMarco - refV, Vector3.up).normalized;
        Vector3 izq = Vector3.Cross(vista, Vector3.up).normalized;
        Vector3 dir = Quaternion.AngleAxis(-GiroDireccion, Vector3.up) * (-vista);
        if (Vector3.Dot(dir, -izq) < 0f) dir = Quaternion.AngleAxis(GiroDireccion, Vector3.up) * (-vista);
        Vector3 lado = Vector3.Cross(Vector3.up, dir).normalized;
        float E = dinamicas.Max(p => Mathf.Abs(Vector3.Dot(p.b.center - centroMarco, dir)));
        float corte = -0.6f * E;
        log.AppendLine($"Piezas {piezas.Count}: dinámicas {dinamicas.Count}, estáticas {estaticas.Count}. Losas del marco {losasMarco.Count}, eje {eje}, inclinación {Vector3.Angle(eje, Vector3.up):0.0}°");
        log.AppendLine($"Dirección de caída {dir} (inclinación original {inclin.normalized}), izquierda de cámara {izq}; apoyadas {dinamicas.Count(p => p.apoyada)}");
        for (int k = 0; k <= m; k++) log.AppendLine($"  piso {k}: losa h={hs[k]:0.0} y={losasMarco[k].b.center.y:0.0}, piezas {porPiso[k].Count} (columnas {porPiso[k].Count(p => p.columna)})");

        // ---------- 4. Objetos y mallas ----------
        var raiz = new GameObject(nombreRaiz);
        Undo.RegisterCreatedObjectUndo(raiz, "Derrumbe");
        raiz.transform.SetParent(city, false);
        var lc = city.lossyScale;
        raiz.transform.localScale = new Vector3(1f / lc.x, 1f / lc.y, 1f / lc.z);
        raiz.transform.rotation = Quaternion.identity;
        raiz.transform.position = new Vector3(centroMarco.x, suelo, centroMarco.z);

        var mallas = new List<Mesh>();
        Mesh Construir(IEnumerable<Pieza> grupo, Vector3 pivote, string nombreMalla, out Material[] matsOut)
        {
            var mapa = new Dictionary<int, int>();
            var pv = new List<Vector3>(); var pn = new List<Vector3>(); var pt = new List<Vector4>();
            var pu = new List<Vector2>(); var pu2 = new List<Vector2>(); var pc = new List<Color>();
            var listas = new List<int>[nSub];
            for (int s = 0; s < nSub; s++) listas[s] = new List<int>();
            foreach (var p in grupo)
                for (int s = 0; s < nSub; s++)
                    foreach (var idx in p.tris[s])
                    {
                        if (!mapa.TryGetValue(idx, out int j))
                        {
                            j = pv.Count; mapa[idx] = j;
                            pv.Add(w[idx] - pivote);
                            if (hayN) pn.Add(wn[idx]);
                            if (hayT) pt.Add(wt[idx]);
                            if (hayUV) pu.Add(uv[idx]);
                            if (hayUV2) pu2.Add(uv2[idx]);
                            if (hayC) pc.Add(colr[idx]);
                        }
                        listas[s].Add(j);
                    }
            var malla = new Mesh { name = nombreMalla };
            if (pv.Count > 65000) malla.indexFormat = IndexFormat.UInt32;
            malla.SetVertices(pv);
            if (hayN) malla.SetNormals(pn);
            if (hayT) malla.SetTangents(pt);
            if (hayUV) malla.SetUVs(0, pu);
            if (hayUV2) malla.SetUVs(1, pu2);
            if (hayC) malla.SetColors(pc);
            var usadas = Enumerable.Range(0, nSub).Where(s => listas[s].Count > 0).ToList();
            malla.subMeshCount = usadas.Count;
            for (int k = 0; k < usadas.Count; k++) malla.SetTriangles(listas[usadas[k]], k);
            malla.RecalculateBounds();
            matsOut = usadas.Select(s => mats[Mathf.Min(s, mats.Length - 1)]).ToArray();
            mallas.Add(malla);
            return malla;
        }
        GameObject Crear(string n, Mesh malla, Material[] mt, Vector3 pivote)
        {
            var go = new GameObject(n);
            go.transform.SetParent(raiz.transform, false);
            go.transform.position = pivote;
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = edificio.gameObject.layer;
            go.AddComponent<MeshFilter>().sharedMesh = malla;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = mt;
            r.shadowCastingMode = mrSrc.shadowCastingMode;
            r.receiveShadows = mrSrc.receiveShadows;
            r.lightProbeUsage = mrSrc.lightProbeUsage;
            r.reflectionProbeUsage = mrSrc.reflectionProbeUsage;
            return go;
        }

        var mEst = Construir(estaticas, raiz.transform.position, nombre + "_estatico", out var matsEst);
        var goEst = Crear(nombre + "_estatico", mEst, matsEst, raiz.transform.position);
        for (int i = 0; i < dinamicas.Count; i++)
        {
            var p = dinamicas[i];
            var malla = Construir(new[] { p }, p.b.center, $"{nombre}_pieza_{i:000}", out var mt);
            p.go = Crear($"pieza_{i:000}", malla, mt, p.b.center);
            p.pos0 = p.go.transform.position;
        }
        AssetDatabase.CreateAsset(mallas[0], rutaMallas);
        for (int i = 1; i < mallas.Count; i++) AssetDatabase.AddObjectToAsset(mallas[i], mallas[0]);
        AssetDatabase.SaveAssets();

        // ---------- 5. Simulación ----------
        int pasos = Mathf.RoundToInt(DuracionSim / DT);
        int cadaN = Mathf.Max(1, Mathf.RoundToInt(1f / (FPS * DT)));
        int cuadros = pasos / cadaN + 1;
        int nD = dinamicas.Count;
        var datos = new float[cuadros * nD * 7];
        var eventos = new List<Evento>();
        var csv = new StringBuilder("t,techo_x,techo_y,techo_z,techo_inclinacion,max_y\n");
        int nIgnorados = 0;

        var gravedad0 = Physics.gravity;
        var modo0 = Physics.simulationMode;
        int iter0 = Physics.defaultSolverIterations, iterV0 = Physics.defaultSolverVelocityIterations;
        float rebote0 = Physics.bounceThreshold;
        var capas0 = new bool[32];
        for (int i = 0; i < 32; i++) capas0[i] = Physics.GetIgnoreLayerCollision(CapaSim, i);
        var ajenos = new List<Rigidbody>();
        var temporales = new List<Object>();
        int capaOriginal = edificio.gameObject.layer;

        try
        {
            EditorUtility.DisplayProgressBar("Derrumbe", "Preparando la física…", 0f);
            Physics.simulationMode = SimulationMode.Script;
            Physics.gravity = new Vector3(0f, -g, 0f);
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.bounceThreshold = 2f * uPorMetro;
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(CapaSim, i, i != CapaSim);
            foreach (var rb in Object.FindObjectsByType<Rigidbody>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (!rb.isKinematic) { ajenos.Add(rb); rb.isKinematic = true; }

            var matFis = new PhysicsMaterial("derrumbe") { dynamicFriction = 0.6f, staticFriction = 0.75f, bounciness = 0.04f, frictionCombine = PhysicsMaterialCombine.Average, bounceCombine = PhysicsMaterialCombine.Minimum };
            temporales.Add(matFis);

            GameObject Suelo(string n, float y)
            {
                var go = new GameObject(n) { layer = CapaSim };
                go.transform.position = new Vector3(centroMarco.x, y - 5f, centroMarco.z);
                var bc = go.AddComponent<BoxCollider>(); bc.size = new Vector3(5000f, 10f, 5000f); bc.sharedMaterial = matFis;
                temporales.Add(go);
                return go;
            }
            Suelo("_suelo_sim", suelo);
            Suelo("_suelo_seguro", suelo - 20f);
            goEst.layer = CapaSim;
            var mcEst = goEst.AddComponent<MeshCollider>(); mcEst.sharedMesh = mEst; mcEst.convex = false; mcEst.sharedMaterial = matFis;
            temporales.Add(mcEst);

            foreach (var p in dinamicas)
            {
                p.go.layer = CapaSim;
                var s = p.b.size;
                float minDim = Mathf.Min(s.x, Mathf.Min(s.y, s.z));
                if (minDim < 0.3f)
                {
                    var bx = p.go.AddComponent<BoxCollider>();
                    bx.size = Vector3.Max(s - Vector3.one * 0.4f, Vector3.one * 0.3f);
                    p.col = bx;
                }
                else
                {
                    var mc = p.go.AddComponent<MeshCollider>();
                    var cm = MallaColision(p.go.GetComponent<MeshFilter>().sharedMesh, p.columna ? eje : Vector3.zero);
                    temporales.Add(cm);
                    mc.sharedMesh = cm;
                    mc.convex = true;
                    p.col = mc;
                }
                p.col.sharedMaterial = matFis;
                p.col.contactOffset = 0.08f;
                p.masa = Mathf.Max(1f, Mathf.Pow(Mathf.Max(1f, s.x * s.y * s.z * 0.5f), 0.75f));
            }
            Physics.SyncTransforms();

            var colEstatico = (Collider)mcEst;
            void IgnorarSolapes(Pieza p, bool conEstatico = true)
            {
                Physics.SyncTransforms();
                var b = p.col.bounds;
                foreach (var o in dinamicas)
                {
                    if (o == p || o.col == null || !b.Intersects(o.col.bounds)) continue;
                    if (Physics.ComputePenetration(p.col, p.col.transform.position, p.col.transform.rotation, o.col, o.col.transform.position, o.col.transform.rotation, out _, out float d) && d > 0.15f)
                    { Physics.IgnoreCollision(p.col, o.col, true); nIgnorados++; }
                }
                if (conEstatico && b.Intersects(colEstatico.bounds) &&
                    Physics.ComputePenetration(p.col, p.col.transform.position, p.col.transform.rotation, colEstatico, colEstatico.transform.position, colEstatico.transform.rotation, out _, out float de) && de > 0.15f)
                { Physics.IgnoreCollision(p.col, colEstatico, true); nIgnorados++; }
            }

            var individuales = new List<Pieza>();
            void Individual(Pieza p, Vector3 vel, Vector3 angVel)
            {
                p.go.transform.SetParent(raiz.transform, true);
                var rb = p.go.AddComponent<Rigidbody>();
                rb.mass = p.masa;
                rb.linearDamping = 0.02f;
                rb.angularDamping = 0.2f;
                rb.maxDepenetrationVelocity = 4f;
                rb.maxAngularVelocity = 12f;
                rb.solverIterations = 12;
                rb.sleepThreshold = 0.005f * uPorMetro * uPorMetro;
                rb.linearVelocity = vel;
                rb.angularVelocity = angVel;
                p.rb = rb; p.vPrev = vel;
                individuales.Add(p);
            }

            // Las columnas de un piso fallan: las del lado de la caída salen disparadas, las otras se ladean
            void Fallar(int k, System.Func<Pieza, Vector3> velDe, Vector3 angBase, float t)
            {
                Vector3 centroPiso = Vector3.zero;
                foreach (var p in porPiso[k]) centroPiso += p.go.transform.position;
                if (porPiso[k].Count > 0) centroPiso /= porPiso[k].Count;
                foreach (var p in porPiso[k])
                {
                    Vector3 patada;
                    if (k == 0)
                    {
                        // Planta baja: revientan las columnas del lado de la caída, las demás se ladean
                        float proy = Vector3.Dot(p.b.center - centroMarco, dir);
                        if (p.apoyada) patada = dir * R(2f, 6f);
                        else if (!p.columna)
                        {
                            // Escombro de planta baja: sale empujado hacia afuera para no detener la caída
                            var radial = Vector3.ProjectOnPlane(p.go.transform.position - centroPiso, Vector3.up);
                            radial = radial.sqrMagnitude > 1e-4f ? radial.normalized : dir;
                            patada = (radial + dir).normalized * R(14f, 26f) + Vector3.up * R(4f, 10f);
                        }
                        else if (proy > corte) patada = dir * R(18f, 32f) + lado * R(-8f, 8f) + Vector3.up * R(2f, 8f);
                        else
                        {
                            var radial = Vector3.ProjectOnPlane(p.go.transform.position - centroPiso, Vector3.up);
                            radial = radial.sqrMagnitude > 1e-4f ? radial.normalized : dir;
                            patada = radial * R(8f, 16f) + dir * R(0f, 6f);
                        }
                    }
                    else
                    {
                        // Pisos de arriba: el golpe avienta las columnas hacia afuera
                        var radial = Vector3.ProjectOnPlane(p.go.transform.position - centroPiso, Vector3.up);
                        radial = radial.sqrMagnitude > 1e-4f ? radial.normalized : dir;
                        patada = radial * R(10f, 26f) + dir * R(2f, 10f) + Vector3.up * R(0f, 5f);
                    }
                    Individual(p, velDe(p) + patada, angBase + Random.onUnitSphere * R(1.5f, 4f));
                }
                foreach (var p in porPiso[k]) IgnorarSolapes(p, p.columna || p.apoyada);
                // Las columnas que fallan ya no cargan: el bloque de arriba cae libre hasta la losa de abajo
                var arriba = Miembros(k);
                foreach (var p in porPiso[k])
                    if (!p.apoyada)
                        foreach (var o in arriba) if (o.col != null) Physics.IgnoreCollision(p.col, o.col, true);
                if (porPiso[k].Count > 0)
                {
                    var bb = porPiso[k][0].col.bounds; foreach (var p in porPiso[k]) bb.Encapsulate(p.col.bounds);
                    eventos.Add(new Evento { t = t, pos = new Vector3(bb.center.x, Mathf.Max(suelo, bb.min.y) + 3f, bb.center.z), e = 1f, tipo = 3, tam = bb.size });
                }
            }

            // Bloque: todo lo que está arriba del piso k cae como una sola pieza (no se deshace en el aire)
            GameObject bloqueGO = null; Rigidbody bloque = null; int bloqueK = -1;
            float bloqueT0 = 0f, bloqueY0 = 0f, bloqueVyMin = 0f;
            List<Pieza> Miembros(int k) => losasMarco.Skip(k + 1).Concat(Enumerable.Range(k + 1, Mathf.Max(0, m - k)).SelectMany(j => porPiso[j])).ToList();
            Vector3 Com(List<Pieza> ps)
            {
                Vector3 s = Vector3.zero; float tm = 0f;
                foreach (var p in ps) { s += p.go.transform.position * p.masa; tm += p.masa; }
                return tm > 0f ? s / tm : Vector3.zero;
            }
            void CrearBloque(int k, System.Func<Vector3, Vector3> velEn, Vector3 angBase, float giro, float t)
            {
                bloque = null; bloqueGO = null;
                var miembros = Miembros(k);
                if (miembros.Count == 0) return;
                var com = Com(miembros);
                var go = new GameObject("_bloque_" + k) { layer = CapaSim };
                go.transform.position = com;
                foreach (var p in miembros) p.go.transform.SetParent(go.transform, true);
                var rb = go.AddComponent<Rigidbody>();
                rb.mass = miembros.Sum(p => p.masa);
                rb.linearDamping = 0.01f;
                rb.angularDamping = 0.05f;
                rb.maxDepenetrationVelocity = 4f;
                rb.maxAngularVelocity = 8f;
                rb.solverIterations = 16;
                // Gira apoyado en su borde de abajo: la parte de abajo casi no se mueve, la de arriba se va hacia "dir"
                float yPivote = losasMarco[k + 1].go.transform.position.y - losasMarco[k + 1].b.extents.y;
                rb.linearVelocity = velEn(com) + dir * giro * Mathf.Max(0f, com.y - yPivote);
                rb.angularVelocity = angBase + lado * giro;
                bloque = rb; bloqueGO = go; bloqueK = k; bloqueT0 = t; bloqueY0 = float.NaN; bloqueVyMin = 0f;
                temporales.Add(go);
            }

            void Separar(float t)
            {
                int k = bloqueK;
                var prev = bloque;
                Vector3 W = prev.angularVelocity;
                var vels = new Dictionary<Pieza, Vector3>();
                foreach (var p in Miembros(k)) vels[p] = prev.GetPointVelocity(p.go.transform.position);
                var siguientes = Miembros(k + 1);
                var comSig = siguientes.Count > 0 ? Com(siguientes) : Vector3.zero;
                Vector3 vSig = prev.GetPointVelocity(comSig);
                foreach (var p in Miembros(k)) p.go.transform.SetParent(raiz.transform, true);
                temporales.Remove(bloqueGO);
                Object.DestroyImmediate(bloqueGO);
                bloque = null; bloqueGO = null;

                var losa = losasMarco[k + 1];
                Individual(losa, vels[losa], W);
                var bl = losa.col.bounds;
                eventos.Add(new Evento { t = t, pos = new Vector3(bl.center.x, bl.center.y, bl.center.z), e = 1f, tipo = 2, tam = losa.b.size });
                Fallar(k + 1, p => vels.TryGetValue(p, out var vv) ? vv : vSig, W, t);
                IgnorarSolapes(losa);
                if (k + 1 < m) CrearBloque(k + 1, _ => vSig, W, GiroExtra, t);
            }

            void Grabar(int c, float t)
            {
                int o = c * nD * 7;
                var inv = Quaternion.Inverse(raiz.transform.rotation);
                float maxY = float.MinValue;
                foreach (var p in dinamicas)
                {
                    var tr = p.go.transform;
                    var lp = raiz.transform.InverseTransformPoint(tr.position);
                    var lq = inv * tr.rotation;
                    datos[o++] = lp.x; datos[o++] = lp.y; datos[o++] = lp.z;
                    datos[o++] = lq.x; datos[o++] = lq.y; datos[o++] = lq.z; datos[o++] = lq.w;
                    maxY = Mathf.Max(maxY, tr.position.y);
                }
                var tt = losasMarco[m].go.transform;
                csv.AppendLine($"{t:0.000},{tt.position.x:0.0},{tt.position.y:0.0},{tt.position.z:0.0},{Vector3.Angle(tt.rotation * eje, eje):0.0},{maxY:0.0}");
            }

            Grabar(0, 0f);
            int cuadro = 1;
            bool fallo = false;
            for (int paso = 1; paso <= pasos; paso++)
            {
                float t = paso * DT;
                if (!fallo && t >= InicioFalla)
                {
                    fallo = true;
                    Fallar(0, _ => Vector3.zero, Vector3.zero, t);
                    CrearBloque(0, _ => Vector3.zero, Vector3.zero, GiroInicial, t);
                    log.AppendLine($"t={t:0.00}: fallan las columnas de planta baja ({porPiso[0].Count} piezas)");
                }
                Physics.Simulate(DT);

                if (bloque != null)
                {
                    float vy = bloque.linearVelocity.y;
                    if (float.IsNaN(bloqueY0)) bloqueY0 = bloque.worldCenterOfMass.y;
                    bloqueVyMin = Mathf.Min(bloqueVyMin, vy);
                    float caida = bloqueY0 - bloque.worldCenterOfMass.y;
                    float hPiso = Mathf.Max(1f, hs[bloqueK + 1] - hs[bloqueK]);
                    bool golpe = caida > 0.45f * hPiso && bloqueVyMin < -0.3f * Mathf.Sqrt(2f * g * hPiso) && vy > 0.35f * bloqueVyMin;
                    if (golpe || caida > 0.7f * hPiso || t - bloqueT0 > 0.9f)
                    {
                        log.AppendLine($"t={t:0.00}: aterriza el bloque sobre el piso {bloqueK} (cayó {caida:0.0} u, vel {bloqueVyMin:0.0} u/s{(golpe ? "" : ", por tiempo")})");
                        Separar(t);
                    }
                }

                foreach (var p in individuales)
                {
                    var vn = p.rb.linearVelocity;
                    var dv = vn - p.vPrev;
                    if (dv.magnitude > 22f && p.vPrev.magnitude > 18f)
                        eventos.Add(new Evento { t = t, pos = p.rb.worldCenterOfMass, e = p.masa * dv.sqrMagnitude, tipo = 1 });
                    p.vPrev = vn;
                }

                if (paso % cadaN == 0 && cuadro < cuadros) Grabar(cuadro++, t);
                if (paso % 60 == 0) EditorUtility.DisplayProgressBar("Derrumbe", $"Simulando {t:0.0} / {DuracionSim:0} s", t / DuracionSim);
            }
            while (cuadro < cuadros) Grabar(cuadro++, DuracionSim);

            // Resumen del resultado
            var bFin = new Bounds(dinamicas[0].go.transform.position, Vector3.zero);
            foreach (var p in dinamicas) bFin.Encapsulate(p.go.transform.position);
            log.AppendLine($"Final: centros de piezas y {bFin.min.y:0.0}..{bFin.max.y:0.0}, x {bFin.min.x:0.0}..{bFin.max.x:0.0}, z {bFin.min.z:0.0}..{bFin.max.z:0.0}");
            log.AppendLine($"Piezas debajo del suelo: {dinamicas.Count(p => p.go.transform.position.y < suelo - 3f)}; pares ignorados por empezar encimados: {nIgnorados}; eventos de impacto {eventos.Count}");
        }
        finally
        {
            // Dejar todo como estaba
            foreach (var p in dinamicas)
            {
                if (p.go == null) continue;
                p.go.transform.SetParent(raiz.transform, true);
                var rb = p.go.GetComponent<Rigidbody>(); if (rb != null) Object.DestroyImmediate(rb);
                foreach (var c in p.go.GetComponents<Collider>()) Object.DestroyImmediate(c);
                p.go.transform.SetPositionAndRotation(p.pos0, Quaternion.identity);
                p.go.layer = capaOriginal;
            }
            if (goEst != null) goEst.layer = capaOriginal;
            foreach (var o in temporales) if (o != null) Object.DestroyImmediate(o);
            Physics.gravity = gravedad0;
            Physics.simulationMode = modo0;
            Physics.defaultSolverIterations = iter0;
            Physics.defaultSolverVelocityIterations = iterV0;
            Physics.bounceThreshold = rebote0;
            for (int i = 0; i < 32; i++) Physics.IgnoreLayerCollision(CapaSim, i, capas0[i]);
            foreach (var rb in ajenos) if (rb != null) rb.isKinematic = false;
            EditorUtility.ClearProgressBar();
        }

        // ---------- 6. Guardar los datos ----------
        using (var ms = new MemoryStream())
        using (var bw = new BinaryWriter(ms))
        {
            bw.Write(0x42524544); bw.Write(nD); bw.Write(cuadros); bw.Write(FPS);
            foreach (var f in datos) bw.Write(f);
            bw.Flush();
            File.WriteAllBytes(Path.GetFullPath(rutaDatos), ms.ToArray());
        }
        AssetDatabase.ImportAsset(rutaDatos);
        var datosAsset = AssetDatabase.LoadAssetAtPath<TextAsset>(rutaDatos);

        // ---------- 7. Polvo y chispas ----------
        var contPolvo = new GameObject("POLVO_Y_CHISPAS");
        contPolvo.transform.SetParent(raiz.transform, false);
        var matPolvo = MaterialPolvo();
        var matChispas = AssetDatabase.LoadAssetAtPath<Material>(RutaBrasas);
        CrearPolvo(contPolvo.transform, eventos, bMarco, suelo, g, matPolvo, matChispas, log);

        var comp = raiz.AddComponent<DerrumbeHorneado>();
        comp.datos = datosAsset;
        comp.piezas = dinamicas.Select(p => p.go.transform).ToArray();
        comp.particulas = contPolvo.GetComponentsInChildren<ParticleSystem>(true);
        comp.fps = FPS;
        comp.cuadros = cuadros;

        Undo.RecordObject(edificio.gameObject, "Ocultar edificio original");
        edificio.gameObject.SetActive(false);

        float duracionLocal = DuracionSim + VidaPolvo;
        string infoTl = AgregarPista(todos, comp, duracionLocal, nombre);
        if (s_explosion) infoTl += "\n" + CrearExplosion(todos, raiz.transform, edificio, mats, suelo);
        log.AppendLine(infoTl);

        File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/derrumbe_" + nombre + ".txt"), log.ToString());
        File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/derrumbe_" + nombre + ".csv"), csv.ToString());
        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeGameObject = raiz;
        Debug.Log("[Derrumbe] listo. " + infoTl + " Detalles en Logs/derrumbe_" + nombre + ".txt");
    }

    // Malla de colisión un poco más chica que la visible (para que las piezas no empiecen encimadas).
    // Las columnas se acortan más a lo largo de su eje.
    static Mesh MallaColision(Mesh visible, Vector3 ejeColumna)
    {
        var vs = visible.vertices;
        var b = visible.bounds;
        var s = b.size;
        const float margen = 0.2f;
        var f = new Vector3(Mathf.Max(0.5f, 1f - 2f * margen / Mathf.Max(s.x, 0.01f)),
                            Mathf.Max(0.5f, 1f - 2f * margen / Mathf.Max(s.y, 0.01f)),
                            Mathf.Max(0.5f, 1f - 2f * margen / Mathf.Max(s.z, 0.01f)));
        for (int i = 0; i < vs.Length; i++)
        {
            var d = vs[i] - b.center;
            d = Vector3.Scale(d, f);
            if (ejeColumna != Vector3.zero) d -= ejeColumna * (Vector3.Dot(d, ejeColumna) * 0.2f);
            vs[i] = b.center + d;
        }
        // Si la pieza es plana (una sola cara), se le da grosor para que el collider convexo se pueda crear
        var tri = visible.triangles;
        if (tri.Length >= 3)
        {
            var n0 = Vector3.Cross(vs[tri[1]] - vs[tri[0]], vs[tri[2]] - vs[tri[0]]);
            if (n0.sqrMagnitude > 1e-12f)
            {
                n0.Normalize();
                float grosor = 0f;
                foreach (var x in vs) grosor = Mathf.Max(grosor, Mathf.Abs(Vector3.Dot(x - vs[tri[0]], n0)));
                if (grosor < 0.1f)
                {
                    int n = vs.Length;
                    var dobles = new Vector3[n * 2];
                    for (int i = 0; i < n; i++) { dobles[i] = vs[i] - n0 * 0.15f; dobles[n + i] = vs[i] + n0 * 0.15f; }
                    var t2 = new int[tri.Length * 2];
                    for (int i = 0; i < tri.Length; i++) { t2[i] = tri[i]; t2[tri.Length + i] = tri[i] + n; }
                    vs = dobles; tri = t2;
                }
            }
        }
        var m = new Mesh { name = visible.name + "_col" };
        m.SetVertices(vs);
        m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        return m;
    }

    // ================= Material del polvo =================
    static Material MaterialPolvo()
    {
        string ruta = Carpeta + "/Derrumbe_Polvo.mat";
        var sh = Shader.Find(NombreShader);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (mat == null) { mat = new Material(sh); AssetDatabase.CreateAsset(mat, ruta); }
        else if (sh != null) mat.shader = sh;
        mat.SetFloat("_Modo", 2f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        var ruido = AssetDatabase.LoadAssetAtPath<Texture2D>(RutaRuido);
        if (ruido != null) mat.SetTexture("_Ruido", ruido);
        mat.SetColor("_ColorBase", Color.white);
        mat.SetFloat("_EscalaRuido", 1f);
        mat.SetFloat("_Velocidad", 0.6f);
        mat.SetFloat("_SuavidadSuelo", 6f);
        mat.renderQueue = 3000;
        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        return mat;
    }

    // ================= Polvo y chispas según los impactos =================
    static void CrearPolvo(Transform padre, List<Evento> eventos, Bounds bMarco, float suelo, float g, Material polvo, Material chispas, StringBuilder log)
    {
        uint semilla = 11;
        var colorA = new Color(0.95f, 0.80f, 0.64f, 0.9f);
        var colorB = new Color(0.68f, 0.55f, 0.44f, 0.8f);

        // a) Cada piso que cae avienta polvo por los lados y chispas
        int n = 0;
        foreach (var e in eventos.Where(x => x.tipo == 2 || x.tipo == 3).OrderBy(x => x.t))
        {
            bool fuerte = e.tipo == 2;
            var tam = new Vector3(Mathf.Max(20f, e.tam.x), 4f, Mathf.Max(20f, e.tam.z));
            var ps = Emisor(padre, $"Polvo_Piso_{n}", e.pos, polvo, semilla++, e.t + 0.2f);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(22f, fuerte ? 70f : 40f);
            m.startSize = new ParticleSystem.MinMaxCurve(18f, 40f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
            int cant = fuerte ? 140 : 80;
            m.maxParticles = cant + 10;
            var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(e.t, (short)cant) });
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 1f; s.radiusThickness = 0.3f; s.scale = new Vector3(tam.x * 0.5f, 4f, tam.z * 0.5f);
            Frenar(ps, 2.2f);
            Velocidad(ps, 0f, 0f, 5f, 14f, 0f, 0f);
            Tamano(ps, new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 2.5f));
            Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.95f, 0.05f), new GradientAlphaKey(0.65f, 0.5f), new GradientAlphaKey(0f, 1f));
            Girar(ps, 0.3f);
            Ruido(ps, 4f, 0.04f, 0.1f);

            if (fuerte && chispas != null)
            {
                var ch = Emisor(padre, $"Chispas_Piso_{n}", e.pos, chispas, semilla++, e.t + 0.2f);
                var r = ch.GetComponent<ParticleSystemRenderer>();
                r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.02f; r.lengthScale = 1.2f; r.cameraVelocityScale = 0f;
                var mc = ch.main;
                mc.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 2.2f);
                mc.startSpeed = new ParticleSystem.MinMaxCurve(25f, 70f);
                mc.startSize = new ParticleSystem.MinMaxCurve(0.4f, 1.0f);
                mc.gravityModifier = g / 9.81f * 0.45f;
                mc.maxParticles = 110;
                var emc = ch.emission; emc.SetBursts(new[] { new ParticleSystem.Burst(e.t, (short)100) });
                var sc = ch.shape; sc.shapeType = ParticleSystemShapeType.Sphere; sc.radius = 1f; sc.scale = new Vector3(tam.x * 0.5f, 3f, tam.z * 0.5f);
                Velocidad(ch, 0f, 0f, 10f, 25f, 0f, 0f);
                Alfa(ch, new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f));
            }
            n++;
        }

        // b) Golpes de piezas sueltas: se agrupan por zona y cada zona tiene sus bocanadas de polvo
        var golpes = eventos.Where(x => x.tipo == 1).ToList();
        int nZonas = 0;
        if (golpes.Count > 0)
        {
            var zonas = golpes.GroupBy(x => new Vector2Int(Mathf.FloorToInt(x.pos.x / 35f), Mathf.FloorToInt(x.pos.z / 35f)))
                .Select(gr => new { E = gr.Sum(x => x.e), pos = gr.Aggregate(Vector3.zero, (a, x) => a + x.pos * x.e) / Mathf.Max(1e-6f, gr.Sum(x => x.e)), lista = gr.ToList() })
                .OrderByDescending(z => z.E).Take(10).ToList();
            float eTop = zonas[0].E;
            foreach (var z in zonas.Where(z => z.E > 0.03f * eTop))
            {
                var rafagas = z.lista.GroupBy(x => Mathf.FloorToInt(x.t / 0.2f))
                    .Select(gr => new { t = gr.Key * 0.2f + 0.1f, E = gr.Sum(x => x.e) })
                    .OrderByDescending(r => r.E).Take(5).OrderBy(r => r.t).ToList();
                var bursts = rafagas.Select(r => new ParticleSystem.Burst(r.t, (short)Mathf.Clamp(Mathf.RoundToInt(5f + 25f * Mathf.Sqrt(r.E / eTop)), 5, 30))).ToArray();
                var pos = z.pos; pos.y = Mathf.Max(suelo + 2f, pos.y - 3f);
                var ps = Emisor(padre, $"Polvo_Golpe_{nZonas}", pos, polvo, semilla++, rafagas.Max(r => r.t) + 0.2f);
                var m = ps.main;
                m.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
                m.startSpeed = new ParticleSystem.MinMaxCurve(8f, 26f);
                m.startSize = new ParticleSystem.MinMaxCurve(6f, 15f);
                m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                m.startColor = new ParticleSystem.MinMaxGradient(colorA, colorB);
                m.maxParticles = bursts.Sum(b => (int)b.count.constant) + 10;
                var em = ps.emission; em.SetBursts(bursts);
                var s = ps.shape; s.shapeType = ParticleSystemShapeType.Hemisphere; s.radius = 5f; s.rotation = new Vector3(-90f, 0f, 0f);
                Frenar(ps, 2.5f);
                Velocidad(ps, 0f, 0f, 2f, 6f, 0f, 0f);
                Tamano(ps, new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 2f));
                Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.8f, 0.08f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f));
                Girar(ps, 0.4f);
                nZonas++;
            }
        }

        // c) Nube general que crece mientras cae el edificio y se queda flotando
        {
            var porTiempo = new float[Mathf.CeilToInt(DuracionSim / 0.25f) + 1];
            foreach (var e in eventos) porTiempo[Mathf.Clamp(Mathf.FloorToInt(e.t / 0.25f), 0, porTiempo.Length - 1)] += e.tipo == 1 ? e.e : 0f;
            float maxE = Mathf.Max(1e-6f, porTiempo.Max());
            var pisos = eventos.Where(x => x.tipo != 1).Select(x => x.t).ToList();
            var curva = new AnimationCurve();
            for (int i = 0; i < porTiempo.Length; i++)
            {
                float t = i * 0.25f;
                float valor = Mathf.Sqrt(porTiempo[i] / maxE);
                foreach (var tp in pisos) valor = Mathf.Max(valor, Mathf.Clamp01(1f - Mathf.Abs(t - tp - 0.3f) / 0.8f));
                curva.AddKey(Mathf.Clamp01(t / DuracionSim), Mathf.Clamp01(valor));
            }
            var pos = new Vector3(bMarco.center.x, suelo + 8f, bMarco.center.z);
            var ps = Emisor(padre, "Polvo_Nube", pos, polvo, semilla++, DuracionSim);
            var m = ps.main;
            m.startLifetime = new ParticleSystem.MinMaxCurve(6f, 9f);
            m.startSpeed = new ParticleSystem.MinMaxCurve(2f, 8f);
            m.startSize = new ParticleSystem.MinMaxCurve(35f, 70f);
            m.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.70f, 0.56f, 0.8f), new Color(0.58f, 0.46f, 0.37f, 0.7f));
            m.maxParticles = 400;
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(40f, curva);
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(bMarco.size.x * 1.1f, 12f, bMarco.size.z * 1.1f);
            Frenar(ps, 0.6f);
            Velocidad(ps, 0f, 0f, 6f, 14f, 0f, 0f);
            var vol = ps.velocityOverLifetime; vol.radial = new ParticleSystem.MinMaxCurve(4f, 10f);
            Tamano(ps, new Keyframe(0f, 0.5f), new Keyframe(0.4f, 1f), new Keyframe(1f, 1.6f));
            Alfa(ps, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.6f, 0.15f), new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f));
            Girar(ps, 0.2f);
            Ruido(ps, 5f, 0.03f, 0.1f);
        }
        log.AppendLine($"Polvo: {n} emisores de piso, {nZonas} zonas de golpes, 1 nube general");
    }

    static ParticleSystem Emisor(Transform padre, string nombre, Vector3 pos, Material mat, uint semilla, float duracion)
    {
        var go = new GameObject(nombre);
        go.transform.SetParent(padre, false);
        go.transform.SetPositionAndRotation(pos, Quaternion.identity);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = semilla;
        var m = ps.main;
        m.duration = Mathf.Max(0.1f, duracion);
        m.loop = false;
        m.prewarm = false;
        m.playOnAwake = false;
        m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.scalingMode = ParticleSystemScalingMode.Local;
        m.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;
        m.startColor = Color.white;
        var em = ps.emission; em.rateOverTime = 0f;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.alignment = ParticleSystemRenderSpace.View;
        r.sortMode = ParticleSystemSortMode.Distance;
        r.maxParticleSize = 2f;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.SetActiveVertexStreams(Streams);
        return ps;
    }

    static void Frenar(ParticleSystem ps, float arrastre)
    {
        var lv = ps.limitVelocityOverLifetime;
        lv.enabled = true;
        lv.limit = 1000f;
        lv.dampen = 0f;
        lv.drag = arrastre;
        lv.multiplyDragByParticleSize = false;
        lv.multiplyDragByParticleVelocity = false;
    }

    static void Velocidad(ParticleSystem ps, float x0, float x1, float y0, float y1, float z0, float z1)
    {
        var v = ps.velocityOverLifetime;
        v.enabled = true;
        v.space = ParticleSystemSimulationSpace.World;
        v.x = new ParticleSystem.MinMaxCurve(x0, x1);
        v.y = new ParticleSystem.MinMaxCurve(y0, y1);
        v.z = new ParticleSystem.MinMaxCurve(z0, z1);
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
        var gr = new Gradient();
        gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) }, a);
        col.color = new ParticleSystem.MinMaxGradient(gr);
    }

    static void Girar(ParticleSystem ps, float radPorSeg)
    {
        var rot = ps.rotationOverLifetime;
        rot.enabled = true;
        rot.z = new ParticleSystem.MinMaxCurve(-radPorSeg, radPorSeg);
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

    // ================= Timeline =================
    static string AgregarPista(List<Transform> todos, DerrumbeHorneado comp, float duracionLocal, string nombreEdificio)
    {
        string nombrePista = NombrePista + " " + nombreEdificio;
        foreach (var dn in new[] { "Director_P1_Bosque", "Director_P2_Ciudad" })
        {
            var d = todos.FirstOrDefault(t => t != null && t.name == dn)?.GetComponent<PlayableDirector>();
            var ta = d != null ? d.playableAsset as TimelineAsset : null;
            if (ta == null) continue;
            foreach (var vieja in ta.GetOutputTracks().OfType<DerrumbeTrack>().Where(t => t.name == nombrePista).ToList()) ta.DeleteTrack(vieja);
            EditorUtility.SetDirty(ta);
        }
        var director = todos.FirstOrDefault(t => t != null && t.name == (s_enCiudad ? "Director_P2_Ciudad" : "Director_P1_Bosque"))?.GetComponent<PlayableDirector>();
        var tl = director != null ? director.playableAsset as TimelineAsset : null;
        if (tl == null) return "No encontré el Timeline del director: agrega una pista Derrumbe a mano.";

        Undo.RecordObject(tl, "Pista de derrumbe");
        var pista = tl.CreateTrack<DerrumbeTrack>(null, nombrePista);
        var clip = pista.CreateClip<DerrumbeClip>();
        ((DerrumbeClip)clip.asset).duracionHorneada = duracionLocal;
        clip.start = s_enCiudad ? s_inicio - 17.5 : s_inicio;
        clip.timeScale = VelocidadClip;
        clip.duration = duracionLocal / VelocidadClip;
        clip.displayName = "Derrumbe " + nombreEdificio;
        Undo.RecordObject(director, "Pista de derrumbe");
        director.SetGenericBinding(pista, comp);
        EditorUtility.SetDirty(tl);
        EditorUtility.SetDirty(director);
        AssetDatabase.SaveAssets();
        director.RebuildGraph();
        var tEditor = System.Type.GetType("UnityEditor.Timeline.TimelineEditor, Unity.Timeline.Editor");
        var tRazon = System.Type.GetType("UnityEditor.Timeline.RefreshReason, Unity.Timeline.Editor");
        if (tEditor != null && tRazon != null)
            try { tEditor.GetMethod("Refresh")?.Invoke(null, new object[] { System.Enum.Parse(tRazon, "ContentsAddedOrRemoved") }); } catch { }
        return $"Pista '{nombrePista}' en {tl.name}: empieza en {s_inicio:0.00} s del principal, velocidad {VelocidadClip}, dura {clip.duration:0.0} s.";
    }

    // =====================================================================================================
    // Tools > Amistad > Derrumbar edificio seleccionado (explosión, vista de la avioneta)
    // Para otro edificio (por ejemplo AVENIDA_ALE/Av_Der_009/ruim.008): explota y luego se derrumba.
    // Busca el momento en que la cámara de la avioneta (CM4) lo ve, lo más cerca posible de que Goku se vuelve SSJ,
    // y hace que caiga hacia esa cámara. La pista y la explosión van en el Timeline de la ciudad.
    // =====================================================================================================
    const float MomentoIdeal = 35.0f;       // explota cuando Goku llega volando, antes del SSJ (tiempo del principal)
    const float BuscarDesde = 33.5f, BuscarHasta = 40.6f;

    [MenuItem("Tools/Amistad/2 · Ciudad/Derrumbar edificio seleccionado (explosión)", false, 205)]
    static void DerrumbarSeleccionado()
    {
        var sel = Selection.activeTransform;
        // Si ya se derrumbó, se puede seleccionar DERRUMBE_<nombre> para rehacerlo: busco el edificio original (apagado).
        if (sel != null && sel.name.StartsWith("DERRUMBE_"))
        {
            string buscado = sel.name.Substring(9);
            var orig = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name.StartsWith("ruim") && t.GetComponent<MeshFilter>() != null &&
                                     (t.name.Replace(".", "") + (t.parent != null && t.parent.name != "CITY" ? "_" + t.parent.name : "")) == buscado);
            if (orig != null) sel = orig;
        }
        if (sel == null || sel.GetComponent<MeshFilter>() == null) { EditorUtility.DisplayDialog("Derrumbe", "Selecciona en el Hierarchy la malla del edificio (por ejemplo AVENIDA_ALE/Av_Der_009/ruim.008).", "OK"); return; }
        var todos = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var av = T("AVIONETA"); var pov = T("AVIONETA_POV");
        var clipAv = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Assets/3d/Avioneta/Avioneta_Vuelo.anim");
        var tlC = T("Director_P2_Ciudad")?.GetComponent<PlayableDirector>()?.playableAsset as TimelineAsset;
        var pistaAv = tlC?.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.StartsWith("Avioneta"));
        var mf = sel.GetComponent<MeshFilter>();
        var b = CajaMundo(mf);
        var log = new StringBuilder();
        double inicio = MomentoIdeal; Vector3? refV = null;
        if (av != null && pov != null && clipAv != null && pistaAv != null)
        {
            float t0 = (float)pistaAv.GetClips().First().start + 17.5f;
            AnimationCurve C(string path, string p) => AnimationUtility.GetEditorCurve(clipAv, EditorCurveBinding.FloatCurve(path, typeof(Transform), p));
            var px = C("", "m_LocalPosition.x"); var py = C("", "m_LocalPosition.y"); var pz = C("", "m_LocalPosition.z");
            var qx = C("", "m_LocalRotation.x"); var qy = C("", "m_LocalRotation.y"); var qz = C("", "m_LocalRotation.z"); var qw = C("", "m_LocalRotation.w");
            var vx = C("AVIONETA_POV", "m_LocalRotation.x"); var vy = C("AVIONETA_POV", "m_LocalRotation.y"); var vz = C("AVIONETA_POV", "m_LocalRotation.z"); var vw = C("AVIONETA_POV", "m_LocalRotation.w");
            var camLocal = Vector3.Scale(pov.localPosition, av.lossyScale);
            float mejor = float.MaxValue; float tMejor = -1; Vector3 posMejor = Vector3.zero; Vector2 vpMejor = Vector2.zero;
            for (float t = BuscarDesde; t <= BuscarHasta; t += 0.05f)
            {
                float l = t - t0;
                var pa = new Vector3(px.Evaluate(l), py.Evaluate(l), pz.Evaluate(l));
                var qa = new Quaternion(qx.Evaluate(l), qy.Evaluate(l), qz.Evaluate(l), qw.Evaluate(l)).normalized;
                var qv = vx != null ? new Quaternion(vx.Evaluate(l), vy.Evaluate(l), vz.Evaluate(l), vw.Evaluate(l)).normalized : Quaternion.identity;
                var camPos = pa + qa * camLocal; var camRot = qa * qv;
                var v = Quaternion.Inverse(camRot) * (b.center - camPos);
                if (v.z <= 1f) continue;
                float x = (v.x / v.z) / (Mathf.Tan(30f * Mathf.Deg2Rad) * 16f / 9f), y = (v.y / v.z) / Mathf.Tan(30f * Mathf.Deg2Rad);
                bool dentro = Mathf.Abs(x) < 0.75f && Mathf.Abs(y) < 0.85f;
                float costo = (dentro ? 0f : 100f + Mathf.Abs(x)) + Mathf.Abs(t - MomentoIdeal);
                if (costo < mejor) { mejor = costo; tMejor = t; posMejor = camPos; vpMejor = new Vector2(x, y); }
            }
            if (tMejor > 0) { inicio = tMejor; refV = posMejor; }
            log.AppendLine($"Momento elegido: {inicio:0.00} s (en pantalla x={vpMejor.x:0.00}, y={vpMejor.y:0.00}; |x|,|y|<1 = dentro). Cae hacia la cámara en {posMejor}");
        }
        else log.AppendLine("No encontré la avioneta o su vuelo: explota a los " + MomentoIdeal + " s y cae hacia Memi.");
        s_inicio = inicio; s_refVista = refV; s_enCiudad = true; s_explosion = true;
        try { Selection.activeTransform = sel; Hornear(); }
        finally { s_inicio = InicioEnTimeline; s_refVista = null; s_enCiudad = false; s_explosion = false; }
        Debug.Log("[Derrumbe seleccionado]\n" + log);
    }

    static Bounds CajaMundo(MeshFilter mf)
    {
        var mb = mf.sharedMesh.bounds; var M = mf.transform.localToWorldMatrix;
        var b = new Bounds(M.MultiplyPoint3x4(mb.center), Vector3.zero);
        for (int i = 0; i < 8; i++)
        {
            var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
            b.Encapsulate(M.MultiplyPoint3x4(c));
        }
        return b;
    }

    // Explosión antes del derrumbe: tres estallidos de fuego por pisos, humo oscuro y brasas. Materiales del fuego del
    // proyecto (shader propio Amistad/Fuego VFX). La dispara un Control Track en el Timeline de la ciudad.
    static string CrearExplosion(List<Transform> todos, Transform raiz, Transform edificio, Material[] mats, float suelo)
    {
        var b = CajaMundo(edificio.GetComponent<MeshFilter>());
        float R = Mathf.Max(b.size.x, b.size.z) * 0.5f, H = b.size.y;
        var go = new GameObject("Explosion"); go.transform.SetParent(raiz, false);
        go.transform.position = new Vector3(b.center.x, suelo, b.center.z);
        var llamas = AssetDatabase.LoadAssetAtPath<Material>("Assets/Assets/shaders/FuegoVFX/Fuego_Llamas.mat");
        var humo = AssetDatabase.LoadAssetAtPath<Material>("Assets/Assets/shaders/FuegoVFX/Fuego_Humo.mat");
        var brasas = AssetDatabase.LoadAssetAtPath<Material>(RutaBrasas);
        uint semilla = 7001;
        string infoExp = ReutilizarExplosionGato(go.transform, b, R, H);
        // humo oscuro que sube
        var hu = Emisor(go.transform, "Humo_Explosion", go.transform.position + Vector3.up * H * 0.3f, humo, semilla++, 8f);
        var mh = hu.main; mh.startDelay = 0.15f;
        mh.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
        mh.startSpeed = new ParticleSystem.MinMaxCurve(R * 0.2f, R * 0.7f);
        mh.startSize = new ParticleSystem.MinMaxCurve(R * 0.9f, R * 1.5f);
        mh.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        mh.startColor = new ParticleSystem.MinMaxGradient(new Color(0.16f, 0.13f, 0.12f, 0.85f), new Color(0.3f, 0.26f, 0.22f, 0.7f));
        var eh = hu.emission; eh.SetBursts(new[] { new ParticleSystem.Burst(0f, 35), new ParticleSystem.Burst(0.4f, 20) });
        var sh2 = hu.shape; sh2.enabled = true; sh2.shapeType = ParticleSystemShapeType.Sphere; sh2.radius = R * 0.6f;
        Velocidad(hu, -R * 0.05f, R * 0.05f, R * 0.1f, R * 0.25f, -R * 0.05f, R * 0.05f);
        Frenar(hu, 1.2f);
        Tamano(hu, new Keyframe(0f, 0.6f), new Keyframe(1f, 2.2f));
        Alfa(hu, new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.12f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f));
        hu.GetComponent<ParticleSystemRenderer>().maxParticleSize = 3f;
        // brasas que salen volando y caen
        var br = Emisor(go.transform, "Brasas_Explosion", go.transform.position + Vector3.up * H * 0.3f, brasas, semilla++, 4f);
        var mb2 = br.main;
        mb2.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3.2f);
        mb2.startSpeed = new ParticleSystem.MinMaxCurve(R * 1.2f, R * 3.5f);
        mb2.startSize = new ParticleSystem.MinMaxCurve(R * 0.025f, R * 0.07f);
        mb2.startColor = new ParticleSystem.MinMaxGradient(new Color(6f, 2.5f, 0.6f, 1f), new Color(4f, 1.2f, 0.3f, 1f));
        mb2.gravityModifier = 8f;
        var eb = br.emission; eb.SetBursts(new[] { new ParticleSystem.Burst(0f, 140), new ParticleSystem.Burst(0.3f, 60) });
        var sh3 = br.shape; sh3.enabled = true; sh3.shapeType = ParticleSystemShapeType.Hemisphere; sh3.radius = R * 0.4f;
        Alfa(br, new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f));
        foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { var mm = ps.main; mm.playOnAwake = true; }
        go.SetActive(false);

        // Control Track en el Timeline de la ciudad
        var dir = todos.FirstOrDefault(t => t != null && t.name == "Director_P2_Ciudad")?.GetComponent<PlayableDirector>();
        var tl = dir?.playableAsset as TimelineAsset;
        if (tl == null) return "No encontré el Timeline de la ciudad para la explosión.";
        string nombrePista = "Explosión " + raiz.name.Replace("DERRUMBE_", "");
        foreach (var vieja in tl.GetOutputTracks().OfType<ControlTrack>().Where(t => t.name == nombrePista).ToList()) tl.DeleteTrack(vieja);
        var pista = tl.CreateTrack<ControlTrack>(null, nombrePista);
        var clip = pista.CreateClip<ControlPlayableAsset>();
        clip.start = s_inicio - 17.5 - 0.3; clip.duration = 10; clip.displayName = "Explosión";
        var cpa = (ControlPlayableAsset)clip.asset;
        var er = cpa.sourceGameObject; er.exposedName = GUID.Generate().ToString(); er.defaultValue = null; cpa.sourceGameObject = er;
        dir.SetReferenceValue(er.exposedName, go);
        cpa.updateParticle = true; cpa.active = true; cpa.updateDirector = false; cpa.updateITimeControl = false; cpa.searchHierarchy = true;
        cpa.postPlayback = ActivationControlPlayable.PostPlaybackState.Inactive;
        EditorUtility.SetDirty(tl); EditorUtility.SetDirty(dir);
        AssetDatabase.SaveAssets();
        return infoExp + $"\nExplosión ({R:0} u de radio, {H:0} u de alto) en la pista '{nombrePista}', {s_inicio - 0.3:0.00} s del principal.";
    }


    // La misma "Explosión horizontal" del bosque (la que asusta al gato en la toma del título): se copia la instancia del
    // prefab con todos sus cambios (sin loop, semilla fija, tamaños) y se escala al tamaño del edificio.
    const string RutaExplosionGato = "Assets/Assets/shaders/Explosión horizontal.prefab";
    const string RutaMatExplosion = "Assets/Assets/shaders/Materiales/Explosion.mat";
    const string RutaMatExplosionCiudad = "Assets/Assets/shaders/Materiales/Explosion_Ciudad.mat";
    const float FactorFuegoCiudad = 0.15f;
    static string ReutilizarExplosionGato(Transform padre, Bounds b, float R, float H)
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RutaExplosionGato);
        if (prefab == null) return "No encontré " + RutaExplosionGato;
        var bosque = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).Select(t => t.gameObject).FirstOrDefault(g => g.name == "BOSQUE");
        var original = bosque == null ? null : bosque.GetComponentsInChildren<Transform>(true)
            .Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == prefab)
            .Select(t => t.gameObject).FirstOrDefault();
        var copia = (GameObject)PrefabUtility.InstantiatePrefab(prefab, padre);
        if (original != null)
        {
            var mods = PrefabUtility.GetPropertyModifications(original);
            if (mods != null) PrefabUtility.SetPropertyModifications(copia, mods);
            copia.transform.SetParent(padre, true);
        }
        copia.name = "Explosion_Gato";
        copia.SetActive(true);
        Quaternion rotMundo = original != null ? original.transform.rotation : Quaternion.Euler(-90f, 0f, 0f);
        Vector3 escalaMundo = original != null ? original.transform.lossyScale : Vector3.one * 27.5f;

        // Mido qué tan grande se ve la explosión original simulándola 1.2 s, para escalarla al edificio.
        copia.transform.SetPositionAndRotation(Vector3.zero, rotMundo);
        PonerEscalaMundo(copia.transform, escalaMundo);
        float ancho = MedirExplosion(copia);
        float objetivo = R * 2.6f;
        float k = ancho > 0.01f ? Mathf.Clamp(objetivo / ancho, 0.1f, 50f) : 4f;
        copia.transform.SetPositionAndRotation(new Vector3(b.center.x, b.min.y + H * 0.3f, b.center.z), rotMundo);
        PonerEscalaMundo(copia.transform, escalaMundo * k);
        foreach (var ps in copia.GetComponentsInChildren<ParticleSystem>(true)) { var m = ps.main; m.playOnAwake = true; }
        // Mismo material del gato pero con el color de fuego menos extremo: con el postpro de la ciudad (contraste y
        // saturación altos) el naranja HDR de 63 se volvía cian. Se usa una copia para no tocar la toma del bosque.
        var matGato = AssetDatabase.LoadAssetAtPath<Material>(RutaMatExplosion);
        if (matGato != null)
        {
            var matCiudad = AssetDatabase.LoadAssetAtPath<Material>(RutaMatExplosionCiudad);
            if (matCiudad == null) { matCiudad = new Material(matGato); AssetDatabase.CreateAsset(matCiudad, RutaMatExplosionCiudad); }
            matCiudad.CopyPropertiesFromMaterial(matGato);
            var c = matGato.GetColor("_FireColor");
            matCiudad.SetColor("_FireColor", c * FactorFuegoCiudad);
            FuegoToonEditor.HacerTransparente(matCiudad, FuegoToonEditor.OpacidadExplosion);   // deja ver los edificios
            EditorUtility.SetDirty(matCiudad);
            foreach (var r in copia.GetComponentsInChildren<ParticleSystemRenderer>(true)) r.sharedMaterial = matCiudad;
        }
        return $"Explosión del gato reutilizada ({(original != null ? "copia de " + original.name + " del BOSQUE" : "prefab sin cambios")}): medía {ancho:0} u de ancho, escala x{k:0.00}.";
    }

    static void PonerEscalaMundo(Transform t, Vector3 escalaMundo)
    {
        var ps = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(escalaMundo.x / ps.x, escalaMundo.y / ps.y, escalaMundo.z / ps.z);
    }

    static float MedirExplosion(GameObject go)
    {
        var padre = go.transform.parent; bool activoPadre = padre == null || padre.gameObject.activeSelf;
        if (padre != null) padre.gameObject.SetActive(true);
        float ancho = 0f;
        try
        {
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.Simulate(1.2f, false, true, false);
            Bounds? bb = null;
            foreach (var r in go.GetComponentsInChildren<ParticleSystemRenderer>(true))
            {
                var ps = r.GetComponent<ParticleSystem>(); if (ps == null || ps.particleCount == 0) continue;
                if (bb == null) bb = r.bounds; else { var x = bb.Value; x.Encapsulate(r.bounds); bb = x; }
            }
            if (bb != null) ancho = Mathf.Max(bb.Value.size.x, bb.Value.size.z);
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); ps.Clear(true); }
        }
        finally { if (padre != null) padre.gameObject.SetActive(activoPadre); }
        return ancho;
    }

}
