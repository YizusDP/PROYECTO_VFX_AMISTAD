using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;

/// Tools > Amistad > Revisión y pruebas > Medir pies y manos (Play, cada 0.1 s)
/// Entra a Play, recorre el Timeline principal de Desde a Hasta (SessionState "Amistad_PiesRango", ej. "17.5,67.5,0.1"),
/// y escribe Logs/pies_manos.csv: tiempo, vcam, suela de Ale y Memi (vértice más bajo del cuerpo; piso de la ciudad en y=0)
/// y posición de las manos (huesos). Sale de Play al terminar. No ensucia la escena.
[InitializeOnLoad]
public static class MedirPiesEditor
{
    const string ClaveActiva = "Amistad_PiesActiva", ClaveRango = "Amistad_PiesRango";
    const string Archivo = "Logs/pies_manos.csv";
    static double t, hasta, paso; static int espera; static StringBuilder csv; static Mesh malla;

    static MedirPiesEditor() { if (SessionState.GetBool(ClaveActiva, false)) Preparar(); }

    [MenuItem("Tools/Amistad/Revisión y pruebas/Medir pies y manos (Play, cada 0.1 s)", false, 901)]
    public static void Iniciar()
    {
        if (EditorApplication.isPlaying) { Debug.LogError("[Pies] Sal de Play primero."); return; }
        SessionState.SetBool(ClaveActiva, true);
        EditorApplication.isPlaying = true;
    }

    static void Preparar()
    {
        var r = SessionState.GetString(ClaveRango, "17.5,67.5,0.1").Split(',')
            .Select(s => double.Parse(s.Trim(), CultureInfo.InvariantCulture)).ToArray();
        t = r[0]; hasta = r[1]; paso = r[2]; espera = -1;
        csv = new StringBuilder("t,vcam,ale_suela,memi_suela,aleLM,aleRM,memiLM,memiRM,memiCaderaFwd,memiL_muslo,memiL_rodilla,memiL_tobillo,memiL_punta,memiR_muslo,memiR_rodilla,memiR_tobillo,memiR_punta\n");
        EditorApplication.update -= Tick; EditorApplication.update += Tick;
    }

    static void Tick()
    {
        if (!EditorApplication.isPlaying) return;
        Application.runInBackground = true;
        var go = GameObject.Find("Director_P1_Bosque");
        var d = go != null ? go.GetComponent<PlayableDirector>() : null;
        if (d == null) return;
        if (t > hasta + 1e-6)
        {
            File.WriteAllText(Archivo, csv.ToString());
            SessionState.SetBool(ClaveActiva, false);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
            Debug.Log("[Pies] Listo: " + Archivo);
            return;
        }
        if (espera < 0) { d.Pause(); espera = 2; }
        d.time = t; d.Evaluate();
        if (--espera > 0) return;   // un cuadro para que los Animators apliquen la pose
        var cam = Camera.main; var brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        var vc = brain != null ? brain.ActiveVirtualCamera : null;
        csv.Append(t.ToString("0.00", CultureInfo.InvariantCulture)).Append(',').Append(vc != null ? vc.Name : "-")
           .Append(',').Append(Suela("ALE")).Append(',').Append(Suela("MEMO_PERFIL"))
           .Append(',').Append(Mano("ALE", HumanBodyBones.LeftHand)).Append(',').Append(Mano("ALE", HumanBodyBones.RightHand))
           .Append(',').Append(Mano("MEMO_PERFIL", HumanBodyBones.LeftHand)).Append(',').Append(Mano("MEMO_PERFIL", HumanBodyBones.RightHand))
           .Append(',').Append(CaderaFwd("MEMO_PERFIL"));
        foreach (var h in new[] { HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
                                  HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes })
            csv.Append(',').Append(Mano("MEMO_PERFIL", h));
        csv.Append('\n');
        t += paso; espera = -1;
    }

    static Transform Buscar(string n) =>
        Resources.FindObjectsOfTypeAll<Transform>().FirstOrDefault(q => q.name == n && q.gameObject.scene.IsValid());

    // Vértice más bajo del cuerpo en mundo (BakeMesh false + TRS sin escala = tamaño real)
    public static string Suela(string n)
    {
        var x = Buscar(n);
        if (x == null || !x.gameObject.activeInHierarchy) return "";
        var smr = x.GetComponentsInChildren<SkinnedMeshRenderer>().FirstOrDefault(s => s.name.StartsWith("Body"));
        if (smr == null) return "";
        if (malla == null) malla = new Mesh();
        smr.BakeMesh(malla, false);
        var m = Matrix4x4.TRS(smr.transform.position, smr.transform.rotation, Vector3.one);
        float minY = float.MaxValue;
        foreach (var v in malla.vertices) minY = Mathf.Min(minY, m.MultiplyPoint3x4(v).y);
        return minY.ToString("0.000", CultureInfo.InvariantCulture);
    }

    // hacia dónde mira la cadera (para medir hacia dónde apunta la rodilla)
    static string CaderaFwd(string n)
    {
        var x = Buscar(n);
        if (x == null || !x.gameObject.activeInHierarchy) return "";
        var an = x.GetComponent<Animator>(); var b = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Hips) : null;
        if (b == null) return "";
        var f = an.transform.forward;   // frente del personaje
        return string.Format(CultureInfo.InvariantCulture, "{0:0.000} {1:0.000} {2:0.000}", f.x, f.y, f.z);
    }

    static string Mano(string n, HumanBodyBones h)
    {
        var x = Buscar(n);
        if (x == null || !x.gameObject.activeInHierarchy) return "";
        var an = x.GetComponent<Animator>(); var b = an != null && an.isHuman ? an.GetBoneTransform(h) : null;
        if (b == null) return "";
        var p = b.position;
        return string.Format(CultureInfo.InvariantCulture, "{0:0.00} {1:0.00} {2:0.00}", p.x, p.y, p.z);
    }
}
