using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// Temporal: compara la explosión del gato con su copia en la ciudad.
static class DiagExplosionEditor
{
    [MenuItem("Tools/Amistad/zz Diagnóstico explosión")]
    static void Diag()
    {
        var sb = new StringBuilder();
        var todos = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true));
        foreach (var t in todos.Where(t => t.name == "Explosión horizontal" || t.name == "Explosion_Gato"))
        {
            sb.AppendLine($"== {t.name} path={Ruta(t)} activeSelf={t.gameObject.activeSelf} pos={t.position} rot={t.rotation.eulerAngles} lossy={t.lossyScale}");
            foreach (var ps in t.GetComponentsInChildren<ParticleSystem>(true))
            {
                var r = ps.GetComponent<ParticleSystemRenderer>();
                var streams = new List<ParticleSystemVertexStream>(); r.GetActiveVertexStreams(streams);
                var m = ps.main;
                sb.AppendLine($"  ps {ps.name} lossy={ps.transform.lossyScale} scaling={m.scalingMode} space={m.simulationSpace} renderMode={r.renderMode} mesh={(r.mesh ? r.mesh.name : "-")} mat={(r.sharedMaterial ? r.sharedMaterial.name + "/" + r.sharedMaterial.shader.name : "-")} streams={string.Join(",", streams)} align={r.alignment} startSize3D={m.startSize3D} startSize={m.startSize.constant}/{m.startSize.constantMax} life={m.startLifetime.constant}/{m.startLifetime.constantMax} speed={m.startSpeed.constant}/{m.startSpeed.constantMax} custom={ps.customData.enabled}");
            }
        }
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Assets/shaders/Materiales/Explosion.mat");
        if (mat) { var sh = mat.shader; for (int i = 0; i < sh.GetPropertyCount(); i++) sb.AppendLine($"  prop {sh.GetPropertyName(i)} {sh.GetPropertyType(i)}"); }
        File.WriteAllText("Logs/diag_explosion.txt", sb.ToString());
        Debug.Log(sb.ToString());
    }
    static string Ruta(Transform t) => t.parent == null ? t.name : Ruta(t.parent) + "/" + t.name;
}
