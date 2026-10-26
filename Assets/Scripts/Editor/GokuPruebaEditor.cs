using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > Goku: prueba de transformaciones
/// 1) Importa los 4 FBX de Goku como Humanoid a la escala de la escena (MC: 17 u = 2 m).
/// 2) Pone a Goku base, SSJ y SSJ3 en el mismo lugar de CITY, fuera de la toma de CM3_MEMO
///    (detrás de donde termina el dolly) y lejos de MC y de Godzilla.
/// 3) Crea un Timeline propio (GOKU_PRUEBA, Play On Awake apagado) con los dos clips y los cambios de modelo:
///    0–6 s base→SSJ (cambio en 3.933 s) · 6–12 s SSJ→SSJ3 (cambio en 9.933 s).
/// No toca el Timeline de la película. Se puede volver a correr (borra y recrea GOKU_PRUEBA).
/// </summary>
public static class GokuPruebaEditor
{
    const string Carpeta = "Assets/Assets/3d/Persoanjes/Goku";
    const string FbxBase = Carpeta + "/goku_base_powerup.fbx";
    const string FbxSSJ = Carpeta + "/goku_ssj_powerup.fbx";
    const string FbxSSJ_a3 = Carpeta + "/goku_ssj_aSSJ3.fbx";
    const string FbxSSJ3_a3 = Carpeta + "/goku_ssj3_aSSJ3.fbx";
    const string RutaTimeline = Carpeta + "/Goku_PruebaTimeline.playable";
    const string CarpetaTex = Carpeta + "/Texturas";        // Texturas/<personaje>/*.png + <personaje>_materiales.txt
    const string CarpetaMat = Carpeta + "/Materiales";      // se crean aquí (URP Lit)

    // Qué juego de texturas usa cada FBX
    static readonly Dictionary<string, string> Personaje = new Dictionary<string, string>
    {
        { FbxBase, "base" }, { FbxSSJ, "ssj" }, { FbxSSJ_a3, "ssj" }, { FbxSSJ3_a3, "ssj3" },
    };

