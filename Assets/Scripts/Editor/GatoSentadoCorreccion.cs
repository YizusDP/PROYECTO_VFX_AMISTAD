using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Corrige la pose sentada del gato (Gato_Acicalarse y el arranque de Gato_Susto) sobre los .anim ya copiados.
/// En la pose hecha en Blender la cadera quedaba a media pierna y el trasero no tocaba el piso: el muslo bajaba en
/// diagonal hasta la rodilla en el suelo y las patas traseras se leían como si estuviera parado en ellas.
/// Aquí, cuadro por cuadro:
///   1. Baja la cadera (Bajar × largo del muslo) y endereza el torso (Enderezar grados): el trasero se sienta.
///   2. La cola conserva su dirección (cae al piso, enroscada como antes).
///   3. Patas traseras como gato sentado: muslo casi horizontal hacia adelante (un poco abierto), pierna de regreso
///      hasta el talón en el piso y pie plano hacia adelante, junto a las patas delanteras.
///   4. Las patas delanteras que estaban apoyadas vuelven a su lugar con IK (no se hunden al bajar el cuerpo).
/// Lo llama GatoAnimacionEditor después de copiar los clips del FBX; también tiene menú propio.
/// </summary>
public static class GatoSentadoCorreccion
{
    public const float Bajar = 0.48f;      // × largo del muslo
    public const float Enderezar = 12f;    // grados extra de torso erguido
    const float PisoMin = -0.6f;            // lo más bajo que llegan las puntas de las patas en la pose original (escala de prueba ×100)

