using UnityEngine;

/// <summary>Hace girar la hélice de la avioneta sobre su eje (en el espacio de su padre).</summary>
public class GirarHelice : MonoBehaviour
{
    public Vector3 eje = Vector3.forward;
    [Tooltip("Vueltas por segundo")] public float vueltas = 6f;
    Quaternion inicial;

    void Awake() => inicial = transform.localRotation;

    void Update() => transform.localRotation = Quaternion.AngleAxis(Time.time * vueltas * 360f, eje) * inicial;
}
