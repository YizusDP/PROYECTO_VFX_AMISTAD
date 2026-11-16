using System.IO;
using System.Linq;
using System.Text;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Tools > Amistad > 3 · Encuentro, saludo y final > Solo: final (explosión, negro y título)
/// Nuevo final del corto (la Escena 8 lo corre sola al final):
///  - 62.0-67.5 s  CM8_9_FINAL FIJA (sin movimiento; el temblor de la explosión lo pone NuclearDetalleEditor) en un plano abierto: ellos en la ciudad y espacio para el hongo.
///  - 63.4 s       detonación NUCLEAR (FINAL_NUCLEAR): destello blanco de pantalla + luz enorme que ilumina la ciudad,
///                 bola de fuego, anillo de choque, onda de polvo sobre el piso, hongo (tallo + copa + fuego) y brasas;
///                 Pistas "Final · ..." en la ciudad.
///  - 66.4 s       corte seco a negro (Saludo_negro_peso) y se queda en negro.
///  - 67.5-72.9 s  el título #AMISTAD sobre negro (el saludo fue tan épico que todo quedó en negro): copias de las letras
///                 del título (TITULO_FINAL, lejos de todo) con fondo negro, cámara propia que se acerca despacio
///                 (CM_TITULO_FINAL) y fundido desde negro / a negro. La ciudad termina a los 65.6 s.
///  SALUDO_EPICO (con sus Volumes globales: negro, look rojo...) solo está activo de 17.5 a 65.6 s (pista
///  "Final · saludo activo" del principal) para que no tiña ni tape el título.
/// Se puede volver a correr.
/// </summary>
public static class FinalTituloEditor
{
    const string TimelinePrincipal = "Assets/Animaciones/Director_P1_CascadaTimeline.playable";
    const string TimelineCiudad = "Assets/Animaciones/Director_P2_CiudadTimeline.playable";
    const string Carpeta = "Assets/Animaciones/Saludo";
    const string RutaExplosion = "Assets/Assets/shaders/Explosión horizontal.prefab";
    const float InicioCiudad = 17.5f;

    // tiempos del principal
    const float IniAlejar = 62.0f, FinAlejar = 63.4f;    // la cámara se aleja hasta la detonación
    const float Explosion = 63.4f;                        // detonación nuclear
    const float IniNegro = 66.4f, FinNegro = 67.0f;
    const float IniTitulo = 67.5f, FinCorto = 72.9f;
    // título final sobre negro
    static readonly Vector3 LugarTitulo = new Vector3(0f, 6000f, 0f);   // lejos de todo (arriba del mundo)
    const float FundidoEntra = 1.2f, FundidoSale = 0.8f, Acercamiento = 0.12f;   // acercamiento: 12% de la distancia

    // cámara que se aleja: cuántas veces su distancia inicial y cuánto sube (u)
    const float VecesDistancia = 5f, SubeAlFinal = 12f, FovAlejar = 45f;   // antes 50
    // cámara FIJA (sin movimiento ni temblor): plano abierto con ellos en la ciudad y espacio arriba para el hongo
    const float DistanciaFija = 4f, SubeFija = 12f, MiraFija = 12f;   // veces la distancia del previz, altura y punto de mira (u). Antes 7 / 20 / 40 (se veían muy chiquitos)
    const float InclinacionFinal = 0.3f;              // al final la cámara queda casi horizontal (descubre la ciudad), no en picada
    const float MiraArribaFinal = 14f;                // y mira un poco por encima del agarre (cielo y edificios en llamas)
    // explosión: detrás de ellos (vista desde la cámara), en el piso
    const float ExplosionDetras = 0f;    // explosión nuclear justo debajo de sus manos: nace de la bola de energía (antes 10 u detrás)

    static readonly string Pt = " · ";
    public const string CarpetaSaludoFinal = "Ciudad · saludo y final";   // carpeta de la jerarquía (si existe)

