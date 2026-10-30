using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;

namespace EAStudio.Core.Editor
{
    public sealed class LightmapAnalyzerWindow : EditorWindow
    {
        private enum Scope { ActiveScene, LoadedScenes, Hierarchy }
        private enum Filter { All, Issues, Bound, Unbound, ContributeGI, LightProbes }
        [SerializeField] private Scope scope;
        [SerializeField] private GameObject root;
        [SerializeField] private bool includeInactive = true;
        [SerializeField] private Filter filter;
        [SerializeField] private string search = "";
        [SerializeField] private int atlasFilter = -1;
        private List<LightmapAnalyzer.Entry> entries;
        private LightmapData[] maps;
        private Vector2 scroll;
        private int tab;
        private bool stale;
        private string scannedScope;
        private int page;
        private const int PageSize = 100;

        public static void Open()
        {
            var window = GetWindow<LightmapAnalyzerWindow>("光照贴图分析");
            window.minSize = new Vector2(760f, 480f);
        }

        private void OnEnable()
        {
            EditorApplication.hierarchyChanged += Invalidate;
            EditorApplication.projectChanged += Invalidate;
            Undo.undoRedoPerformed += Invalidate;
        }

        private void OnDisable()
        {
            EditorApplication.hierarchyChanged -= Invalidate;
            EditorApplication.projectChanged -= Invalidate;
            Undo.undoRedoPerformed -= Invalidate;
        }

        private void Invalidate() { stale = true; Repaint(); }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();
            scope = (Scope)EditorGUILayout.Popup("扫描范围", (int)scope, new[] { "当前场景", "所有已加载场景", "指定物体层级" });
            if (scope == Scope.Hierarchy)
                root = (GameObject)EditorGUILayout.ObjectField("根物体", root, typeof(GameObject), true);
            includeInactive = EditorGUILayout.Toggle("包含未激活物体", includeInactive);
            if (EditorGUI.EndChangeCheck()) stale = true;
            bool valid = scope != Scope.Hierarchy || (root && root.scene.IsValid() && !EditorUtility.IsPersistent(root));
            using (new EditorGUI.DisabledScope(!valid || EditorApplication.isCompiling))
                if (GUILayout.Button("扫描 / 刷新")) Scan();
            EditorGUILayout.HelpBox("只读分析 MeshRenderer，包含已禁用的渲染器，可选择包含未激活物体。不包含地形与蒙皮网格。图集数据来自所有已加载场景，引用数仅统计扫描范围。烘焙或修改属性后请刷新。", MessageType.Info);
            if (entries == null) return;
            if (stale) EditorGUILayout.HelpBox("场景、设置或资源可能已变化，请刷新分析结果。", MessageType.Warning);
            int bound = entries.Count(e => e.Bound);
            EditorGUILayout.LabelField($"{scannedScope} | 网格渲染器：{entries.Count} | 已绑定：{bound} | 未绑定：{entries.Count - bound} | 待检查：{entries.Count(e => e.Issues.Length > 0)}", EditorStyles.boldLabel);
            tab = GUILayout.Toolbar(tab, new[] { "物体列表", "光照图集" });
            scroll = EditorGUILayout.BeginScrollView(scroll);
            if (tab == 0) DrawObjects(); else DrawAtlases();
            EditorGUILayout.EndScrollView();
        }

        private void Scan()
        {
            var roots = new List<GameObject>();
            if (scope == Scope.Hierarchy) roots.Add(root);
            else if (scope == Scope.ActiveScene) roots.AddRange(SceneManager.GetActiveScene().GetRootGameObjects());
            else
                for (int i = 0; i < SceneManager.sceneCount; i++)
                {
                    var scene = SceneManager.GetSceneAt(i);
                    if (scene.isLoaded) roots.AddRange(scene.GetRootGameObjects());
                }
            maps = (LightmapData[])LightmapSettings.lightmaps.Clone();
            entries = LightmapAnalyzer.Scan(roots, includeInactive, maps);
            scannedScope = scope == Scope.Hierarchy ? root.name : (scope == Scope.ActiveScene ? "当前场景" : "所有已加载场景");
            stale = false;
        }

