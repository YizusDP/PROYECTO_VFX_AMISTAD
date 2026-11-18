using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 2 · Ciudad > Goku: aura Ki y SSJ
/// Arma la carga de Ki y la transformación a Super Saiyajin sobre la toma CM4 (tiempos del Timeline principal,
/// los mismos que GokuVueloEditor):
///   35.0 - 38.93  carga de Ki: aura delgada blanco-azul que late cada vez más rápido, motas que se juntan hacia el
///                 cuerpo, lenguas azules y los primeros rayos.
///   38.93         cambio Goku_Base -> Goku_SSJ: destello blanco (tapa el cambio de modelo), golpe de luz, estallido.
///   38.93 - 43.3  aura dorada de SSJ: cascarón de llamas toon, lenguas doradas, motas que suben y rayos.
/// Qué crea:
///   1. Materiales en Assets/Assets/shaders/AuraKi/ (shaders "Amistad/Aura Ki", "Amistad/Rayo Ki" y "Amistad/Fuego VFX").
///   2. Cascarones: junto a cada SkinnedMeshRenderer de Goku_Base y Goku_SSJ, una copia "Aura_..." con los mismos huesos
///      y una malla de UN solo submesh (todas las partes juntas): 1 malla = 2 draw calls (brillo + llamas), en vez de
///      2 por cada material de Goku. Mallas en Assets/Assets/3d/Persoanjes/Goku/Aura/.
///   3. GOKU/AURA_KI: script AuraKi + Animator (para la pista), luz, 6 rayos (LineRenderer) y las partículas.
///   4. En el Timeline de la ciudad, grupo "Goku": pista de animación "Goku · aura Ki" (curvas de carga, mezcla,
///      destello, rayos y tiempo en Goku_AuraKi.anim, se pueden retocar en el Timeline) y 3 Control Tracks de partículas.
/// Correr DESPUÉS de "CM4: primera persona en la avioneta + Goku SSJ". Se puede volver a correr (reemplaza lo anterior).
/// Tools > Amistad > 2 · Ciudad > Goku: quitar aura Ki  deja todo como estaba antes.
/// </summary>
public static class GokuAuraEditor
{
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string CarpetaMallas = "Assets/Assets/3d/Persoanjes/Goku/Aura";
    const string CarpetaMat = "Assets/Assets/shaders/AuraKi";
    const string RutaRuido = "Assets/Assets/shaders/TexturaS/Ruido_Fuego.png";
    const string RutaClip = CarpetaMallas + "/Goku_AuraKi.anim";
    const float InicioCiudad = 17.5f;

    // Tiempos del Timeline principal (ver GokuVueloEditor)
    const float PowerUp = 35.0f;
    const float Swap = 35.0f + 118f / 30f;     // 38.93: se apaga Goku_Base y aparece Goku_SSJ
    const float Fin = 43.3f;                    // Goku_SSJ deja de verse
    const int NumRayos = 6;

    const string PistaAura = "Goku · aura Ki";
    const string PistaCarga = "Goku · VFX carga Ki";
    const string PistaEstallido = "Goku · VFX estallido SSJ";
    const string PistaSSJ = "Goku · VFX aura SSJ";
    static readonly string[] NuestrasPistas = { PistaAura, PistaCarga, PistaEstallido, PistaSSJ };

    static readonly List<ParticleSystemVertexStream> Streams = new List<ParticleSystemVertexStream>
    {
        ParticleSystemVertexStream.Position,
        ParticleSystemVertexStream.Color,
        ParticleSystemVertexStream.UV,
        ParticleSystemVertexStream.StableRandomX,
        ParticleSystemVertexStream.AgePercent
    };

