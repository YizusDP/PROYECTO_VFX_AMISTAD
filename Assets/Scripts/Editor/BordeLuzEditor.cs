using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Amistad > 4 · Iluminación > Borde de luz en Ale y Memi (rim light)
/// El shader toon (Unlit, de Memo) calculaba un borde Fresnel × EdgeColour pero esa salida no estaba conectada a nada,
/// y las luces de contraluz del saludo no afectan a un shader Unlit. "TOON SHADER Borde.shadergraph" es una copia con esa
/// conexión hecha (Fresnel × EdgeColour se suma al color final). El shader original NO se toca.
/// Esta herramienta crea Ale_Borde.mat y Memi_Borde.mat (copias de Ale.mat / Toon Shading.mat con el shader nuevo y un
/// borde cálido y delgado) y los asigna a los renderers de los personajes. "Quitar" regresa los materiales originales.
/// </summary>
public static class BordeLuzEditor
{
    const string Carpeta = "Assets/Assets/3d/Persoanjes";
    const float Grosor = 7f;                                         // potencia del Fresnel: más alto = borde más delgado
    static readonly Color ColorBorde = new Color(0.6f, 0.24f, 0.1f);  // cálido y tenue (fuego de la ciudad); con 3.5 / (0.85,0.42,0.3) lavaba los colores

    [MenuItem("Tools/Amistad/4 · Iluminación/Borde de luz en Ale y Memi (rim light)")]
    static void Menu()
    {
        var log = new StringBuilder();
        var sh = Shader.Find("Shader Graphs/TOON SHADER Borde");
        if (sh == null) { Debug.LogError("[Borde] Falta 'TOON SHADER Borde.shadergraph'."); return; }
        foreach (var (orig, nuevo) in new[] { ("Ale", "Ale_Borde"), ("Toon Shading", "Memi_Borde") })
        {
            var mo = AssetDatabase.LoadAssetAtPath<Material>($"{Carpeta}/{orig}.mat");
            if (mo == null) { log.AppendLine($"No encontré {orig}.mat"); continue; }
            string ruta = $"{Carpeta}/{nuevo}.mat";
            var mn = AssetDatabase.LoadAssetAtPath<Material>(ruta);
            if (mn == null) { mn = new Material(mo); AssetDatabase.CreateAsset(mn, ruta); }
            mn.shader = sh; mn.CopyPropertiesFromMaterial(mo);
            mn.SetFloat("_CantidadBorde", Grosor);
            mn.SetColor("_EdgeColour", ColorBorde);
            EditorUtility.SetDirty(mn);
            int n = 0;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials; bool cambio = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] == mo) { mats[i] = mn; cambio = true; }
                if (cambio) { Undo.RecordObject(r, "Borde"); r.sharedMaterials = mats; EditorUtility.SetDirty(r); n++; }
            }
            log.AppendLine($"{nuevo}: {n} renderers (borde {Grosor}, color {ColorBorde}).");
        }
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[Borde] Listo.\n" + log);
    }

    [MenuItem("Tools/Amistad/4 · Iluminación/Borde de luz: quitar (materiales originales)")]
    static void Quitar()
    {
        foreach (var (orig, nuevo) in new[] { ("Ale", "Ale_Borde"), ("Toon Shading", "Memi_Borde") })
        {
            var mo = AssetDatabase.LoadAssetAtPath<Material>($"{Carpeta}/{orig}.mat");
            var mn = AssetDatabase.LoadAssetAtPath<Material>($"{Carpeta}/{nuevo}.mat");
            if (mo == null || mn == null) continue;
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                var mats = r.sharedMaterials; bool cambio = false;
                for (int i = 0; i < mats.Length; i++) if (mats[i] == mn) { mats[i] = mo; cambio = true; }
                if (cambio) { Undo.RecordObject(r, "Borde"); r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
            }
        }
        EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("[Borde] Materiales originales de vuelta.");
    }
}
