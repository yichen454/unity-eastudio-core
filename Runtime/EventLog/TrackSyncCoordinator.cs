using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace EAStudio.Core.EventLog
{
    /// <summary>
    /// 管理 <c>sync_cursor.json</c> 游标状态机。
    /// 从已冻结分卷中按批次提取游标之后的事件，供外部网络上报器消费；
    /// 收到成功 ACK 后原子更新游标，不删除源文件。
    /// </summary>
    public sealed class TrackSyncCoordinator
    {
        // ── 配置 ─────────────────────────────────────────────────────────────

        /// <summary>单次上报批次的最大事件数量。</summary>
        public const int BatchSize = 200;

        // ── 路径 ─────────────────────────────────────────────────────────────

        private readonly string _segmentsDir;
        private readonly string _cursorPath;

        // ── 游标状态 ─────────────────────────────────────────────────────────

        private SyncCursor _cursor;

        public TrackSyncCoordinator(string rootDir)
        {
            _segmentsDir = Path.Combine(rootDir, "Segments");
            _cursorPath  = Path.Combine(rootDir, "Meta", "sync_cursor.json");

            Directory.CreateDirectory(Path.Combine(rootDir, "Meta"));
            _cursor = LoadCursor();
        }

        // ── 公共 API ─────────────────────────────────────────────────────────

        /// <summary>
        /// 从游标位点之后的分卷中读取最多 <see cref="BatchSize"/> 条原始 NDJSON 行。
        /// 所有分卷均已同步时返回空列表。
        /// </summary>
        public IReadOnlyList<string> FetchNextBatch()
        {
            string[] segFiles = GetSegmentsSortedAscending();
            bool pastCursor   = string.IsNullOrEmpty(_cursor.lastAckedSegment);
            var lines         = new List<string>(BatchSize);

            foreach (string segPath in segFiles)
            {
                string segName = Path.GetFileName(segPath);

                if (!pastCursor)
                {
                    if (segName == _cursor.lastAckedSegment)
                        pastCursor = true;
                    continue;
                }

                long startOffset = 0;
                if (segName == _cursor.lastAckedSegment)
                    startOffset = _cursor.lastAckedOffset;

                ReadLinesFromSegment(segPath, startOffset, lines);
                if (lines.Count >= BatchSize) break;
            }

            return lines;
        }

        /// <summary>
        /// 将游标推进到本批次读取结束的位置。
        /// 仅在上游网络上报成功（HTTP 200）后调用，原分卷文件保留不删。
        /// </summary>
        /// <param name="lastSegmentName">本批次最后消费的分卷文件名。</param>
        /// <param name="lastByteOffset">本批次读取到的字节偏移量。</param>
        public void AcknowledgeBatch(string lastSegmentName, long lastByteOffset)
        {
            _cursor.lastAckedSegment = lastSegmentName;
            _cursor.lastAckedOffset  = lastByteOffset;
            SaveCursor();
        }

        // ── 工具方法 ─────────────────────────────────────────────────────────

        private string[] GetSegmentsSortedAscending()
        {
            if (!Directory.Exists(_segmentsDir))
                return Array.Empty<string>();

            string[] files = Directory.GetFiles(_segmentsDir, "*.data");
            Array.Sort(files, CompareSegmentsByTimestampAscending);
            return files;
        }

        private static void ReadLinesFromSegment(string path, long startOffset, List<string> output)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite,
                    bufferSize: 4096);

                if (startOffset > 0)
                    stream.Seek(startOffset, SeekOrigin.Begin);

                using var reader = new StreamReader(stream, Encoding.UTF8);
                string line;
                while ((line = reader.ReadLine()) != null && output.Count < BatchSize)
                {
                    line = line.Trim();
                    // 过滤残缺行
                    if (line.StartsWith("{", StringComparison.Ordinal) &&
                        line.EndsWith("}",  StringComparison.Ordinal))
                    {
                        output.Add(line);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TrackSyncCoordinator] 读取分卷失败 {path}: {ex.Message}");
            }
        }

        private static int CompareSegmentsByTimestampAscending(string a, string b)
        {
            long tsA = ExtractTimestamp(a);
            long tsB = ExtractTimestamp(b);
            return tsA.CompareTo(tsB);
        }

        private static long ExtractTimestamp(string path)
        {
            string name  = Path.GetFileNameWithoutExtension(path);
            string[] pts = name.Split('_');
            return pts.Length >= 2 && long.TryParse(pts[1], out long ts) ? ts : 0L;
        }

        // ── 游标持久化 ────────────────────────────────────────────────────────

        private SyncCursor LoadCursor()
        {
            if (!File.Exists(_cursorPath))
                return new SyncCursor();

            try
            {
                string json = File.ReadAllText(_cursorPath, Encoding.UTF8);
                return JsonUtility.FromJson<SyncCursor>(json) ?? new SyncCursor();
            }
            catch
            {
                return new SyncCursor();
            }
        }

        private void SaveCursor()
        {
            try
            {
                string json    = JsonUtility.ToJson(_cursor);
                string tmpPath = _cursorPath + ".tmp";
                File.WriteAllText(tmpPath, json, Encoding.UTF8);
                File.Move(tmpPath, _cursorPath); // 先写临时文件再重命名，保证写入原子性
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TrackSyncCoordinator] 游标保存失败: {ex.Message}");
            }
        }

        // ── 游标数据模型 ──────────────────────────────────────────────────────

        [Serializable]
        private sealed class SyncCursor
        {
            public string lastAckedSegment = "";
            public long   lastAckedOffset  = 0;
        }
    }
}
