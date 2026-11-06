using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Tools > Amistad > Goku: revisar vuelo
///   Vuelca a Logs/goku_vuelo/curvas.txt los músculos (Humanoid) del ciclo de vuelo y de los power up base/SSJ.
/// Tools > Amistad > Goku: corregir vuelo
///   Genera dos clips Humanoid a partir del ciclo de vuelo de Blender (goku_base_vuelo.fbx) y guarda fotos de prueba:
///   - Goku_Vuelo_Fix.anim: mismo ciclo, pero el retarget dejaba las espinillas torcidas (Lower Leg Twist 1.87, fuera
///     del rango -1..1) y los pies volteados. Giro de espinilla y de pie en 0, pies en punta, sin objetivos de IK
///     (LeftFootT/Q... conservaban la torcedura).
///   - Goku_Despegue.anim: pose de salida del SSJ: brazos pegados al cuerpo y un poco atrás, puños, piernas juntas.
///     El ciclo tenía el brazo izquierdo arriba (Arm Down-Up 0.94) y al mezclarlo con el final del power up (brazos
///     abajo) el brazo subía: parecía que saludaba.
///   Fotos: Logs/goku_vuelo/<clip>_lado.png y _34.png (copia temporal de Goku_Base, no toca la escena).
/// GokuVueloEditor (CM4) usa estos clips. Se puede volver a correr.
/// </summary>
public static class GokuVueloFixEditor
{
    const string Carpeta = "Assets/Assets/3d/Persoanjes/Goku/";
    public const string RutaVueloFix = Carpeta + "Goku_Vuelo_Fix.anim";
    public const string RutaDespegue = Carpeta + "Goku_Despegue.anim";
    public const string RutaSaludo = Carpeta + "Goku_Saludo.anim";        // SSJ: dos dedos a la frente y lo lanza
    public const string RutaDespedida = Carpeta + "Goku_Despedida.anim";  // se va volando despidiéndose con la mano
    public const string RutaVueloSalida = Carpeta + "Goku_VueloSalida.anim";   // vuelo corregido con una rodilla doblada
    const float RodillaMuslo = 0.25f, RodillaDoblez = 0.4f;   // rodilla derecha apenas doblada (1 = pierna estirada)
    const float PuntaPies = 0.9f;                              // los dos pies en punta, alineados
    public const float LargoSaludo = 1.8f;   // s: 0-0.35 sube la mano, 0.35-0.85 la sostiene, 0.85-1.1 la lanza, 1.0-1.55 dobla las rodillas (impulso)

    // Pies en punta (Foot Up-Down; negativo = punta hacia abajo/atrás). Ver fotos de prueba.
    const float PieApunta = 0.6f;   // positivo = punta hacia abajo (con negativo el pie se doblaba hacia arriba)

