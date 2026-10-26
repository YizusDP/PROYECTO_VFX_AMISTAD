using UnityEngine;

/// <summary>
/// Luz verde de un charco radiactivo: pulsa despacio (en Play) a la par del shader "Amistad/Charco Radiactivo".
/// </summary>
[ExecuteAlways]
[RequireComponent(typeof(Light))]
public class LuzRadiactiva : MonoBehaviour
{
    public float intensidad = 1500f;
    public float velocidad = 0.8f;
    [Range(0f, 1f)] public float pulso = 0.35f;

    Light luz;
    float semilla;

    void OnEnable()
    {
        luz = GetComponent<Light>();
        semilla = Mathf.Repeat(transform.position.x * 0.37f + transform.position.z * 0.11f, 10f);
        Update();
    }

    void Update()
    {
        if (luz == null) return;
        float t = Application.isPlaying ? Time.time : 0f;
        float s = 0.5f + 0.5f * Mathf.Sin(t * velocidad * 2.1f + semilla * 20f);
        luz.intensity = intensidad * (1f - pulso + pulso * s);
    }
}
