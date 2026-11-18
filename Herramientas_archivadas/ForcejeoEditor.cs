using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Solo: forcejeo más fuerte (59.6-62 s)
/// Después del agarre (CM8_8_CHOQUE, primer plano de las manos) el forcejeo del saludo de Blender era sutil (±0.4 u).
///  - Empujones: los DOS cuerpos se mueven juntos (ALE_RAIZ y MEMO_PERFIL_RAIZ, curvas X/Z de "Ale · recorrido" y
///    "MC · recorrido") sobre la línea entre ellos: empuja Ale, responde Memi, empuja Ale, regresan. Como se mueven
///    igual, las manos nunca se separan. Regresa a 0 antes del corte a CM8_9_FINAL (62.0 s, ahí se ven los pies).
///  - Temblor de esfuerzo: vibración rápida y chica encima, más fuerte en los empujones.
///  - Cámara en mano: Perlin (6D Shake) en CM8_8_CHOQUE con pulsos sincronizados (pista "Forcejeo · temblor cámara").
/// Se puede volver a correr: las curvas de recorrido no tienen llaves entre 41.5 y 45 s de la ciudad (están parados),
/// así que la herramienta borra las llaves de la ventana y las vuelve a escribir.
/// Orden: Escena 8 -> Solo: pies al piso -> esta (pies al piso solo toca Y; esta solo X/Z).
/// </summary>
public static class ForcejeoEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string RutaClipCamara = "Assets/Animaciones/Saludo/Forcejeo_TemblorCamara.anim";
    const float InicioCiudad = 17.5f;
    const float Ini = 59.6f, Fin = 62.0f;            // principal (CM8_8_CHOQUE)
    const float Margen = 0.35f;                      // llaves de 0 antes/después para no deformar la curva
    const float Fundido = 0.15f;                     // el temblor de esfuerzo entra/sale suave
    const float Fps = 60f;

    // empujón sobre la línea entre ellos (u de mundo; + = hacia Memi, o sea Ale empuja). Segundos desde Ini.
    static readonly (float t, float v)[] Empuje =
    {
        (0f, 0f), (0.3f, 0.45f), (0.55f, 0.3f),      // empuja Ale
        (0.9f, -0.5f), (1.15f, -0.35f),               // responde Memi
        (1.5f, 0.55f), (1.75f, 0.4f),                 // empuja Ale más fuerte
        (2.05f, -0.25f), (2.4f, 0f)                   // se acomodan
    };
    // bamboleo lateral (u de mundo)
    static readonly (float t, float v)[] Lateral = { (0f, 0f), (0.6f, 0.12f), (1.2f, -0.1f), (1.8f, 0.1f), (2.4f, 0f) };
    const float TemblorEsfuerzo = 0.05f;             // u de mundo
    // cámara en mano (ganancia sobre 6D Shake; 48 ≈ ±3-6° con frecuencia 0.55)
    static readonly (float t, float v)[] Camara =
    {
        (0f, 0f), (0.05f, 0f), (0.3f, 7f), (0.55f, 3f), (0.9f, 8f), (1.15f, 3f),
        (1.5f, 9f), (1.75f, 4f), (2.05f, 6f), (2.32f, 1.5f), (2.4f, 0f)
    };
    const float FrecuenciaCamara = 1.2f;

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Solo: forcejeo más fuerte (59.6-62 s)")]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Forcejeo] Sal de Play primero."); return; }
        var log = new StringBuilder();
        if (Aplicar(log)) { AssetDatabase.SaveAssets(); Debug.Log("[Forcejeo] Listo.\n" + log); }
        else Debug.LogError("[Forcejeo] No se pudo.\n" + log);
    }

    static AnimationCurve Curva((float t, float v)[] k)
    {
        var c = new AnimationCurve(k.Select(x => new Keyframe(x.t, x.v)).ToArray());
        for (int i = 0; i < c.length; i++) { AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto); }
        return c;
    }

    public static bool Aplicar(StringBuilder log)
    {
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dir = tl != null ? Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.playableAsset == tl) : null;
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var city = todos.FirstOrDefault(x => x.name == "CITY");
        AnimationTrack Rec(string pref) => tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(x => !(x.parent is AnimationTrack) && x.name.StartsWith(pref) && x.name.Contains("recorrido"));
        var recAle = tl != null ? Rec("Ale") : null; var recMemi = tl != null ? Rec("MC") : null;
        if (dir == null || city == null || recAle?.infiniteClip == null || recMemi?.infiniteClip == null) { log.AppendLine("Falta el Timeline de la ciudad, CITY o las pistas de recorrido."); return false; }

        // dirección de Ale hacia Memi (mundo), a partir de sus recorridos en ese momento
        float local = (Ini + Fin) * 0.5f - InicioCiudad;
        Vector3 Pos(AnimationTrack r)
        {
            float E(string p) { var cu = AnimationUtility.GetEditorCurve(r.infiniteClip, EditorCurveBinding.FloatCurve("", typeof(Transform), p)); return cu != null ? cu.Evaluate(local) : 0f; }
            return city.TransformPoint(r.infiniteClipOffsetPosition + Quaternion.Euler(r.infiniteClipOffsetEulerAngles) * new Vector3(E("m_LocalPosition.x"), E("m_LocalPosition.y"), E("m_LocalPosition.z")));
        }
        var eje = Pos(recMemi) - Pos(recAle); eje.y = 0; eje.Normalize();
        var lado = Vector3.Cross(Vector3.up, eje);

        var cEmp = Curva(Empuje); var cLat = Curva(Lateral);
        float dur = Fin - Ini, a = Ini - InicioCiudad, b = Fin - InicioCiudad;
        float maxEmp = Empuje.Max(x => Mathf.Abs(x.v));
        Vector3 Delta(float s)
        {
            if (s <= 0f || s >= dur) return Vector3.zero;
            float env = (0.4f + 0.6f * Mathf.Abs(cEmp.Evaluate(s)) / maxEmp) * Mathf.SmoothStep(0, 1, s / Fundido) * Mathf.SmoothStep(0, 1, (dur - s) / Fundido);
            float tr = env * TemblorEsfuerzo * (Mathf.Sin(2 * Mathf.PI * 13f * s) + 0.6f * Mathf.Sin(2 * Mathf.PI * 19f * s + 1f));
            float trL = env * TemblorEsfuerzo * 0.6f * Mathf.Sin(2 * Mathf.PI * 16f * s + 2f);
            return eje * (cEmp.Evaluate(s) + tr) + lado * (cLat.Evaluate(s) + trL);
        }

        Undo.RecordObject(recAle.infiniteClip, "Forcejeo"); Undo.RecordObject(recMemi.infiniteClip, "Forcejeo");
        foreach (var rec in new[] { recAle, recMemi })
        {
            var clip = rec.infiniteClip;
            var aCurva = Quaternion.Inverse(Quaternion.Euler(rec.infiniteClipOffsetEulerAngles)) * Quaternion.Inverse(city.rotation);
            float escala = city.lossyScale.x;
            foreach (var (prop, comp) in new[] { ("m_LocalPosition.x", 0), ("m_LocalPosition.z", 2) })
            {
                var bind = EditorCurveBinding.FloatCurve("", typeof(Transform), prop);
                var cu = AnimationUtility.GetEditorCurve(clip, bind);
                if (cu == null) { log.AppendLine($"{rec.name}: no tiene {prop}."); return false; }
                // quitar lo que esta herramienta escribió antes (la ventana no tiene llaves propias)
                var base0 = cu.keys.Where(k => k.time < a - Margen - 0.001f || k.time > b + Margen + 0.001f).ToList();
                var baseCurva = new AnimationCurve(base0.ToArray());   // Keyframe conserva sus tangentes
                var nuevas = base0.ToList();
                int n = Mathf.CeilToInt((b - a + 2 * Margen) * Fps);
                for (int i = 0; i <= n; i++)
                {
                    float t = a - Margen + i / Fps; if (t > b + Margen) t = b + Margen;
                    var d = aCurva * (Delta(t - a) / escala);
                    nuevas.Add(new Keyframe(t, baseCurva.Evaluate(t) + d[comp]));
                }
                var nueva = new AnimationCurve(nuevas.OrderBy(k => k.time).ToArray());
                for (int i = 0; i < nueva.length; i++)
                {
                    float t = nueva.keys[i].time;
                    if (t >= a - Margen - 0.001f && t <= b + Margen + 0.001f)
                    { AnimationUtility.SetKeyLeftTangentMode(nueva, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(nueva, i, AnimationUtility.TangentMode.ClampedAuto); }
                }
                AnimationUtility.SetEditorCurve(clip, bind, nueva);
            }
            EditorUtility.SetDirty(clip);
        }
        log.AppendLine($"Empujones de {Ini} a {Fin} s sobre el eje Ale->Memi {eje} (máx ±{maxEmp} u), bamboleo lateral ±0.12 u y temblor de esfuerzo {TemblorEsfuerzo} u.");

        // ---------- cámara en mano en CM8_8
        var camT = todos.FirstOrDefault(x => x.name == "CM8_8_CHOQUE");
        if (camT != null)
        {
            var perlin = camT.GetComponent<CinemachineBasicMultiChannelPerlin>();
            if (perlin == null) perlin = Undo.AddComponent<CinemachineBasicMultiChannelPerlin>(camT.gameObject);
            Undo.RecordObject(perlin, "Forcejeo");
            perlin.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>("Packages/com.unity.cinemachine/Presets/Noise/6D Shake.asset");
            perlin.AmplitudeGain = 0f; perlin.FrequencyGain = FrecuenciaCamara;
            EditorUtility.SetDirty(perlin);
            var an = camT.GetComponent<Animator>();
            if (an == null) an = Undo.AddComponent<Animator>(camT.gameObject);
            var clipC = AssetDatabase.LoadAssetAtPath<AnimationClip>(RutaClipCamara);
            if (clipC == null) { clipC = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clipC, RutaClipCamara); }
            clipC.ClearCurves();
            var cg = new AnimationCurve(Camara.Select(x => new Keyframe(x.t, x.v)).ToArray());
            for (int i = 0; i < cg.length; i++) { AnimationUtility.SetKeyLeftTangentMode(cg, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(cg, i, AnimationUtility.TangentMode.Linear); }
            AnimationUtility.SetEditorCurve(clipC, EditorCurveBinding.FloatCurve("", typeof(CinemachineBasicMultiChannelPerlin), "AmplitudeGain"), cg);
            EditorUtility.SetDirty(clipC);

            Undo.RecordObject(tl, "Forcejeo");
            const string nombre = "Forcejeo · temblor cámara";
            foreach (var vieja in tl.GetOutputTracks().Where(x => x.name == nombre).ToList()) tl.DeleteTrack(vieja);
            var grupo = tl.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "8 · Saludo");
            var pista = tl.CreateTrack<AnimationTrack>(grupo, nombre);
            var tc = pista.CreateClip(clipC); tc.start = a; tc.duration = dur; tc.displayName = "Forcejeo_TemblorCamara";
            if (tc.asset is AnimationPlayableAsset ap) ap.removeStartOffset = false;
            pista.trackOffset = TrackOffset.ApplySceneOffsets;
            dir.SetGenericBinding(pista, an);
            EditorUtility.SetDirty(pista);
            log.AppendLine($"Cámara en mano en CM8_8_CHOQUE (6D Shake, frecuencia {FrecuenciaCamara}): pulsos de hasta {Camara.Max(x => x.v)} con cada empujón.");
        }
        else log.AppendLine("No encontré CM8_8_CHOQUE: sin temblor de cámara.");

        EditorUtility.SetDirty(tl);
        EditorSceneManager.MarkSceneDirty(dir.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return true;
    }
}
