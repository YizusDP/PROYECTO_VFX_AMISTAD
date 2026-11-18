using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 1 · Bosque > Cascada + disolvencia al gato
/// Parte el dolly viejo de CM1_CASCADA en dos tomas con corte seco, y quita el tramo del giro entre ellas:
///   CM1_CASCADA (0 -> Corte): avanza suave por el tramo recto del spline (0 a 623.6), mirando la cascada.
///   CM1_GATO   (Corte -> 11.95): mismo spline y mismo LookAt; arranca en el knot 2 (fin del giro), se acerca al gato,
///              se detiene frente a él (6.9 s) y se aleja cuando explota (8.6 -> 11.2 s), igual que el dolly original.
/// Entre las dos tomas hay una disolvencia (DisolvenciaCamaras + FX_Disolvencia): la Main Camera corta al gato en
/// Corte - Disolvencia/2 y la imagen de la cascada (que sigue en movimiento) se desvanece encima durante 'Disolvencia' s.
/// Las tres curvas van en CM1_CASCADATimeline (pistas "Cámara cascada (dolly)", "Cámara gato (dolly)" y "Disolvencia").
/// La curva original se respalda la primera vez en Assets/Animaciones/CM1_CASCADA_dolly_original.anim.
/// Se puede volver a correr: todos los valores son absolutos.
///
/// Tools > Amistad > Revisión y pruebas > Capturas del bosque
/// Guarda lo que ve la Main Camera en Logs/capturas_bosque/t_XX.png (sin la disolvencia, que es UI). Recarga la escena al final.
/// </summary>
public static class BosqueCamarasEditor
{
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineBosque = "Assets/Animaciones/CM1_CASCADATimeline.playable";
    const string RespaldoCurva = "Assets/Animaciones/CM1_CASCADA_dolly_original.anim";
    const string ClipGatoPath = "Assets/Animaciones/CM1_GATO_dolly.anim";
    const string PistaGatoNombre = "Cámara gato (dolly)";
    const string NombreGato = "CM1_GATO";
    const string PropDolly = "m_SplineSettings.Position";

    // Centro de la transición cascada -> gato (segundos del Timeline principal) y duración de la disolvencia.
    const float Corte = 5f;
    const float Disolvencia = 1.2f;
    const float InicioDisolvencia = Corte - Disolvencia / 2f;   // aquí corta la Main Camera al gato
    const float FinDisolvencia = Corte + Disolvencia / 2f;      // aquí ya solo se ve el gato
    const string ClipDisolvenciaPath = "Assets/Animaciones/CM1_Disolvencia.anim";
    const string PistaDisolvenciaNombre = "Disolvencia";

    // Cascada: distancia sobre el spline (unidades). El tramo recto llega a 623.6; después empieza el giro.
    const float CascadaFin = 600f;         // posición al terminar la disolvencia
    const float CascadaVelFinal = 40f;     // sigue en movimiento mientras se desvanece

    // Gato: valores del dolly original.
    const float GatoInicio = 918.2578f;    // knot 2, ya terminado el giro
    const float GatoVelInicio = 60f;
    static Keyframe[] KeysGato() => new[]
    {
        new Keyframe(0f, GatoInicio, 0f, 0f),
        new Keyframe(InicioDisolvencia, GatoInicio, 0f, GatoVelInicio),
        new Keyframe(6.9f, 1005.7272f, 0f, 0f),             // se detiene frente al gato
        new Keyframe(8.6f, 1109.4197f, 91.49337f, 91.49337f), // se aleja con la explosión
        new Keyframe(11.2f, 1262.9989f, 0f, 0f),
        new Keyframe(11.617f, 1262.9989f, 0f, 0f),
        new Keyframe(13f, 1575.299f, 0f, 0f),
    };
    static Keyframe[] KeysCascada() => new[]
    {
        new Keyframe(0f, 0f, 0f, 0f),
        new Keyframe(FinDisolvencia, CascadaFin, CascadaVelFinal, CascadaVelFinal),
    };

