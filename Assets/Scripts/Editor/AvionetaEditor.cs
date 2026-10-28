using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > Godzilla: pasa la avioneta
/// Al final de la toma de Godzilla aparece la avioneta: viene de atrás de Godzilla por la derecha, pasa rozándole la cabeza,
/// baja en picada hacia la cámara, pasa a un lado y se aleja por la avenida en llamas. Se ladea en las vueltas.
///  - Material TOON_Avioneta (shader TOON_Godzilla con la textura de la avioneta, sin emisión) en el prefab.
///  - AVIONETA en la raíz de la escena (no dentro de CITY), escalada a 'Largo' u. Hélice con GirarHelice; sonido 3D de la hélice.
///  - Vuelo: clip Avioneta_Vuelo.anim generado aquí (posición suave por puntos + orientación por la tangente con alabeo),
///    en el Timeline de la ciudad (grupo "Avioneta"). Godzilla se queda visible y caminando hasta que termina.
///  - CM3_AVIONETA: misma posición que CM3_GODZILLA, gira siguiendo a la avioneta (Rotation Composer).
///    En el principal: blend de 0.6 s desde CM3_GODZILLA y luego 3 s siguiéndola.
/// Se puede volver a correr: todos los valores son absolutos.
/// </summary>
public static class AvionetaEditor
{
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string Carpeta = "Assets/Assets/3d/Avioneta/";
    const string PrefabPath = Carpeta + "Airplane.prefab";
    const string MatPath = Carpeta + "TOON_Avioneta.mat";
    const string ClipPath = Carpeta + "Avioneta_Vuelo.anim";
    const string MatBase = "Assets/Assets/3d/Persoanjes/Godzilla/Godzilla_Body.mat";
    const float InicioCiudad = 17.5f;
    const float Largo = 32f;                 // u de punta a cola (≈ 3.8 m)
    const float InicioBlend = 28.0f, FinBlend = 28.6f, FinSeguir = 31.6f;   // principal
    const float Alabeo = 0.35f, AlabeoMax = 55f;
    // Cabina: fracción de la altura del casco sobre su centro (1 = arriba) y hacia atrás (-1 = cola)
    const float PovAltura = 0.62f, PovAtras = -0.2f;   // grados de alabeo por grado/s de giro

    // Trayectoria (tiempo del principal, posición de mundo). Cabeza de Godzilla a los 28 s ≈ (0, 97, 3); cámara en (-19, 13, -144).
    static readonly (float t, Vector3 p)[] Puntos =
    {
        (26.5f, new Vector3(330f, 165f, 240f)),   // fuera de cuadro, atrás y a la derecha
        (27.0f, new Vector3(215f, 150f, 150f)),
        (28.0f, new Vector3(40f, 118f, -10f)),    // roza la cabeza de Godzilla
        (29.2f, new Vector3(30f, 45f, -150f)),    // pasa a un lado de la cámara (≈ 50 u a la derecha)
        (30.2f, new Vector3(0f, 55f, -330f)),     // se va por la avenida
        (31.6f, new Vector3(-50f, 105f, -560f)),  // sube a lo lejos
        // Sigue sobre la avenida (primera persona desde la cabina, CM4)
        (33.0f, new Vector3(-80f, 125f, -760f)),
        (35.0f, new Vector3(-105f, 135f, -1060f)),
        (37.0f, new Vector3(-110f, 138f, -1360f)),
        (39.0f, new Vector3(-95f, 136f, -1660f)),
        (43.6f, new Vector3(-55f, 128f, -2350f)),
    };

    // Vista del piloto (hijo AVIONETA_POV): gira a la izquierda para ver a Goku
    public const float GiroInicio = 32.6f, GiroFin = 33.9f, GiroGrados = -130f, PovInclinacion = -2f;

    static TC Comp<TC>(GameObject go) where TC : Component
    {
        var c = go.GetComponent<TC>();
        return c != null ? c : Undo.AddComponent<TC>(go);
    }

