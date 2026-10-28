using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// Disolvencia (fundido cruzado) entre dos cámaras de Cinemachine, controlada por Timeline.
/// Cinemachine solo sabe hacer cortes o blends de posición; esto agrega el fundido de imagen:
/// mientras 'mezcla' > 0, una cámara auxiliar se coloca exactamente donde está 'camaraSaliente' (que sigue en movimiento
/// porque su dolly lo anima el Timeline), renderiza a una RenderTexture, y esa imagen se pinta encima de la Main Camera
/// con opacidad = mezcla. El Timeline corta la Main Camera a la toma nueva y baja 'mezcla' de 1 a 0.
/// Fuera de la transición la cámara auxiliar está apagada (no cuesta nada).
/// </summary>
[ExecuteAlways, DefaultExecutionOrder(1000)]
public class DisolvenciaCamaras : MonoBehaviour
{
    [Tooltip("Cámara de la toma que se va (se sigue renderizando mientras se desvanece)")]
    public CinemachineCamera camaraSaliente;
    [Tooltip("Main Camera (la del CinemachineBrain)")]
    public Camera camaraPrincipal;
    [Tooltip("RawImage a pantalla completa en un Canvas overlay")]
    public RawImage imagen;
    [Range(0f, 1f), Tooltip("Opacidad de la toma que se va. La anima el Timeline.")]
    public float mezcla;

    Camera aux;
    RenderTexture rt;

    void OnDisable() => Liberar();

    void Liberar()
    {
        if (imagen != null) { imagen.texture = null; imagen.enabled = false; }
        if (aux != null) { aux.targetTexture = null; DestroyImmediate(aux.gameObject); aux = null; }
        if (rt != null) { rt.Release(); DestroyImmediate(rt); rt = null; }
    }

    void LateUpdate()
    {
        bool activa = mezcla > 0.001f && camaraSaliente != null && camaraPrincipal != null && imagen != null;
        if (!activa)
        {
            if (imagen != null && imagen.enabled) imagen.enabled = false;
            if (aux != null && aux.enabled) aux.enabled = false;
            return;
        }
        if (aux == null) aux = CrearAux();

        int w = Mathf.Max(16, camaraPrincipal.pixelWidth), h = Mathf.Max(16, camaraPrincipal.pixelHeight);
        if (rt == null || rt.width != w || rt.height != h)
        {
            aux.targetTexture = null;
            if (rt != null) { rt.Release(); DestroyImmediate(rt); }
            rt = new RenderTexture(w, h, 24) { name = "RT_Disolvencia", hideFlags = HideFlags.DontSave };
        }

        // Pose actual de la toma que sale 
        camaraSaliente.InternalUpdateCameraState(Vector3.up, -1f);
        var st = camaraSaliente.State;
        aux.transform.SetPositionAndRotation(st.GetFinalPosition(), st.GetFinalOrientation());
        aux.fieldOfView = st.Lens.FieldOfView;
        aux.nearClipPlane = st.Lens.NearClipPlane;
        aux.farClipPlane = st.Lens.FarClipPlane;
        aux.targetTexture = rt;
        aux.enabled = true;

        imagen.texture = rt;
        imagen.color = new Color(1f, 1f, 1f, mezcla);
        imagen.enabled = true;
    }

    Camera CrearAux()
    {
        var go = new GameObject("CamaraDisolvencia (temporal)") { hideFlags = HideFlags.DontSave };
        go.transform.SetParent(transform, false);
        var c = go.AddComponent<Camera>();
        c.CopyFrom(camaraPrincipal);
        c.targetTexture = null;
        c.depth = camaraPrincipal.depth - 1;
        // Mismo postpro que la Main Camera para que no se note la diferencia al desvanecerse
        var dP = camaraPrincipal.GetUniversalAdditionalCameraData();
        var dA = c.GetUniversalAdditionalCameraData();
        dA.renderPostProcessing = dP.renderPostProcessing;
        dA.antialiasing = dP.antialiasing;
        dA.volumeLayerMask = dP.volumeLayerMask;
        dA.renderShadows = dP.renderShadows;
        dA.volumeTrigger = c.transform;
        c.enabled = false;
        return c;
    }
}
