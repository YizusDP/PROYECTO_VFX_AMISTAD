using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Amistad > 4 · Iluminación > Ale y Memi: piel toon (sin look de plástico)
/// Cambia los materiales Ale_Borde / Memi_Borde (TOON SHADER Borde, Unlit) por materiales con el shader
/// Amistad/Personaje Toon: luz del sol con sombras, corte toon suave con sombra de color, banda cálida de piel,
/// luz de los fuegos y de las light probes, brillo ancho y tenue, borde de luz y línea de tinta.
/// El cuerpo y el pelo usan la misma textura pero ahora tienen material propio (el pelo no lleva piel y sí banda de brillo).
/// Si existe la oclusión horneada en Blender (Texturas_MemiAle/AO_<nombre>.png) se conecta sola.
/// "Quitar" regresa los materiales _Borde. Se puede volver a correr: actualiza los valores de los materiales.
/// </summary>
public static class PersonajesToonEditor
{
    const string Carpeta = "Assets/Assets/3d/Persoanjes/Materiales_Toon";
    const string CarpetaTex = "Assets/Assets/3d/Persoanjes/Texturas_MemiAle";
    const string Shader_ = "Amistad/Personaje Toon";

    [MenuItem("Tools/Amistad/4 · Iluminación/Ale y Memi: piel toon (sin look de plástico)", false, 410)]
    static void Aplicar()
    {
        var sh = Shader.Find(Shader_);
        if (sh == null) { Debug.LogError("[Piel] Falta el shader " + Shader_); return; }
        if (!AssetDatabase.IsValidFolder(Carpeta)) AssetDatabase.CreateFolder("Assets/Assets/3d/Persoanjes", "Materiales_Toon");
        var log = new StringBuilder();
        foreach (var nombre in new[] { "Ale", "Memi" })
        {
            var borde = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Assets/3d/Persoanjes/{nombre}_Borde.mat");
            if (borde == null) { log.AppendLine($"AVISO: falta {nombre}_Borde.mat"); continue; }
            var piel = Material_($"{Carpeta}/{nombre}_Piel.mat", sh);
            var pelo = Material_($"{Carpeta}/{nombre}_Pelo.mat", sh);
            var ao = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CarpetaTex}/AO_{nombre}.png");
            foreach (var m in new[] { piel, pelo })
            {
                m.SetTexture("_MainTex", borde.GetTexture("_MainTex"));
                m.SetColor("_Color", Color.white);
                m.SetColor("_EdgeColour", borde.GetColor("_EdgeColour"));
                m.SetFloat("_CantidadBorde", 7f);
                m.SetFloat("_Umbral", 0.24f); m.SetFloat("_Suavidad", 0.045f);   // el TOON SHADER original cortaba en N·L ≈ 0.26 (LightRange): misma cantidad de cara en sombra
                m.SetColor("_ColorSombra", new Color(0.46f, 0.34f, 0.42f)); m.SetFloat("_Relleno", 0.12f);
                m.SetFloat("_LuzAlta", 0.14f); m.SetFloat("_UmbralAlto", 0.62f); m.SetFloat("_Nucleo", 0.22f); m.SetFloat("_UmbralNucleo", -0.4f);
                m.SetFloat("_IntensidadSol", 1.25f); m.SetFloat("_InvertirSol", 1f); m.SetFloat("_Ambiente", 0.42f); m.SetFloat("_SatAmbiente", 0.5f);
                m.SetFloat("_LucesExtra", 0.25f); m.SetFloat("_SombrasRecibidas", 0f);   // con el sol invertido (convención del TOON SHADER) las sombras del sol caen del otro lado: no se usan
                m.SetColor("_ColorSSS", new Color(1f, 0.45f, 0.3f)); m.SetFloat("_SSSFuerza", 0.25f); m.SetFloat("_SSSAncho", 0.18f); m.SetFloat("_Traspaso", 0.25f);
                m.SetFloat("_BordeLuz", 0.35f); m.SetColor("_ColorLinea", new Color(0.22f, 0.12f, 0.1f)); m.SetFloat("_Grano", 0.055f);
                m.SetFloat("_MateRopa", 0.08f); m.SetFloat("_BandasLuces", 3f);
                m.SetTexture("_AOTex", ao);
                m.SetFloat("_AOFuerza", ao ? 0.75f : 0f);
            }
            // textura repintada (Tools > Amistad > 4 · Iluminación > repintar texturas): el cuerpo la usa con el segundo UV;
            // el pelo sigue con la textura original y sus UVs.
            var pintada = AssetDatabase.LoadAssetAtPath<Texture2D>($"{CarpetaTex}/{nombre}_Pintada.png");
            piel.SetTexture("_MainTex", pintada ? pintada : borde.GetTexture("_MainTex")); piel.SetFloat("_UsarUV2", pintada ? 1f : 0f);
            pelo.SetFloat("_UsarUV2", 0f);
            // piel y ropa del cuerpo
            piel.SetFloat("_MascaraPiel", 0.5f); piel.SetFloat("_Especular", 0.07f); piel.SetFloat("_Aspereza", 0.65f); piel.SetFloat("_BandaPelo", 0f);
            piel.SetFloat("_GrosorLinea", 1.4f);
            // pelo: sin piel, brillo más angosto y banda de brillo
            pelo.SetFloat("_MascaraPiel", 0f); pelo.SetFloat("_Especular", 0.06f); pelo.SetFloat("_Aspereza", 0.4f); pelo.SetFloat("_BandaPelo", 0.12f); pelo.SetFloat("_BordeLuz", 0.3f);
            // las mechas son tarjetas delgadas: su línea se veía como rayas sobre la cara y el cuello
            pelo.SetFloat("_GrosorLinea", 0f); pelo.SetFloat("_Suavidad", 0.12f);
            EditorUtility.SetDirty(piel); EditorUtility.SetDirty(pelo);

            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials; bool cambio = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == borde || mats[i] == piel || mats[i] == pelo)
                    {
                        var nuevo = EsPelo(r) ? pelo : piel;
                        if (mats[i] != nuevo) { mats[i] = nuevo; cambio = true; }
                    }
                if (cambio) { Undo.RecordObject(r, "Piel toon"); r.sharedMaterials = mats; EditorUtility.SetDirty(r); n++; }
                // el pelo no proyecta sombra: con el shader iluminado oscurecía toda la cara
                if (EsPelo(r) && r.sharedMaterials.Contains(pelo) && r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)
                { Undo.RecordObject(r, "Piel toon"); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; EditorUtility.SetDirty(r); }
            }
            bool conAOV = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Any(r => r.sharedMaterial == piel && r.sharedMesh != null && r.sharedMesh.colors.Length > 0);
            piel.SetFloat("_AOVertice", conAOV ? 0.85f : 0f); pelo.SetFloat("_AOVertice", 0f);
            log.AppendLine($"{nombre}: {n} renderers -> {nombre}_Piel / {nombre}_Pelo" + (ao ? $" (oclusión {ao.name})" : "") + (conAOV ? " (oclusión por vértice)" : " (sin oclusión por vértice: corre 'hornear oclusión')"));
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[Piel] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }

    [MenuItem("Tools/Amistad/4 · Iluminación/Ale y Memi: piel toon, quitar (regresa _Borde)", false, 411)]
    static void Quitar()
    {
        foreach (var nombre in new[] { "Ale", "Memi" })
        {
            var borde = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Assets/3d/Persoanjes/{nombre}_Borde.mat");
            var piel = AssetDatabase.LoadAssetAtPath<Material>($"{Carpeta}/{nombre}_Piel.mat");
            var pelo = AssetDatabase.LoadAssetAtPath<Material>($"{Carpeta}/{nombre}_Pelo.mat");
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials; bool cambio = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] != null && (mats[i] == piel || mats[i] == pelo)) { mats[i] = borde; cambio = true; }
                if (cambio) { Undo.RecordObject(r, "Piel toon"); r.sharedMaterials = mats; if (EsPelo(r)) r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On; EditorUtility.SetDirty(r); }
            }
        }
        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log("[Piel] Materiales _Borde de vuelta.");
    }

    /// Oclusión ambiental por vértice: rayos desde cada vértice del cuerpo, en su pose de enlace, contra el mismo cuerpo.
    /// Se guarda en el color de vértice de la malla del cuerpo (Manos_HD) y el shader la usa con _AOVertice.
    /// Oscurece axilas, cuello bajo la barbilla, entre dedos y bajo los pectorales: da volumen sin tocar la textura pintada.
    [MenuItem("Tools/Amistad/4 · Iluminación/Ale y Memi: hornear oclusión por vértice", false, 412)]
    static void HornearOclusion()
    {
        const int Rayos = 64;
        int capa = Enumerable.Range(8, 24).Reverse().FirstOrDefault(l => !Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None).Any(c => c.gameObject.layer == l));
        if (capa == 0) { Debug.LogError("[AO] No hay una capa libre para los colisionadores temporales."); return; }
        var dirs = new Vector3[Rayos];   // hemisferio con distribución coseno (Fibonacci) alrededor de +Z
        for (int i = 0; i < Rayos; i++)
        {
            float u = (i + 0.5f) / Rayos, phi = i * 2.39996323f;
            float r = Mathf.Sqrt(u); dirs[i] = new Vector3(r * Mathf.Cos(phi), r * Mathf.Sin(phi), Mathf.Sqrt(1f - u));
        }
        var log = new StringBuilder();
        bool backfaces = Physics.queriesHitBackfaces; Physics.queriesHitBackfaces = true;
        var temporales = new System.Collections.Generic.List<Object>();
        try
        {
            foreach (var raiz in new[] { "ALE", "MEMO_PERFIL" })
            {
                var t = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(x => x.name == raiz && x.gameObject.scene.IsValid());
                if (t == null) { log.AppendLine($"{raiz}: no está en la escena"); continue; }
                var smrs = t.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var cuerpo = smrs.FirstOrDefault(r => r.sharedMaterial != null && r.sharedMaterial.name.EndsWith("_Piel"));
                if (cuerpo == null) { log.AppendLine($"{raiz}: corre primero 'piel toon'"); continue; }
                // Pose de enlace (la de la malla, brazos separados): la oclusión no depende de la pose del cuadro actual.
                var malla = cuerpo.sharedMesh;
                var go = new GameObject("_AO_temp") { hideFlags = HideFlags.HideAndDontSave, layer = capa }; temporales.Add(go);
                go.transform.localScale = Vector3.one * (10f / Mathf.Max(1e-5f, malla.bounds.size.y));   // a 10 u de alto: precisión de la física
                go.AddComponent<MeshCollider>().sharedMesh = malla;
                Physics.SyncTransforms();
                var m = go.transform.localToWorldMatrix;
                float alto = 10f, eps = alto * 0.002f, dist = alto * 0.12f;
                var vs = malla.vertices; var ns = malla.normals;
                var col = new Color[vs.Length]; float suma = 0;
                for (int v = 0; v < vs.Length; v++)
                {
                    var p = m.MultiplyPoint3x4(vs[v]); var n = m.MultiplyVector(ns[v]).normalized;
                    var q = Quaternion.FromToRotation(Vector3.forward, n);
                    int golpes = 0;
                    for (int k = 0; k < Rayos; k++) if (Physics.Raycast(p + n * eps, q * dirs[k], dist, 1 << capa)) golpes++;
                    float ao = Mathf.Pow(1f - golpes / (float)Rayos, 1.3f);
                    col[v] = new Color(ao, ao, ao, 1f); suma += ao;
                }
                if (!AssetDatabase.GetAssetPath(malla).EndsWith(".asset")) { log.AppendLine($"{raiz}: la malla está dentro de un FBX; no se puede escribir"); continue; }
                Undo.RecordObject(malla, "AO"); malla.colors = col; EditorUtility.SetDirty(malla);
                log.AppendLine($"{raiz}: {vs.Length} vértices, {Rayos} rayos, distancia {dist:0.00} u, oclusión media {suma / vs.Length:0.00} -> {malla.name}");
            }
        }
        finally
        {
            Physics.queriesHitBackfaces = backfaces;
            foreach (var o in temporales) if (o != null) Object.DestroyImmediate(o);
        }
        AssetDatabase.SaveAssets();
        Debug.Log("[AO] Listo. Vuelve a correr 'piel toon' para activarla en los materiales.\n" + log);
        System.IO.Directory.CreateDirectory("Logs/personajes"); System.IO.File.WriteAllText("Logs/personajes/oclusion.txt", log.ToString());
    }

    static bool EsPelo(Renderer r) => r.name.ToLower().Contains("hair") || r.name.ToLower().Contains("pelo");

    static Material Material_(string ruta, Shader sh)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
        else if (m.shader != sh) m.shader = sh;
        return m;
    }
}
