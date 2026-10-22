using UnityEngine;

/// <summary>
/// Reproduce un derrumbe "horneado": una simulación de física que se grabó en el editor
/// (Tools > Amistad > Derrumbar edificio). Guarda la posición y rotación de cada pieza por cuadro,
/// así se ve igual siempre y se puede mover el cabezal del Timeline hacia adelante y atrás.
/// Lo mueve la pista "Derrumbe" del Timeline (DerrumbeTrack), que llama a SetTiempo().
/// </summary>
[DisallowMultipleComponent]
public class DerrumbeHorneado : MonoBehaviour
{
    [Tooltip("Datos de la simulación: posición y rotación de cada pieza en cada cuadro.")]
    public TextAsset datos;
    [Tooltip("Piezas que se mueven, en el mismo orden que los datos.")]
    public Transform[] piezas;
    [Tooltip("Polvo y chispas del derrumbe; se simulan con el mismo tiempo que las piezas.")]
    public ParticleSystem[] particulas;
    [HideInInspector] public float fps = 30f;
    [HideInInspector] public int cuadros;

    float[] pose;
    int nPiezas;
    float ultimoT = -1f;

    public float Duracion => cuadros > 1 ? (cuadros - 1) / fps : 0f;

    bool Cargar()
    {
        if (pose != null) return true;
        if (datos == null || piezas == null) return false;
        var b = datos.bytes;
        if (b.Length < 16 || System.BitConverter.ToInt32(b, 0) != 0x42524544) return false;
        nPiezas = System.BitConverter.ToInt32(b, 4);
        cuadros = System.BitConverter.ToInt32(b, 8);
        fps = System.BitConverter.ToSingle(b, 12);
        if (nPiezas != piezas.Length)
        {
            Debug.LogWarning($"[Derrumbe] Los datos tienen {nPiezas} piezas y el componente {piezas.Length}. Vuelve a hornear.", this);
            return false;
        }
        pose = new float[(b.Length - 16) / 4];
        System.Buffer.BlockCopy(b, 16, pose, 0, pose.Length * 4);
        if (pose.Length < nPiezas * cuadros * 7) { pose = null; return false; }
        return true;
    }

    /// <summary>Coloca el derrumbe en el segundo t de la simulación (0 = edificio intacto).</summary>
    public void SetTiempo(float t)
    {
        if (!Cargar()) return;
        t = Mathf.Max(0f, t);
        float f = Mathf.Clamp(t * fps, 0f, cuadros - 1);
        int c0 = Mathf.FloorToInt(f);
        int c1 = Mathf.Min(c0 + 1, cuadros - 1);
        float k = f - c0;
        int o0 = c0 * nPiezas * 7, o1 = c1 * nPiezas * 7;
        for (int i = 0; i < nPiezas; i++)
        {
            var tr = piezas[i];
            if (tr == null) continue;
            int a = o0 + i * 7, b = o1 + i * 7;
            var p = Vector3.LerpUnclamped(new Vector3(pose[a], pose[a + 1], pose[a + 2]), new Vector3(pose[b], pose[b + 1], pose[b + 2]), k);
            var q = Quaternion.Slerp(new Quaternion(pose[a + 3], pose[a + 4], pose[a + 5], pose[a + 6]),
                                     new Quaternion(pose[b + 3], pose[b + 4], pose[b + 5], pose[b + 6]), k);
            tr.SetLocalPositionAndRotation(p, q);
        }
        SimularParticulas(t);
    }

    // Igual que la Control Track de Unity: si el tiempo regresa, se reinicia y se simula desde 0
    void SimularParticulas(float t)
    {
        if (particulas == null) return;
        bool reiniciar = ultimoT < 0f || t < ultimoT - 1e-4f;
        float dt = t - ultimoT;
        foreach (var ps in particulas)
        {
            if (ps == null || !ps.gameObject.activeInHierarchy) continue;
            if (reiniciar) Simular(ps, t, true);
            else if (dt > 0f) Simular(ps, dt, false);
        }
        ultimoT = t;
    }

    static void Simular(ParticleSystem ps, float dt, bool reiniciar)
    {
        const float paso = 0.05f;
        if (reiniciar) ps.Simulate(0f, false, true, false);
        while (dt > paso) { ps.Simulate(paso, false, false, false); dt -= paso; }
        if (dt > 0f) ps.Simulate(dt, false, false, false);
    }

    void OnValidate() { pose = null; ultimoT = -1f; }
    void OnEnable() { ultimoT = -1f; }
}
