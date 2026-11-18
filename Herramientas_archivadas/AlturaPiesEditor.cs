using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// Tools > Amistad > 3 · Encuentro, saludo y final > Solo: pies al piso (mide en Play y corrige alturas)
///
/// Problema: con las "piernas de pie" (48.3 s en adelante) Ale quedaba ~1.8 u hundido y Memi ~2.8 u; caminando, Memi iba
/// 0.5-1.2 u hundida. Se veía en las tomas donde salen los pies (CM7, CM8_1, CM8_4, plano final).
///
/// Qué hace:
///  0) Si las curvas Y de recorrido son las que escribió esta herramienta la última vez, primero las regresa a la base
///     (la de la Escena 8), guardada en PiesAlPiso_curvas.json. Si alguien las rehízo (Escena 8), la actual es la nueva base.
///     Así se puede correr las veces que sea sin que se acumule.
///  1) Entra a Play con "Medir pies y manos" (MedirPiesEditor): suela de cada uno cada 0.1 s (vértice más bajo; piso en y=0).
///  2) Al volver a Edit, corrige la curva Y de "Ale · recorrido" y "MC · recorrido" (mueven ALE_RAIZ / MEMO_PERFIL_RAIZ):
///     - Tomas donde se ven los pies: la suela queda en el piso (promedio de 3 muestras dentro de la misma toma,
///       sin mezclar a través de un corte). Incluye la contrapicada del impulso (CM8_4), cuadro por cuadro.
///     - SIN TOCAR (altura original): las caras CM8_2 y CM8_3 (sus cámaras del previz apuntan a la cara original; no se ven
///       los pies) y las tomas de las manos CM8_5 a CM8_8 (así no se mueve el agarre; con los dos en el piso la palma de
///       Memi quedaría ~0.6 u más alta que la de Ale). El escalón de altura cae en los cortes (invisible).
///  3) Encuadres (valores fijos): CM5_ALE_CAMINA inclinada CM5Inclinacion° hacia abajo (los pies de Ale tocaban el borde)
///     y CM8_1_FRENTE con FOV CM8_1Fov (los pies de Memi quedaban cortados en el borde).
///  OJO: la Escena 8 rehace las curvas desde 49 s y el FOV de CM8_1, y "Caminata Ale" rehace CM5: correr esto después.
[InitializeOnLoad]
public static class AlturaPiesEditor
{
    const string ClaveAplicar = "Amistad_PiesAplicar";
    const string Csv = "Logs/pies_manos.csv";
    const string ArchivoCurvas = "Assets/Animaciones/Saludo/PiesAlPiso_curvas.json";
    const float InicioCiudad = 17.5f;
    const float CM5Inclinacion = 2.5f;   // antes -1 (miraba un poco hacia arriba)
    const float CM8_1Fov = 42f;          // antes 32.3 (del previz)
    // tramos SIN TOCAR: [inicio de la 1a toma, inicio de la 2a toma)
    static readonly (string desde, string hasta)[] SinTocar = { ("CM8_2", "CM8_4"), ("CM8_5", "CM8_9") };

