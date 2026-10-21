using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Menú Tools > Amistad > Crear vegetación en Piso_FOREST.
/// Crea (o actualiza) el objeto VEGETACION_PISO con tres capas:
/// pasto (matojo procedural), arbustos y abetos tomados del mismo modelo del bosque.
/// </summary>
public static class VegetacionPisoEditor
{
    const string Carpeta = "Assets/Assets/3d/Bakcground/the-landscape-is-a-forest-in-the-mountains/vegetacion";
    const string RutaMallaPasto = Carpeta + "/Pasto_Matojo_Malla.asset";
    const string RutaMatPasto = Carpeta + "/Pasto_Matojo.mat";
    const string RutaMatRoca = "Assets/Assets/3d/Bakcground/the-landscape-is-a-forest-in-the-mountains/textures/a37081a7611992c6f6d9dc1786dbc08d.mat";
    const string RutaPiedras = "Assets/Assets/3d/Bakcground/CASCADA/rock_set_0{0}.fbx";

    // Claves para reconocer la vegetación del bosque por el nombre de su material
    const string ClaveArbusto = "3f84aea6648aa6cc5af5f07ee43c6056";
    const string ClaveAbeto = "cc24eba76e0e306bc19df7b7a9208504";

    [MenuItem("Tools/Amistad/Crear vegetación en Piso_FOREST")]
    static void Crear()
    {
        var escena = SceneManager.GetActiveScene();
        var todos = escena.GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();

        var piso = todos.FirstOrDefault(t => t.name == "Piso_FOREST");
        if (piso == null)
        {
            EditorUtility.DisplayDialog("Vegetación", "No encontré Piso_FOREST en la escena abierta.", "OK");
            return;
        }
        var colPiso = piso.GetComponent<Collider>();
        if (colPiso == null)
        {
            EditorUtility.DisplayDialog("Vegetación", "Piso_FOREST no tiene Collider.", "OK");
            return;
        }

        var rends = todos.Select(t => t.GetComponent<MeshRenderer>())
            .Where(r => r != null && r.gameObject.activeInHierarchy && r.enabled)
            .Where(r => { var f = r.GetComponent<MeshFilter>(); return f != null && f.sharedMesh != null; })
            .ToList();
        MeshRenderer Muestra(string clave) =>
            rends.FirstOrDefault(r => r.sharedMaterials.Any(m => m != null && m.name.Contains(clave)));

        var arbusto = Muestra(ClaveArbusto);
        var abeto = Muestra(ClaveAbeto);

        // Objeto contenedor (hermano de Piso_FOREST para heredar la misma jerarquía)
        var existente = todos.FirstOrDefault(t => t.name == "VEGETACION_PISO");
        GameObject go;
        if (existente != null)
        {
            go = existente.gameObject;
            var previo = go.GetComponent<VegetacionPiso>();
            if (previo != null) Undo.RecordObject(previo, "Vegetación");
        }
        else
        {
            go = new GameObject("VEGETACION_PISO");
            Undo.RegisterCreatedObjectUndo(go, "Crear vegetación");
            go.transform.SetParent(piso.parent, false);
        }
        go.transform.position = new Vector3(colPiso.bounds.center.x, colPiso.bounds.center.y, colPiso.bounds.center.z);
        var veg = go.GetComponent<VegetacionPiso>();
        if (veg == null) veg = Undo.AddComponent<VegetacionPiso>(go);

        veg.superficie = colPiso;
        veg.area = new Vector2(colPiso.bounds.size.x, colPiso.bounds.size.z);
        veg.capas = new List<VegetacionPiso.Capa>();

        // Tamaño de referencia: la altura del gato (el protagonista camina sobre este piso)
        float altoGato = 30f;
        var gato = todos.FirstOrDefault(t => t.name == "cat");
        if (gato != null)
        {
            var rs = gato.GetComponentsInChildren<Renderer>(true);
            if (rs.Length > 0)
            {
                var bb = rs[0].bounds;
                foreach (var r in rs) bb.Encapsulate(r.bounds);
                altoGato = Mathf.Clamp(bb.size.y, 5f, 300f);
            }
        }

        // Zonas libres: camino del gato (todas las capas) y recorrido de cámara + línea de visión (arbustos y abetos)
        var libresGato = new List<Vector3>();
        var libresCamara = new List<Vector3>();
        MuestrearTimeline(todos, gato, colPiso.bounds, libresGato, libresCamara);
        if (gato != null) libresGato.Add(gato.position);
        var cm1 = todos.FirstOrDefault(t => t.name == "CM1_CASCADA");
        if (cm1 != null) libresCamara.Add(cm1.position);
        var spline = todos.FirstOrDefault(t => t.name == "Spline_P1_Cascada");
        if (spline != null) libresCamara.AddRange(MuestrearSpline(spline.gameObject, 16));
        veg.puntosLibres = libresGato.ToArray();
        veg.puntosLibresCamara = libresCamara.ToArray();
        veg.radioZonaLibre = altoGato * 2f;
        veg.evitarTapados = false; // CASCADA tiene un BoxCollider invisible que dejaría un hueco cuadrado

        // 1) Pasto: ~1/5 de la altura del gato
        var mallaPasto = ObtenerMallaPasto();
        var matPasto = AssetDatabase.LoadAssetAtPath<Material>(RutaMatPasto);
        if (mallaPasto != null && matPasto != null)
        {
            float h = altoGato * 0.22f;
            veg.capas.Add(new VegetacionPiso.Capa
            {
                nombre = "Pasto",
                malla = mallaPasto,
                materiales = new[] { matPasto },
                cantidad = 9000,                 // pocos y en manchones: el piso es de tierra/polvo
                escalaBase = new Vector3(h * 1.3f, h, h * 1.3f),
                rangoEscala = new Vector2(0.5f, 1.2f),
                agrupamiento = 0.9f,
                tamanoManchon = altoGato * 4f,
                hundir = 0.05f,
                proyectarSombras = false,
                respetarZonasLibres = true,      // deja un caminito de tierra por donde pasa el gato
                multiplicadorRadioLibre = 0.8f,
                evitarCamara = false             // el pasto sí se ve bajo la cámara
            });
        }
        else Debug.LogWarning("Vegetación: no encontré el material del pasto en " + RutaMatPasto);

        // 2) Arbustos y 3) abetos del propio bosque, escalados respecto al gato
        if (arbusto != null)
        {
            var capaArbustos = CapaDesde("Arbustos del bosque", arbusto, 220, altoGato * 0.6f, altoGato * 1.4f, 0.75f, altoGato * 5f, true);
            capaArbustos.multiplicadorRadioLibre = 1.5f;
            veg.capas.Add(capaArbustos);
        }
        else Debug.LogWarning("Vegetación: no encontré arbustos del bosque (" + ClaveArbusto + ") en la escena.");
        if (abeto != null)
        {
            var capaAbetos = CapaDesde("Abetos del bosque", abeto, 25, altoGato * 2f, altoGato * 4.5f, 0.8f, altoGato * 9f, true);
            capaAbetos.multiplicadorRadioLibre = 3f;
            veg.capas.Add(capaAbetos);
        }
        else Debug.LogWarning("Vegetación: no encontré abetos del bosque (" + ClaveAbeto + ") en la escena.");

        // 4) Piedritas (rock_set del paquete de la montaña) con el material de roca del bosque
        var matRoca = AssetDatabase.LoadAssetAtPath<Material>(RutaMatRoca);
        if (matRoca != null)
        {
            for (int i = 1; i <= 4; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(string.Format(RutaPiedras, i));
                var mf = prefab != null ? prefab.GetComponentInChildren<MeshFilter>() : null;
                if (mf == null || mf.sharedMesh == null) continue;
                Quaternion rot = mf.transform.rotation;
                Vector3 esc = mf.transform.lossyScale;
                float altoMalla = AltoEnMundo(mf.sharedMesh.bounds, rot, esc);
                float medio = altoGato * 0.3f;
                veg.capas.Add(new VegetacionPiso.Capa
                {
                    nombre = "Piedritas " + i,
                    malla = mf.sharedMesh,
                    materiales = new[] { matRoca },
                    cantidad = 35,
                    escalaBase = esc * (medio / altoMalla),
                    rotacionBase = rot.eulerAngles,
                    rangoEscala = new Vector2(0.4f, 1.8f),
                    agrupamiento = 0.5f,
                    tamanoManchon = altoGato * 6f,
                    hundir = 0.25f,
                    proyectarSombras = true,
                    respetarZonasLibres = true,
                    multiplicadorRadioLibre = 0.4f,
                    evitarCamara = false
                });
            }
        }
        else Debug.LogWarning("Vegetación: no encontré el material de roca del bosque en " + RutaMatRoca);

        // Claro para el lago de la cascada (se conserva si ya existe y lo moviste)
        if (veg.claros == null) veg.claros = new List<VegetacionPiso.Claro>();
        veg.claros.RemoveAll(c => c == null || c.centro == null);
        if (!veg.claros.Any(c => c.nombre == "Lago"))
        {
            var cascada = todos.FirstOrDefault(t => t.name == "Cascada");
            Vector3 centroLago = colPiso.bounds.center;
            float radioLago = altoGato * 6f;
            if (cascada != null)
            {
                var rs = cascada.GetComponentsInChildren<Renderer>(true);
                if (rs.Length > 0)
                {
                    var bb = rs[0].bounds;
                    foreach (var r in rs) bb.Encapsulate(r.bounds);
                    // El agua cae al pie de la cascada: ponemos el claro un poco hacia el lado del piso
                    var pie = new Vector3(bb.center.x, colPiso.bounds.center.y, bb.center.z);
                    var haciaPiso = colPiso.bounds.center - pie; haciaPiso.y = 0f;
                    radioLago = Mathf.Clamp(Mathf.Max(bb.size.x, bb.size.z) * 0.6f, altoGato * 5f, altoGato * 8f);
                    centroLago = pie + haciaPiso.normalized * radioLago * 0.6f;
                    Debug.Log($"Vegetación: cascada en {bb.center} (tamaño {bb.size}); claro del lago radio {radioLago:0}.");
                }
            }
            var goLago = new GameObject("Claro_Lago");
            Undo.RegisterCreatedObjectUndo(goLago, "Claro del lago");
            goLago.transform.SetParent(go.transform, true);
            goLago.transform.position = centroLago;
            veg.claros.Add(new VegetacionPiso.Claro { nombre = "Lago", centro = goLago.transform, radio = radioLago });
        }

        veg.Regenerar();
        EditorUtility.SetDirty(veg);
        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeGameObject = go;
        Debug.Log($"Vegetación: {veg.TotalInstancias} instancias en {veg.capas.Count} capas. Altura del gato (referencia) = {altoGato:0.##} unidades.");
    }

