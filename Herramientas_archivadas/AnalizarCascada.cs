using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AnalizarCascada
{
    [MenuItem("Tools/Amistad/Analizar cascada (consola)")]
    static void Analizar()
    {
        var todos = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        var cascada = todos.FirstOrDefault(t => t.name == "Cascada");
        var montana = todos.FirstOrDefault(t => t.name == "mountain_terrain_02");
        var piso = todos.FirstOrDefault(t => t.name == "Piso_FOREST");
        if (cascada == null || montana == null || piso == null) { Debug.LogWarning("Falta Cascada, montaña o piso"); return; }

        // Vértices de la cascada en mundo
        var mfC = cascada.GetComponentInChildren<MeshFilter>();
        var v = mfC.sharedMesh.vertices.Select(p => mfC.transform.TransformPoint(p)).ToArray();
        float yMin = v.Min(p => p.y), yMax = v.Max(p => p.y);
        var arriba = v.Where(p => p.y > yMax - (yMax - yMin) * 0.05f).ToArray();
        var abajo = v.Where(p => p.y < yMin + (yMax - yMin) * 0.05f).ToArray();
        Vector3 Prom(Vector3[] a) => a.Aggregate(Vector3.zero, (s, p) => s + p) / Mathf.Max(1, a.Length);
        Debug.Log($"[Cascada] malla local bounds {mfC.sharedMesh.bounds} | rot local {mfC.transform.localEulerAngles} (hijo de '{mfC.transform.parent?.name}')");
        Debug.Log($"[Cascada] mundo: y {yMin:0} .. {yMax:0} | borde arriba {Prom(arriba)} (ancho X {arriba.Max(p=>p.x)-arriba.Min(p=>p.x):0}, Z {arriba.Max(p=>p.z)-arriba.Min(p=>p.z):0}) | borde abajo {Prom(abajo)}");
        // perfil: centro de la cascada por alturas
        var sb = new StringBuilder("[Cascada] perfil (y -> x,z promedio): ");
        for (int i = 0; i <= 8; i++)
        {
            float y = Mathf.Lerp(yMin, yMax, i / 8f);
            var capa = v.Where(p => Mathf.Abs(p.y - y) < (yMax - yMin) / 16f).ToArray();
            if (capa.Length > 0) { var c = Prom(capa); sb.Append($" y{y:0}:({c.x:0},{c.z:0})"); }
        }
        Debug.Log(sb.ToString());

        // Collider temporal de la montaña para medir alturas
        var mfM = montana.GetComponentInChildren<MeshFilter>();
        var temp = new GameObject("temp_montana") { hideFlags = HideFlags.HideAndDontSave };
        temp.transform.SetPositionAndRotation(mfM.transform.position, mfM.transform.rotation);
        temp.transform.localScale = mfM.transform.lossyScale;
        var mc = temp.AddComponent<MeshCollider>(); mc.sharedMesh = mfM.sharedMesh;
        Physics.SyncTransforms();
        var bM = mfM.GetComponent<Renderer>().bounds;
        Debug.Log($"[Montaña] bounds {bM.center} tamaño {bM.size} | piso centro {piso.position} y={piso.position.y}");

        // Mapa de alturas alrededor de la cascada (paso 60)
        Vector3 c0 = Prom(abajo); c0 = new Vector3((Prom(arriba).x + c0.x) / 2, 0, (Prom(arriba).z + c0.z) / 2);
        var mapa = new StringBuilder($"[Montaña] alturas alrededor de ({c0.x:0},{c0.z:0}); filas = Z de +300 a -300, columnas = X de -480 a +480 (paso 60). '.' = sin montaña\n");
        for (int iz = 5; iz >= -5; iz--)
        {
            mapa.Append($"z{c0.z + iz * 60f,6:0}: ");
            for (int ix = -8; ix <= 8; ix++)
            {
                var o = new Vector3(c0.x + ix * 60f, bM.max.y + 50f, c0.z + iz * 60f);
                if (mc.Raycast(new Ray(o, Vector3.down), out var h, 5000f)) mapa.Append($"{h.point.y,5:0}"); else mapa.Append("    .");
            }
            mapa.Append('\n');
        }
        Debug.Log(mapa.ToString());

        // Distancia del borde superior de la cascada a la pared de la montaña (rayos horizontales desde varias direcciones)
        var top = Prom(arriba);
        foreach (var dir in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
        {
            var o = top - dir * 300f;
            if (mc.Raycast(new Ray(o, dir), out var h, 600f)) Debug.Log($"[Cascada] desde arriba, rayo hacia {dir}: pared a {h.distance - 300f:0} u (punto {h.point})");
        }
        // Búsqueda de un mejor lugar: mover la cascada (dx, dy, dz) para que
        //  - el borde de arriba quede apoyado en la montaña,
        //  - el agua caiga por delante de la pared sin atravesarla,
        //  - y aterrice lo más cerca posible del nivel del piso (donde va el lago).
        float H(float x, float z)
        {
            var o = new Vector3(x, bM.max.y + 50f, z);
            return mc.Raycast(new Ray(o, Vector3.down), out var hh, 5000f) ? hh.point.y : piso.position.y;
        }
        // Perfil de la caída: puntos (x,y) de la línea central de la cascada
        var perfil = new System.Collections.Generic.List<Vector2>();
        for (int i = 0; i <= 40; i++)
        {
            float y = Mathf.Lerp(yMin, yMax, i / 40f);
            var capa = v.Where(p => Mathf.Abs(p.y - y) < (yMax - yMin) / 60f).ToArray();
            if (capa.Length > 0) { var c = Prom(capa); perfil.Add(new Vector2(c.x, c.y)); }
        }
        var res = new System.Collections.Generic.List<(float score, float dx, float dy, float dz, float esc, float caida, float aterriza, float fondo)>();
        float zc = top.z;
        foreach (float esc in new[] { 1f, 0.85f, 0.7f, 0.55f })
        for (float dz = -400f; dz <= 400f; dz += 10f)
            for (float dx = -250f; dx <= 250f; dx += 10f)
            {
                float z = zc + dz;
                float hLabio = H(top.x + dx + 15f, z);
                float yTop = hLabio + 8f;                      // borde apoyado 8 u arriba del terreno
                float dy = yTop - top.y;
                // perfil escalado en Y respecto al borde superior
                var pf = perfil.Select(pp => new Vector2(pp.x + dx, yTop + (pp.y - top.y) * esc)).ToList();
                float xCaida = pf.Where(pp => pp.y < yTop - 120f * esc).Select(pp => pp.x).DefaultIfEmpty(top.x + dx).Average();
                float hCaida = H(xCaida, z);
                float caida = yTop - hCaida;
                if (caida < 100f) continue;
                // ¿la cascada alcanza el suelo? (su punto más bajo debe quedar bajo tierra)
                if (pf.Min(pp => pp.y) > hCaida) continue;
                // roca detrás del borde superior (para que no se vea cielo detrás del nacimiento del agua)
                float fondo = Mathf.Max(H(top.x + dx + 80f, z), H(top.x + dx + 140f, z)) - yTop;
                if (fondo < 40f) continue;
                bool choca = false;
                foreach (float oz in new[] { -50f, 0f, 50f })
                {
                    foreach (var pp in pf)
                    {
                        if (pp.y > yTop - 25f) continue;
                        if (pp.y < hCaida + 10f) continue;
                        if (H(pp.x, z + oz) > pp.y - 3f) { choca = true; break; }
                    }
                    if (choca) break;
                }
                if (choca) continue;
                float score = caida - 3f * Mathf.Max(0f, hCaida - piso.position.y) - 0.6f * Mathf.Abs(dz) - 0.3f * Mathf.Abs(dx) + 0.2f * Mathf.Min(fondo, 150f);
                res.Add((score, dx, dy, dz, esc, caida, hCaida, fondo));
            }
        foreach (var r in res.OrderByDescending(r => r.score).Take(12))
            Debug.Log($"[Lugar] score {r.score:0} | mover dx {r.dx:0} dy {r.dy:0} dz {r.dz:0} | escala Y x{r.esc:0.00} | caída {r.caida:0} u, aterriza a y={r.aterriza:0}, roca detrás +{r.fondo:0}");
        Object.DestroyImmediate(temp);
    }
}
