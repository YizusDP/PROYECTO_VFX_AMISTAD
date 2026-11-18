using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Manos con más definición (Ale y Memi)
/// Los cuerpos son "Body Male LowPoly" (866 triángulos todo el cuerpo, ~65 vértices por mano): en las tomas de palma
/// (CM8_5 a CM8_8) se veían las aristas y los dedos como bloques.
/// NO toca los FBX: genera una copia del cuerpo (Assets/Animaciones/Saludo/Manos_HD/<cuerpo>_ManosHD.asset) y la asigna
/// al SkinnedMeshRenderer (mismos huesos y bindposes, así que se anima igual):
///  - Subdivide TODO el cuerpo (cada triángulo -> 4, dos veces = 16) para que no haya grietas en la muñeca.
///  - Solo en las manos (vértices con peso de los huesos de mano/dedos) mueve los vértices con la regla de Loop
///    (superficie suave); el resto queda exactamente en el mismo lugar (el estilo low-poly del cuerpo no cambia).
///  - Normales suaves en las manos; en el resto se interpolan las originales (mismo sombreado de antes).
///  - Pesos de huesos, UV y UV2 interpolados.
/// "Quitar" regresa la malla original del FBX.
/// </summary>
public static class ManosDefinicionEditor
{
    const string Carpeta = "Assets/Animaciones/Saludo/Manos_HD";
    const int Niveles = 2;
    const float PesoMin = 0.15f, PesoMax = 0.75f;   // peso de mano -> cuánto se suaviza (0 = nada, 1 = Loop completo)

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Manos con más definición (Ale y Memi)")]
    static void Menu()
    {
        var log = new StringBuilder();
        foreach (var r in Cuerpos()) Aplicar(r, log);
        AssetDatabase.SaveAssets();
        Debug.Log("[Manos HD]\n" + log);
    }

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Manos con más definición: quitar (malla original)")]
    static void Quitar()
    {
        var log = new StringBuilder();
        foreach (var r in Cuerpos())
        {
            var orig = Original(r);
            if (orig == null) { log.AppendLine(r.name + ": no encontré la malla original."); continue; }
            Undo.RecordObject(r, "Manos HD"); r.sharedMesh = orig;
            var lim = LimitesOriginales(r, r.GetComponentInParent<Animator>(true)); if (lim.HasValue) r.localBounds = lim.Value;
            EditorUtility.SetDirty(r);
            log.AppendLine(r.name + ": malla original " + orig.name);
            EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
        }
        Debug.Log("[Manos HD] Quitado.\n" + log);
    }

    static IEnumerable<SkinnedMeshRenderer> Cuerpos() =>
        Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(r => r.name.StartsWith("Body Male LowPoly") && r.GetComponentInParent<Animator>(true) != null);

    // la malla original vive en el FBX del personaje (el Animator sabe cuál por el avatar)
    static Mesh Original(SkinnedMeshRenderer r)
    {
        string nombre = r.sharedMesh != null ? r.sharedMesh.name.Replace("_ManosHD", "") : r.name;
        var an = r.GetComponentInParent<Animator>(true);
        string fbx = an != null && an.avatar != null ? AssetDatabase.GetAssetPath(an.avatar) : null;
        if (string.IsNullOrEmpty(fbx)) return null;
        return AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<Mesh>().FirstOrDefault(m => m.name == nombre);
    }

    static Bounds? LimitesOriginales(SkinnedMeshRenderer r, Animator an)
    {
        string fbx = an != null && an.avatar != null ? AssetDatabase.GetAssetPath(an.avatar) : null;
        var go = string.IsNullOrEmpty(fbx) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(fbx);
        var smr = go != null ? go.GetComponentsInChildren<SkinnedMeshRenderer>(true).FirstOrDefault(x => x.name == r.name) : null;
        return smr != null ? smr.localBounds : (Bounds?)null;
    }

