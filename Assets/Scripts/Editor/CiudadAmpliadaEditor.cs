using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Tools > Amistad > Ciudad: ampliar (más grande)
/// Agranda la ciudad para la caminata de Ale (después de 23 s) y para que el fondo se sienta enorme:
/// 1) PISO: losetas extra del mismo piso (mismo material y tiling) alrededor del original.
/// 2) AVENIDA_ALE: la calle por donde camina Memi (hueco entre ruim.005 y ruim.007, X = AvenidaX) sigue hacia -Z,
///    flanqueada por ruinas que van creciendo con la distancia. La calle queda libre para Ale.
/// 3) ANILLO_CERCA / ANILLO_LEJOS: copias de los bloques ruim.00X con giro y escala variada; las lejanas son torres
///    2-3 veces más altas (skyline) y no proyectan sombra (optimización).
/// 4) NIEBLA_CIUDAD (hijo de Director_P2_Ciudad): niebla lineal solo en la ciudad (perspectiva atmosférica).
/// 5) Far clip de las cámaras CM3 a FarClip para que se vea el fondo.
/// 6) Guarda capturas de revisión en Logs/ciudad_ampliada (incluye las cámaras del bosque para checar que no se vea la ciudad).
/// Se puede volver a correr: borra CIUDAD_AMPLIADA y la rehace igual (semilla fija). Ctrl+Z la deshace.
/// </summary>
public static class CiudadAmpliadaEditor
{
    const string NombreGrupo = "CIUDAD_AMPLIADA";
    const string NombreNiebla = "NIEBLA_CIUDAD";
    const string CarpetaCapturas = "Logs/ciudad_ampliada";
    const int Semilla = 2026;

    // Calle de Memi que continúa hacia -Z para Ale (coordenadas de mundo).
    const float AvenidaX = -29f;
    const float AvenidaMedio = 45f;      // medio ancho libre
    const float AvenidaDesdeZ = -470f;   // donde acaba la ciudad original
    const float AvenidaHastaZ = -2900f;

    static readonly Vector2 Foco = new Vector2(-29f, -300f); // zona del encuentro
    const float RadioCerca = 1700f;
    const float RadioLejos = 3400f;
    const float PasoCerca = 520f;
    const float PasoLejos = 760f;
    const int MaxBloques = 120;
    const int PisoMitad = 4;             // (2*4+1)^2 losetas
    const float FarClip = 8000f;

    static System.Random rng;
    static float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
    static float Yaw() => rng.Next(4) * 90f + R(-5f, 5f);

