using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Amistad > 4 · Iluminación > Ale y Memi: repintar texturas (hornear)
/// Las texturas de Ale y Memi están dibujadas de frente (cara, cuello, clavículas, hombros y pecho), pero en los UVs del
/// cuerpo solo la cara usa ese dibujo: el torso, los brazos y las piernas caen en manchitas diminutas de la textura y
/// cada parte agarra un solo color. Los trazos del pecho y las luces de los hombros nunca llegaban al modelo.
/// Esta herramienta:
///   1. Genera un segundo juego de UVs sin traslapes (uv2) en una copia de la malla del cuerpo (<cuerpo>_Pintada.asset)
///      y se la asigna a Ale y Memi; la malla Manos_HD queda como estaba.
///   2. Hornea una textura nueva en ese uv2: parte del color que el modelo ya tenía (ropa y piel no cambian) y le
///      proyecta de frente, con sutileza (FuerzaTrazo / FuerzaLuz), los trazos y las luces del dibujo original,
///      alineados con la cara: los trazos quedan como sombreado del mismo tono y las luces como piel más clara.
///   3. Donde el dibujo no llega (antebrazos, manos, piernas, espalda) pinta las luces con la misma forma y color
///      que usó el dibujo, desde arriba y al frente, para que todo el cuerpo tenga el mismo estilo.
/// Rasgos del dibujo (trazos = R, luces = G) en Texturas_MemiAle/Rasgos_<nombre>.png (los saca Docs/scripts/rasgos_dibujo.py).
/// Las texturas originales no se tocan. Después corre "piel toon" para que el material del cuerpo use la textura nueva.
/// </summary>
public static class PinturaPersonajesEditor
{
    const int Res = 2048;
    // Sutileza (0..1): qué tanto entran los trazos y las luces del dibujo en el cuerpo. La cara no se ve afectada.
    const float FuerzaTrazo = 0.28f;   // trazos de pecho y cuello: sombreado del mismo tono de piel, no tinta negra
    const float FuerzaLuz = 0.35f;     // formas de luz: aclaran el tono de la piel, no lo cambian a durazno
    const string CarpetaTex = "Assets/Assets/3d/Persoanjes/Texturas_MemiAle";

    struct Personaje { public string nombre, malla, pintura, rasgos; }
    static readonly Personaje[] Lista =
    {
        new Personaje { nombre = "Ale", malla = "Assets/Animaciones/Saludo/Manos_HD/Body Male LowPoly.001_ManosHD.asset", pintura = CarpetaTex + "/20260907 TEX A VER SI SALE.png", rasgos = CarpetaTex + "/Rasgos_Ale.png" },
        new Personaje { nombre = "Memi", malla = "Assets/Animaciones/Saludo/Manos_HD/Body Male LowPoly.003_ManosHD.asset", pintura = CarpetaTex + "/20260907 TEX A VER SI SALE 1.png", rasgos = CarpetaTex + "/Rasgos_Memi.png" },
    };

    [MenuItem("Tools/Amistad/4 · Iluminación/Ale y Memi: repintar texturas (hornear)", false, 413)]
    static void Repintar()
    {
        var log = new StringBuilder();
        foreach (var p in Lista)
        {
            try { log.AppendLine(Hornear(p)); }
            catch (System.Exception e) { log.AppendLine($"{p.nombre}: ERROR {e.Message}"); Debug.LogException(e); }
        }
        AssetDatabase.SaveAssets(); AssetDatabase.Refresh();
        Directory.CreateDirectory("Logs/personajes"); File.WriteAllText("Logs/personajes/repintado.txt", log.ToString());
        Debug.Log("[Pintura] Listo. Corre 'piel toon' para usar las texturas nuevas.\n" + log);
    }