    static VegetacionPiso.Capa CapaDesde(string nombre, MeshRenderer r, int cantidad, float altoMin, float altoMax, float agrupamiento, float manchon, bool sombras)
    {
        var t = r.transform;
        // Quitamos el giro en Y del ejemplar: el script pone uno aleatorio
        var e = t.rotation.eulerAngles;
        float altoMuestra = Mathf.Max(r.bounds.size.y, 0.01f);
        float medio = (altoMin + altoMax) * 0.5f;
        return new VegetacionPiso.Capa
        {
            nombre = nombre,
            malla = r.GetComponent<MeshFilter>().sharedMesh,
            materiales = r.sharedMaterials,
            cantidad = cantidad,
            escalaBase = t.lossyScale * (medio / altoMuestra),
            rotacionBase = new Vector3(e.x, 0f, e.z),
            rangoEscala = new Vector2(altoMin / medio, altoMax / medio),
            agrupamiento = agrupamiento,
            tamanoManchon = manchon,
            hundir = 0.03f,
            proyectarSombras = sombras,
            dobleCara = true,
            respetarZonasLibres = true
        };
    }

    /// Recorre el Timeline del bosque (solo si está en modo Preview) y guarda las posiciones del gato,
    /// de la cámara y puntos intermedios entre ambos, para no tapar la acción.
    static void MuestrearTimeline(List<Transform> todos, Transform gato, Bounds piso, List<Vector3> libresGato, List<Vector3> libresCamara)
    {
        var directorGO = todos.FirstOrDefault(t => t.name == "Director_P1_Bosque");
        var director = directorGO != null ? directorGO.GetComponent<UnityEngine.Playables.PlayableDirector>() : null;
        var camGO = todos.FirstOrDefault(t => t.name == "Main Camera");
        if (director == null || gato == null || camGO == null) return;
        if (!AnimationMode.InAnimationMode())
        {
            Debug.LogWarning("Vegetación: activa 'Preview' en la ventana Timeline y vuelve a correr la herramienta para despejar el camino del gato y la cámara.");
            return;
        }

        // Cinemachine 3: para mover la cámara al instante hay que poner el Brain en ManualUpdate (y regresarlo después)
        var brain = camGO.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "CinemachineBrain");
        System.Reflection.FieldInfo campoModo = null;
        object modoOriginal = null;
        System.Reflection.MethodInfo actualizar = null;
        if (brain != null)
        {
            campoModo = brain.GetType().GetField("UpdateMethod");
            actualizar = brain.GetType().GetMethod("ManualUpdate", System.Type.EmptyTypes);
            if (campoModo != null && actualizar != null && campoModo.FieldType.IsEnum)
            {
                modoOriginal = campoModo.GetValue(brain);
                try { campoModo.SetValue(brain, System.Enum.Parse(campoModo.FieldType, "ManualUpdate")); }
                catch { actualizar = null; }
            }
            else actualizar = null;
        }

