using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using EAStudio.Core.RenderFeature.Shadow;

namespace EAStudio.Core.Editor.RenderFeature.Shadow
{
    [CustomEditor(typeof(ShadowSettings))]
    public class ShadowSettingsEditor : VolumeComponentEditor
    {
        SerializedDataParameter m_ShadowDistance;
        SerializedDataParameter m_WorkingUnit;
        SerializedDataParameter m_CascadeCount;
        SerializedDataParameter m_Cascade2Distance;
        SerializedDataParameter m_Cascade3Distances;
        SerializedDataParameter m_Cascade4Distances;
        SerializedDataParameter m_CascadeBorderDistance;
        SerializedDataParameter m_ShadowDepthBias;
        SerializedDataParameter m_ShadowNormalBias;
        private int m_DraggedSplit = -1;
        private readonly float[] m_Splits = new float[5];
        private static readonly Color[] CascadeColors =
        {
            new Color(0.39f, 0.38f, 0.56f), new Color(0.38f, 0.55f, 0.40f),
            new Color(0.56f, 0.55f, 0.39f), new Color(0.55f, 0.39f, 0.39f)
        };

        public override void OnEnable()
        {
            var fetcher = new PropertyFetcher<ShadowSettings>(serializedObject);
            m_WorkingUnit = Unpack(fetcher.Find(settings => settings.workingUnit));
            var o = fetcher;
            m_ShadowDistance = Unpack(o.Find(x => x.shadowDistance));
            m_CascadeCount = Unpack(o.Find(x => x.cascadeCount));
            m_Cascade2Distance = Unpack(o.Find(x => x.cascade2Distance));
            m_Cascade3Distances = Unpack(o.Find(x => x.cascade3Distances));
            m_Cascade4Distances = Unpack(o.Find(x => x.cascade4Distances));
            m_CascadeBorderDistance = Unpack(o.Find(x => x.cascadeBorderDistance));
            m_ShadowDepthBias = Unpack(o.Find(x => x.shadowDepthBias));
            m_ShadowNormalBias = Unpack(o.Find(x => x.shadowNormalBias));
        }

        public override void OnInspectorGUI()
        {
            PropertyField(m_ShadowDistance, new GUIContent("Max Distance", "Maximum shadow distance in meters."));
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Directional Light", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(m_WorkingUnit.value, new GUIContent("Working Unit"));
            PropertyField(m_CascadeCount, new GUIContent("Cascade Count"));

            var pipelineAsset = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
            int cascadeCount = Mathf.Clamp(!m_CascadeCount.overrideState.boolValue && pipelineAsset != null
                ? pipelineAsset.shadowCascadeCount : m_CascadeCount.value.intValue, 1, 4);
            
            EditorGUI.indentLevel++;
            if (cascadeCount == 2)
            {
                DrawDistance(m_Cascade2Distance, "Split 1", -1);
            }
            else if (cascadeCount == 3)
            {
                DrawDistance(m_Cascade3Distances, "Split 1", 0);
                DrawDistance(m_Cascade3Distances, "Split 2", 1);
            }
            else if (cascadeCount == 4)
            {
                DrawDistance(m_Cascade4Distances, "Split 1", 0);
                DrawDistance(m_Cascade4Distances, "Split 2", 1);
                DrawDistance(m_Cascade4Distances, "Split 3", 2);
            }

            DrawDistance(m_CascadeBorderDistance, "Border", -1);
            EditorGUI.indentLevel--;
            EditorGUILayout.Space();
            DrawCascadeSplits(cascadeCount);
            
            EditorGUILayout.Space();
            PropertyField(m_ShadowDepthBias);
            PropertyField(m_ShadowNormalBias);
        }

