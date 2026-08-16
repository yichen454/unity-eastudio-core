#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EAStudio.Core.Timeline
{
    [CustomEditor(typeof(TimelineTrigger))]
    public class TimelineTriggerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            EditorGUILayout.Space();
            if (GUILayout.Button("刷新 Clip 列表"))
                RefreshClips((TimelineTrigger)target);
        }

        private static void RefreshClips(TimelineTrigger trigger)
        {
            // 在场景中查找绑定了此 TimelineTrigger 的 PlayableDirector。
            var directors = Object.FindObjectsByType<PlayableDirector>(FindObjectsSortMode.None);
            foreach (var director in directors)
            {
                if (director.playableAsset is not TimelineAsset timeline) continue;

                foreach (var track in timeline.GetOutputTracks())
                {
                    if (track is not TimelineTriggerTrack triggerTrack) continue;
                    if (director.GetGenericBinding(track) as TimelineTrigger != trigger) continue;

                    // 按开始时间排序 clip。
                    var clips = new List<TimelineClip>(triggerTrack.GetClips());
                    clips.Sort((a, b) => a.start.CompareTo(b.start));

                    Undo.RecordObject(trigger, "刷新 TimelineTrigger Clip 列表");

                    // 补足不够的条目。
                    while (trigger.clips.Count < clips.Count)
                        trigger.clips.Add(new TimelineTrigger.ClipConfig());

                    // 移除多余的条目。
                    while (trigger.clips.Count > clips.Count)
                        trigger.clips.RemoveAt(trigger.clips.Count - 1);

                    // 将 clip 上的 label 同步到 ClipConfig.label（仅在为空时补填）。
                    for (int i = 0; i < clips.Count; i++)
                    {
                        if (clips[i].asset is TimelineTriggerClip triggerClip
                            && !string.IsNullOrWhiteSpace(triggerClip.label)
                            && string.IsNullOrWhiteSpace(trigger.clips[i].label))
                        {
                            trigger.clips[i].label = triggerClip.label;
                        }
                    }

                    EditorUtility.SetDirty(trigger);
                    Debug.Log($"[TimelineTrigger] '{trigger.name}' 已同步 {clips.Count} 个 clip。");
                    return;
                }
            }

            Debug.LogWarning($"[TimelineTrigger] '{trigger.name}': 未找到绑定此组件的 TimelineTriggerTrack，请确认 PlayableDirector 在场景中。");
        }
    }
}
#endif
