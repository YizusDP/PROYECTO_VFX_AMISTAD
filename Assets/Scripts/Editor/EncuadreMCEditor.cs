using System.Linq;
using System.Text;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > Encuadre de MC (derrumbe)
/// Toma frontal de MC caminando hacia la cámara (como el storyboard), con el mismo movimiento de dolly que ya tenía
/// (recto hacia atrás, de 15 a 23 s con entrada y salida suaves). Solo cambia:
///  - la altura: la cámara va un poco arriba de la cabeza de MC, así el edificio de atrás queda por encima de él
///    en vez de taparse con su cuerpo;
///  - el ángulo: un Rotation Composer mantiene a MC centrado, con la cabeza un poco arriba del centro.
/// Se puede volver a correr.
/// </summary>
public static class EncuadreMCEditor
{
    // ---- Ajustes ----
    const float AlturaCamara = 19f;        // mundo (MC mide ~17; antes iba a 12.85)
    const float LargoRecorrido = 34.88f;   // lo que retrocede la cámara (igual que antes)
    const float InicioDolly = 15f, FinDolly = 23f;   // tiempo del Timeline maestro (igual que antes)
    const float FOV = 60f;
    static readonly Vector2 PosicionCabeza = new Vector2(0f, -0.05f); // Cinemachine: 0 = centro, ±0.5 = orilla, y negativa = arriba

    [MenuItem("Tools/Amistad/Encuadre de MC (derrumbe)")]
    static void Aplicar()
    {
        var escena = SceneManager.GetActiveScene();
        var todos = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var cm3 = T("CM3_MEMO"); var splineT = T("Spline_P2_Ciudad"); var memo = T("MEMO_PERFIL");
        var p1 = T("Director_P1_Bosque")?.GetComponent<PlayableDirector>();
        var p2 = T("Director_P2_Ciudad")?.GetComponent<PlayableDirector>();
        if (cm3 == null || splineT == null || memo == null || p1 == null)
        { EditorUtility.DisplayDialog("Encuadre", "Falta CM3_MEMO, Spline_P2_Ciudad, MEMO_PERFIL o Director_P1_Bosque.", "OK"); return; }
        var cam = cm3.GetComponent<CinemachineCamera>();
        var dolly = cm3.GetComponent<CinemachineSplineDolly>();
        var contenedor = splineT.GetComponent<SplineContainer>();
        var animCam = cm3.GetComponent<Animator>();
        if (cam == null || dolly == null || contenedor == null) { EditorUtility.DisplayDialog("Encuadre", "CM3_MEMO necesita CinemachineCamera y Spline Dolly.", "OK"); return; }
        var log = new StringBuilder();

        // ---------- 1. Objetivo: la cabeza de MC ----------
        float alto = 17f;
        var rs = memo.GetComponentsInChildren<Renderer>();
        if (rs.Length > 0) { var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds); alto = bb.size.y; }
        var mira = memo.Find("MIRA_CM3");
        if (mira == null)
        {
            var go = new GameObject("MIRA_CM3");
            Undo.RegisterCreatedObjectUndo(go, "Encuadre MC");
            go.transform.SetParent(memo, false);
            mira = go.transform;
        }
        Undo.RecordObject(mira, "Encuadre MC");
        mira.localPosition = Vector3.zero;
        mira.position = memo.position + Vector3.up * (alto * 0.91f);