        private void DrawDistance(SerializedDataParameter parameter, string label, int component)
        {
            bool percent = m_WorkingUnit.value.intValue == (int)ShadowWorkingUnit.Percent;
            float maxDistance = GetMaxDistance();
            SerializedProperty value = component < 0 ? parameter.value
                : parameter.value.FindPropertyRelative(component == 0 ? "x" : component == 1 ? "y" : "z");
            float scale = percent && maxDistance > 0f ? 100f / maxDistance : 1f;
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawOverrideCheckbox(parameter);
                using (new EditorGUI.DisabledScope(!parameter.overrideState.boolValue || maxDistance <= 0f))
                {
                    EditorGUI.BeginProperty(GUILayoutUtility.GetLastRect(), GUIContent.none, value);
                    EditorGUI.showMixedValue = value.hasMultipleDifferentValues;
                    EditorGUI.BeginChangeCheck();
                    float distance = EditorGUILayout.Slider(new GUIContent(label + (percent ? " (%)" : " (m)"),
                        component < 0 ? "Distance relative to Max Distance." : "Cumulative split distance. Vector splits share one override state."),
                        value.floatValue * scale, 0f, percent ? 100f : maxDistance);
                    if (EditorGUI.EndChangeCheck())
                    {
                        float minimum = component > 0 ? parameter.value.FindPropertyRelative(component == 1 ? "x" : "y").floatValue : 0f;
                        float maximum = maxDistance;
                        if ((component == 0 && parameter == m_Cascade3Distances)
                            || (component >= 0 && component < 2 && parameter == m_Cascade4Distances))
                            maximum = Mathf.Min(maximum, parameter.value.FindPropertyRelative(component == 0 ? "y" : "z").floatValue);
                        value.floatValue = Mathf.Clamp(distance / scale, Mathf.Clamp(minimum, 0f, maxDistance), Mathf.Clamp(maximum, Mathf.Clamp(minimum, 0f, maxDistance), maxDistance));
                    }
                    EditorGUI.showMixedValue = false;
                    EditorGUI.EndProperty();
                }
            }
        }

        private float GetMaxDistance()
        {
            var asset = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
            return Mathf.Max(0f, !m_ShadowDistance.overrideState.boolValue && asset != null
                ? asset.shadowDistance : m_ShadowDistance.value.floatValue);
        }

        private SerializedProperty GetSplitValue(SerializedDataParameter parameter, int split)
        {
            return parameter == m_Cascade2Distance ? parameter.value
                : parameter.value.FindPropertyRelative(split == 0 ? "x" : split == 1 ? "y" : "z");
        }

