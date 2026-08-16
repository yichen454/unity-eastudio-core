using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EA.Timeline
{
    /// <summary>
    /// <see cref="TimelineTriggerTrack"/> 上的单个 clip。
    /// 事件均在绑定的 <see cref="TimelineTrigger"/> 中按顺序配置，支持场景对象拖拽。
    /// </summary>
    public class TimelineTriggerClip : PlayableAsset, ITimelineClipAsset
    {
        [Tooltip("显示在 Timeline clip 上的标签，便于区分。同步到 TimelineTrigger.clips[i].label。")]
        public string label = string.Empty;

        public ClipCaps clipCaps => ClipCaps.Blending;

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            return ScriptPlayable<TimelineTriggerBehaviour>.Create(graph);
        }
    }
}
