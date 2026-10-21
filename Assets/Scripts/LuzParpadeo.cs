using UnityEngine;

/// <summary>
/// Luz de fuego: ajusta rango e intensidad al tamaño del fuego (escala del objeto)
/// y en Play hace que parpadee y tiemble un poco, como una flama real.
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class LuzParpadeo : MonoBehaviour
{
    [Tooltip("Intensidad = intensidadRelativa × tamaño²  (URP atenúa la luz con el cuadrado de la distancia).")]
    public float intensidadRelativa = 2f;
    [Tooltip("Rango = rangoRelativo × tamaño del fuego.")]
    public float rangoRelativo = 4f;
    [Range(0f, 1f)] public float parpadeo = 0.3f;
    public float velocidad = 7f;
    [Tooltip("Cuánto se mueve la luz (fracción del tamaño del fuego).")]
    public float temblor = 0.04f;

    Light luz;
    Vector3 posicionBase;
    float semilla;

    void OnEnable()
    {
        luz = GetComponent<Light>();
        posicionBase = transform.localPosition;
        semilla = Random.Range(0f, 100f);
        Aplicar(1f, Vector3.zero);
    }

    void OnDisable()
    {
        if (Application.isPlaying) transform.localPosition = posicionBase;
    }

    void Update()
    {
        if (!Application.isPlaying) { Aplicar(1f, Vector3.zero); return; }

        float t = Time.time * velocidad;
        float n = Mathf.PerlinNoise(semilla, t) * 0.7f + Mathf.PerlinNoise(semilla + 13.7f, t * 2.3f) * 0.3f;
        float factor = 1f + (n - 0.5f) * 2f * parpadeo;
        var mov = new Vector3(Mathf.PerlinNoise(semilla + 3f, t * 0.6f) - 0.5f,
                              Mathf.PerlinNoise(semilla + 7f, t * 0.6f) - 0.5f,
                              Mathf.PerlinNoise(semilla + 11f, t * 0.6f) - 0.5f) * temblor;
        Aplicar(factor, mov);
    }

    void Aplicar(float factor, Vector3 movimiento)
    {
        if (luz == null) return;
        float tam = Mathf.Max(0.01f, transform.lossyScale.y);
        luz.range = rangoRelativo * tam;
        luz.intensity = intensidadRelativa * tam * tam * factor;
        if (Application.isPlaying) transform.localPosition = posicionBase + movimiento;
    }
}
