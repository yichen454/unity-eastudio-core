using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EAStudio.Core.SceneWarmup
{
    /// <summary>
    /// Timeline track that controls scene loading via a bound <see cref="SceneWarmup"/>.
    /// Place clips on this track to define the time windows during which the scene is loaded.
    /// </summary>
    [TrackColor(0.2f, 0.6f, 0.9f)]
    [TrackClipType(typeof(SceneWarmupClip))]
    [TrackBindingType(typeof(SceneWarmup))]
    public class SceneWarmupTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
#if UNITY_EDITOR
            var director = go.GetComponent<PlayableDirector>();
            if (director != null && director.GetGenericBinding(this) is SceneWarmup warmup
                && !string.IsNullOrEmpty(warmup.SceneName))
            {
                foreach (var clip in GetClips())
                    clip.displayName = warmup.SceneName;
            }
#endif
            return ScriptPlayable<SceneWarmupMixerBehaviour>.Create(graph, inputCount);
        }
    }
}