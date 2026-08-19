using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using EAStudio.Core.EventLog;
using UnityEditor;
using UnityEngine;

namespace EAStudio.Core.Editor
{
    /// <summary>
    /// 埋点日志检索工具。
    /// 支持从默认目录（persistentDataPath/AnalyticsData）及额外指定目录读取日志，
    /// 可按事件名、用户 ID、业务域、Session、时间范围过滤，并提供逐行详情展开。
    /// </summary>
    public sealed class EventLogBrowserWindow : EditorWindow
    {
        // ── 菜单入口 ─────────────────────────────────────────────────────────

        [MenuItem("Tools/EAStudio/数据/埋点日志检索")]
        private static void Open()
        {
            var win = GetWindow<EventLogBrowserWindow>("埋点日志检索");
            win.minSize = new Vector2(800, 540);
        }

        // ── 数据源目录 ────────────────────────────────────────────────────────

        /// <summary>是否启用默认目录（persistentDataPath/AnalyticsData）。</summary>
        private bool   _useDefaultDir  = true;

        /// <summary>额外指定的目录路径列表（可多条）。</summary>
        private List<string> _extraDirs = new List<string>();

        // ── 过滤条件 ─────────────────────────────────────────────────────────

        private string _filterEvent   = "";
        private string _filterUserId  = "";
        private string _filterBizCode = "";
        private string _filterSession = "";
        private bool   _useTimeRange;
        private string _timeFromStr   = "";
        private string _timeToStr     = "";

        // ── 读取结果 ─────────────────────────────────────────────────────────

        private List<EventRow> _rows        = new List<EventRow>();
        private List<EventRow> _filtered    = new List<EventRow>();
        private int            _expandedIdx = -1;
        private Vector2        _listScroll;
        private string         _statusMsg   = "点击「刷新」加载日志";
        private bool           _isError;

        // ── 常量 ─────────────────────────────────────────────────────────────

        private const int   MaxRows      = 2000;  // 最多加载条数，防止编辑器卡顿
        private const float RowHeight    = 22f;
        private const float HeaderHeight = 22f;

        private static readonly float[] ColWidths  = { 160f, 140f, 110f, 90f, 80f, 80f };
        private static readonly string[] ColHeaders = { "时间 (本地)", "事件名", "用户 ID", "业务域", "Session", "Props" };

        // ── 样式（延迟初始化）────────────────────────────────────────────────

        private GUIStyle _rowEven;
        private GUIStyle _rowOdd;
        private GUIStyle _rowSelected;
        private GUIStyle _detailBox;
        private GUIStyle _headerStyle;
        private bool     _stylesReady;

        // ── 数据行模型 ────────────────────────────────────────────────────────

        private struct EventRow
        {
            public long   ts;
            public string tsLocal;    // 本地时间字符串，用于展示
            public string eventName;
            public string userId;
            public string bizCode;
            public string sessionId;
            public string props;
            public string rawLine;    // 原始 JSON，用于详情展开
            public string sourceDir;  // 来源目录，用于区分多数据源
        }

        // ── GUI 主入口 ────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
            DrawSourceBar();
            EditorGUILayout.Space(2);
            DrawFilterBar();
            EditorGUILayout.Space(4);
            DrawStatusBar();
            EditorGUILayout.Space(2);
            DrawTableHeader();
            DrawTableBody();
        }

        // ── 数据源配置区 ──────────────────────────────────────────────────────

        private void DrawSourceBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("数据源目录", EditorStyles.boldLabel);

            // 默认目录行
            EditorGUILayout.BeginHorizontal();
            _useDefaultDir = EditorGUILayout.ToggleLeft("默认目录", _useDefaultDir, GUILayout.Width(72));
            string defaultPath = DefaultRootDir();
            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.TextField(defaultPath);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            // 额外目录列表
            for (int i = 0; i < _extraDirs.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"目录 {i + 1}", GUILayout.Width(46));
                _extraDirs[i] = EditorGUILayout.TextField(_extraDirs[i]);