    static string Hornear(Personaje p)
    {
        var malla = AssetDatabase.LoadAssetAtPath<Mesh>(p.malla);
        if (malla == null) return $"{p.nombre}: falta {p.malla}";
        // ---- 1. malla nueva con uv2 (asset aparte; la malla Manos_HD no se toca) ----
        // Si una versión anterior de esta herramienta modificó la malla, se regresa desde el respaldo.
        string respaldo = p.malla.Replace(".asset", "_original.asset");
        var resp = AssetDatabase.LoadAssetAtPath<Mesh>(respaldo);
        if (resp != null && resp.vertexCount != malla.vertexCount) { CopiarMalla(resp, malla); EditorUtility.SetDirty(malla); }
        var m = Object.Instantiate(malla);
        UnwrapParam.SetDefaults(out var up); up.packMargin = 0.006f; up.hardAngle = 70f;
        if (!Unwrapping.GenerateSecondaryUVSet(m, up)) return $"{p.nombre}: no se pudo generar uv2";
        string rutaNueva = p.malla.Replace("_ManosHD.asset", "_Pintada.asset");
        var nueva = AssetDatabase.LoadAssetAtPath<Mesh>(rutaNueva);
        if (nueva == null) { nueva = new Mesh(); AssetDatabase.CreateAsset(nueva, rutaNueva); }
        CopiarMalla(m, nueva); nueva.name = malla.name.Replace("_ManosHD", "_Pintada");
        EditorUtility.SetDirty(nueva); Object.DestroyImmediate(m);
        foreach (var smr in Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (smr.sharedMesh == malla || smr.sharedMesh == nueva) { Undo.RecordObject(smr, "Pintura"); smr.sharedMesh = nueva; EditorUtility.SetDirty(smr); }
        malla = nueva;

        // ---- 2. datos ----
        var pintura = Cargar(p.pintura); var rasgos = Cargar(p.rasgos);
        if (pintura == null || rasgos == null) return $"{p.nombre}: falta la pintura o {p.rasgos}";
        var V = malla.vertices; var N = malla.normals; var U0 = malla.uv; var U2 = malla.uv2; var T = malla.triangles;
        var C = malla.colors.Length == V.Length ? malla.colors : null;
        float W = pintura.width, H = pintura.height;

        // ---- 3. frente y proyección: triángulos bien mapeados (grandes en el dibujo) del lado izquierdo = la cara ----
        Vector3 frente = Vector3.zero; var caraV = new HashSet<int>();
        for (int i = 0; i < T.Length; i += 3)
        {
            Vector2 a = U0[T[i]], b = U0[T[i + 1]], c = U0[T[i + 2]];
            float areaPx = Mathf.Abs((b.x - a.x) * (c.y - a.y) - (c.x - a.x) * (b.y - a.y)) * 0.5f * W * H;
            float cu = (a.x + b.x + c.x) / 3f;
            if (areaPx < 15f || cu > 0.62f) continue;
            var n = Vector3.Cross(V[T[i + 1]] - V[T[i]], V[T[i + 2]] - V[T[i]]);
            frente += n; caraV.Add(T[i]); caraV.Add(T[i + 1]); caraV.Add(T[i + 2]);
        }
        frente.Normalize();
        var b0 = malla.bounds; Vector3 arriba = b0.size.y >= b0.size.z && b0.size.y >= b0.size.x ? Vector3.up : (b0.size.z >= b0.size.x ? Vector3.forward : Vector3.right);
        if (Mathf.Abs(Vector3.Dot(arriba, frente)) > 0.8f) arriba = Vector3.up;
        var der = Vector3.Cross(arriba, frente).normalized; arriba = Vector3.Cross(frente, der).normalized;
        // u = a0*x + a1*y + a2 ; v igual (mínimos cuadrados, dos pasadas quitando atípicos)
        var idx = caraV.ToList();
        double[] au = null, av = null;
        for (int pasada = 0; pasada < 2; pasada++)
        {
            au = Ajuste(idx, V, U0, der, arriba, true); av = Ajuste(idx, V, U0, der, arriba, false);
            var res = idx.Select(k => (k, e: Mathf.Abs(Eval(au, V[k], der, arriba) - U0[k].x) * W + Mathf.Abs(Eval(av, V[k], der, arriba) - U0[k].y) * H)).ToList();
            float corte = res.Select(r => r.e).OrderBy(e => e).ElementAt((int)(res.Count * 0.8f)) * 1.5f + 1f;
            idx = res.Where(r => r.e <= corte).Select(r => r.k).ToList();
        }
        // El dibujo se hizo con los brazos abajo y el modelo está en pose T: los hombros y brazos del dibujo no caen en
        // su lugar. Solo se proyecta la franja central (cara, cuello, clavículas y pecho), con borde suave.
        float centroU = idx.Average(k => U0[k].x);
        const float MediaFranja = 0.12f, Suave = 0.04f;
        float rms = Mathf.Sqrt(idx.Average(k => Mathf.Pow((Eval(au, V[k], der, arriba) - U0[k].x) * W, 2) + Mathf.Pow((Eval(av, V[k], der, arriba) - U0[k].y) * H, 2)));

        // ---- 4. hornear ----
        var px = new Color[Res * Res]; var lleno = new bool[Res * Res];
        var dirLuz = (frente * 0.55f + arriba * 0.8f + der * 0.25f).normalized;
        for (int i = 0; i < T.Length; i += 3)
        {
            int i0 = T[i], i1 = T[i + 1], i2 = T[i + 2];
            Vector2 a = U2[i0] * Res, b = U2[i1] * Res, c = U2[i2] * Res;
            int x0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.x, Mathf.Min(b.x, c.x))) - 1), x1 = Mathf.Min(Res - 1, Mathf.CeilToInt(Mathf.Max(a.x, Mathf.Max(b.x, c.x))) + 1);
            int y0 = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(a.y, Mathf.Min(b.y, c.y))) - 1), y1 = Mathf.Min(Res - 1, Mathf.CeilToInt(Mathf.Max(a.y, Mathf.Max(b.y, c.y))) + 1);
            float den = (b.y - c.y) * (a.x - c.x) + (c.x - b.x) * (a.y - c.y);
            if (Mathf.Abs(den) < 1e-8f) continue;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    float qx = x + 0.5f, qy = y + 0.5f;
                    float w0 = ((b.y - c.y) * (qx - c.x) + (c.x - b.x) * (qy - c.y)) / den;
                    float w1 = ((c.y - a.y) * (qx - c.x) + (a.x - c.x) * (qy - c.y)) / den;
                    float w2 = 1f - w0 - w1;
                    const float e = -0.02f;   // un poco más allá del borde: evita rendijas entre triángulos
                    if (w0 < e || w1 < e || w2 < e) continue;
                    var pos = V[i0] * w0 + V[i1] * w1 + V[i2] * w2;
                    var nor = (N[i0] * w0 + N[i1] * w1 + N[i2] * w2).normalized;
                    var uv0 = U0[i0] * w0 + U0[i1] * w1 + U0[i2] * w2;
                    var col = pintura.GetPixelBilinear(uv0.x, uv0.y); col.a = 1f;
                    bool piel = EsPiel(col);
                    // proyección frontal del dibujo
                    float pu = Eval(au, pos, der, arriba), pv = Eval(av, pos, der, arriba);
                    float wf = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.2f, 0.55f, Vector3.Dot(nor, frente)));
                    float dentro = (pu > 0.002f && pu < 0.998f && pv > 0.002f && pv < 0.998f) ? pintura.GetPixelBilinear(pu, pv).a : 0f;
                    float franja = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(MediaFranja - Suave, MediaFranja, Mathf.Abs(pu - centroU)));
                    float cubre = wf * dentro * franja;
                    if (piel)
                    {
                        var claro = Aclarar(col);      // el mismo tono de piel, más claro (no el durazno fijo)
                        var oscuro = col * 0.72f; oscuro.a = 1f;
                        if (cubre > 0.01f)
                        {
                            var r = rasgos.GetPixelBilinear(pu, pv);
                            col = Color.Lerp(col, claro, r.g * cubre * FuerzaLuz);
                            col = Color.Lerp(col, oscuro, r.r * cubre * FuerzaTrazo);
                        }
                        // luces pintadas donde el dibujo no llega (misma forma, borde suave)
                        float k = Vector3.Dot(nor, dirLuz);
                        float luz = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.7f, k)) * FuerzaLuz * (1f - cubre);
                        col = Color.Lerp(col, claro, luz);
                        // un poco más oscuro del lado de la sombra
                        float bordeSombra = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.38f, -0.52f, k)) * 0.15f * (1f - cubre);
                        col = Color.Lerp(col, oscuro, bordeSombra);
                    }
                    else
                    {
                        // ropa oscura: brillo de tela suave arriba
                        float k = Vector3.Dot(nor, dirLuz);
                        col = Color.Lerp(col, col + new Color(0.07f, 0.07f, 0.08f), Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.55f, 0.8f, k)));
                    }
                    int id = y * Res + x; px[id] = col; lleno[id] = true;
                }
        }
        // ---- 5. relleno de bordes (evita costuras con mipmaps) ----
        for (int it = 0; it < 8; it++)
        {
            var nuevos = new List<(int, Color)>();
            for (int y = 0; y < Res; y++)
                for (int x = 0; x < Res; x++)
                {
                    int id = y * Res + x; if (lleno[id]) continue;
                    Color s = Color.clear; int n = 0;
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        {
                            int xx = x + dx, yy = y + dy; if (xx < 0 || yy < 0 || xx >= Res || yy >= Res) continue;
                            int j = yy * Res + xx; if (lleno[j]) { s += px[j]; n++; }
                        }
                    if (n > 0) nuevos.Add((id, s / n));
                }
            foreach (var (id, c) in nuevos) { px[id] = c; lleno[id] = true; }
        }
        var tex = new Texture2D(Res, Res, TextureFormat.RGBA32, false); tex.SetPixels(px); tex.Apply();
        string salida = $"{CarpetaTex}/{p.nombre}_Pintada.png";
        File.WriteAllBytes(salida, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(salida);
        var ti = (TextureImporter)AssetImporter.GetAtPath(salida);
        ti.sRGBTexture = true; ti.maxTextureSize = Res; ti.textureCompression = TextureImporterCompression.CompressedHQ; ti.wrapMode = TextureWrapMode.Clamp; ti.mipmapEnabled = true;
        ti.SaveAndReimport();
        Object.DestroyImmediate(pintura); Object.DestroyImmediate(rasgos);
        return $"{p.nombre}: uv2 nuevo ({malla.vertexCount} vértices), frente {frente:F2}, ajuste de la proyección con {idx.Count} vértices de la cara (error {rms:0.0} px) -> {salida}";
    }

    /// Copia arreglo por arreglo (CopySerialized dejaba la malla con piel sin dibujarse).
    static void CopiarMalla(Mesh de, Mesh a)
    {
        a.Clear();
        a.indexFormat = de.indexFormat;
        a.vertices = de.vertices; a.normals = de.normals; a.tangents = de.tangents;
        if (de.colors.Length == de.vertexCount) a.colors = de.colors;
        a.uv = de.uv; if (de.uv2.Length == de.vertexCount) a.uv2 = de.uv2;
        a.bindposes = de.bindposes; a.boneWeights = de.boneWeights;
        a.subMeshCount = de.subMeshCount;
        for (int i = 0; i < de.subMeshCount; i++) a.SetTriangles(de.GetTriangles(i), i);
        a.RecalculateBounds();
    }

    static Texture2D Cargar(string ruta)
    {
        if (!File.Exists(ruta)) return null;
        var t = new Texture2D(2, 2, TextureFormat.RGBA32, false, false); t.LoadImage(File.ReadAllBytes(ruta)); t.wrapMode = TextureWrapMode.Clamp; t.filterMode = FilterMode.Bilinear;
        return t;
    }

    static float Eval(double[] c, Vector3 p, Vector3 der, Vector3 arriba) => (float)(c[0] * Vector3.Dot(p, der) + c[1] * Vector3.Dot(p, arriba) + c[2]);

    static double[] Ajuste(List<int> idx, Vector3[] V, Vector2[] U, Vector3 der, Vector3 arriba, bool esU)
    {
        // normal equations 3x3
        double[,] A = new double[3, 3]; double[] b = new double[3];
        foreach (var k in idx)
        {
            double[] f = { Vector3.Dot(V[k], der), Vector3.Dot(V[k], arriba), 1.0 };
            double t = esU ? U[k].x : U[k].y;
            for (int r = 0; r < 3; r++) { b[r] += f[r] * t; for (int c = 0; c < 3; c++) A[r, c] += f[r] * f[c]; }
        }
        // Gauss
        for (int i = 0; i < 3; i++)
        {
            int piv = i; for (int r = i + 1; r < 3; r++) if (System.Math.Abs(A[r, i]) > System.Math.Abs(A[piv, i])) piv = r;
            for (int c = 0; c < 3; c++) { var tmp = A[i, c]; A[i, c] = A[piv, c]; A[piv, c] = tmp; } { var tb = b[i]; b[i] = b[piv]; b[piv] = tb; }
            for (int r = i + 1; r < 3; r++) { double f = A[r, i] / A[i, i]; for (int c = i; c < 3; c++) A[r, c] -= f * A[i, c]; b[r] -= f * b[i]; }
        }
        var x = new double[3];
        for (int i = 2; i >= 0; i--) { double s = b[i]; for (int c = i + 1; c < 3; c++) s -= A[i, c] * x[c]; x[i] = s / A[i, i]; }
        return x;
    }

    static Color Aclarar(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        var r = Color.HSVToRGB(h, s * 0.85f, Mathf.Min(1f, v * 1.22f)); r.a = 1f; return r;
    }

    static bool EsPiel(Color c)
    {
        float mx = Mathf.Max(c.r, Mathf.Max(c.g, c.b)), mn = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        if (mx < 0.2f || mx - mn < 0.08f) return false;
        Color.RGBToHSV(c, out float h, out float s, out float v);
        return (h < 0.13f || h > 0.95f) && s > 0.2f;
    }
}
