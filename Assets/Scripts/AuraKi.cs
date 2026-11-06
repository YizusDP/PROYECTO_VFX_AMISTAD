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

    [Header("Luz")]
    public Color luzKi = new Color(0.55f, 0.75f, 1f);
    public Color luzSSJ = new Color(1f, 0.8f, 0.3f);
    public float luzCarga = 1500f;
    public float luzDestello = 7000f;

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

        // ---------- 2. Partículas pegadas a la cadera ----------
        if (vfx != null && modelo != null) vfx.position = Cadera(modelo);

        // ---------- 3. Luz ----------
        if (luz != null)
        {
            if (luz.enabled != encendida) luz.enabled = encendida;
            if (encendida)
            {
                float temblor = 0.85f + 0.15f * Mathf.Sin(tiempo * 37f) * Mathf.Sin(tiempo * 23f + 1.3f);
                luz.color = Color.Lerp(Color.Lerp(luzKi, luzSSJ, mezcla), Color.white, destello);
                luz.intensity = luzCarga * carga * temblor + luzDestello * destello;
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