        double t0 = director.time;
        double dur = director.duration;
        var area = piso; area.Expand(new Vector3(400f, 100000f, 400f));
        int pasos = Mathf.Clamp(Mathf.CeilToInt((float)(dur / 0.25)), 10, 600);
        var gatoTmp = new List<Vector3>();
        var camTmp = new List<Vector3>();
        try
        {
            for (int i = 0; i <= pasos; i++)
            {
                director.time = dur * i / pasos;
                director.Evaluate();
                if (actualizar != null) actualizar.Invoke(brain, null);
                Vector3 g = gato.position, c = camGO.transform.position;
                bool gEn = gato.gameObject.activeInHierarchy && area.Contains(g);
                bool cEn = actualizar != null && area.Contains(c);
                if (gEn) gatoTmp.Add(g);
                if (cEn) camTmp.Add(c);
                if (gEn && cEn && Vector3.Distance(g, c) < 600f)
                    for (int k = 1; k < 4; k++) camTmp.Add(Vector3.Lerp(c, g, k / 4f));
            }
        }
        finally
        {
            director.time = t0;
            director.Evaluate();
            if (actualizar != null) actualizar.Invoke(brain, null);
            if (campoModo != null && modoOriginal != null) campoModo.SetValue(brain, modoOriginal);
        }

