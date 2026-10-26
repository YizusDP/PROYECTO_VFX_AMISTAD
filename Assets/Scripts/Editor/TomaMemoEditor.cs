using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;
using UnityEngine.UI;

/// <summary>
/// Tools > Amistad > CM3_MEMO: recorrer toma y negro al pasar Memi
/// Arma la toma de la ciudad (17.5 a 23 s del Timeline principal):
/// 1) Memi (pista "MC · recorrido") y la cámara van en la misma línea X (MemiX), para que Godzilla quede libre a un lado del edificio.
/// 2) CM3_MEMO viaja hacia atrás a la par de Memi (la mantiene a DistanciaMemi), así Memi se ve igual mientras al fondo
///    se va descubriendo Godzilla y el derrumbe. En TiempoAlto la cámara frena y se queda; Memi sigue caminando hacia ella.
///    TiempoAlto se calcula solo para que el negro termine en FinNegroObjetivo.
/// 3) Cuando la cámara entra en el cuerpo de Memi, FX_PasoNegro (Canvas negro) cubre la pantalla.
/// 4) A mitad del negro se corta a CM3_GODZILLA (misma posición, lente igual) que sigue la cabeza de Godzilla hasta el final.
///    El corte va en la pista de cámaras del Timeline principal.
/// Se puede volver a correr: todos los valores son absolutos.
/// </summary>
public static class TomaMemoEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string ClipNegroPath = "Assets/Animaciones/CM3_PasoNegro.anim";
    const string NombrePista = "Negro · paso de Memi";
    const float InicioCiudad = 17.5f;     // tiempo del principal donde empieza la ciudad
    const float DuracionCiudad = 5.5f;    // 17.5 -> 23

    // Línea por donde camina Memi (X local de CITY). +X = derecha en la toma.
    const float MemiX = -0.3672f;
    // Altura de la cámara (Y local del spline, 1 = 1 u).
    const float CamY = 0.5f;
    // Distancia cámara-Memi mientras la sigue (unidades de CITY; 0.09 = 9 u ≈ 1.07 m)
    const float DistanciaMemi = 0.09f;
    // El negro debe terminar aquí (s locales de la ciudad) para que quede tiempo de ver a Godzilla.
    const float FinNegroObjetivo = 3.9f;
    const float Frenado = 0.5f;          // segundos que tarda la cámara en frenar
    const float CercaCam = 0.004f;       // plano cercano + margen (CITY)
    const float MedioCuerpo = 0.03f;     // centro de Memi al frente / espalda, con brazos y pelo (CITY)
    const float FundidoEntrada = 0.12f, FundidoSalida = 0.25f;
    const float Fov = 50f, Inclinacion = -10f;
    const float FovGodzilla = 30f;
    // CM3_GODZILLA se pone un poco delante (hacia Godzilla) de donde frenó CM3_MEMO; así Memi ya queda detrás de ella
    // y el negro puede durar la mitad. El salto no se nota porque pasa dentro del negro.
    const float AvanceGodzilla = 0.06f;
    const float FraccionNegro = 0.5f;    // cuánto del paso completo dura el negro (1 = todo el paso)       // lente de CM3_GODZILLA (más cerrado para que Godzilla se vea grande)

    [MenuItem("Tools/Amistad/CM3_MEMO: recorrer toma y negro al pasar Memi")]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[CM3_MEMO] Sal de Play antes de correr la herramienta (en Play los cambios a la escena no se guardan)."); return; }
        var log = new StringBuilder();
        var tl = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        if (tl == null || tlP == null) { Debug.LogError("No encontré los Timelines"); return; }

        var directores = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dir = directores.FirstOrDefault(d => d.playableAsset == tl);
        var dirP = directores.FirstOrDefault(d => d.playableAsset == tlP);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var spline = todos.FirstOrDefault(t => t.name == "Spline_P2_Ciudad");
        var cm3 = todos.FirstOrDefault(t => t.name == "CM3_MEMO");
        var godzilla = todos.FirstOrDefault(t => t.name == "GODZILLA");
        var pistaMemi = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.Contains("recorrido"));
        var pistaCam = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.Contains("dolly"));
        if (dir == null || dirP == null || spline == null || cm3 == null || godzilla == null || pistaMemi == null || pistaCam == null)
        { Debug.LogError("[CM3_MEMO] Falta algo: directores, Spline_P2_Ciudad, CM3_MEMO, GODZILLA o las pistas de recorrido/dolly"); return; }
        var sc = spline.GetComponent<UnityEngine.Splines.SplineContainer>();
        if (sc == null || sc.Spline == null || sc.Spline.Count < 2) { Debug.LogError("Spline_P2_Ciudad no tiene 2 knots"); return; }

        // ---------- 1. Línea de Memi ----------
        Undo.RecordObject(pistaMemi, "CM3");
        var off = pistaMemi.infiniteClipOffsetPosition; off.x = MemiX; pistaMemi.infiniteClipOffsetPosition = off;

        // Posición Z de Memi en el tiempo (local de CITY)
        var curvaZ = AnimationUtility.GetEditorCurve(pistaMemi.infiniteClip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.z"));
        if (curvaZ == null) { Debug.LogError("La pista de Memi no tiene curva Z"); return; }
        float signo = Mathf.Abs(Mathf.DeltaAngle(pistaMemi.infiniteClipOffsetEulerAngles.y, 180f)) < 1f ? -1f : 1f;
        System.Func<float, float> memiZ = t => off.z + signo * curvaZ.Evaluate(t);

        // ---------- 2. Cuándo frena la cámara ----------
        // Cámara: memiZ(t) - D hasta tAlto, luego frena en Frenado s recorriendo la mitad de lo que recorrería.
        float CamZ(float t, float tAlto)
        {
            if (t <= tAlto) return memiZ(t) - DistanciaMemi;
            float v = (memiZ(tAlto + 0.01f) - memiZ(tAlto)) / 0.01f;
            float u = Mathf.Clamp01((t - tAlto) / Frenado);
            return memiZ(tAlto) - DistanciaMemi + v * Frenado * (u - 0.5f * u * u);   // velocidad baja lineal a 0
        }
        (float tIn, float tOut) Paso(float tAlto)
        {
            float a = -1, b = -1;
            for (float t = 0; t <= DuracionCiudad; t += 1f / 120f)
            {
                float z = memiZ(t), c = CamZ(t, tAlto);
                if (a < 0 && (z - MedioCuerpo) - c <= CercaCam) a = t;
                if (a >= 0 && (z + MedioCuerpo) < c - CercaCam) { b = t; break; }
            }
            return (a, b);
        }
        float tAltoOk = 0f; (float tIn, float tOut) paso = (-1, -1);
        for (float ta = 3.5f; ta >= 0f; ta -= 0.05f)
        {
            var p = Paso(ta);
            if (p.tIn >= 0 && p.tOut >= 0 && p.tOut <= FinNegroObjetivo) { tAltoOk = ta; paso = p; break; }
        }
        if (paso.tIn < 0) { Debug.LogError("[CM3_MEMO] Con estos valores Memi no alcanza a pasar la cámara a tiempo. Baja DistanciaMemi o sube FinNegroObjetivo."); return; }
        float tIn = paso.tIn, tOutCompleto = paso.tOut;
        float zFinCam = CamZ(tAltoOk + Frenado, tAltoOk);
        // Memi queda detrás de CM3_GODZILLA cuando su frente pasa camZ + AvanceGodzilla - CercaCam... (su espalda ya no se ve)
        float tLibre = tIn;
        for (float t = tIn; t <= DuracionCiudad; t += 1f / 120f)
            if (memiZ(t) + MedioCuerpo < zFinCam + AvanceGodzilla - CercaCam) { tLibre = t; break; }
        float tOut = Mathf.Max(tIn + (tOutCompleto - tIn) * FraccionNegro, tLibre, tIn + 0.1f);
        log.AppendLine($"La cámara sigue a Memi a {DistanciaMemi} (CITY) y frena en {tAltoOk:F2} s locales ({tAltoOk + InicioCiudad:F2} principal).");
        log.AppendLine($"Negro de {tIn:F2} a {tOut:F2} s locales ({tIn + InicioCiudad:F2} a {tOut + InicioCiudad:F2} principal).");

        // ---------- 3. Spline recto de la cámara y curva del dolly ----------
        float zIni = CamZ(0, tAltoOk), zFin = CamZ(tAltoOk + Frenado, tAltoOk);
        Undo.RecordObject(spline, "CM3"); Undo.RecordObject(sc, "CM3");
        float kx = 0.32f;                                  // X del knot (×0.01) — la cámara queda en MemiX
        var lp = spline.localPosition; lp.x = MemiX - kx * spline.localScale.x; spline.localPosition = lp;
        float sz = spline.localScale.z;
        var sp = sc.Spline;
        var k0 = sp[0]; k0.Position = new float3(kx, CamY, (zIni - lp.z) / sz); sp[0] = k0;
        var k1 = sp[sp.Count - 1]; k1.Position = new float3(kx, CamY, (zFin - lp.z) / sz); sp[sp.Count - 1] = k1;
        EditorUtility.SetDirty(sc);

        var cc = pistaCam.infiniteClip;
        Undo.RecordObject(cc, "CM3");
        var pos = new AnimationCurve();
        float tFinMov = tAltoOk + Frenado;
        for (float t = 0; t < tFinMov; t += 0.1f) pos.AddKey(t, Mathf.InverseLerp(zIni, zFin, CamZ(t, tAltoOk)));
        pos.AddKey(tFinMov, 1f); pos.AddKey(DuracionCiudad, 1f);
        for (int i = 0; i < pos.length; i++) { AnimationUtility.SetKeyLeftTangentMode(pos, i, AnimationUtility.TangentMode.ClampedAuto); AnimationUtility.SetKeyRightTangentMode(pos, i, AnimationUtility.TangentMode.ClampedAuto); }
        AnimationUtility.SetEditorCurve(cc, EditorCurveBinding.FloatCurve("", typeof(CinemachineSplineDolly), "m_SplineSettings.Position"), pos);
        AnimationCurve Fija(float v) { var k = AnimationCurve.Constant(0, DuracionCiudad, v); return k; }
        AnimationUtility.SetEditorCurve(cc, EditorCurveBinding.FloatCurve("", typeof(CinemachineCamera), "Lens.FieldOfView"), Fija(Fov));
        AnimationUtility.SetEditorCurve(cc, EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.x"), Fija(Inclinacion));
        AnimationUtility.SetEditorCurve(cc, EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.y"), Fija(0));
        AnimationUtility.SetEditorCurve(cc, EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.z"), Fija(0));
        EditorUtility.SetDirty(cc);

        // ---------- 4. Canvas negro y su pista ----------
        var fx = Object.FindObjectsByType<CanvasGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.name == "FX_PasoNegro");
        if (fx == null)
        {
            var go = new GameObject("FX_PasoNegro", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup), typeof(Animator));
            Undo.RegisterCreatedObjectUndo(go, "FX_PasoNegro");
            var cv = go.GetComponent<Canvas>(); cv.renderMode = RenderMode.ScreenSpaceOverlay; cv.sortingOrder = 100;
            var img = new GameObject("Negro", typeof(RectTransform), typeof(Image));
            img.transform.SetParent(go.transform, false);
            var rt = img.GetComponent<RectTransform>(); rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var im = img.GetComponent<Image>(); im.color = Color.black; im.raycastTarget = false;
            fx = go.GetComponent<CanvasGroup>();
        }
        fx.alpha = 0f; fx.blocksRaycasts = false; fx.interactable = false;
        var anim = fx.GetComponent<Animator>();
        if (anim == null) anim = fx.gameObject.AddComponent<Animator>();

        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipNegroPath);
        if (clip == null) { clip = new AnimationClip { frameRate = 60 }; AssetDatabase.CreateAsset(clip, ClipNegroPath); }
        var alfa = new AnimationCurve(
            new Keyframe(0, 0), new Keyframe(Mathf.Max(0.01f, tIn - FundidoEntrada), 0), new Keyframe(tIn, 1),
            new Keyframe(tOut, 1), new Keyframe(Mathf.Min(DuracionCiudad - 0.01f, tOut + FundidoSalida), 0), new Keyframe(DuracionCiudad, 0));
        for (int i = 0; i < alfa.length; i++) { AnimationUtility.SetKeyLeftTangentMode(alfa, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(alfa, i, AnimationUtility.TangentMode.Linear); }
        clip.ClearCurves();
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(CanvasGroup), "m_Alpha"), alfa);
        EditorUtility.SetDirty(clip);

        var pista = tl.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == NombrePista);
        if (pista == null) pista = tl.CreateTrack<AnimationTrack>(tl.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "MC"), NombrePista);
        foreach (var vieja in pista.GetClips().ToList()) tl.DeleteClip(vieja);
        var tc = pista.CreateClip(clip); tc.start = 0; tc.duration = DuracionCiudad; tc.displayName = "Negro al pasar Memi";
        Undo.RecordObject(dir, "CM3"); dir.SetGenericBinding(pista, anim);

        // ---------- 5. CM3_GODZILLA: misma posición final, sigue la cabeza de Godzilla ----------
        Transform cabeza = null;
        var animG = godzilla.GetComponentInChildren<Animator>(true);
        if (animG != null && animG.isHuman) cabeza = animG.GetBoneTransform(HumanBodyBones.Head);
        if (cabeza == null) cabeza = godzilla.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.ToLower().EndsWith("head"));
        if (cabeza == null) cabeza = godzilla;

        var camG = todos.FirstOrDefault(t => t.name == "CM3_GODZILLA");
        if (camG == null)
        {
            var go = new GameObject("CM3_GODZILLA");
            Undo.RegisterCreatedObjectUndo(go, "CM3_GODZILLA");
            camG = go.transform;
        }
        Undo.RecordObject(camG, "CM3");
        camG.SetParent(cm3.parent, false);
        camG.localScale = cm3.localScale;
        camG.localPosition = new Vector3(MemiX, lp.y + CamY * spline.localScale.y, zFin + AvanceGodzilla);
        camG.localRotation = Quaternion.Euler(Inclinacion, 0, 0);
        var vcam = camG.GetComponent<CinemachineCamera>(); if (vcam == null) vcam = Undo.AddComponent<CinemachineCamera>(camG.gameObject);
        var comp = camG.GetComponent<CinemachineRotationComposer>(); if (comp == null) comp = Undo.AddComponent<CinemachineRotationComposer>(camG.gameObject);
        var cm3cam = cm3.GetComponent<CinemachineCamera>();
        var lens = cm3cam != null ? cm3cam.Lens : LensSettings.Default; lens.FieldOfView = FovGodzilla; vcam.Lens = lens;
        vcam.Priority.Enabled = false;
        vcam.Target.TrackingTarget = cabeza; vcam.Target.CustomLookAtTarget = false;
        comp.Damping = new Vector2(0.4f, 0.4f);
        comp.CenterOnActivate = false;          // arranca con el encuadre de CM3 (inclinada) y gira suave hacia la cabeza
        comp.Composition.ScreenPosition = new Vector2(0f, 0.1f);
        EditorUtility.SetDirty(vcam); EditorUtility.SetDirty(comp);

        // Corte en el Timeline principal, a mitad del negro
        var pistaCortes = tlP.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault();
        if (pistaCortes == null) { Debug.LogError("No encontré la pista de Cinemachine en el Timeline principal"); return; }
        Undo.RecordObject(tlP, "CM3"); Undo.RecordObject(dirP, "CM3");
        TimelineClip clipCM3 = null, clipG = null;
        foreach (var tcl in pistaCortes.GetClips())
        {
            if (!(tcl.asset is CinemachineShot shot)) continue;
            var obj = dirP.GetReferenceValue(shot.VirtualCamera.exposedName, out bool ok) as Component;
            if (obj == null) obj = shot.VirtualCamera.defaultValue as Component;
            if (obj != null && obj.transform == cm3) clipCM3 = tcl;
            if ((obj != null && obj.transform == camG) || tcl.displayName == "CM3_GODZILLA") clipG = tcl;
        }
        if (clipCM3 == null) { Debug.LogError("No encontré el clip de CM3_MEMO en Cortes de cámara"); return; }
        double finToma = System.Math.Max(clipCM3.end, clipG != null ? clipG.end : 0);
        double corte = InicioCiudad + tIn + 0.03;
        clipCM3.duration = corte - clipCM3.start;
        if (clipG == null) clipG = pistaCortes.CreateClip<CinemachineShot>();
        clipG.start = corte; clipG.duration = finToma - corte; clipG.displayName = "CM3_GODZILLA";
        var shotG = (CinemachineShot)clipG.asset;
        var er = shotG.VirtualCamera;
        if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
        shotG.VirtualCamera = er;
        dirP.SetReferenceValue(er.exposedName, vcam);
        EditorUtility.SetDirty(shotG);
        log.AppendLine($"Corte CM3_MEMO -> CM3_GODZILLA en {corte:F2} s (principal), sigue a '{cabeza.name}' hasta {finToma:F2}.");

        EditorUtility.SetDirty(tl); EditorUtility.SetDirty(tlP);
        EditorUtility.SetDirty(dir); EditorUtility.SetDirty(dirP);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dir.gameObject.scene);
        Debug.Log("[CM3_MEMO] Listo.\n" + log);
    }
}
