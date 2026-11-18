using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Tools > Amistad > 2 · Ciudad > Fuego estilo caricatura
/// Cambia el fuego de la ciudad (Fuego_VFX, llamas realistas) por el estilo de las explosiones (la del gato y la del derrumbe):
/// esferas que se disuelven con ruido, núcleo amarillo y borde oscuro (shader Explosion, material Explosion_Ciudad).
///   1. Crea/actualiza Assets/Assets/shaders/FuegoVFX/Fuego_Toon.prefab: copia de Fuego_VFX (mismo tamaño, humo, brasas y luz)
///      donde las llamas (Llamas y Base) se cambian por "Llamas_Toon": el sistema "Explosion vertical" de la explosión del gato,
///      pero continuo y hacia arriba, con la forma, tiempos y tamaños de las llamas originales.
///   2. Cambia en la escena cada instancia de Fuego_VFX por Fuego_Toon (mismo padre, nombre, posición, escala y luz).
/// CiudadCaosEditor ya usa Fuego_Toon, así que volver a correr el caos conserva el estilo. Se puede volver a correr.
/// </summary>
public static class FuegoToonEditor
{
    const string RutaFuego = "Assets/Assets/shaders/FuegoVFX/Fuego_VFX.prefab";
    const string RutaToon = "Assets/Assets/shaders/FuegoVFX/Fuego_Toon.prefab";
    const string RutaExplosion = "Assets/Assets/shaders/Explosión horizontal.prefab";
    const string RutaMat = "Assets/Assets/shaders/Materiales/Explosion_Ciudad.mat";
    const string RutaMatFuego = "Assets/Assets/shaders/Materiales/Fuego_Toon.mat";
    // Transparencia: variante del shader Explosion (transparente, alpha = disolvencia × _Opacidad). La usan el fuego y la
    // explosión de la ciudad (Explosion_Ciudad.mat); la del gato en el bosque (Explosion.mat) se queda opaca.
    const string RutaShaderTr = "Assets/Assets/shaders/Explosion_Transparente.shadergraph";
    public const float OpacidadFuego = 0.5f, OpacidadExplosion = 0.7f;
    const string RutaMalla = "Assets/Assets/shaders/FuegoVFX/Llama_Toon.asset";       // lengua de fuego (base redonda, punta afilada)
    const float Alargue = 3.2f;          // alto de cada llama respecto a su ancho
    const float Curva = 0.3f;           // cuánto se dobla la punta (fracción del ancho)
    const float Inclina = 14f;           // grados máximos de inclinación de cada llama
    // Fuego combinado: las llamas realistas (shader Amistad/Fuego VFX) con el estilo caricatura dentro del mismo shader.
    const string RutaMatLlamasToon = "Assets/Assets/shaders/FuegoVFX/Fuego_Llamas_Toon.mat";
    const float Toon = 0.85f;            // 0 = realista, 1 = caricatura total (silueta nítida, bandas, orilla oscura)
    const float Cubre = 0.45f;            // 0 = transparente/aditivo como el original; más alto = tapa más lo de atrás
    const float Borde = 0.08f;           // grosor de la orilla oscura
    const bool LenguasToon = false;      // true = además las lenguas de malla con el shader de la explosión
    const bool QuitarRealistas = false;  // false = híbrido (llamas realistas + lenguas toon); true = solo lenguas toon
    // Color HDR del fuego: más rojo que verde para que el postpro de la ciudad no lo vuelva amarillo lima
    static readonly Color ColorFuego = new Color(7f, 1.6f, 0f, 1f);

    // Ajustes del look
    const float TamRelativo = 0.14f;     // tamaño de las esferas respecto al de las llamas originales (las llamas viejas son texturas casi transparentes)
    const float Tasa = 0.55f;             // partículas por segundo respecto a las llamas originales
    const float Sube = 1.9f;             // velocidad hacia arriba respecto a las llamas originales (forma la lengua de fuego)
    const float Ancho = 0.6f;           // radio de la base respecto al de las llamas originales

