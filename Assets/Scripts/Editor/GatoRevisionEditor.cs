using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Amistad > Gato: revisar modelo
///   Revisa el gato actual de CM1 (instancia de cat.fbx en la escena): jerarquía, malla, materiales, texturas, huesos, Animator.
///   Log en Logs/gato/info.txt y fotos en Logs/gato/vista_*.png.
/// Tools > Amistad > Gato: revisar Kitty (ithappy)
///   Revisa Assets/ithappy/Animals_FREE/Prefabs/Kitty_001.prefab y lo compara en tamaño con el gato actual.
///   Log en Logs/gato/kitty_info.txt; fotos en Logs/gato/kitty_vista_*.png y poses de cada clip en Logs/gato/kitty_<clip>_<n>.png.
/// Ambas usan copias temporales (HideAndDontSave): no tocan la escena. Se pueden volver a correr.
/// </summary>
public static class GatoRevisionEditor
{
    const string Fbx = "Assets/Assets/3d/Persoanjes/cat.fbx";
    const string KittyPrefab = "Assets/ithappy/Animals_FREE/Prefabs/Kitty_001.prefab";
    const string KittyFbx = "Assets/ithappy/Animals_FREE/Meshes/Kitty_001.fbx";
    const int Capa = 31, Res = 640;

    static Transform GatoActual()
    {
        var fuente = AssetDatabase.LoadAssetAtPath<GameObject>(Fbx);
        return Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault(t => PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == fuente);
    }

    static string Ruta(Transform t, Transform raiz) => t == raiz ? t.name : Ruta(t.parent, raiz) + "/" + t.name;