    // ======================================================================================================
    [MenuItem("Tools/Amistad/2 · Ciudad/Goku: aura Ki y SSJ", false, 223)]
    static void Crear()
    {
        var log = new StringBuilder();
        if (!Buscar(out var tlC, out var dirC, out var goku, out var gBase, out var gSSJ)) return;

        var shAura = Shader.Find("Amistad/Aura Ki");
        var shRayo = Shader.Find("Amistad/Rayo Ki");
        var shFuego = Shader.Find("Amistad/Fuego VFX");
        var ruido = AssetDatabase.LoadAssetAtPath<Texture2D>(RutaRuido);
        if (shAura == null || shRayo == null || shFuego == null || ruido == null)
        { Debug.LogError("[Aura Ki] Faltan shaders (Aura Ki, Rayo Ki, Fuego VFX) o la textura Ruido_Fuego.png."); return; }
        Carpeta(CarpetaMallas); Carpeta(CarpetaMat);

        // ---------- 1. Materiales ----------
        var matLlamas = Mat("Aura_Llamas", shAura, m =>
        {
            m.SetTexture("_Ruido", ruido);
            m.SetFloat("_Grosor", 1.6f); m.SetFloat("_Llamas", 6f); m.SetFloat("_Caida", 2.2f);
            m.SetFloat("_Umbral", 0.28f); m.SetFloat("_Borde", 0.06f); m.SetFloat("_Interior", 0.12f);
            m.SetFloat("_CorteMedio", 0.5f); m.SetFloat("_CortePunta", 0.78f); m.SetFloat("_Intensidad", 1.4f);
            m.renderQueue = 3002;
        });
        var matBrillo = Mat("Aura_Brillo", shAura, m =>
        {
            // Pegado al cuerpo: casi sin lenguas, solo el contorno brillante (el "glow" de la silueta)
            m.SetTexture("_Ruido", ruido);
            m.SetFloat("_Grosor", 0.35f); m.SetFloat("_Llamas", 0.6f); m.SetFloat("_Caida", 1.3f);
            m.SetFloat("_Umbral", 0.12f); m.SetFloat("_Borde", 0.04f); m.SetFloat("_Interior", 0.3f);
            m.SetFloat("_CorteMedio", 0.4f); m.SetFloat("_CortePunta", 0.7f); m.SetFloat("_Intensidad", 1.1f);
            m.SetFloat("_EscalaRuido", 0.15f);
            m.renderQueue = 3001;
        });
        var matRayo = Mat("Rayo_Ki", shRayo, m => { });
        var fuegoSSJ = MatFuego("Aura_Fuego_SSJ", shFuego, ruido, 0,
            new Color(2.4f, 0.8f, 0.05f), new Color(3.8f, 2.4f, 0.3f), new Color(4.5f, 4.2f, 1.9f), new Color(1.4f, 0.4f, 0.02f));
        var fuegoKi = MatFuego("Aura_Fuego_Ki", shFuego, ruido, 0,
            new Color(0.25f, 0.55f, 2.2f), new Color(0.8f, 1.3f, 3.2f), new Color(2.6f, 3.0f, 3.6f), new Color(0.12f, 0.3f, 1.4f));
        var motasSSJ = MatFuego("Aura_Motas_SSJ", shFuego, ruido, 1,
            new Color(2.4f, 0.8f, 0.05f), new Color(4f, 2.8f, 0.4f), new Color(6f, 5.5f, 2.5f), Color.black);
        var motasKi = MatFuego("Aura_Motas_Ki", shFuego, ruido, 1,
            new Color(0.3f, 0.6f, 2f), new Color(1.2f, 1.8f, 4f), new Color(4f, 4.5f, 6f), Color.black);
        log.AppendLine("Materiales en " + CarpetaMat);

        // ---------- 2. Cascarones ----------
        Quitar(goku, gBase, gSSJ, tlC, dirC, silencioso: true);
        HacerLegibles(new[] { gBase, gSSJ }, log);
        var cascarones = new List<Renderer>();
        foreach (var modelo in new[] { gBase, gSSJ })
        {
            foreach (var smr in modelo.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => !x.name.StartsWith("Aura_")).ToList())
            {
                if (smr.sharedMesh == null) continue;
                var malla = MallaCascaron(smr.sharedMesh, $"{CarpetaMallas}/Cascaron_{Limpio(modelo.name)}_{Limpio(smr.name)}.asset");
                var go = new GameObject("Aura_" + smr.name);
                Undo.RegisterCreatedObjectUndo(go, "Aura Ki");
                go.layer = smr.gameObject.layer;
                go.transform.SetParent(smr.transform.parent, false);
                go.transform.localPosition = smr.transform.localPosition;
                go.transform.localRotation = smr.transform.localRotation;
                go.transform.localScale = smr.transform.localScale;
                var r = go.AddComponent<SkinnedMeshRenderer>();
                r.sharedMesh = malla;
                r.bones = smr.bones;
                r.rootBone = smr.rootBone;
                r.quality = smr.quality;
                r.updateWhenOffscreen = false;
                var b = smr.localBounds;
                float esc = smr.rootBone != null ? Mathf.Max(0.001f, smr.rootBone.lossyScale.x) : 1f;
                b.Expand(24f / esc);                        // las llamas salen ~8 u del cuerpo: que no lo recorte el culling
                r.localBounds = b;
                r.sharedMaterials = new[] { matBrillo, matLlamas };   // un submesh + 2 materiales = 2 pasadas sobre la misma malla
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
                r.skinnedMotionVectors = false;
                r.enabled = false;                          // lo prende el script cuando hay carga
                cascarones.Add(r);
                log.AppendLine($"Cascarón {modelo.name}/{go.name}: {malla.vertexCount} vértices, {smr.sharedMesh.subMeshCount} submeshes -> 1");
            }
        }

