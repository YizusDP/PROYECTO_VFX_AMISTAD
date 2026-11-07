using UnityEngine;

/// <summary>
/// Deja que el Timeline controle en qué segundo va el estado "Saludo" del Animator de un personaje.
/// Va en un objeto aparte (SALUDO_ALE / SALUDO_MEMI) con su propia pista, igual que VelocidadAnimator:
/// así no se tocan las pistas de recorrido del personaje humanoide.
///  tiempo &lt; 0  -> no hace nada (el Animator sigue con lo suyo: caminata)
///  tiempo &gt;= 0 -> la primera vez mezcla suave desde la pose que tenga (la caminata al detenerse) hacia el saludo;
///                después pone el estado exactamente en ese segundo (velocidad 0: el Timeline manda).
/// Corre después de VelocidadAnimator (orden 1000) para poder dejar avanzar el Animator mientras dura la mezcla.
/// Funciona en Play (igual que la caminata y VelocidadAnimator).
/// </summary>
[DefaultExecutionOrder(1000)]
public class SaludoControl : MonoBehaviour
{
    public Animator animator;
    public string estado = "Saludo";
    [Tooltip("Duración del clip del saludo en segundos (la escribe la herramienta).")]
    public float duracion = 7f;
    [Tooltip("Segundo del saludo. Lo anima el Timeline; -1 = apagado.")]
    public float tiempo = -1f;
    [Tooltip("Segundos de mezcla desde la pose anterior (caminata) hacia el saludo.")]
    public float fundido = 0.6f;

    bool iniciado;
    float inicioMezcla = -100f;

    bool Mezclando => Time.time - inicioMezcla < fundido;

    void Update()
    {
        if (animator == null || !Application.isPlaying) return;
        if (tiempo < 0f) { iniciado = false; return; }
        if (!iniciado)
        {
            iniciado = true;
            inicioMezcla = Time.time;
            animator.speed = 1f;
            animator.CrossFadeInFixedTime(estado, fundido, 0, Mathf.Max(0f, tiempo));
            return;
        }
        if (Mezclando) return;   // dejar que termine la mezcla
        animator.speed = 0f;
        animator.Play(estado, 0, Mathf.Clamp01(tiempo / Mathf.Max(0.01f, duracion)));
    }

    void LateUpdate()
    {
        // VelocidadAnimator deja la velocidad en 0 al detenerse; durante la mezcla el Animator necesita avanzar
        if (animator != null && Application.isPlaying && tiempo >= 0f && Mezclando) animator.speed = 1f;
    }
}
