using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EA.Timeline
{
    /// <summary>
    /// A clip on the <see cref="SceneWarmupTrack"/>.
    /// Each clip represents a time window during which the target scene should be loaded.
    /// </summary>
    public class SceneWarmupClip : PlayableAsset, ITimelineClipAsset
    {
        public ClipCaps clipCaps => ClipCaps.None;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<SceneWarmupBehaviour>.Create(graph);
        }
    }
}
