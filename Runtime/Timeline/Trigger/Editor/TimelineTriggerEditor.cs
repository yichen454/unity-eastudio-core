#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Timeline;

namespace EAStudio.Core.Timeline
{
    [CustomEditor(typeof(TimelineTrigger))]
    public class TimelineTriggerEditor : UnityEditor.Editor
    {
        private ReorderableList _clipsList;

        private void OnEnable()
        {
            var clipsProperty = serializedObject.FindProperty("clips");
            _clipsList = new ReorderableList(serializedObject, clipsProperty, true, true, true, true);
            _clipsList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Clips");
            _clipsList.elementHeightCallback = index =>
            {
                var element = clipsProperty.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(element, true) + EditorGUIUtility.standardVerticalSpacing;
            };
            _clipsList.drawElementCallback = (rect, index, active, focused) =>
            {
                var element = clipsProperty.GetArrayElementAtIndex(index);
                var labelProperty = element.FindPropertyRelative("label");

                rect.y += EditorGUIUtility.standardVerticalSpacing;
                rect.height = EditorGUI.GetPropertyHeight(element, true);

                var displayLabel = string.IsNullOrWhiteSpace(labelProperty.stringValue)
                    ? $"Clip {index + 1}"
                    : labelProperty.stringValue;
                EditorGUI.PropertyField(rect, element, new GUIContent(displayLabel), true);
            };
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawPropertiesExcluding(serializedObject, "clips");

            EditorGUILayout.Space();
            _clipsList.DoLayoutList();

            serializedObject.ApplyModifiedProperties();

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
                    Undo.RecordObject(timeline, "刷新 TimelineTrigger Clip 列表");

                    var configById = new Dictionary<string, TimelineTrigger.ClipConfig>();
                    foreach (var config in trigger.clips)
                    {
                        if (config == null || string.IsNullOrWhiteSpace(config.clipId)) continue;
                        if (!configById.ContainsKey(config.clipId))
                            configById.Add(config.clipId, config);
                    }

                    var seenClipIds = new HashSet<string>();
                    var refreshedConfigs = new List<TimelineTrigger.ClipConfig>(clips.Count);

                    for (int i = 0; i < clips.Count; i++)
                    {
                        if (clips[i].asset is not TimelineTriggerClip triggerClip) continue;

                        Undo.RecordObject(triggerClip, "刷新 TimelineTrigger Clip 列表");

                        if (string.IsNullOrWhiteSpace(triggerClip.ClipId) || seenClipIds.Contains(triggerClip.ClipId))
                        {
                            triggerClip.EnsureClipId(true);
                            EditorUtility.SetDirty(triggerClip);
                        }

                        seenClipIds.Add(triggerClip.ClipId);

                        if (!string.IsNullOrWhiteSpace(triggerClip.label))
                        {
                            clips[i].displayName = triggerClip.label;
                        }

                        var config = configById.TryGetValue(triggerClip.ClipId, out var existing)
                            ? existing
                            : new TimelineTrigger.ClipConfig();

                        config.clipId = triggerClip.ClipId;
                        config.label = triggerClip.label;

                        refreshedConfigs.Add(config);
                    }

                    trigger.clips.Clear();
                    trigger.clips.AddRange(refreshedConfigs);
                    trigger.MarkLookupDirty();

                    EditorUtility.SetDirty(trigger);
                    EditorUtility.SetDirty(timeline);
                    Debug.Log($"[TimelineTrigger] '{trigger.name}' 已同步 {clips.Count} 个 clip。");
                    return;
                }
            }

            Debug.LogWarning($"[TimelineTrigger] '{trigger.name}': 未找到绑定此组件的 TimelineTriggerTrack，请确认 PlayableDirector 在场景中。");
        }
    }
}
#endif
