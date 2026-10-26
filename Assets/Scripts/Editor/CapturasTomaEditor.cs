using System.Linq;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

/// Tools > Amistad > Capturas de la toma de la ciudad
/// Pone el Timeline principal en varios segundos (17.5 a 23) y guarda lo que ve la Main Camera en Logs/capturas/t_XX.png.
/// La caminata de Memi se muestrea a mano porque el Animator Controller no corre fuera de Play. No cambia nada de la escena.
public static class CapturasTomaEditor
{
    // Ojo: recarga la escena del disco al terminar (guarda antes con Ctrl+S).
    [MenuItem("Tools/Amistad/Capturas de la toma de la ciudad")]
    static void Capturar()
    {
        var escena = SceneManager.GetActiveScene();
        var todos = escena.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToList();
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var p1 = T("Director_P1_Bosque").GetComponent<PlayableDirector>();
        var cam = T("Main Camera").GetComponent<Camera>();
        var brain = cam.GetComponent<CinemachineBrain>();
        var memi = T("MEMO_PERFIL");
        var an = memi != null ? memi.GetComponent<Animator>() : null;
        var clip = an != null && an.runtimeAnimatorController != null ? an.runtimeAnimatorController.animationClips.FirstOrDefault(c => c.name.Contains("Caminata")) : null;
        System.IO.Directory.CreateDirectory("Logs/capturas");
        var rt = new RenderTexture(960, 540, 24);
        var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
        var info = new System.Text.StringBuilder();
        double tiempoOriginal = p1.time;
        foreach (var t in new[] { 17.6, 18.5, 19.5, 20.5, 21.5, 22.5 })
        {
            p1.time = t; p1.Evaluate();
            if (brain != null) brain.ManualUpdate();
            if (clip != null && memi != null)
            {
                var pos = memi.localPosition; var rot = memi.localRotation;
                if (!AnimationMode.InAnimationMode()) AnimationMode.StartAnimationMode();
                AnimationMode.BeginSampling();
                AnimationMode.SampleAnimationClip(memi.gameObject, clip, (float)((t - 17.5) % clip.length));
                AnimationMode.EndSampling();
                memi.localPosition = pos; memi.localRotation = rot;
            }
            if (brain != null) brain.ManualUpdate();
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply(); RenderTexture.active = null;
            System.IO.File.WriteAllBytes($"Logs/capturas/t_{t:00.0}.png", tex.EncodeToPNG());
            info.AppendLine($"t={t}: cam {cam.transform.position} rot {cam.transform.eulerAngles} · Memi {(memi ? memi.position.ToString() : "-")} activo={(memi ? memi.gameObject.activeInHierarchy : false)}");
        }
        if (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
        p1.time = tiempoOriginal; p1.Evaluate();
        // La escena se guardó antes: se recarga del disco para no dejar ninguna pose o posición de la prueba
        if (!string.IsNullOrEmpty(escena.path)) UnityEditor.SceneManagement.EditorSceneManager.OpenScene(escena.path);
        Object.DestroyImmediate(rt);
        System.IO.File.WriteAllText("Logs/capturas/info.txt", info.ToString());
        Debug.Log("[Capturas]\n" + info);
    }
}