    [MenuItem("Tools/Amistad/Godzilla: pasa la avioneta")]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Avioneta] Sal de Play."); return; }
        var log = new StringBuilder();
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        var directores = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = directores.FirstOrDefault(d => d.playableAsset == tlP);
        var dirC = directores.FirstOrDefault(d => d.playableAsset == tlC);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var camG = T("CM3_GODZILLA");
        var pistaCortes = tlP != null ? tlP.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault() : null;
        if (prefab == null || dirP == null || dirC == null || camG == null || pistaCortes == null)
        { Debug.LogError("[Avioneta] Falta algo: prefab de la avioneta, directores, CM3_GODZILLA o la pista de cortes."); return; }

        // ---------- 1. Material toon ----------
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        var baseMat = AssetDatabase.LoadAssetAtPath<Material>(MatBase);
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(Carpeta + "MasterTexture.png");
        if (mat == null)
        {
            mat = baseMat != null ? new Material(baseMat) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, MatPath);
        }
        if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
        if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
        if (mat.HasProperty("_EmisionMap")) mat.SetTexture("_EmisionMap", null);
        if (mat.HasProperty("_EmisionIntensidad")) mat.SetFloat("_EmisionIntensidad", 0f);
        if (mat.HasProperty("_PulsoAmplitud")) mat.SetFloat("_PulsoAmplitud", 0f);
        EditorUtility.SetDirty(mat);
        var contenido = PrefabUtility.LoadPrefabContents(PrefabPath);
        foreach (var r in contenido.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(_ => mat).ToArray();
        PrefabUtility.SaveAsPrefabAsset(contenido, PrefabPath);
        PrefabUtility.UnloadPrefabContents(contenido);
        log.AppendLine($"Material {MatPath} con shader {mat.shader.name}");

        // ---------- 2. AVIONETA en la escena ----------
        var av = T("AVIONETA");
        if (av == null)
        {
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, dirP.gameObject.scene);
            go.name = "AVIONETA";
            Undo.RegisterCreatedObjectUndo(go, "Avioneta");
            av = go.transform;
        }
        Undo.SetTransformParent(av, null, "Avioneta");
        Undo.RecordObject(av, "Avioneta");
        av.localScale = Vector3.one; av.rotation = Quaternion.identity;
        av.gameObject.SetActive(true);   // para medir los bounds (al final se deja apagada; la activa el Timeline)
        var posMedida = av.position;
        var rs = av.GetComponentsInChildren<Renderer>(true);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
        float escala = Largo / Mathf.Max(0.001f, b.size.z);
        av.localScale = Vector3.one * escala;
        if (av.GetComponent<Animator>() == null) Undo.AddComponent<Animator>(av.gameObject);
        var helice = av.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Propeller");
        if (helice != null)
        {
            var gh = Comp<GirarHelice>(helice.gameObject);
            gh.eje = Vector3.forward; gh.vueltas = 6f; EditorUtility.SetDirty(gh);
        }
        var audio = Comp<AudioSource>(av.gameObject);
        audio.clip = AssetDatabase.LoadAssetAtPath<AudioClip>(Carpeta + "Airplane propel.wav");
        audio.loop = true; audio.playOnAwake = true; audio.spatialBlend = 1f; audio.dopplerLevel = 1f;
        audio.rolloffMode = AudioRolloffMode.Logarithmic; audio.minDistance = 40f; audio.maxDistance = 1500f; audio.volume = 0.9f;
        EditorUtility.SetDirty(audio);
        log.AppendLine($"AVIONETA: escala {escala:F2} (largo {Largo} u)");

        // ---------- 3. Clip de vuelo ----------
        float t0 = Puntos[0].t;
        var cx = new AnimationCurve(); var cy = new AnimationCurve(); var cz = new AnimationCurve();
        foreach (var (t, p) in Puntos) { cx.AddKey(t - t0, p.x); cy.AddKey(t - t0, p.y); cz.AddKey(t - t0, p.z); }
        for (int i = 0; i < cx.length; i++) { cx.SmoothTangents(i, 0); cy.SmoothTangents(i, 0); cz.SmoothTangents(i, 0); }
        Vector3 P(float t) => new Vector3(cx.Evaluate(t), cy.Evaluate(t), cz.Evaluate(t));
        float dur = Puntos[Puntos.Length - 1].t - t0, dt = 1f / 30f;
        var qx = new AnimationCurve(); var qy = new AnimationCurve(); var qz = new AnimationCurve(); var qw = new AnimationCurve();
        Quaternion previo = Quaternion.identity; float rollSuave = 0f;
        for (float t = 0; t <= dur + 0.0001f; t += dt)
        {
            float ta = Mathf.Max(0, t - dt), tb = Mathf.Min(dur, t + dt);
            var fwd = (P(tb) - P(ta)).normalized;
            var f1 = P(Mathf.Min(dur, t + 2 * dt)) - P(t); var f0 = P(t) - P(Mathf.Max(0, t - 2 * dt));
            f1.y = 0; f0.y = 0;
            float giro = (f0.sqrMagnitude > 1e-6f && f1.sqrMagnitude > 1e-6f) ? Vector3.SignedAngle(f0, f1, Vector3.up) / (4 * dt) : 0f;  // grados/s
            float roll = Mathf.Clamp(-giro * Alabeo, -AlabeoMax, AlabeoMax);
            rollSuave = Mathf.Lerp(rollSuave, roll, 0.25f);
            var q = Quaternion.LookRotation(fwd, Vector3.up) * Quaternion.AngleAxis(rollSuave, Vector3.forward);
            if (Quaternion.Dot(q, previo) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            previo = q;
            qx.AddKey(t, q.x); qy.AddKey(t, q.y); qz.AddKey(t, q.z); qw.AddKey(t, q.w);
        }
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, ClipPath); }
        clip.ClearCurves(); clip.frameRate = 30;
        void C(string prop, AnimationCurve c) => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), prop), c);
        C("m_LocalPosition.x", cx); C("m_LocalPosition.y", cy); C("m_LocalPosition.z", cz);
        C("m_LocalRotation.x", qx); C("m_LocalRotation.y", qy); C("m_LocalRotation.z", qz); C("m_LocalRotation.w", qw);
        // Vista del piloto: hijo en la cabina (detrás de las alas, a la altura de los ojos) con su giro en el mismo clip
        var pov = av.Find("AVIONETA_POV");
        if (pov == null) { var gp = new GameObject("AVIONETA_POV"); Undo.RegisterCreatedObjectUndo(gp, "Avioneta"); pov = gp.transform; pov.SetParent(av, false); }
        // b = caja de la avioneta medida con rotación identidad y escala 1 (ejes = ejes locales de la avioneta)
        var cab = new Vector3(b.center.x, b.center.y + b.extents.y * PovAltura, b.center.z + b.extents.z * PovAtras) - posMedida;
        Undo.RecordObject(pov, "Avioneta");
        pov.localPosition = cab; pov.localRotation = Quaternion.Euler(PovInclinacion, 0, 0); pov.localScale = Vector3.one;
        log.AppendLine($"AVIONETA_POV en {cab} (local; caja {b.center - posMedida} ± {b.extents})");
        var gx = new AnimationCurve(); var gy = new AnimationCurve(); var gz = new AnimationCurve(); var gw = new AnimationCurve();
        for (float t = 0; t <= dur + 0.0001f; t += dt)
        {
            float k = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GiroInicio, GiroFin, t + t0));
            var q = Quaternion.Euler(PovInclinacion, GiroGrados * k, 0f);
            gx.AddKey(t, q.x); gy.AddKey(t, q.y); gz.AddKey(t, q.z); gw.AddKey(t, q.w);
        }
        void CP(string prop, AnimationCurve c) => AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("AVIONETA_POV", typeof(Transform), prop), c);
        CP("m_LocalRotation.x", gx); CP("m_LocalRotation.y", gy); CP("m_LocalRotation.z", gz); CP("m_LocalRotation.w", gw);
        var pl = new AnimationCurve(new Keyframe(0, cab.x), new Keyframe(dur, cab.x)); CP("m_LocalPosition.x", pl);
        CP("m_LocalPosition.y", new AnimationCurve(new Keyframe(0, cab.y), new Keyframe(dur, cab.y)));
        CP("m_LocalPosition.z", new AnimationCurve(new Keyframe(0, cab.z), new Keyframe(dur, cab.z)));
        EditorUtility.SetDirty(clip);

        // ---------- 4. Timeline de la ciudad ----------
        Undo.RecordObject(tlC, "Avioneta"); Undo.RecordObject(dirC, "Avioneta");
        var grupo = tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "Avioneta") ?? tlC.CreateTrack<GroupTrack>(null, "Avioneta");
        var pVis = grupo.GetChildTracks().OfType<ActivationTrack>().FirstOrDefault() ?? tlC.CreateTrack<ActivationTrack>(grupo, "Avioneta · visible");
        var pVuelo = grupo.GetChildTracks().OfType<AnimationTrack>().FirstOrDefault() ?? tlC.CreateTrack<AnimationTrack>(grupo, "Avioneta · vuelo");
        foreach (var c in pVis.GetClips().ToList()) tlC.DeleteClip(c);
        foreach (var c in pVuelo.GetClips().ToList()) tlC.DeleteClip(c);
        var cv = pVis.CreateDefaultClip(); cv.start = t0 - InicioCiudad; cv.duration = dur; cv.displayName = "Avioneta visible";
        var ca = pVuelo.CreateClip(clip); ca.start = t0 - InicioCiudad; ca.duration = dur; ca.displayName = "Vuelo";
        var apa = (AnimationPlayableAsset)ca.asset; apa.removeStartOffset = false; apa.position = Vector3.zero; apa.rotation = Quaternion.identity;
        pVuelo.trackOffset = TrackOffset.ApplyTransformOffsets; pVuelo.position = Vector3.zero; pVuelo.rotation = Quaternion.identity;
        pVis.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
        dirC.SetGenericBinding(pVis, av.gameObject);
        dirC.SetGenericBinding(pVuelo, av.GetComponent<Animator>());
        Undo.RecordObject(av.gameObject, "Avioneta"); av.gameObject.SetActive(false);
        log.AppendLine($"Vuelo: {t0:F1} -> {t0 + dur:F1} s del principal ({Puntos.Length} puntos)");

        // Godzilla sigue visible y caminando mientras pasa la avioneta
        float finLocal = FinSeguir - InicioCiudad + 0.5f;
        foreach (var pista in tlC.GetOutputTracks().Where(t => t.name.Contains("Godzilla")))
            foreach (var c in pista.GetClips()) if (c.end < finLocal) { Undo.RecordObject(pista, "Avioneta"); c.duration = finLocal - c.start; EditorUtility.SetDirty(pista); log.AppendLine($"'{pista.name}' hasta {finLocal + InicioCiudad:F1} s"); }

        // ---------- 5. Cámara que la sigue ----------
        var cam = T("CM3_AVIONETA");
        if (cam == null) { var go = new GameObject("CM3_AVIONETA"); Undo.RegisterCreatedObjectUndo(go, "Avioneta"); cam = go.transform; }
        Undo.RecordObject(cam, "Avioneta");
        cam.SetPositionAndRotation(camG.position, camG.rotation); cam.localScale = Vector3.one;
        var vcam = Comp<CinemachineCamera>(cam.gameObject);
        var lens = camG.GetComponent<CinemachineCamera>().Lens; lens.FieldOfView = 40f;
        vcam.Lens = lens; vcam.Priority.Enabled = false;
        vcam.Target.TrackingTarget = av; vcam.Target.CustomLookAtTarget = false;
        var comp = Comp<CinemachineRotationComposer>(cam.gameObject);
        comp.Damping = new Vector2(0.25f, 0.25f);
        comp.CenterOnActivate = false;
        EditorUtility.SetDirty(vcam); EditorUtility.SetDirty(comp);

        // ---------- 6. Cortes ----------
        Undo.RecordObject(pistaCortes, "Avioneta"); Undo.RecordObject(dirP, "Avioneta");
        TimelineClip clipG = null;
        foreach (var c in pistaCortes.GetClips())
        {
            if (!(c.asset is CinemachineShot s)) continue;
            var o = dirP.GetReferenceValue(s.VirtualCamera.exposedName, out bool _) as Component;
            if ((o != null && o.transform == camG) || c.displayName == "CM3_GODZILLA") clipG = c;
        }
        if (clipG != null) clipG.duration = FinBlend - clipG.start;
        var clipA = pistaCortes.GetClips().FirstOrDefault(c => c.displayName == "CM3_AVIONETA") ?? pistaCortes.CreateClip<CinemachineShot>();
        clipA.start = InicioBlend; clipA.duration = FinSeguir - InicioBlend; clipA.displayName = "CM3_AVIONETA";
        var shot = (CinemachineShot)clipA.asset; Undo.RecordObject(shot, "Avioneta");
        var er = shot.VirtualCamera;
        if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
        er.defaultValue = null; shot.VirtualCamera = er;
        dirP.SetReferenceValue(er.exposedName, vcam);
        EditorUtility.SetDirty(shot);
        log.AppendLine($"Cortes: CM3_GODZILLA hasta {FinBlend} | blend {InicioBlend}-{FinBlend} | CM3_AVIONETA hasta {FinSeguir}");

        EditorUtility.SetDirty(tlC); EditorUtility.SetDirty(tlP); EditorUtility.SetDirty(pistaCortes); EditorUtility.SetDirty(dirC); EditorUtility.SetDirty(dirP);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dirP.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        System.IO.File.WriteAllText("Logs/avioneta.txt", log.ToString());
        Debug.Log("[Avioneta] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }
}
