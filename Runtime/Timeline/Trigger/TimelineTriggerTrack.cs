using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EAStudio.Core.Timeline
{
    /// <summary>
    /// Timeline 触发器轨道。将 <see cref="TimelineTrigger"/> 组件绑定到此轨道，
    /// 播放头进入 / 离开 clip 时自动触发对应事件。
    /// </summary>
    [TrackColor(0.9f, 0.5f, 0.1f)]
    [TrackClipType(typeof(TimelineTriggerClip))]
    [TrackBindingType(typeof(TimelineTrigger))]
    public class TimelineTriggerTrack : TrackAsset
    {
        public override Playable CreateTrackMixer(PlayableGraph graph, GameObject go, int inputCount)
        {
#if UNITY_EDITOR
            // 将每个 clip 的 label 同步为 Timeline 窗口中的显示名称。
            foreach (var clip in GetClips())
            {
                if (clip.asset is TimelineTriggerClip triggerClip
                    && !string.IsNullOrWhiteSpace(triggerClip.label))
                    clip.displayName = triggerClip.label;
            }
#endif
            return ScriptPlayable<TimelineTriggerMixerBehaviour>.Create(graph, inputCount);
        }
    }
}