        private void DrawObjects()
        {
            EditorGUI.BeginChangeCheck();
            search = EditorGUILayout.TextField("搜索物体路径", search);
            filter = (Filter)EditorGUILayout.Popup("状态筛选", (int)filter, new[] { "全部", "待检查", "已绑定", "未绑定", "贡献全局光照", "光照探针" });
            atlasFilter = EditorGUILayout.IntField("图集索引（-1 为全部）", atlasFilter);
            if (EditorGUI.EndChangeCheck()) page = 0;
            EditorGUILayout.HelpBox("索引 0 为有效图集；-1 / 65535 表示无图集，65534 为 Unity 的特殊非图集值。配置为光照贴图不代表已完成烘焙；光照探针筛选仅表示接收方式，不验证探针覆盖。", MessageType.None);
            var matches = entries.Where(Matches).ToList();
            int pages = Mathf.Max(1, Mathf.CeilToInt(matches.Count / (float)PageSize));
            page = Mathf.Clamp(page, 0, pages - 1);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(page == 0))
                    if (GUILayout.Button("上一页", GUILayout.Width(80))) page--;
                EditorGUILayout.LabelField($"第 {page + 1} / {pages} 页 | 匹配：{matches.Count} / {entries.Count}");
                using (new EditorGUI.DisabledScope(page == pages - 1))
                    if (GUILayout.Button("下一页", GUILayout.Width(80))) page++;
            }
            foreach (var entry in matches.Skip(page * PageSize).Take(PageSize))
            {
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(entry.Path, EditorStyles.boldLabel);
                        using (new EditorGUI.DisabledScope(!entry.Renderer))
                        {
                            if (GUILayout.Button("选择", GUILayout.Width(58)))
                            {
                                Selection.activeGameObject = entry.Renderer.gameObject;
                                EditorGUIUtility.PingObject(entry.Renderer.gameObject);
                            }
                            if (GUILayout.Button("聚焦", GUILayout.Width(58)))
                            {
                                Selection.activeGameObject = entry.Renderer.gameObject;
                                SceneView.lastActiveSceneView?.FrameSelected();
                            }
                        }
                    }
                    EditorGUILayout.LabelField($"{(entry.Bound ? "已绑定" : "未绑定")} | 索引：{entry.Index} | 贡献全局光照：{(entry.ContributeGI ? "是" : "否")} | 接收方式：{(entry.ReceiveGI == ReceiveGI.Lightmaps ? "光照贴图" : "光照探针")} | 缩放：{entry.Scale:G4}");
                    EditorGUILayout.LabelField($"UV 缩放 / 偏移： {entry.ScaleOffset.ToString("G4")}");
                    if (!entry.Renderer) EditorGUILayout.LabelField("物体已被移除，请刷新结果。");
                    if (entry.Issues.Length > 0) EditorGUILayout.HelpBox(entry.Issues, MessageType.Warning);
                }
            }
        }

        private bool Matches(LightmapAnalyzer.Entry entry)
        {
            if (!string.IsNullOrEmpty(search) && entry.Path.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (atlasFilter >= 0 && entry.Index != atlasFilter) return false;
            switch (filter)
            {
                case Filter.Issues: return entry.Issues.Length > 0;
                case Filter.Bound: return entry.Bound;
                case Filter.Unbound: return !entry.Bound;
                case Filter.ContributeGI: return entry.ContributeGI;
                case Filter.LightProbes: return entry.ReceiveGI == ReceiveGI.LightProbes;
                default: return true;
            }
        }

        private void DrawAtlases()
        {
            var textures = new HashSet<Texture>();
            foreach (var map in maps)
                if (map != null)
                {
                    if (map.lightmapColor) textures.Add(map.lightmapColor);
                    if (map.lightmapDir) textures.Add(map.lightmapDir);
                    if (map.shadowMask) textures.Add(map.shadowMask);
                }
            long bytes = textures.Sum(texture => Profiler.GetRuntimeMemorySizeLong(texture));
            EditorGUILayout.LabelField($"图集槽位：{maps.Length} | 去重纹理：{textures.Count} | 已加载纹理内存：{EditorUtility.FormatBytes(bytes)}");
            EditorGUILayout.HelpBox("内存为 Unity 当前已加载纹理的分配量，不代表磁盘大小或目标设备占用。引用数仅统计扫描范围，零引用不代表图集未使用。UV 矩形无法表示实际 UV 岛占用率。", MessageType.None);
            for (int i = 0; i < maps.Length; i++)
            {
                var map = maps[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    EditorGUILayout.LabelField($"图集 {i} | 扫描范围内引用：{entries.Count(e => e.Index == i)}", EditorStyles.boldLabel);
                    DrawTexture("颜色贴图", map?.lightmapColor);
                    DrawTexture("方向贴图", map?.lightmapDir);
                    DrawTexture("阴影遮罩", map?.shadowMask);
                    if (GUILayout.Button("查看引用物体"))
                    {
                        atlasFilter = i;
                        filter = Filter.All;
                        search = "";
                        page = 0;
                        tab = 0;
                    }
                }
            }
        }

        private static void DrawTexture(string label, Texture2D texture)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(label, texture, typeof(Texture2D), false);
                if (texture)
                {
                    EditorGUILayout.LabelField($"{texture.width} x {texture.height} | {texture.format} | {EditorUtility.FormatBytes(Profiler.GetRuntimeMemorySizeLong(texture))}", GUILayout.Width(300));
                    if (GUILayout.Button("定位", GUILayout.Width(48))) EditorGUIUtility.PingObject(texture);
                }
            }
        }
    }
}