        // ---------- 3. AURA_KI: script, luz, rayos y partículas ----------
        var raiz = new GameObject("AURA_KI");
        Undo.RegisterCreatedObjectUndo(raiz, "Aura Ki");
        raiz.transform.SetParent(goku, false);
        var anim = raiz.AddComponent<Animator>();
        anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var aura = raiz.AddComponent<AuraKi>();

        var vfx = Hijo(raiz.transform, "VFX");
        var luzGo = Hijo(vfx, "Luz_Aura").gameObject;
        luzGo.transform.localPosition = new Vector3(0f, 3f, 0f);
        var luz = luzGo.AddComponent<Light>();
        luz.type = LightType.Point; luz.range = 70f; luz.shadows = LightShadows.None; luz.intensity = 0f; luz.enabled = false;

        var rayosT = Hijo(raiz.transform, "Rayos");
        var lineas = new List<LineRenderer>();
        for (int i = 0; i < NumRayos; i++)
        {
            var lr = Hijo(rayosT, $"Rayo_{i}").gameObject.AddComponent<LineRenderer>();
            lr.useWorldSpace = true;
            lr.sharedMaterial = matRayo;
            lr.textureMode = LineTextureMode.Stretch;
            lr.alignment = LineAlignment.View;
            lr.widthCurve = new AnimationCurve(new Keyframe(0f, 0.25f), new Keyframe(0.15f, 1f), new Keyframe(0.8f, 0.8f), new Keyframe(1f, 0.05f));
            lr.numCapVertices = 0; lr.numCornerVertices = 0;
            lr.shadowCastingMode = ShadowCastingMode.Off; lr.receiveShadows = false;
            lr.positionCount = 2;
            lr.enabled = false;
            lineas.Add(lr);
        }

        // Carga de Ki: motas que se juntan hacia el cuerpo + lenguas azules
        var carga = Hijo(vfx, "Carga_Ki");
        {
            var ps = Sistema(carga, "Motas_Ki", motasKi, 11u);
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(0.7f, 1.0f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.35f, 0.7f); m.maxParticles = 200;
            var e = ps.emission; e.rateOverTime = 70f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 13f; s.radiusThickness = 0f; s.position = new Vector3(0f, 2f, 0f);
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
            v.x = new ParticleSystem.MinMaxCurve(0f); v.y = new ParticleSystem.MinMaxCurve(0f); v.z = new ParticleSystem.MinMaxCurve(0f);
            v.radial = new ParticleSystem.MinMaxCurve(-16f);          // hacia el centro: el Ki se junta en Goku
            Alfa(ps, (0f, 0f), (1f, 0.2f), (1f, 0.8f), (0f, 1f));
            Tamano(ps, new Keyframe(0f, 1f), new Keyframe(1f, 0.4f));

            var ll = Sistema(carga, "Llamas_Ki", fuegoKi, 12u);
            Llamas(ll, 22f, 0.45f, 0.7f, 9f, 14f, 3f, 5f, 3.5f);
        }

