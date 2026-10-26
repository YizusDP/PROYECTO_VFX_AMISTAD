using UnityEngine;

/// <summary>
/// Niebla lineal (perspectiva atmosférica) solo durante la ciudad.
/// Va como hijo de Director_P2_Ciudad: el Control Track del Timeline principal activa ese objeto
/// de 17.5 s en adelante y lo regresa a apagado al salir, así el bosque y el título no llevan niebla.
/// La niebla desvanece las ruinas lejanas hacia el color del cielo: el ojo lo lee como distancia y la ciudad se siente enorme.
/// </summary>
[ExecuteAlways]
public class NieblaCiudad : MonoBehaviour
{
    [Tooltip("Debe parecerse al color del cielo en el horizonte (naranja quemado del atardecer).")]
    public Color color = new Color(0.56f, 0.33f, 0.19f);
    [Tooltip("Distancia (u) donde empieza la niebla. Los personajes quedan antes, sin niebla.")]
    public float inicio = 350f;
    [Tooltip("Distancia (u) donde la niebla ya tapa todo.")]
    public float fin = 4500f;

    void OnEnable() => Aplicar();
    void OnValidate() { if (isActiveAndEnabled) Aplicar(); }
    void OnDisable() => RenderSettings.fog = false;

    public void Aplicar()
    {
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogColor = color;
        RenderSettings.fogStartDistance = inicio;
        RenderSettings.fogEndDistance = fin;
    }
}
