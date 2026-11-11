using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Sombras del sol más lejanas solo durante la ciudad (mismo patrón que NieblaCiudad).
/// Va como hijo de Director_P2_Ciudad: el Control Track "3 · Ciudad" lo activa y al salir se apaga.
/// La ciudad mide ~9000 u y la distancia de sombras del URP (50 u) dejaba los planos abiertos sin sombras de edificios;
/// en el bosque el sol está casi en el horizonte y con sombras largas se apagaba el pasto del título, por eso no es global.
/// </summary>
[ExecuteAlways]
public class SombrasCiudad : MonoBehaviour
{
    [Tooltip("Distancia de sombras (u) mientras se ve la ciudad. La del URP (fuera de la ciudad) se restaura al apagarse.")]
    public float distancia = 400f;
    static float? original;

    static UniversalRenderPipelineAsset Asset() => GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;

    void OnEnable()
    {
        var a = Asset(); if (a == null) return;
        if (original == null) original = a.shadowDistance;
        a.shadowDistance = distancia;
    }

    void OnValidate() { if (isActiveAndEnabled) { var a = Asset(); if (a != null) a.shadowDistance = distancia; } }

    void OnDisable()
    {
        var a = Asset(); if (a == null || original == null) return;
        a.shadowDistance = original.Value;
        original = null;
    }
}