        // Estallido al transformarse (una sola ráfaga)
        var estallido = Hijo(vfx, "Estallido_SSJ");
        {
            var ps = Sistema(estallido, "Chispas", motasSSJ, 21u);
            var m = ps.main; m.loop = false; m.duration = 1.2f;
            m.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f); m.startSpeed = new ParticleSystem.MinMaxCurve(30f, 55f);
            m.startSize = new ParticleSystem.MinMaxCurve(0.4f, 0.9f); m.maxParticles = 200;
            var e = ps.emission; e.rateOverTime = 0f; e.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)140) });
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 2f; s.position = new Vector3(0f, 2f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 4f; lim.dampen = 0.12f;   // frenan como chispas
            Alfa(ps, (1f, 0f), (1f, 0.6f), (0f, 1f));

            var on = Sistema(estallido, "Onda_Llamas", fuegoSSJ, 22u);
            var m2 = on.main; m2.loop = false; m2.duration = 1.2f;
            m2.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.6f); m2.startSpeed = new ParticleSystem.MinMaxCurve(18f, 28f);
            m2.startSize = new ParticleSystem.MinMaxCurve(5f, 8f); m2.maxParticles = 60;
            var e2 = on.emission; e2.rateOverTime = 0f; e2.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)40) });
            var s2 = on.shape; s2.shapeType = ParticleSystemShapeType.Sphere; s2.radius = 3f; s2.scale = new Vector3(1f, 1.8f, 1f); s2.position = new Vector3(0f, 2f, 0f);
            var lim2 = on.limitVelocityOverLifetime; lim2.enabled = true; lim2.limit = 3f; lim2.dampen = 0.2f;
            Alfa(on, (0f, 0f), (1f, 0.1f), (1f, 0.6f), (0f, 1f));
            Tamano(on, new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.7f));
        }

        // Aura de SSJ: lenguas doradas + motas que suben (como en las referencias)
        var aSSJ = Hijo(vfx, "Aura_SSJ");
        {
            var ll = Sistema(aSSJ, "Llamas_SSJ", fuegoSSJ, 31u);
            Llamas(ll, 50f, 0.45f, 0.75f, 12f, 20f, 4f, 7f, 4f);

            var ps = Sistema(aSSJ, "Motas_SSJ", motasSSJ, 32u);
            var m = ps.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.5f); m.startSpeed = 0f;
            m.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f); m.maxParticles = 120;
            var e = ps.emission; e.rateOverTime = 30f;
            var s = ps.shape; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = 9f; s.radiusThickness = 1f;
            s.scale = new Vector3(1f, 1.6f, 1f); s.position = new Vector3(0f, 2f, 0f);
            var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
            v.x = new ParticleSystem.MinMaxCurve(0f, 0f); v.y = new ParticleSystem.MinMaxCurve(5f, 9f); v.z = new ParticleSystem.MinMaxCurve(0f, 0f);
            Alfa(ps, (0f, 0f), (1f, 0.2f), (1f, 0.7f), (0f, 1f));
            Ruido(ps, 1.2f, 0.3f, 0.4f);
        }
        foreach (var g in new[] { carga, estallido, aSSJ }) g.gameObject.SetActive(false);   // los prende el Control Track

        Undo.RecordObject(aura, "Aura Ki");
        aura.modelos = new[] { gBase.GetComponent<Animator>(), gSSJ.GetComponent<Animator>() };
        aura.cascarones = cascarones.ToArray();
        aura.vfx = vfx;
        aura.luz = luz;
        aura.lineasRayo = lineas.ToArray();
        EditorUtility.SetDirty(aura);

        // ---------- 4. Curvas y pistas del Timeline ----------
        var clip = ClipAura(log);
        Undo.RecordObject(tlC, "Aura Ki"); Undo.RecordObject(dirC, "Aura Ki");
        var grupo = tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "Goku") ?? tlC.CreateTrack<GroupTrack>(null, "Goku");

        var pA = tlC.CreateTrack<AnimationTrack>(grupo, PistaAura);
        var tc = pA.CreateClip(clip);
        tc.start = 0; tc.duration = 60; tc.displayName = "Carga Ki -> SSJ";
        ((AnimationPlayableAsset)tc.asset).applyFootIK = false;
        dirC.SetGenericBinding(pA, anim);

        Control(tlC, dirC, grupo, PistaCarga, carga.gameObject, PowerUp + 0.3f, Swap + 0.02f, 101u, log);
        Control(tlC, dirC, grupo, PistaEstallido, estallido.gameObject, Swap - 0.08f, Swap + 1.2f, 102u, log);
        Control(tlC, dirC, grupo, PistaSSJ, aSSJ.gameObject, Swap - 0.03f, Fin, 103u, log);

        EditorUtility.SetDirty(tlC);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(goku.gameObject.scene);
        aura.Aplicar();

        log.AppendLine($"Carga {PowerUp:0.00}-{Swap:0.00} s, cambio a SSJ en {Swap:0.00} s, aura SSJ hasta {Fin:0.00} s (tiempos del principal).");
        System.IO.Directory.CreateDirectory("Logs/goku_aura");
        System.IO.File.WriteAllText("Logs/goku_aura/info.txt", log.ToString());
        Debug.Log("[Aura Ki] Listo. Guarda la escena con Ctrl+S y revisa el Timeline de 35 a 43 s.\n" + log);
    }

    [MenuItem("Tools/Amistad/2 · Ciudad/Goku: quitar aura Ki", false, 224)]
    static void QuitarMenu()
    {
        if (!Buscar(out var tlC, out var dirC, out var goku, out var gBase, out var gSSJ)) return;
        Quitar(goku, gBase, gSSJ, tlC, dirC, silencioso: false);
        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(goku.gameObject.scene);
    }

    // ======================================================================================================
    static bool Buscar(out TimelineAsset tlC, out PlayableDirector dirC, out Transform goku, out Transform gBase, out Transform gSSJ)
    {
        tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var tl = tlC;
        dirC = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(d => d.playableAsset == tl);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        goku = todos.FirstOrDefault(t => t != null && t.name == "GOKU");
        gBase = goku != null ? goku.Find("Goku_Base") : null;
        gSSJ = goku != null ? goku.Find("Goku_SSJ") : null;
        if (tlC == null || dirC == null || goku == null || gBase == null || gSSJ == null
            || gBase.GetComponent<Animator>() == null || gSSJ.GetComponent<Animator>() == null)
        {
            Debug.LogError("[Aura Ki] Falta algo: Timeline de la ciudad, su Director, GOKU, GOKU/Goku_Base o GOKU/Goku_SSJ (con Animator). " +
                           "Abre la escena ciudad y corre antes 'CM4: primera persona en la avioneta + Goku SSJ'.");
            return false;
        }
        return true;
    }

    static void Quitar(Transform goku, Transform gBase, Transform gSSJ, TimelineAsset tlC, PlayableDirector dirC, bool silencioso)
    {
        int n = 0;
        foreach (var modelo in new[] { gBase, gSSJ })
            foreach (var r in modelo.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(x => x.name.StartsWith("Aura_")).ToList())
            { Undo.DestroyObjectImmediate(r.gameObject); n++; }
        var viejo = goku.Find("AURA_KI");
        if (viejo != null) { Undo.DestroyObjectImmediate(viejo.gameObject); n++; }
        Undo.RecordObject(tlC, "Quitar aura Ki");
        foreach (var p in tlC.GetOutputTracks().Where(x => NuestrasPistas.Contains(x.name)).ToList()) { tlC.DeleteTrack(p); n++; }
        EditorUtility.SetDirty(tlC);
        if (!silencioso) Debug.Log($"[Aura Ki] Quitado ({n} objetos/pistas). Las mallas y materiales siguen en {CarpetaMallas} y {CarpetaMat}.");
    }

    /// Los FBX de Goku vienen con Read/Write apagado; para copiar la malla del cascarón hay que leer sus triángulos.
    /// Se prende una sola vez en el importador (queda guardado en el .meta).
    static void HacerLegibles(Transform[] modelos, StringBuilder log)
    {
        var rutas = modelos.SelectMany(m => m.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            .Where(x => x.sharedMesh != null && !x.sharedMesh.isReadable)
            .Select(x => AssetDatabase.GetAssetPath(x.sharedMesh)).Where(x => !string.IsNullOrEmpty(x)).Distinct().ToList();
        foreach (var ruta in rutas)
        {
            if (AssetImporter.GetAtPath(ruta) is ModelImporter mi && !mi.isReadable)
            {
                mi.isReadable = true;
                mi.SaveAndReimport();
                log.AppendLine($"Read/Write activado en {ruta} (necesario para copiar la malla).");
            }
        }
    }

    /// Copia de la malla con UN solo submesh (todos los triángulos juntos). Si el asset ya existe se sobreescribe
    /// conservando su GUID.
    static Mesh MallaCascaron(Mesh fuente, string ruta)
    {
        var m = Object.Instantiate(fuente);
        m.name = "Cascaron_" + fuente.name;
        var tris = new List<int>();
        for (int s = 0; s < fuente.subMeshCount; s++)
            if (fuente.GetTopology(s) == MeshTopology.Triangles) tris.AddRange(fuente.GetTriangles(s));
        m.subMeshCount = 1;
        m.SetTriangles(tris, 0, true);
        m.ClearBlendShapes();                      // el aura no necesita las caras
        var existe = AssetDatabase.LoadAssetAtPath<Mesh>(ruta);
        if (existe == null) { AssetDatabase.CreateAsset(m, ruta); return m; }
        EditorUtility.CopySerialized(m, existe);
        Object.DestroyImmediate(m);
        EditorUtility.SetDirty(existe);
        return existe;
    }

    static AnimationClip ClipAura(StringBuilder log)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(RutaClip);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, RutaClip); }
        clip.ClearCurves(); clip.frameRate = 30;
        clip.name = "Goku_AuraKi";

        // Tiempo del clip = tiempo de la ciudad (el clip empieza en 0); t = tiempo del principal
        var cCarga = new AnimationCurve(); var cMezcla = new AnimationCurve(); var cDest = new AnimationCurve(); var cRayos = new AnimationCurve();
        void Todas(float lt, float t)
        { cCarga.AddKey(lt, Carga(t)); cMezcla.AddKey(lt, Mezcla(t)); cDest.AddKey(lt, Destello(t)); cRayos.AddKey(lt, Rayos(t)); }
        Todas(0f, InicioCiudad);
        float t0 = PowerUp - 0.5f, t1 = Fin + 0.3f;
        for (float t = t0; t <= t1 + 1e-4f; t += 1f / 30f) Todas(t - InicioCiudad, t);
        Todas(60f, InicioCiudad + 60f);
        var cTiempo = new AnimationCurve(new Keyframe(0f, InicioCiudad), new Keyframe(60f, InicioCiudad + 60f));

        void Poner(string campo, AnimationCurve c)
        {
            for (int k = 0; k < c.length; k++)
            {
                AnimationUtility.SetKeyLeftTangentMode(c, k, AnimationUtility.TangentMode.Linear);
                AnimationUtility.SetKeyRightTangentMode(c, k, AnimationUtility.TangentMode.Linear);
            }
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(AuraKi), campo), c);
        }
        Poner("carga", cCarga); Poner("mezcla", cMezcla); Poner("destello", cDest); Poner("rayos", cRayos); Poner("tiempo", cTiempo);
        EditorUtility.SetDirty(clip);
        log.AppendLine($"Curvas en {RutaClip} ({cCarga.length} claves por curva).");
        return clip;
    }

    // ---------- Curvas (t = segundos del Timeline principal) ----------
    static float S(float a, float b, float t) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, t));
    static float Apaga(float t) => 1f - S(Fin - 0.3f, Fin, t);

    static float Carga(float t)
    {
        if (t < PowerUp - 0.2f) return 0f;
        if (t < Swap)
        {
            // Sube de 0.3 a 0.75 mientras "late": latidos de 2.5 a 6 por segundo, cada vez más fuertes
            float D = Swap - PowerUp, tau = Mathf.Max(0f, t - PowerUp), u = Mathf.Clamp01(tau / D);
            float baseC = S(PowerUp - 0.2f, PowerUp + 0.6f, t) * Mathf.Lerp(0.3f, 0.75f, Mathf.InverseLerp(PowerUp + 0.6f, Swap - 0.15f, t));
            float fase = 2f * Mathf.PI * (2.5f * tau + (6f - 2.5f) * tau * tau / (2f * D));
            float latido = Mathf.Pow(0.5f + 0.5f * Mathf.Sin(fase), 3f) * Mathf.Lerp(0.08f, 0.2f, u);
            return Mathf.Clamp01(baseC + latido);
        }
        return (0.93f + 0.07f * Mathf.Sin(t * 19f)) * Apaga(t);   // SSJ: completa, "ruge" un poco
    }

    static float Mezcla(float t) => S(Swap - 0.1f, Swap + 0.05f, t);

    static float Destello(float t) => S(Swap - 0.2f, Swap - 0.02f, t) * (1f - S(Swap + 0.1f, Swap + 0.7f, t));

    static float Rayos(float t)
    {
        float golpe = 0.5f * S(Swap - 0.1f, Swap, t) * (1f - S(Swap + 0.2f, Swap + 1.0f, t));   // ráfaga al transformarse
        float r = t < Swap ? 0.15f * S(36.3f, 36.8f, t) + 0.35f * S(37.5f, Swap, t) : 0.45f;
        return Mathf.Clamp01(r + golpe) * Apaga(t);
    }

    // ---------- Timeline ----------
    static void Control(TimelineAsset tl, PlayableDirector dir, GroupTrack grupo, string nombre, GameObject go, float ini, float fin, uint semilla, StringBuilder log)
    {
        var p = tl.CreateTrack<ControlTrack>(grupo, nombre);
        var c = p.CreateClip<ControlPlayableAsset>();
        c.start = ini - InicioCiudad;
        c.duration = fin - ini;
        c.displayName = go.name;
        var cpa = (ControlPlayableAsset)c.asset;
        cpa.sourceGameObject.exposedName = GUID.Generate().ToString();
        dir.SetReferenceValue(cpa.sourceGameObject.exposedName, go);
        cpa.updateParticle = true;
        cpa.particleRandomSeed = semilla;     // mismas partículas cada vez que se reproduce
        cpa.active = true;
        cpa.postPlayback = ActivationControlPlayable.PostPlaybackState.Inactive;
        cpa.updateDirector = false;
        cpa.updateITimeControl = false;
        cpa.searchHierarchy = false;
        log.AppendLine($"Pista '{nombre}': {go.name} {ini:0.00}-{fin:0.00} s");
    }

    // ---------- Partículas ----------
    static ParticleSystem Sistema(Transform padre, string nombre, Material mat, uint semilla)
    {
        var go = Hijo(padre, nombre).gameObject;
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.useAutoRandomSeed = false;
        ps.randomSeed = semilla;
        var m = ps.main;
        m.loop = true;
        m.prewarm = false;
        m.playOnAwake = true;
        m.startColor = Color.white;
        m.simulationSpace = ParticleSystemSimulationSpace.Local;   // el aura viaja con Goku (va a la velocidad de la avioneta)
        m.scalingMode = ParticleSystemScalingMode.Local;
        var s = ps.shape; s.enabled = true;
        var r = go.GetComponent<ParticleSystemRenderer>();
        r.sharedMaterial = mat;
        r.renderMode = ParticleSystemRenderMode.Billboard;
        r.alignment = ParticleSystemRenderSpace.View;
        r.maxParticleSize = 3f;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.SetActiveVertexStreams(Streams);
        return ps;
    }

    /// Lenguas de fuego toon que suben desde un elipsoide alrededor del cuerpo
    static void Llamas(ParticleSystem ps, float tasa, float vidaMin, float vidaMax, float velMin, float velMax, float tamMin, float tamMax, float radio)
    {
        var m = ps.main;
        m.startLifetime = new ParticleSystem.MinMaxCurve(vidaMin, vidaMax);
        m.startSpeed = 0f;
        m.startSize = new ParticleSystem.MinMaxCurve(tamMin, tamMax);
        m.maxParticles = 150;
        var e = ps.emission; e.rateOverTime = tasa;
        var s = ps.shape; s.shapeType = ParticleSystemShapeType.Sphere; s.radius = radio; s.radiusThickness = 0.2f;
        s.scale = new Vector3(1.1f, 2.4f, 0.9f); s.position = new Vector3(0f, 1.5f, 0f);
        var v = ps.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.Local;
        v.x = new ParticleSystem.MinMaxCurve(0f, 0f); v.y = new ParticleSystem.MinMaxCurve(velMin, velMax); v.z = new ParticleSystem.MinMaxCurve(0f, 0f);
        Alfa(ps, (0f, 0f), (1f, 0.15f), (1f, 0.7f), (0f, 1f));
        Tamano(ps, new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0.4f));
        Ruido(ps, 1.5f, 0.4f, 0.5f);
    }

    static void Tamano(ParticleSystem ps, params Keyframe[] k)
    {
        var sol = ps.sizeOverLifetime;
        sol.enabled = true;
        sol.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(k));
    }

    static void Alfa(ParticleSystem ps, params (float a, float t)[] claves)
    {
        var col = ps.colorOverLifetime;
        col.enabled = true;
        var g = new Gradient();
        g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                  claves.Select(c => new GradientAlphaKey(c.a, c.t)).ToArray());
        col.color = new ParticleSystem.MinMaxGradient(g);
    }

    static void Ruido(ParticleSystem ps, float fuerza, float frecuencia, float desplazamiento)
    {
        var n = ps.noise;
        n.enabled = true;
        n.strength = fuerza;
        n.frequency = frecuencia;
        n.scrollSpeed = desplazamiento;
        n.damping = true;
        n.quality = ParticleSystemNoiseQuality.Medium;
    }

    // ---------- Materiales ----------
    static Material Mat(string nombre, Shader sh, System.Action<Material> config)
    {
        string ruta = $"{CarpetaMat}/{nombre}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(ruta);
        if (m == null) { m = new Material(sh); AssetDatabase.CreateAsset(m, ruta); }
        m.shader = sh;
        config(m);
        EditorUtility.SetDirty(m);
        return m;
    }

    /// Material del shader de fuego del proyecto. modo 0 = llama (estilo toon, mezcla premultiplicada), 1 = brasa (aditivo)
    static Material MatFuego(string nombre, Shader sh, Texture ruido, int modo, Color baseC, Color medio, Color punta, Color borde)
    {
        return Mat(nombre, sh, m =>
        {
            m.SetFloat("_Modo", modo);
            m.SetTexture("_Ruido", ruido);
            m.SetColor("_ColorBase", baseC); m.SetColor("_ColorMedio", medio); m.SetColor("_ColorPunta", punta);
            m.SetFloat("_Intensidad", 1.2f);
            m.SetFloat("_SuavidadSuelo", 2f);
            if (modo == 0)
            {
                m.SetFloat("_Toon", 1f); m.SetFloat("_Umbral", 0.16f); m.SetFloat("_Borde", 0.07f);
                m.SetColor("_ColorBorde", borde); m.SetFloat("_CorteMedio", 0.42f); m.SetFloat("_CortePunta", 0.72f);
                m.SetFloat("_Cubre", 0.3f); m.SetFloat("_Distorsion", 0.3f); m.SetFloat("_Ancho", 0.8f);
                m.SetFloat("_Erosion", 0.9f); m.SetFloat("_Velocidad", 1.8f);
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            }
            else
            {
                m.SetFloat("_SrcBlend", (float)BlendMode.One);
                m.SetFloat("_DstBlend", (float)BlendMode.One);
            }
        });
    }

    // ---------- Utilidades ----------
    static Transform Hijo(Transform padre, string nombre)
    {
        var go = new GameObject(nombre);
        Undo.RegisterCreatedObjectUndo(go, "Aura Ki");
        go.transform.SetParent(padre, false);
        return go.transform;
    }

    static void Carpeta(string ruta)
    {
        if (AssetDatabase.IsValidFolder(ruta)) return;
        var partes = ruta.Split('/');
        string actual = partes[0];
        for (int i = 1; i < partes.Length; i++)
        {
            string sig = actual + "/" + partes[i];
            if (!AssetDatabase.IsValidFolder(sig)) AssetDatabase.CreateFolder(actual, partes[i]);
            actual = sig;
        }
    }

    static string Limpio(string s) => new string(s.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
}
