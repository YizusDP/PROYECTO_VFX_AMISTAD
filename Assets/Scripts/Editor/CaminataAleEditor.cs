using System.Linq;
using System.Text;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 2 · Ciudad > Caminata de Ale (43-46 s)
/// Toma nueva entre CM4_SigueGoku y CM7_ENCUENTRO: Ale caminando hacia el encuentro, de cuerpo completo.
///  - Antes Ale recorría ese tramo fuera de cuadro a 4x su paso; ahora desde los 43 s camina a su paso normal
///    (4.5 u/s, el mismo del encuentro) para que los pies no patinen. Lo que le falta lo sigue haciendo antes, fuera de cuadro.
///  - CM5_ALE_CAMINA: cámara de 3/4 que lo acompaña (Cinemachine Follow en World Space), del mismo lado que CM7_ENCUENTRO
///    para que Ale avance en la misma dirección de pantalla en las dos tomas.
/// Se puede volver a correr: los valores son absolutos.
/// </summary>
public static class CaminataAleEditor
{
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const float InicioCiudad = 17.5f;
    const float Inicio = 43f, Fin = 46f;             // principal
    const float FinTomaAle = 6f;                     // local: termina CM3_ALE
    const float VelAle = 0.045f;                     // unidades locales de CITY por s (= EncuentroEditor)
    // Cámara (unidades de mundo; Ale mide ~17 u)
    const float Lateral = 26f, Adelante = 10f, Altura = 9f, AlturaMirada = 9.5f, Fov = 40f;
    const string NombreCam = "CM5_ALE_CAMINA";