    /// Crea (o actualiza) los materiales URP de un personaje a partir de su tabla material|textura|color.
    static Dictionary<string, Material> Materiales(string ch, StringBuilder log)
    {
        var res = new Dictionary<string, Material>();
        var tabla = System.IO.Path.Combine(Application.dataPath, "..", CarpetaTex, ch, ch + "_materiales.txt");
        if (!System.IO.File.Exists(tabla)) { log.AppendLine("Falta " + tabla); return res; }
        var dirMat = CarpetaMat + "/" + ch;
        System.IO.Directory.CreateDirectory(System.IO.Path.Combine(Application.dataPath, "..", dirMat));
        AssetDatabase.Refresh();
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        foreach (var linea in System.IO.File.ReadAllLines(tabla))
        {
            if (string.IsNullOrWhiteSpace(linea)) continue;
            var p = linea.Split('|');
            var nombre = p[0];
            var ruta = dirMat + "/" + nombre + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, ruta); }
            else mat.shader = lit;
            var c = p[2].Split(',').Select(v => float.Parse(v, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            mat.SetColor("_BaseColor", new Color(c[0], c[1], c[2], 1f));
            Texture2D tex = null;
            if (p[1].Length > 0) tex = AssetDatabase.LoadAssetAtPath<Texture2D>(CarpetaTex + "/" + ch + "/" + p[1]);
            mat.SetTexture("_BaseMap", tex);
            mat.SetTexture("_MainTex", tex);
            mat.SetFloat("_Smoothness", 0.15f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            res[nombre] = mat;
            if (p[1].Length > 0 && tex == null) log.AppendLine($"  ¡no encontré la textura {p[1]} para {nombre}!");
        }
        AssetDatabase.SaveAssets();
        log.AppendLine($"Materiales {ch}: {res.Count}");
        return res;
    }

    const float Escala = 8.43f;          // 1 m = 8.43 u (MC 17 u = 2 m) → Goku 1.8 m ≈ 15.2 u
    const double Swap = 118.0 / 30.0;    // 3.933 s
    const double Largo = 6.0;            // cada clip dura 6 s (0–180 a 30 fps)

    [MenuItem("Tools/Amistad/Goku: prueba de transformaciones")]
    static void Aplicar()
    {
        var log = new StringBuilder();

        // ---------- 1. Importación ----------
        var nombres = new Dictionary<string, string>
        {
            { FbxBase, "Goku_Base_PowerUp_SSJ" },
            { FbxSSJ, "Goku_SSJ_PowerUp_SSJ" },
            { FbxSSJ_a3, "Goku_SSJ_PowerUp_SSJ3" },
            { FbxSSJ3_a3, "Goku_SSJ3_PowerUp_SSJ3" },
        };
        AssetDatabase.Refresh();
        var mats = new Dictionary<string, Dictionary<string, Material>>();
        foreach (var ch in Personaje.Values.Distinct()) mats[ch] = Materiales(ch, log);
        foreach (var kv in nombres)
        {
            var mi = AssetImporter.GetAtPath(kv.Key) as ModelImporter;
            if (mi == null) { EditorUtility.DisplayDialog("Goku", "No encuentro " + kv.Key + ".\nHaz Assets > Refresh y vuelve a correr.", "OK"); return; }
            mi.globalScale = Escala;
            mi.useFileScale = true;
            mi.animationType = ModelImporterAnimationType.Human;
            mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true;
            mi.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips)
            {
                c.name = kv.Value;
                c.loopTime = false;
                c.lockRootRotation = true; c.keepOriginalOrientation = true;
                c.lockRootHeightY = true; c.keepOriginalPositionY = true;
                c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true;
            }
            mi.clipAnimations = clips;
            int remaps = 0;
            foreach (var m in mats[Personaje[kv.Key]])
            {
                mi.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), m.Key), m.Value);
                remaps++;
            }
            mi.SaveAndReimport();
            log.AppendLine($"  {kv.Key}: {remaps} materiales asignados");
            var avatar = AssetDatabase.LoadAllAssetsAtPath(kv.Key).OfType<Avatar>().FirstOrDefault();
            var clip = Clip(kv.Key);
            log.AppendLine($"{kv.Key}: avatar humano={(avatar != null && avatar.isHuman)} válido={(avatar != null && avatar.isValid)} clip={(clip ? clip.name + " " + clip.length.ToString("0.000") + " s" : "NINGUNO")}");
        }

        // ---------- 2. Lugar en CITY ----------
        var escena = SceneManager.GetActiveScene();
        var todos = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var city = T("CITY"); var memo = T("MEMO_PERFIL"); var cm3 = T("CM3_MEMO");
        var spline = T("Spline_P2_Ciudad"); var godzilla = T("GODZILLA"); var edificio = T("DERRUMBE_ruim008");

        Vector3 suelo = memo != null ? memo.position : Vector3.zero;
        Vector3 hacia = Vector3.back;     // de MC hacia la cámara
        Vector3 finCamara = suelo;
        if (spline != null && memo != null)
        {
            var sc = spline.GetComponent<UnityEngine.Splines.SplineContainer>();
            if (sc != null && sc.Spline.Count >= 2)
            {
                var a = spline.TransformPoint((Vector3)sc.Spline[0].Position);
                var b = spline.TransformPoint((Vector3)sc.Spline[sc.Spline.Count - 1].Position);
                hacia = b - a; hacia.y = 0; hacia.Normalize();
                finCamara = b;
            }
        }
        // Detrás de donde termina la cámara (la cámara mira hacia MC, no hacia atrás) y un poco a un lado.
        var lado = Vector3.Cross(Vector3.up, hacia).normalized;
        var lugar = finCamara + hacia * 35f + lado * 15f;
        lugar.y = suelo.y;
        // Si hay colliders, apoyarlo en el piso.
        if (Physics.Raycast(lugar + Vector3.up * 200f, Vector3.down, out var hit, 400f)) lugar.y = hit.point.y;

        log.AppendLine($"MC: {(memo ? memo.position.ToString() : "-")}  CM3: {(cm3 ? cm3.position.ToString() : "-")}  Godzilla: {(godzilla ? godzilla.position.ToString() : "-")}  Edificio: {(edificio ? edificio.position.ToString() : "-")}");
        log.AppendLine($"Cámara termina en {finCamara}, retrocede hacia {hacia}. Goku en {lugar}");
        if (memo) log.AppendLine($"  distancia a MC: {Vector3.Distance(lugar, memo.position):0.0} u");
        if (godzilla) log.AppendLine($"  distancia a Godzilla (objeto): {Vector3.Distance(lugar, godzilla.position):0.0} u");

        // ---------- 3. Objetos ----------
        var viejo = T("GOKU_PRUEBA");
        if (viejo != null) Undo.DestroyObjectImmediate(viejo.gameObject);
        var raiz = new GameObject("GOKU_PRUEBA");
        Undo.RegisterCreatedObjectUndo(raiz, "Goku prueba");
        if (city != null && (city.lossyScale - Vector3.one).sqrMagnitude < 1e-4f) raiz.transform.SetParent(city, false);
        raiz.transform.position = lugar;
        raiz.transform.rotation = Quaternion.LookRotation(-hacia, Vector3.up);   // mirando hacia la ciudad

        GameObject Poner(string ruta, string nombre, bool activo)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ruta);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, raiz.transform);
            go.name = nombre;
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            var an = go.GetComponent<Animator>() ?? go.AddComponent<Animator>();
            an.runtimeAnimatorController = null;
            an.applyRootMotion = false;
            an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            go.SetActive(activo);
            return go;
        }
        var gBase = Poner(FbxBase, "Goku_Base", true);
        var gSSJ = Poner(FbxSSJ, "Goku_SSJ", false);
        var gSSJ3 = Poner(FbxSSJ3_a3, "Goku_SSJ3", false);

        // ---------- 4. Timeline ----------
        AssetDatabase.DeleteAsset(RutaTimeline);
        var tl = ScriptableObject.CreateInstance<TimelineAsset>();
        AssetDatabase.CreateAsset(tl, RutaTimeline);
        var dir = raiz.AddComponent<PlayableDirector>();
        dir.playableAsset = tl;
        dir.playOnAwake = false;
        dir.extrapolationMode = DirectorWrapMode.Hold;

        void Grupo(string nombre, GameObject go, (string fbx, double inicio)[] clips, double visibleDesde, double visibleHasta)
        {
            var g = tl.CreateTrack<GroupTrack>(null, nombre);
            var anim = tl.CreateTrack<AnimationTrack>(g, nombre + " · animación");
            anim.trackOffset = TrackOffset.ApplySceneOffsets;
            foreach (var (fbx, inicio) in clips)
            {
                var c = anim.CreateClip(Clip(fbx));
                c.start = inicio;
                c.duration = Largo;
                c.displayName = Clip(fbx).name;
            }
            dir.SetGenericBinding(anim, go.GetComponent<Animator>());
            var act = tl.CreateTrack<ActivationTrack>(g, nombre + " · visible");
            act.postPlaybackState = ActivationTrack.PostPlaybackState.Revert;
            var ac = act.CreateDefaultClip();
            ac.start = visibleDesde;
            ac.duration = visibleHasta - visibleDesde;
            ac.displayName = "visible";
            dir.SetGenericBinding(act, go);
        }
        Grupo("Goku base", gBase, new[] { (FbxBase, 0.0) }, 0, Swap);
        Grupo("Goku SSJ", gSSJ, new[] { (FbxSSJ, 0.0), (FbxSSJ_a3, Largo) }, Swap, Largo + Swap);
        Grupo("Goku SSJ3", gSSJ3, new[] { (FbxSSJ3_a3, Largo) }, Largo + Swap, 2 * Largo);
        EditorUtility.SetDirty(tl);
        AssetDatabase.SaveAssets();

        // ---------- 5. Mostrarlo ----------
        EditorSceneManager.MarkSceneDirty(escena);
        Selection.activeGameObject = raiz;
        dir.time = 0; dir.Evaluate();
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            var b = new Bounds(lugar + Vector3.up * 8f, new Vector3(20f, 20f, 20f));
            sv.Frame(b, false);
        }
        System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "../Logs/goku_prueba.txt"), log.ToString());
        Debug.Log("[Goku prueba] listo. Selecciona GOKU_PRUEBA y mueve el cabezal en la ventana Timeline (0–12 s). Detalles en Logs/goku_prueba.txt\n" + log);
    }

    static AnimationClip Clip(string ruta) =>
        AssetDatabase.LoadAllAssetsAtPath(ruta).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
}
