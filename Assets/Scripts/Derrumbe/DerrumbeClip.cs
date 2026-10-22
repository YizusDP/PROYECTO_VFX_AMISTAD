using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>Clip de la pista Derrumbe. Su duración es la de la simulación horneada.</summary>
public class DerrumbeClip : PlayableAsset, ITimelineClipAsset
{
    [Tooltip("Segundos de simulación (se llena al hornear).")]
    public float duracionHorneada = 10f;

    public ClipCaps clipCaps => ClipCaps.SpeedMultiplier | ClipCaps.ClipIn;
    public override double duration => duracionHorneada;

    public override Playable CreatePlayable(PlayableGraph graph, GameObject owner) => Playable.Create(graph);
}
