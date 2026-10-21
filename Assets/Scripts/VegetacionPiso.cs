using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Dispersa vegetación sobre una superficie (p. ej. Piso_FOREST) con GPU instancing
/// (Graphics.RenderMeshInstanced). No crea GameObjects: se dibuja en Scene, Game,
/// en la vista previa de Timeline y en Play.
/// Cambia cualquier valor en el Inspector y se regenera solo.
/// </summary>
[ExecuteAlways]
public class VegetacionPiso : MonoBehaviour
{
    [System.Serializable]
    public class Capa
    {
        public string nombre = "Capa";
        public bool activa = true;
        public Mesh malla;
        public Material[] materiales;
        [Min(0)] public int cantidad = 500;

        [Tooltip("Escala base del objeto (la del modelo original en el bosque).")]
        public Vector3 escalaBase = Vector3.one;
        [Tooltip("Rotación base (la del modelo original; los FBX de Blender suelen traer -90 en X).")]
        public Vector3 rotacionBase = Vector3.zero;
        [Tooltip("Multiplicador aleatorio de escala (mín, máx).")]
        public Vector2 rangoEscala = new Vector2(0.7f, 1.3f);

        [Tooltip("0 = reparto uniforme, 1 = solo en manchones.")]
        [Range(0f, 1f)] public float agrupamiento = 0.5f;
        [Tooltip("Tamaño aproximado de los manchones, en unidades.")]
        [Min(1f)] public float tamanoManchon = 80f;

        [Tooltip("Cuánto se hunde en el piso (fracción de la altura) para que no flote.")]
        [Range(0f, 0.5f)] public float hundir = 0.05f;
        public bool proyectarSombras = true;
        [Tooltip("Dibuja ambas caras (útil para planos con textura recortada).")]
        public bool dobleCara = false;
        [Tooltip("Si está activo, esta capa no se pone en las zonas libres (camino del gato, cámara...).")]
        public bool respetarZonasLibres = true;
        [Tooltip("Multiplica el radio de las zonas libres para esta capa (p. ej. árboles más lejos de la cámara).")]
        [Min(0f)] public float multiplicadorRadioLibre = 1f;
        [Tooltip("Además, no se pone en el recorrido de la cámara ni entre la cámara y el gato.")]
        public bool evitarCamara = true;
    }

    [System.Serializable]
    public class Claro
    {
        public string nombre = "Claro";
        [Tooltip("Mueve este objeto para reubicar el claro.")]
        public Transform centro;
        [Min(0f)] public float radio = 150f;
    }

    [Tooltip("Collider sobre el que se reparte la vegetación.")]
    public Collider superficie;
    [Tooltip("Tamaño (X, Z) del área a cubrir, centrada en este objeto.")]
    public Vector2 area = new Vector2(1000f, 1000f);
    public int semilla = 2026;

    [Tooltip("Alrededor de estos objetos NO se pone vegetación (personajes, caminos, etc.).")]
    public Transform[] zonasLibres;
    [Tooltip("Puntos fijos (en mundo) donde tampoco se pone vegetación, p. ej. el camino del gato.")]
    public Vector3[] puntosLibres;
    [Tooltip("Recorrido de la cámara y línea de visión cámara-gato (solo para capas con 'Evitar Camara').")]
    public Vector3[] puntosLibresCamara;
    [Min(0f)] public float radioZonaLibre = 30f;
    [Tooltip("Borde suave: la vegetación se va haciendo menos densa hacia el centro de la zona libre en vez de cortarse de golpe.")]
    public bool bordeSuave = true;
    [Tooltip("No poner vegetación donde otra geometría con collider tapa el piso.")]
    public bool evitarTapados = false;

    [Tooltip("Zonas despejadas para TODAS las capas (p. ej. el lago de la cascada).")]
    public List<Claro> claros = new List<Claro>();

    public List<Capa> capas = new List<Capa>();

    class Lote
    {
        public Mesh malla;
        public Material[] mats;
        public readonly List<Matrix4x4[]> bloques = new List<Matrix4x4[]>();
        public Bounds limites;
        public ShadowCastingMode sombras;
    }

    const int MaxPorLlamada = 1023;
    readonly List<Lote> lotes = new List<Lote>();
    readonly List<Material> copias = new List<Material>();
    bool sucio = true;
    Vector3 ultimaPos;
    Quaternion ultimaRot;
    Vector4 ultimaFirmaClaros;

    public int TotalInstancias
    {
        get { int t = 0; foreach (var l in lotes) foreach (var b in l.bloques) t += b.Length; return t; }
    }

