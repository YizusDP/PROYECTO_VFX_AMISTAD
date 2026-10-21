using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class MedirEscena
{
    [MenuItem("Tools/Amistad/Medir tamaños (consola)")]
    static void Medir()
    {
        var todos = SceneManager.GetActiveScene().GetRootGameObjects()
            .SelectMany(g => g.GetComponentsInChildren<Renderer>(true)).ToList();
        void Log(string etiqueta, System.Func<Renderer, bool> filtro, int max = 4)
        {
            foreach (var r in todos.Where(filtro).Take(max))
                Debug.Log($"[Medir] {etiqueta}: '{r.name}' padre='{(r.transform.parent ? r.transform.parent.name : "-")}' tamaño={r.bounds.size} centro={r.bounds.center} escala={r.transform.lossyScale} malla={(r.GetComponent<MeshFilter>() && r.GetComponent<MeshFilter>().sharedMesh ? r.GetComponent<MeshFilter>().sharedMesh.name + " " + r.GetComponent<MeshFilter>().sharedMesh.bounds.size : "-")}");
        }
        Log("Arbusto", r => r.sharedMaterials.Any(m => m && m.name.Contains("3f84aea6648aa6cc5af5f07ee43c6056")), 6);
        Log("Abeto", r => r.sharedMaterials.Any(m => m && m.name.Contains("cc24eba76e0e306bc19df7b7a9208504")), 4);
        Log("Pino", r => r.sharedMaterials.Any(m => m && m.name.Contains("05ade0e713892be558511d706fa6c547")), 2);
        Log("Gato", r => r.transform.root.name == "cat" || (r.transform.parent && r.transform.parent.name == "cat"), 3);
        Log("Piso", r => r.name == "Piso_FOREST", 1);
        foreach (var c in Object.FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            Debug.Log($"[Medir] Cámara '{c.name}' pos={c.transform.position}");
    }
}
