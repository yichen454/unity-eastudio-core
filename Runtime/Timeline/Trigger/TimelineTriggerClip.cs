using System;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EAStudio.Core.Timeline
{
    /// <summary>
    /// <see cref="TimelineTriggerTrack"/> 上的单个 clip。
    /// 事件均在绑定的 <see cref="TimelineTrigger"/> 中按顺序配置，支持场景对象拖拽。
    /// </summary>
    public class TimelineTriggerClip : PlayableAsset, ITimelineClipAsset
    {
        [SerializeField, HideInInspector]
        private string clipId;

        [Tooltip("显示在 Timeline clip 上的标签，便于区分。同步到 TimelineTrigger.clips[i].label。")]
        public string label = string.Empty;

        public string ClipId => clipId;

        public ClipCaps clipCaps => ClipCaps.Blending;

        private void OnValidate()
        {
            EnsureClipId();
        }

        public void EnsureClipId(bool forceNew = false)
        {
            if (!forceNew && !string.IsNullOrWhiteSpace(clipId)) return;

            clipId = Guid.NewGuid().ToString("N");
        }

        public override Playable CreatePlayable(PlayableGraph graph, GameObject owner)
        {
            EnsureClipId();

            var playable = ScriptPlayable<TimelineTriggerBehaviour>.Create(graph);
            var behaviour = playable.GetBehaviour();
            behaviour.clipId = clipId;
            behaviour.label = label;
            return playable;
        }
    }
}
