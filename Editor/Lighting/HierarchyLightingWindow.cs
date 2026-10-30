using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace EAStudio.Core.Editor
{
    public sealed class HierarchyLightingWindow : EditorWindow
    {
        [SerializeField] private GameObject root;
        [SerializeField] private bool includeRoot = true;
        [SerializeField] private bool includeInactive = true;
        [SerializeField] private bool editCastShadows;
        [SerializeField] private ShadowCastingMode castShadows = ShadowCastingMode.On;
        [SerializeField] private bool editStaticShadowCaster;
        [SerializeField] private bool staticShadowCaster;
        [SerializeField] private bool editLightProbes;
        [SerializeField] private LightProbeUsage lightProbes = LightProbeUsage.BlendProbes;
        [SerializeField] private bool editContributeGI;
        [SerializeField] private bool contributeGI;
        [SerializeField] private bool editReceiveGI;
        [SerializeField] private ReceiveGI receiveGI = ReceiveGI.Lightmaps;
        [SerializeField] private bool editScaleInLightmap;
        [SerializeField] private float scaleInLightmap = 1f;
        private string result;

        [MenuItem("Tools/EAStudio/光照/层级光照批量设置")]
        public static void Open()
        {
            var window = GetWindow<HierarchyLightingWindow>("层级光照批量设置");
            window.minSize = new Vector2(420f, 410f);
            window.root = Selection.activeGameObject;
        }

        private void OnGUI()
        {
            root = (GameObject)EditorGUILayout.ObjectField("根物体", root, typeof(GameObject), true);
            if (GUILayout.Button("使用当前选中物体")) root = Selection.activeGameObject;
            includeRoot = EditorGUILayout.Toggle("包含根物体", includeRoot);
            includeInactive = EditorGUILayout.Toggle("包含未激活物体", includeInactive);
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox("仅勾选需要修改的属性，其他属性与静态标记保持不变。支持场景物体及预制体编辑模式，不直接修改预制体资源。", MessageType.Info);
            DrawOption("投射阴影", ref editCastShadows,
                () => castShadows = (ShadowCastingMode)EditorGUILayout.Popup((int)castShadows, new[] { "关闭", "开启", "双面", "仅阴影" }));
            DrawOption("静态阴影投射物", ref editStaticShadowCaster,
                () => staticShadowCaster = EditorGUILayout.Toggle(staticShadowCaster));
            DrawOption("光照探针", ref editLightProbes,
                () => lightProbes = (LightProbeUsage)EditorGUILayout.IntPopup((int)lightProbes, new[] { "关闭", "混合探针", "使用代理体积", "自定义提供" }, new[] { 0, 1, 2, 4 }));
            DrawOption("贡献全局光照", ref editContributeGI,
                () => contributeGI = EditorGUILayout.Toggle(contributeGI));
            DrawOption("接收全局光照", ref editReceiveGI,
                () => receiveGI = (ReceiveGI)EditorGUILayout.IntPopup((int)receiveGI, new[] { "光照贴图", "光照探针" }, new[] { 1, 2 }));
            DrawOption("光照贴图缩放", ref editScaleInLightmap,
                () => scaleInLightmap = EditorGUILayout.FloatField(scaleInLightmap));
            EditorGUILayout.HelpBox("接收全局光照 and 光照贴图缩放 apply to MeshRenderers only. Lightmaps require 贡献全局光照 and suitable lightmap UVs. Changes do not bake or clear existing lightmaps.", MessageType.None);

            bool validRoot = root != null && !EditorUtility.IsPersistent(root) && root.scene.IsValid();
            bool hasChanges = editCastShadows || editStaticShadowCaster || editLightProbes ||
                              editContributeGI || editReceiveGI || editScaleInLightmap;
            bool validScale = !editScaleInLightmap ||
                              (!float.IsNaN(scaleInLightmap) && !float.IsInfinity(scaleInLightmap) && scaleInLightmap >= 0f);
            using (new EditorGUI.DisabledScope(!validRoot || !hasChanges || !validScale || EditorApplication.isPlayingOrWillChangePlaymode))
            {
                if (GUILayout.Button("应用到物体层级")) Apply();
            }
            if (!validScale) EditorGUILayout.HelpBox("光照贴图缩放 must be finite and non-negative.", MessageType.Error);
            if (!string.IsNullOrEmpty(result)) EditorGUILayout.HelpBox(result, MessageType.Info);
        }

        private static void DrawOption(string label, ref bool enabled, Action drawValue)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                enabled = EditorGUILayout.ToggleLeft(label, enabled, GUILayout.Width(180f));
                using (new EditorGUI.DisabledScope(!enabled)) drawValue();
            }
        }

        private void Apply()
        {
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("批量设置层级光照");
            int objectCount = 0;
            int rendererCount = 0;
            try
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(includeInactive))
                {
                    if (!includeRoot && child == root.transform) continue;
                    GameObject target = child.gameObject;
                    if (editContributeGI)
                    {
                        var flags = GameObjectUtility.GetStaticEditorFlags(target);
                        var updated = contributeGI ? flags | StaticEditorFlags.ContributeGI : flags & ~StaticEditorFlags.ContributeGI;
                        if (flags != updated)
                        {
                            Undo.RecordObject(target, "修改全局光照贡献");
                            GameObjectUtility.SetStaticEditorFlags(target, updated);
                            RecordChange(target, target);
                            objectCount++;
                        }
                    }

                    foreach (Renderer renderer in target.GetComponents<Renderer>())
                    {
                        bool changed = (editCastShadows && renderer.shadowCastingMode != castShadows) ||
                                       (editStaticShadowCaster && renderer.staticShadowCaster != staticShadowCaster) ||
                                       (editLightProbes && renderer.lightProbeUsage != lightProbes);
                        var serialized = new SerializedObject(renderer);
                        var receive = renderer is MeshRenderer ? serialized.FindProperty("m_ReceiveGI") : null;
                        var scale = renderer is MeshRenderer ? serialized.FindProperty("m_ScaleInLightmap") : null;
                        bool changeReceive = editReceiveGI && receive != null && receive.intValue != (int)receiveGI;
                        bool changeScale = editScaleInLightmap && scale != null && scale.floatValue != scaleInLightmap;
                        if (!changed && !changeReceive && !changeScale) continue;

                        Undo.RecordObject(renderer, "修改渲染器光照");
                        if (editCastShadows) renderer.shadowCastingMode = castShadows;
                        if (editStaticShadowCaster) renderer.staticShadowCaster = staticShadowCaster;
                        if (editLightProbes) renderer.lightProbeUsage = lightProbes;
                        serialized.Update();
                        if (changeReceive) receive.intValue = (int)receiveGI;
                        if (changeScale) scale.floatValue = scaleInLightmap;
                        serialized.ApplyModifiedProperties();
                        RecordChange(renderer, target);
                        rendererCount++;
                    }
                }
                result = $"已修改 {objectCount} 个物体的静态标记和 {rendererCount} 个渲染器。可通过撤销恢复。";
            }
            finally
            {
                Undo.CollapseUndoOperations(group);
            }
        }

        private static void RecordChange(UnityEngine.Object changed, GameObject owner)
        {
            if (PrefabUtility.IsPartOfPrefabInstance(changed))
                PrefabUtility.RecordPrefabInstancePropertyModifications(changed);
            EditorUtility.SetDirty(changed);
            EditorSceneManager.MarkSceneDirty(owner.scene);
        }
    }
}