    static AnimationClip ClipDe(string fbx) => AssetDatabase.LoadAllAssetsAtPath(fbx).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));

    [MenuItem("Tools/Amistad/Goku: revisar vuelo")]
    static void Revisar()
    {
        var log = new StringBuilder();
        foreach (var fbx in new[] { "goku_base_vuelo.fbx", "goku_base_powerup.fbx", "goku_ssj_powerup.fbx" })
        {
            var c = ClipDe(Carpeta + fbx);
            if (c == null) { log.AppendLine($"== {fbx}: sin clip"); continue; }
            log.AppendLine($"== {fbx}: clip {c.name}, {c.length:0.00}s, humano {c.humanMotion}, loop {c.isLooping}");
            foreach (var b in AnimationUtility.GetCurveBindings(c).OrderBy(b => b.propertyName))
            {
                var cu = AnimationUtility.GetEditorCurve(c, b);
                if (cu == null || cu.length == 0) continue;
                float mn = cu.keys.Min(k => k.value), mx = cu.keys.Max(k => k.value);
                log.AppendLine($"   {b.propertyName,-40} min {mn,7:0.00} max {mx,7:0.00} inicio {cu.Evaluate(0),7:0.00} final {cu.Evaluate(c.length),7:0.00}");
            }
        }
        System.IO.Directory.CreateDirectory("Logs/goku_vuelo");
        System.IO.File.WriteAllText("Logs/goku_vuelo/curvas.txt", log.ToString());
        Debug.Log("[Goku vuelo] Logs/goku_vuelo/curvas.txt");
    }

    [MenuItem("Tools/Amistad/Goku: corregir vuelo")]
    public static void Corregir()
    {
        var fuente = ClipDe(Carpeta + "goku_base_vuelo.fbx");
        var salidaBlender = ImportarSalida();   // pose de piernas hecha en Blender (goku_base_salida.blend)
        if (fuente == null) { Debug.LogError("[Goku vuelo] No encuentro el clip de goku_base_vuelo.fbx"); return; }
        var log = new StringBuilder();
        float L = fuente.length;
        AnimationCurve K(float v) => new AnimationCurve(new Keyframe(0f, v), new Keyframe(L, v));
        // Valor constante con una oscilación suave que cierra el ciclo (para que no se vea congelado)
        AnimationCurve Ola(float v, float amp, float fase)
        {
            var c = new AnimationCurve();
            for (int i = 0; i <= 8; i++) { float t = L * i / 8f; c.AddKey(t, v + amp * Mathf.Sin(2f * Mathf.PI * (i / 8f) + fase)); }
            return c;
        }

        // ---------- Vuelo corregido ----------
        var fix = new Dictionary<string, AnimationCurve>();
        foreach (var lado in new[] { "Left", "Right" })
        {
            float f = lado == "Left" ? 0f : Mathf.PI;     // las piernas se mecen alternadas
            fix[$"{lado} Lower Leg Twist In-Out"] = K(0f);
            fix[$"{lado} Foot Twist In-Out"] = K(0f);
            fix[$"{lado} Foot Up-Down"] = Ola(PieApunta, 0.06f, f);
            fix[$"{lado} Toes Up-Down"] = K(0f);
            fix[$"{lado} Upper Leg Twist In-Out"] = K(0f);
        }
        var vuelo = CrearClip(fuente, RutaVueloFix, fix, log);

        // ---------- Despegue (SSJ se va) ----------
        var dsp = new Dictionary<string, AnimationCurve>(fix);
        // Puño de la mano izquierda del ciclo (ya viene cerrada) para las dos manos
        var bindings = AnimationUtility.GetCurveBindings(fuente);
        foreach (var b in bindings.Where(b => b.propertyName.StartsWith("LeftHand.")))
        {
            var c = AnimationUtility.GetEditorCurve(fuente, b);
            dsp["RightHand." + b.propertyName.Substring("LeftHand.".Length)] = c;
        }
        foreach (var lado in new[] { "Left", "Right" })
        {
            float f = lado == "Left" ? 0f : Mathf.PI;
            dsp[$"{lado} Shoulder Down-Up"] = K(-0.15f);
            dsp[$"{lado} Shoulder Front-Back"] = K(-0.1f);
            dsp[$"{lado} Arm Down-Up"] = Ola(-0.62f, 0.02f, f);
            dsp[$"{lado} Arm Front-Back"] = K(-0.35f);         // brazos hacia atrás, pegados al cuerpo
            dsp[$"{lado} Arm Twist In-Out"] = K(0f);
            dsp[$"{lado} Forearm Stretch"] = K(0.85f);
            dsp[$"{lado} Forearm Twist In-Out"] = K(0f);
            dsp[$"{lado} Hand Down-Up"] = K(0f);
            dsp[$"{lado} Hand In-Out"] = K(0f);
            dsp[$"{lado} Upper Leg Front-Back"] = Ola(0.05f, 0.04f, f);   // piernas juntas y casi estiradas
            dsp[$"{lado} Upper Leg In-Out"] = K(-0.05f);
            dsp[$"{lado} Lower Leg Stretch"] = Ola(0.8f, 0.05f, f);
        }
        dsp["Head Nod Down-Up"] = K(0.25f);      // la cabeza levantada, mirando hacia donde va
        var despegue = CrearClip(fuente, RutaDespegue, dsp, log);

        // ---------- Saludo SSJ (referencia: el saludo de dos dedos de Goku) ----------
        var ssj = ClipDe(Carpeta + "goku_ssj_powerup.fbx");
        var sal = new Dictionary<string, AnimationCurve>();
        float LS = LargoSaludo;
        // Base: la pose final del power up SSJ, quieta
        foreach (var b in AnimationUtility.GetCurveBindings(ssj))
        { float v = AnimationUtility.GetEditorCurve(ssj, b).Evaluate(ssj.length); sal[b.propertyName] = new AnimationCurve(new Keyframe(0f, v), new Keyframe(LS, v)); }
        float V(string m) => sal.TryGetValue(m, out var c) ? c.Evaluate(0f) : 0f;
        // Brazo derecho: de la pose final -> dedos en la frente (0.35) -> sostiene (0.85) -> lo lanza al frente (1.1)
        // La pose "dedos en la frente" se resuelve con IK de dos huesos sobre el modelo y se convierte a músculos
        // (con músculos a mano el giro del brazo topaba y la mano quedaba frente al pecho).
        var frente = PoseManoEnFrente(ssj, log);
        void Pasos(string m, float frenteDefecto, float lanza)
        {
            float f = frente != null && frente.TryGetValue(m, out var v) ? v : frenteDefecto;
            float v0 = V(m);
            // Llega con un pequeño rebote (pasa 10 % y se asienta), se sostiene con una deriva mínima y lo lanza rápido,
            // también con rebote. Así no se ve mecánico.
            sal[m] = Curva((0f, v0), (0.28f, f + (f - v0) * 0.1f), (0.4f, f), (0.8f, f + (f - v0) * 0.02f),
                           (0.98f, lanza + (lanza - f) * 0.1f), (1.12f, lanza), (LS, lanza));
        }
        Pasos("Right Shoulder Down-Up", 0.35f, 0.15f);
        Pasos("Right Shoulder Front-Back", 0.2f, 0.25f);
        Pasos("Right Arm Down-Up", 0.62f, 0.55f);
        Pasos("Right Arm Front-Back", 0.3f, 0.75f);
        Pasos("Right Arm Twist In-Out", -0.5f, 0.1f);
        Pasos("Right Forearm Stretch", -0.95f, 0.35f);
        Pasos("Right Forearm Twist In-Out", 0.2f, 0.1f);
        Pasos("Right Hand Down-Up", 0.2f, 0f);
        Pasos("Right Hand In-Out", 0f, 0.1f);
        // Mano: índice y medio estirados y juntos; anular, meñique y pulgar cerrados (puño del ciclo de vuelo)
        foreach (var dedo in new[] { "Index", "Middle" })
        {
            foreach (var seg in new[] { "1", "2", "3" }) sal[$"RightHand.{dedo}.{seg} Stretched"] = K2(0.75f, LS);
            sal[$"RightHand.{dedo}.Spread"] = K2(dedo == "Index" ? 0.25f : -0.25f, LS);
        }
        foreach (var b in AnimationUtility.GetCurveBindings(fuente).Where(b => b.propertyName.StartsWith("LeftHand.Ring") || b.propertyName.StartsWith("LeftHand.Little") || b.propertyName.StartsWith("LeftHand.Thumb")))
            sal["RightHand." + b.propertyName.Substring("LeftHand.".Length)] = K2(AnimationUtility.GetEditorCurve(fuente, b).Evaluate(0f), LS);
        // Cabeza: baja un poco la barbilla y se ladea hacia la mano (sonrisa de lado)
        // Cabeza: acompaña la mano con un poco de retraso y cabecea al lanzar el saludo
        sal["Head Nod Down-Up"] = Curva((0f, V("Head Nod Down-Up")), (0.33f, -0.18f), (0.46f, -0.15f), (0.95f, -0.13f), (1.05f, -0.22f), (1.22f, -0.05f), (LS, -0.05f));
        sal["Head Tilt Left-Right"] = Curva((0f, V("Head Tilt Left-Right")), (0.4f, -0.22f), (0.52f, -0.2f), (1.0f, -0.18f), (1.2f, 0.03f), (1.32f, 0f), (LS, 0f));
        // Una sola rodilla: la derecha sube y se dobla mientras hace el saludo (desde que la mano llega a la frente) y se
        // queda así; la izquierda se estira con el pie en punta. Parece que va a saltar. Al final se inclina un poco.
        void Rodilla(string m, float valor) => sal[m] = Curva((0f, V(m)), (0.3f, V(m)), (0.8f, valor), (LS, valor));
        if (salidaBlender != null)
        {
            // Piernas exactas de la pose de Blender (rodilla de adelante doblada, la de atrás larga, pies en punta)
            foreach (var b in AnimationUtility.GetCurveBindings(salidaBlender).Where(b => EsPierna(b.propertyName)))
                Rodilla(b.propertyName, AnimationUtility.GetEditorCurve(salidaBlender, b).Evaluate(0f));
        }
        else
        {
        Rodilla("Right Upper Leg Front-Back", RodillaMuslo);
        Rodilla("Right Lower Leg Stretch", RodillaDoblez);
        Rodilla("Right Foot Up-Down", PuntaPies);
        Rodilla("Left Upper Leg Front-Back", 0.05f);
        Rodilla("Left Lower Leg Stretch", 0.95f);
        Rodilla("Left Foot Up-Down", PuntaPies);
        Rodilla("Right Upper Leg In-Out", -0.1f);   // piernas juntas
        Rodilla("Left Upper Leg In-Out", -0.1f);
        }
        void Inclina(string m, float valor) => sal[m] = Curva((0f, V(m)), (1.1f, V(m)), (1.6f, valor), (LS, valor));
        Inclina("Spine Front-Back", 0.2f);
        Inclina("Chest Front-Back", 0.1f);
        var saludo = CrearClip(ssj, RutaSaludo, sal, log, loop: false);

        // ---------- Vuelo de salida: el vuelo corregido, pero con la rodilla derecha doblada (sigue la pose del saludo) ----------
        var vsal = new Dictionary<string, AnimationCurve>(fix);
        vsal["Right Upper Leg Front-Back"] = Ola(RodillaMuslo, 0.03f, 0f);
        vsal["Right Lower Leg Stretch"] = Ola(RodillaDoblez, 0.03f, 0f);
        vsal["Left Upper Leg Front-Back"] = Ola(0.05f, 0.02f, Mathf.PI);
        vsal["Left Lower Leg Stretch"] = Ola(0.95f, 0.02f, Mathf.PI);
        vsal["Right Foot Up-Down"] = Ola(PuntaPies, 0.04f, 0f);
        vsal["Left Foot Up-Down"] = Ola(PuntaPies, 0.04f, Mathf.PI);
        vsal["Right Upper Leg In-Out"] = K(-0.1f);
        vsal["Left Upper Leg In-Out"] = K(-0.1f);
        if (salidaBlender != null)
            foreach (var b in AnimationUtility.GetCurveBindings(salidaBlender).Where(b => EsPierna(b.propertyName)))
            {
                // piernas de Blender con un vaivén leve (alternado entre piernas) para que no se vean congeladas
                float v = AnimationUtility.GetEditorCurve(salidaBlender, b).Evaluate(0f);
                bool estira = b.propertyName.Contains("Front-Back") || b.propertyName.Contains("Stretch") || b.propertyName.Contains("Up-Down");
                vsal[b.propertyName] = estira ? Ola(v, 0.03f, b.propertyName.StartsWith("Left") ? Mathf.PI : 0f) : K(v);
            }
        var vueloSalida = CrearClip(fuente, RutaVueloSalida, vsal, log);

        // ---------- Despedida: se va volando con el brazo arriba y la mano abierta, agitándola ----------
        var dsd = new Dictionary<string, AnimationCurve>(dsp);
        foreach (var b in AnimationUtility.GetCurveBindings(fuente).Where(b => b.propertyName.StartsWith("RightHand.")))
            dsd[b.propertyName] = AnimationUtility.GetEditorCurve(fuente, b);          // mano derecha abierta (la del ciclo)
        dsd["Right Shoulder Down-Up"] = K(0.3f);
        dsd["Right Shoulder Front-Back"] = K(0f);
        dsd["Right Arm Down-Up"] = K(0.85f);
        dsd["Right Arm Front-Back"] = K(0.15f);
        dsd["Right Arm Twist In-Out"] = K(0f);
        dsd["Right Forearm Stretch"] = K(0.6f);
        dsd["Right Forearm Twist In-Out"] = Vaiven(0f, 0.35f, 2, L);   // agita la mano
        dsd["Right Hand In-Out"] = Vaiven(0f, 0.4f, 2, L);
        dsd["Head Turn Left-Right"] = K(0.3f);                           // voltea un poco hacia atrás
        var despedida = CrearClip(fuente, RutaDespedida, dsd, log);

        AssetDatabase.SaveAssets();
        Fotos(new List<(AnimationClip, float, string)>
        {
            (fuente, 0.25f, "Vuelo"), (vuelo, 0.25f, "Vuelo_Fix"), (despegue, 0.25f, "Despegue"),
            (saludo, 0.6f, "Saludo_frente"), (saludo, 1.15f, "Saludo_lanza"), (saludo, 1.7f, "Saludo_impulso"), (vueloSalida, 0.25f, "VueloSalida"),
        }, log);
        System.IO.Directory.CreateDirectory("Logs/goku_vuelo");
        System.IO.File.WriteAllText("Logs/goku_vuelo/correccion.txt", log.ToString());
        Debug.Log("[Goku vuelo] Clips corregidos.\n" + log);
    }

    /// Pone la mano derecha en la frente (lado derecho) con IK analítico de dos huesos sobre una copia de Goku_Base en la
    /// pose final del power up SSJ, y devuelve los músculos Humanoid del brazo derecho de esa pose.
    static Dictionary<string, float> PoseManoEnFrente(AnimationClip ssj, StringBuilder log)
    {
        var baseT = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Goku_Base");
        if (baseT == null) { log.AppendLine("Saludo: sin Goku_Base, uso valores por defecto"); return null; }
        var copia = Object.Instantiate(baseT.gameObject); copia.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            copia.SetActive(true);
            copia.transform.SetPositionAndRotation(new Vector3(0, -9500, 0), Quaternion.identity); copia.transform.localScale = baseT.lossyScale;
            var an = copia.GetComponent<Animator>();
            ssj.SampleAnimation(copia, ssj.length);
            copia.transform.SetPositionAndRotation(new Vector3(0, -9500, 0), Quaternion.identity);
            Transform B(HumanBodyBones h) => an.GetBoneTransform(h);
            var hombro = B(HumanBodyBones.RightUpperArm); var codo = B(HumanBodyBones.RightLowerArm); var mano = B(HumanBodyBones.RightHand);
            var cabeza = B(HumanBodyBones.Head); var cuello = B(HumanBodyBones.Neck) ?? cabeza.parent;
            var dedo = B(HumanBodyBones.RightMiddleProximal);
            // Ejes del personaje
            Vector3 fw = copia.transform.forward, up = copia.transform.up, der = copia.transform.right;
            float alturaCabeza = Vector3.Distance(cabeza.position, cuello.position);
            // Objetivo: la muñeca frente a la sien derecha, con los dedos tocando la frente
            float largoMano = dedo ? Vector3.Distance(mano.position, dedo.position) : alturaCabeza * 0.5f;
            Vector3 frente = cabeza.position + up * alturaCabeza * 0.9f + fw * alturaCabeza * 0.9f;
            Vector3 objetivo = frente + der * largoMano * 1.1f + fw * largoMano * 0.3f - up * largoMano * 0.2f;
            float L1 = Vector3.Distance(hombro.position, codo.position), L2 = Vector3.Distance(codo.position, mano.position);
            Vector3 a = hombro.position; Vector3 dirT = objetivo - a; float d = Mathf.Clamp(dirT.magnitude, Mathf.Abs(L1 - L2) + 1e-3f, L1 + L2 - 1e-3f);
            dirT.Normalize();
            // Codo hacia afuera y un poco abajo y al frente (como en el saludo de referencia)
            Vector3 polo = (der * 1.0f - up * 0.35f + fw * 0.25f).normalized;
            Vector3 perp = Vector3.ProjectOnPlane(polo, dirT).normalized;
            float cosA = (L1 * L1 + d * d - L2 * L2) / (2f * L1 * d);
            float A = Mathf.Acos(Mathf.Clamp(cosA, -1f, 1f));
            Vector3 posCodo = a + dirT * L1 * Mathf.Cos(A) + perp * L1 * Mathf.Sin(A);
            hombro.rotation = Quaternion.FromToRotation(codo.position - hombro.position, posCodo - a) * hombro.rotation;
            codo.rotation = Quaternion.FromToRotation(mano.position - codo.position, (a + dirT * d) - codo.position) * codo.rotation;
            // Dedos hacia la frente: la mano apunta de la muñeca hacia el centro de la frente
            if (dedo) mano.rotation = Quaternion.FromToRotation(dedo.position - mano.position, frente - mano.position) * mano.rotation;
            var hph = new HumanPoseHandler(an.avatar, copia.transform);
            var hp = new HumanPose(); hph.GetHumanPose(ref hp);
            var res = new Dictionary<string, float>();
            for (int i = 0; i < HumanTrait.MuscleCount; i++)
            {
                string n = HumanTrait.MuscleName[i];
                if (n.StartsWith("Right Shoulder") || n.StartsWith("Right Arm") || n.StartsWith("Right Forearm") || n.StartsWith("Right Hand")) res[n] = hp.muscles[i];
            }
            log.AppendLine("Saludo (IK): " + string.Join(", ", res.Select(kv => $"{kv.Key} {kv.Value:0.00}")) + $"; error mano-objetivo {Vector3.Distance(mano.position, objetivo) / alturaCabeza:0.00} cabezas");
            return res;
        }
        finally { Object.DestroyImmediate(copia); }
    }

    static bool EsPierna(string m) => m.Contains("Upper Leg") || m.Contains("Lower Leg") || m.Contains("Foot") && !m.Contains("FootT") && !m.Contains("FootQ") || m.Contains("Toes");

    /// Importa goku_base_salida.fbx (exportado de Blender con la pose de salida) como Humanoid, igual que el vuelo.
    static AnimationClip ImportarSalida()
    {
        string ruta = Carpeta + "goku_base_salida.fbx";
        var mi = AssetImporter.GetAtPath(ruta) as ModelImporter;
        if (mi == null) return null;
        if (mi.animationType != ModelImporterAnimationType.Human || mi.clipAnimations.Length == 0 || mi.clipAnimations[0].name != "Goku_Salida_Blender")
        {
            mi.globalScale = 8.43f; mi.useFileScale = true;
            mi.animationType = ModelImporterAnimationType.Human; mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            mi.importAnimation = true; mi.materialImportMode = ModelImporterMaterialImportMode.None;
            var cl = mi.defaultClipAnimations;
            foreach (var c in cl) { c.name = "Goku_Salida_Blender"; c.loopTime = true; c.lockRootRotation = true; c.keepOriginalOrientation = true; c.lockRootHeightY = true; c.keepOriginalPositionY = true; c.lockRootPositionXZ = true; c.keepOriginalPositionXZ = true; }
            mi.clipAnimations = cl;
            mi.SaveAndReimport();
        }
        return ClipDe(ruta);
    }

    static AnimationCurve K2(float v, float largo) => new AnimationCurve(new Keyframe(0f, v), new Keyframe(largo, v));

    /// Curva suave por puntos (t, valor)
    static AnimationCurve Curva(params (float t, float v)[] p)
    {
        var c = new AnimationCurve(p.Select(x => new Keyframe(x.t, x.v)).ToArray());
        for (int i = 0; i < c.length; i++)
        {
            AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
        }
        return c;
    }

    /// Oscilación de 'ciclos' vueltas completas en 'largo' segundos (cierra el loop)
    static AnimationCurve Vaiven(float v, float amp, int ciclos, float largo)
    {
        var c = new AnimationCurve(); int n = 8 * ciclos;
        for (int i = 0; i <= n; i++) c.AddKey(largo * i / n, v + amp * Mathf.Sin(2f * Mathf.PI * ciclos * i / n));
        return c;
    }

    /// Copia todas las curvas de músculos de 'fuente' (sin objetivos de IK de pies y manos) y reemplaza las de 'cambios'.
    static AnimationClip CrearClip(AnimationClip fuente, string ruta, Dictionary<string, AnimationCurve> cambios, StringBuilder log, bool loop = true)
    {
        var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ruta);
        if (clip == null) { clip = new AnimationClip(); AssetDatabase.CreateAsset(clip, ruta); }
        clip.ClearCurves(); clip.frameRate = fuente.frameRate;
        var usados = new HashSet<string>();
        int n = 0;
        foreach (var b in AnimationUtility.GetCurveBindings(fuente))
        {
            string p = b.propertyName;
            if (System.Text.RegularExpressions.Regex.IsMatch(p, @"^(Left|Right)(Foot|Hand)(T|Q)\.[xyzw]$")) continue;   // objetivos de IK
            var c = cambios.TryGetValue(p, out var nuevo) ? nuevo : AnimationUtility.GetEditorCurve(fuente, b);
            AnimationUtility.SetEditorCurve(clip, b, c); usados.Add(p); n++;
        }
        foreach (var kv in cambios.Where(kv => !usados.Contains(kv.Key) && !System.Text.RegularExpressions.Regex.IsMatch(kv.Key, @"^(Left|Right)(Foot|Hand)(T|Q)\.[xyzw]$")))
        { AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), kv.Key), kv.Value); n++; }
        var st = AnimationUtility.GetAnimationClipSettings(fuente);
        st.loopTime = loop; st.loopBlend = loop; st.stopTime = cambios.Values.Max(c => c.length > 0 ? c.keys[c.length - 1].time : 0f);
        AnimationUtility.SetAnimationClipSettings(clip, st);
        EditorUtility.SetDirty(clip);
        log.AppendLine($"{clip.name}: {n} curvas, {cambios.Count} cambiadas, humano {clip.humanMotion}");
        return clip;
    }

    /// Fotos de lado y 3/4 de cada clip sobre una copia temporal de Goku_Base (pose horneada; no toca la escena).
    static void Fotos(List<(AnimationClip clip, float t, string nombre)> clips, StringBuilder log)
    {
        var baseT = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(t => t.name == "Goku_Base");
        if (baseT == null) { log.AppendLine("Sin Goku_Base en la escena: no hay fotos."); return; }
        var copia = Object.Instantiate(baseT.gameObject); copia.hideFlags = HideFlags.HideAndDontSave;
        var objs = new List<Object> { copia };
        try
        {
            copia.SetActive(true);
            copia.transform.SetPositionAndRotation(new Vector3(0, -9000, 0), Quaternion.identity); copia.transform.localScale = baseT.lossyScale;
            var an = copia.GetComponent<Animator>();
            var camGo = new GameObject("__CamGoku") { hideFlags = HideFlags.HideAndDontSave }; objs.Add(camGo);
            var cam = camGo.AddComponent<Camera>(); cam.cullingMask = 1 << 31; cam.fieldOfView = 30;
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.35f, 0.38f, 0.42f);
            var luzGo = new GameObject("__LuzGoku") { hideFlags = HideFlags.HideAndDontSave }; objs.Add(luzGo);
            var luz = luzGo.AddComponent<Light>(); luz.type = LightType.Directional; luz.cullingMask = 1 << 31; luz.intensity = 1.4f;
            var rt = new RenderTexture(640, 640, 24); var tex = new Texture2D(640, 640, TextureFormat.RGB24, false); objs.Add(rt); objs.Add(tex);
            foreach (var (clip, tFoto, nombreFoto) in clips)
            {
                clip.SampleAnimation(copia, tFoto);
                // El clip de Blender ya acuesta a Goku (RootQ): la raíz va derecha, mirando a +Z como en CM4
                copia.transform.SetPositionAndRotation(new Vector3(0, -9000, 0), Quaternion.identity);
                var pose = new GameObject("__PoseGoku") { hideFlags = HideFlags.HideAndDontSave }; objs.Add(pose);
                foreach (var sk in copia.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!sk.enabled || !sk.gameObject.activeInHierarchy) continue;
                    var m = new Mesh { hideFlags = HideFlags.HideAndDontSave }; objs.Add(m); sk.BakeMesh(m, true);
                    var go = new GameObject(sk.name) { hideFlags = HideFlags.HideAndDontSave, layer = 31 }; objs.Add(go);
                    go.transform.SetParent(pose.transform, false); go.transform.SetPositionAndRotation(sk.transform.position, sk.transform.rotation);
                    go.AddComponent<MeshFilter>().sharedMesh = m; go.AddComponent<MeshRenderer>().sharedMaterials = sk.sharedMaterials;
                }
                var rs = pose.GetComponentsInChildren<Renderer>(); if (rs.Length == 0) continue;
                var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
                float d = b.extents.magnitude / Mathf.Tan(15f * Mathf.Deg2Rad) * 1.1f;
                foreach (var (nombre, dir) in new[] { ("lado", Vector3.right), ("34", (Vector3.right + Vector3.up * 0.4f + Vector3.forward * 0.7f).normalized), ("frente", (Vector3.forward + Vector3.up * 0.15f).normalized), ("atras", (Vector3.back + Vector3.up * 0.3f).normalized) })
                {
                    cam.transform.position = b.center + dir * d; cam.transform.LookAt(b.center, Vector3.up);
                    luzGo.transform.rotation = Quaternion.LookRotation(-dir + Vector3.down * 0.4f);
                    cam.targetTexture = rt; cam.Render(); cam.targetTexture = null;
                    RenderTexture.active = rt; tex.ReadPixels(new Rect(0, 0, 640, 640), 0, 0); tex.Apply(); RenderTexture.active = null;
                    System.IO.File.WriteAllBytes($"Logs/goku_vuelo/{nombreFoto}_{nombre}.png", tex.EncodeToPNG());
                }
                Object.DestroyImmediate(pose);   // si no, la pose de este clip sale encimada en la foto del siguiente
            }
            log.AppendLine("Fotos: Logs/goku_vuelo/<pose>_lado|34|frente|atras.png");
        }
        finally { foreach (var o in objs) if (o != null) Object.DestroyImmediate(o); }
    }
}
