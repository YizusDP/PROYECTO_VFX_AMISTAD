using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// Tools > Amistad > Ciudad: diagnóstico (qué tapa la avenida)
/// Lanza rayos desde puntos de cámara de la avenida y lista los renderers cuyo bounds cruzan (Logs/ciudad_ampliada/diagnostico.txt).
public static class CiudadDiagnosticoEditor
{
    [MenuItem("Tools/Amistad/Ciudad: diagnóstico (qué tapa la avenida)")]
    static void Diagnostico()
    {
        var sb = new StringBuilder();
        var rs = Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        void Rayo(string et, Vector3 o, Vector3 haciaPunto, float max)
        {
            var ray = new Ray(o, (haciaPunto - o).normalized);
            sb.AppendLine($"== {et} desde {o} hacia {haciaPunto}");
            foreach (var x in rs.Select(r => { float d; bool h = r.bounds.IntersectRay(ray, out d); return (r, h, d); })
                                 .Where(t => t.h && t.d < max).OrderBy(t => t.d).Take(25))
                sb.AppendLine($"  d={x.d:0} {Ruta(x.r.transform)} [{x.r.GetType().Name}] centro {x.r.bounds.center} tam {x.r.bounds.size}");
        }
        var o1 = new Vector3(-29f, 16f, -330f);
        Rayo("contraplano centro", o1, new Vector3(-29f, 22f, -1400f), 1500f);
        Rayo("contraplano abajo-izq", o1, new Vector3(200f, 0f, -900f), 1500f);
        Rayo("contraplano abajo-der", o1, new Vector3(-260f, 0f, -900f), 1500f);
        Rayo("contraplano abajo", o1, new Vector3(-29f, 0f, -600f), 1500f);
        Directory.CreateDirectory("Logs/ciudad_ampliada");
        File.WriteAllText("Logs/ciudad_ampliada/diagnostico.txt", sb.ToString());
        Debug.Log("[Ciudad] diagnóstico listo");
    }
    static string Ruta(Transform t) => t.parent == null ? t.name : Ruta(t.parent) + "/" + t.name;
}
