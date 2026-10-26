using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Amistad > Memi y Ale: importar y poner a Memi en lugar de MC
/// 1) Importa "Memi Walking.fbx" y "Ale Strut Walking.fbx" (Mixamo) como Humanoid a la escala de la escena
///    (1 m = 8.43 u, igual que Goku), saca su textura embebida y deja la caminata en loop, en su lugar.
/// 2) En ciudad.unity pone a Memi exactamente donde estaba MC (MEMO_PERFIL): mismo padre, posición y giro,
///    mismo Animator Controller (copia MEMI.controller con la caminata de Memi), mismas pistas del Timeline
///    (recorrido, activación…), y le pasa MIRA_CM3 a la altura de su cabeza para que CM3_MEMO lo siga.
///    El nuevo objeto se llama MEMO_PERFIL (así lo buscan las otras herramientas). MC queda apagado como MC_REFERENCIA.
/// 3) Agrega a Ale (objeto ALE, apagado) junto a Memi, con su caminata en loop, listo para acomodarlo en su toma.
/// Se puede volver a correr: deshace la sustitución anterior y la rehace.
/// </summary>
public static class MemiAleEditor
{
    const string Carpeta = "Assets/Assets/3d/Persoanjes";
    const string FbxMemi = Carpeta + "/Memi Walking.fbx";
    const string FbxAle = Carpeta + "/Ale Strut Walking.fbx";
    const string CarpetaTex = Carpeta + "/Texturas_MemiAle";
    const string FbxMC = Carpeta + "/Master Chief@Walking.fbx";
    const string CtrlMC = "Assets/Animaciones/MEMO_PERFIL.controller";
    const string CtrlMemi = "Assets/Animaciones/MEMI.controller";
    const string CtrlAle = "Assets/Animaciones/ALE.controller";
    const string Escena = "Assets/Scenes/ciudad.unity";
    const float AltoObjetivo = 17f;   // mismo alto que MC (17 u = 2 m), así la toma de CM3_MEMO no cambia