    static void Describir(Transform g, StringBuilder log, bool jerarquiaCompleta)
    {
        log.AppendLine($"== {g.name} pos {g.position} rot {g.eulerAngles} escala {g.lossyScale}");
        var ts = g.GetComponentsInChildren<Transform>(true);
        log.AppendLine($"   {ts.Length} transforms");
        foreach (var t in ts.Take(jerarquiaCompleta ? 200 : 20))
            log.AppendLine($"   {new string(' ', 2 * Ruta(t, g).Count(c => c == '/'))}{t.name} [{string.Join(", ", t.GetComponents<Component>().Where(c => !(c is Transform)).Select(c => c.GetType().Name))}]");
        var an = g.GetComponentInChildren<Animator>(true);
        if (an != null)
        {
            var rc = an.runtimeAnimatorController;
            log.AppendLine($"   Animator: controller {(rc ? rc.name : "-")}, avatar {(an.avatar ? an.avatar.name + (an.avatar.isHuman ? " (humano)" : " (generic)") : "-")}, applyRootMotion {an.applyRootMotion}");
            if (rc) foreach (var c in rc.animationClips.Distinct())
                    log.AppendLine($"      clip {c.name}: {c.length:0.00}s, {c.frameRate} fps, loop {c.isLooping}, {AnimationUtility.GetCurveBindings(c).Length} curvas, {AssetDatabase.GetAssetPath(c)}");
            if (rc is UnityEditor.Animations.AnimatorController ac) log.AppendLine("      parámetros: " + string.Join(", ", ac.parameters.Select(p => $"{p.name}({p.type})")));
        }
        var b = Limites(g);
        log.AppendLine($"   bounds centro {b.center} tamaño {b.size}");
        foreach (var r in g.GetComponentsInChildren<Renderer>(true))
        {
            Mesh m = r is SkinnedMeshRenderer s ? s.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
            if (m != null)
                log.AppendLine($"   Malla {m.name} ({r.GetType().Name}): {m.vertexCount} vértices, {m.triangles.Length / 3} triángulos, {m.subMeshCount} submallas, UV0={m.uv.Length > 0}, colores de vértice={m.colors.Length > 0}, blendshapes={m.blendShapeCount}");
            if (r is SkinnedMeshRenderer sk) log.AppendLine($"   Skinned: {sk.bones.Length} huesos, root {(sk.rootBone ? sk.rootBone.name : "-")}, quality {sk.quality}");
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) { log.AppendLine("   Material: (vacío)"); continue; }
                var texs = mat.GetTexturePropertyNames().Select(p => (p, t: mat.GetTexture(p))).Where(x => x.t != null)
                    .Select(x => $"{x.p}={x.t.name} {x.t.width}x{x.t.height} ({AssetDatabase.GetAssetPath(x.t)})");
                var col = mat.HasProperty("_BaseColor") ? mat.GetColor("_BaseColor") : mat.HasProperty("_Color") ? mat.GetColor("_Color") : Color.clear;
                log.AppendLine($"   Material {mat.name} ({AssetDatabase.GetAssetPath(mat)}): shader {mat.shader.name} (soportado={mat.shader.isSupported}), color {col}, texturas: {(texs.Any() ? string.Join("; ", texs) : "ninguna")}");
            }
        }
    }

    static Bounds Limites(Transform g)
    {
        var rs = g.GetComponentsInChildren<Renderer>(true);
        if (rs.Length == 0) return new Bounds(g.position, Vector3.zero);
        var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds); return b;
    }

    static Transform CopiaTemporal(GameObject original, Vector3 pos, Quaternion rot)
    {
        var go = Object.Instantiate(original, pos, rot);
        foreach (var t in go.GetComponentsInChildren<Transform>(true)) { t.gameObject.hideFlags = HideFlags.HideAndDontSave; t.gameObject.layer = Capa; }
        go.SetActive(true);
        return go.transform;
    }

    /// Copia fija (MeshRenderer) de la pose actual de cada SkinnedMeshRenderer de 'g'. Los skinned originales se apagan.
    static Transform HornearPose(Transform g)
    {
        var raiz = new GameObject("__PoseGato") { hideFlags = HideFlags.HideAndDontSave, layer = Capa };
        foreach (var sk in g.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            var m = new Mesh { hideFlags = HideFlags.HideAndDontSave };
            sk.enabled = true; sk.BakeMesh(m, true);
            var go = new GameObject(sk.name) { hideFlags = HideFlags.HideAndDontSave, layer = Capa };
            go.transform.SetParent(raiz.transform, false);
            go.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            go.AddComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
            sk.enabled = false;
        }
        // HornearPose deja una sola malla hija; la raíz guarda su MeshFilter para poder destruirla
        var hija = raiz.transform.childCount > 0 ? raiz.transform.GetChild(0).gameObject : null;
        if (hija != null) { var mf = raiz.AddComponent<MeshFilter>(); mf.sharedMesh = hija.GetComponent<MeshFilter>().sharedMesh; }
        return raiz.transform;
    }

    /// Renderiza 'g' desde 'dir' (mundo) encuadrando 'b'. Devuelve el PNG.
    static byte[] Foto(Transform g, Bounds b, Vector3 dir)
    {
        var go = new GameObject("__CamGato") { hideFlags = HideFlags.HideAndDontSave };
        var luzGo = new GameObject("__LuzGato") { hideFlags = HideFlags.HideAndDontSave };
        var rt = new RenderTexture(Res, Res, 24); var tex = new Texture2D(Res, Res, TextureFormat.RGB24, false);
        try
        {
            var cam = go.AddComponent<Camera>();
            cam.fieldOfView = 30; cam.nearClipPlane = 0.01f; cam.farClipPlane = 10000; cam.cullingMask = 1 << Capa;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.35f, 0.38f, 0.42f);
            var luz = luzGo.AddComponent<Light>(); luz.type = LightType.Directional; luz.intensity = 1.5f; luz.cullingMask = 1 << Capa;
            float dist = b.extents.magnitude / Mathf.Tan(15 * Mathf.Deg2Rad) * 1.1f;
            cam.transform.position = b.center + dir.normalized * dist;
            cam.transform.LookAt(b.center);
            luzGo.transform.rotation = Quaternion.LookRotation(-dir.normalized + Vector3.down * 0.8f);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, Res, Res), 0, 0); tex.Apply(); RenderTexture.active = null;
            return tex.EncodeToPNG();
        }
        finally { Object.DestroyImmediate(go); Object.DestroyImmediate(luzGo); Object.DestroyImmediate(rt); Object.DestroyImmediate(tex); }
    }

    static void Preparar()
    {
        System.IO.Directory.CreateDirectory("Logs/gato");
    }

    [MenuItem("Tools/Amistad/Gato: revisar modelo")]
    static void Revisar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Gato] Sal de Play."); return; }
        Preparar();
        foreach (var f in System.IO.Directory.GetFiles("Logs/gato", "vista_*")) System.IO.File.Delete(f);
        var log = new StringBuilder();
        var original = GatoActual();
        if (original == null) { Debug.LogError("[Gato] No encontré instancias de cat.fbx en la escena"); return; }
        var mi = AssetImporter.GetAtPath(Fbx) as ModelImporter;
        log.AppendLine($"FBX {Fbx}: animationType {mi.animationType}, avatarSetup {mi.avatarSetup}, materialImportMode {mi.materialImportMode}, escala {mi.globalScale}");
        Describir(original, log, true);
        var g = CopiaTemporal(original.gameObject, original.position, original.rotation);
        try
        {
            var b = Limites(g);
            string[] nombres = { "frente", "lado_izq", "atras", "lado_der", "tres_cuartos", "arriba" };
            Vector3[] dirs = { g.forward, -g.right, -g.forward, g.right, g.forward + g.right + Vector3.up * 0.6f, Vector3.up * 2 + g.forward };
            for (int i = 0; i < dirs.Length; i++) System.IO.File.WriteAllBytes($"Logs/gato/vista_{i}_{nombres[i]}.png", Foto(g, b, dirs[i]));
        }
        finally { Object.DestroyImmediate(g.gameObject); }
        System.IO.File.WriteAllText("Logs/gato/info.txt", log.ToString());
        Debug.Log("[Gato] Listo: Logs/gato/info.txt\n" + log);
    }

    [MenuItem("Tools/Amistad/Gato: revisar Kitty (ithappy)")]
    static void RevisarKitty()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Gato] Sal de Play."); return; }
        Preparar();
        foreach (var f in System.IO.Directory.GetFiles("Logs/gato", "kitty_*")) System.IO.File.Delete(f);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KittyPrefab);
        if (prefab == null) { Debug.LogError("[Gato] No encuentro " + KittyPrefab); return; }
        var log = new StringBuilder();
        var mi = AssetImporter.GetAtPath(KittyFbx) as ModelImporter;
        log.AppendLine($"FBX {KittyFbx}: animationType {mi.animationType}, avatarSetup {mi.avatarSetup}, escala {mi.globalScale}, useFileScale {mi.useFileScale}, materialImportMode {mi.materialImportMode}");
        foreach (var c in mi.clipAnimations) log.AppendLine($"   clip importado {c.name}: frames {c.firstFrame}-{c.lastFrame}, loop {c.loopTime}, loopPose {c.loopPose}");
        var clipsFbx = AssetDatabase.LoadAllAssetsAtPath(KittyFbx).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToList();
        log.AppendLine("   clips en el FBX: " + string.Join(", ", clipsFbx.Select(c => $"{c.name} {c.length:0.00}s")));

        var lejos = new Vector3(0, -5000, 0);   // lejos de todo; la cámara solo ve la capa temporal
        var g = CopiaTemporal(prefab, lejos, Quaternion.identity);
        try
        {
            Describir(g, log, true);
            var actual = GatoActual();
            var bK = Limites(g);
            if (actual != null)
            {
                var bA = Limites(actual);
                log.AppendLine($"\nGato actual (cat.fbx) mide {bA.size} (alto {bA.size.y:0.00}); Kitty mide {bK.size} (alto {bK.size.y:0.00}).");
                log.AppendLine($"Para igualar la altura, Kitty necesita escala ≈ {bA.size.y / Mathf.Max(0.0001f, bK.size.y):0.00}");
            }
            // Vistas en reposo (pose del prefab)
            string[] nombres = { "frente", "lado", "tres_cuartos", "atras" };
            Vector3[] dirs = { g.forward, g.right, g.forward + g.right + Vector3.up * 0.6f, -g.forward + Vector3.up * 0.3f };
            for (int i = 0; i < dirs.Length; i++) System.IO.File.WriteAllBytes($"Logs/gato/kitty_vista_{i}_{nombres[i]}.png", Foto(g, bK, dirs[i]));
            // Poses de cada clip (vista de lado y 3/4), con el mismo encuadre
            var marco = bK; marco.Expand(new Vector3(bK.size.z * 0.3f, bK.size.y * 0.3f, bK.size.z * 0.3f));
            foreach (var clip in clipsFbx)
            {
                int n = 4;
                for (int i = 0; i < n; i++)
                {
                    float t = clip.length * i / n;
                    clip.SampleAnimation(g.gameObject, t);
                    g.position = lejos; g.rotation = Quaternion.identity;   // sin root motion para comparar poses
                    // En modo edición el skinning no se actualiza antes de cam.Render(): se hornea la pose en una malla fija
                    var pose = HornearPose(g);
                    try
                    {
                        System.IO.File.WriteAllBytes($"Logs/gato/kitty_{clip.name}_{i}_lado.png", Foto(pose, marco, Vector3.right));
                        System.IO.File.WriteAllBytes($"Logs/gato/kitty_{clip.name}_{i}_34.png", Foto(pose, marco, Vector3.forward + Vector3.right * 0.8f + Vector3.up * 0.4f));
                    }
                    finally { Object.DestroyImmediate(pose.GetComponent<MeshFilter>().sharedMesh); Object.DestroyImmediate(pose.gameObject); }
                }
                log.AppendLine($"Poses de {clip.name}: t = {string.Join(", ", Enumerable.Range(0, 4).Select(i => (clip.length * i / 4).ToString("0.00")))}");
            }
        }
        finally { Object.DestroyImmediate(g.gameObject); }
        System.IO.File.WriteAllText("Logs/gato/kitty_info.txt", log.ToString());
        Debug.Log("[Gato] Kitty listo: Logs/gato/kitty_info.txt\n" + log);
    }
}
