using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace EAStudio.Core.EventLog
{
    /// <summary>
    /// 三级联合读取器，按以下优先级聚合事件：
    /// <list type="number">
    ///   <item>Tier 1 – 内存环形缓冲（最新数据，零 I/O）</item>
    ///   <item>Tier 2 – 活跃 WAL 文件 <c>Active/current.wal</c></item>
    ///   <item>Tier 3 – 已冻结分卷 <c>Segments/*.data</c></item>
    /// </list>
    /// 末尾残缺（Torn Write）的 JSON 行会被静默丢弃，保证崩溃后历史数据完整可读。
    /// </summary>
    public sealed class UnifiedTrackReader
    {
        private readonly string _rootDir;
        private readonly TrackStorageEngine _storageEngine;

        public UnifiedTrackReader(string rootDir, TrackStorageEngine storageEngine)
        {
            _rootDir       = rootDir;
            _storageEngine = storageEngine;
        }

        /// <summary>
        /// 跨三级数据源返回最多 <paramref name="limit"/> 条事件，按 <c>ts</c> 降序排列（最新在前）。
        /// 相同 <c>id</c> 的事件只保留一份（内存与 WAL 同时存在时去重）。
        /// </summary>
        /// <param name="limit">最多返回的事件数量，0 表示不限。</param>
        public IReadOnlyList<TrackEnvelope> Read(int limit = 200)
        {
            var seen    = new HashSet<string>(StringComparer.Ordinal);
            var results = new List<TrackEnvelope>();

            // Tier 1：内存缓冲（GetMemorySnapshot 已按最新在前排序）
            foreach (TrackEnvelope ev in _storageEngine.GetMemorySnapshot())
            {
                if (seen.Add(ev.id))
                    results.Add(ev);
            }

            // Tier 2：活跃 WAL
            string walPath = Path.Combine(_rootDir, "Active", "current.wal");
            ReadNdjsonFile(walPath, seen, results);

            // Tier 3：历史分卷（按文件名时间戳降序，优先读最新分卷）
            string segDir = Path.Combine(_rootDir, "Segments");
            if (Directory.Exists(segDir))
            {
                string[] segFiles = Directory.GetFiles(segDir, "*.data");
                Array.Sort(segFiles, CompareSegmentsByTimestampDescending);
                foreach (string segPath in segFiles)
                    ReadNdjsonFile(segPath, seen, results);
            }

            // 全局按 ts 降序排序
            results.Sort((a, b) => b.ts.CompareTo(a.ts));

            if (limit > 0 && results.Count > limit)
                results.RemoveRange(limit, results.Count - limit);

            return results;
        }

        // ── 文件读取 ─────────────────────────────────────────────────────────

        /// <summary>
        /// 以 <see cref="FileShare.ReadWrite"/> 打开文件，支持后台 Worker 同时追加写入。
        /// 残缺行（没有闭合 <c>}</c> 或无换行符）直接跳过。
        /// </summary>
        private static void ReadNdjsonFile(
            string path,
            HashSet<string> seen,
            List<TrackEnvelope> output)
        {
            if (!File.Exists(path)) return;

            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 4096);
                using var reader = new StreamReader(stream, Encoding.UTF8);

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (!IsCompleteLine(line)) continue;

                    TrackEnvelope ev = ParseLine(line);
                    if (ev == null) continue;
                    if (!string.IsNullOrEmpty(ev.id) && seen.Add(ev.id))
                        output.Add(ev);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnifiedTrackReader] 读取失败 {path}: {ex.Message}");
            }
        }

        // ── Torn Write 检测 ───────────────────────────────────────────────────

        /// <summary>检查行是否以 '{' 开头、'}' 结尾，用于过滤崩溃时写入的残缺行。</summary>
        private static bool IsCompleteLine(string line) =>
            line.StartsWith("{", StringComparison.Ordinal) &&
            line.EndsWith("}",  StringComparison.Ordinal);

        // ── 简易 JSON 解析 ────────────────────────────────────────────────────

        /// <summary>
        /// 解析由 <see cref="TrackStorageEngine"/> 输出的单行 NDJSON。
        /// 任何解析异常均返回 null，上层负责丢弃。
        /// </summary>
        private static TrackEnvelope ParseLine(string line)
        {
            try
            {
                // 写入端与读取端字段名一致，JsonUtility 可安全往返序列化
                return JsonUtility.FromJson<TrackEnvelope>(line);
            }
            catch
            {
                return null;
            }
        }

        // ── 分卷排序辅助 ──────────────────────────────────────────────────────

        // 分卷文件名格式：seg_{epochMs}_{bytes}.data，按 epochMs 降序排列
        private static int CompareSegmentsByTimestampDescending(string a, string b)
        {
            long tsA = ExtractTimestampFromSegmentName(a);
            long tsB = ExtractTimestampFromSegmentName(b);
            return tsB.CompareTo(tsA);
        }

        private static long ExtractTimestampFromSegmentName(string path)
        {
            string name      = Path.GetFileNameWithoutExtension(path); // seg_1787150000_2097152
            string[] parts   = name.Split('_');
            if (parts.Length >= 2 && long.TryParse(parts[1], out long ts))
                return ts;
            return 0L;
        }
    }
}
