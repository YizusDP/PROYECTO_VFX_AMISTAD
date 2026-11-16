using UnityEngine;

/// <summary>
/// Aura de Ki y transformación a Super Saiyajin de Goku.
/// La controlan cuatro valores (carga, mezcla, destello, rayos) y el tiempo, que anima la pista "Goku · aura Ki" del
/// Timeline de la ciudad (Director_P2). Todo sale de esos valores, sin Random ni Time.time: al adelantar o regresar el
/// Timeline el aura, la luz y los rayos se ven exactamente igual en el mismo segundo.
/// Cada cuadro:
///   1. Pasa carga/mezcla/destello/tiempo al shader "Amistad/Aura Ki" de los cascarones (MaterialPropertyBlock, sin
///      crear copias de material). Si el aura está apagada, apaga los cascarones: cero draw calls extra.
///   2. Pega el objeto de partículas (vfx) a la cadera del modelo visible (Goku_Base o Goku_SSJ).
///   3. Luz puntual del aura: azul al cargar, dorada en SSJ y un golpe de luz blanca en el destello.
///   4. Rayos: LineRenderers en zigzag que nacen junto a huesos al azar. El "azar" usa como semilla el número de
///      golpe (tiempo * golpesPorSegundo), así es repetible.
/// Lo crea y conecta Tools > Amistad > Goku: aura Ki y SSJ.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class AuraKi : MonoBehaviour
{
    [Header("Los anima el Timeline (pista \"Goku · aura Ki\")")]
    [Range(0f, 1f)] public float carga;
    [Tooltip("0 = Ki blanco-azul (cargando), 1 = aura dorada de Super Saiyajin")]
    [Range(0f, 1f)] public float mezcla;
    [Tooltip("Golpe de luz blanca que tapa el cambio de Goku_Base a Goku_SSJ")]
    [Range(0f, 1f)] public float destello;
    [Tooltip("Cuántos rayos hay a la vez (0 = ninguno)")]
    [Range(0f, 1f)] public float rayos;
    [Tooltip("Segundos del Timeline de la ciudad (lo anima el Timeline en línea recta)")]
    public float tiempo;

    [Header("Referencias (las llena la herramienta)")]
    public Animator[] modelos;
    public Renderer[] cascarones;
    public Transform vfx;
    public Light luz;
    public LineRenderer[] lineasRayo;
    [Tooltip("Esfera del aura (VFX/Aura_Envoltura). Si está vacío se busca por nombre")]
    public Transform envoltura;

    [Header("Envoltura pegada al cuerpo (sigue la mano)")]
    [Tooltip("Se ajusta cada cuadro a los huesos visibles: se inclina y se estira hacia la mano si sube más que la cabeza")]
    public bool ajustarAlCuerpo = true;
    [Tooltip("Espacio extra a los lados (u)")] public float margenLado = 1.2f;
    [Tooltip("Espacio extra adelante y atrás (u)")] public float margenFrente = 1.0f;
    [Tooltip("Espacio extra en la punta, arriba de la cabeza o del puño (u)")] public float margenArriba = 1.6f;
    [Tooltip("Espacio extra bajo los pies (u)")] public float margenAbajo = 0.6f;
    [Tooltip("Lo que sube el pelo sobre el hueso de la cabeza (u)")] public float alturaPelo = 2.6f;
    [Tooltip("Qué tan suave pasa la punta del aura de la cabeza a la mano (u). Más alto = cambio más gradual")]
    public float suavidadPunta = 1.2f;
    [Tooltip("1 = ningún hueso se sale; menos de 1 deja que puños y pies rocen la orilla (aura más pegada)")]
    [Range(0.8f, 1.1f)] public float holgura = 0.95f;
    [Tooltip("Volando: cuánto se inclinan las lenguas hacia atrás (al lado contrario de la mano). 0 = siempre hacia arriba")]
    [Range(0f, 2f)] public float arrastre = 0.9f;
    [Tooltip("Volando: estiramiento del ruido (_Estirar). Más alto = picos redondos en todas direcciones en vez de rayas hacia la mano")]
    [Range(0.02f, 1f)] public float estirarVolando = 0.55f;

    [Header("Luz")]
    public Color luzKi = new Color(0.55f, 0.75f, 1f);
    public Color luzSSJ = new Color(1f, 0.8f, 0.3f);
    public float luzCarga = 1500f;
    public float luzDestello = 7000f;
    [Tooltip("Altura de la luz sobre la cadera (u)")] public float luzAltura = 1f;
    [Tooltip("Qué tan detrás de Goku va la luz (u). Si queda cerca de la cara la quema con el Bloom")]
    public float luzAtras = 3f;

    [Header("Rayos")]
    [Tooltip("Golpes por segundo de cada línea")] public float golpesPorSegundo = 7f;
    [Tooltip("Parte de cada golpe en la que el rayo se ve (el resto queda apagado)")]
    [Range(0.1f, 1f)] public float vidaRayo = 0.55f;
    [Tooltip("Largo del rayo (u). Goku mide ~17 u")] public float largo = 8f;
    [Tooltip("Qué tan lejos del hueso nace (u)")] public float separacion = 1.2f;
    public float zigzag = 1.4f;
    [Range(4, 24)] public int segmentos = 10;
    public float ancho = 0.35f;
    public Color colorRayoKi = new Color(0.7f, 0.85f, 1f);
    public Color colorRayoSSJ = new Color(0.8f, 0.92f, 1f);

    static readonly HumanBodyBones[] Huesos =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot,
    };

    static readonly int IdCarga = Shader.PropertyToID("_Carga");
    static readonly int IdMezcla = Shader.PropertyToID("_Mezcla");
    static readonly int IdDestello = Shader.PropertyToID("_Destello");
    static readonly int IdTiempo = Shader.PropertyToID("_TiempoAura");
    static readonly int IdDirLlamas = Shader.PropertyToID("_DirLlamas");
    static readonly int IdEstirar = Shader.PropertyToID("_Estirar");

    MaterialPropertyBlock bloque;
    Vector3[] puntos;

    void OnEnable() => Aplicar();
    void LateUpdate() => Aplicar();
    // Unity lo llama justo después de que el Timeline/Animator escribe los valores animados (también en el editor al
    // mover el cabezal del Timeline), así la vista previa se actualiza aunque no corra el juego.
    void OnDidApplyAnimationProperties() => Aplicar();

    public void Aplicar()
    {
        bool encendida = carga > 0.001f || destello > 0.001f;
        var modelo = ModeloVisible();

        // ---------- 1. Cascarones (shader Aura Ki) ----------
        if (bloque == null) bloque = new MaterialPropertyBlock();
        if (cascarones != null)
        {
            foreach (var r in cascarones)
            {
                if (r == null) continue;
                if (r.enabled != encendida) r.enabled = encendida;
                if (!encendida) continue;
                r.GetPropertyBlock(bloque);
                bloque.SetFloat(IdCarga, carga);
                bloque.SetFloat(IdMezcla, mezcla);
                bloque.SetFloat(IdDestello, destello);
                bloque.SetFloat(IdTiempo, tiempo);
                r.SetPropertyBlock(bloque);
            }
        }

        // ---------- 2. Envoltura y partículas pegadas al cuerpo ----------
        if (vfx != null && modelo != null)
        {
            if (envoltura == null) envoltura = vfx.Find("Aura_Envoltura");
            if (ajustarAlCuerpo && modelo.isHuman && encendida) AjustarAlCuerpo(modelo);
            else
            {
                vfx.position = Cadera(modelo);
                PonerLlamas(Vector3.up, -1f);
            }
        }

        // ---------- 3. Luz ----------
        if (luz != null)
        {
            if (luz.enabled != encendida) luz.enabled = encendida;
            if (encendida)
            {
                float temblor = 0.85f + 0.15f * Mathf.Sin(tiempo * 37f) * Mathf.Sin(tiempo * 23f + 1.3f);
                luz.color = Color.Lerp(Color.Lerp(luzKi, luzSSJ, mezcla), Color.white, destello);
                // Sube poco a poco (carga²): al empezar apenas se nota, y no quema la cara con el Bloom
                luz.intensity = luzCarga * carga * carga * temblor + luzDestello * destello;
                // Va a la altura de la cadera y un poco detrás de Goku: ilumina el contorno, no la cara
                if (modelo != null)
                    luz.transform.position = Cadera(modelo) + Vector3.up * luzAltura - modelo.transform.forward * luzAtras;
            }
        }

        // ---------- 4. Rayos ----------
        if (lineasRayo != null)
            for (int i = 0; i < lineasRayo.Length; i++) Rayo(lineasRayo[i], i, modelo);
    }

    Animator ModeloVisible()
    {
        if (modelos == null) return null;
        foreach (var m in modelos)
            if (m != null && m.gameObject.activeInHierarchy) return m;
        return null;
    }

    static readonly HumanBodyBones[] HuesosForma =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand,
        HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes,
        HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot, HumanBodyBones.RightToes,
    };
    [System.NonSerialized] public float ultimoK;
    readonly System.Collections.Generic.List<Vector3> forma = new System.Collections.Generic.List<Vector3>(24);

    /// Ajusta la envoltura (elipsoide) a la pose de este cuadro. Todo sale de la pose (sin memoria entre cuadros),
    /// así que al adelantar o regresar el Timeline se ve igual.
    ///   - Eje: de los pies a la "punta", que es la cima del pelo o la mano que suba más (mezcla suave, sin saltos).
    ///     Si Goku levanta el puño al despegar, el aura se inclina y se estira hacia esa mano.
    ///   - Tamaño: la caja de los huesos en ese eje + márgenes; luego se agranda lo justo para que ningún hueso
    ///     (codos, puños, rodillas) quede fuera del elipsoide.
    void AjustarAlCuerpo(Animator m)
    {
        Vector3 Hueso(HumanBodyBones b) { var t = m.GetBoneTransform(b); return t != null ? t.position : m.transform.position; }
        Vector3 cadera = Hueso(HumanBodyBones.Hips), cabeza = Hueso(HumanBodyBones.Head);
        Vector3 u0 = cabeza - cadera;
        u0 = u0.sqrMagnitude > 1e-6f ? u0.normalized : m.transform.up;
        Vector3 cima = cabeza + u0 * alturaPelo;

        // Puños: un poco más allá del hueso de la mano (los nudillos)
        Vector3 Puno(HumanBodyBones mano, HumanBodyBones ante)
        {
            Vector3 h = Hueso(mano), d = h - Hueso(ante);
            return d.sqrMagnitude > 1e-6f ? h + d.normalized * 0.9f : h;
        }
        Vector3 punoI = Puno(HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm);
        Vector3 punoD = Puno(HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm);

        float tau = Mathf.Max(0.05f, suavidadPunta);
        Vector3 punta = Suave(cadera, u0, tau, cima, punoI, punoD);
        Vector3 pies = Suave(cadera, -u0, tau, Hueso(HumanBodyBones.LeftFoot), Hueso(HumanBodyBones.RightFoot));

        Vector3 eje = punta - pies;
        eje = eje.sqrMagnitude > 1e-4f ? eje.normalized : u0;
        Vector3 frente = Vector3.ProjectOnPlane(m.transform.forward, eje);
        if (frente.sqrMagnitude < 1e-4f) frente = Vector3.ProjectOnPlane(-u0, eje);
        Quaternion rot = Quaternion.LookRotation(frente.normalized, eje);
        Quaternion inv = Quaternion.Inverse(rot);

        forma.Clear();
        foreach (var b in HuesosForma) { var t = m.GetBoneTransform(b); if (t != null) forma.Add(inv * (t.position - pies)); }
        forma.Add(inv * (cima - pies)); forma.Add(inv * (punoI - pies)); forma.Add(inv * (punoD - pies));

        Vector3 lo = forma[0], hi = forma[0];
        foreach (var p in forma) { lo = Vector3.Min(lo, p); hi = Vector3.Max(hi, p); }
        lo -= new Vector3(margenLado, margenAbajo, margenFrente);
        hi += new Vector3(margenLado, margenArriba, margenFrente);
        Vector3 c = (lo + hi) * 0.5f, h = (hi - lo) * 0.5f;
        // Corte redondo (igual de ancho que de profundo): de frente y de lado se ve el mismo óvalo, como en las refs
        h.x = h.z = Mathf.Max(h.x, h.z);

        // Que ningún hueso quede fuera del elipsoide (la caja deja fuera las esquinas: codos, puños, pies abiertos).
        // Se agranda más a lo ancho que a lo alto para que el aura no crezca hacia arriba. "holgura" deja que un puño
        // roce la orilla (el shader todavía empuja la superficie hacia afuera con _Grosor).
        float k = 0f;
        foreach (var p in forma)
        {
            Vector3 d = p - c;
            k = Mathf.Max(k, new Vector3(d.x / h.x, d.y / h.y, d.z / h.z).magnitude);
        }
        ultimoK = k;
        float s = Mathf.Max(1f, k * holgura);
        h.x *= Mathf.Pow(s, 1.3f); h.z *= Mathf.Pow(s, 1.3f); h.y *= Mathf.Pow(s, 0.5f);

        // VFX (partículas) solo gira en horizontal: las lenguas de partículas siempre suben, aunque el aura se incline
        // hacia la mano. Sus formas están 1.5 u arriba del origen de VFX: VFX va 1.5 u abajo del centro del aura.
        const float subidaVfx = 1.5f;
        Vector3 centro = pies + rot * c;
        Vector3 yaw = Vector3.ProjectOnPlane(m.transform.forward, Vector3.up);
        Quaternion rotVfx = yaw.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(yaw.normalized, Vector3.up) : Quaternion.identity;
        vfx.SetPositionAndRotation(centro - Vector3.up * subidaVfx, rotVfx);
        if (envoltura != null)
        {
            envoltura.SetPositionAndRotation(centro, rot);
            envoltura.localScale = h * 2f;              // la esfera de Unity mide 1 de diámetro

            // Lenguas: el Shader Graph las empuja en _DirLlamas (espacio del objeto). Si el aura se inclina hacia la
            // mano, las lenguas NO deben seguir el eje: van hacia arriba y, entre más inclinada, un poco hacia atrás
            // (al lado contrario del puño). En espacio del objeto: S^-1 · R^-1 · dirección del mundo.
            Vector3 ladeo = eje - Vector3.up * Vector3.Dot(eje, Vector3.up);      // parte horizontal del eje
            Vector3 dirMundo = (Vector3.up - ladeo * arrastre).normalized;
            Vector3 dirObj = inv * dirMundo;
            dirObj = new Vector3(dirObj.x / h.x, dirObj.y / h.y, dirObj.z / h.z).normalized;
            float inclinacion = Mathf.Clamp01(ladeo.magnitude / 0.6f);           // 0 de pie, 1 a ~37° de ladeo
            PonerLlamas(dirObj, inclinacion);
        }
    }

    /// Dirección de las lenguas y estiramiento del ruido solo en la envoltura (inclinacion < 0 = valores del material)
    void PonerLlamas(Vector3 dirObj, float inclinacion)
    {
        var r = envoltura != null ? envoltura.GetComponent<Renderer>() : null;
        if (r == null) return;
        if (bloque == null) bloque = new MaterialPropertyBlock();
        r.GetPropertyBlock(bloque);
        bloque.SetVector(IdDirLlamas, dirObj);
        var mat = r.sharedMaterial;
        float est = mat != null && mat.HasProperty(IdEstirar) ? mat.GetFloat(IdEstirar) : 0.1f;
        bloque.SetFloat(IdEstirar, inclinacion < 0f ? est : Mathf.Lerp(est, estirarVolando, inclinacion));
        r.SetPropertyBlock(bloque);
    }

    /// Promedio de los puntos pesado por qué tan lejos llegan en la dirección "dir" (softmax): el que más sale manda,
    /// pero el cambio de uno a otro es gradual.
    static Vector3 Suave(Vector3 origen, Vector3 dir, float tau, params Vector3[] pts)
    {
        float mx = float.MinValue;
        foreach (var p in pts) mx = Mathf.Max(mx, Vector3.Dot(p - origen, dir));
        Vector3 suma = Vector3.zero; float w = 0f;
        foreach (var p in pts)
        {
            float wi = Mathf.Exp((Vector3.Dot(p - origen, dir) - mx) / tau);
            suma += p * wi; w += wi;
        }
        return suma / w;
    }

    static Vector3 Cadera(Animator a)
    {
        var h = a.isHuman ? a.GetBoneTransform(HumanBodyBones.Hips) : null;
        return h != null ? h.position : a.transform.position + a.transform.up * 8.5f;
    }

    void Rayo(LineRenderer linea, int i, Animator modelo)
    {
        if (linea == null) return;
        if (rayos <= 0.001f || modelo == null || !modelo.isHuman) { if (linea.enabled) linea.enabled = false; return; }

        // Cada línea va desfasada (proporción áurea) para que no salgan todas al mismo tiempo
        float fase = tiempo * golpesPorSegundo + i * 0.618f;
        int golpe = Mathf.FloorToInt(fase);
        float edad = fase - golpe;                       // 0..1 dentro del golpe
        uint s = Semilla(i, golpe);
        if (Azar(ref s) > rayos || edad > vidaRayo) { if (linea.enabled) linea.enabled = false; return; }
        float vida = 1f - edad / vidaRayo;               // 1 al nacer, 0 al apagarse

        var hueso = modelo.GetBoneTransform(Huesos[(int)(Azar(ref s) * Huesos.Length) % Huesos.Length]);
        if (hueso == null) { if (linea.enabled) linea.enabled = false; return; }

        // Nace un poco afuera del cuerpo y sale en una dirección al azar (un poco más hacia arriba)
        Vector3 dir = EsferaAzar(ref s);
        dir.y = dir.y * 0.7f + 0.3f;
        dir.Normalize();
        Vector3 a = hueso.position + dir * separacion;
        Vector3 desvio = EsferaAzar(ref s) * 0.6f;
        Vector3 b = a + (dir + desvio).normalized * largo * (0.5f + 0.5f * Azar(ref s));

        Vector3 eje = (b - a).normalized;
        Vector3 p1 = Vector3.Cross(eje, Vector3.up);
        if (p1.sqrMagnitude < 1e-4f) p1 = Vector3.Cross(eje, Vector3.right);
        p1.Normalize();
        Vector3 p2 = Vector3.Cross(eje, p1);

        // El zigzag se re-sortea 3 veces durante la vida del rayo: tiembla sin perder su dirección
        int sub = Mathf.Min(2, Mathf.FloorToInt(edad / vidaRayo * 3f));
        uint z = Semilla(i * 31 + sub + 7, golpe);
        int n = segmentos + 1;
        if (puntos == null || puntos.Length != n) puntos = new Vector3[n];
        for (int k = 0; k < n; k++)
        {
            float u = k / (float)(n - 1);
            float env = Mathf.Sin(u * Mathf.PI);         // puntas quietas, el centro se dobla más
            float j1 = (Azar(ref z) - 0.5f) * 2f * zigzag * env;
            float j2 = (Azar(ref z) - 0.5f) * 2f * zigzag * env * 0.5f;
            puntos[k] = Vector3.Lerp(a, b, u) + p1 * j1 + p2 * j2;
        }

        if (!linea.enabled) linea.enabled = true;
        linea.positionCount = n;
        linea.SetPositions(puntos);
        linea.widthMultiplier = ancho * (0.6f + 0.4f * vida) * (1f + destello);
        var c = Color.Lerp(colorRayoKi, colorRayoSSJ, mezcla);
        c.a = vida;
        linea.startColor = c;
        linea.endColor = c;
    }

    // ---------- Azar repetible (hash + xorshift) ----------
    static uint Semilla(int a, int b)
    {
        unchecked
        {
            uint h = (uint)(a * 73856093) ^ (uint)(b * 19349663) ^ 0x9E3779B9u;
            h ^= h >> 16; h *= 0x7feb352du; h ^= h >> 15; h *= 0x846ca68bu; h ^= h >> 16;
            return h == 0 ? 1u : h;
        }
    }

    static float Azar(ref uint s)
    {
        s ^= s << 13; s ^= s >> 17; s ^= s << 5;
        return (s & 0xFFFFFF) / 16777216f;
    }

    static Vector3 EsferaAzar(ref uint s)
    {
        float z = Azar(ref s) * 2f - 1f;
        float ang = Azar(ref s) * Mathf.PI * 2f;
        float r = Mathf.Sqrt(1f - z * z);
        return new Vector3(r * Mathf.Cos(ang), z, r * Mathf.Sin(ang));
    }
}
