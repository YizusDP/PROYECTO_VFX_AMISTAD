using System.IO;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

/// Tools > Amistad > Revisión y pruebas > Capturas por tiempo (Play, saltando)
/// Entra a Play, salta el Timeline principal a cada tiempo de la lista (sin esperar a que corra),
/// guarda el Game view en Logs/capturas_tiempos/<etiqueta>_t_XX.X.png y el estado (cámara activa, objetos clave,
/// luz, volúmenes) en <etiqueta>_estado.txt; luego sale de Play. No ensucia la escena.
/// Tiempos y etiqueta: SessionState "Amistad_CapTiempos" (ej. "50,63.6,69") y "Amistad_CapEtiqueta".
[InitializeOnLoad]
public static class CapturasTiemposEditor
{
    const string ClaveActiva = "Amistad_CapActiva", ClaveTiempos = "Amistad_CapTiempos", ClaveEtiqueta = "Amistad_CapEtiqueta";
    const string Carpeta = "Logs/capturas_tiempos";
    const string TiemposPorDefecto = "5,15,20,30,40,47,50,52.5,55,58,61,63.6,65,66.8,69,72";
    const int CuadrosEspera = 8;
    static double[] tiempos; static int indice, espera, cuadro0; static bool capturado; static string etiqueta;

    static CapturasTiemposEditor()
    {
        if (SessionState.GetBool(ClaveActiva, false)) Preparar();
    }

    [MenuItem("Tools/Amistad/Revisión y pruebas/Capturas por tiempo (Play, saltando)", false, 900)]
    public static void Iniciar()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Capturas] Sal de Play primero."); return; }
        Directory.CreateDirectory(Carpeta);
        SessionState.SetBool(ClaveActiva, true);
        EditorApplication.isPlaying = true;
    }

    static void Preparar()
    {
        var txt = SessionState.GetString(ClaveTiempos, TiemposPorDefecto);
        if (string.IsNullOrWhiteSpace(txt)) txt = TiemposPorDefecto;
        tiempos = txt.Split(',').Select(s => double.Parse(s.Trim(), System.Globalization.CultureInfo.InvariantCulture)).ToArray();
        etiqueta = SessionState.GetString(ClaveEtiqueta, "cap");
        if (string.IsNullOrWhiteSpace(etiqueta)) etiqueta = "cap";
        indice = 0; espera = -1;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        Application.runInBackground = true;   // si Unity está en segundo plano, el Play no se congela (solo en esta sesión de Play)
        var go = GameObject.Find("Director_P1_Bosque");
        var d = go != null ? go.GetComponent<PlayableDirector>() : null;
        if (d == null) return;
        if (indice >= tiempos.Length)
        {
            SessionState.SetBool(ClaveActiva, false);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            Debug.Log($"[Capturas] Listo: {tiempos.Length} capturas en {Carpeta} ({etiqueta}).");
            return;
        }
        if (espera < 0)
        {
            d.Pause(); cuadro0 = Time.frameCount; capturado = false;
            espera = 1;
            if (indice == 0) File.WriteAllText($"{Carpeta}/{etiqueta}_estado.txt", "");
        }
        d.time = tiempos[indice]; d.Evaluate();
        int pasados = Time.frameCount - cuadro0;
        if (ShaderUtil.anythingCompiling) { cuadro0 = Time.frameCount; return; }   // variantes nuevas (p. ej. con lightmaps) salen cian mientras compilan
        if (!capturado && pasados >= CuadrosEspera)
        {
            ScreenCapture.CaptureScreenshot($"{Carpeta}/{etiqueta}_t_{tiempos[indice]:00.0}.png");
            capturado = true; cuadro0 = Time.frameCount;
        }
        else if (capturado && pasados >= 3)
        {
            File.AppendAllText($"{Carpeta}/{etiqueta}_estado.txt", Estado(tiempos[indice]));
            indice++; espera = -1;
        }
    }

    static string Estado(double t)
    {
        var sb = new StringBuilder();
        var cam = Camera.main;
        var brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        var vc = brain != null ? brain.ActiveVirtualCamera : null;
        sb.Append($"t={t:0.00} | vcam={(vc != null ? vc.Name : "-")}");
        if (cam != null) sb.Append($" | cam pos={cam.transform.position:F1} rot={cam.transform.eulerAngles:F1} fov={cam.fieldOfView:0.0}");
        var luz = GameObject.Find("Directional Light");
        if (luz != null) sb.Append($" | luz rot={luz.transform.eulerAngles:F1}");
        foreach (var n in new[] { "BOSQUE", "CITY", "SALUDO_EPICO", "FINAL_NUCLEAR", "TITULO_FINAL", "GODZILLA", "AVIONETA", "ALE", "MEMO_PERFIL" })
        {
            var x = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(q => q.name == n && q.gameObject.scene.IsValid());
            sb.Append($" | {n}={(x == null ? "?" : (x.gameObject.activeInHierarchy ? "on" : "off"))}");
            if (x != null && (n == "GODZILLA" || n == "AVIONETA" || n == "ALE")) sb.Append($"@{x.position:F0}/{x.eulerAngles.y:0}");
        }
        var vols = Object.FindObjectsByType<UnityEngine.Rendering.Volume>(FindObjectsSortMode.None)
            .Where(v => v.isActiveAndEnabled && v.weight > 0.001f).OrderBy(v => v.name).Select(v => $"{v.name}:{v.weight:0.00}");
        sb.Append(" | vol=" + string.Join(",", vols));
        sb.Append(Pies("ALE", cam)).Append(Pies("MEMO_PERFIL", cam));
        sb.AppendLine();
        return sb.ToString();
    }

    // Pies: suela = vértice más bajo del cuerpo (piso de la ciudad en y=0; + flota, - se hunde),
    // vp = parte más baja / más alta del cuerpo en el viewport (abajo < 0 = pies cortados por el cuadro).
    static Mesh mallaPies;
    static string Pies(string n, Camera cam)
    {
        var x = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(q => q.name == n && q.gameObject.scene.IsValid());
        if (x == null || !x.gameObject.activeInHierarchy) return $" | pies {n}=off";
        var smr = x.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(s => s.name.StartsWith("Body"));
        if (smr == null) return $" | pies {n}=?";
        if (mallaPies == null) mallaPies = new Mesh();
        smr.BakeMesh(mallaPies, false);   // false + TRS sin escala = tamaño real (la jerarquía trae escalas raras: CITY ×100, malla ×0.01)
        var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        float minY = float.MaxValue, vpMin = float.MaxValue, vpMax = float.MinValue; bool dentro = false;
        foreach (var v in mallaPies.vertices)
        {
            var w = m.MultiplyPoint3x4(v);
            if (w.y < minY) minY = w.y;
            if (cam == null) continue;
            var vp = cam.WorldToViewportPoint(w);
            if (vp.z <= 0 || vp.x < 0 || vp.x > 1) continue;
            dentro = true; vpMin = Mathf.Min(vpMin, vp.y); vpMax = Mathf.Max(vpMax, vp.y);
        }
        return $" | pies {n}: suela={minY:+0.00;-0.00} vp={(dentro ? $"{vpMin:0.00}..{vpMax:0.00}" : "fuera")}";
    }
}
