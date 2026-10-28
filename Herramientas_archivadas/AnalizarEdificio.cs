using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

public static class AnalizarEdificio
{
    [MenuItem("Tools/Amistad/Analizar edificio (ruim.008)")]
    static void Analizar()
    {
        var sb = new StringBuilder();
        var todos = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string n) => todos.FirstOrDefault(t => t.name == n);

        var ed = T("ruim.008");
        if (ed == null) { Debug.LogWarning("No encontré ruim.008"); return; }
        sb.AppendLine($"ruim.008 ruta: {Ruta(ed)}");
        sb.AppendLine($"  pos {ed.position} rot {ed.eulerAngles} lossy {ed.lossyScale} local {ed.localPosition} / {ed.localEulerAngles} / {ed.localScale}");
        foreach (var c in ed.GetComponents<Component>()) sb.AppendLine($"  comp {c.GetType().Name}");
        sb.AppendLine($"  hijos {ed.childCount}");
        var r = ed.GetComponent<Renderer>();
        if (r != null) sb.AppendLine($"  bounds centro {r.bounds.center} tamaño {r.bounds.size} | mats {string.Join(", ", r.sharedMaterials.Select(m => m ? m.name + "(" + m.shader.name + ")" : "null"))}");
        var mf = ed.GetComponent<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            var m = mf.sharedMesh;
            sb.AppendLine($"  malla {m.name} verts {m.vertexCount} submallas {m.subMeshCount} readable {m.isReadable} bounds {m.bounds} ruta {AssetDatabase.GetAssetPath(m)}");
            for (int s = 0; s < m.subMeshCount; s++) sb.AppendLine($"    sub {s}: {m.GetSubMesh(s).indexCount / 3} tris");
            try
            {
                var v = m.vertices; var tri = m.triangles;
                sb.AppendLine($"  lectura OK: {v.Length} verts {tri.Length / 3} tris");
                // Islas (vértices soldados por posición)
                var clave = new Dictionary<Vector3Int, int>();
                int[] id = new int[v.Length];
                float q = m.bounds.size.magnitude * 1e-5f + 1e-6f;
                for (int i = 0; i < v.Length; i++)
                {
                    var k = new Vector3Int(Mathf.RoundToInt(v[i].x / q), Mathf.RoundToInt(v[i].y / q), Mathf.RoundToInt(v[i].z / q));
                    if (!clave.TryGetValue(k, out id[i])) { id[i] = clave.Count; clave[k] = id[i]; }
                }
                var uf = Enumerable.Range(0, clave.Count).ToArray();
                int F(int x) { while (uf[x] != x) { uf[x] = uf[uf[x]]; x = uf[x]; } return x; }
                for (int t = 0; t < tri.Length; t += 3)
                {
                    int a = F(id[tri[t]]), b = F(id[tri[t + 1]]), c = F(id[tri[t + 2]]);
                    uf[b] = a; uf[F(c)] = F(a);
                }
                var islas = new Dictionary<int, (int tris, Bounds b)>();
                for (int t = 0; t < tri.Length; t += 3)
                {
                    int raiz = F(id[tri[t]]);
                    var p = ed.TransformPoint(v[tri[t]]);
                    if (!islas.TryGetValue(raiz, out var info)) info = (0, new Bounds(p, Vector3.zero));
                    info.b.Encapsulate(p); info.b.Encapsulate(ed.TransformPoint(v[tri[t + 1]])); info.b.Encapsulate(ed.TransformPoint(v[tri[t + 2]]));
                    info.tris++;
                    islas[raiz] = info;
                }
                sb.AppendLine($"  ISLAS: {islas.Count}");
                var csv = new StringBuilder("tris,minx,miny,minz,maxx,maxy,maxz\n");
                foreach (var i in islas.Values) csv.AppendLine($"{i.tris},{i.b.min.x:0.###},{i.b.min.y:0.###},{i.b.min.z:0.###},{i.b.max.x:0.###},{i.b.max.y:0.###},{i.b.max.z:0.###}");
                File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/islas_ruim008.csv"), csv.ToString());
                var obj = new StringBuilder();
                foreach (var p in v) { var w = ed.TransformPoint(p); obj.AppendLine($"v {w.x:0.###} {w.y:0.###} {w.z:0.###}"); }
                for (int s = 0; s < m.subMeshCount; s++)
                {
                    obj.AppendLine($"g sub{s}");
                    var ts = m.GetTriangles(s);
                    for (int t = 0; t < ts.Length; t += 3) obj.AppendLine($"f {ts[t] + 1} {ts[t + 1] + 1} {ts[t + 2] + 1}");
                }
                File.WriteAllText(Path.Combine(Application.dataPath, "../Logs/ruim008_mundo.obj"), obj.ToString());
                var orden = islas.Values.OrderByDescending(i => i.b.size.magnitude).ToList();
                foreach (var i in orden.Take(25)) sb.AppendLine($"    isla {i.tris} tris, centro {i.b.center}, tamaño {i.b.size}");
                var tam = orden.Select(i => i.b.size.magnitude).ToList();
                sb.AppendLine($"  tamaños: max {tam.Max():0.0} mediana {tam[tam.Count / 2]:0.00} min {tam.Min():0.000}");
                foreach (float lim in new[] { 0.5f, 1, 2, 5, 10, 20 }) sb.AppendLine($"    islas > {lim}: {tam.Count(x => x > lim)}");
            }
            catch (System.Exception e) { sb.AppendLine("  NO SE PUDO LEER: " + e.Message); }
        }

        // Vecinos
        if (ed.parent != null)
        {
            sb.AppendLine($"Padre {ed.parent.name} pos {ed.parent.position} lossy {ed.parent.lossyScale}; hermanos:");
            foreach (Transform h in ed.parent)
            {
                var rr = h.GetComponent<Renderer>();
                sb.AppendLine($"  {h.name} activo {h.gameObject.activeSelf} {(rr ? $"centro {rr.bounds.center} tamaño {rr.bounds.size}" : "")}");
            }
        }
        foreach (var n in new[] { "CITY", "EDIFICIOS", "Escena", "Plane", "MEMO_PERFIL", "CM3_MEMO", "Main Camera", "VFX_CIUDAD", "Spline_P2_Ciudad" })
        {
            var t = T(n); if (t == null) { sb.AppendLine($"{n}: no existe"); continue; }
            var rr = t.GetComponentsInChildren<Renderer>().Where(x => x.enabled).ToArray();
            string b = "";
            if (rr.Length > 0) { var bb = rr[0].bounds; foreach (var x in rr) bb.Encapsulate(x.bounds); b = $"bounds {bb.center} tamaño {bb.size}"; }
            sb.AppendLine($"{n}: ruta {Ruta(t)} pos {t.position} rot {t.eulerAngles} lossy {t.lossyScale} {b}");
            sb.AppendLine("   comps: " + string.Join(", ", t.GetComponents<Component>().Select(c => c.GetType().Name)));
        }

        // Timeline P1
        foreach (var dn in new[] { "Director_P1_Bosque", "Director_P2_Ciudad" })
        {
            var d = T(dn)?.GetComponent<PlayableDirector>();
            if (d == null || !(d.playableAsset is TimelineAsset ta)) continue;
            sb.AppendLine($"{dn}: {ta.name} dur {ta.duration:0.00}");
            foreach (var tr in ta.GetOutputTracks())
            {
                var bind = d.GetGenericBinding(tr);
                sb.AppendLine($"  pista {tr.GetType().Name} '{tr.name}' -> {(bind ? bind.name : "-")}");
                if (tr is AnimationTrack at && at.infiniteClip != null)
                {
                    var clip = at.infiniteClip;
                    sb.AppendLine($"     infinito '{clip.name}' dur {clip.length:0.00}");
                    foreach (var bnd in AnimationUtility.GetCurveBindings(clip))
                    {
                        var curva = AnimationUtility.GetEditorCurve(clip, bnd);
                        var muestras = new List<string>();
                        for (float tt = 0; tt <= clip.length + 0.01f; tt += 0.5f) muestras.Add($"{tt:0.0}:{curva.Evaluate(tt):0.###}");
                        sb.AppendLine($"        {bnd.path}/{bnd.type.Name}.{bnd.propertyName}: {string.Join(" ", muestras)}");
                    }
                }
                foreach (var c in tr.GetClips())
                {
                    sb.AppendLine($"     clip '{c.displayName}' {c.start:0.00}-{c.end:0.00} ({c.asset?.GetType().Name})");
                    if (c.asset is AnimationPlayableAsset apa && apa.clip != null && bind != null)
                    {
                        var clip = apa.clip;
                        foreach (var bnd in AnimationUtility.GetCurveBindings(clip))
                        {
                            var curva = AnimationUtility.GetEditorCurve(clip, bnd);
                            var muestras = new List<string>();
                            for (double tt = System.Math.Ceiling(c.start); tt <= c.end + 0.01; tt += 1.0)
                                muestras.Add($"{tt:0}:{curva.Evaluate((float)((tt - c.start) * c.timeScale + c.clipIn)):0.##}");
                            sb.AppendLine($"        {bnd.path}/{bnd.propertyName}: {string.Join(" ", muestras)}");
                        }
                    }
                }
            }
        }
        sb.AppendLine($"Gravedad {Physics.gravity} fixedDelta {Time.fixedDeltaTime}");
        var cm3 = T("CM3_MEMO");
        if (cm3 != null)
        {
            foreach (var c in cm3.GetComponents<Component>())
            {
                var so = new SerializedObject(c);
                var it = so.GetIterator();
                var lin = new StringBuilder($"  [{c.GetType().Name}] ");
                int n = 0;
                if (it.NextVisible(true)) do { if (n++ < 40 && it.propertyType != SerializedPropertyType.Generic) lin.Append($"{it.propertyPath}={Valor(it)}; "); } while (it.NextVisible(true));
                sb.AppendLine(lin.ToString());
            }
        }
        string ruta = Path.Combine(Application.dataPath, "../Logs/analisis_edificio.txt");
        File.WriteAllText(ruta, sb.ToString());
        Debug.Log("[Analizar edificio] listo -> Logs/analisis_edificio.txt");
    }

    static string Valor(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Float: return p.floatValue.ToString("0.###");
            case SerializedPropertyType.Integer: return p.intValue.ToString();
            case SerializedPropertyType.Boolean: return p.boolValue.ToString();
            case SerializedPropertyType.Enum: return p.enumValueIndex.ToString();
            case SerializedPropertyType.ObjectReference: return p.objectReferenceValue ? p.objectReferenceValue.name : "null";
            case SerializedPropertyType.Vector3: return p.vector3Value.ToString();
            case SerializedPropertyType.String: return p.stringValue;
            default: return p.propertyType.ToString();
        }
    }

    static string Ruta(Transform t) => t.parent == null ? t.name : Ruta(t.parent) + "/" + t.name;
}