    [MenuItem("Tools/Amistad/Ciudad: ampliar (más grande)")]
    static void Ampliar()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Ciudad] Sal de Play primero."); return; }
        var city = GameObject.Find("CITY");
        if (city == null) { Debug.LogError("[Ciudad] No encontré CITY. Abre ciudad.unity."); return; }
        var edif = city.transform.Find("EDIFICIOS");
        var piso = city.transform.Find("Escena/Plane");
        if (edif == null || piso == null) { Debug.LogError("[Ciudad] Faltan EDIFICIOS o Escena/Plane dentro de CITY."); return; }

        var previo = city.transform.Find(NombreGrupo);
        if (previo != null) Undo.DestroyObjectImmediate(previo.gameObject);

        rng = new System.Random(Semilla);
        var log = new StringBuilder();

        var fuentes = edif.GetComponentsInChildren<MeshFilter>(true)
            .Where(m => m.sharedMesh != null && m.name.StartsWith("ruim"))
            .Select(m => m.gameObject).ToList();
        if (fuentes.Count == 0) { Debug.LogError("[Ciudad] No hay bloques ruim.* en EDIFICIOS."); return; }
        log.AppendLine("Bloques fuente: " + string.Join(", ", fuentes.Select(f => f.name)));

        // ---- Zonas prohibidas: ciudad original, la calle y el bosque ----
        var ocupados = fuentes.Select(f => XZ(AABB(f))).ToList();
        ocupados.Add(Rect.MinMaxRect(AvenidaX - AvenidaMedio, -9000f, AvenidaX + AvenidaMedio, AvenidaDesdeZ + 60f));
        float limiteX = float.MaxValue;
        var bosque = GameObject.Find("BOSQUE");
        if (bosque != null)
        {
            var rs = bosque.GetComponentsInChildren<Renderer>();
            if (rs.Length > 0)
            {
                var bb = rs[0].bounds;
                foreach (var r in rs) bb.Encapsulate(r.bounds);
                limiteX = Mathf.Max(bb.min.x - 400f, 900f);
                log.AppendLine($"BOSQUE bounds min {bb.min} max {bb.max} -> la ciudad no pasa de X = {limiteX:0}");
            }
        }

        var grupo = new GameObject(NombreGrupo);
        grupo.transform.SetParent(city.transform, false);
        grupo.transform.localScale = Vector3.one / city.transform.localScale.x; // adentro todo queda en unidades de mundo
        var gPiso = Hijo(grupo.transform, "PISO");
        var gAv = Hijo(grupo.transform, "AVENIDA_ALE");
        var gCerca = Hijo(grupo.transform, "ANILLO_CERCA");
        var gLejos = Hijo(grupo.transform, "ANILLO_LEJOS");

        // ---- 1) Piso ----
        float tam = AABB(piso.gameObject).size.x;
        int nPiso = 0;
        for (int i = -PisoMitad; i <= PisoMitad; i++)
            for (int j = -PisoMitad; j <= PisoMitad; j++)
            {
                if (i == 0 && j == 0) continue;
                var pos = piso.position + new Vector3(i * tam, 0f, j * tam);
                if (pos.x + tam * 0.5f > limiteX + 200f) continue;
                var p = Object.Instantiate(piso.gameObject, gPiso, true);
                p.name = $"Piso_{i}_{j}";
                p.transform.position = pos;
                var col = p.GetComponent<Collider>();
                if (col != null) Object.DestroyImmediate(col);
                var mr = p.GetComponent<MeshRenderer>();
                if (mr != null) mr.shadowCastingMode = ShadowCastingMode.Off;
                Estatico(p);
                nPiso++;
            }

        // ---- 2) Avenida para Ale ----
        int n = 0; long tris = 0; int nAv = 0;
        foreach (int lado in new[] { -1, 1 })
        {
            float z = AvenidaDesdeZ - R(0f, 40f);
            int intentos = 0;
            while (z > AvenidaHastaZ && intentos++ < 80 && n < MaxBloques)
            {
                float t = Mathf.InverseLerp(AvenidaDesdeZ, AvenidaHastaZ, z);
                var f = fuentes[rng.Next(fuentes.Count)];
                float sxz = R(0.75f, 0.95f);
                float sy = R(1.0f, 1.3f) * Mathf.Lerp(1.1f, 1.9f, t);
                var b = CrearBloque(f, gAv, $"Av_{(lado < 0 ? "Izq" : "Der")}_{n:000}", Yaw(), new Vector3(sxz, sy, sxz));
                var box = AABB(b);
                float borde = AvenidaX + lado * (AvenidaMedio + R(0f, 25f));
                float dx = lado > 0 ? borde - box.min.x : borde - box.max.x;
                float dz = z - box.max.z;
                b.transform.position += new Vector3(dx, 0f, dz);
                var rect = XZ(AABB(b));
                if (Choca(Encoge(rect, 0.04f), ocupados) || rect.xMax > limiteX)
                {
                    Object.DestroyImmediate(b); z -= 60f; continue;
                }
                ocupados.Add(rect); n++; nAv++; tris += Tris(b);
                z = rect.yMin - R(25f, 70f);
            }
        }

        // ---- 3) Anillos ----
        var cand = new List<(Vector2 p, bool lejos)>();
        for (float x = Foco.x - RadioLejos; x <= Foco.x + RadioLejos; x += PasoCerca)
            for (float z = Foco.y - RadioLejos; z <= Foco.y + RadioLejos; z += PasoCerca)
            {
                var p = new Vector2(x + R(-0.3f, 0.3f) * PasoCerca, z + R(-0.3f, 0.3f) * PasoCerca);
                if (Vector2.Distance(p, Foco) < RadioCerca) cand.Add((p, false));
            }
        for (float x = Foco.x - RadioLejos; x <= Foco.x + RadioLejos; x += PasoLejos)
            for (float z = Foco.y - RadioLejos; z <= Foco.y + RadioLejos; z += PasoLejos)
            {
                var p = new Vector2(x + R(-0.3f, 0.3f) * PasoLejos, z + R(-0.3f, 0.3f) * PasoLejos);
                float d = Vector2.Distance(p, Foco);
                if (d >= RadioCerca && d <= RadioLejos) cand.Add((p, true));
            }
        for (int i = cand.Count - 1; i > 0; i--) { int k = rng.Next(i + 1); var tmp = cand[i]; cand[i] = cand[k]; cand[k] = tmp; }

        int nCerca = 0, nLejos = 0;
        foreach (var c in cand.OrderBy(q => q.lejos ? 1 : 0))
        {
            if (n >= MaxBloques) break;
            if (c.p.x > limiteX) continue;
            float d = Vector2.Distance(c.p, Foco);
            float t = Mathf.InverseLerp(600f, RadioLejos, d);
            var f = fuentes[rng.Next(fuentes.Count)];
            float sxz = c.lejos ? R(1.0f, 1.35f) : R(0.8f, 1.05f);
            float sy = c.lejos ? R(1.8f, 3.0f) : R(1.0f, 1.5f) * Mathf.Lerp(1f, 1.4f, t);
            var b = CrearBloque(f, c.lejos ? gLejos : gCerca, (c.lejos ? "Lejos_" : "Cerca_") + n.ToString("000"), Yaw(), new Vector3(sxz, sy, sxz));
            var box = AABB(b);
            b.transform.position += new Vector3(c.p.x - box.center.x, 0f, c.p.y - box.center.z);
            var rect = XZ(AABB(b));
            if (Choca(Encoge(rect, 0.12f), ocupados) || rect.xMax > limiteX) { Object.DestroyImmediate(b); continue; }
            ocupados.Add(rect); n++; tris += Tris(b);
            if (c.lejos)
            {
                foreach (var r in b.GetComponentsInChildren<MeshRenderer>(true)) r.shadowCastingMode = ShadowCastingMode.Off;
                nLejos++;
            }
            else nCerca++;
        }

        Undo.RegisterCreatedObjectUndo(grupo, "Ciudad ampliada");

        // ---- 4) Niebla solo en la ciudad ----
        NieblaCiudad niebla = null;
        var dir = city.transform.Find("Director_P2_Ciudad");
        if (dir != null)
        {
            var existente = dir.Find(NombreNiebla);
            if (existente != null) niebla = existente.GetComponent<NieblaCiudad>();
            if (niebla == null)
            {
                var gn = new GameObject(NombreNiebla);
                gn.transform.SetParent(dir, false);
                niebla = gn.AddComponent<NieblaCiudad>();
                Undo.RegisterCreatedObjectUndo(gn, "Niebla ciudad");
            }
        }
        else log.AppendLine("OJO: no encontré Director_P2_Ciudad; no se creó la niebla.");

        // Que el build no quite las variantes de niebla (la escena se guarda con niebla apagada).
        var gs = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset");
        if (gs.Length > 0)
        {
            var so = new SerializedObject(gs[0]);
            var strip = so.FindProperty("m_FogStripping");
            var keep = so.FindProperty("m_FogKeepLinear");
            if (strip != null) strip.intValue = 1;
            if (keep != null && keep.propertyType == SerializedPropertyType.Boolean) keep.boolValue = true;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
        }

        // ---- 5) Far clip ----
        foreach (var cm in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (!cm.name.StartsWith("CM3")) continue;
            Undo.RecordObject(cm, "Far clip ciudad");
            var lens = cm.Lens; lens.FarClipPlane = FarClip; cm.Lens = lens;
            EditorUtility.SetDirty(cm);
            log.AppendLine($"{cm.name}: far clip {FarClip}");
        }
        var main = Camera.main;
        if (main != null) { Undo.RecordObject(main, "Far clip"); main.farClipPlane = FarClip; }

        EditorSceneManager.MarkSceneDirty(city.scene);

        log.AppendLine($"Piso: {nPiso} losetas de {tam:0} u");
        log.AppendLine($"Bloques nuevos: {n} (avenida {nAv}, cerca {nCerca}, lejos {nLejos}) · triángulos nuevos {tris:N0}");
        Directory.CreateDirectory(CarpetaCapturas);
        File.WriteAllText(Path.Combine(CarpetaCapturas, "info.txt"), log.ToString());
        Debug.Log("[Ciudad] " + log.ToString().Replace("\n", " | "));

        Capturas(niebla);
    }

    [MenuItem("Tools/Amistad/Ciudad: capturas de revisión")]
    static void SoloCapturas()
    {
        var city = GameObject.Find("CITY");
        var dir = city != null ? city.transform.Find("Director_P2_Ciudad/" + NombreNiebla) : null;
        Capturas(dir != null ? dir.GetComponent<NieblaCiudad>() : null);
    }

    [MenuItem("Tools/Amistad/Ciudad: quitar ampliación")]
    static void Quitar()
    {
        var city = GameObject.Find("CITY");
        if (city == null) return;
        var g = city.transform.Find(NombreGrupo);
        if (g != null) Undo.DestroyObjectImmediate(g.gameObject);
        var nb = city.transform.Find("Director_P2_Ciudad/" + NombreNiebla);
        if (nb != null) Undo.DestroyObjectImmediate(nb.gameObject);
        EditorSceneManager.MarkSceneDirty(city.scene);
    }

    // ---------------- Capturas ----------------
    static void Capturas(NieblaCiudad niebla)
    {
        Directory.CreateDirectory(CarpetaCapturas);
        foreach (var f in Directory.GetFiles(CarpetaCapturas, "*.png")) File.Delete(f);
        var mira = new System.Func<Vector3, Vector3, Quaternion>((desde, hacia) => Quaternion.LookRotation(hacia - desde, Vector3.up));

        var p1 = new Vector3(AvenidaX, 16f, -330f);
        Capturar("1_contraplano_Ale.png", p1, mira(p1, new Vector3(AvenidaX, 22f, -1400f)), 45f, true, niebla);
        var parts = Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Where(r => r.enabled).ToList();
        parts.ForEach(r => r.enabled = false);
        try
        {
            Capturar("1b_contraplano_Ale_sin_humo.png", p1, mira(p1, new Vector3(AvenidaX, 22f, -1400f)), 45f, true, niebla);
            var p1c = new Vector3(AvenidaX, 16f, -560f);
            Capturar("1c_contraplano_Ale_mas_atras.png", p1c, mira(p1c, new Vector3(AvenidaX, 30f, -2000f)), 40f, true, niebla);
            var p1d = new Vector3(AvenidaX + 25f, 8f, -420f);
            Capturar("1d_contrapicado_avenida.png", p1d, mira(p1d, new Vector3(AvenidaX, 120f, -1500f)), 50f, true, niebla);
        }
        finally { parts.ForEach(r => r.enabled = true); }
        var p2 = new Vector3(AvenidaX, 14f, -170f);
        Capturar("2_telefoto_hacia_Ale.png", p2, mira(p2, new Vector3(AvenidaX, 45f, -2500f)), 20f, true, niebla);
        var p3 = new Vector3(-27f, 14.85f, -168f);
        Capturar("3_toma_Memi_fondo.png", p3, mira(p3, new Vector3(-27f, 20f, 1000f)), 45f, true, niebla);
        var p4 = new Vector3(AvenidaX, 1500f, -2600f);
        Capturar("4_aerea.png", p4, mira(p4, new Vector3(AvenidaX, 0f, -300f)), 55f, true, niebla);
        Capturar("5_cenital.png", new Vector3(AvenidaX, 7000f, -300f), Quaternion.Euler(90f, 0f, 0f), 60f, false, niebla);

        foreach (var cm in Object.FindObjectsByType<CinemachineCamera>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (cm.name.StartsWith("CM1") || cm.name.StartsWith("CM2"))
                Capturar($"6_{cm.name}.png", cm.transform.position, cm.transform.rotation, cm.Lens.FieldOfView, false, niebla, cm.Lens.FarClipPlane);

        Debug.Log("[Ciudad] Capturas en " + Path.GetFullPath(CarpetaCapturas));
    }

    internal static void Capturar(string archivo, Vector3 pos, Quaternion rot, float fov, bool conNiebla, NieblaCiudad niebla, float far = FarClip)
    {
        bool f0 = RenderSettings.fog; var c0 = RenderSettings.fogColor; var m0 = RenderSettings.fogMode;
        float s0 = RenderSettings.fogStartDistance, e0 = RenderSettings.fogEndDistance;
        var go = new GameObject("_CamRevision") { hideFlags = HideFlags.HideAndDontSave };
        try
        {
            if (conNiebla)
            {
                RenderSettings.fog = true;
                RenderSettings.fogMode = FogMode.Linear;
                RenderSettings.fogColor = niebla != null ? niebla.color : new Color(0.56f, 0.33f, 0.19f);
                RenderSettings.fogStartDistance = niebla != null ? niebla.inicio : 350f;
                RenderSettings.fogEndDistance = niebla != null ? niebla.fin : 4500f;
            }
            else RenderSettings.fog = false;

            var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(pos, rot);
            cam.fieldOfView = fov; cam.nearClipPlane = 0.3f; cam.farClipPlane = far; cam.allowHDR = true;
            cam.GetUniversalAdditionalCameraData().renderPostProcessing = true;
            const int W = 1600, H = 900;
            var rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            cam.targetTexture = rt;
            cam.Render();
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            File.WriteAllBytes(Path.Combine(CarpetaCapturas, archivo), tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            cam.targetTexture = null;
            RenderTexture.ReleaseTemporary(rt);
        }
        catch (System.Exception ex) { Debug.LogWarning($"[Ciudad] No pude capturar {archivo}: {ex.Message}"); }
        finally
        {
            Object.DestroyImmediate(go);
            RenderSettings.fog = f0; RenderSettings.fogColor = c0; RenderSettings.fogMode = m0;
            RenderSettings.fogStartDistance = s0; RenderSettings.fogEndDistance = e0;
        }
    }

    // ---------------- Utilidades ----------------
    static GameObject CrearBloque(GameObject fuente, Transform padre, string nombre, float yaw, Vector3 escala)
    {
        var copia = Object.Instantiate(fuente);
        copia.name = fuente.name;
        copia.SetActive(true);
        copia.transform.SetPositionAndRotation(fuente.transform.position, fuente.transform.rotation);
        copia.transform.localScale = fuente.transform.lossyScale;
        var mr = copia.GetComponent<MeshRenderer>();
        if (mr != null) mr.enabled = true;
        var box = AABB(copia);
        var env = new GameObject(nombre);
        env.transform.SetParent(padre, false);
        env.transform.position = new Vector3(box.center.x, box.min.y, box.center.z);
        copia.transform.SetParent(env.transform, true);
        env.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        env.transform.localScale = escala;
        Estatico(copia);
        return env;
    }

    static Transform Hijo(Transform padre, string nombre)
    {
        var g = new GameObject(nombre);
        g.transform.SetParent(padre, false);
        return g.transform;
    }

    static void Estatico(GameObject g) =>
        GameObjectUtility.SetStaticEditorFlags(g, StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);

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

    static long Tris(GameObject raiz)
    {
        long t = 0;
        foreach (var mf in raiz.GetComponentsInChildren<MeshFilter>(true))
            if (mf.sharedMesh != null)
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) t += mf.sharedMesh.GetIndexCount(s) / 3;
        return t;
    }

    static Rect XZ(Bounds b) => Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z);
    static Rect Encoge(Rect r, float f) => Rect.MinMaxRect(r.xMin + r.width * f, r.yMin + r.height * f, r.xMax - r.width * f, r.yMax - r.height * f);
    static bool Choca(Rect r, List<Rect> l) => l.Any(o => o.Overlaps(r));
}
