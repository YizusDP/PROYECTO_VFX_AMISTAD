using UnityEngine;

public class CaminataSencilla : MonoBehaviour
{
    [Header("Recorrido")]
    [SerializeField] private Vector3 pointB = new Vector3(0, 0, 5);
    [SerializeField] private float speed = 2f;

    [Header("Corrección de orientación del modelo")]
    [SerializeField] private float yawOffset = 0f;
    [SerializeField] private float rotationSpeed = 5f;

    [Header("Precisión de llegada")]
    [SerializeField] private float arrivalThreshold = 0.05f;

    [Header("Activación")]
    [Tooltip("Si está destildado, el personaje se queda quieto hasta que algo llame a Activar()")]
    [SerializeField] private bool empezarCaminandoDeInmediato = false;

    private bool caminando;
    private Vector3 pointA;
    private Vector3 target;

    void Start()
    {
        pointA = transform.position;
        target = transform.position + pointB;
        caminando = empezarCaminandoDeInmediato;
    }

    // Llama esto desde una Signal de Timeline (o desde cualquier otro script) para que arranque a caminar
    public void Activar()
    {
        if (caminando) return; // evita reiniciar si ya está caminando
        pointA = transform.position;
        target = transform.position + pointB;
        caminando = true;
    }

    void Update()
    {
        if (!caminando) return;

        transform.position = Vector3.MoveTowards(transform.position, target, speed * Time.deltaTime);

        Vector3 dir = target - transform.position;
        if (dir.sqrMagnitude > 0.001f)
        {
            Quaternion lookRot = Quaternion.LookRotation(dir) * Quaternion.Euler(0f, yawOffset, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, lookRot, Time.deltaTime * rotationSpeed);
        }

        if (Vector3.Distance(transform.position, target) < arrivalThreshold)
            caminando = false; // llega a Point B y se queda quieto ahí, ya no regresa
    }
}