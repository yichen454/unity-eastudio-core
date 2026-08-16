using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    public class ResourceOrganizerWindow : EditorWindow
    {
        [Header("Source Scene Objects")]
        public List<GameObject> sourceObjects = new List<GameObject>();

        [Header("Output Root")]
        public DefaultAsset outputRootFolder;

        [Header("Mode")]
        public bool dryRun = true;

        private readonly HashSet<string> processedRenderers = new HashSet<string>();
        private readonly HashSet<string> movedAssets = new HashSet<string>();

        private Vector2 scroll;

        [MenuItem("Tools/EAStudio/资产/场景资源归类整理")]
        public static void Open()
        {
            GetWindow<ResourceOrganizerWindow>("场景资源归类整理");
        }

        private void OnGUI()
        {
            outputRootFolder = (DefaultAsset)EditorGUILayout.ObjectField(
                "Output Root",
                outputRootFolder,
                typeof(DefaultAsset),
                false);

            dryRun = EditorGUILayout.Toggle("Dry Run", dryRun);

            DrawDragArea();
            DrawList();

            if (GUILayout.Button("Execute"))
                Execute();
        }

        private void DrawDragArea()
        {
            Rect r = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(r, "Drag Scene GameObjects Here");

            Event e = Event.current;

            if (r.Contains(e.mousePosition))
            {
                if (e.type == EventType.DragPerform || e.type == EventType.DragUpdated)
                {
                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (e.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (var obj in DragAndDrop.objectReferences)
                        {
                            if (obj is GameObject go && !sourceObjects.Contains(go))
                                sourceObjects.Add(go);
                        }
                    }

                    Event.current.Use();
                }
            }
        }

        private void DrawList()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.Height(150));

            for (int i = 0; i < sourceObjects.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                sourceObjects[i] = (GameObject)EditorGUILayout.ObjectField(
                    sourceObjects[i], typeof(GameObject), true);

                if (GUILayout.Button("X", GUILayout.Width(20)))
                {
                    sourceObjects.RemoveAt(i);
                    i--;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }

        private void Execute()
        {
            processedRenderers.Clear();
            movedAssets.Clear();

            string root = AssetDatabase.GetAssetPath(outputRootFolder);

            foreach (var go in sourceObjects)
            {
                if (go == null) continue;

                string goFolder = $"{root}/{Sanitize(go.name)}";
                EnsureFolder(goFolder);

                ProcessGameObject(go, goFolder);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(dryRun ? "DryRun 完成" : "整理完成");
        }

        private void ProcessGameObject(GameObject go, string goFolder)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(true);

            foreach (var r in renderers)
            {
                if (r == null) continue;

                string rendererKey = r.gameObject.GetInstanceID().ToString();
                if (processedRenderers.Contains(rendererKey))
                    continue;

                processedRenderers.Add(rendererKey);

                string modelName = GetModelName(r);
                string modelFolder = $"{goFolder}/{modelName}";

                EnsureFolder(modelFolder);

                CollectRenderer(r, modelFolder);
            }
        }

        private string GetModelName(Renderer r)
        {
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(r.gameObject);
            if (prefab != null)
                return prefab.name;

            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
                return mf.sharedMesh.name;

            return r.gameObject.name;
        }

        private void CollectRenderer(Renderer r, string modelFolder)
        {
            foreach (var mat in r.sharedMaterials)
            {
                if (mat == null) continue;

                MoveAsset(mat, $"{modelFolder}/Materials");
                CollectTextures(mat, $"{modelFolder}/Textures");
            }

            var meshFilter = r.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                MoveAsset(meshFilter.sharedMesh, $"{modelFolder}/Meshes");
            }
        }

        private void CollectTextures(Material mat, string targetFolder)
        {
            var shader = mat.shader;
            int count = ShaderUtil.GetPropertyCount(shader);

            for (int i = 0; i < count; i++)
            {
                if (ShaderUtil.GetPropertyType(shader, i) == ShaderUtil.ShaderPropertyType.TexEnv)
                {
                    string propName = ShaderUtil.GetPropertyName(shader, i);
                    var tex = mat.GetTexture(propName);

                    if (tex != null)
                    {
                        MoveAsset(tex, targetFolder);
                    }
                }
            }
        }

        private void MoveAsset(UnityEngine.Object asset, string targetFolder)
        {
            string path = AssetDatabase.GetAssetPath(asset);

            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets"))
                return;

            if (movedAssets.Contains(path))
                return;

            EnsureFolder(targetFolder);

            string fileName = Path.GetFileName(path);
            string newPath = $"{targetFolder}/{fileName}";

            if (path == newPath)
                return;

            if (!dryRun)
            {
                string error = AssetDatabase.MoveAsset(path, newPath);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"Move failed: {path} -> {newPath}\n{error}");
                    return;
                }
            }

            movedAssets.Add(path);
            Debug.Log($"[Move] {path} -> {newPath}");
        }

        private void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = Path.GetDirectoryName(path).Replace("\\", "/");
            string folder = Path.GetFileName(path);

            if (!AssetDatabase.IsValidFolder(parent))
                EnsureFolder(parent);

            if (!dryRun)
                AssetDatabase.CreateFolder(parent, folder);
        }

        private string Sanitize(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');

            return name.Replace(" ", "_");
        }
    }
}

