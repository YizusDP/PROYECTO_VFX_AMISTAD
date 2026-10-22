using System.Linq;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

/// <summary>
/// Pista de Timeline para un derrumbe horneado. Antes del clip el edificio está intacto,
/// después del clip se queda caído. La velocidad del clip (Speed Multiplier) hace cámara lenta.
/// </summary>
[TrackColor(0.85f, 0.45f, 0.15f)]
[TrackClipType(typeof(DerrumbeClip))]
[TrackBindingType(typeof(DerrumbeHorneado))]
public class DerrumbeTrack : TrackAsset
{
    public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
    {
        var mezcla = ScriptPlayable<DerrumbeMezcla>.Create(graph, inputCount);
        mezcla.GetBehaviour().clips = GetClips().OrderBy(c => c.start).ToArray();
        return mezcla;
    }

    // Para que al salir del modo Preview del Timeline las piezas regresen a su lugar
    public override void GatherProperties(PlayableDirector director, IPropertyCollector driver)
    {
        var d = director != null ? director.GetGenericBinding(this) as DerrumbeHorneado : null;
        if (d != null && d.piezas != null)
        {
            foreach (var p in d.piezas)
            {
                if (p == null) continue;
                var go = p.gameObject;
                driver.AddFromName<Transform>(go, "m_LocalPosition.x");
                driver.AddFromName<Transform>(go, "m_LocalPosition.y");
                driver.AddFromName<Transform>(go, "m_LocalPosition.z");
                driver.AddFromName<Transform>(go, "m_LocalRotation.x");
                driver.AddFromName<Transform>(go, "m_LocalRotation.y");
                driver.AddFromName<Transform>(go, "m_LocalRotation.z");
                driver.AddFromName<Transform>(go, "m_LocalRotation.w");
            }
        }
        base.GatherProperties(director, driver);
    }
}

public class DerrumbeMezcla : PlayableBehaviour
{
    public TimelineClip[] clips;

    public override void ProcessFrame(Playable playable, FrameData info, object playerData)
    {
        var d = playerData as DerrumbeHorneado;
        if (d == null || clips == null || clips.Length == 0) return;

        var director = playable.GetGraph().GetResolver() as PlayableDirector;
        double t = director != null ? director.time : playable.GetGraph().GetRootPlayable(0).GetTime();

        // El último clip que ya empezó; antes del primero se usa el inicio (edificio intacto)
        TimelineClip c = clips[0];
        foreach (var x in clips) if (x.start <= t) c = x;
        double local = (t - c.start) * c.timeScale + c.clipIn;
        double fin = c.clipIn + c.duration * c.timeScale;
        if (local < c.clipIn) local = c.clipIn;
        if (local > fin) local = fin;
        d.SetTiempo((float)local);
    }
}
