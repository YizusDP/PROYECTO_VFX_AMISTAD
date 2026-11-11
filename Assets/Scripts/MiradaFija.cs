using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Durante el enfrentamiento (CM8_1_FRENTE, 49.0-51.5 s del principal) Ale y Memi se miran fijamente a los ojos:
/// después de la animación (LateUpdate) gira el cuello y la cabeza de cada uno hacia los ojos del otro.
/// Solo en Play (en el preview del Timeline la pose no se reevalúa cada cuadro y el giro se acumularía).
/// Lo arma Tools > Amistad > 3 · Encuentro, saludo y final > Solo: enfrentamiento (mirada fija, puños, viento).
/// </summary>
public class MiradaFija : MonoBehaviour
{
    public PlayableDirector director;            // el principal (Director_P1_Bosque)
    public Animator ale, memi;
    public float inicio = 48.9f, llegada = 49.4f, fin = 51.5f;   // segundos del principal
    [Range(0f, 1f)] public float pesoCuello = 0.35f;
    public float alturaOjos = 1.0f;              // u sobre el hueso de la cabeza
    public float anguloMax = 75f;

    Vector3 frenteAle, frenteMemi;               // dirección de la cara en el espacio local del hueso de la cabeza
    bool listo;

    void Start()
    {
        if (ale == null || memi == null || director == null) return;
        // la cara es el eje Z local del hueso de la cabeza (rig de Mixamo). Calcularla con la pose del primer cuadro
        // (Frente) la sesgaba: Memi empezaba con la cabeza inclinada y terminaba mirando ~20° hacia arriba.
        frenteAle = Vector3.forward; frenteMemi = Vector3.forward; listo = true;
    }

    static Vector3 Frente(Animator an)
    {
        var h = an.GetBoneTransform(HumanBodyBones.Head);
        return h != null ? Quaternion.Inverse(h.rotation) * an.transform.forward : Vector3.forward;
    }

    void LateUpdate()
    {
        if (!listo || !Application.isPlaying) return;
        float t = (float)director.time;
        if (t < inicio || t >= fin) return;
        float w = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inicio, llegada, t));
        Mirar(ale, frenteAle, memi, w);
        Mirar(memi, frenteMemi, ale, w);
    }

    void Mirar(Animator quien, Vector3 frenteLocal, Animator aQuien, float w)
    {
        var cabeza = quien.GetBoneTransform(HumanBodyBones.Head);
        var cuello = quien.GetBoneTransform(HumanBodyBones.Neck);
        var otra = aQuien.GetBoneTransform(HumanBodyBones.Head);
        if (cabeza == null || otra == null || !quien.gameObject.activeInHierarchy) return;
        var ojos = otra.position + Vector3.up * alturaOjos;
        if (cuello != null)
        {
            var q = Giro(cabeza.rotation * frenteLocal, ojos - cabeza.position);
            cuello.rotation = Quaternion.Slerp(Quaternion.identity, q, w * pesoCuello) * cuello.rotation;
        }
        var q2 = Giro(cabeza.rotation * frenteLocal, ojos - cabeza.position);
        cabeza.rotation = Quaternion.Slerp(Quaternion.identity, q2, w) * cabeza.rotation;
    }

    Quaternion Giro(Vector3 desde, Vector3 hacia)
    {
        var q = Quaternion.FromToRotation(desde, hacia);
        q.ToAngleAxis(out float ang, out Vector3 eje);
        if (ang > 180f) ang -= 360f;
        return Quaternion.AngleAxis(Mathf.Clamp(ang, -anguloMax, anguloMax), eje);
    }
}
