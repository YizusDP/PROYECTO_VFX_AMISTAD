using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

/// Tools > Amistad > Capturas en Play (toma de la ciudad)
/// Entra a Play y guarda el Game view en Logs/capturas_play/t_XX.png en varios segundos del Timeline principal; luego sale de Play.
[InitializeOnLoad]
public static class CapturasPlayEditor
{
    const string Clave = "Amistad_CapturasPlay";
    static readonly double[] Tiempos = { 18.0, 19.0, 20.0, 21.0, 22.0, 22.9 };
    static int siguiente;

    static CapturasPlayEditor()
    {
        if (SessionState.GetBool(Clave, false)) { siguiente = 0; EditorApplication.update += Tick; }
    }

    [MenuItem("Tools/Amistad/Capturas en Play (toma de la ciudad)")]
    static void Iniciar()
    {
        System.IO.Directory.CreateDirectory("Logs/capturas_play");
        foreach (var f in System.IO.Directory.GetFiles("Logs/capturas_play")) System.IO.File.Delete(f);
        SessionState.SetBool(Clave, true);
        siguiente = 0;
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
        EditorApplication.isPlaying = true;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        var go = GameObject.Find("Director_P1_Bosque");
        var d = go != null ? go.GetComponent<PlayableDirector>() : null;
        if (d == null) return;
        if (siguiente < Tiempos.Length && d.time >= Tiempos[siguiente])
        {
            ScreenCapture.CaptureScreenshot($"Logs/capturas_play/t_{Tiempos[siguiente]:00.0}.png");
            var memi = GameObject.Find("MEMO_PERFIL");
            var cam = Camera.main;
            if (memi != null && cam != null)
            {
                var an = memi.GetComponent<Animator>();
                var hips = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Hips) : null;
                var head = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Head) : null;
                var haciaCam = cam.transform.position - (head != null ? head.position : memi.transform.position); haciaCam.y = 0;
                var frente = hips != null ? Vector3.ProjectOnPlane(an.bodyRotation * Vector3.forward, Vector3.up) : memi.transform.forward;
                System.IO.File.AppendAllText("Logs/capturas_play/info.txt",
                    $"t={d.time:0.00} cam {cam.transform.position} fwd {cam.transform.forward} · Memi raiz {memi.transform.position} rotY {memi.transform.eulerAngles.y:0} · cuerpo mira {frente} · angulo cuerpo-vs-camara {Vector3.Angle(frente, haciaCam):0}°\n");
            }
            siguiente++;
        }
        else if (siguiente >= Tiempos.Length && d.time >= Tiempos[Tiempos.Length - 1] + 1.0)
        {
            SessionState.SetBool(Clave, false);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }
    }
}
