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
    static readonly double[] Tiempos = { 41.6, 41.9, 42.1, 42.3, 42.5, 42.8 };
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
            if (memi != null)
            {
                var anM = memi.GetComponent<Animator>();
                var rsM = memi.GetComponentsInChildren<Renderer>(true);
                var bM = rsM.Length > 0 ? rsM[0].bounds : new Bounds();
                foreach (var r in rsM) bM.Encapsulate(r.bounds);
                var hipsM = anM != null && anM.isHuman ? anM.GetBoneTransform(HumanBodyBones.Hips) : null;
                var velM = memi.GetComponent("VelocidadAnimator");
                System.IO.File.AppendAllText("Logs/capturas_play/info.txt",
                    $"   MEMI activa={memi.activeInHierarchy} escala {memi.transform.lossyScale} bounds {bM.center} tam {bM.size} visibles={rsM.Count(r => r.isVisible)}/{rsM.Length} hips {(hipsM ? hipsM.position.ToString() : "-")} animSpeed {(anM ? anM.speed : -1)} controller {(anM && anM.runtimeAnimatorController ? anM.runtimeAnimatorController.name : "-")} estado {(anM && anM.runtimeAnimatorController ? anM.GetCurrentAnimatorStateInfo(0).normalizedTime : -1):0.00}\n");
            }
            var gz = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "GODZILLA" && t.gameObject.scene.IsValid());
            if (gz != null)
            {
                var anG = gz.GetComponentInChildren<Animator>(true);
                var cabeza = anG != null && anG.isHuman ? anG.GetBoneTransform(HumanBodyBones.Head) : null;
                var rs = gz.GetComponentsInChildren<Renderer>(true);
                var bnd = rs.Length > 0 ? rs[0].bounds : new Bounds(gz.position, Vector3.zero);
                foreach (var r in rs) bnd.Encapsulate(r.bounds);
                var camM = Camera.main;
                var vp = camM != null ? camM.WorldToViewportPoint(bnd.center) : Vector3.zero;
                System.IO.File.AppendAllText("Logs/capturas_play/info.txt",
                    $"   GODZILLA activo={gz.gameObject.activeInHierarchy} pos {gz.position} escala {gz.lossyScale} cabeza {(cabeza ? cabeza.position.ToString() : "-")} bounds {bnd.center} tam {bnd.size} viewport {vp} renderers={rs.Length} visibles={rs.Count(r => r.isVisible)}\n");
            }
            var avi = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "AVIONETA" && t.gameObject.scene.IsValid());
            if (avi != null && Camera.main != null)
                System.IO.File.AppendAllText("Logs/capturas_play/info.txt", $"   AVIONETA activa={avi.gameObject.activeInHierarchy} pos {avi.position} viewport {Camera.main.WorldToViewportPoint(avi.position)}\n");
            var kitty = Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(t => t.name == "GATO_KITTY" && t.gameObject.scene.IsValid());
            if (kitty != null && Camera.main != null)
            {
                var rk = kitty.GetComponentInChildren<SkinnedMeshRenderer>(true);
                System.IO.File.AppendAllText("Logs/capturas_play/info.txt",
                    $"   KITTY activo={kitty.gameObject.activeInHierarchy} pos {kitty.position} fwd {kitty.forward} bounds {(rk ? rk.bounds.center.ToString() : "-")} tam {(rk ? rk.bounds.size.ToString() : "-")} viewport {Camera.main.WorldToViewportPoint(rk ? rk.bounds.center : kitty.position)} visible={(rk && rk.isVisible)}\n");
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