    class M
    {
        public List<Vector3> p = new(); public List<Vector3> n = new(); public List<Vector2> uv = new(), uv2 = new();
        public List<BoneWeight> w = new(); public List<int> weld = new(); public List<int> tri = new();
        public Dictionary<int, float> suave = new();   // por id soldado
    }

    static void Aplicar(SkinnedMeshRenderer r, StringBuilder log)
    {
        var orig = Original(r);
        if (orig == null) { log.AppendLine(r.name + ": no encontré la malla original en el FBX."); return; }
        var an = r.GetComponentInParent<Animator>(true);
        var manos = new HashSet<Transform>();
        foreach (var hb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
        {
            var h = an.GetBoneTransform(hb);
            if (h != null) foreach (var t in h.GetComponentsInChildren<Transform>(true)) manos.Add(t);
        }
        var esMano = r.bones.Select(b => b != null && manos.Contains(b)).ToArray();

        // ---------- malla de entrada con vértices soldados por posición
        var m = new M();
        var pos = orig.vertices; var nor = orig.normals; var uv = orig.uv; var uv2 = orig.uv2; var bw = orig.boneWeights;
        var ids = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < pos.Length; i++)
        {
            m.p.Add(pos[i]); m.n.Add(nor.Length > 0 ? nor[i] : Vector3.up);
            m.uv.Add(uv.Length > 0 ? uv[i] : Vector2.zero); m.uv2.Add(uv2.Length > 0 ? uv2[i] : Vector2.zero);
            m.w.Add(bw[i]);
            var k = new Vector3Int(Mathf.RoundToInt(pos[i].x * 1e4f), Mathf.RoundToInt(pos[i].y * 1e4f), Mathf.RoundToInt(pos[i].z * 1e4f));
            if (!ids.TryGetValue(k, out int id)) { id = ids.Count; ids[k] = id; }
            m.weld.Add(id);
            float h = 0f; var b = bw[i];
            if (esMano[b.boneIndex0]) h += b.weight0; if (esMano[b.boneIndex1]) h += b.weight1;
            if (esMano[b.boneIndex2]) h += b.weight2; if (esMano[b.boneIndex3]) h += b.weight3;
            float s = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(PesoMin, PesoMax, h));
            m.suave[id] = m.suave.TryGetValue(id, out float prev) ? Mathf.Max(prev, s) : s;
        }
        m.tri.AddRange(orig.triangles);
        int trisAntes = m.tri.Count / 3;

        for (int nivel = 0; nivel < Niveles; nivel++) m = Subdividir(m);

        // ---------- normales: suaves en las manos (por id soldado), interpoladas en el resto
        var nSuave = new Dictionary<int, Vector3>();
        for (int t = 0; t < m.tri.Count; t += 3)
        {
            int a = m.tri[t], b = m.tri[t + 1], c = m.tri[t + 2];
            var fn = Vector3.Cross(m.p[b] - m.p[a], m.p[c] - m.p[a]) * 1e6f;   // ponderada por área (escalada: la malla es diminuta)
            foreach (var v in new[] { a, b, c }) { int id = m.weld[v]; nSuave[id] = (nSuave.TryGetValue(id, out var acc) ? acc : Vector3.zero) + fn; }
        }
        for (int i = 0; i < m.p.Count; i++)
        {
            float s = m.suave[m.weld[i]];
            // ojo: la malla es diminuta (~0.1 u) y las áreas son ~1e-6: Vector3.normalized devolvería cero
            var ns = nSuave.TryGetValue(m.weld[i], out var v) && v.sqrMagnitude > 0f ? v / Mathf.Sqrt(v.sqrMagnitude) : m.n[i];
            m.n[i] = Vector3.Slerp(m.n[i].normalized, ns, s).normalized;
        }