    void OnEnable()
    {
        sucio = true;
        RenderPipelineManager.beginCameraRendering += Dibujar;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= Dibujar;
        Liberar();
    }

    void OnValidate() { sucio = true; }

    [ContextMenu("Regenerar")]
    public void Regenerar() { Construir(); }

    void Dibujar(ScriptableRenderContext contexto, Camera cam)
    {
        if (cam.cameraType == CameraType.Preview || cam.cameraType == CameraType.Reflection) return;
        if (transform.position != ultimaPos || transform.rotation != ultimaRot || FirmaClaros() != ultimaFirmaClaros) sucio = true;
        if (sucio) Construir();

        foreach (var l in lotes)
        {
            for (int s = 0; s < l.mats.Length; s++)
            {
                var mat = l.mats[s];
                if (mat == null) continue;
                var rp = new RenderParams(mat)
                {
                    camera = cam,
                    layer = gameObject.layer,
                    shadowCastingMode = l.sombras,
                    receiveShadows = true,
                    worldBounds = l.limites,
                    lightProbeUsage = LightProbeUsage.Off
                };
                foreach (var bloque in l.bloques)
                    Graphics.RenderMeshInstanced(rp, l.malla, s, bloque);
            }
        }
    }

    void Construir()
    {
        Liberar();
        sucio = false;
        ultimaPos = transform.position;
        ultimaRot = transform.rotation;
        ultimaFirmaClaros = FirmaClaros();
        if (superficie == null || capas == null) return;

        Bounds b = superficie.bounds;
        float alto = b.max.y + 500f;
        float distancia = alto - b.min.y + 10f;
        var rnd = new System.Random(semilla);
        Vector3 centro = transform.position;

        for (int ci = 0; ci < capas.Count; ci++)
        {
            var c = capas[ci];
            if (c == null || !c.activa || c.malla == null || c.materiales == null || c.materiales.Length == 0 || c.cantidad <= 0)
                continue;

            var lista = new List<Matrix4x4>(c.cantidad);
            Quaternion rotBase = Quaternion.Euler(c.rotacionBase);
            float altoMalla = c.malla.bounds.size.y * Mathf.Abs(c.escalaBase.y);
            float desX = 137.13f * (ci + 1), desZ = 59.71f * (ci + 1);
            int intentos = c.cantidad * 6;

            for (int i = 0; i < intentos && lista.Count < c.cantidad; i++)
            {
                float x = centro.x + ((float)rnd.NextDouble() - 0.5f) * area.x;
                float z = centro.z + ((float)rnd.NextDouble() - 0.5f) * area.y;
                float azar = (float)rnd.NextDouble();
                float giro = (float)rnd.NextDouble() * 360f;
                float esc = Mathf.Lerp(c.rangoEscala.x, c.rangoEscala.y, (float)rnd.NextDouble());
                float azarZona = (float)rnd.NextDouble();

                if (c.agrupamiento > 0f)
                {
                    float n = Mathf.PerlinNoise((x + desX) / c.tamanoManchon, (z + desZ) / c.tamanoManchon);
                    float manchon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.35f, 0.75f, n));
                    if (azar > Mathf.Lerp(1f, manchon, c.agrupamiento)) continue;
                }

                var rayo = new Ray(new Vector3(x, alto, z), Vector3.down);
                if (!superficie.Raycast(rayo, out RaycastHit golpe, distancia)) continue;
                if (evitarTapados &&
                    Physics.Raycast(rayo, out RaycastHit primero, distancia, ~0, QueryTriggerInteraction.Ignore) &&
                    primero.collider != superficie && primero.point.y > golpe.point.y + 0.5f)
                    continue;
                if (EnClaro(golpe.point, azarZona)) continue;
                if (c.respetarZonasLibres && EnZonaLibre(golpe.point, c.multiplicadorRadioLibre, azarZona, c.evitarCamara)) continue;

                Vector3 pos = golpe.point - Vector3.up * (altoMalla * esc * c.hundir);
                Quaternion rot = Quaternion.AngleAxis(giro, Vector3.up) * rotBase;
                lista.Add(Matrix4x4.TRS(pos, rot, c.escalaBase * esc));
            }

            if (lista.Count == 0) continue;

            var lote = new Lote
            {
                malla = c.malla,
                sombras = c.proyectarSombras ? ShadowCastingMode.On : ShadowCastingMode.Off,
                mats = new Material[c.malla.subMeshCount]
            };
            for (int s = 0; s < lote.mats.Length; s++)
            {
                var origen = c.materiales[Mathf.Min(s, c.materiales.Length - 1)];
                if (origen == null) continue;
                var copia = new Material(origen)
                {
                    name = origen.name + " (instanciado)",
                    enableInstancing = true,
                    hideFlags = HideFlags.HideAndDontSave
                };
                if (c.dobleCara && copia.HasProperty("_Cull")) copia.SetFloat("_Cull", 0f);
                copias.Add(copia);
                lote.mats[s] = copia;
            }

