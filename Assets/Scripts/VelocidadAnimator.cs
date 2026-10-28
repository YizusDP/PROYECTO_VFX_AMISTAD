using UnityEngine;

/// <summary>
/// Deja que el Timeline controle la velocidad de un Animator (Animator.speed no se puede animar directo).
/// 1 = la animación corre normal, 0 = se congela en la pose actual.
/// Va en un objeto aparte (VELOCIDAD_MEMI / VELOCIDAD_ALE) con su propia pista: si la curva se mete en el clip
/// del recorrido del personaje humanoide, el Timeline deja de respetar la caminata del Animator Controller.
/// </summary>
public class VelocidadAnimator : MonoBehaviour
{
    public Animator animator;
    [Range(0f, 1f)] public float velocidad = 1f;

    void LateUpdate()
    {
        if (animator != null && Application.isPlaying) animator.speed = velocidad;
    }
}