        // ---------- guardar como asset (si ya existe se rellena ESE mismo objeto: con CopySerialized la GPU se quedaba
        //            con los datos viejos hasta reiniciar Unity)
        Directory.CreateDirectory(Carpeta);
        string ruta = $"{Carpeta}/{orig.name}_ManosHD.asset";
        var existente = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        var nueva = existente != null ? existente : new Mesh();
        nueva.Clear();
        nueva.name = orig.name + "_ManosHD";
        nueva.indexFormat = m.p.Count > 65535 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        nueva.SetVertices(m.p); nueva.SetNormals(m.n); nueva.SetUVs(0, m.uv);
        if (uv2.Length > 0) nueva.SetUVs(1, m.uv2);
        nueva.boneWeights = m.w.ToArray();
        nueva.bindposes = orig.bindposes;
        nueva.SetTriangles(m.tri, 0);
        nueva.RecalculateTangents();
        nueva.RecalculateBounds();
        nueva.UploadMeshData(false);
        if (existente != null) EditorUtility.SetDirty(existente); else AssetDatabase.CreateAsset(nueva, ruta);

        Undo.RecordObject(r, "Manos HD");
        r.sharedMesh = nueva;
        // mismos límites que el renderer original del FBX (están en el espacio del rootBone, no del mesh);
        // si no los encuentro, que calcule los límites cada cuadro para que no desaparezca de cámara
        var limites = LimitesOriginales(r, an);
        if (limites.HasValue) r.localBounds = limites.Value; else r.updateWhenOffscreen = true;
        EditorUtility.SetDirty(r);
        EditorSceneManager.MarkSceneDirty(r.gameObject.scene);
        int manosV = m.suave.Count(kv => kv.Value > 0.5f);
        log.AppendLine($"{r.transform.root.name}/{an.name}/{r.name}: {trisAntes} -> {m.tri.Count / 3} triángulos ({manosV} vértices de mano suavizados) -> {ruta}");
    }

    static BoneWeight Mezcla(BoneWeight a, BoneWeight b)
    {
        var d = new Dictionary<int, float>();
        void Add(int i, float w) { if (w <= 0f) return; d[i] = (d.TryGetValue(i, out float x) ? x : 0f) + w * 0.5f; }
        Add(a.boneIndex0, a.weight0); Add(a.boneIndex1, a.weight1); Add(a.boneIndex2, a.weight2); Add(a.boneIndex3, a.weight3);
        Add(b.boneIndex0, b.weight0); Add(b.boneIndex1, b.weight1); Add(b.boneIndex2, b.weight2); Add(b.boneIndex3, b.weight3);
        var top = d.OrderByDescending(kv => kv.Value).Take(4).ToList();
        float sum = top.Sum(kv => kv.Value); if (sum <= 0f) return a;
        var r = new BoneWeight();
        if (top.Count > 0) { r.boneIndex0 = top[0].Key; r.weight0 = top[0].Value / sum; }
        if (top.Count > 1) { r.boneIndex1 = top[1].Key; r.weight1 = top[1].Value / sum; }
        if (top.Count > 2) { r.boneIndex2 = top[2].Key; r.weight2 = top[2].Value / sum; }
        if (top.Count > 3) { r.boneIndex3 = top[3].Key; r.weight3 = top[3].Value / sum; }
        return r;
    }

    static long Llave(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    // una vuelta de subdivisión 1 -> 4 (Loop en las zonas suaves, lineal en el resto)
    static M Subdividir(M m)
    {
        // posición de cada id soldado (la primera que aparece)
        var posW = new Dictionary<int, Vector3>();
        for (int i = 0; i < m.p.Count; i++) if (!posW.ContainsKey(m.weld[i])) posW[m.weld[i]] = m.p[i];
        // aristas soldadas: vértices opuestos
        var opuestos = new Dictionary<long, List<int>>();
        var vecinos = new Dictionary<int, HashSet<int>>();
        for (int t = 0; t < m.tri.Count; t += 3)
        {
            int[] w = { m.weld[m.tri[t]], m.weld[m.tri[t + 1]], m.weld[m.tri[t + 2]] };
            for (int e = 0; e < 3; e++)
            {
                int a = w[e], b = w[(e + 1) % 3], c = w[(e + 2) % 3];
                long k = Llave(a, b);
                if (!opuestos.TryGetValue(k, out var l)) opuestos[k] = l = new List<int>();
                l.Add(c);
                if (!vecinos.TryGetValue(a, out var na)) vecinos[a] = na = new HashSet<int>(); na.Add(b);
                if (!vecinos.TryGetValue(b, out var nb)) vecinos[b] = nb = new HashSet<int>(); nb.Add(a);
            }
        }
        var borde = new HashSet<int>();
        foreach (var kv in opuestos) if (kv.Value.Count != 2) { borde.Add((int)(kv.Key >> 32)); borde.Add((int)(kv.Key & 0xffffffff)); }

        // nuevas posiciones de los vértices viejos
        var nuevaW = new Dictionary<int, Vector3>();
        foreach (var kv in posW)
        {
            int id = kv.Key; var v = kv.Value; float s = m.suave[id];
            if (s <= 0f || borde.Contains(id) || !vecinos.ContainsKey(id)) { nuevaW[id] = v; continue; }
            var ns = vecinos[id]; int n = ns.Count;
            float beta = n == 3 ? 3f / 16f : 3f / (8f * n);
            var sum = Vector3.zero; foreach (var j in ns) sum += posW[j];
            var loop = (1f - n * beta) * v + beta * sum;
            nuevaW[id] = Vector3.Lerp(v, loop, s);
        }
        // vértices de arista (por arista soldada)
        int sig = posW.Keys.Max() + 1;
        var idArista = new Dictionary<long, int>();
        var posArista = new Dictionary<int, Vector3>();
        var suaveN = new Dictionary<int, float>(m.suave);
        foreach (var kv in opuestos)
        {
            int a = (int)(kv.Key >> 32), b = (int)(kv.Key & 0xffffffff);
            var lin = (posW[a] + posW[b]) * 0.5f;
            float s = (m.suave[a] + m.suave[b]) * 0.5f;
            var p = lin;
            if (s > 0f && kv.Value.Count == 2) p = Vector3.Lerp(lin, 0.375f * (posW[a] + posW[b]) + 0.125f * (posW[kv.Value[0]] + posW[kv.Value[1]]), s);
            idArista[kv.Key] = sig; posArista[sig] = p; suaveN[sig] = s; sig++;
        }

        // malla nueva (vértices separados como en la original: costuras de UV se conservan)
        var r = new M { suave = suaveN };
        for (int i = 0; i < m.p.Count; i++)
        {
            r.p.Add(nuevaW[m.weld[i]]); r.n.Add(m.n[i]); r.uv.Add(m.uv[i]); r.uv2.Add(m.uv2[i]); r.w.Add(m.w[i]); r.weld.Add(m.weld[i]);
        }
        var medio = new Dictionary<long, int>();   // por arista de vértices separados
        int Medio(int a, int b)
        {
            long k = Llave(a, b);
            if (medio.TryGetValue(k, out int idx)) return idx;
            int ws = idArista[Llave(m.weld[a], m.weld[b])];
            idx = r.p.Count;
            r.p.Add(posArista[ws]); r.n.Add((m.n[a] + m.n[b]).normalized);
            r.uv.Add((m.uv[a] + m.uv[b]) * 0.5f); r.uv2.Add((m.uv2[a] + m.uv2[b]) * 0.5f);
            r.w.Add(Mezcla(m.w[a], m.w[b])); r.weld.Add(ws);
            medio[k] = idx; return idx;
        }
        for (int t = 0; t < m.tri.Count; t += 3)
        {
            int a = m.tri[t], b = m.tri[t + 1], c = m.tri[t + 2];
            int ab = Medio(a, b), bc = Medio(b, c), ca = Medio(c, a);
            r.tri.AddRange(new[] { a, ab, ca, ab, b, bc, ca, bc, c, ab, bc, ca });
        }
        return r;
    }
}
