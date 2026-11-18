using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 2 · Ciudad > CM4: primera persona en la avioneta + Goku SSJ
/// Secuencia (tiempos del principal):
///   31.6        corte a la vista del piloto (AVIONETA_POV, en la cabina) mientras la avioneta sigue sobre la avenida.
///   32.6-33.9   la vista voltea 130° a la izquierda, por encima del hombro (lo anima AvionetaEditor en el clip de vuelo).
///   31.6-34.6   Goku llega por atrás, de frente a la cámara, y vuela a la par atrás y a la izquierda (ciclo Goku_Vuelo de Blender).
///   34.0-40.0   sin dejar de avanzar con la avioneta, voltea hacia la cámara y hace el power up; en 37.93 cambia a SSJ.
///   40.4-43.0   vuelve a volar (ya SSJ) y sale disparado: pasa junto a la avioneta y la cámara (CM4_SigueGoku) gira para seguirlo.
/// La posición de Goku se calcula a partir del vuelo de la avioneta (Avioneta_Vuelo.anim): siempre va pegado a ella.
/// Correr ANTES "Godzilla: pasa la avioneta" (genera el vuelo extendido y AVIONETA_POV).
/// </summary>
public static class GokuVueloEditor
{
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string Carpeta = "Assets/Assets/3d/Persoanjes/Goku/";
    const string FbxVuelo = Carpeta + "goku_base_vuelo.fbx";
    const string ClipAvionetaPath = "Assets/Assets/3d/Avioneta/Avioneta_Vuelo.anim";
    const string ClipGokuPath = Carpeta + "Goku_Vuelo.anim";
    const float InicioCiudad = 17.5f;

    const float Inicio = 31.6f, Fin = 43.0f;
    const float Junto = 34.6f;              // ya va volando a la par
    const float PowerUp = 35.0f;            // empieza el power up (sin dejar de avanzar)
    const double Swap = 118.0 / 30.0, LargoPowerUp = 6.0;
    const float Mezcla = 1.0f;              // blend entre vuelo y power up (se endereza con calma)
    const float Agacha = 2.5f;              // u que se hunde justo antes de despegar (impulso)
    const float Despega = 41.8f;            // sale disparado (después del saludo y del impulso con las rodillas)
    const float SaludoIni = 40.2f;          // voltea a la cámara y se lleva dos dedos a la frente; lo lanza en SaludoIni + 1.1
    const float MezclaSaludo = 0.3f;
    const float SubeAlIrse = 0f;            // la salida es en línea recta (ver Calc); antes subía 150 u en curva
    const float BlendSigue = 0.4f;          // la vista del piloto pasa a seguir a Goku (solo rota)

    // Dónde va Goku respecto a la VISTA DEL PILOTO: en la dirección a la que voltea la cámara (GiroGrados), a 'Distancia' u,
    // con su cuerpo a la altura de la cámara (centro del cuerpo: volando ≈ 6 u sobre su raíz; de pie ≈ 8.5 u).
    const float Distancia = 32f, CentroVolando = 6f, CentroDePie = 8.5f, BajoCamara = -3f;   // negativo = arriba de la cámara (contra el cielo)
    const float VolteaVolando = 0.2f;
    const float MasALaIzquierda = 0f;     // grados extra: aleja a Goku del borde del ala de arriba      // durante el vuelo gira ~30° hacia la avioneta para que se le vea la cara
    const float AtrasAlInicio = 140f, AbajoAlInicio = 25f, AdelanteAlFinal = 450f;

    static TC Comp<TC>(GameObject go) where TC : Component { var c = go.GetComponent<TC>(); return c != null ? c : Undo.AddComponent<TC>(go); }
    static AnimationClip ClipDe(string fbx) => AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

    /// 0 -> 1 -> 0 alrededor del despegue: pico 0.15 s antes de Despega
    static float Hundida(float t)
    {
        float k = Mathf.InverseLerp(Despega - 0.4f, Despega + 0.1f, t);
        return k <= 0f || k >= 1f ? 0f : Mathf.Sin(k * Mathf.PI);
    }