        // ---------- 2. Ruta: la misma recta de antes (frente a MC, retrocediendo), solo más alta ----------
        double inicioP2 = 17.5;
        var tlP1 = p1.playableAsset as TimelineAsset;
        foreach (var tr in tlP1.GetOutputTracks().OfType<ControlTrack>())
            foreach (var c in tr.GetClips())
                if (c.asset is ControlPlayableAsset a && a.sourceGameObject.Resolve(p1) is GameObject fuente && fuente.name == "Director_P2_Ciudad") inicioP2 = c.start - c.clipIn;
        // Punto de partida en planta: el mismo de antes (el origen del spline), frente a MC.
        // La dirección sale de dónde empieza MC según su animación (no de dónde esté ahora, por si el Preview está activo).
        var origen = splineT.position;
        var inicioMC = memo.position;
        var tlP2 = p2 != null ? p2.playableAsset as TimelineAsset : null;
        var pistaMemo = tlP2 != null ? tlP2.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => p2.GetGenericBinding(t) is Animator am && am.transform == memo) : null;
        if (pistaMemo != null && memo.parent != null) inicioMC = memo.parent.TransformPoint(pistaMemo.infiniteClipOffsetPosition);
        var hacia = origen - inicioMC; hacia.y = 0f; hacia.Normalize();          // de MC hacia la cámara (la cámara retrocede en esa dirección)
        log.AppendLine($"MC empieza en {inicioMC}; la cámara retrocede hacia {hacia}");
        var p0 = new Vector3(origen.x, AlturaCamara, origen.z);
        var pf = p0 + hacia * LargoRecorrido;
        Undo.RecordObject(contenedor, "Encuadre MC");
        var spline = contenedor.Spline;
        spline.Clear();
        spline.Add(new BezierKnot((float3)splineT.InverseTransformPoint(p0)), TangentMode.Linear);
        spline.Add(new BezierKnot((float3)splineT.InverseTransformPoint(pf)), TangentMode.Linear);
        EditorUtility.SetDirty(contenedor);
        log.AppendLine($"Ruta: de {p0} a {pf} (retrocede {LargoRecorrido} u frente a MC, altura {AlturaCamara})");

        // ---------- 3. Cámara: dolly normalizado, composer centrado en la cabeza de MC ----------
        Undo.RecordObject(dolly, "Encuadre MC");
        var so = new SerializedObject(dolly);
        so.FindProperty("m_SplineSettings.Spline").objectReferenceValue = contenedor;
        so.FindProperty("m_SplineSettings.Units").intValue = (int)PathIndexUnit.Normalized;
        so.FindProperty("m_SplineSettings.Position").floatValue = 0f;
        so.FindProperty("CameraRotation").intValue = 0;
        so.ApplyModifiedProperties();

        Undo.RecordObject(cam, "Encuadre MC");
        cam.Target.TrackingTarget = mira;
        cam.Target.CustomLookAtTarget = false;
        cam.Lens.FieldOfView = FOV;
        EditorUtility.SetDirty(cam);

        var composer = cm3.GetComponent<CinemachineRotationComposer>();
        if (composer == null) composer = Undo.AddComponent<CinemachineRotationComposer>(cm3.gameObject);
        Undo.RecordObject(composer, "Encuadre MC");
        composer.Composition.ScreenPosition = PosicionCabeza;
        composer.Composition.DeadZone.Enabled = false;
        composer.Composition.HardLimits.Enabled = false;
        composer.Damping = Vector2.zero;
        composer.Lookahead.Enabled = false;
        composer.CenterOnActivate = true;
        composer.TargetOffset = Vector3.zero;
        EditorUtility.SetDirty(composer);

        // ---------- 4. Dolly: la curva original (suave de 15 a 23 s), igual en los dos Timelines ----------
        // p(t) = smoothstep((t - 15) / 8). Se escribe con llaves cada 0.5 s para que P1 y P2 coincidan exacto.
        float P(float t) { float u = Mathf.Clamp01((t - InicioDolly) / (FinDolly - InicioDolly)); return u * u * (3f - 2f * u); }
        float dP(float t) { float u = (t - InicioDolly) / (FinDolly - InicioDolly); return (u <= 0f || u >= 1f) ? 0f : 6f * u * (1f - u) / (FinDolly - InicioDolly); }
        int curvas = 0;
        void Poner(TimelineAsset tl, PlayableDirector dir, float desfase)
        {
            if (tl == null || dir == null) return;
            foreach (var pista in tl.GetOutputTracks().OfType<AnimationTrack>())
            {
                if (!(dir.GetGenericBinding(pista) is Animator an) || an != animCam || pista.infiniteClip == null) continue;
                var clip = pista.infiniteClip;
                Undo.RecordObject(clip, "Encuadre MC");
                var curva = new AnimationCurve();
                float tIni = Mathf.Max(0f, InicioDolly - desfase), tFin = FinDolly - desfase;
                if (tIni > 0f) curva.AddKey(new Keyframe(0f, 0f, 0f, 0f));
                for (float tl0 = tIni; tl0 <= tFin + 1e-3f; tl0 += 0.5f)
                {
                    float tg = tl0 + desfase;
                    curva.AddKey(new Keyframe(tl0, P(tg), dP(tg), dP(tg)));
                }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(CinemachineSplineDolly), "m_SplineSettings.Position"), curva);
                // Las unidades (enum) no se animan: se quedan en Normalized desde el componente.
                foreach (var b in AnimationUtility.GetCurveBindings(clip).Where(b => b.propertyName == "m_SplineSettings.Units").ToList())
                    AnimationUtility.SetEditorCurve(clip, b, null);
                EditorUtility.SetDirty(clip);
                EditorUtility.SetDirty(tl);
                curvas++;
                log.AppendLine($"  dolly en {tl.name}/{pista.name}: 0 → 1 de {InicioDolly - desfase:0.0} a {FinDolly - desfase:0.0} s (tiempo local)");
            }
        }
        Poner(tlP1, p1, 0f);
        Poner(tlP2, p2, (float)inicioP2);
        AssetDatabase.SaveAssets();
        p1.RebuildGraph();
        if (p2 != null) p2.RebuildGraph();

        EditorSceneManager.MarkSceneDirty(escena);
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/encuadre_mc.txt"), log.ToString());
        Debug.Log($"[Encuadre MC] listo: toma frontal a {AlturaCamara} u de altura, {curvas} curvas de dolly. Detalles en Logs/encuadre_mc.txt");
    }
}