            float maxEsc = Mathf.Max(Mathf.Abs(c.escalaBase.x), Mathf.Abs(c.escalaBase.y), Mathf.Abs(c.escalaBase.z)) * c.rangoEscala.y;
            var lim = new Bounds(lista[0].GetPosition(), Vector3.zero);
            foreach (var m in lista) lim.Encapsulate(m.GetPosition());
            lim.Expand(c.malla.bounds.extents.magnitude * maxEsc * 2f + 1f);
            lote.limites = lim;

            for (int k = 0; k < lista.Count; k += MaxPorLlamada)
                lote.bloques.Add(lista.GetRange(k, Mathf.Min(MaxPorLlamada, lista.Count - k)).ToArray());
            lotes.Add(lote);
        }
    }

    // Devuelve true si el punto cae en una zona libre (con borde suave, de forma probabilística).
    bool EnZonaLibre(Vector3 p, float multiplicador, float azar, bool incluirCamara)
    {
        float r = radioZonaLibre * multiplicador;
        if (r <= 0f) return false;
        float dMin = float.MaxValue;
        if (zonasLibres != null)
            foreach (var t in zonasLibres)
                if (t != null) dMin = Mathf.Min(dMin, DistanciaXZ(p, t.position));
        if (puntosLibres != null)
            foreach (var q in puntosLibres)
                dMin = Mathf.Min(dMin, DistanciaXZ(p, q));
        if (incluirCamara && puntosLibresCamara != null)
            foreach (var q in puntosLibresCamara)
                dMin = Mathf.Min(dMin, DistanciaXZ(p, q));
        if (dMin >= r) return false;
        if (!bordeSuave) return true;
        float conservar = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(r * 0.35f, r, dMin));
        return azar > conservar;
    }

    // Claros: despejados para todas las capas, con una orilla angosta donde la vegetación se va acabando.
    bool EnClaro(Vector3 p, float azar)
    {
        if (claros == null) return false;
        foreach (var cl in claros)
        {
            if (cl == null || cl.centro == null || cl.radio <= 0f) continue;
            float d = DistanciaXZ(p, cl.centro.position);
            if (d >= cl.radio) continue;
            if (!bordeSuave) return true;
            float conservar = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(cl.radio * 0.8f, cl.radio, d));
            if (azar > conservar) return true;
        }
        return false;
    }

    Vector4 FirmaClaros()
    {
        var f = Vector4.zero;
        if (claros == null) return f;
        foreach (var cl in claros)
        {
            if (cl == null || cl.centro == null) continue;
            var q = cl.centro.position;
            f += new Vector4(q.x, q.y, q.z, cl.radio);
        }
        return f;
    }

    static float DistanciaXZ(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }

    void Liberar()
    {
        foreach (var m in copias)
        {
            if (m == null) continue;
            if (Application.isPlaying) Destroy(m); else DestroyImmediate(m);
        }
        copias.Clear();
        lotes.Clear();
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.9f, 0.3f, 0.8f);
        float y = superficie != null ? superficie.bounds.center.y : transform.position.y;
        Gizmos.DrawWireCube(new Vector3(transform.position.x, y, transform.position.z), new Vector3(area.x, 1f, area.y));
        Gizmos.color = new Color(1f, 0.4f, 0.2f, 0.8f);
        if (zonasLibres != null)
            foreach (var t in zonasLibres)
                if (t != null) Gizmos.DrawWireSphere(t.position, radioZonaLibre);
        if (puntosLibres != null)
            foreach (var q in puntosLibres)
                Gizmos.DrawWireSphere(new Vector3(q.x, y, q.z), radioZonaLibre);
        if (claros != null)
        {
            Gizmos.color = new Color(0.2f, 0.9f, 1f, 1f);
            foreach (var cl in claros)
                if (cl != null && cl.centro != null)
                {
                    var c0 = cl.centro.position; c0.y = y;
                    Gizmos.DrawWireSphere(c0, cl.radio);
                }
        }
        // El recorrido de la cámara solo se marca con puntitos para no saturar la vista
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.8f);
        if (puntosLibresCamara != null)
            foreach (var q in puntosLibresCamara)
                Gizmos.DrawSphere(new Vector3(q.x, y, q.z), radioZonaLibre * 0.08f);
    }
}
