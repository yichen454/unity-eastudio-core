using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor.ShaderAnalysis
{
    public class SceneShaderAnalyzerWindow : EditorWindow
    {
        private SceneShaderReport _report;
        private Vector2 _shaderListScroll;
        private Vector2 _detailScroll;
        private string _searchFilter = string.Empty;
        private ShaderUsageEntry _selectedEntry;

        private readonly Dictionary<string, bool> _variantFoldouts = new Dictionary<string, bool>();
        private readonly Dictionary<string, bool> _targetFoldouts = new Dictionary<string, bool>();
        private bool _showKeywordsImpact = true;
        private bool _showVariantList = true;

        private GUIStyle _headerLabelStyle;
        private GUIStyle _cardBoxStyle;
        private GUIStyle _selectedRowStyle;
        private GUIStyle _unselectedRowStyle;

        [MenuItem("Tools/EAStudio/Shader/Scene Shader Analyzer", false, 100)]
        public static void Open()
        {
            var window = GetWindow<SceneShaderAnalyzerWindow>("Shader 分析器");
            window.minSize = new Vector2(900, 560);
            window.Show();
        }

        private void OnEnable()
        {
            if (_report == null)
            {
                RunScan();
            }
        }

        public void RunScan()
        {
            _report = SceneShaderScanner.ScanActiveScenes();
            _variantFoldouts.Clear();
            _targetFoldouts.Clear();
            _selectedEntry = _report.Entries.FirstOrDefault();
            Repaint();
        }

        private void InitStyles()
        {
            if (_headerLabelStyle != null)
                return;

            _headerLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };

            _cardBoxStyle = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 8, 8)
            };

            _selectedRowStyle = new GUIStyle(GUI.skin.box);
            _selectedRowStyle.normal.background = Texture2D.grayTexture;

            _unselectedRowStyle = new GUIStyle(GUI.skin.box);
        }

        private void OnGUI()
        {
            InitStyles();

            DrawTopToolbar();
            DrawSummaryDashboard();

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            {
                // Left Panel: Shader List (40% width)
                DrawLeftShaderList(position.width * 0.40f);

                // Splitter line
                GUILayout.Box(GUIContent.none, GUILayout.Width(2), GUILayout.ExpandHeight(true));

                // Right Panel: Details (Keyword Impacts, Variants, Materials, Referencers)
                DrawRightDetails();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTopToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                if (GUILayout.Button("重新扫描场景", EditorStyles.toolbarButton, GUILayout.Width(95)))
                {
                    RunScan();
                }

                GUILayout.Space(10);
                GUILayout.Label("搜索 Shader:", GUILayout.Width(80));
                _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(220));

                if (GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    _searchFilter = string.Empty;
                    GUI.FocusControl(null);
                }

                GUILayout.FlexibleSpace();

                if (_report != null)
                {
                    GUILayout.Label($"扫描时间: {_report.ScanTime:HH:mm:ss}", EditorStyles.miniLabel);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSummaryDashboard()
        {
            if (_report == null)
                return;

            EditorGUILayout.BeginHorizontal(_cardBoxStyle);
            {
                DrawStatCard("已扫描场景", _report.TotalScenesScanned.ToString());
                DrawStatCard("渲染组件 (Renderers)", _report.TotalRenderersScanned.ToString());
                DrawStatCard("材质实例 (Materials)", _report.TotalMaterialsScanned.ToString());
                DrawStatCard("独立着色器 (Shaders)", _report.TotalUniqueShaders.ToString());
                DrawStatCard("场景激活变体 (组合数)", _report.TotalSceneVariants.ToString());
                DrawStatCard("场景预估 Pass 变体", _report.TotalEstimatedPassVariants.ToString());
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatCard(string title, string value)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            {
                GUILayout.Label(title, EditorStyles.miniLabel);
                GUILayout.Label(value, _headerLabelStyle);
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawLeftShaderList(float width)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                {
                    GUILayout.Label("Shader 列表", EditorStyles.boldLabel);
                    GUILayout.FlexibleSpace();
                    GUILayout.Label("变体 / 材质 / 引用", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                _shaderListScroll = EditorGUILayout.BeginScrollView(_shaderListScroll, GUILayout.ExpandHeight(true));
                {
                    if (_report == null || _report.Entries.Count == 0)
                    {
                        EditorGUILayout.HelpBox("场景中未扫描到任何材质与 Shader。", MessageType.Info);
                    }
                    else
                    {
                        var filtered = string.IsNullOrEmpty(_searchFilter)
                            ? _report.Entries
                            : _report.Entries.Where(e => e.ShaderName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0).ToList();

                        foreach (var entry in filtered)
                        {
                            DrawShaderRow(entry);
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawShaderRow(ShaderUsageEntry entry)
        {
            bool isSelected = _selectedEntry == entry;
            GUIStyle rowStyle = isSelected ? _selectedRowStyle : _unselectedRowStyle;

            EditorGUILayout.BeginVertical(rowStyle);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    string displayName = entry.ShaderName;
                    if (GUILayout.Button(new GUIContent(displayName, entry.AssetPath), EditorStyles.label, GUILayout.ExpandWidth(true)))
                    {
                        _selectedEntry = entry;
                    }

                    // Badges for Variant / Material / Renderer count
                    GUILayout.Label($"{entry.SceneVariantCount} 变体", EditorStyles.miniLabel, GUILayout.Width(50));
                    GUILayout.Label($"{entry.MaterialCount} 材质", EditorStyles.miniLabel, GUILayout.Width(45));
                    GUILayout.Label($"{entry.ReferencerCount} 引用", EditorStyles.miniLabel, GUILayout.Width(45));

                    if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(35)))
                    {
                        _selectedEntry = entry;
                        if (entry.Shader != null)
                        {
                            EditorGUIUtility.PingObject(entry.Shader);
                            Selection.activeObject = entry.Shader;
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawRightDetails()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            {
                if (_selectedEntry == null)
                {
                    EditorGUILayout.HelpBox("请从左侧选择一个 Shader 查看详细变体分布与场景引用。", MessageType.Info);
                    EditorGUILayout.EndVertical();
                    return;
                }

                DrawDetailHeader();

                EditorGUILayout.Space(4);

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll, GUILayout.ExpandHeight(true));
                {
                    DrawKeywordsImpactSection();
                    EditorGUILayout.Space(6);
                    DrawVariantBreakdown();
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawDetailHeader()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                EditorGUILayout.BeginHorizontal();
                {
                    EditorGUILayout.ObjectField(_selectedEntry.Shader, typeof(UnityEngine.Shader), false, GUILayout.Width(260));
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button("在 Project 中高亮", EditorStyles.miniButton, GUILayout.Width(110)))
                    {
                        if (_selectedEntry.Shader != null)
                        {
                            EditorGUIUtility.PingObject(_selectedEntry.Shader);
                            Selection.activeObject = _selectedEntry.Shader;
                        }
                    }

                    if (GUILayout.Button("选中场景所有引用物体", EditorStyles.miniButton, GUILayout.Width(135)))
                    {
                        SelectAllTargetsInScene(_selectedEntry.AllReferencingTargets);
                    }
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(2);

                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.Label($"资源路径: {_selectedEntry.AssetPath}", EditorStyles.miniLabel);
                }
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                {
                    GUILayout.Label($"场景激活 Keyword 组合: {_selectedEntry.SceneVariantCount} 组", EditorStyles.miniBoldLabel);
                    GUILayout.Space(15);
                    GUILayout.Label($"有效 Pass 数: {_selectedEntry.PassCount}", EditorStyles.miniBoldLabel);
                    GUILayout.Space(15);
                    GUILayout.Label($"场景预估 Pass 变体总量: {_selectedEntry.SceneEstimatedPassVariants}", EditorStyles.miniBoldLabel);
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawKeywordsImpactSection()
        {
            _showKeywordsImpact = EditorGUILayout.Foldout(_showKeywordsImpact, $"影响变体的 Keywords 清单 ({_selectedEntry.KeywordImpacts.Count})", true, EditorStyles.foldoutHeader);
            if (!_showKeywordsImpact)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                if (_selectedEntry.KeywordImpacts.Count == 0)
                {
                    GUILayout.Label("当前 Shader 在场景材质中无任何激活的 Keyword（全部使用默认 Pass 变体）。", EditorStyles.miniLabel);
                }
                else
                {
                    // Table Header
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    {
                        GUILayout.Label("Keyword 名称", EditorStyles.boldLabel, GUILayout.Width(220));
                        GUILayout.Label("导致变体分裂", EditorStyles.boldLabel, GUILayout.Width(90));
                        GUILayout.Label("关联变体数", EditorStyles.boldLabel, GUILayout.Width(75));
                        GUILayout.Label("影响材质", EditorStyles.boldLabel, GUILayout.Width(70));
                        GUILayout.Label("影响渲染组件", EditorStyles.boldLabel, GUILayout.Width(80));
                        GUILayout.FlexibleSpace();
                        GUILayout.Label("快速操作", EditorStyles.boldLabel, GUILayout.Width(100));
                    }
                    EditorGUILayout.EndHorizontal();

                    foreach (var impact in _selectedEntry.KeywordImpacts)
                    {
                        EditorGUILayout.BeginHorizontal(EditorStyles.textArea);
                        {
                            // Keyword name with highlight if it causes splitting
                            if (impact.IsMultiVariantCause)
                            {
                                GUILayout.Label($"★ {impact.Keyword}", EditorStyles.boldLabel, GUILayout.Width(220));
                                GUILayout.Label("是 (部分开启)", EditorStyles.miniBoldLabel, GUILayout.Width(90));
                            }
                            else
                            {
                                GUILayout.Label($"  {impact.Keyword}", EditorStyles.label, GUILayout.Width(220));
                                GUILayout.Label("否 (全部开启)", EditorStyles.miniLabel, GUILayout.Width(90));
                            }

                            GUILayout.Label($"{impact.AffectedVariantCount} 个", EditorStyles.miniLabel, GUILayout.Width(75));
                            GUILayout.Label($"{impact.AffectedMaterialCount} 个", EditorStyles.miniLabel, GUILayout.Width(70));
                            GUILayout.Label($"{impact.AffectedRendererCount} 个", EditorStyles.miniLabel, GUILayout.Width(80));

                            GUILayout.FlexibleSpace();

                            if (GUILayout.Button("全选对应物体", EditorStyles.miniButton, GUILayout.Width(95)))
                            {
                                SelectObjectsByKeyword(impact.Keyword);
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawVariantBreakdown()
        {
            _showVariantList = EditorGUILayout.Foldout(_showVariantList, $"场景变体分布详情 ({_selectedEntry.VariantUsages.Count} 组变体组合)", true, EditorStyles.foldoutHeader);
            if (!_showVariantList)
                return;

            for (int i = 0; i < _selectedEntry.VariantUsages.Count; i++)
            {
                var variant = _selectedEntry.VariantUsages[i];
                string foldoutKey = $"{_selectedEntry.ShaderName}_{variant.KeywordSignature}_{i}";

                if (!_variantFoldouts.TryGetValue(foldoutKey, out bool foldout))
                {
                    foldout = i == 0;
                    _variantFoldouts[foldoutKey] = foldout;
                }

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        string headerText = $"[变体 #{i + 1}] ({variant.Materials.Count} 材质, {variant.ReferencingTargets.Count} 引用)";
                        _variantFoldouts[foldoutKey] = EditorGUILayout.Foldout(foldout, headerText, true, EditorStyles.foldoutHeader);

                        GUILayout.FlexibleSpace();

                        if (GUILayout.Button("全选该变体对象", EditorStyles.miniButton, GUILayout.Width(105)))
                        {
                            SelectAllTargetsInScene(variant.ReferencingTargets);
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    if (_variantFoldouts[foldoutKey])
                    {
                        EditorGUI.indentLevel++;

                        // Draw Keywords
                        EditorGUILayout.BeginVertical(GUI.skin.box);
                        {
                            GUILayout.Label("激活 Keywords:", EditorStyles.miniBoldLabel);
                            if (variant.Keywords == null || variant.Keywords.Length == 0)
                            {
                                GUILayout.Label("  [无激活 Keywords - 默认 Pass 状态]", EditorStyles.miniLabel);
                            }
                            else
                            {
                                EditorGUILayout.BeginHorizontal();
                                {
                                    int count = 0;
                                    foreach (var kw in variant.Keywords)
                                    {
                                        GUILayout.Label(kw, EditorStyles.helpBox, GUILayout.ExpandWidth(false));
                                        count++;
                                        if (count % 3 == 0)
                                        {
                                            EditorGUILayout.EndHorizontal();
                                            EditorGUILayout.BeginHorizontal();
                                        }
                                    }
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                        }
                        EditorGUILayout.EndVertical();

                        EditorGUILayout.Space(2);

                        // Draw Materials and Referencing Targets
                        DrawVariantMaterialsAndTargets(variant, foldoutKey);

                        EditorGUI.indentLevel--;
                    }
                }
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }
        }

        private void DrawVariantMaterialsAndTargets(MaterialVariantUsage variant, string parentKey)
        {
            GUILayout.Label("使用此变体的材质及场景对象:", EditorStyles.miniBoldLabel);

            foreach (var mat in variant.Materials)
            {
                if (mat == null)
                    continue;

                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        EditorGUILayout.ObjectField(mat, typeof(Material), false, GUILayout.Width(220));

                        var targetsForMat = variant.ReferencingTargets
                            .Where(t => t.TargetObject is Renderer r && r.sharedMaterials.Contains(mat) ||
                                        t.TargetObject is Terrain terr && terr.materialTemplate == mat ||
                                        t.TargetType == SceneTargetType.Skybox)
                            .ToList();

                        GUILayout.Label($"引用物体: {targetsForMat.Count}", EditorStyles.miniLabel);

                        GUILayout.FlexibleSpace();

                        if (targetsForMat.Count > 0 && GUILayout.Button("选中这些物体", EditorStyles.miniButton, GUILayout.Width(85)))
                        {
                            SelectAllTargetsInScene(targetsForMat);
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    string targetKey = $"{parentKey}_{mat.name}";
                    if (!_targetFoldouts.TryGetValue(targetKey, out bool targetFoldout))
                    {
                        targetFoldout = false;
                        _targetFoldouts[targetKey] = targetFoldout;
                    }

                    var targets = variant.ReferencingTargets
                        .Where(t => t.TargetObject is Renderer r && r.sharedMaterials.Contains(mat) ||
                                    t.TargetObject is Terrain terr && terr.materialTemplate == mat ||
                                    t.TargetType == SceneTargetType.Skybox)
                        .ToList();

                    if (targets.Count > 0)
                    {
                        _targetFoldouts[targetKey] = EditorGUILayout.Foldout(targetFoldout, $"展开引用对象清单 ({targets.Count})", true);
                        if (_targetFoldouts[targetKey])
                        {
                            EditorGUI.indentLevel++;
                            foreach (var target in targets)
                            {
                                EditorGUILayout.BeginHorizontal();
                                {
                                    string iconPrefix = target.IsActiveInHierarchy ? "● " : "○ [未激活] ";
                                    GUILayout.Label($"{iconPrefix}[{target.TargetType}] {target.HierarchyPath}", EditorStyles.miniLabel);

                                    GUILayout.FlexibleSpace();

                                    if (target.HostGameObject != null && GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
                                    {
                                        EditorGUIUtility.PingObject(target.HostGameObject);
                                        Selection.activeGameObject = target.HostGameObject;
                                    }
                                }
                                EditorGUILayout.EndHorizontal();
                            }
                            EditorGUI.indentLevel--;
                        }
                    }
                }
                EditorGUILayout.EndVertical();
            }
        }

        private void SelectObjectsByKeyword(string keyword)
        {
            if (_selectedEntry == null)
                return;

            var matchingTargets = new List<SceneReferencingTarget>();
            foreach (var vu in _selectedEntry.VariantUsages)
            {
                if (vu.Keywords != null && vu.Keywords.Contains(keyword))
                {
                    matchingTargets.AddRange(vu.ReferencingTargets);
                }
            }

            SelectAllTargetsInScene(matchingTargets);
        }

        private static void SelectAllTargetsInScene(List<SceneReferencingTarget> targets)
        {
            if (targets == null || targets.Count == 0)
                return;

            var gos = targets
                .Where(t => t.HostGameObject != null)
                .Select(t => t.HostGameObject)
                .Distinct()
                .ToArray();

            if (gos.Length > 0)
            {
                Selection.objects = gos;
                EditorGUIUtility.PingObject(gos[0]);
            }
        }
    }
}