    [MenuItem("Tools/Amistad/2 · Ciudad/Fuego estilo caricatura", false, 203)]
    static void Aplicar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[FuegoToon] Sal de Play."); return; }
        var log = new StringBuilder();
        var fuego = AssetDatabase.LoadAssetAtPath<GameObject>(RutaFuego);
        var expl = AssetDatabase.LoadAssetAtPath<GameObject>(RutaExplosion);
        var mat = AssetDatabase.LoadAssetAtPath<Material>(RutaMat) ?? AssetDatabase.LoadAssetAtPath<Material>("Assets/Assets/shaders/Materiales/Explosion.mat");
        if (fuego == null || expl == null || mat == null) { Debug.LogError("[FuegoToon] Falta Fuego_VFX, la explosión o su material"); return; }
        HacerTransparente(mat, OpacidadExplosion);
        var matF = AssetDatabase.LoadAssetAtPath<Material>(RutaMatFuego);
        if (matF == null) { matF = new Material(mat); AssetDatabase.CreateAsset(matF, RutaMatFuego); }
        matF.CopyPropertiesFromMaterial(mat);
        matF.SetColor("_FireColor", ColorFuego);
        HacerTransparente(matF, OpacidadFuego);
        EditorUtility.SetDirty(matF);
        mat = matF;

