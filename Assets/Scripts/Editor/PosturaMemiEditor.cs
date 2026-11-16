using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using Unity.Cinemachine;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Postura erguida de Memi
/// Crea (o actualiza) POSTURA_MEMI junto a MIRADA_FIJA con el script PosturaErguida: durante el saludo (51.3-62.3 s)
/// baja los hombros de Memi, abre el pecho y alarga el cuello sin mover las manos.
/// Además reencuadra CM8_7_CENTRO (perfil de los dos con las palmas juntas) con un Recomposer: la cámara sube un poco
/// la mira y abre el lente, así las cabezas ya no quedan cortadas arriba y los ojos caen cerca del tercio superior.
/// </summary>
public static class PosturaMemiEditor
{
    const float Tilt = -3.5f;   // grados: negativo = la cámara mira un poco más arriba
    const float Zoom = 1.12f;   // >1 abre el lente

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Postura erguida de Memi", false, 330)]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Postura] Sal de Play."); return; }
        var todos = Resources.FindObjectsOfTypeAll<Transform>().Where(t => t.gameObject.scene.IsValid()).ToList();
        var mirada = todos.FirstOrDefault(t => t.name == "MIRADA_FIJA");
        var memi = todos.FirstOrDefault(t => t.name == "MEMO_PERFIL");
        var dir = todos.FirstOrDefault(t => t.name == "Director_P1_Bosque");
        if (mirada == null || memi == null || dir == null) { Debug.LogError("[Postura] Falta MIRADA_FIJA, MEMO_PERFIL o Director_P1_Bosque"); return; }
        var go = todos.FirstOrDefault(t => t.name == "POSTURA_MEMI")?.gameObject;
        if (go == null) { go = new GameObject("POSTURA_MEMI"); Undo.RegisterCreatedObjectUndo(go, "Postura"); go.transform.SetParent(mirada.parent, false); }
        var p = go.GetComponent<PosturaErguida>() ?? Undo.AddComponent<PosturaErguida>(go);
        Undo.RecordObject(p, "Postura");
        p.director = dir.GetComponent<PlayableDirector>(); p.personaje = memi.GetComponent<Animator>();
        p.inicio = 51.3f; p.llegada = 51.8f; p.salida = 61.8f; p.fin = 62.3f;
        p.bajarHombros = 14f; p.abrirPecho = 5f; p.alargarCuello = 7f; p.cabeza = -3f; p.mantenerManos = true;
        EditorUtility.SetDirty(p);

        var cm87 = todos.FirstOrDefault(t => t.name == "CM8_7_CENTRO");
        if (cm87 != null)
        {
            var rc = cm87.GetComponent<CinemachineRecomposer>() ?? Undo.AddComponent<CinemachineRecomposer>(cm87.gameObject);
            Undo.RecordObject(rc, "Postura");
            rc.Tilt = Tilt; rc.ZoomScale = Zoom; rc.Pan = 0f; rc.Dutch = 0f;
            EditorUtility.SetDirty(rc);
        }
        EditorSceneManager.MarkSceneDirty(go.scene);
        Debug.Log("[Postura] POSTURA_MEMI listo (51.3-62.3 s). Guarda la escena.");
    }
}