    [MenuItem("Tools/Amistad/2 · Ciudad/Caminata de Ale (43-46 s)", false, 226)]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Caminata Ale] Sal de Play antes de correr la herramienta."); return; }
        var log = new StringBuilder();
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var directores = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = directores.FirstOrDefault(d => d.playableAsset == tlP);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var city = T("CITY"); var ale = T("ALE"); var cm7 = T("CM7_ENCUENTRO");
        var pistaAle = tlC?.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.StartsWith("Ale") && t.name.Contains("recorrido"));
        var pistaCortes = tlP?.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault();
        if (dirP == null || city == null || ale == null || cm7 == null || pistaAle?.infiniteClip == null || pistaCortes == null)
        { Debug.LogError("[Caminata Ale] Falta algo (director principal, CITY, ALE, CM7_ENCUENTRO o la pista 'Ale · recorrido'). Corre antes 'Escena 7: encuentro'."); return; }

        // ---------- 1. Ale camina a su paso desde los 43 s ----------
        float tIni = Inicio - InicioCiudad, tFin = Fin - InicioCiudad;          // local de la ciudad
        var clipA = pistaAle.infiniteClip;
        var bZ = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.z");
        var cz = AnimationUtility.GetEditorCurve(clipA, bZ);
        float zEntra = cz.Evaluate(tFin);
        float zIni = zEntra - VelAle * (tFin - tIni);
        var keys = cz.keys.Where(k => k.time <= FinTomaAle + 0.01f || k.time >= tFin - 0.01f).ToList();
        keys.Add(new Keyframe(tIni, zIni, VelAle, VelAle));
        keys = keys.OrderBy(k => k.time).ToList();
        Undo.RecordObject(clipA, "Caminata Ale");
        AnimationUtility.SetEditorCurve(clipA, bZ, new AnimationCurve(keys.ToArray()));
        EditorUtility.SetDirty(clipA);
        float escala = city.lossyScale.z;
        var kFinToma = keys.First(k => k.time >= FinTomaAle - 0.01f);
        log.AppendLine($"Ale: de {Inicio} a {Fin} s camina a {VelAle * escala:F1} u/s (su paso). Fuera de cuadro ({InicioCiudad + FinTomaAle}-{Inicio} s) recorre a {(zIni - kFinToma.value) / (tIni - kFinToma.time) * escala:F1} u/s.");

        // ¿Ale visible en la toma?
        var pistaVis = tlC.GetOutputTracks().OfType<ActivationTrack>().FirstOrDefault(t => t.name.StartsWith("Ale"));
        if (pistaVis != null)
        {
            Undo.RecordObject(pistaVis, "Caminata Ale");
            // "su toma" termina con CM3_ALE; el clip del encuentro empieza ahora con esta toma (43 s).
            foreach (var c in pistaVis.GetClips().Where(c => c.displayName.Contains("su toma") && c.end > FinTomaAle + 0.01)) { c.duration = FinTomaAle - c.start; log.AppendLine("'Ale visible (su toma)' termina otra vez a los " + (InicioCiudad + FinTomaAle) + " s."); }
            var enc = pistaVis.GetClips().Where(c => c.end >= tFin).OrderBy(c => c.start).FirstOrDefault();
            if (enc != null && enc.start > tIni + 0.01) { double fin = enc.end; enc.start = tIni; enc.duration = fin - tIni; log.AppendLine($"'{enc.displayName}' empieza ahora a los {Inicio} s."); }
            EditorUtility.SetDirty(pistaVis);
        }

        // ---------- 2. Cámara ----------
        Vector3 MundoAle(float z) => city.TransformPoint(pistaAle.infiniteClipOffsetPosition + Quaternion.Euler(pistaAle.infiniteClipOffsetEulerAngles) * new Vector3(0, 0, z));
        var pAle = MundoAle(zIni);
        var dirA = MundoAle(zIni + 0.1f) - pAle; dirA.y = 0; dirA.Normalize();
        var lat = Vector3.Cross(Vector3.up, dirA).normalized;
        var lado7 = cm7.position - MundoAle(zEntra); lado7.y = 0;                  // del mismo lado que CM7_ENCUENTRO
        if (Vector3.Dot(lat, lado7) < 0) lat = -lat;
        var offset = lat * Lateral + dirA * Adelante + Vector3.up * Altura;
        var posCam = pAle + offset;
        var mira = pAle + Vector3.up * AlturaMirada;

        var t = T(NombreCam);
        if (t == null) { var go = new GameObject(NombreCam); Undo.RegisterCreatedObjectUndo(go, NombreCam); t = go.transform; }
        Undo.RecordObject(t, NombreCam);
        if (t.parent != null && (t.parent.lossyScale - Vector3.one).sqrMagnitude > 1e-6f) t.SetParent(null, true);   // en una carpeta (escala 1) se queda
        t.SetPositionAndRotation(posCam, Quaternion.LookRotation(mira - posCam, Vector3.up));
        t.localScale = Vector3.one;
        var vcam = t.GetComponent<CinemachineCamera>(); if (vcam == null) vcam = Undo.AddComponent<CinemachineCamera>(t.gameObject);
        Undo.RecordObject(vcam, NombreCam);
        var lens = cm7.GetComponent<CinemachineCamera>().Lens; lens.FieldOfView = Fov;
        vcam.Lens = lens; vcam.Priority.Enabled = false;
        vcam.Target.TrackingTarget = ale; vcam.Target.CustomLookAtTarget = false;
        foreach (var c in t.GetComponents<CinemachineComponentBase>().Where(c => !(c is CinemachineFollow))) Undo.DestroyObjectImmediate(c);
        var follow = t.GetComponent<CinemachineFollow>(); if (follow == null) follow = Undo.AddComponent<CinemachineFollow>(t.gameObject);
        Undo.RecordObject(follow, NombreCam);
        follow.FollowOffset = offset;
        var ts = follow.TrackerSettings; ts.BindingMode = BindingMode.WorldSpace; ts.PositionDamping = Vector3.zero; follow.TrackerSettings = ts;
        EditorUtility.SetDirty(vcam); EditorUtility.SetDirty(follow);
        log.AppendLine($"{NombreCam}: a {Lateral} u de lado y {Adelante} u adelante de Ale, altura {Altura}, FOV {Fov}. Empieza en {posCam}.");

        // ---------- 3. Corte en el principal ----------
        Undo.RecordObject(pistaCortes, "Caminata Ale"); Undo.RecordObject(dirP, "Caminata Ale");
        var clip = pistaCortes.GetClips().FirstOrDefault(c => c.displayName == NombreCam) ?? pistaCortes.CreateClip<CinemachineShot>();
        clip.start = Inicio; clip.duration = Fin - Inicio; clip.displayName = NombreCam;
        var shot = (CinemachineShot)clip.asset;
        Undo.RecordObject(shot, "Caminata Ale");
        var er = shot.VirtualCamera;
        if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
        er.defaultValue = null; shot.VirtualCamera = er;
        dirP.SetReferenceValue(er.exposedName, vcam);
        EditorUtility.SetDirty(shot); EditorUtility.SetDirty(pistaCortes); EditorUtility.SetDirty(tlP); EditorUtility.SetDirty(tlC); EditorUtility.SetDirty(dirP);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dirP.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        log.AppendLine($"Corte {NombreCam}: {Inicio}-{Fin} s");
        System.IO.File.WriteAllText("Logs/caminata_ale.txt", log.ToString());
        Debug.Log("[Caminata Ale] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }
}