        // ---------- 1. Prefab Fuego_Toon ----------
        var tmp = (GameObject)PrefabUtility.InstantiatePrefab(fuego);
        PrefabUtility.UnpackPrefabInstance(tmp, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        tmp.name = "Fuego_Toon";
        try
        {
            var llamas = tmp.transform.Find("Llamas")?.GetComponent<ParticleSystem>();
            if (llamas == null) { Debug.LogError("[FuegoToon] Fuego_VFX no tiene 'Llamas'"); return; }
            var fuenteT = expl.GetComponentsInChildren<ParticleSystem>(true).FirstOrDefault(p => p.name == "Explosion vertical")
                          ?? expl.GetComponentsInChildren<ParticleSystem>(true).Last();
            var go = new GameObject("Llamas_Toon");
            go.transform.SetParent(tmp.transform, false);
            go.transform.localPosition = llamas.transform.localPosition;
            go.transform.localRotation = Quaternion.identity; go.transform.localScale = Vector3.one;
            UnityEditorInternal.ComponentUtility.CopyComponent(fuenteT); UnityEditorInternal.ComponentUtility.PasteComponentAsNew(go);
            var rFuente = fuenteT.GetComponent<ParticleSystemRenderer>();
            var rNuevo = go.GetComponent<ParticleSystemRenderer>();
            UnityEditorInternal.ComponentUtility.CopyComponent(rFuente); UnityEditorInternal.ComponentUtility.PasteComponentValues(rNuevo);
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var mL = llamas.main; var m = ps.main;
            m.duration = 2f; m.loop = true; m.prewarm = true; m.playOnAwake = true; m.simulationSpeed = 1f;
            m.scalingMode = mL.scalingMode; m.simulationSpace = mL.simulationSpace;
            m.startDelay = 0f;
            m.startLifetime = mL.startLifetime;
            m.startSpeed = Mul(mL.startSpeed, Sube);
            // Llamas alargadas hacia arriba (tamaño 3D: Y = Alargue × ancho)
            var tam = Mul(mL.startSize3D ? mL.startSizeX : mL.startSize, TamRelativo);
            m.startSize3D = true;
            m.startSizeX = tam; m.startSizeZ = tam; m.startSizeY = Mul(tam, Alargue);
            // De pie, girada al azar sobre su eje y apenas inclinada (cada lengua se dobla hacia un lado distinto)
            float inc = Inclina * Mathf.Deg2Rad;
            m.startRotation3D = true;
            m.startRotationX = new ParticleSystem.MinMaxCurve(-inc, inc);
            m.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            m.startRotationZ = new ParticleSystem.MinMaxCurve(-inc, inc);
            var rotL = ps.rotationOverLifetime; rotL.enabled = true; rotL.separateAxes = true;
            rotL.x = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f); rotL.y = new ParticleSystem.MinMaxCurve(-1.5f, 1.5f); rotL.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            m.gravityModifier = mL.gravityModifier;
            m.maxParticles = Mathf.Max(200, mL.maxParticles);
            var e = ps.emission; e.enabled = true; e.SetBursts(new ParticleSystem.Burst[0]);
            e.rateOverTime = Mul(llamas.emission.rateOverTime, Tasa);
            var sL = llamas.shape; var s = ps.shape;
            s.enabled = true; s.shapeType = sL.shapeType; s.angle = sL.angle; s.radius = sL.radius * Ancho; s.radiusThickness = sL.radiusThickness;
            s.position = sL.position; s.rotation = sL.rotation; s.scale = sL.scale; s.arc = sL.arc;
            // Las llamas suben y se mecen: velocidad y ruido de las llamas originales
            Copiar(llamas.velocityOverLifetime, ps.velocityOverLifetime);
            var nL = llamas.noise; var n = ps.noise;
            n.enabled = nL.enabled; if (nL.enabled) { n.strength = nL.strength; n.frequency = nL.frequency; n.scrollSpeed = nL.scrollSpeed; n.quality = nL.quality; }
            var lim = ps.limitVelocityOverLifetime; lim.enabled = false;
            // Disolvencia (Custom1.xy del shader Explosion): aparece, queda sólida un momento y se rompe en jirones con
            // borde oscuro mientras sube (en la explosión original se rompía casi al final).
            var cd = ps.customData; cd.enabled = true;
            cd.SetMode(ParticleSystemCustomData.Custom1, ParticleSystemCustomDataMode.Vector);
            cd.SetVectorComponentCount(ParticleSystemCustomData.Custom1, 4);
            cd.SetVector(ParticleSystemCustomData.Custom1, 0, new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.83f), new Keyframe(0.15f, -0.08f), new Keyframe(0.55f, 0.35f), new Keyframe(1f, 0.8f))));
            cd.SetVector(ParticleSystemCustomData.Custom1, 1, new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
                new Keyframe(0f, 0.65f), new Keyframe(0.15f, -0.5f), new Keyframe(0.55f, 0.4f), new Keyframe(1f, 1.0f))));
            // Silueta de llama: nace chica, se infla abajo y se adelgaza hacia la punta
            var sol = ps.sizeOverLifetime; sol.enabled = true; sol.separateAxes = false;
            sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(0.25f, 1f), new Keyframe(0.7f, 0.6f), new Keyframe(1f, 0.1f)));
            rNuevo.sharedMaterial = mat;
            rNuevo.renderMode = ParticleSystemRenderMode.Mesh;
            rNuevo.mesh = MallaLlama();
            rNuevo.sortMode = ParticleSystemSortMode.Distance;     // transparente: se dibujan de atrás hacia adelante
            rNuevo.alignment = ParticleSystemRenderSpace.World;   // la punta siempre hacia arriba, no hacia la cámara
            rNuevo.maxParticleSize = Mathf.Max(rNuevo.maxParticleSize, 2f);
            rNuevo.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            // Híbrido: las llamas realistas (Llamas, Base) se quedan como cuerpo del fuego y las lenguas toon van encima.
            var quitar = QuitarRealistas ? new[] { "Llamas", "Base" } : new string[0];
            foreach (var q in quitar) { var t = tmp.transform.Find(q); if (t != null) Object.DestroyImmediate(t.gameObject); }
            log.AppendLine($"Fuego_Toon: Llamas_Toon copia de '{fuenteT.name}' (vida {Txt(m.startLifetime)}, tamaño {Txt(m.startSize)}, tasa {Txt(e.rateOverTime)}, forma {s.shapeType} r {s.radius:0.00}); se quitaron {string.Join(", ", quitar)}; se quedan {string.Join(", ", tmp.transform.Cast<Transform>().Select(t => t.name))}");
            // Llamas realistas con el estilo caricatura del shader (mismos colores y transparencia)
            var matLl = MaterialLlamasToon(llamas.GetComponent<ParticleSystemRenderer>().sharedMaterial);
            foreach (var nombre in new[] { "Llamas", "Base" })
            {
                var t = tmp.transform.Find(nombre);
                if (t != null) t.GetComponent<ParticleSystemRenderer>().sharedMaterial = matLl;
            }
            log.AppendLine($"Llamas y Base con {matLl.name}: toon {Toon}, cubre {Cubre}, orilla {Borde}");
            if (!LenguasToon) { Object.DestroyImmediate(go); log.AppendLine("Sin lenguas de malla (LenguasToon = false)."); }
            PrefabUtility.SaveAsPrefabAsset(tmp, RutaToon);
        }
        finally { Object.DestroyImmediate(tmp); }
        var toon = AssetDatabase.LoadAssetAtPath<GameObject>(RutaToon);

        // ---------- 2. Cambiar las instancias de la escena ----------
        var viejos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(t => PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject) && PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject) == fuego).ToList();
        int n0 = 0;
        foreach (var v in viejos)
        {
            var nuevo = (GameObject)PrefabUtility.InstantiatePrefab(toon, v.parent);
            Undo.RegisterCreatedObjectUndo(nuevo, "Fuego toon");
            nuevo.name = v.name;
            nuevo.transform.SetSiblingIndex(v.GetSiblingIndex());
            nuevo.transform.localPosition = v.localPosition; nuevo.transform.localRotation = v.localRotation; nuevo.transform.localScale = v.localScale;
            nuevo.SetActive(v.gameObject.activeSelf);
            bool conLuz = v.GetComponentsInChildren<Light>(true).Any(l => l.enabled);
            foreach (var l in nuevo.GetComponentsInChildren<Light>(true)) l.enabled = conLuz;
            Undo.DestroyObjectImmediate(v.gameObject);
            n0++;
        }
        log.AppendLine($"Escena: {n0} fuegos cambiados de Fuego_VFX a Fuego_Toon.");
        // Explosiones de la ciudad que usan Explosion_Ciudad: ordenar sus partículas por distancia (ya son transparentes)
        int nE = 0;
        foreach (var r in Object.FindObjectsByType<ParticleSystemRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (r.sharedMaterial == mat && r.sortMode != ParticleSystemSortMode.Distance) { Undo.RecordObject(r, "Fuego toon"); r.sortMode = ParticleSystemSortMode.Distance; nE++; }
        log.AppendLine($"Transparencia: fuego {OpacidadFuego}, explosión de la ciudad {OpacidadExplosion} ({nE} sistemas de explosión ordenados por distancia).");
        if (nE > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        if (n0 > 0) EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
        System.IO.Directory.CreateDirectory("Logs/fuego_toon");
        System.IO.File.WriteAllText("Logs/fuego_toon/info.txt", log.ToString());
        Debug.Log("[FuegoToon] Listo. Guarda la escena con Ctrl+S.\n" + log);
    }

    /// Copia del material de las llamas realistas con el estilo caricatura activado y mezcla premultiplicada
    /// (Blend One OneMinusSrcAlpha): conserva el color y lo transparente del aditivo, pero deja ver la orilla oscura.
    static Material MaterialLlamasToon(Material original)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(RutaMatLlamasToon);
        if (m == null) { m = new Material(original); AssetDatabase.CreateAsset(m, RutaMatLlamasToon); }
        m.shader = original.shader;
        m.CopyPropertiesFromMaterial(original);
        m.SetFloat("_Toon", Toon); m.SetFloat("_Cubre", Cubre); m.SetFloat("_Borde", Borde);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.renderQueue = original.renderQueue;
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Cambia el material a la variante transparente del shader Explosion con la opacidad dada (conserva sus colores).
    public static void HacerTransparente(Material m, float opacidad)
    {
        var sh = AssetDatabase.LoadAssetAtPath<Shader>(RutaShaderTr);
        if (m == null || sh == null) { Debug.LogWarning("[FuegoToon] No encuentro " + RutaShaderTr); return; }
        if (m.shader != sh) m.shader = sh;
        m.SetFloat("_Opacidad", opacidad);
        m.renderQueue = 3000;
        EditorUtility.SetDirty(m);
    }

    /// Malla de una lengua de fuego: perfil de revolución r(t) = 0.5·sin(π·t^0.45) (base redonda, punta afilada),
    /// alto 1 (pivote cerca del centro del bulbo), con la punta doblada 'Curva' hacia +X. UV: u = ángulo, v = altura.
    static Mesh MallaLlama()
    {
        var malla = AssetDatabase.LoadAssetAtPath<Mesh>(RutaMalla);
        bool nueva = malla == null;
        if (nueva) malla = new Mesh { name = "Llama_Toon" };
        malla.Clear();
        int anillos = 24, lados = 20;
        var v = new System.Collections.Generic.List<Vector3>(); var uv = new System.Collections.Generic.List<Vector2>(); var tri = new System.Collections.Generic.List<int>();
        for (int i = 0; i <= anillos; i++)
        {
            float t = (float)i / anillos;
            float r = 0.5f * Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.45f));
            float dobla = Curva * t * t;              // la punta se va de lado
            float y = t - 0.22f;                       // pivote en el bulbo
            for (int j = 0; j <= lados; j++)
            {
                float a = 2f * Mathf.PI * j / lados;
                v.Add(new Vector3(Mathf.Cos(a) * r + dobla, y, Mathf.Sin(a) * r));
                uv.Add(new Vector2((float)j / lados, t));
            }
        }
        for (int i = 0; i < anillos; i++)
            for (int j = 0; j < lados; j++)
            {
                int a = i * (lados + 1) + j, b = a + lados + 1;
                tri.Add(a); tri.Add(b); tri.Add(a + 1);
                tri.Add(a + 1); tri.Add(b); tri.Add(b + 1);
            }
        malla.SetVertices(v); malla.SetUVs(0, uv); malla.SetTriangles(tri, 0);
        malla.RecalculateNormals(); malla.RecalculateTangents(); malla.RecalculateBounds();
        if (nueva) AssetDatabase.CreateAsset(malla, RutaMalla); else EditorUtility.SetDirty(malla);
        return malla;
    }

    static ParticleSystem.MinMaxCurve Mul(ParticleSystem.MinMaxCurve c, float k)
    {
        switch (c.mode)
        {
            case ParticleSystemCurveMode.Constant: return new ParticleSystem.MinMaxCurve(c.constant * k);
            case ParticleSystemCurveMode.TwoConstants: return new ParticleSystem.MinMaxCurve(c.constantMin * k, c.constantMax * k);
            default: c.curveMultiplier *= k; return c;
        }
    }

    static string Txt(ParticleSystem.MinMaxCurve c) => c.mode == ParticleSystemCurveMode.TwoConstants ? $"{c.constantMin:0.##}-{c.constantMax:0.##}" : c.mode == ParticleSystemCurveMode.Constant ? $"{c.constant:0.##}" : $"curva x{c.curveMultiplier:0.##}";

    static void Copiar(ParticleSystem.VelocityOverLifetimeModule a, ParticleSystem.VelocityOverLifetimeModule b)
    {
        b.enabled = a.enabled;
        if (!a.enabled) return;
        b.space = a.space; b.x = a.x; b.y = a.y; b.z = a.z;
        b.orbitalX = a.orbitalX; b.orbitalY = a.orbitalY; b.orbitalZ = a.orbitalZ; b.radial = a.radial; b.speedModifier = a.speedModifier;
    }

    // Tools > Amistad > Revisión y pruebas > Ciudad: probar fuego caricatura
    // Pone Fuego_VFX y Fuego_Toon lado a lado (copias temporales, lejos de todo), simula 3 s y guarda
    // Logs/fuego_toon/prueba.png + medidas de las llamas de cada uno en prueba.txt. No toca la escena.
    [MenuItem("Tools/Amistad/Revisión y pruebas/Ciudad: probar fuego caricatura", false, 915)]
    static void Probar()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        var log = new StringBuilder();
        var objs = new System.Collections.Generic.List<GameObject>();
        try
        {
            float alto = 30f; var base0 = new Vector3(0f, -8000f, 0f);
            GameObject Poner(string ruta, float x)
            {
                var g = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(ruta));
                foreach (var t in g.GetComponentsInChildren<Transform>(true)) { t.gameObject.hideFlags = HideFlags.HideAndDontSave; t.gameObject.layer = 31; }
                g.transform.position = base0 + Vector3.right * x; g.transform.localScale = Vector3.one * alto;
                objs.Add(g); return g;
            }
            var viejo = Poner(RutaFuego, -alto * 0.9f); var nuevo = Poner(RutaToon, alto * 0.9f);
            foreach (var g in new[] { viejo, nuevo })
            {
                foreach (var ps in g.GetComponentsInChildren<ParticleSystem>(true)) if (ps.transform.parent == g.transform || ps.transform == g.transform) ps.Simulate(3f, true, true);
                foreach (var r in g.GetComponentsInChildren<ParticleSystemRenderer>(true))
                {
                    var ps = r.GetComponent<ParticleSystem>();
                    log.AppendLine($"{g.name}/{r.name}: {ps.particleCount} partículas, bounds tam {r.bounds.size}, centro y {r.bounds.center.y - base0.y:0.0}, scalingMode {ps.main.scalingMode}, mat {(r.sharedMaterial ? r.sharedMaterial.name : "-")}, renderMode {r.renderMode}");
                }
            }
            var camGo = new GameObject("__CamFuego") { hideFlags = HideFlags.HideAndDontSave }; objs.Add(camGo);
            var cam = camGo.AddComponent<Camera>(); cam.cullingMask = 1 << 31; cam.fieldOfView = 40;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.12f, 0.1f, 0.12f);
            cam.transform.position = base0 + new Vector3(0f, alto * 0.9f, -alto * 4.2f); cam.transform.LookAt(base0 + Vector3.up * alto * 0.8f);
            var rt = new RenderTexture(1280, 720, 24); var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
            RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); tex.Apply(); RenderTexture.active = null;
            System.IO.Directory.CreateDirectory("Logs/fuego_toon");
            System.IO.File.WriteAllBytes("Logs/fuego_toon/prueba.png", tex.EncodeToPNG());
            Object.DestroyImmediate(rt); Object.DestroyImmediate(tex);
        }
        finally { foreach (var o in objs) if (o != null) Object.DestroyImmediate(o); }
        System.IO.File.WriteAllText("Logs/fuego_toon/prueba.txt", log.ToString());
        Debug.Log("[FuegoToon] Prueba: Logs/fuego_toon/prueba.png\n" + log);
    }
}