    [MenuItem("Tools/Amistad/2 · Ciudad/CM4: primera persona en la avioneta + Goku SSJ", false, 222)]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[CM4] Sal de Play."); return; }
        var log = new StringBuilder();

        // ---------- 0. Importar el ciclo de vuelo (Humanoid, en loop) ----------
        var mi = AssetImporter.GetAtPath(FbxVuelo) as ModelImporter;
        if (mi == null) { Debug.LogError("[CM4] No encuentro " + FbxVuelo); return; }
        mi.globalScale = 8.43f; mi.useFileScale = true;
        mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true; mi.materialImportMode = ModelImporterMaterialImportMode.None;
        var cl = mi.defaultClipAnimations;
        foreach (var c in cl)
        {
            c.name = "Goku_Vuelo"; c.loopTime = true; c.loopPose = true;
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
        }
        mi.clipAnimations = cl;
        mi.SaveAndReimport();
        // Clips corregidos (pies derechos; despegue sin el brazo que subía como saludo): GokuVueloFixEditor
        GokuVueloFixEditor.Corregir();
        var clipVuelo = AssetDatabase.LoadAssetAtPath<AnimationClip>(GokuVueloFixEditor.RutaVueloFix) ?? ClipDe(FbxVuelo);
        var clipDespegue = AssetDatabase.LoadAssetAtPath<AnimationClip>(GokuVueloFixEditor.RutaDespedida)
                           ?? AssetDatabase.LoadAssetAtPath<AnimationClip>(GokuVueloFixEditor.RutaDespegue) ?? clipVuelo;
        var clipSaludo = AssetDatabase.LoadAssetAtPath<AnimationClip>(GokuVueloFixEditor.RutaSaludo);
        var clipBase = ClipDe(Carpeta + "goku_base_powerup.fbx"); var clipSSJ = ClipDe(Carpeta + "goku_ssj_powerup.fbx");
        var clipAv = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipAvionetaPath);

        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var directores = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = directores.FirstOrDefault(d => d.playableAsset == tlP);
        var dirC = directores.FirstOrDefault(d => d.playableAsset == tlC);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var goku = T("GOKU") ?? T("GOKU_PRUEBA"); var av = T("AVIONETA"); var pov = T("AVIONETA_POV"); var cm3 = T("CM3_MEMO");
        var pistaCortes = tlP?.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault();
        var pistaAv = tlC?.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(t => t.name.StartsWith("Avioneta"));
        if (dirP == null || dirC == null || goku == null || av == null || pov == null || cm3 == null || pistaCortes == null || pistaAv == null || clipVuelo == null || clipBase == null || clipSSJ == null || clipAv == null)
        { Debug.LogError("[CM4] Falta algo (GOKU, AVIONETA, AVIONETA_POV, clips o pistas). Corre antes 'Godzilla: pasa la avioneta'."); return; }
        var gBase = goku.Find("Goku_Base"); var gSSJ = goku.Find("Goku_SSJ");
        var vueloAv = pistaAv.GetClips().First();
        float t0Av = (float)vueloAv.start + InicioCiudad;   // tiempo del principal donde empieza el clip de la avioneta

        // ---------- 1. Rumbo de la avioneta en el tiempo ----------
        AnimationCurve CA(string p) => AnimationUtility.GetEditorCurve(clipAv, EditorCurveBinding.FloatCurve("", typeof(Transform), p));
        var ax = CA("m_LocalPosition.x"); var ay = CA("m_LocalPosition.y"); var az = CA("m_LocalPosition.z");
        Vector3 PA(float t) { float l = Mathf.Clamp(t - t0Av, 0, ax.keys.Last().time); return new Vector3(ax.Evaluate(l), ay.Evaluate(l), az.Evaluate(l)); }
        Vector3 Rumbo(float t) { var d = PA(t + 0.05f) - PA(t - 0.05f); d.y = 0; return d.sqrMagnitude > 1e-6f ? d.normalized : Vector3.back; }
        // Cámara del piloto respecto al origen de la avioneta (en su marco: x derecha, y arriba, z adelante)
        var camLocal = Vector3.Scale(pov.localPosition, av.lossyScale);
        float giro = (AvionetaEditor.GiroGrados + MasALaIzquierda) * Mathf.Deg2Rad;      // negativo = izquierda
        float camAdelante = camLocal.z + Distancia * Mathf.Cos(giro), camIzq = -camLocal.x + Distancia * Mathf.Sin(-giro);

        // ---------- 2. Vuelo de Goku pegado a la avioneta ----------
        Undo.RecordObject(goku.gameObject, "CM4"); goku.name = "GOKU";
        Undo.SetTransformParent(goku, null, "CM4");
        var dirViejo = goku.GetComponent<PlayableDirector>(); if (dirViejo != null) { Undo.RecordObject(dirViejo, "CM4"); dirViejo.enabled = false; }
        var animGoku = Comp<Animator>(goku.gameObject);
        float dt = 1f / 30f, t1 = Fin + 0.3f;
        var cx = new AnimationCurve(); var cy = new AnimationCurve(); var cz = new AnimationCurve();
        var qx = new AnimationCurve(); var qy = new AnimationCurve(); var qz = new AnimationCurve(); var qw = new AnimationCurve();
        Quaternion previo = Quaternion.identity;
        // Posición de Goku en el tiempo t (mundo) hasta el despegue (vuela pegado a la avioneta).
        (Vector3 pos, Vector3 f, Vector3 izq, float mira, float sale) CalcBase(float t)
        {
            var f = Rumbo(t); var izq = Vector3.Cross(f, Vector3.up).normalized;   // izquierda del rumbo
            float llega = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Inicio, Junto, t));
            float u = Mathf.InverseLerp(Despega, Fin, t);
            float sale = Mathf.Pow(u, 2.8f);                       // hacia adelante: acelera
            float subeSale = Mathf.Pow(u, 1.9f);                   // hacia arriba: sube antes de acelerar al frente -> curva
            float mira = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(PowerUp, PowerUp + Mezcla, t)) * (1f - Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Despega - Mezcla, Despega, t)));
            float centro = Mathf.Lerp(CentroVolando, CentroDePie, mira);
            float adelante = Mathf.Lerp(-AtrasAlInicio, camAdelante, llega) + sale * AdelanteAlFinal;
            float arriba = Mathf.Lerp(-AbajoAlInicio, camLocal.y - centro - BajoCamara, llega) + 1.2f * Mathf.Sin(t * 2.4f)
                           + subeSale * SubeAlIrse                 // se va subiendo: queda contra el cielo, arriba del ala
                           - Agacha * Hundida(t);                  // se hunde un poco antes de salir disparado
            return (PA(t) + f * adelante + izq * camIzq + Vector3.up * arriba, f, izq, mira, sale);
        }
        // Al irse sale en LÍNEA RECTA hacia adelante: desde donde estaba al despegar, en la dirección de la avioneta en ese
        // instante, con una pendiente leve hacia arriba (Inclinacion). Arranca a la velocidad de la avioneta y acelera (AdelanteAlFinal extra en Despega..Fin).
        var salida0 = CalcBase(Despega);
        const float Inclinacion = 0.22f;        // la línea sube un poco (pendiente) para pasar por encima del ala, no detrás
        var rumbo0 = (Rumbo(Despega) + Vector3.up * Inclinacion).normalized;
        float vAvioneta = (PA(Despega + 0.05f) - PA(Despega - 0.05f)).magnitude / 0.1f;
        (Vector3 pos, Vector3 f, Vector3 izq, float mira, float sale) Calc(float t)
        {
            if (t <= Despega) return CalcBase(t);
            float dtS = t - Despega;
            float sale = Mathf.Pow(dtS / (Fin - Despega), 2.8f);
            return (salida0.pos + rumbo0 * (vAvioneta * dtS + sale * AdelanteAlFinal), rumbo0, salida0.izq, 0f, Mathf.Min(sale, 1f));
        }
        for (float t = Inicio; t <= t1 + 1e-4f; t += dt)
        {
            var (pos, f, izq, mira, sale) = Calc(t);
            // vuela hacia adelante girado un poco hacia la avioneta; en el power up la voltea a ver de frente
            var camMundo = PA(t) + f * camLocal.z - izq * camLocal.x + Vector3.up * camLocal.y;
            var haciaCam = camMundo - pos; haciaCam.y = 0;
            var q = Quaternion.Slerp(Quaternion.LookRotation(f, Vector3.up), Quaternion.LookRotation(haciaCam.normalized, Vector3.up), Mathf.Lerp(VolteaVolando, 0.85f, mira) * (1f - sale));
            // Al irse, el cuerpo apunta hacia donde va (incluye la subida): va inclinado siguiendo la curva
            var vel = Calc(t + 0.05f).pos - Calc(t - 0.05f).pos;
            float wSalida = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(Despega - 0.1f, Despega + 0.35f, t));
            if (wSalida > 0f && vel.sqrMagnitude > 1e-4f) q = Quaternion.Slerp(q, Quaternion.LookRotation(vel.normalized, Vector3.up), wSalida);
            if (Quaternion.Dot(q, previo) < 0) q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
            previo = q;
            float lt = t - Inicio;
            cx.AddKey(lt, pos.x); cy.AddKey(lt, pos.y); cz.AddKey(lt, pos.z);
            qx.AddKey(lt, q.x); qy.AddKey(lt, q.y); qz.AddKey(lt, q.z); qw.AddKey(lt, q.w);
        }
        var clipG = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipGokuPath);
        if (clipG == null) { clipG = new AnimationClip(); AssetDatabase.CreateAsset(clipG, ClipGokuPath); }
        clipG.ClearCurves(); clipG.frameRate = 30;
        void C(string prop, AnimationCurve c) => AnimationUtility.SetEditorCurve(clipG, EditorCurveBinding.FloatCurve("", typeof(Transform), prop), c);
        C("m_LocalPosition.x", cx); C("m_LocalPosition.y", cy); C("m_LocalPosition.z", cz);
        C("m_LocalRotation.x", qx); C("m_LocalRotation.y", qy); C("m_LocalRotation.z", qz); C("m_LocalRotation.w", qw);
        EditorUtility.SetDirty(clipG);

        // ---------- 3. Timeline de la ciudad (grupo Goku) ----------
        Undo.RecordObject(tlC, "CM4"); Undo.RecordObject(dirC, "CM4");
        var grupo = tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "Goku") ?? tlC.CreateTrack<GroupTrack>(null, "Goku");
        TrackAsset Pista<TT>(string nombre) where TT : TrackAsset, new()
        {
            var p = grupo.GetChildTracks().FirstOrDefault(x => x.name == nombre) ?? tlC.CreateTrack<TT>(grupo, nombre);
            foreach (var c in p.GetClips().ToList()) tlC.DeleteClip(c);
            return p;
        }
        double L(double tp) => tp - InicioCiudad;
        TimelineClip Clip(AnimationTrack p, AnimationClip c, double ini, double fin, string nombre)
        {
            var tc = p.CreateClip(c); tc.start = L(ini); tc.duration = fin - ini; tc.displayName = nombre;
            tc.mixInCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);    // mezclas con aceleración y frenado, no lineales
            tc.mixOutCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
            ((AnimationPlayableAsset)tc.asset).applyFootIK = false;   // el IK de pies volvía a torcerlos
            return tc;
        }

        var pV = (AnimationTrack)Pista<AnimationTrack>("Goku · vuelo");
        pV.trackOffset = TrackOffset.ApplyTransformOffsets; pV.position = Vector3.zero; pV.rotation = Quaternion.identity;
        var cV = Clip(pV, clipG, Inicio, t1, "Junto a la avioneta");
        var apa = (AnimationPlayableAsset)cV.asset; apa.removeStartOffset = false; apa.position = Vector3.zero; apa.rotation = Quaternion.identity;
        dirC.SetGenericBinding(pV, animGoku);

        var pB = (AnimationTrack)Pista<AnimationTrack>("Goku base · animación"); pB.trackOffset = TrackOffset.ApplySceneOffsets;
        Clip(pB, clipVuelo, Inicio, PowerUp + Mezcla, "Vuelo");
        Clip(pB, clipBase, PowerUp, PowerUp + LargoPowerUp, "Power up");          // se encima con el vuelo: blend
        dirC.SetGenericBinding(pB, gBase.GetComponent<Animator>());
        var vB = (ActivationTrack)Pista<ActivationTrack>("Goku base · visible");
        var cvb = vB.CreateDefaultClip(); cvb.start = L(Inicio); cvb.duration = PowerUp + Swap - Inicio; cvb.displayName = "base";
        vB.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive; dirC.SetGenericBinding(vB, gBase.gameObject);

        var pS = (AnimationTrack)Pista<AnimationTrack>("Goku SSJ · animación"); pS.trackOffset = TrackOffset.ApplySceneOffsets;
        // Power up -> saludo de dos dedos a la cámara -> lo lanza -> se va volando despidiéndose con la mano
        Clip(pS, clipSSJ, PowerUp, SaludoIni + MezclaSaludo, "Power up SSJ");
        if (clipSaludo != null) Clip(pS, clipSaludo, SaludoIni, SaludoIni + GokuVueloFixEditor.LargoSaludo, "Saludo");
        var clipSalida = AssetDatabase.LoadAssetAtPath<AnimationClip>(GokuVueloFixEditor.RutaVueloSalida) ?? clipVuelo;
        Clip(pS, clipSalida, Despega - MezclaSaludo, t1, "Vuelo SSJ (se va)");   // vuelo corregido con una rodilla doblada
        dirC.SetGenericBinding(pS, gSSJ.GetComponent<Animator>());
        var vS = (ActivationTrack)Pista<ActivationTrack>("Goku SSJ · visible");
        var cvs = vS.CreateDefaultClip(); cvs.start = L(PowerUp + Swap); cvs.duration = t1 - (PowerUp + Swap); cvs.displayName = "SSJ";
        vS.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive; dirC.SetGenericBinding(vS, gSSJ.gameObject);
        foreach (var g in new[] { gBase, gSSJ })
        {
            var an = g.GetComponent<Animator>(); Undo.RecordObject(an, "CM4"); an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            Undo.RecordObject(g.gameObject, "CM4"); g.gameObject.SetActive(false);
        }
        log.AppendLine($"Goku: llega {Inicio}-{Junto}, power up {PowerUp}-{SaludoIni} (SSJ en {PowerUp + Swap:F2}), saludo {SaludoIni}-{SaludoIni + 1.1f:0.0}, despega {Despega}-{Fin}");

        // ---------- 4. Cámara del piloto ----------
        var cam = T("CM4_PrimeraPersona");
        if (cam == null) { var go = new GameObject("CM4_PrimeraPersona"); Undo.RegisterCreatedObjectUndo(go, "CM4"); cam = go.transform; }
        Undo.RecordObject(cam, "CM4"); cam.localScale = Vector3.one;
        var vcam = Comp<CinemachineCamera>(cam.gameObject);
        Undo.RecordObject(vcam, "CM4");
        var lens = cm3.GetComponent<CinemachineCamera>().Lens; lens.FieldOfView = 60f; lens.NearClipPlane = 0.1f;
        vcam.Lens = lens; vcam.Priority.Enabled = false;
        vcam.Target.TrackingTarget = pov; vcam.Target.CustomLookAtTarget = false;
        foreach (var c in cam.GetComponents<CinemachineComponentBase>().Where(c => !(c is CinemachineHardLockToTarget) && !(c is CinemachineRotateWithFollowTarget) && !(c is CinemachineBasicMultiChannelPerlin))) Undo.DestroyObjectImmediate(c);
        Comp<CinemachineHardLockToTarget>(cam.gameObject);
        Comp<CinemachineRotateWithFollowTarget>(cam.gameObject);
        var ruido = Comp<CinemachineBasicMultiChannelPerlin>(cam.gameObject);
        ruido.NoiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>("Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset");
        ruido.AmplitudeGain = 0.35f; ruido.FrequencyGain = 2.5f;     // vibración del motor
        EditorUtility.SetDirty(vcam); EditorUtility.SetDirty(ruido);

        // ---------- 4b. Cámara que sigue a Goku cuando se va: misma posición (cabina), solo rota ----------
        var camS = T("CM4_SigueGoku");
        if (camS == null) { var go = new GameObject("CM4_SigueGoku"); Undo.RegisterCreatedObjectUndo(go, "CM4"); camS = go.transform; }
        Undo.RecordObject(camS, "CM4"); camS.localScale = Vector3.one;
        var vcamS = Comp<CinemachineCamera>(camS.gameObject);
        Undo.RecordObject(vcamS, "CM4");
        vcamS.Lens = vcam.Lens; vcamS.Priority.Enabled = false;
        vcamS.Target.TrackingTarget = pov;                     // posición: la cabina
        vcamS.Target.CustomLookAtTarget = true; vcamS.Target.LookAtTarget = goku;   // rotación: hacia Goku
        foreach (var c in camS.GetComponents<CinemachineComponentBase>().Where(c => !(c is CinemachineHardLockToTarget) && !(c is CinemachineRotationComposer) && !(c is CinemachineBasicMultiChannelPerlin))) Undo.DestroyObjectImmediate(c);
        Comp<CinemachineHardLockToTarget>(camS.gameObject);
        var compS = Comp<CinemachineRotationComposer>(camS.gameObject);
        compS.TargetOffset = new Vector3(0f, CentroVolando, 0f);
        compS.Damping = new Vector2(0.35f, 0.35f);              // como una cabeza que voltea, no un robot
        compS.CenterOnActivate = false;
        var ruidoS = Comp<CinemachineBasicMultiChannelPerlin>(camS.gameObject);
        ruidoS.NoiseProfile = ruido.NoiseProfile; ruidoS.AmplitudeGain = ruido.AmplitudeGain; ruidoS.FrequencyGain = ruido.FrequencyGain;
        EditorUtility.SetDirty(vcamS); EditorUtility.SetDirty(compS); EditorUtility.SetDirty(ruidoS);

        // ---------- 5. Corte ----------
        Undo.RecordObject(pistaCortes, "CM4"); Undo.RecordObject(dirP, "CM4"); Undo.RecordObject(tlP, "CM4");
        var clip = pistaCortes.GetClips().FirstOrDefault(c => c.displayName == "CM4_PrimeraPersona") ?? pistaCortes.CreateClip<CinemachineShot>();
        clip.start = Inicio; clip.duration = (Despega + BlendSigue) - Inicio; clip.displayName = "CM4_PrimeraPersona";
        var shot = (CinemachineShot)clip.asset; Undo.RecordObject(shot, "CM4");
        var er = shot.VirtualCamera;
        if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
        er.defaultValue = null; shot.VirtualCamera = er;
        dirP.SetReferenceValue(er.exposedName, vcam);
        EditorUtility.SetDirty(shot);
        log.AppendLine($"Corte CM4_PrimeraPersona: {Inicio}-{Despega + BlendSigue} s (cámara en AVIONETA_POV)");
        var clipS = pistaCortes.GetClips().FirstOrDefault(c => c.displayName == "CM4_SigueGoku") ?? pistaCortes.CreateClip<CinemachineShot>();
        clipS.start = Despega; clipS.duration = Fin - Despega; clipS.displayName = "CM4_SigueGoku";   // se encima con CM4: blend = la cámara voltea
        var shotS = (CinemachineShot)clipS.asset; Undo.RecordObject(shotS, "CM4");
        var erS = shotS.VirtualCamera;
        if (PropertyName.IsNullOrEmpty(erS.exposedName)) erS.exposedName = GUID.Generate().ToString();
        erS.defaultValue = null; shotS.VirtualCamera = erS;
        dirP.SetReferenceValue(erS.exposedName, vcamS);
        EditorUtility.SetDirty(shotS);
        log.AppendLine($"CM4_SigueGoku: {Despega}-{Fin} s (blend {BlendSigue} s; solo rota hacia Goku)");

        EditorUtility.SetDirty(tlC); EditorUtility.SetDirty(tlP); EditorUtility.SetDirty(pistaCortes); EditorUtility.SetDirty(dirC); EditorUtility.SetDirty(dirP);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(dirP.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        System.IO.File.WriteAllText("Logs/cm4_goku.txt", log.ToString());
        Debug.Log("[CM4] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }
}