    [MenuItem("Tools/Amistad/1 · Bosque/Gato: corregir patas traseras (sentado)", false, 103)]
    static void Menu()
    {
        var k = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "GATO_KITTY");
        var a = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animaciones/Gato/Gato_Acicalarse.anim");
        var s = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/Animaciones/Gato/Gato_Susto.anim");
        if (k == null || a == null || s == null) { Debug.LogError("[Gato] Falta GATO_KITTY o los .anim (corre primero 'Gato: acicalarse, susto y carrera')."); return; }
        Debug.Log("[Gato] " + Aplicar(k.gameObject, a, t => 1f) + "\n" + Aplicar(k.gameObject, s, t => 1f - Mathf.SmoothStep(0f, 1f, t / 0.1f)));
        AssetDatabase.SaveAssets();
    }

    /// peso(t): 1 = corrección completa, 0 = pose original.
    public static string Aplicar(GameObject kitty, AnimationClip clip, System.Func<float, float> peso)
    {
        var copia = Object.Instantiate(kitty); copia.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            copia.transform.SetParent(null); copia.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); copia.transform.localScale = Vector3.one * 100f;
            var ts = copia.GetComponentsInChildren<Transform>(true);
            Transform B(string n) => ts.First(t => t.name == n);
            var pelvis = B("spine.005"); var colaBase = B("spine.004");
            var lados = new[] { ("R", 1f), ("L", -1f) };
            float fr = clip.frameRate > 0 ? clip.frameRate : 30f;
            int n = Mathf.RoundToInt(clip.length * fr);
            var huesos = new List<Transform> { pelvis, colaBase };
            foreach (var (s, _) in lados) huesos.AddRange(new[] { B("thigh." + s), B("shin." + s), B("foot." + s), B("toe." + s), B("front_thigh." + s), B("front_shin." + s), B("front_foot." + s) });
            var rot = huesos.ToDictionary(h => h, h => new List<Quaternion>());
            var pos = new List<Vector3>();
            var smr = copia.GetComponentInChildren<SkinnedMeshRenderer>(); var malla = new Mesh(); int levantados = 0;
            float bajaMax = 0f, traseroAntes = 0f, traseroDespues = 0f;

            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Min(clip.length, i / fr);
                clip.SampleAnimation(copia, t);
                float w = Mathf.Clamp01(peso(t));
                float L = Vector3.Distance(B("thigh.R").position, B("shin.R").position);
                float alto = B("scull").position.y;
                if (i == 0) traseroAntes = B("thigh.R").position.y;
                if (w > 0.001f)
                {
                    // originales
                    var talon = lados.ToDictionary(l => l.Item1, l => B("foot." + l.Item1).position);
                    var puntaY = lados.ToDictionary(l => l.Item1, l => Mathf.Min(B("toe." + l.Item1 + "_end").position.y, 0.6f));
                    var rPie = lados.ToDictionary(l => l.Item1, l => (B("foot." + l.Item1).rotation, B("toe." + l.Item1).rotation));
                    var pataDel = lados.ToDictionary(l => l.Item1, l => (B("front_foot." + l.Item1).position, B("front_foot." + l.Item1).rotation, B("front_shin." + l.Item1).position));
                    var apoyada = lados.ToDictionary(l => l.Item1, l => B("front_foot." + l.Item1).position.y < alto * 0.2f);
                    var rCola = colaBase.rotation;
                    // 1. cuerpo
                    pelvis.rotation = Quaternion.AngleAxis(-Enderezar * w, Vector3.right) * pelvis.rotation;
                    pelvis.position += Vector3.down * (Bajar * L * w);
                    colaBase.rotation = rCola;
                    // 3. patas traseras: muslo hacia adelante (casi horizontal, un poco abierto), rodilla adelante,
                    //    pierna de regreso hacia atrás hasta el talón en el piso, pie plano hacia adelante.
                    foreach (var (s, sx) in lados)
                    {
                        var a = B("thigh." + s); var b = B("shin." + s); var c = B("foot." + s);
                        var r0 = (a.localRotation, b.localRotation, c.localRotation, B("toe." + s).localRotation);
                        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
                        var rodilla = a.position + new Vector3(sx * 0.32f, 0.08f, 1f).normalized * la;
                        float dy = Mathf.Clamp(rodilla.y - talon[s].y, 0f, lb * 0.95f);
                        var talonNuevo = rodilla + new Vector3(0f, -dy, -Mathf.Sqrt(lb * lb - dy * dy));
                        a.rotation = Quaternion.FromToRotation(b.position - a.position, rodilla - a.position) * a.rotation;
                        b.rotation = Quaternion.FromToRotation(c.position - b.position, talonNuevo - b.position) * b.rotation;
                        c.rotation = rPie[s].Item1; B("toe." + s).rotation = rPie[s].Item2;
                        if (w < 0.999f)
                        {
                            a.localRotation = Quaternion.Slerp(r0.Item1, a.localRotation, w); b.localRotation = Quaternion.Slerp(r0.Item2, b.localRotation, w);
                            c.localRotation = Quaternion.Slerp(r0.Item3, c.localRotation, w); B("toe." + s).localRotation = Quaternion.Slerp(r0.Item4, B("toe." + s).localRotation, w);
                        }
                        // la punta del pie no se mete al piso (en la mezcla con el brinco)
                        var punta = B("toe." + s + "_end");
                        for (int it = 0; it < 60 && punta.position.y < puntaY[s] - 0.05f; it++)
                        {
                            var eje = Vector3.Cross(punta.position - c.position, Vector3.down);
                            if (eje.sqrMagnitude < 1e-6f) break;
                            float y0 = punta.position.y; var rc = c.rotation;
                            c.rotation = Quaternion.AngleAxis(3f, eje.normalized) * rc;
                            if (punta.position.y <= y0) c.rotation = Quaternion.AngleAxis(-3f, eje.normalized) * rc;   // el sentido que sube
                        }
                    }
                    // 4. patas delanteras apoyadas
                    foreach (var (s, _) in lados)
                        if (apoyada[s])
                        {
                            DosHuesos(B("front_thigh." + s), B("front_shin." + s), B("front_foot." + s), pataDel[s].Item1, pataDel[s].Item3 + Vector3.back * L);
                            B("front_foot." + s).rotation = pataDel[s].Item2;
                        }
                    bajaMax = Mathf.Max(bajaMax, Bajar * L * w);
                }
                // nada del cuerpo por debajo del piso (la mezcla sentado -> brinco hundía las patas 1-3 cuadros)
                {
                    smr.BakeMesh(malla, true);
                    float minY = float.MaxValue; foreach (var v in malla.vertices) minY = Mathf.Min(minY, smr.transform.TransformPoint(v).y);
                    if (minY < PisoMin) { pelvis.position += Vector3.up * (PisoMin - minY); levantados++; }
                }
                if (i == 0) traseroDespues = B("thigh.R").position.y;
                foreach (var h in huesos) rot[h].Add(h.localRotation);
                pos.Add(pelvis.localPosition);
            }

            // escribir curvas (quaternions continuos, tangentes por diferencias)
            void Curva(string ruta, string prop, float[] v)
            {
                var keys = new Keyframe[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    float d = i == 0 ? (v.Length > 1 ? (v[1] - v[0]) * fr : 0f) : i == v.Length - 1 ? (v[i] - v[i - 1]) * fr : (v[i + 1] - v[i - 1]) * fr * 0.5f;
                    keys[i] = new Keyframe(Mathf.Min(clip.length, i / fr), v[i], d, d);
                }
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(ruta, typeof(Transform), prop), new AnimationCurve(keys));
            }
            foreach (var h in huesos)
            {
                var q = rot[h];
                for (int i = 1; i < q.Count; i++) if (Quaternion.Dot(q[i - 1], q[i]) < 0f) q[i] = new Quaternion(-q[i].x, -q[i].y, -q[i].z, -q[i].w);
                string ruta = AnimationUtility.CalculateTransformPath(h, copia.transform);
                Curva(ruta, "m_LocalRotation.x", q.Select(x => x.x).ToArray()); Curva(ruta, "m_LocalRotation.y", q.Select(x => x.y).ToArray());
                Curva(ruta, "m_LocalRotation.z", q.Select(x => x.z).ToArray()); Curva(ruta, "m_LocalRotation.w", q.Select(x => x.w).ToArray());
            }
            string rp = AnimationUtility.CalculateTransformPath(pelvis, copia.transform);
            Curva(rp, "m_LocalPosition.x", pos.Select(p => p.x).ToArray()); Curva(rp, "m_LocalPosition.y", pos.Select(p => p.y).ToArray()); Curva(rp, "m_LocalPosition.z", pos.Select(p => p.z).ToArray());
            EditorUtility.SetDirty(clip);
            Object.DestroyImmediate(malla);
            return $"{clip.name}: {n + 1} cuadros, cadera {traseroAntes:0.0} -> {traseroDespues:0.0} (bajó hasta {bajaMax:0.0}), torso +{Enderezar}°, {levantados} cuadros levantados del piso";
        }
        finally { Object.DestroyImmediate(copia); }
    }

    // IK de dos huesos con polo: a = muslo, b = pierna, c = talón
    static void DosHuesos(Transform a, Transform b, Transform c, Vector3 objetivo, Vector3 polo)
    {
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        var haciaObj = objetivo - a.position;
        float d = Mathf.Clamp(haciaObj.magnitude, Mathf.Abs(la - lb) + 1e-3f, la + lb - 1e-3f);
        var dir = haciaObj.normalized;
        var dobla = Vector3.ProjectOnPlane(polo - a.position, dir);
        if (dobla.sqrMagnitude < 1e-8f) dobla = Vector3.ProjectOnPlane(b.position - a.position, dir);
        dobla.Normalize();
        float cosA = Mathf.Clamp((la * la + d * d - lb * lb) / (2f * la * d), -1f, 1f);
        float sinA = Mathf.Sqrt(1f - cosA * cosA);
        var rodilla = a.position + dir * (la * cosA) + dobla * (la * sinA);
        a.rotation = Quaternion.FromToRotation(b.position - a.position, rodilla - a.position) * a.rotation;
        b.rotation = Quaternion.FromToRotation(c.position - b.position, objetivo - b.position) * b.rotation;
    }
}