    static AlturaPiesEditor()
    {
        EditorApplication.playModeStateChanged -= Cambio;
        EditorApplication.playModeStateChanged += Cambio;
    }

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Solo: pies al piso (mide en Play y corrige alturas)", false, 315)]
    public static void Iniciar()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Pies al piso] Sal de Play primero."); return; }
        if (!Pistas(out var pAle, out var pMemi, out var tlC)) return;
        Debug.Log("[Pies al piso] " + RegresarABase(pAle, pMemi, tlC));
        if (File.Exists(Csv)) File.Delete(Csv);
        SessionState.SetString("Amistad_PiesRango", "17.5,67.5,0.1");
        SessionState.SetBool(ClaveAplicar, true);
        MedirPiesEditor.Iniciar();
    }

    static void Cambio(PlayModeStateChange s)
    {
        if (s != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(ClaveAplicar, false)) return;
        SessionState.SetBool(ClaveAplicar, false);
        Aplicar();   // directo: un delayCall a veces no corre si el editor está en segundo plano
    }

    // ------------------------------------------------------------------ curvas base / escritas (JSON)
    [System.Serializable] class Clave { public float t, v, i, o; public int m; }
    [System.Serializable] class Curva { public string pista; public List<Clave> baseY = new List<Clave>(), escritaY = new List<Clave>(); }
    [System.Serializable] class Guardado { public List<Curva> curvas = new List<Curva>(); }

    static List<Clave> ACl(AnimationCurve c) => c.keys.Select(k => new Clave { t = k.time, v = k.value, i = k.inTangent, o = k.outTangent, m = (int)k.weightedMode }).ToList();
    static AnimationCurve DeCl(List<Clave> l) => new AnimationCurve(l.Select(k => new Keyframe(k.t, k.v, k.i, k.o) { weightedMode = (WeightedMode)k.m }).ToArray());
    static bool Igual(AnimationCurve c, List<Clave> l) =>
        c != null && l != null && c.length == l.Count && c.keys.Select((k, j) => Mathf.Abs(k.time - l[j].t) < 1e-4f && Mathf.Abs(k.value - l[j].v) < 1e-6f).All(x => x);
    static Guardado Leer() => File.Exists(ArchivoCurvas) ? JsonUtility.FromJson<Guardado>(File.ReadAllText(ArchivoCurvas)) : new Guardado();
    static EditorCurveBinding BY => EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y");

    static string RegresarABase(AnimationTrack pAle, AnimationTrack pMemi, TimelineAsset tlC)
    {
        var g = Leer(); var sb = new StringBuilder();
        foreach (var p in new[] { pAle, pMemi })
        {
            var c = g.curvas.FirstOrDefault(x => x.pista == p.name);
            var actual = AnimationUtility.GetEditorCurve(p.infiniteClip, BY);
            if (c != null && Igual(actual, c.escritaY))
            {
                Undo.RecordObject(p.infiniteClip, "Pies al piso");
                AnimationUtility.SetEditorCurve(p.infiniteClip, BY, DeCl(c.baseY));
                EditorUtility.SetDirty(p.infiniteClip);
                sb.Append($"'{p.name}': regresada a su base ({c.baseY.Count} claves). ");
            }
            else sb.Append($"'{p.name}': la curva actual es la base. ");
        }
        EditorUtility.SetDirty(tlC); AssetDatabase.SaveAssets();
        return sb.ToString();
    }

    static bool Pistas(out AnimationTrack pAle, out AnimationTrack pMemi, out TimelineAsset tlC)
    {
        pAle = pMemi = null; tlC = null;
        var dirC = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.name == "Director_P2_Ciudad");
        if (dirC == null) { Debug.LogError("[Pies al piso] Falta Director_P2_Ciudad."); return false; }
        tlC = (TimelineAsset)dirC.playableAsset;
        var pistas = tlC.GetOutputTracks().OfType<AnimationTrack>().Where(t => !(t.parent is AnimationTrack) && t.name.Contains("recorrido")).ToList();
        pAle = pistas.FirstOrDefault(t => t.name.StartsWith("Ale"));
        pMemi = pistas.FirstOrDefault(t => t.name.StartsWith("MC"));
        if (pAle?.infiniteClip == null || pMemi?.infiniteClip == null) { Debug.LogError("[Pies al piso] No encontré 'Ale · recorrido' o 'MC · recorrido'."); return false; }
        return true;
    }

    // ------------------------------------------------------------------ aplicar
    struct Muestra { public float t; public string vcam; public float? ale, memi; }

    [MenuItem("Tools/Amistad/Revisión y pruebas/Pies al piso: aplicar última medición", false, 902)]
    public static void Aplicar()
    {
        if (!File.Exists(Csv)) { Debug.LogError("[Pies al piso] No hay medición (" + Csv + "). Corre 'Solo: pies al piso'."); return; }
        var ci = CultureInfo.InvariantCulture;
        float? F(string x) => string.IsNullOrEmpty(x) ? (float?)null : float.Parse(x, ci);
        var ms = File.ReadAllLines(Csv).Skip(1).Where(l => l.Trim().Length > 0).Select(l => l.Split(','))
            .Select(c => new Muestra { t = float.Parse(c[0], ci), vcam = c[1], ale = F(c[2]), memi = F(c[3]) }).ToList();

        var dirP = GameObject.Find("Director_P1_Bosque")?.GetComponent<PlayableDirector>();
        var city = GameObject.Find("CITY")?.transform;
        if (dirP == null || city == null) { Debug.LogError("[Pies al piso] Falta Director_P1_Bosque o CITY."); return; }
        if (!Pistas(out var pAle, out var pMemi, out var tlC)) return;

        var cortes = ((TimelineAsset)dirP.playableAsset).GetOutputTracks().Where(t => t.name.Contains("Cortes")).SelectMany(t => t.GetClips()).ToList();
        float Corte(string n) { var c = cortes.FirstOrDefault(x => x.displayName.StartsWith(n)); return c != null ? (float)c.start : -1f; }
        var tramos = SinTocar.Select(s => (a: Corte(s.desde), b: Corte(s.hasta))).ToList();
        if (tramos.Any(x => x.a < 0 || x.b < 0)) { Debug.LogError("[Pies al piso] No encontré los cortes CM8_2 / CM8_4 / CM8_5 / CM8_9."); return; }

        var log = new StringBuilder("[Pies al piso] Sin tocar: " + string.Join(", ", tramos.Select(x => $"{x.a:F2}-{x.b:F2} s")) + "\n");
        Undo.RecordObjects(new Object[] { pAle.infiniteClip, pMemi.infiniteClip }, "Pies al piso");
        var g = Leer();
        foreach (var (pista, suela, quien) in new[] { (pAle, (System.Func<Muestra, float?>)(m => m.ale), "Ale"), (pMemi, (System.Func<Muestra, float?>)(m => m.memi), "Memi") })
        {
            var baseY = AnimationUtility.GetEditorCurve(pista.infiniteClip, BY) ?? new AnimationCurve(new Keyframe(0f, 0f));
            var nueva = Corregir(pista, city, baseY, ms, suela, tramos, log, quien);
            var c = g.curvas.FirstOrDefault(x => x.pista == pista.name);
            if (c == null) { c = new Curva { pista = pista.name }; g.curvas.Add(c); }
            c.baseY = ACl(baseY); c.escritaY = ACl(nueva);
        }
        File.WriteAllText(ArchivoCurvas, JsonUtility.ToJson(g, true));
        AssetDatabase.ImportAsset(ArchivoCurvas);
        EditorUtility.SetDirty(tlC);
        AssetDatabase.SaveAssets();
        log.Append(Encuadres());
        File.Copy(Csv, "Logs/pies_manos_aplicado.csv", true); File.Delete(Csv);   // para no aplicar dos veces la misma medición
        Debug.Log(log.ToString());
    }

    [MenuItem("Tools/Amistad/Revisión y pruebas/Pies al piso: solo encuadres (CM5 y CM8_1)", false, 903)]
    static void EncuadresMenu() => Debug.Log("[Pies al piso] " + Encuadres());

    public static string Encuadres()
    {
        var sb = new StringBuilder();
        Transform T(string n) => Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(q => q.name == n && q.gameObject.scene.IsValid());
        var cm5 = T("CM5_ALE_CAMINA");
        if (cm5 != null)
        {
            Undo.RecordObject(cm5, "Encuadres");
            var e = cm5.eulerAngles; float antes = Mathf.DeltaAngle(0, e.x);
            cm5.rotation = Quaternion.Euler(CM5Inclinacion, e.y, 0f);
            EditorUtility.SetDirty(cm5);
            sb.AppendLine($"CM5_ALE_CAMINA: inclinación {antes:0.0}° -> {CM5Inclinacion:0.0}° (hacia abajo)");
        }
        else sb.AppendLine("No encontré CM5_ALE_CAMINA.");
        var cm81 = T("CM8_1_FRENTE")?.GetComponent<Unity.Cinemachine.CinemachineCamera>();
        if (cm81 != null)
        {
            Undo.RecordObject(cm81, "Encuadres");
            var lens = cm81.Lens; float antes = lens.FieldOfView; lens.FieldOfView = CM8_1Fov; cm81.Lens = lens;
            EditorUtility.SetDirty(cm81);
            sb.AppendLine($"CM8_1_FRENTE: FOV {antes:0.0} -> {CM8_1Fov:0.0}");
        }
        else sb.AppendLine("No encontré CM8_1_FRENTE.");
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        return sb.ToString();
    }

    static AnimationCurve Corregir(AnimationTrack pista, Transform city, AnimationCurve viejo, List<Muestra> ms, System.Func<Muestra, float?> suela,
                                   List<(float a, float b)> tramos, StringBuilder log, string quien)
    {
        // mundo por unidad de Y local (CITY ×100 y el giro del offset de la pista)
        float k = city.TransformVector(Quaternion.Euler(pista.infiniteClipOffsetEulerAngles) * Vector3.up).y;
        int n = ms.Count;
        bool Libre(float t) => !tramos.Any(x => t >= x.a - 1e-3f && t < x.b - 1e-3f);

        // corrección en mundo = -suela, promedio de 3 dentro de la misma toma; 0 en los tramos sin tocar
        var corr = new float[n];
        for (int i = 0; i < n; i++)
        {
            if (suela(ms[i]) == null || !Libre(ms[i].t)) continue;
            float s = 0; int c = 0;
            for (int j = i - 1; j <= i + 1; j++)
                if (j >= 0 && j < n && suela(ms[j]) != null && ms[j].vcam == ms[i].vcam) { s += suela(ms[j]).Value; c++; }
            corr[i] = -s / c;
        }

        // cortes donde cambia el modo (escalón)
        var limites = tramos.SelectMany(x => new[] { x.a, x.b }).OrderBy(x => x).ToList();
        var keys = new List<Keyframe>();
        float desde = ms.First().t - InicioCiudad, hasta = ms.Last().t - InicioCiudad;
        foreach (var kf in viejo.keys) if (kf.time < desde - 1e-3f || kf.time > hasta + 1e-3f) keys.Add(kf);
        var escalones = new List<float>();
        float maxC = 0, sumC = 0; int cnt = 0;
        for (int i = 0; i < n; i++)
        {
            float tc = ms[i].t - InicioCiudad;
            foreach (var cut in limites)
                if (i > 0 && ms[i - 1].t < cut - 1e-3f && ms[i].t >= cut - 1e-3f)
                {
                    float tPrev = cut - InicioCiudad - 1f / 60f;
                    keys.Add(new Keyframe(tPrev, viejo.Evaluate(tPrev) + corr[i - 1] / k));
                    escalones.Add(tPrev);
                    if (Mathf.Abs(ms[i].t - cut) > 1e-3f)
                    {
                        float tCut = cut - InicioCiudad;
                        keys.Add(new Keyframe(tCut, viejo.Evaluate(tCut) + corr[i] / k));
                    }
                }
            keys.Add(new Keyframe(tc, viejo.Evaluate(tc) + corr[i] / k));
            if (corr[i] != 0) { maxC = Mathf.Max(maxC, Mathf.Abs(corr[i])); sumC += corr[i]; cnt++; }
        }
        keys.Sort((a, b) => a.time.CompareTo(b.time));
        var curva = new AnimationCurve(keys.ToArray());
        for (int i = 0; i < curva.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(curva, i, AnimationUtility.TangentMode.Linear);
            AnimationUtility.SetKeyRightTangentMode(curva, i, AnimationUtility.TangentMode.Linear);
        }
        foreach (var te in escalones)
        {
            int idx = System.Array.FindIndex(curva.keys, q => Mathf.Abs(q.time - te) < 1e-5f);
            if (idx >= 0) AnimationUtility.SetKeyRightTangentMode(curva, idx, AnimationUtility.TangentMode.Constant);
        }
        AnimationUtility.SetEditorCurve(pista.infiniteClip, BY, curva);
        EditorUtility.SetDirty(pista.infiniteClip);
        log.AppendLine($"{quien}: {curva.length} claves; corrección media {(cnt > 0 ? sumC / cnt : 0):+0.00;-0.00} u, máx {maxC:0.00} u");
        return curva;
    }
}