    [MenuItem("Tools/Amistad/1 · Bosque/Cascada + disolvencia al gato", false, 100)]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Bosque] Sal de Play antes de correr la herramienta."); return; }
        var log = new StringBuilder();
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var tlB = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineBosque);
        if (tlP == null || tlB == null) { Debug.LogError("[Bosque] No encontré los Timelines principal o del bosque"); return; }

        var directores = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = directores.FirstOrDefault(d => d.playableAsset == tlP);
        var dirB = directores.FirstOrDefault(d => d.playableAsset == tlB);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var cm1 = todos.FirstOrDefault(t => t.name == "CM1_CASCADA");
        if (dirP == null || dirB == null || cm1 == null) { Debug.LogError("[Bosque] Falta Director_P1_Bosque, el director de CM1_CASCADATimeline o CM1_CASCADA"); return; }
        var vcam1 = cm1.GetComponent<CinemachineCamera>();
        var dolly1 = cm1.GetComponent<CinemachineSplineDolly>();
        var mira1 = cm1.GetComponent<CinemachineSplineDollyLookAtTargets>();
        if (vcam1 == null || dolly1 == null) { Debug.LogError("[Bosque] CM1_CASCADA no tiene CinemachineCamera + Spline Dolly"); return; }

        var binding = EditorCurveBinding.FloatCurve("", typeof(CinemachineSplineDolly), PropDolly);

        // ---------- 1. Cascada: nueva curva en la pista del dolly de CM1_CASCADA ----------
        var pistaC = tlB.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.Contains("dolly") && t.name != PistaGatoNombre);
        var clipC = pistaC != null ? pistaC.infiniteClip : null;
        if (clipC == null) { Debug.LogError("[Bosque] No encontré la pista 'Cámara cascada (dolly)' con su clip grabado"); return; }
        var bC = AnimationUtility.GetCurveBindings(clipC).FirstOrDefault(x => x.propertyName == PropDolly);
        if (bC.propertyName == null) bC = binding;
        if (AssetDatabase.LoadAssetAtPath<AnimationClip>(RespaldoCurva) == null)
        {
            var viejo = AnimationUtility.GetEditorCurve(clipC, bC);
            if (viejo != null)
            {
                var resp = new AnimationClip { frameRate = 60 };
                AnimationUtility.SetEditorCurve(resp, bC, viejo);
                AssetDatabase.CreateAsset(resp, RespaldoCurva);
                log.AppendLine($"Respaldo de la curva original: {RespaldoCurva}");
            }
        }
        Undo.RecordObject(clipC, "Bosque cámaras");
        AnimationUtility.SetEditorCurve(clipC, bC, new AnimationCurve(KeysCascada()));
        EditorUtility.SetDirty(clipC);
        log.AppendLine($"CM1_CASCADA: dolly 0 -> {CascadaFin} entre 0 y {FinDisolvencia} s");

        // ---------- 2. CM1_GATO: copia de la cámara de dolly (sin director) ----------
        var gato = todos.FirstOrDefault(t => t != null && t.name == NombreGato);
        if (gato == null)
        {
            var go = new GameObject(NombreGato);
            Undo.RegisterCreatedObjectUndo(go, NombreGato);
            gato = go.transform;
            gato.SetParent(cm1.parent, false);
            gato.SetSiblingIndex(cm1.GetSiblingIndex() + 1);
        }
        Undo.RecordObject(gato, NombreGato);
        gato.SetPositionAndRotation(cm1.position, cm1.rotation);
        gato.localScale = Vector3.one;
        foreach (var c in gato.GetComponents<CinemachineComponentBase>()) Undo.DestroyObjectImmediate(c);
        foreach (var c in gato.GetComponents<CinemachineExtension>()) Undo.DestroyObjectImmediate(c);
        var vcamG = gato.GetComponent<CinemachineCamera>();
        if (vcamG == null) vcamG = Undo.AddComponent<CinemachineCamera>(gato.gameObject);
        Undo.RecordObject(vcamG, NombreGato);
        vcamG.Lens = vcam1.Lens;
        vcamG.Priority.Enabled = false;
        vcamG.OutputChannel = vcam1.OutputChannel;
        vcamG.Target = vcam1.Target;
        EditorUtility.SetDirty(vcamG);
        if (ComponentUtility.CopyComponent(dolly1)) ComponentUtility.PasteComponentAsNew(gato.gameObject);
        if (mira1 != null && ComponentUtility.CopyComponent(mira1)) ComponentUtility.PasteComponentAsNew(gato.gameObject);
        var dollyG = gato.GetComponent<CinemachineSplineDolly>();
        if (dollyG == null) { Debug.LogError("[Bosque] No se pudo copiar el Spline Dolly a CM1_GATO"); return; }
        dollyG.CameraPosition = GatoInicio;
        EditorUtility.SetDirty(dollyG);
        var animG = gato.GetComponent<Animator>();
        if (animG == null) animG = Undo.AddComponent<Animator>(gato.gameObject);

        // Curva del gato en su propio clip y su propia pista dentro de CM1_CASCADATimeline
        var clipG = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipGatoPath);
        if (clipG == null) { clipG = new AnimationClip { frameRate = 60 }; AssetDatabase.CreateAsset(clipG, ClipGatoPath); }
        Undo.RecordObject(clipG, "Bosque cámaras");
        AnimationUtility.SetEditorCurve(clipG, binding, new AnimationCurve(KeysGato()));
        EditorUtility.SetDirty(clipG);

        Undo.RecordObject(tlB, "Bosque cámaras");
        var pistaG = tlB.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == PistaGatoNombre)
                     ?? tlB.CreateTrack<AnimationTrack>(null, PistaGatoNombre);
        foreach (var tc in pistaG.GetClips().ToList()) tlB.DeleteClip(tc);
        var tcG = pistaG.CreateClip(clipG);
        tcG.start = 0; tcG.duration = 17.5; tcG.displayName = "Dolly gato";
        Undo.RecordObject(dirB, "Bosque cámaras");
        dirB.SetGenericBinding(pistaG, animG);
        EditorUtility.SetDirty(tlB); EditorUtility.SetDirty(pistaG); EditorUtility.SetDirty(dirB);
        log.AppendLine($"CM1_GATO: dolly {GatoInicio} (knot 2) -> 1005.7 (6.9 s, frente al gato) -> 1263 (11.2 s, se aleja)");

        // ---------- 3. Cortes en el Timeline principal ----------
        var pistaCortes = tlP.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault();
        if (pistaCortes == null) { Debug.LogError("[Bosque] No encontré la pista de Cinemachine en el Timeline principal"); return; }
        Undo.RecordObject(tlP, "Bosque cámaras"); Undo.RecordObject(pistaCortes, "Bosque cámaras"); Undo.RecordObject(dirP, "Bosque cámaras");
        var fija = todos.FirstOrDefault(t => t != null && t.name == "CM1_CASCADA_FIJA");
        TimelineClip corteC = null, corteG = null;
        foreach (var tcl in pistaCortes.GetClips())
        {
            if (!(tcl.asset is CinemachineShot shot)) continue;
            var obj = dirP.GetReferenceValue(shot.VirtualCamera.exposedName, out bool _) as Component;
            var tr = obj != null ? obj.transform : null;
            if ((tr != null && (tr == cm1 || tr == fija)) || tcl.displayName == "CM1_CASCADA" || tcl.displayName == "CM1_CASCADA_FIJA") corteC = tcl;
            else if ((tr != null && tr == gato) || tcl.displayName == NombreGato) corteG = tcl;
        }
        if (corteC == null) { Debug.LogError("[Bosque] No encontré el clip de la cascada en Cortes de cámara"); return; }
        double fin = corteG != null ? corteG.end : corteC.end;
        corteC.duration = InicioDisolvencia - corteC.start; corteC.displayName = "CM1_CASCADA";
        Asignar((CinemachineShot)corteC.asset, vcam1, dirP);
        if (corteG == null) corteG = pistaCortes.CreateClip<CinemachineShot>();
        corteG.start = InicioDisolvencia; corteG.duration = fin - InicioDisolvencia; corteG.displayName = NombreGato;
        Asignar((CinemachineShot)corteG.asset, vcamG, dirP);
        EditorUtility.SetDirty(tlP); EditorUtility.SetDirty(pistaCortes); EditorUtility.SetDirty(dirP);
        log.AppendLine($"Cortes: CM1_CASCADA {corteC.start:F2}-{InicioDisolvencia:F2} s | CM1_GATO {InicioDisolvencia:F2}-{fin:F2} s");

        // ---------- 4. Disolvencia ----------
        var mainCam = Object.FindObjectsByType<CinemachineBrain>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                          .Select(b => b.GetComponent<Camera>()).FirstOrDefault(c => c != null);
        if (mainCam == null) { Debug.LogError("[Bosque] No encontré la Main Camera con CinemachineBrain"); return; }
        var fx = todos.FirstOrDefault(t => t != null && t.name == "FX_Disolvencia");
        if (fx == null)
        {
            var go = new GameObject("FX_Disolvencia");
            Undo.RegisterCreatedObjectUndo(go, "FX_Disolvencia");
            fx = go.transform;
            fx.SetSiblingIndex(cm1.GetSiblingIndex() + 2);
        }
        var canvasT = fx.Find("Canvas");
        if (canvasT == null)
        {
            var goC = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas));
            Undo.RegisterCreatedObjectUndo(goC, "FX_Disolvencia");
            goC.transform.SetParent(fx, false);
            canvasT = goC.transform;
        }
        var canvas = canvasT.GetComponent<Canvas>();
        Undo.RecordObject(canvas, "FX_Disolvencia");
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 40;
        var imgT = canvasT.Find("ImagenCascada");
        if (imgT == null)
        {
            var goI = new GameObject("ImagenCascada", typeof(RectTransform), typeof(UnityEngine.UI.RawImage));
            Undo.RegisterCreatedObjectUndo(goI, "FX_Disolvencia");
            goI.transform.SetParent(canvasT, false);
            imgT = goI.transform;
        }
        var rtI = (RectTransform)imgT;
        rtI.anchorMin = Vector2.zero; rtI.anchorMax = Vector2.one; rtI.offsetMin = Vector2.zero; rtI.offsetMax = Vector2.zero;
        var raw = imgT.GetComponent<UnityEngine.UI.RawImage>();
        Undo.RecordObject(raw, "FX_Disolvencia");
        raw.raycastTarget = false; raw.enabled = false; raw.texture = null;
        var dis = fx.GetComponent<DisolvenciaCamaras>();
        if (dis == null) dis = Undo.AddComponent<DisolvenciaCamaras>(fx.gameObject);
        Undo.RecordObject(dis, "FX_Disolvencia");
        dis.camaraSaliente = vcam1; dis.camaraPrincipal = mainCam; dis.imagen = raw; dis.mezcla = 0f;
        EditorUtility.SetDirty(dis); EditorUtility.SetDirty(raw); EditorUtility.SetDirty(canvas);
        var animD = fx.GetComponent<Animator>();
        if (animD == null) animD = Undo.AddComponent<Animator>(fx.gameObject);

        // Curva de opacidad: 0 hasta el corte, salta a 1 justo en el corte y baja suave a 0
        var curvaD = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0f),
            new Keyframe(InicioDisolvencia - 0.001f, 0f, 0f, 1000f),
            new Keyframe(InicioDisolvencia, 1f, 1000f, 0f),
            new Keyframe(FinDisolvencia, 0f, 0f, 0f));
        var clipD = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipDisolvenciaPath);
        if (clipD == null) { clipD = new AnimationClip { frameRate = 60 }; AssetDatabase.CreateAsset(clipD, ClipDisolvenciaPath); }
        Undo.RecordObject(clipD, "Bosque cámaras");
        AnimationUtility.SetEditorCurve(clipD, EditorCurveBinding.FloatCurve("", typeof(DisolvenciaCamaras), "mezcla"), curvaD);
        EditorUtility.SetDirty(clipD);
        var pistaD = tlB.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name == PistaDisolvenciaNombre)
                     ?? tlB.CreateTrack<AnimationTrack>(null, PistaDisolvenciaNombre);
        foreach (var tc in pistaD.GetClips().ToList()) tlB.DeleteClip(tc);
        var tcD = pistaD.CreateClip(clipD);
        tcD.start = 0; tcD.duration = 17.5; tcD.displayName = "Disolvencia cascada -> gato";
        dirB.SetGenericBinding(pistaD, animD);
        EditorUtility.SetDirty(tlB); EditorUtility.SetDirty(pistaD); EditorUtility.SetDirty(dirB);
        log.AppendLine($"Disolvencia: {InicioDisolvencia:F2} -> {FinDisolvencia:F2} s (FX_Disolvencia, pista '{PistaDisolvenciaNombre}')");

        // La cámara fija de la versión anterior ya no se usa
        if (fija != null) { Undo.DestroyObjectImmediate(fija.gameObject); log.AppendLine("Se borró CM1_CASCADA_FIJA (versión fija anterior)."); }

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dirP.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        Debug.Log("[Bosque] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }

    static void Asignar(CinemachineShot shot, CinemachineCamera cam, PlayableDirector dir)
    {
        Undo.RecordObject(shot, "Bosque cámaras");
        var er = shot.VirtualCamera;
        if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
        er.defaultValue = null;
        shot.VirtualCamera = er;
        dir.SetReferenceValue(er.exposedName, cam);
        shot.DisplayName = "";
        EditorUtility.SetDirty(shot);
    }

    [MenuItem("Tools/Amistad/Revisión y pruebas/Capturas del bosque", false, 911)]
    static void Capturar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Capturas bosque] Sal de Play."); return; }
        var escena = SceneManager.GetActiveScene();
        if (escena.isDirty) { Debug.LogError("[Capturas bosque] Guarda la escena (Ctrl+S) antes: al terminar se recarga del disco."); return; }
        var todos = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var p1 = T("Director_P1_Bosque")?.GetComponent<PlayableDirector>();
        var cam = T("Main Camera")?.GetComponent<Camera>();
        if (p1 == null || cam == null) { Debug.LogError("[Capturas bosque] Falta Director_P1_Bosque o Main Camera"); return; }
        var brain = cam.GetComponent<CinemachineBrain>();
        System.IO.Directory.CreateDirectory("Logs/capturas_bosque");
        var rt = new RenderTexture(960, 540, 24);
        var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var info = new StringBuilder();
        foreach (var t in new[] { 0.5, 2.0, 3.5, 4.3, 4.5, 5.6, 6.9, 7.8, 8.6, 9.5, 10.5, 11.5 })
        {
            p1.time = t; p1.Evaluate();
            if (brain != null) { brain.ManualUpdate(); brain.ManualUpdate(); }
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = null;
            System.IO.File.WriteAllBytes($"Logs/capturas_bosque/t_{t:00.0}.png", tex.EncodeToPNG());
            var activa = brain != null && brain.ActiveVirtualCamera != null ? brain.ActiveVirtualCamera.Name : "-";
            info.AppendLine($"t={t}: cámara activa {activa} · pos {cam.transform.position} rot {cam.transform.eulerAngles}");
        }
        if (!string.IsNullOrEmpty(escena.path)) EditorSceneManager.OpenScene(escena.path);
        Object.DestroyImmediate(rt);
        System.IO.File.WriteAllText("Logs/capturas_bosque/info.txt", info.ToString());
        Debug.Log("[Capturas bosque]\n" + info);
    }
}
