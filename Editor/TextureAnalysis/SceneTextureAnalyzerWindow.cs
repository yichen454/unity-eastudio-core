using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor.TextureAnalysis
{
    public class SceneTextureAnalyzerWindow : EditorWindow
    {
        private TextureReport _report;
        private TextureScanScope _selectedScope = TextureScanScope.CurrentScene;
        private Vector2 _listScroll;
        private Vector2 _detailScroll;
        private string _searchFilter = string.Empty;
        private TextureUsageEntry _selectedEntry;

        private enum SortField
        {
            MemorySize,
            StorageSize,
            Resolution,
            Name,
            Mipmaps
        }

        private SortField _sortField = SortField.MemorySize;
        private bool _sortAscending = false;

        private bool _filterPotOnly = false;
        private bool _filterNonPotOnly = false;
        private bool _filterUncompressedOnly = false;
        private bool _filterHighResOnly = false;
        private bool _filterReadableOnly = false;
        private bool _filterMissingMipsOnly = false;

        private GUIStyle _headerLabelStyle;
        private GUIStyle _cardBoxStyle;
        private GUIStyle _selectedRowStyle;
        private GUIStyle _unselectedRowStyle;
        private GUIStyle _badgeStyle;

        [MenuItem("Tools/EAStudio/贴图/场景贴图分析", false, 101)]
        public static void Open()
        {
            var window = GetWindow<SceneTextureAnalyzerWindow>("纹理分析器");
            window.minSize = new Vector2(920, 580);
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
            try
            {
                EditorUtility.DisplayProgressBar("扫描纹理", "正在收集场景引用的纹理...", 0.1f);
                _report = TextureScanner.Scan(_selectedScope, (progress, message) =>
                {
                    EditorUtility.DisplayProgressBar("分析纹理中", message, progress);
                });
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }

            _selectedEntry = _report != null && _report.Entries.Count > 0 ? _report.Entries[0] : null;
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

            _badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold
            };
        }

        private void OnGUI()
        {
            InitStyles();

            DrawTopToolbar();
            DrawSummaryDashboard();
            DrawFilterToolbar();

            EditorGUILayout.Space(2);

            EditorGUILayout.BeginHorizontal();
            {
                // Left Panel: Texture List (55% width)
                DrawLeftTextureList(position.width * 0.55f);

                // Splitter line
                GUILayout.Box(GUIContent.none, GUILayout.Width(2), GUILayout.ExpandHeight(true));

                // Right Panel: Details (Preview, Properties, Referencing Scenes, Optimization Suggestions)
                DrawRightDetails();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTopToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label("扫描范围:", GUILayout.Width(60));
                var newScope = (TextureScanScope)EditorGUILayout.EnumPopup(_selectedScope, EditorStyles.toolbarDropDown, GUILayout.Width(110));
                if (newScope != _selectedScope)
                {
                    _selectedScope = newScope;
                    RunScan();
                }

                if (GUILayout.Button("重新扫描", EditorStyles.toolbarButton, GUILayout.Width(75)))
                {
                    RunScan();
                }

                GUILayout.Space(10);
                GUILayout.Label("搜索:", GUILayout.Width(35));
                _searchFilter = EditorGUILayout.TextField(_searchFilter, EditorStyles.toolbarSearchField, GUILayout.Width(160));

                if (GUILayout.Button("清除", EditorStyles.toolbarButton, GUILayout.Width(40)))
                {
                    _searchFilter = string.Empty;
                    GUI.FocusControl(null);
                }

                GUILayout.Space(10);
                GUILayout.Label("排序:", GUILayout.Width(35));
                var newSort = (SortField)EditorGUILayout.EnumPopup(_sortField, EditorStyles.toolbarDropDown, GUILayout.Width(90));
                if (newSort != _sortField)
                {
                    _sortField = newSort;
                }

                string orderLabel = _sortAscending ? "▲ 升序" : "▼ 降序";
                if (GUILayout.Button(orderLabel, EditorStyles.toolbarButton, GUILayout.Width(55)))
                {
                    _sortAscending = !_sortAscending;
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
                DrawStatCard("扫描场景数", _report.ScannedScenePaths.Count.ToString());
                DrawStatCard("纹理总数", _report.TotalTextureCount.ToString());
                DrawStatCard("显存总占用", TextureUsageEntry.FormatBytes(_report.TotalMemorySizeBytes));
                DrawStatCard("高分辨率 (≥2K)", _report.TotalHighResCount.ToString(), _report.TotalHighResCount > 0 ? Color.yellow : Color.white);
                DrawStatCard("未压缩纹理", _report.TotalUncompressedCount.ToString(), _report.TotalUncompressedCount > 0 ? Color.yellow : Color.white);
                DrawStatCard("缺失 Mipmap", _report.TotalMissingMipmapsCount.ToString(), _report.TotalMissingMipmapsCount > 0 ? new Color(1f, 0.6f, 0.2f) : Color.white);
                DrawStatCard("Read/Write 开启", _report.TotalReadableCount.ToString(), _report.TotalReadableCount > 0 ? Color.red : Color.white);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawStatCard(string title, string value, Color? color = null)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.ExpandWidth(true));
            {
                GUILayout.Label(title, EditorStyles.miniLabel);
                var prevColor = GUI.color;
                if (color.HasValue) GUI.color = color.Value;
                GUILayout.Label(value, EditorStyles.boldLabel);
                GUI.color = prevColor;
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawFilterToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            {
                GUILayout.Label("筛选:", EditorStyles.miniLabel, GUILayout.Width(35));

                _filterHighResOnly = GUILayout.Toggle(_filterHighResOnly, "高分辨率(≥2K)", EditorStyles.toolbarButton);
                _filterUncompressedOnly = GUILayout.Toggle(_filterUncompressedOnly, "未压缩", EditorStyles.toolbarButton);
                _filterMissingMipsOnly = GUILayout.Toggle(_filterMissingMipsOnly, "缺失Mipmap", EditorStyles.toolbarButton);
                _filterReadableOnly = GUILayout.Toggle(_filterReadableOnly, "Read/Write", EditorStyles.toolbarButton);
                _filterNonPotOnly = GUILayout.Toggle(_filterNonPotOnly, "非POT(NPOT)", EditorStyles.toolbarButton);

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("重置筛选", EditorStyles.toolbarButton, GUILayout.Width(65)))
                {
                    _filterHighResOnly = false;
                    _filterUncompressedOnly = false;
                    _filterMissingMipsOnly = false;
                    _filterReadableOnly = false;
                    _filterNonPotOnly = false;
                    _searchFilter = string.Empty;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private List<TextureUsageEntry> GetFilteredAndSortedEntries()
        {
            if (_report == null || _report.Entries == null)
                return new List<TextureUsageEntry>();

            IEnumerable<TextureUsageEntry> list = _report.Entries;

            if (!string.IsNullOrEmpty(_searchFilter))
            {
                list = list.Where(e => e.TextureName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                       e.AssetPath.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            if (_filterHighResOnly)
                list = list.Where(e => (e.Issues & TextureIssueFlags.HighResolution) != 0);

            if (_filterUncompressedOnly)
                list = list.Where(e => (e.Issues & TextureIssueFlags.Uncompressed) != 0);

            if (_filterMissingMipsOnly)
                list = list.Where(e => (e.Issues & TextureIssueFlags.MissingMipmaps) != 0);

            if (_filterReadableOnly)
                list = list.Where(e => (e.Issues & TextureIssueFlags.ReadOnlyViolation) != 0);

            if (_filterNonPotOnly)
                list = list.Where(e => (e.Issues & TextureIssueFlags.NonPowerOfTwo) != 0);

            // Sorting
            switch (_sortField)
            {
                case SortField.MemorySize:
                    list = _sortAscending ? list.OrderBy(e => e.MemorySizeBytes) : list.OrderByDescending(e => e.MemorySizeBytes);
                    break;
                case SortField.StorageSize:
                    list = _sortAscending ? list.OrderBy(e => e.StorageSizeBytes) : list.OrderByDescending(e => e.StorageSizeBytes);
                    break;
                case SortField.Resolution:
                    list = _sortAscending ? list.OrderBy(e => e.Width * e.Height) : list.OrderByDescending(e => e.Width * e.Height);
                    break;
                case SortField.Name:
                    list = _sortAscending ? list.OrderBy(e => e.TextureName) : list.OrderByDescending(e => e.TextureName);
                    break;
                case SortField.Mipmaps:
                    list = _sortAscending ? list.OrderBy(e => e.MipmapCount) : list.OrderByDescending(e => e.MipmapCount);
                    break;
            }

            return list.ToList();
        }

        private void DrawLeftTextureList(float width)
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(width));
            {
                // List Header
                EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                {
                    GUILayout.Label("纹理名称与格式", GUILayout.Width(width * 0.45f));
                    GUILayout.Label("分辨率", GUILayout.Width(75));
                    GUILayout.Label("Mip", GUILayout.Width(45));
                    GUILayout.Label("显存占用", GUILayout.Width(75));
                    GUILayout.Label("问题警报", GUILayout.ExpandWidth(true));
                }
                EditorGUILayout.EndHorizontal();

                var entries = GetFilteredAndSortedEntries();

                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);
                {
                    if (entries.Count == 0)
                    {
                        EditorGUILayout.HelpBox("未找到符合条件的纹理，或尚未执行扫描。", MessageType.Info);
                    }
                    else
                    {
                        foreach (var entry in entries)
                        {
                            bool isSelected = _selectedEntry == entry;
                            GUIStyle rowStyle = isSelected ? _selectedRowStyle : _unselectedRowStyle;

                            EditorGUILayout.BeginHorizontal(rowStyle, GUILayout.Height(24));
                            {
                                // Thumbnail + Name
                                Texture texAsset = AssetDatabase.LoadAssetAtPath<Texture>(entry.AssetPath);
                                GUIContent content = new GUIContent(entry.TextureName, texAsset, entry.AssetPath);

                                if (GUILayout.Button(content, EditorStyles.label, GUILayout.Width(width * 0.45f), GUILayout.Height(22)))
                                {
                                    _selectedEntry = entry;
                                    Selection.activeObject = texAsset;
                                }

                                GUILayout.Label(entry.ResolutionString, EditorStyles.miniLabel, GUILayout.Width(75));
                                GUILayout.Label(entry.HasMipmaps ? $"{entry.MipmapCount} 级" : "无", EditorStyles.miniLabel, GUILayout.Width(45));
                                GUILayout.Label(entry.MemorySizeFormatted, EditorStyles.miniBoldLabel, GUILayout.Width(75));

                                // Badges
                                DrawIssueBadges(entry);

                                GUILayout.FlexibleSpace();

                                if (GUILayout.Button("定位", EditorStyles.miniButton, GUILayout.Width(40)))
                                {
                                    _selectedEntry = entry;
                                    if (texAsset != null)
                                    {
                                        EditorGUIUtility.PingObject(texAsset);
                                        Selection.activeObject = texAsset;
                                    }
                                }
                            }
                            EditorGUILayout.EndHorizontal();
                        }
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawIssueBadges(TextureUsageEntry entry)
        {
            if (entry.Issues == TextureIssueFlags.None)
            {
                var prevColor = GUI.color;
                GUI.color = Color.green;
                GUILayout.Label("✓ 正常", EditorStyles.miniLabel, GUILayout.Width(50));
                GUI.color = prevColor;
                return;
            }

            var old = GUI.color;
            if ((entry.Issues & TextureIssueFlags.ReadOnlyViolation) != 0)
            {
                GUI.color = Color.red;
                GUILayout.Label("[R/W]", EditorStyles.miniLabel, GUILayout.Width(40));
            }
            if ((entry.Issues & TextureIssueFlags.Uncompressed) != 0)
            {
                GUI.color = new Color(1f, 0.4f, 0.2f);
                GUILayout.Label("[未压缩]", EditorStyles.miniLabel, GUILayout.Width(45));
            }
            if ((entry.Issues & TextureIssueFlags.HighResolution) != 0)
            {
                GUI.color = Color.yellow;
                GUILayout.Label("[≥2K]", EditorStyles.miniLabel, GUILayout.Width(35));
            }
            if ((entry.Issues & TextureIssueFlags.MissingMipmaps) != 0)
            {
                GUI.color = new Color(1f, 0.7f, 0.2f);
                GUILayout.Label("[缺Mip]", EditorStyles.miniLabel, GUILayout.Width(45));
            }
            if ((entry.Issues & TextureIssueFlags.NonPowerOfTwo) != 0)
            {
                GUI.color = Color.cyan;
                GUILayout.Label("[NPOT]", EditorStyles.miniLabel, GUILayout.Width(45));
            }
            GUI.color = old;
        }

        private void DrawRightDetails()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            {
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                {
                    if (_selectedEntry == null)
                    {
                        EditorGUILayout.HelpBox("请在左侧列表中选择一个纹理以查看详细分析与优化建议。", MessageType.Info);
                    }
                    else
                    {
                        DrawSelectedTextureInfo(_selectedEntry);
                    }
                }
                EditorGUILayout.EndScrollView();
            }
            EditorGUILayout.EndVertical();
        }

        private void DrawSelectedTextureInfo(TextureUsageEntry entry)
        {
            EditorGUILayout.LabelField("纹理详细属性", _headerLabelStyle);
            EditorGUILayout.Space(2);

            Texture texAsset = AssetDatabase.LoadAssetAtPath<Texture>(entry.AssetPath);

            // Preview & Path Block
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                if (texAsset != null)
                {
                    Rect previewRect = GUILayoutUtility.GetRect(100, 100, GUILayout.Width(100), GUILayout.Height(100));
                    EditorGUI.DrawPreviewTexture(previewRect, texAsset, null, ScaleMode.ScaleToFit);
                }

                EditorGUILayout.BeginVertical();
                {
                    EditorGUILayout.ObjectField("资源对象", texAsset, typeof(Texture), false);
                    EditorGUILayout.TextField("资源路径", entry.AssetPath);
                    EditorGUILayout.LabelField("纹理类型", entry.TextureType.ToString());
                    EditorGUILayout.LabelField("像素格式", entry.Format.ToString());
                    EditorGUILayout.LabelField("分辨率", $"{entry.ResolutionString} {(entry.IsPowerOfTwo ? "(POT)" : "(NPOT)")}");
                    EditorGUILayout.LabelField("Mipmap 级数", entry.HasMipmaps ? $"{entry.MipmapCount} 级 (已开启)" : "未开启");
                    EditorGUILayout.LabelField("显存预估占用", entry.MemorySizeFormatted);
                    EditorGUILayout.LabelField("磁盘文件大小", entry.StorageSizeFormatted);
                    EditorGUILayout.LabelField("可读写 (Read/Write)", entry.IsReadable ? "开启 (占用双倍内存)" : "关闭");
                }
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // Issue & Optimization Recommendations
            DrawOptimizationRecommendations(entry);

            EditorGUILayout.Space(6);

            // Referencing Scenes
            DrawReferencingScenes(entry);
        }

        private void DrawOptimizationRecommendations(TextureUsageEntry entry)
        {
            EditorGUILayout.LabelField("诊断分析与优化建议", _headerLabelStyle);

            if (entry.Issues == TextureIssueFlags.None)
            {
                EditorGUILayout.HelpBox("该纹理设置合理，未发现明显的内存浪费或异常配置。", MessageType.Info);
                return;
            }

            var importer = AssetImporter.GetAtPath(entry.AssetPath) as TextureImporter;

            if ((entry.Issues & TextureIssueFlags.ReadOnlyViolation) != 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.HelpBox("【严重】Read/Write Enabled 已开启：这会导致纹理在 CPU 内存与 GPU 显存各驻留一份（双倍内存占用）。如无需在脚本中读取像素，强烈建议关闭！", MessageType.Error);
                    if (importer != null && GUILayout.Button("一键关闭 Read/Write", GUILayout.Height(24)))
                    {
                        importer.isReadable = false;
                        importer.SaveAndReimport();
                        RunScan();
                    }
                }
                EditorGUILayout.EndVertical();
            }

            if ((entry.Issues & TextureIssueFlags.Uncompressed) != 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.HelpBox("【建议】未压缩格式：当前为 RGBA32/RGB24 等非压缩格式。对于移动端/主机平台，建议开启硬件纹理压缩（ASTC / ETC2 / BC7 / DXT）。", MessageType.Warning);
                }
                EditorGUILayout.EndVertical();
            }

            if ((entry.Issues & TextureIssueFlags.HighResolution) != 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.HelpBox("【注意】高分辨率纹理 (≥ 2048)：纹理分辨率每翻一倍，显存占用增长 4 倍！请评估是否需要如此高分辨率，可适当降低 Max Size。", MessageType.Warning);
                    if (importer != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            if (GUILayout.Button("限制为 1024", GUILayout.Width(100)))
                            {
                                importer.maxTextureSize = 1024;
                                importer.SaveAndReimport();
                                RunScan();
                            }
                            if (GUILayout.Button("限制为 512", GUILayout.Width(100)))
                            {
                                importer.maxTextureSize = 512;
                                importer.SaveAndReimport();
                                RunScan();
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
                EditorGUILayout.EndVertical();
            }

            if ((entry.Issues & TextureIssueFlags.MissingMipmaps) != 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.HelpBox("【建议】缺失 Mipmap：对于 3D 场景中的非 UI 纹理，未开启 Mipmap 在远距离观察时会导致严重的采样摩尔纹/噪点走样，且降低 GPU 采样缓存命中率。", MessageType.Warning);
                    if (importer != null && GUILayout.Button("一键开启 Mipmap", GUILayout.Height(24)))
                    {
                        importer.mipmapEnabled = true;
                        importer.SaveAndReimport();
                        RunScan();
                    }
                }
                EditorGUILayout.EndVertical();
            }

            if ((entry.Issues & TextureIssueFlags.UnnecessaryMipmaps) != 0)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    EditorGUILayout.HelpBox("【建议】冗余 Mipmap：UI 或 2D 精灵通常以 1:1 像素或正交投影显示，开启 Mipmap 会无端浪费约 33% 的内存空间。", MessageType.Warning);
                    if (importer != null && GUILayout.Button("一键关闭 Mipmap", GUILayout.Height(24)))
                    {
                        importer.mipmapEnabled = false;
                        importer.SaveAndReimport();
                        RunScan();
                    }
                }
                EditorGUILayout.EndVertical();
            }

            if ((entry.Issues & TextureIssueFlags.NonPowerOfTwo) != 0)
            {
                EditorGUILayout.HelpBox("【提示】NPOT 纹理：宽高不是 2 的整数次幂，部分平台或压缩格式无法生效或会导致自动补边垫高开销。", MessageType.Info);
            }

            if ((entry.Issues & TextureIssueFlags.DuplicateName) != 0)
            {
                EditorGUILayout.HelpBox("【提示】存在同名纹理：项目中存在其他相同名称的纹理文件，请确认是否存在重复资源冗余打包。", MessageType.Info);
            }
        }

        private void DrawReferencingScenes(TextureUsageEntry entry)
        {
            EditorGUILayout.LabelField($"引用此纹理的场景 ({entry.ReferencingScenes.Count})", _headerLabelStyle);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            {
                if (entry.ReferencingScenes.Count == 0)
                {
                    GUILayout.Label("无直接关联场景", EditorStyles.miniLabel);
                }
                else
                {
                    foreach (var sc in entry.ReferencingScenes)
                    {
                        EditorGUILayout.BeginHorizontal();
                        {
                            GUILayout.Label($"• {sc}", EditorStyles.miniLabel);
                            GUILayout.FlexibleSpace();
                            if (GUILayout.Button("选中场景", EditorStyles.miniButton, GUILayout.Width(70)))
                            {
                                var scAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(sc);
                                if (scAsset != null)
                                {
                                    EditorGUIUtility.PingObject(scAsset);
                                    Selection.activeObject = scAsset;
                                }
                            }
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            EditorGUILayout.EndVertical();
        }
    }
}
