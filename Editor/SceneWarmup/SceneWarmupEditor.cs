using EAStudio.Core.SceneWarmup;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    /// <summary>
    /// Custom Inspector for <see cref="SceneWarmup"/> that edits only <c>scenePath</c>
    /// while supporting drag-and-drop or picking of a <see cref="SceneAsset"/> to quickly fill the path.
    /// </summary>
    [CustomEditor(typeof(EAStudio.Core.SceneWarmup.SceneWarmup))]
    [CanEditMultipleObjects]
    public class SceneWarmupEditor : UnityEditor.Editor
    {
        private SerializedProperty _scenePathProp;

        private void OnEnable()
        {
            _scenePathProp = serializedObject.FindProperty("scenePath");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawScenePathField(_scenePathProp, new GUIContent("Scene Path", _scenePathProp.tooltip));

            DrawPropertiesExcluding(serializedObject, "m_Script", "scenePath");

            serializedObject.ApplyModifiedProperties();
        }

        internal static void DrawScenePathField(SerializedProperty scenePathProp, GUIContent label)
        {
            Rect totalRect = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);
            DrawScenePathField(totalRect, scenePathProp, label);
        }

        internal static void DrawScenePathField(Rect totalRect, SerializedProperty scenePathProp, GUIContent label)
        {
            HandleSceneDragAndDrop(totalRect, scenePathProp);

            EditorGUI.BeginProperty(totalRect, label, scenePathProp);

            Rect contentRect = EditorGUI.PrefixLabel(totalRect, label);
            const float dropFieldWidth = 88f;
            const float spacing = 4f;

            float pathWidth = Mathf.Max(40f, contentRect.width - dropFieldWidth - spacing);
            Rect pathRect = new Rect(contentRect.x, contentRect.y, pathWidth, contentRect.height);
            Rect dropRect = new Rect(pathRect.xMax + spacing, contentRect.y, dropFieldWidth, contentRect.height);

            EditorGUI.BeginChangeCheck();
            string newPath = EditorGUI.TextField(pathRect, scenePathProp.stringValue);
            if (EditorGUI.EndChangeCheck())
            {
                scenePathProp.stringValue = newPath;
            }

            // Always pass null so no SceneAsset reference is retained; selecting or dropping fills scenePath only.
            EditorGUI.BeginChangeCheck();
            var pickedScene = EditorGUI.ObjectField(dropRect, GUIContent.none, null, typeof(SceneAsset), false) as SceneAsset;
            if (EditorGUI.EndChangeCheck() && pickedScene != null)
            {
                string assetPath = AssetDatabase.GetAssetPath(pickedScene);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    GUIUtility.keyboardControl = 0;
                    scenePathProp.stringValue = assetPath;
                }
            }

            EditorGUI.EndProperty();
        }

        private static void HandleSceneDragAndDrop(Rect dropArea, SerializedProperty scenePathProp)
        {
            Event evt = Event.current;
            if ((evt.type != EventType.DragUpdated && evt.type != EventType.DragPerform)
                || !dropArea.Contains(evt.mousePosition))
            {
                return;
            }

            if (!TryGetDraggedScenePath(out string draggedScenePath))
            {
                return;
            }

            DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

            if (evt.type == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                GUIUtility.keyboardControl = 0;
                scenePathProp.stringValue = draggedScenePath;
                GUI.changed = true;
            }

            evt.Use();
        }

        private static bool TryGetDraggedScenePath(out string scenePath)
        {
            var objectRefs = DragAndDrop.objectReferences;
            if (objectRefs != null)
            {
                for (int i = 0; i < objectRefs.Length; i++)
                {
                    if (objectRefs[i] is SceneAsset sceneAsset)
                    {
                        string path = AssetDatabase.GetAssetPath(sceneAsset);
                        if (!string.IsNullOrEmpty(path))
                        {
                            scenePath = path;
                            return true;
                        }
                    }
                }
            }

            var paths = DragAndDrop.paths;
            if (paths != null)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    if (!string.IsNullOrEmpty(paths[i]) && paths[i].EndsWith(".unity", System.StringComparison.OrdinalIgnoreCase))
                    {
                        scenePath = paths[i];
                        return true;
                    }
                }
            }

            scenePath = null;
            return false;
        }
    }

    [CustomPropertyDrawer(typeof(SceneReference))]
    public class SceneReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty scenePathProp = property.FindPropertyRelative("scenePath");
            if (scenePathProp != null)
            {
                SceneWarmupEditor.DrawScenePathField(position, scenePathProp, label);
            }
            else
            {
                EditorGUI.PropertyField(position, property, label, true);
            }
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }
    }
}