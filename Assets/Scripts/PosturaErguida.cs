using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Endereza a un personaje humanoide en un tramo del Timeline principal, después de la animación (LateUpdate):
/// baja los hombros (la animación del saludo los sube hasta las orejas al levantar los brazos y esconde el cuello:
/// se leía "jorobado"), abre un poco el pecho y alarga el cuello. Las manos se regresan a su lugar con IK de dos huesos
/// (brazo y antebrazo), así el choque de palmas y el agarre de dedos no se mueven.
/// Solo en Play, como MiradaFija (en el preview del Timeline la pose no se reevalúa cada cuadro y el giro se acumularía).
/// Lo arma Tools > Amistad > 3 · Encuentro, saludo y final > Postura erguida de Memi.
/// </summary>
[DefaultExecutionOrder(100)]   // después de MiradaFija
public class PosturaErguida : MonoBehaviour
{
    public PlayableDirector director;
    public Animator personaje;
    public float inicio = 51.3f, llegada = 51.8f, salida = 61.8f, fin = 62.3f;   // segundos del principal
    [Range(0f, 30f)] public float bajarHombros = 14f;
    [Range(0f, 15f)] public float abrirPecho = 5f;
    [Range(0f, 20f)] public float alargarCuello = 7f;
    [Range(-10f, 10f)] public float cabeza = -3f;          // compensa el cuello para que siga mirando al mismo lugar
    public bool mantenerManos = true;

    void LateUpdate()
    {
        if (!Application.isPlaying || director == null || personaje == null || !personaje.gameObject.activeInHierarchy) return;
        float t = (float)director.time;
        if (t < inicio || t > fin) return;
        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inicio, llegada, t)) * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(salida, fin, t)));
        if (w <= 0.001f) return;

        var an = personaje;
        Transform B(HumanBodyBones b) => an.GetBoneTransform(b);
        var manoI = B(HumanBodyBones.LeftHand); var manoD = B(HumanBodyBones.RightHand);
        Vector3 pI = manoI ? manoI.position : Vector3.zero, pD = manoD ? manoD.position : Vector3.zero;
        Quaternion rI = manoI ? manoI.rotation : Quaternion.identity, rD = manoD ? manoD.rotation : Quaternion.identity;

        var derecha = an.transform.right;
        var pecho = B(HumanBodyBones.UpperChest) ?? B(HumanBodyBones.Chest);
        if (pecho) pecho.rotation = Quaternion.AngleAxis(-abrirPecho * w, derecha) * pecho.rotation;   // extensión: el pecho se abre hacia atrás
        var cuello = B(HumanBodyBones.Neck);
        if (cuello) cuello.rotation = Quaternion.AngleAxis(-alargarCuello * w, derecha) * cuello.rotation;
        var cab = B(HumanBodyBones.Head);
        if (cab) cab.rotation = Quaternion.AngleAxis(-cabeza * w, derecha) * cab.rotation;
        Bajar(B(HumanBodyBones.LeftShoulder), B(HumanBodyBones.LeftUpperArm), bajarHombros * w);
        Bajar(B(HumanBodyBones.RightShoulder), B(HumanBodyBones.RightUpperArm), bajarHombros * w);

        if (mantenerManos)
        {
            if (manoI) { DosHuesos(B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), manoI, pI); manoI.rotation = rI; }
            if (manoD) { DosHuesos(B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), manoD, pD); manoD.rotation = rD; }
        }
    }

    // gira la clavícula para que el hombro baje (la punta del hueso va hacia abajo)
    static void Bajar(Transform hombro, Transform brazo, float grados)
    {
        if (hombro == null || brazo == null || grados <= 0f) return;
        var afuera = brazo.position - hombro.position;
        var eje = Vector3.Cross(afuera, Vector3.down);
        if (eje.sqrMagnitude < 1e-8f) return;
        hombro.rotation = Quaternion.AngleAxis(grados, eje.normalized) * hombro.rotation;
    }

    // IK analítico de dos huesos conservando el plano del codo
    static void DosHuesos(Transform a, Transform b, Transform c, Vector3 objetivo)
    {
        if (a == null || b == null || c == null) return;
        float la = Vector3.Distance(a.position, b.position), lb = Vector3.Distance(b.position, c.position);
        var normal = Vector3.Cross(b.position - a.position, c.position - b.position);
        if (normal.sqrMagnitude < 1e-8f) normal = Vector3.Cross(b.position - a.position, Vector3.up);
        normal.Normalize();
        // 1) ángulo del codo para alcanzar la distancia
        float d = Mathf.Clamp(Vector3.Distance(a.position, objetivo), Mathf.Abs(la - lb) + 1e-4f, la + lb - 1e-4f);
        float cosObj = Mathf.Clamp((la * la + lb * lb - d * d) / (2f * la * lb), -1f, 1f);
        float angObj = Mathf.Acos(cosObj) * Mathf.Rad2Deg;                                  // ángulo interior deseado
        float angAct = Vector3.Angle(a.position - b.position, c.position - b.position);      // ángulo interior actual
        var orig = b.rotation;
        b.rotation = Quaternion.AngleAxis(angAct - angObj, normal) * orig;
        float e1 = Mathf.Abs(Vector3.Distance(a.position, c.position) - d);
        b.rotation = Quaternion.AngleAxis(angObj - angAct, normal) * orig;
        float e2 = Mathf.Abs(Vector3.Distance(a.position, c.position) - d);
        if (e1 < e2) b.rotation = Quaternion.AngleAxis(angAct - angObj, normal) * orig;   // el signo que sí llega
        // 2) apunta el brazo para que la mano caiga en el objetivo
        a.rotation = Quaternion.FromToRotation(c.position - a.position, objetivo - a.position) * a.rotation;
    }
}