        libresGato.AddRange(QuitarRepetidos(gatoTmp, 15f));
        libresCamara.AddRange(QuitarRepetidos(camTmp, 15f));
        Debug.Log($"Vegetación: Timeline muestreado — {libresGato.Count} puntos del gato, {libresCamara.Count} de cámara/visión.");
    }

    static float AltoEnMundo(Bounds b, Quaternion rot, Vector3 esc)
    {
        var m = Matrix4x4.TRS(Vector3.zero, rot, esc);
        float min = float.MaxValue, max = float.MinValue;
        for (int i = 0; i < 8; i++)
        {
            var e = b.extents;
            var p = b.center + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            float y = m.MultiplyPoint3x4(p).y;
            min = Mathf.Min(min, y); max = Mathf.Max(max, y);
        }
        return Mathf.Max(max - min, 0.01f);
    }

    static List<Vector3> QuitarRepetidos(List<Vector3> puntos, float distancia)
    {
        var limpios = new List<Vector3>();
        float d2 = distancia * distancia;
        foreach (var p in puntos)
            if (!limpios.Any(q => (q - p).sqrMagnitude < d2)) limpios.Add(p);
        return limpios;
    }

    /// Muestrea un SplineContainer (paquete Splines) por reflexión, en coordenadas de mundo.
    static List<Vector3> MuestrearSpline(GameObject go, int muestras)
    {
        var puntos = new List<Vector3>();
        var cont = go.GetComponents<Component>().FirstOrDefault(c => c != null && c.GetType().Name == "SplineContainer");
        if (cont == null) return puntos;
        var metodo = cont.GetType().GetMethod("EvaluatePosition", new[] { typeof(float) });
        if (metodo == null) return puntos;
        for (int i = 0; i <= muestras; i++)
        {
            try
            {
                object r = metodo.Invoke(cont, new object[] { i / (float)muestras });
                var tipo = r.GetType();
                float x = (float)tipo.GetField("x").GetValue(r);
                float y = (float)tipo.GetField("y").GetValue(r);
                float z = (float)tipo.GetField("z").GetValue(r);
                puntos.Add(new Vector3(x, y, z));
            }
            catch (System.Exception e) { Debug.LogWarning("Vegetación: no pude muestrear el spline: " + e.Message); break; }
        }
        return puntos;
    }

    static Mesh ObtenerMallaPasto()
    {
        var nueva = CrearMatojo();
        var existente = AssetDatabase.LoadAssetAtPath<Mesh>(RutaMallaPasto);
        if (existente != null)
        {
            EditorUtility.CopySerialized(nueva, existente);   // actualiza la malla sin romper referencias
            Object.DestroyImmediate(nueva);
            EditorUtility.SetDirty(existente);
            AssetDatabase.SaveAssets();
            return existente;
        }
        AssetDatabase.CreateAsset(nueva, RutaMallaPasto);
        AssetDatabase.SaveAssets();
        return nueva;
    }

    /// Tres planos cruzados a 60° (cada uno con sus dos caras), base en el origen, 1 x 1 unidades.
    static Mesh CrearMatojo()
    {
        var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
        for (int i = 0; i < 3; i++)
        {
            float ang = i * 60f * Mathf.Deg2Rad;
            var dir = new Vector3(Mathf.Cos(ang), 0f, Mathf.Sin(ang)) * 0.5f;
            var normal = new Vector3(-Mathf.Sin(ang), 0f, Mathf.Cos(ang));
            for (int lado = 0; lado < 2; lado++)
            {
                var nn = (lado == 0 ? normal : -normal) + Vector3.up * 0.6f;
                nn.Normalize();
                int b0 = v.Count;
                v.Add(-dir); v.Add(dir); v.Add(dir + Vector3.up); v.Add(-dir + Vector3.up);
                uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(1, 0)); uv.Add(new Vector2(1, 1)); uv.Add(new Vector2(0, 1));
                for (int k = 0; k < 4; k++) n.Add(nn);
                // Unity: cara frontal = sentido horario visto desde donde apunta la normal
                if (lado == 0) tri.AddRange(new[] { b0, b0 + 1, b0 + 2, b0, b0 + 2, b0 + 3 });
                else tri.AddRange(new[] { b0, b0 + 2, b0 + 1, b0, b0 + 3, b0 + 2 });
            }
        }
        var m = new Mesh { name = "Pasto_Matojo_Malla" };
        m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
        m.RecalculateBounds();
        m.RecalculateTangents();
        return m;
    }
}