        private void DrawCascadeSplits(int cascadeCount)
        {
            EditorGUILayout.LabelField("Cascade splits");
            Rect area = GUILayoutUtility.GetRect(1f, 86f, GUILayout.ExpandWidth(true));
            Rect bar = new Rect(area.x + 5f, area.y + 17f, Mathf.Max(1f, area.width - 10f), 52f);
            float maxDistance = GetMaxDistance();
            var parameter = cascadeCount == 2 ? m_Cascade2Distance : cascadeCount == 3 ? m_Cascade3Distances : m_Cascade4Distances;
            bool mixed = m_CascadeCount.value.hasMultipleDifferentValues || m_ShadowDistance.value.hasMultipleDifferentValues
                || parameter.value.hasMultipleDifferentValues;
            bool editable = GUI.enabled && cascadeCount > 1 && maxDistance > 0f && !mixed
                && parameter.overrideState.boolValue && !parameter.overrideState.hasMultipleDifferentValues;
            m_Splits[0] = 0f;
            var asset = UnityEngine.Rendering.Universal.UniversalRenderPipeline.asset;
            for (int split = 0; split < cascadeCount - 1; split++)
            {
                float distance = GetSplitValue(parameter, split).floatValue;
                if (!parameter.overrideState.boolValue && asset != null)
                    distance = maxDistance * (cascadeCount == 2 ? asset.cascade2Split
                        : cascadeCount == 3 ? asset.cascade3Split[split] : asset.cascade4Split[split]);
                m_Splits[split + 1] = Mathf.Clamp(distance, m_Splits[split], maxDistance);
            }
            m_Splits[cascadeCount] = maxDistance;

            int controlId = GUIUtility.GetControlID(FocusType.Passive, area);
            Event current = Event.current;
            if (current.GetTypeForControl(controlId) == EventType.MouseDown && current.button == 0 && editable)
            {
                for (int split = 1; split < cascadeCount; split++)
                {
                    float position = bar.x + bar.width * m_Splits[split] / maxDistance;
                    if (new Rect(position - 6f, area.y, 12f, area.height).Contains(current.mousePosition))
                    {
                        m_DraggedSplit = split;
                        GUIUtility.hotControl = controlId;
                        Undo.IncrementCurrentGroup();
                        Undo.SetCurrentGroupName("Adjust Shadow Cascade Split");
                        current.Use();
                        break;
                    }
                }
            }
            if (GUIUtility.hotControl == controlId)
            {
                if (current.rawType == EventType.MouseUp || current.type == EventType.Ignore || !editable)
                {
                    GUIUtility.hotControl = 0;
                    m_DraggedSplit = -1;
                    if (current.type == EventType.MouseUp)
                        current.Use();
                }
                else if (current.type == EventType.MouseDrag && m_DraggedSplit > 0 && m_DraggedSplit < cascadeCount)
                {
                    float distance = (current.mousePosition.x - bar.x) / bar.width * maxDistance;
                    distance = Mathf.Clamp(distance, m_Splits[m_DraggedSplit - 1], m_Splits[m_DraggedSplit + 1]);
                    GetSplitValue(parameter, m_DraggedSplit - 1).floatValue = distance;
                    m_Splits[m_DraggedSplit] = distance;
                    GUI.changed = true;
                    current.Use();
                    Repaint();
                }
            }

            if (current.type != EventType.Repaint)
                return;
            EditorGUI.DrawRect(new Rect(bar.x - 1f, bar.y - 1f, bar.width + 2f, bar.height + 2f), Color.black);
            var labelStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleCenter, clipping = TextClipping.Clip };
            labelStyle.normal.textColor = Color.black;
            bool percent = m_WorkingUnit.value.intValue == (int)ShadowWorkingUnit.Percent;
            for (int cascade = 0; cascade < cascadeCount; cascade++)
            {
                float start = maxDistance > 0f ? m_Splits[cascade] / maxDistance : (float)cascade / cascadeCount;
                float end = maxDistance > 0f ? m_Splits[cascade + 1] / maxDistance : (float)(cascade + 1) / cascadeCount;
                Rect segment = new Rect(bar.x + start * bar.width, bar.y, (end - start) * bar.width, bar.height);
                EditorGUI.DrawRect(segment, CascadeColors[cascade]);
                float length = m_Splits[cascade + 1] - m_Splits[cascade];
                string text = mixed ? "\u2014" : percent ? (maxDistance > 0f ? length / maxDistance * 100f : 0f).ToString("0.0") + "%" : length.ToString("0.0") + "m";
                if (segment.width > 28f)
                    GUI.Label(segment, cascade + "\n" + text, labelStyle);
                if (cascade > 0)
                {
                    EditorGUI.DrawRect(new Rect(segment.x, bar.y, 1f, bar.height), Color.black);
                    Color handleColor = editable ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.35f, 0.35f, 0.35f);
                    Color previousColor = Handles.color;
                    Handles.color = handleColor;
                    Handles.DrawAAConvexPolygon(
                        new Vector3(segment.x - 5f, area.y + 2f), new Vector3(segment.x + 5f, area.y + 2f),
                        new Vector3(segment.x + 5f, area.y + 10f), new Vector3(segment.x, area.y + 16f),
                        new Vector3(segment.x - 5f, area.y + 10f));
                    Handles.DrawAAConvexPolygon(
                        new Vector3(segment.x, bar.yMax + 1f), new Vector3(segment.x + 5f, bar.yMax + 7f),
                        new Vector3(segment.x + 5f, bar.yMax + 15f), new Vector3(segment.x - 5f, bar.yMax + 15f),
                        new Vector3(segment.x - 5f, bar.yMax + 7f));
                    Handles.color = previousColor;
                    if (editable)
                        EditorGUIUtility.AddCursorRect(new Rect(segment.x - 6f, area.y, 12f, area.height), MouseCursor.ResizeHorizontal);
                }
            }
        }
    }
}