    [MenuItem("Tools/Amistad/Memi y Ale: importar y poner a Memi en lugar de MC")]
    static void Aplicar()
    {
        var log = new StringBuilder();

        // ---------- 1. Importación ----------
        AssetDatabase.Refresh();
        if (!System.IO.Directory.Exists(CarpetaTex)) { System.IO.Directory.CreateDirectory(CarpetaTex); AssetDatabase.Refresh(); }
        var clipMemi = Importar(FbxMemi, "Memi_Caminata", log);
        var clipAle = Importar(FbxAle, "Ale_Caminata", log);
        if (clipMemi == null || clipAle == null) { Fin(log, "Faltó importar un FBX."); return; }

        // ---------- 2. Controllers ----------
        if (AssetDatabase.LoadAssetAtPath<AnimatorController>(CtrlMemi) == null) AssetDatabase.CopyAsset(CtrlMC, CtrlMemi);
        var ctrlMemi = AssetDatabase.LoadAssetAtPath<AnimatorController>(CtrlMemi);
        if (ctrlMemi == null) { Fin(log, "No pude copiar " + CtrlMC); return; }
        int cambiados = 0;
        foreach (var capa in ctrlMemi.layers)
            foreach (var st in capa.stateMachine.states)
                if (st.state.motion is AnimationClip c && c.name.ToLower().Contains("walk")) { st.state.motion = clipMemi; cambiados++; }
        if (cambiados == 0) { var s = ctrlMemi.layers[0].stateMachine.defaultState; if (s != null) { s.motion = clipMemi; cambiados++; } }
        EditorUtility.SetDirty(ctrlMemi);
        log.AppendLine($"MEMI.controller: {cambiados} estado(s) con la caminata de Memi");

        var ctrlAle = AssetDatabase.LoadAssetAtPath<AnimatorController>(CtrlAle);
        if (ctrlAle == null) ctrlAle = AnimatorController.CreateAnimatorControllerAtPathWithClip(CtrlAle, clipAle);
        else { ctrlAle.layers[0].stateMachine.defaultState.motion = clipAle; EditorUtility.SetDirty(ctrlAle); }
        AssetDatabase.SaveAssets();

        // ---------- 3. Escena ----------
        var escena = SceneManager.GetActiveScene();
        if (escena.path != Escena)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            escena = EditorSceneManager.OpenScene(Escena);
        }
        var todos = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);

        var mcRef = T("MC_REFERENCIA");
        var actual = T("MEMO_PERFIL");
        if (actual == null) { Fin(log, "No encuentro MEMO_PERFIL en la escena."); return; }
        string Fuente(Transform t) { var f = PrefabUtility.GetCorrespondingObjectFromOriginalSource(t.gameObject); return f != null ? AssetDatabase.GetAssetPath(f) : ""; }
        bool yaEsMemi = Fuente(actual) != FbxMC;
        // Si MC_REFERENCIA no es MC (quedó una copia de Memi de otra corrida), se borra y se vuelve a crear con MC
        if (mcRef != null && Fuente(mcRef) != FbxMC) { Undo.DestroyObjectImmediate(mcRef.gameObject); mcRef = null; log.AppendLine("Se quitó un MC_REFERENCIA que no era MC."); }
        log.AppendLine($"MEMO_PERFIL viene de: {Fuente(actual)}");
        var ale0 = T("ALE"); if (ale0 != null) Undo.DestroyObjectImmediate(ale0.gameObject);

        GameObject memi;
        if (yaEsMemi)
        {
            memi = actual.gameObject;
            log.AppendLine("MEMO_PERFIL ya es Memi: solo se ajusta.");
            if (mcRef == null)   // MC se perdió en la corrida anterior: se vuelve a poner como referencia apagada
            {
                var mcGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxMC), escena);
                Undo.RegisterCreatedObjectUndo(mcGo, "MC");
                mcGo.name = "MC_REFERENCIA";
                mcGo.transform.SetParent(memi.transform.parent, false);
                mcGo.transform.SetSiblingIndex(memi.transform.GetSiblingIndex());
                mcGo.transform.localPosition = memi.transform.localPosition;
                mcGo.transform.localRotation = memi.transform.localRotation;
                var anR = mcGo.GetComponent<Animator>(); if (anR == null) anR = mcGo.AddComponent<Animator>();
                anR.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(CtrlMC);
                anR.applyRootMotion = false;
                mcGo.SetActive(false);
                log.AppendLine("MC_REFERENCIA recreado (apagado).");
            }
        }
        else
        {
            var mc = actual;
            memi = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxMemi), escena);
            Undo.RegisterCreatedObjectUndo(memi, "Memi");
            memi.name = "MEMO_PERFIL";
            memi.transform.SetParent(mc.parent, false);
            memi.transform.SetSiblingIndex(mc.GetSiblingIndex() + 1);
            memi.transform.localPosition = mc.localPosition;
            memi.transform.localRotation = mc.localRotation;
            memi.transform.localScale = mc.localScale;
            var anMC = mc.GetComponent<Animator>();
            var anN = memi.GetComponent<Animator>(); if (anN == null) anN = memi.AddComponent<Animator>();
            if (anMC != null) { anN.applyRootMotion = anMC.applyRootMotion; anN.cullingMode = anMC.cullingMode; anN.updateMode = anMC.updateMode; }
            foreach (var comp in mc.GetComponents<Component>())
            {
                if (comp == null || comp is Transform || comp is Animator) continue;
                if (UnityEditorInternal.ComponentUtility.CopyComponent(comp)) UnityEditorInternal.ComponentUtility.PasteComponentAsNew(memi);
                log.AppendLine("  copiado a Memi: " + comp.GetType().Name);
            }
            var miraMC = mc.Find("MIRA_CM3");
            if (miraMC != null) Undo.SetTransformParent(miraMC, memi.transform, "Memi");
            Reenlazar(escena, mc.gameObject, memi, log);
            Undo.RecordObject(mc.gameObject, "Memi");
            mc.name = "MC_REFERENCIA";
            mc.gameObject.SetActive(false);
        }

        var an = memi.GetComponent<Animator>(); if (an == null) an = memi.AddComponent<Animator>();
        an.runtimeAnimatorController = ctrlMemi;
        Undo.RecordObject(memi.transform, "Memi");
        memi.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
        float altoMC = 17f;
        AjustarAlto(memi, log);
        float altoMemi = Alto(memi);
        var mira = memi.transform.Find("MIRA_CM3");
        if (mira != null) { Undo.RecordObject(mira, "Memi"); mira.localPosition = Vector3.zero; mira.position = memi.transform.position + Vector3.up * (altoMemi * 0.91f); }
        else log.AppendLine("¡Ojo! Memi no tiene MIRA_CM3.");

        var ale = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(FbxAle), escena);
        Undo.RegisterCreatedObjectUndo(ale, "Ale");
        ale.name = "ALE";
        ale.transform.SetParent(memi.transform.parent, false);
        ale.transform.SetSiblingIndex(memi.transform.GetSiblingIndex() + 1);
        ale.transform.localRotation = memi.transform.localRotation;
        ale.transform.position = memi.transform.position + memi.transform.right * (altoMemi * 0.6f);
        var anAle = ale.GetComponent<Animator>(); if (anAle == null) anAle = ale.AddComponent<Animator>();
        anAle.runtimeAnimatorController = ctrlAle;
        anAle.applyRootMotion = false;
        AjustarAlto(ale, log);
        ale.SetActive(false);

        log.AppendLine($"\nAlturas: MC (ref) {altoMC:0.0} u · Memi {altoMemi:0.0} u · Ale {Alto(ale, true):0.0} u");

        EditorSceneManager.MarkSceneDirty(escena);
        EditorSceneManager.SaveScene(escena);
        AssetDatabase.SaveAssets();
        Fin(log, null);
    }

    static AnimationClip Importar(string ruta, string nombreClip, StringBuilder log)
    {
        var mi = AssetImporter.GetAtPath(ruta) as ModelImporter;
        if (mi == null) { log.AppendLine("No encuentro " + ruta); return null; }
        if (System.IO.Directory.GetFiles(CarpetaTex, "*.png").Length == 0) mi.ExtractTextures(CarpetaTex);
        AssetDatabase.Refresh();
        mi.useFileScale = true;
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.importAnimation = true;
        mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
        mi.materialSearch = ModelImporterMaterialSearch.Everywhere;
        mi.SaveAndReimport();   // primero para que exista el avatar y los clips por defecto
        var clips = mi.defaultClipAnimations;
        foreach (var c in clips)
        {
            c.name = nombreClip;
            c.loopTime = true; c.loopPose = true;
            c.lockRootRotation = true; c.keepOriginalOrientation = true;
            c.rotationOffset = 0f;   // la pista "MC · recorrido" ya gira la raíz 180° hacia la cámara
            c.lockRootHeightY = true; c.keepOriginalPositionY = true;
            c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;   // camina en su lugar; el recorrido lo da el Timeline
        }
        mi.clipAnimations = clips;
        mi.SaveAndReimport();
        // Tamaño: se mide el modelo y se ajusta el Scale Factor para que mida lo mismo que MC
        float medido = AltoAsset(ruta);
        if (medido > 0.01f && Mathf.Abs(medido - AltoObjetivo) > 0.2f)
        {
            mi.globalScale = mi.globalScale * AltoObjetivo / medido;
            mi.SaveAndReimport();
        }
        RegenerarAvatar(mi, log);
        log.AppendLine($"  {System.IO.Path.GetFileName(ruta)}: Scale Factor {mi.globalScale:0.#####} → alto {AltoAsset(ruta):0.0} u");
        var clip = AssetDatabase.LoadAllAssetsAtPath(ruta).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
        var mats = AssetDatabase.LoadAllAssetsAtPath(ruta).OfType<Material>().ToList();
        int conTex = mats.Count(m => (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null) || (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null));
        log.AppendLine($"{System.IO.Path.GetFileName(ruta)}: Humanoid {(mi.animationType == ModelImporterAnimationType.Human ? "OK" : "NO")}, clip {clip?.name}, materiales {mats.Count} ({conTex} con textura)");
        return clip;
    }

    /// El avatar Humanoid guarda las posiciones de los huesos; si cambia el Scale Factor quedan viejas y el personaje
    /// se deforma en Play ("Bone length in configuration does not match"). Se rehace pasando por Generic.
    static void RegenerarAvatar(ModelImporter mi, StringBuilder log)
    {
        var clips = mi.clipAnimations;
        mi.animationType = ModelImporterAnimationType.Generic;
        mi.SaveAndReimport();
        mi.animationType = ModelImporterAnimationType.Human;
        mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        mi.SaveAndReimport();
        foreach (var c in clips) c.rotationOffset = 0f;
        mi.clipAnimations = clips;
        mi.SaveAndReimport();
        var av = AssetDatabase.LoadAllAssetsAtPath(mi.assetPath).OfType<Avatar>().FirstOrDefault();
        log.AppendLine($"  avatar rehecho: {(av != null && av.isValid && av.isHuman ? "OK" : "¡con problemas!")}");
    }

    /// Escala el objeto en la escena para que mida AltoObjetivo en el mundo (CITY tiene escala propia).
    static void AjustarAlto(GameObject go, StringBuilder log)
    {
        float h = Alto(go, true);
        if (h < 0.001f) return;
        Undo.RecordObject(go.transform, "Memi");
        go.transform.localScale = go.transform.localScale * (AltoObjetivo / h);
        log.AppendLine($"  {go.name}: escala local {go.transform.localScale.x:0.####} → alto {Alto(go, true):0.0} u");
    }

    static float AltoAsset(string ruta)
    {
        var p = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
        if (p == null) return 0f;
        var tmp = (GameObject)Object.Instantiate(p);
        tmp.hideFlags = HideFlags.HideAndDontSave;
        tmp.transform.position = Vector3.zero; tmp.transform.rotation = Quaternion.identity; tmp.transform.localScale = Vector3.one;
        float h = Alto(tmp, true);
        Object.DestroyImmediate(tmp);
        return h;
    }

    static float Alto(GameObject go, bool incluirApagados = false)
    {
        var rs = go.GetComponentsInChildren<Renderer>(incluirApagados);
        if (rs.Length == 0) return 0f;
        var bb = rs[0].bounds; foreach (var r in rs) bb.Encapsulate(r.bounds);
        return bb.size.y;
    }

    /// Cambia toda referencia de la escena (pistas de Timeline, cámaras, scripts) del objeto viejo al nuevo.
    static void Reenlazar(Scene escena, GameObject viejo, GameObject nuevo, StringBuilder log)
    {
        var mapa = new Dictionary<Object, Object> { { viejo, nuevo }, { viejo.transform, nuevo.transform } };
        var anV = viejo.GetComponent<Animator>(); var anN = nuevo.GetComponent<Animator>();
        if (anV != null && anN != null) mapa[anV] = anN;

        int pistas = 0, refs = 0;
        var comps = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true))
            .Where(c => c != null && !c.transform.IsChildOf(viejo.transform) && !c.transform.IsChildOf(nuevo.transform)).ToList();

        foreach (var dir in comps.OfType<PlayableDirector>())
        {
            if (dir.playableAsset == null) continue;
            foreach (var salida in dir.playableAsset.outputs)
            {
                if (salida.sourceObject == null) continue;
                var b = dir.GetGenericBinding(salida.sourceObject);
                if (b != null && mapa.TryGetValue(b, out var n))
                { Undo.RecordObject(dir, "Memi"); dir.SetGenericBinding(salida.sourceObject, n); pistas++;
                  log.AppendLine($"  pista {dir.name} / {salida.streamName} → {nuevo.name}"); }
            }
            EditorUtility.SetDirty(dir);
        }
        foreach (var c in comps)
        {
            if (c is PlayableDirector || c is Transform) continue;
            var so = new SerializedObject(c);
            var it = so.GetIterator();
            bool cambio = false;
            while (it.Next(true))
            {
                if (it.propertyType != SerializedPropertyType.ObjectReference || it.objectReferenceValue == null) continue;
                if (mapa.TryGetValue(it.objectReferenceValue, out var n)) { it.objectReferenceValue = n; cambio = true; refs++;
                    log.AppendLine($"  referencia {c.gameObject.name}.{c.GetType().Name}.{it.propertyPath} → {nuevo.name}"); }
            }
            if (cambio) { Undo.RecordObject(c, "Memi"); so.ApplyModifiedProperties(); }
        }
        log.AppendLine($"Reenlazado {viejo.name} → {nuevo.name}: {pistas} pista(s), {refs} referencia(s)");
    }

    static void Fin(StringBuilder log, string error)
    {
        if (error != null) log.AppendLine("\n¡" + error + "!");
        Debug.Log("[Memi y Ale]\n" + log);
        System.IO.File.WriteAllText("Logs/MemiAle.txt", log.ToString());
        EditorUtility.DisplayDialog("Memi y Ale", (error ?? "Listo.") + "\n\nDetalle en la consola.", "OK");
    }
}