    [MenuItem("Tools/Amistad/3 · Encuentro, saludo y final/Solo: final (explosión, negro y título)", false, 314)]
    static void Menu()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Debug.LogError("[Final] Sal de Play antes de correr la herramienta."); return; }
        var log = new StringBuilder();
        if (Aplicar(log))
        {
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/final_titulo.txt", log.ToString());
            Debug.Log("[Final] Listo. Guarda la escena con Ctrl+S.\n" + log);
        }
        else Debug.LogError("[Final] No se pudo.\n" + log);
    }

    public static bool Aplicar(StringBuilder log)
    {
        var tlP = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelinePrincipal);
        var tlC = AssetDatabase.LoadAssetAtPath<TimelineAsset>(TimelineCiudad);
        var dirs = Object.FindObjectsByType<PlayableDirector>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var dirP = dirs.FirstOrDefault(d => d.playableAsset == tlP);
        var dirC = dirs.FirstOrDefault(d => d.playableAsset == tlC);
        var todos = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Transform T(string n) => todos.FirstOrDefault(t => t != null && t.name == n);
        var city = T("CITY"); var raiz = T("SALUDO_EPICO"); var cam = T("CM8_9_FINAL"); var negro = T("SALUDO_VOLUMEN_NEGRO");
        if (dirP == null || dirC == null || city == null || raiz == null || cam == null || negro == null)
        { log.AppendLine("Falta algo (directores, CITY, SALUDO_EPICO, CM8_9_FINAL o SALUDO_VOLUMEN_NEGRO). Corre antes la Escena 8."); return false; }
        Undo.RecordObject(tlP, "Final"); Undo.RecordObject(tlC, "Final");

        // ---------- punto del agarre (entre los dos, a la altura de las manos) a los 62 s
        AnimationTrack Rec(string pref) => tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(x => !(x.parent is AnimationTrack) && x.name.StartsWith(pref) && x.name.Contains("recorrido"));
        Vector3 Pos(AnimationTrack r, float tPrincipal)
        {
            float local = tPrincipal - InicioCiudad;
            float E(string p) { var cu = AnimationUtility.GetEditorCurve(r.infiniteClip, EditorCurveBinding.FloatCurve("", typeof(Transform), p)); return cu != null ? cu.Evaluate(local) : 0f; }
            return city.TransformPoint(r.infiniteClipOffsetPosition + Quaternion.Euler(r.infiniteClipOffsetEulerAngles) * new Vector3(E("m_LocalPosition.x"), E("m_LocalPosition.y"), E("m_LocalPosition.z")));
        }
        var rA = Rec("Ale"); var rM = Rec("MC");
        if (rA?.infiniteClip == null || rM?.infiniteClip == null) { log.AppendLine("No encuentro las pistas de recorrido."); return false; }
        var piso = (Pos(rA, IniAlejar) + Pos(rM, IniAlejar)) * 0.5f;
        var agarre = piso + Vector3.up * 10f;

        // ---------- 1. la cámara final se aleja
        var pistaMov = tlC.GetOutputTracks().OfType<AnimationTrack>().FirstOrDefault(x => x.name == "CM8_9_FINAL" + Pt + "mov");
        var clipPrevio = AssetDatabase.LoadAssetAtPath<AnimationClip>(Carpeta + "/CM8_9_FINAL.anim");
        if (pistaMov == null || clipPrevio == null) { log.AppendLine("No encuentro la pista 'CM8_9_FINAL · mov' o su clip."); return false; }
        float E0(string p) { var cu = AnimationUtility.GetEditorCurve(clipPrevio, EditorCurveBinding.FloatCurve("", typeof(Transform), p)); return cu != null ? cu.Evaluate(IniAlejar - 49f) : 0f; }
        var p0 = new Vector3(E0("m_LocalPosition.x"), E0("m_LocalPosition.y"), E0("m_LocalPosition.z"));
        if (cam.parent != null) p0 = cam.parent.TransformPoint(p0);
        var desde = p0 - agarre; float r0 = desde.magnitude; var dir = desde / Mathf.Max(0.001f, r0);
        var horiz = new Vector3(dir.x, 0f, dir.z); if (horiz.sqrMagnitude < 1e-4f) horiz = Vector3.back; horiz.Normalize();
        var dirFinal = (horiz + Vector3.up * InclinacionFinal).normalized;
        var clipAlejar = AssetDatabase.LoadAssetAtPath<AnimationClip>(Carpeta + "/CM8_9_ALEJAR.anim");
        if (clipAlejar == null) { clipAlejar = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clipAlejar, Carpeta + "/CM8_9_ALEJAR.anim"); }
        clipAlejar.ClearCurves();
        var cx = new AnimationCurve(); var cy = new AnimationCurve(); var cz = new AnimationCurve();
        var qx = new AnimationCurve(); var qy = new AnimationCurve(); var qz = new AnimationCurve(); var qw = new AnimationCurve();
        float durTotal = IniTitulo - IniAlejar;
        {
            var p = agarre + dirFinal * (r0 * DistanciaFija) + Vector3.up * SubeFija;
            var q = Quaternion.LookRotation(agarre + Vector3.up * MiraFija - p, Vector3.up);
            var pl = cam.parent != null ? cam.parent.InverseTransformPoint(p) : p;
            var ql = cam.parent != null ? Quaternion.Inverse(cam.parent.rotation) * q : q;
            foreach (float t in new[] { 0f, durTotal })
            {
                cx.AddKey(t, pl.x); cy.AddKey(t, pl.y); cz.AddKey(t, pl.z);
                qx.AddKey(t, ql.x); qy.AddKey(t, ql.y); qz.AddKey(t, ql.z); qw.AddKey(t, ql.w);
            }
        }
        foreach (var (prop, cu) in new[] { ("m_LocalPosition.x", cx), ("m_LocalPosition.y", cy), ("m_LocalPosition.z", cz),
                                           ("m_LocalRotation.x", qx), ("m_LocalRotation.y", qy), ("m_LocalRotation.z", qz), ("m_LocalRotation.w", qw) })
        {
            for (int k = 0; k < cu.length; k++) { AnimationUtility.SetKeyLeftTangentMode(cu, k, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(cu, k, AnimationUtility.TangentMode.Linear); }
            AnimationUtility.SetEditorCurve(clipAlejar, EditorCurveBinding.FloatCurve("", typeof(Transform), prop), cu);
        }
        EditorUtility.SetDirty(clipAlejar);
        foreach (var c in pistaMov.GetClips().ToList()) tlC.DeleteClip(c);
        var tcM = pistaMov.CreateClip(clipAlejar);
        tcM.start = IniAlejar - InicioCiudad; tcM.duration = IniTitulo - IniAlejar; tcM.displayName = "CM8_9_ALEJAR";
        if (tcM.asset is AnimationPlayableAsset apa) { apa.removeStartOffset = false; apa.position = Vector3.zero; apa.rotation = Quaternion.identity; EditorUtility.SetDirty(apa); }
        pistaMov.muted = false;
        EditorUtility.SetDirty(pistaMov);
        var vcam = cam.GetComponent<CinemachineCamera>();
        if (vcam != null) { Undo.RecordObject(vcam, "Final"); var lens = vcam.Lens; lens.FieldOfView = FovAlejar; vcam.Lens = lens; EditorUtility.SetDirty(vcam); }
        log.AppendLine($"CM8_9_FINAL: fija (sin movimiento) a {r0 * DistanciaFija:0} u del agarre, {SubeFija} u arriba, mirando a {MiraFija} u de altura; FOV {FovAlejar}.");

        // ---------- 2. explosión nuclear (destello, luz, bola de fuego, onda, hongo, brasas)
        var viejaExp = raiz.Find("FINAL_EXPLOSION"); if (viejaExp != null) Undo.DestroyObjectImmediate(viejaExp.gameObject);
        var viejoNuc = raiz.Find("FINAL_NUCLEAR"); if (viejoNuc != null) Undo.DestroyObjectImmediate(viejoNuc.gameObject);
        var atras = -horiz;
        var centroExp = piso + atras * ExplosionDetras;
        var nuc = new GameObject("FINAL_NUCLEAR"); Undo.RegisterCreatedObjectUndo(nuc, "Final");
        nuc.transform.SetParent(raiz, false); nuc.transform.position = centroExp;
        var sistemas = Nuclear(nuc.transform, log);
        if (sistemas == null) return false;
        var grupoC = tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "8" + Pt + "Saludo") ?? tlC.CreateTrack<GroupTrack>(null, "8" + Pt + "Saludo");
        foreach (var vieja in tlC.GetOutputTracks().Where(x => x.name.StartsWith("Final" + Pt)).ToList()) tlC.DeleteTrack(vieja);
        var pExp = tlC.CreateTrack<ControlTrack>(grupoC, "Final" + Pt + "explosi\u00F3n nuclear");
        foreach (var go in sistemas)
        {
            var tcE = pExp.CreateDefaultClip(); tcE.start = Explosion - InicioCiudad; tcE.duration = IniTitulo - Explosion; tcE.displayName = go.name;
            var ca = (ControlPlayableAsset)tcE.asset;
            var er = ca.sourceGameObject; if (PropertyName.IsNullOrEmpty(er.exposedName)) er.exposedName = GUID.Generate().ToString();
            er.defaultValue = null; ca.sourceGameObject = er;
            ca.updateParticle = true; ca.particleRandomSeed = (uint)(go.name.GetHashCode() & 0x7fffffff); ca.active = true;
            ca.updateDirector = false; ca.updateITimeControl = false; ca.searchHierarchy = false;
            dirC.SetReferenceValue(er.exposedName, go);
            go.SetActive(false);
            EditorUtility.SetDirty(ca);
        }
        // pistas de animación: luz del destello y Volume del destello
        foreach (var (hijo, clipNombre) in new[] { ("DESTELLO_LUZ", "Final_DestelloLuz"), ("DESTELLO_PANTALLA", "Final_DestelloPantalla") })
        {
            var t = nuc.transform.Find(hijo);
            var an = t.GetComponent<Animator>();
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>($"{Carpeta}/{clipNombre}.anim");
            var pa = tlC.CreateTrack<AnimationTrack>(grupoC, "Final" + Pt + hijo.ToLower().Replace('_', ' '));
            var tca = pa.CreateClip(clip); tca.start = Explosion - InicioCiudad; tca.duration = IniTitulo - Explosion; tca.displayName = clipNombre;
            if (tca.asset is AnimationPlayableAsset ap3) ap3.removeStartOffset = false;
            pa.trackOffset = TrackOffset.ApplySceneOffsets;
            dirC.SetGenericBinding(pa, an);
            EditorUtility.SetDirty(pa);
        }
        EditorUtility.SetDirty(pExp);
        NuclearDetalleEditor.Agregar(nuc.transform, tlC, dirC, grupoC, log);   // humo que los traga, escombros, condensación y temblor
        log.AppendLine($"FINAL_NUCLEAR en {centroExp}: destello blanco + luz que ilumina la ciudad, bola de fuego, onda de polvo, anillo de choque, hongo (tallo y copa) y brasas, a los {Explosion} s.");

        // ---------- 3. negro
        var clipNegro = AssetDatabase.LoadAssetAtPath<AnimationClip>(Carpeta + "/Saludo_negro_peso.anim");
        if (clipNegro != null)
        {
            var b = EditorCurveBinding.FloatCurve("", typeof(UnityEngine.Rendering.Volume), "weight");
            // corte seco a negro (sin fundido)
            var cn = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(IniNegro - InicioCiudad - 0.001f, 0f), new Keyframe(IniNegro - InicioCiudad, 1f), new Keyframe(60f, 1f));
            for (int k = 0; k < cn.length; k++) { AnimationUtility.SetKeyLeftTangentMode(cn, k, AnimationUtility.TangentMode.Constant); AnimationUtility.SetKeyRightTangentMode(cn, k, AnimationUtility.TangentMode.Constant); }
            Undo.RecordObject(clipNegro, "Final");
            AnimationUtility.SetEditorCurve(clipNegro, b, cn);
            EditorUtility.SetDirty(clipNegro);
        }
        var gruposP = tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "8" + Pt + "Saludo" + Pt + "cámaras");
        // los Volumes del saludo (negro, look rojo, impacto, sombra) son globales y se quedan con su último valor cuando
        // termina la ciudad: todo SALUDO_EPICO solo está activo durante la ciudad, así no tiñen ni tapan el título
        foreach (var tlx in new[] { tlP, tlC })
            foreach (var vieja in tlx.GetOutputTracks().OfType<ActivationTrack>().Where(x => x.name == "Final" + Pt + "negro" || (tlx == tlP && x.name == "Final" + Pt + "saludo activo")).ToList()) tlx.DeleteTrack(vieja);
        var pAct = tlC.GetOutputTracks().OfType<ActivationTrack>().FirstOrDefault(x => x.name == "Final" + Pt + "saludo activo") ?? tlC.CreateTrack<ActivationTrack>(gruposP, "Final" + Pt + "saludo activo");
        foreach (var c in pAct.GetClips().ToList()) tlC.DeleteClip(c);
        var tcN = pAct.CreateDefaultClip(); tcN.start = 0; tcN.duration = IniTitulo - InicioCiudad; tcN.displayName = "SALUDO_EPICO activo";
        pAct.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
        dirC.SetGenericBinding(pAct, raiz.gameObject);
        EditorUtility.SetDirty(pAct);
        log.AppendLine($"Negro: corte seco a los {IniNegro} s. SALUDO_EPICO (volúmenes, luces, efectos) se apaga a los {IniTitulo} s para no teñir ni tapar el título.");

        // ---------- 4. la ciudad dura hasta el final del corto: el título final vive dentro del Timeline de la ciudad
        //            (lo demás de la ciudad se recorta al empezar el título, ver RecortarCiudad)
        ControlTrack Ctrl(string nombre) => tlP.GetOutputTracks().OfType<ControlTrack>().FirstOrDefault(x => x.name == nombre);
        var cCiudad = Ctrl("3" + Pt + "Ciudad")?.GetClips().OrderBy(c => c.start).FirstOrDefault();
        if (cCiudad != null) { cCiudad.duration = FinCorto - cCiudad.start; EditorUtility.SetDirty(Ctrl("3" + Pt + "Ciudad")); }
        // versión anterior: el título en el bosque (ya no)
        foreach (var nombre in new[] { "2" + Pt + "T\u00EDtulo", "1" + Pt + "Bosque" })
        {
            var pista = Ctrl(nombre);
            if (pista != null) foreach (var viejo in pista.GetClips().Where(c => c.start > 60f).ToList()) tlP.DeleteClip(viejo);
        }
        var cortes = tlP.GetOutputTracks().OfType<CinemachineTrack>().FirstOrDefault();
        if (cortes == null) { log.AppendLine("No encuentro la pista de cortes de cámara."); return false; }
        foreach (var viejo in cortes.GetClips().Where(c => c.start >= IniTitulo - 0.01).ToList()) tlP.DeleteClip(viejo);   // cualquier corte del título final (de versiones anteriores)
        var c89 = cortes.GetClips().FirstOrDefault(c => c.displayName == "CM8_9_FINAL");
        if (c89 != null) c89.duration = IniTitulo - c89.start;

        // ---------- 5. título sobre negro
        bool EnTituloFinal(Transform x) { for (var q = x; q != null; q = q.parent) if (q.name == "TITULO_FINAL") return true; return false; }
        var gato = todos.FirstOrDefault(x => x != null && x.name == "#" && !EnTituloFinal(x));   // las letras originales, no las copias
        var padreLetras = gato != null ? gato.parent : null;
        if (padreLetras == null) { log.AppendLine("No encuentro las letras del título (objeto '#')."); return false; }
        var letras = padreLetras.Cast<Transform>().Where(x => System.Text.RegularExpressions.Regex.IsMatch(x.name, @"^(#|[A-Z])( \(?\d+\)?)?$")).ToList();
        foreach (var viejoGrupo in todos.Where(x => x != null && x.name == "TITULO_FINAL").ToList()) Undo.DestroyObjectImmediate(viejoGrupo.gameObject);
        var grupo = new GameObject("TITULO_FINAL"); Undo.RegisterCreatedObjectUndo(grupo, "Final");
        grupo.transform.SetPositionAndRotation(padreLetras.position + LugarTitulo, padreLetras.rotation);
        grupo.transform.localScale = padreLetras.lossyScale;
        foreach (var l in letras)
        {
            var copia = Object.Instantiate(l.gameObject, grupo.transform, false);
            copia.name = l.name; copia.SetActive(true);
            copia.transform.localPosition = l.localPosition; copia.transform.localRotation = l.localRotation; copia.transform.localScale = l.localScale;
            foreach (var an in copia.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(an);
        }
        var rends = grupo.GetComponentsInChildren<Renderer>(true);
        if (rends.Length == 0) { log.AppendLine("Las letras copiadas no tienen Renderer."); return false; }
        var bounds = rends[0].bounds; foreach (var r in rends) bounds.Encapsulate(r.bounds);
        var centro = bounds.center;
        // hacia dónde miran las letras: hacia la cámara del título original (si está), si no, -forward del grupo
        var fila = grupo.transform.right;
        var camOrig = T("CM2_TITULO");
        var haciaCam = camOrig != null ? (camOrig.position - padreLetras.position) : -padreLetras.forward;
        haciaCam = Vector3.ProjectOnPlane(haciaCam, fila); haciaCam.y = Mathf.Clamp(haciaCam.normalized.y, -0.15f, 0.15f) * haciaCam.magnitude;
        haciaCam.Normalize();
        // del lado en que se leen bien: el "#" a la izquierda y la "D" a la derecha de la cámara
        var primera = grupo.transform.Find("#"); var ultima = grupo.transform.Find("D");
        var orden = primera != null && ultima != null ? (ultima.position - primera.position) : fila;
        if (Vector3.Dot(Vector3.Cross(Vector3.up, -haciaCam), orden) < 0f) haciaCam = -haciaCam;
        float ancho = Vector3.Scale(bounds.size, new Vector3(Mathf.Abs(fila.x), Mathf.Abs(fila.y), Mathf.Abs(fila.z))).magnitude;
        if (ancho < 1f) ancho = bounds.size.magnitude;
        float fov = 30f, aspecto = 16f / 9f;
        float hfov = 2f * Mathf.Atan(Mathf.Tan(fov * 0.5f * Mathf.Deg2Rad) * aspecto);
        float dist = (ancho * 0.5f) / Mathf.Tan(hfov * 0.5f) * 1.25f;          // las letras ocupan ~80% del ancho

        // fondo negro detrás de las letras
        var matNegro = AssetDatabase.LoadAssetAtPath<Material>(Carpeta + "/Titulo_FondoNegro.mat");
        if (matNegro == null)
        {
            matNegro = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.black };
            if (matNegro.HasProperty("_BaseColor")) matNegro.SetColor("_BaseColor", Color.black);
            AssetDatabase.CreateAsset(matNegro, Carpeta + "/Titulo_FondoNegro.mat");
        }
        var fondo = GameObject.CreatePrimitive(PrimitiveType.Quad);
        fondo.name = "FONDO_NEGRO"; Object.DestroyImmediate(fondo.GetComponent<Collider>());
        fondo.transform.SetParent(grupo.transform, true);
        fondo.transform.position = centro - haciaCam * (dist * 0.6f);
        fondo.transform.rotation = Quaternion.LookRotation(-haciaCam, Vector3.up);   // la cara del quad ve a la cámara
        float lado = (dist * 1.6f) * Mathf.Tan(hfov * 0.5f) * 2f * 2.5f;
        fondo.transform.localScale = Vector3.one; fondo.transform.localScale = new Vector3(lado, lado, 1f) / Mathf.Max(0.0001f, grupo.transform.lossyScale.x);
        var mr = fondo.GetComponent<MeshRenderer>(); mr.sharedMaterial = matNegro;
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; mr.receiveShadows = false;

        // cámara del título final (se acerca despacio)
        var camGo = new GameObject("CM_TITULO_FINAL"); camGo.transform.SetParent(grupo.transform, true);
        var vcamT = camGo.AddComponent<CinemachineCamera>();
        var lensT = vcamT.Lens; lensT.FieldOfView = fov; lensT.NearClipPlane = 0.3f; lensT.FarClipPlane = dist * 4f; vcamT.Lens = lensT;
        vcamT.Priority.Enabled = false;
        var animCam = camGo.AddComponent<Animator>(); animCam.applyRootMotion = false; animCam.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var pIni = centro + haciaCam * dist * (1f + Acercamiento) + Vector3.up * (ancho * 0.02f);
        var pFin = centro + haciaCam * dist + Vector3.up * (ancho * 0.02f);
        var rot = Quaternion.LookRotation(centro - pFin, Vector3.up);
        camGo.transform.SetPositionAndRotation(pIni, rot);
        var clipCam = AssetDatabase.LoadAssetAtPath<AnimationClip>(Carpeta + "/Titulo_Final_Camara.anim");
        if (clipCam == null) { clipCam = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clipCam, Carpeta + "/Titulo_Final_Camara.anim"); }
        clipCam.ClearCurves();
        var lIni = grupo.transform.InverseTransformPoint(pIni); var lFin = grupo.transform.InverseTransformPoint(pFin);
        var lRot = Quaternion.Inverse(grupo.transform.rotation) * rot;
        float durT = FinCorto - IniTitulo;
        for (int i = 0; i < 3; i++)
            AnimationUtility.SetEditorCurve(clipCam, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition." + "xyz"[i]), AnimationCurve.EaseInOut(0f, lIni[i], durT, lFin[i]));
        for (int i = 0; i < 4; i++)
            AnimationUtility.SetEditorCurve(clipCam, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation." + "xyzw"[i]), AnimationCurve.Constant(0f, durT, lRot[i]));
        EditorUtility.SetDirty(clipCam);

        // fundido: Volume global con exposición muy baja (solo existe mientras está activo TITULO_FINAL)
        var perfil = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(Carpeta + "/Titulo_Fundido.asset");
        if (perfil == null)
        {
            perfil = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
            AssetDatabase.CreateAsset(perfil, Carpeta + "/Titulo_Fundido.asset");
            var ca2 = perfil.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
            ca2.postExposure.Override(-12f);
            AssetDatabase.AddObjectToAsset(ca2, perfil);
        }
        var fundGo = new GameObject("TITULO_FUNDIDO"); fundGo.transform.SetParent(grupo.transform, false);
        var vol = fundGo.AddComponent<UnityEngine.Rendering.Volume>(); vol.isGlobal = true; vol.priority = 95; vol.sharedProfile = perfil; vol.weight = 1f;
        var animFund = fundGo.AddComponent<Animator>(); animFund.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var clipFund = AssetDatabase.LoadAssetAtPath<AnimationClip>(Carpeta + "/Titulo_Final_Fundido.anim");
        if (clipFund == null) { clipFund = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clipFund, Carpeta + "/Titulo_Final_Fundido.anim"); }
        clipFund.ClearCurves();
        var cf = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(FundidoEntra, 0f), new Keyframe(durT - FundidoSale, 0f), new Keyframe(durT, 1f));
        for (int k = 0; k < cf.length; k++) { AnimationUtility.SetKeyLeftTangentMode(cf, k, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(cf, k, AnimationUtility.TangentMode.Linear); }
        AnimationUtility.SetEditorCurve(clipFund, EditorCurveBinding.FloatCurve("", typeof(UnityEngine.Rendering.Volume), "weight"), cf);
        EditorUtility.SetDirty(clipFund);
        grupo.SetActive(false);
        var carpetaFinal = todos.FirstOrDefault(x => x != null && x.name == CarpetaSaludoFinal);
        if (carpetaFinal != null) grupo.transform.SetParent(carpetaFinal, true);

        // pistas en el Timeline de la ciudad: visible, cámara y fundido (el corte de cámara va en el principal)
        foreach (var viejoG in tlP.GetRootTracks().OfType<GroupTrack>().Where(g => g.name == "9" + Pt + "T\u00EDtulo final").ToList()) tlP.DeleteTrack(viejoG);
        double iniTituloC = IniTitulo - InicioCiudad;
        var grupoTl = tlC.GetRootTracks().OfType<GroupTrack>().FirstOrDefault(g => g.name == "9" + Pt + "T\u00EDtulo final") ?? tlC.CreateTrack<GroupTrack>(null, "9" + Pt + "T\u00EDtulo final");
        foreach (var vieja in grupoTl.GetChildTracks().ToList()) tlC.DeleteTrack(vieja);
        var pVis = tlC.CreateTrack<ActivationTrack>(grupoTl, "T\u00EDtulo final" + Pt + "visible");
        var tcV = pVis.CreateDefaultClip(); tcV.start = iniTituloC; tcV.duration = durT; tcV.displayName = "TITULO_FINAL";
        pVis.postPlaybackState = ActivationTrack.PostPlaybackState.Inactive;
        dirC.SetGenericBinding(pVis, grupo);
        foreach (var (nombrePista, an, clip) in new[] { ("T\u00EDtulo final" + Pt + "c\u00E1mara", animCam, clipCam), ("T\u00EDtulo final" + Pt + "fundido", animFund, clipFund) })
        {
            var pa = tlC.CreateTrack<AnimationTrack>(grupoTl, nombrePista);
            pa.trackOffset = TrackOffset.ApplyTransformOffsets; pa.position = Vector3.zero; pa.eulerAngles = Vector3.zero;
            var tca = pa.CreateClip(clip); tca.start = iniTituloC; tca.duration = durT; tca.displayName = clip.name;
            if (tca.asset is AnimationPlayableAsset ap2) { ap2.removeStartOffset = false; ap2.position = Vector3.zero; ap2.rotation = Quaternion.identity; }
            dirC.SetGenericBinding(pa, an);
        }
        log.AppendLine(RecortarCiudad(tlC, grupoTl, iniTituloC));
        log.AppendLine(NieblaHastaTitulo(tlC, dirC, grupoTl, iniTituloC, todos));
        var tcT = cortes.CreateClip<CinemachineShot>();
        tcT.start = IniTitulo; tcT.duration = durT; tcT.displayName = "CM_TITULO_FINAL";
        var shot = (CinemachineShot)tcT.asset;
        var erT = shot.VirtualCamera; if (PropertyName.IsNullOrEmpty(erT.exposedName)) erT.exposedName = GUID.Generate().ToString();
        erT.defaultValue = null; shot.VirtualCamera = erT;
        dirP.SetReferenceValue(erT.exposedName, vcamT);
        EditorUtility.SetDirty(shot); EditorUtility.SetDirty(cortes);
        // secuencia épica: el # nace con chispas, luego AMISTAD, golpe de energía y se apaga a negro
        if (!TituloEpicoEditor.Agregar(grupo.transform, tlC, dirC, grupoTl, durT, log)) log.AppendLine("OJO: no se pudo armar el título épico.");
        log.AppendLine($"T\u00EDtulo final: {letras.Count} letras copiadas a TITULO_FINAL (lejos de todo) con fondo negro; CM_TITULO_FINAL a {dist:0} u, se acerca {Acercamiento * 100:0}%; aparece desde negro en {FundidoEntra} s y se va a negro al final ({IniTitulo}-{FinCorto} s). La ciudad termina a los {IniTitulo} s.");

        EditorUtility.SetDirty(tlP); EditorUtility.SetDirty(tlC);
        EditorSceneManager.MarkSceneDirty(dirP.gameObject.scene);
        try { UnityEditor.Timeline.TimelineEditor.Refresh(UnityEditor.Timeline.RefreshReason.ContentsModified); } catch { }
        return true;
    }

    /// Todo lo de la ciudad (menos el título final) termina cuando empieza el título: antes lo cortaba el fin del clip
    /// "3 · Ciudad" del principal; ahora ese clip llega al final del corto, así que se recortan aquí los clips que se pasan.
    public static string RecortarCiudad(TimelineAsset tlC, TrackAsset grupoTitulo, double finCiudad)
    {
        int n = 0;
        foreach (var tr in tlC.GetOutputTracks())
        {
            bool enTitulo = false; for (var q = tr as TrackAsset; q != null; q = q.parent as TrackAsset) if (q == grupoTitulo) enTitulo = true;
            if (enTitulo) continue;
            foreach (var c in tr.GetClips().ToList())
            {
                if (c.start >= finCiudad - 1e-4) { tlC.DeleteClip(c); n++; continue; }
                if (c.end > finCiudad + 1e-4) { c.duration = finCiudad - c.start; n++; EditorUtility.SetDirty(tr); }
            }
        }
        return $"Ciudad: {n} clips recortados a {finCiudad:0.##} s (empieza el título).";
    }

    /// La niebla de la ciudad (NIEBLA_CIUDAD, hijo de Director_P2_Ciudad) teñiría de café el fondo negro del título:
    /// se apaga al empezar el título (su OnDisable quita la niebla).
    public static string NieblaHastaTitulo(TimelineAsset tlC, PlayableDirector dirC, TrackAsset grupoTitulo, double finCiudad, Transform[] todos)
    {
        var niebla = todos.FirstOrDefault(x => x != null && x.name == "NIEBLA_CIUDAD");
        if (niebla == null) return "OJO: no encontré NIEBLA_CIUDAD (la niebla podría teñir el título).";
        const string nombre = "Niebla ciudad \u00B7 se apaga en el t\u00EDtulo";
        foreach (var vieja in tlC.GetOutputTracks().OfType<ActivationTrack>().Where(x => x.name == nombre).ToList()) tlC.DeleteTrack(vieja);
        var pN = tlC.CreateTrack<ActivationTrack>(grupoTitulo, nombre);
        var c = pN.CreateDefaultClip(); c.start = 0; c.duration = finCiudad; c.displayName = "NIEBLA_CIUDAD";
        pN.postPlaybackState = ActivationTrack.PostPlaybackState.Active;
        dirC.SetGenericBinding(pN, niebla.gameObject);
        EditorUtility.SetDirty(pN);
        return $"Niebla de la ciudad: encendida de 0 a {finCiudad:0.##} s de la ciudad, apagada en el título.";
    }

    // ================================================================= explosión nuclear
    static Material Mat(string ruta) => AssetDatabase.LoadAssetAtPath<Material>(ruta);

    static ParticleSystem Sistema(Transform padre, string nombre, Material mat, ParticleSystemRenderMode modo = ParticleSystemRenderMode.Billboard)
    {
        var go = new GameObject(nombre); go.transform.SetParent(padre, false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var m = ps.main; m.loop = false; m.playOnAwake = true; m.simulationSpace = ParticleSystemSimulationSpace.World;
        m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.maxParticles = 3000;
        var r = go.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mat; r.renderMode = modo;
        r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false;
        r.sortMode = ParticleSystemSortMode.Distance; r.maxParticleSize = 5f;
        // el shader FuegoVFX del proyecto necesita estos datos por partícula (si no, no dibuja nada)
        if (mat != null && mat.shader != null && mat.shader.name.Contains("FuegoVFX"))
            r.SetActiveVertexStreams(new System.Collections.Generic.List<ParticleSystemVertexStream>
            { ParticleSystemVertexStream.Position, ParticleSystemVertexStream.Color, ParticleSystemVertexStream.UV,
              ParticleSystemVertexStream.StableRandomX, ParticleSystemVertexStream.AgePercent });
        return ps;
    }

    static Gradient Grad(params (float t, Color c)[] k)
    {
        var g = new Gradient();
        g.SetKeys(k.Select(x => new GradientColorKey(new Color(x.c.r, x.c.g, x.c.b), x.t)).ToArray(),
                  k.Select(x => new GradientAlphaKey(x.c.a, x.t)).ToArray());
        return g;
    }

    static GameObject[] Nuclear(Transform nuc, StringBuilder log)
    {
        var humo = Mat("Assets/Assets/shaders/FuegoVFX/Fuego_Ceniza.mat");      // humo gris claro (el Fuego_Humo es casi negro)
        var llamas = Mat("Assets/Assets/shaders/FuegoVFX/Fuego_Llamas.mat");     // fuego procedural (aditivo, HDR)
        var brasasM = Mat("Assets/Assets/shaders/FuegoVFX/Fuego_Brasas.mat");
        var brillo = Mat(Carpeta + "/Saludo_Estallido.mat");
        var anillo = Mat(Carpeta + "/Saludo_Onda.mat");
        var pref = AssetDatabase.LoadAssetAtPath<GameObject>(RutaExplosion);
        if (humo == null || llamas == null || brillo == null || anillo == null || pref == null) { log.AppendLine("Faltan materiales de humo/brillo/onda o el prefab de la explosión."); return null; }
        var lista = new System.Collections.Generic.List<GameObject>();
        Color Naranja = new Color(1f, 0.55f, 0.15f, 1f), Amarillo = new Color(1f, 0.9f, 0.55f, 1f);
        Color Rojo = new Color(0.8f, 0.2f, 0.05f, 1f), Hollin = new Color(0.16f, 0.13f, 0.12f, 1f), Gris = new Color(0.28f, 0.26f, 0.25f, 1f);

        // bola de fuego: llamas procedurales que se expanden y suben (en lugar del prefab plano)
        {
            var ps = Sistema(nuc, "BOLA_FUEGO", llamas);
            var m = ps.main; m.duration = 0.5f; m.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.9f); m.startSpeed = new ParticleSystem.MinMaxCurve(28f, 50f);
            m.startSize = new ParticleSystem.MinMaxCurve(30f, 55f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.35f;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70), new ParticleSystem.Burst(0.15f, 40) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 6f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 8f; lim.dampen = 0.08f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0, 0.4f), new Keyframe(0.25f, 1f), new Keyframe(1, 1.3f)));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 1, 1, 1)), (0.4f, new Color(1, 0.85f, 0.6f, 1)), (1f, new Color(1, 0.4f, 0.2f, 0)));
            ps.transform.localPosition = Vector3.up * 10f; lista.Add(ps.gameObject);
        }
        // humo que envuelve la bola (oscurece el borde y le da volumen)
        {
            var ps = Sistema(nuc, "BOLA_HUMO", humo);
            var m = ps.main; m.duration = 0.6f; m.startDelay = 0.25f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.5f, 3.5f); m.startSpeed = new ParticleSystem.MinMaxCurve(20f, 40f);
            m.startSize = new ParticleSystem.MinMaxCurve(35f, 60f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.12f;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 80) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 14f;
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 5f; lim.dampen = 0.06f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1f, 0.5f, 0.2f, 0f)), (0.15f, new Color(0.55f, 0.3f, 0.18f, 0.8f)), (0.6f, new Color(0.3f, 0.26f, 0.24f, 0.7f)), (1f, new Color(0.25f, 0.23f, 0.22f, 0f)));
            ps.transform.localPosition = Vector3.up * 12f; lista.Add(ps.gameObject);
        }
        // destello central (aditivo, enorme y breve)
        {
            var ps = Sistema(nuc, "DESTELLO_CENTRO", brillo);
            var m = ps.main; m.duration = 0.5f; m.startLifetime = 0.9f; m.startSpeed = 0f; m.startSize = 180f; m.startColor = Amarillo;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 3) });
            var sh = ps.shape; sh.enabled = false;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 1, 0.9f, 1)), (0.15f, Amarillo), (1f, new Color(1, 0.4f, 0.1f, 0)));
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0, 0.3f, 1, 1.4f));
            ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 20f;
            ps.transform.localPosition = Vector3.up * 15f; lista.Add(ps.gameObject);
        }
        // anillo de choque (plano, se abre rapidísimo sobre el piso)
        {
            var ps = Sistema(nuc, "ANILLO_CHOQUE", anillo, ParticleSystemRenderMode.HorizontalBillboard);
            var m = ps.main; m.duration = 0.3f; m.startLifetime = 1.4f; m.startSpeed = 0f; m.startSize = 20f; m.startColor = new Color(1f, 0.8f, 0.5f, 1f);
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 1), new ParticleSystem.Burst(0.25f, 1) });
            var sh = ps.shape; sh.enabled = false;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(30f, new AnimationCurve(new Keyframe(0, 0.5f, 0, 6f), new Keyframe(1, 30f)));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 0.9f, 0.7f, 1)), (0.5f, new Color(1, 0.5f, 0.2f, 0.6f)), (1f, new Color(1, 0.3f, 0.1f, 0)));
            ps.GetComponent<ParticleSystemRenderer>().maxParticleSize = 50f;
            ps.transform.localPosition = Vector3.up * 1.5f; lista.Add(ps.gameObject);
        }
        // onda de polvo (pared de polvo que barre el piso hacia afuera)
        {
            var ps = Sistema(nuc, "ONDA_POLVO", humo);
            var m = ps.main; m.duration = 0.4f; m.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 2.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(110f, 160f);
            m.startSize = new ParticleSystem.MinMaxCurve(12f, 22f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0.08f, 700) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 10f; sh.radiusThickness = 0f; sh.rotation = new Vector3(90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 6f; lim.dampen = 0.06f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 3f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1f, 0.6f, 0.3f, 0.9f)), (0.2f, new Color(0.45f, 0.36f, 0.28f, 0.85f)), (1f, new Color(0.3f, 0.26f, 0.22f, 0f)));
            ps.transform.localPosition = Vector3.up * 3f; lista.Add(ps.gameObject);
        }
        // hongo: tallo (columna de fuego que se vuelve humo)
        {
            var ps = Sistema(nuc, "HONGO_TALLO", humo);
            var m = ps.main; m.duration = 1.8f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.0f); m.startSpeed = new ParticleSystem.MinMaxCurve(55f, 75f);
            m.startSize = new ParticleSystem.MinMaxCurve(14f, 24f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.15f;
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 260f, 1, 60f));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 5f; sh.radius = 7f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 10f; lim.dampen = 0.035f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.8f, 1, 2.2f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, Amarillo), (0.12f, Naranja), (0.35f, Rojo), (0.6f, Hollin), (1f, new Color(Gris.r, Gris.g, Gris.b, 0f)));
            lista.Add(ps.gameObject);
        }
        // hongo: fuego del tallo (llamas que suben por la columna)
        {
            var ps = Sistema(nuc, "HONGO_TALLO_FUEGO", llamas);
            var m = ps.main; m.duration = 1.5f; m.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.4f); m.startSpeed = new ParticleSystem.MinMaxCurve(70f, 95f);
            m.startSize = new ParticleSystem.MinMaxCurve(16f, 26f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.2f;
            var em = ps.emission; em.rateOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 200f, 1, 40f));
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 4f; sh.radius = 6f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 14f; lim.dampen = 0.03f;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 1f, 1, 1.6f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 1, 1, 1)), (0.6f, new Color(1, 0.8f, 0.6f, 0.8f)), (1f, new Color(1, 0.5f, 0.3f, 0f)));
            lista.Add(ps.gameObject);
        }
        // hongo: copa (sube, frena arriba y se abre)
        {
            var ps = Sistema(nuc, "HONGO_COPA", humo);
            var m = ps.main; m.duration = 1.6f; m.startDelay = 0.3f; m.startLifetime = new ParticleSystem.MinMaxCurve(2.8f, 3.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(4f, 10f);
            m.startSize = new ParticleSystem.MinMaxCurve(30f, 50f); m.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f); m.gravityModifier = -0.04f;
            var em = ps.emission; em.rateOverTime = 150f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 160) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 12f;
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f)); vel.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 0f));   // x/y/z en el mismo modo (curva)
            vel.y = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 160f), new Keyframe(0.35f, 45f), new Keyframe(0.6f, 6f), new Keyframe(1f, 0f)));
            vel.radial = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 6f), new Keyframe(0.5f, 34f), new Keyframe(1f, 12f)));
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0, 0.6f, 1, 1.8f));
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, Amarillo), (0.15f, Naranja), (0.4f, Rojo), (0.65f, Hollin), (1f, new Color(Gris.r, Gris.g, Gris.b, 0f)));
            ps.transform.localPosition = Vector3.up * 10f; lista.Add(ps.gameObject);
        }
        // fuego interno de la copa (aditivo: la parte de abajo brilla)
        {
            var ps = Sistema(nuc, "HONGO_FUEGO", llamas);
            var m = ps.main; m.duration = 1.4f; m.startDelay = 0.2f; m.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 1.8f); m.startSpeed = new ParticleSystem.MinMaxCurve(60f, 90f);
            m.startSize = new ParticleSystem.MinMaxCurve(20f, 34f); m.gravityModifier = -0.1f;
            var em = ps.emission; em.rateOverTime = 90f;
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 12f; sh.radius = 8f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var lim = ps.limitVelocityOverLifetime; lim.enabled = true; lim.limit = 12f; lim.dampen = 0.05f;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, new Color(1, 0.95f, 0.7f, 1)), (0.3f, new Color(1, 0.55f, 0.15f, 0.9f)), (1f, new Color(0.8f, 0.15f, 0.05f, 0)));
            lista.Add(ps.gameObject);
        }
        // brasas que salen disparadas
        if (brasasM != null)
        {
            var ps = Sistema(nuc, "BRASAS", brasasM);
            var m = ps.main; m.duration = 0.6f; m.startLifetime = new ParticleSystem.MinMaxCurve(1.5f, 3f); m.startSpeed = new ParticleSystem.MinMaxCurve(60f, 140f);
            m.startSize = new ParticleSystem.MinMaxCurve(1.5f, 4f); m.gravityModifier = 0.6f; m.startColor = Naranja;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0.05f, 400) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Hemisphere; sh.radius = 10f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Grad((0f, Amarillo), (0.5f, Naranja), (1f, new Color(0.6f, 0.1f, 0f, 0f)));
            ps.transform.localPosition = Vector3.up * 8f; lista.Add(ps.gameObject);
        }

        // luz del destello: ilumina toda la ciudad
        {
            var go = new GameObject("DESTELLO_LUZ"); go.transform.SetParent(nuc, false); go.transform.localPosition = Vector3.up * 40f;
            var l = go.AddComponent<Light>(); l.type = LightType.Point; l.range = 900f; l.color = new Color(1f, 0.78f, 0.5f); l.intensity = 0f; l.shadows = LightShadows.None;
            var an = go.AddComponent<Animator>(); an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ClipCurva($"{Carpeta}/Final_DestelloLuz.anim", typeof(Light), "m_Intensity",
                (0f, 0f), (0.04f, 60f), (0.25f, 40f), (1.2f, 14f), (2.5f, 6f), (6f, 3f));
        }
        // destello de pantalla: Volume global que quema a blanco y baja
        {
            var go = new GameObject("DESTELLO_PANTALLA"); go.transform.SetParent(nuc, false);
            var perfil = AssetDatabase.LoadAssetAtPath<UnityEngine.Rendering.VolumeProfile>(Carpeta + "/Final_Destello.asset");
            if (perfil == null)
            {
                perfil = ScriptableObject.CreateInstance<UnityEngine.Rendering.VolumeProfile>();
                AssetDatabase.CreateAsset(perfil, Carpeta + "/Final_Destello.asset");
                var adj = perfil.Add<UnityEngine.Rendering.Universal.ColorAdjustments>(true);
                adj.postExposure.Override(4.5f); adj.colorFilter.Override(new Color(1f, 0.96f, 0.88f)); adj.saturation.Override(-40f);
                AssetDatabase.AddObjectToAsset(adj, perfil);
                var blo = perfil.Add<UnityEngine.Rendering.Universal.Bloom>(true);
                blo.intensity.Override(6f); blo.threshold.Override(0.6f);
                AssetDatabase.AddObjectToAsset(blo, perfil);
            }
            var v = go.AddComponent<UnityEngine.Rendering.Volume>(); v.isGlobal = true; v.priority = 92; v.sharedProfile = perfil; v.weight = 0f;
            var an = go.AddComponent<Animator>(); an.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            ClipCurva($"{Carpeta}/Final_DestelloPantalla.anim", typeof(UnityEngine.Rendering.Volume), "weight",
                (0f, 0f), (0.05f, 1f), (0.18f, 1f), (0.6f, 0.35f), (1.6f, 0.08f), (3f, 0f));
        }
        return lista.ToArray();
    }

    static AnimationClip ClipCurva(string ruta, System.Type tipo, string prop, params (float t, float v)[] k)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (clip == null) { clip = new AnimationClip { frameRate = 30 }; AssetDatabase.CreateAsset(clip, ruta); }
        clip.ClearCurves();
        var cu = new AnimationCurve(k.Select(x => new Keyframe(x.t, x.v)).ToArray());
        for (int i = 0; i < cu.length; i++) { AnimationUtility.SetKeyLeftTangentMode(cu, i, AnimationUtility.TangentMode.Linear); AnimationUtility.SetKeyRightTangentMode(cu, i, AnimationUtility.TangentMode.Linear); }
        AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", tipo, prop), cu);
        EditorUtility.SetDirty(clip);
        return clip;
    }
}