                if (GUILayout.Button("浏览…", GUILayout.Width(54)))
                {
                    string picked = EditorUtility.OpenFolderPanel("选择 AnalyticsData 目录", _extraDirs[i], "");
                    if (!string.IsNullOrEmpty(picked))
                        _extraDirs[i] = picked;
                }
                if (GUILayout.Button("✕", GUILayout.Width(22)))
                {
                    _extraDirs.RemoveAt(i);
                    break; // 列表改变，本帧结束
                }
                EditorGUILayout.EndHorizontal();
            }

            // 添加目录按钮
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("+ 添加目录", GUILayout.Width(90)))
                _extraDirs.Add("");
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── 过滤条件区 ────────────────────────────────────────────────────────

        private void DrawFilterBar()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // 第一行：文本过滤
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("事件名", GUILayout.Width(42));
            _filterEvent = EditorGUILayout.TextField(_filterEvent, GUILayout.Width(120));

            GUILayout.Space(6);
            GUILayout.Label("用户 ID", GUILayout.Width(48));
            _filterUserId = EditorGUILayout.TextField(_filterUserId, GUILayout.Width(110));

            GUILayout.Space(6);
            GUILayout.Label("业务域", GUILayout.Width(42));
            _filterBizCode = EditorGUILayout.TextField(_filterBizCode, GUILayout.Width(90));

            GUILayout.Space(6);
            GUILayout.Label("Session", GUILayout.Width(50));
            _filterSession = EditorGUILayout.TextField(_filterSession, GUILayout.Width(130));

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("刷新", GUILayout.Width(56), GUILayout.Height(20)))
                LoadAndFilter();
            if (GUILayout.Button("过滤", GUILayout.Width(56), GUILayout.Height(20)))
                ApplyFilter();
            if (GUILayout.Button("清空", GUILayout.Width(56), GUILayout.Height(20)))
                ClearFilter();

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // 第二行：时间范围
            EditorGUILayout.BeginHorizontal();
            _useTimeRange = EditorGUILayout.ToggleLeft("时间范围", _useTimeRange, GUILayout.Width(70));
            EditorGUI.BeginDisabledGroup(!_useTimeRange);
            GUILayout.Label("从", GUILayout.Width(16));
            _timeFromStr = EditorGUILayout.TextField(_timeFromStr, GUILayout.Width(148));
            GUILayout.Label("到", GUILayout.Width(16));
            _timeToStr = EditorGUILayout.TextField(_timeToStr, GUILayout.Width(148));
            GUILayout.Label("（yyyy-MM-dd HH:mm:ss）", EditorStyles.miniLabel);
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndVertical();
        }

        // ── 状态栏 ────────────────────────────────────────────────────────────

        private void DrawStatusBar()
        {
            var prevColor = GUI.color;
            if (_isError) GUI.color = new Color(1f, 0.4f, 0.4f);
            EditorGUILayout.LabelField($"共 {_filtered.Count} 条（原始 {_rows.Count} 条）  |  {_statusMsg}",
                EditorStyles.miniLabel);
            GUI.color = prevColor;
        }

        // ── 表头 ─────────────────────────────────────────────────────────────

        private void DrawTableHeader()
        {
            EditorGUILayout.BeginHorizontal(_headerStyle);
            foreach ((string h, float w) in ZipHeaderWidths())
                GUILayout.Label(h, EditorStyles.boldLabel, GUILayout.Width(w));
            GUILayout.Label("来源", EditorStyles.boldLabel, GUILayout.ExpandWidth(true));
            EditorGUILayout.EndHorizontal();
        }

        // ── 表体 ─────────────────────────────────────────────────────────────

        private void DrawTableBody()
        {
            float usedHeight = 98f   // 数据源区（大约）
                             + 62f   // 过滤区
                             + 18f   // 状态栏
                             + HeaderHeight;
            float bodyHeight = Mathf.Max(position.height - usedHeight, 100f);

            _listScroll = EditorGUILayout.BeginScrollView(_listScroll, GUILayout.Height(bodyHeight));

            for (int i = 0; i < _filtered.Count; i++)
            {
                EventRow row      = _filtered[i];
                bool     selected = _expandedIdx == i;
                var      style    = selected ? _rowSelected : (i % 2 == 0 ? _rowEven : _rowOdd);

                EditorGUILayout.BeginHorizontal(style, GUILayout.Height(RowHeight));
                GUILayout.Label(row.tsLocal,                        GUILayout.Width(ColWidths[0]));
                GUILayout.Label(row.eventName,                      GUILayout.Width(ColWidths[1]));
                GUILayout.Label(row.userId,                         GUILayout.Width(ColWidths[2]));
                GUILayout.Label(row.bizCode,                        GUILayout.Width(ColWidths[3]));
                GUILayout.Label(ShortSession(row.sessionId),        GUILayout.Width(ColWidths[4]));
                GUILayout.Label(Ellipsis(row.props, 12),            GUILayout.Width(ColWidths[5]));
                GUILayout.Label(ShortDir(row.sourceDir),            GUILayout.ExpandWidth(true));

                if (GUILayout.Button(selected ? "▲" : "▼", EditorStyles.miniButton, GUILayout.Width(22)))
                    _expandedIdx = selected ? -1 : i;

                EditorGUILayout.EndHorizontal();

                if (selected)
                    DrawDetail(row);
            }

            EditorGUILayout.EndScrollView();
        }

        // ── 行详情展开 ────────────────────────────────────────────────────────

        private void DrawDetail(EventRow row)
        {
            EditorGUILayout.BeginVertical(_detailBox);

            DetailField("事件名",      row.eventName);
            DetailField("时间 (本地)", row.tsLocal);
            DetailField("Unix ms",     row.ts.ToString());
            DetailField("用户 ID",     row.userId);
            DetailField("业务域",      row.bizCode);
            DetailField("Session",     row.sessionId);
            DetailField("Props",       row.props);
            DetailField("来源目录",    row.sourceDir);

            GUILayout.Space(4);
            EditorGUILayout.LabelField("原始 JSON", EditorStyles.boldLabel);
            EditorGUILayout.SelectableLabel(row.rawLine, EditorStyles.textArea,
                GUILayout.MinHeight(56f), GUILayout.ExpandWidth(true));

            if (GUILayout.Button("复制 JSON", GUILayout.Width(80)))
                EditorGUIUtility.systemCopyBuffer = row.rawLine;

            EditorGUILayout.EndVertical();
        }

        private static void DetailField(string label, string value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(86));
            EditorGUILayout.SelectableLabel(value ?? "",
                GUILayout.Height(EditorGUIUtility.singleLineHeight));
            EditorGUILayout.EndHorizontal();
        }

        // ── 数据读取 ─────────────────────────────────────────────────────────

        private void LoadAndFilter()
        {
            _rows.Clear();
            _filtered.Clear();
            _expandedIdx = -1;
            _isError     = false;

            var errors = new List<string>();

            // 默认目录
            if (_useDefaultDir)
            {
                string def = DefaultRootDir();
                if (Directory.Exists(def))
                    ReadFromRoot(def, ref errors);
                else
                    errors.Add($"默认目录不存在：{def}");
            }

            // 额外目录
            foreach (string extra in _extraDirs)
            {
                if (string.IsNullOrWhiteSpace(extra)) continue;
                if (Directory.Exists(extra))
                    ReadFromRoot(extra, ref errors);
                else
                    errors.Add($"目录不存在：{extra}");
            }

            // 按时间戳降序排列所有来源数据
            _rows.Sort((a, b) => b.ts.CompareTo(a.ts));

            if (errors.Count > 0)
            {
                _statusMsg = string.Join(" | ", errors);
                _isError   = true;
            }
            else
            {
                _statusMsg = $"已加载 {_rows.Count} 条原始记录";
                _isError   = false;
            }

            ApplyFilter();
        }

        /// <summary>读取单个 rootDir（含 Active/WAL 和 Segments/*.data）。</summary>
        private void ReadFromRoot(string rootDir, ref List<string> errors)
        {
            try
            {
                string walPath = Path.Combine(rootDir, "Active", "current.wal");
                ReadNdjsonFile(walPath, rootDir);

                string segDir = Path.Combine(rootDir, "Segments");
                if (Directory.Exists(segDir))
                {
                    string[] segs = Directory.GetFiles(segDir, "*.data");
                    Array.Sort(segs, (a, b) => ExtractTs(b).CompareTo(ExtractTs(a)));
                    foreach (string seg in segs)
                    {
                        if (_rows.Count >= MaxRows) break;
                        ReadNdjsonFile(seg, rootDir);
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"读取失败 [{rootDir}]：{ex.Message}");
            }
        }

        private void ReadNdjsonFile(string path, string sourceDir)
        {
            if (!File.Exists(path)) return;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite, 4096);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (_rows.Count >= MaxRows) break;
                line = line.Trim();
                if (!line.StartsWith("{") || !line.EndsWith("}")) continue;

                EventRow row = ParseRow(line, sourceDir);
                if (row.ts > 0)
                    _rows.Add(row);
            }
        }

        private static EventRow ParseRow(string json, string sourceDir)
        {
            try
            {
                var ev = JsonUtility.FromJson<TrackEnvelope>(json);
                if (ev == null) return default;
                return new EventRow
                {
                    ts        = ev.ts,
                    tsLocal   = FormatLocalTime(ev.ts),
                    eventName = ev.@event    ?? "",
                    userId    = ev.userId    ?? "",
                    bizCode   = ev.bizCode   ?? "",
                    sessionId = ev.sessionId ?? "",
                    props     = ev.props     ?? "{}",
                    rawLine   = json,
                    sourceDir = sourceDir,
                };
            }
            catch { return default; }
        }

        // ── 过滤逻辑 ─────────────────────────────────────────────────────────

        private void ApplyFilter()
        {
            _filtered.Clear();
            _expandedIdx = -1;

            long tsFrom = 0, tsTo = long.MaxValue;
            if (_useTimeRange)
            {
                tsFrom = ParseLocalTimeToMs(_timeFromStr);
                tsTo   = ParseLocalTimeToMs(_timeToStr);
                if (tsTo <= 0) tsTo = long.MaxValue;
            }

            string evFilter  = _filterEvent.Trim();
            string uidFilter = _filterUserId.Trim().ToLowerInvariant();
            string bizFilter = _filterBizCode.Trim().ToLowerInvariant();
            string sesFilter = _filterSession.Trim().ToLowerInvariant();

            foreach (EventRow row in _rows)
            {
                if (_useTimeRange && (row.ts < tsFrom || row.ts > tsTo)) continue;

                if (evFilter.Length > 0)
                {
                    string eventKey = evFilter;
                    bool exact = evFilter.StartsWith("#sym:", StringComparison.OrdinalIgnoreCase);
                    if (exact)
                        eventKey = evFilter.Substring("#sym:".Length).Trim();

                    if (string.IsNullOrEmpty(eventKey))
                        continue;

                    bool matchesEvent = exact
                        ? string.Equals(row.eventName, eventKey, StringComparison.OrdinalIgnoreCase)
                        : row.eventName.IndexOf(eventKey, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!matchesEvent)
                        continue;
                }

                if (uidFilter.Length > 0 && !row.userId.ToLowerInvariant().Contains(uidFilter))     continue;
                if (bizFilter.Length > 0 && !row.bizCode.ToLowerInvariant().Contains(bizFilter))    continue;
                if (sesFilter.Length > 0 && !row.sessionId.ToLowerInvariant().Contains(sesFilter))  continue;
                _filtered.Add(row);
            }

            Repaint();
        }

        private void ClearFilter()
        {
            _filterEvent   = "";
            _filterUserId  = "";
            _filterBizCode = "";
            _filterSession = "";
            _useTimeRange  = false;
            _timeFromStr   = "";
            _timeToStr     = "";
            ApplyFilter();
        }

        // ── 工具方法 ─────────────────────────────────────────────────────────

        private static string DefaultRootDir() =>
            Path.Combine(Application.persistentDataPath, "AnalyticsData");

        private static string FormatLocalTime(long epochMs)
        {
            try { return DateTimeOffset.FromUnixTimeMilliseconds(epochMs).LocalDateTime.ToString("MM-dd HH:mm:ss.fff"); }
            catch { return epochMs.ToString(); }
        }

        private static long ParseLocalTimeToMs(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return 0;
            return DateTime.TryParse(s, out DateTime dt)
                ? new DateTimeOffset(dt).ToUnixTimeMilliseconds()
                : 0;
        }

        private static long ExtractTs(string path)
        {
            string[] pts = Path.GetFileNameWithoutExtension(path).Split('_');
            return pts.Length >= 2 && long.TryParse(pts[1], out long ts) ? ts : 0L;
        }

        /// <summary>只展示 Session 末尾 6 字符，节省列宽。</summary>
        private static string ShortSession(string s) =>
            string.IsNullOrEmpty(s) ? "" : s.Length > 6 ? "…" + s[^6..] : s;

        /// <summary>截断字符串并附加省略号。</summary>
        private static string Ellipsis(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : s.Length > max ? s[..max] + "…" : s;

        /// <summary>只展示目录路径末尾部分，防止列过宽。</summary>
        private static string ShortDir(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return "";
            // 展示最后两段路径
            string[] parts = dir.Replace('\\', '/').TrimEnd('/').Split('/');
            return parts.Length >= 2 ? $"…/{parts[^2]}/{parts[^1]}" : dir;
        }

        private IEnumerable<(string header, float width)> ZipHeaderWidths()
        {
            for (int i = 0; i < ColHeaders.Length; i++)
                yield return (ColHeaders[i], ColWidths[i]);
        }

        // ── 样式初始化 ────────────────────────────────────────────────────────

        private void EnsureStyles()
        {
            if (_stylesReady) return;

            _rowEven = new GUIStyle(EditorStyles.label)
            {
                normal  = { background = MakeTex(new Color(0.22f, 0.22f, 0.22f)) },
                padding = new RectOffset(4, 4, 2, 2),
            };
            _rowOdd = new GUIStyle(_rowEven)
            {
                normal = { background = MakeTex(new Color(0.19f, 0.19f, 0.19f)) },
            };
            _rowSelected = new GUIStyle(_rowEven)
            {
                normal = { background = MakeTex(new Color(0.17f, 0.36f, 0.53f)) },
            };
            _detailBox = new GUIStyle(EditorStyles.helpBox)
            {
                padding = new RectOffset(8, 8, 6, 6),
            };
            _headerStyle = new GUIStyle(EditorStyles.toolbar)
            {
                fixedHeight = HeaderHeight,
            };

            _stylesReady = true;
        }

        private static Texture2D MakeTex(Color color)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, color);
            tex.Apply();
            return tex;
        }
    }
}
