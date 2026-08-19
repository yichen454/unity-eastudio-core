using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEngine;

namespace EAStudio.Core.EventLog
{
    /// <summary>
    /// 磁盘保留策略执行器：
    /// <list type="bullet">
    ///   <item>将超过 <see cref="SegmentRetentionDays"/> 天的分卷归档到按月命名的 ZIP 文件。</item>
    ///   <item>当 <c>AnalyticsData/</c> 总占用超过 <see cref="MaxTotalBytes"/> 时，按 FIFO 删除最旧 ZIP。</item>
    /// </list>
    /// 建议在后台线程调用 <see cref="RunMaintenance"/>，避免阻塞主线程。
    /// </summary>
    public sealed class TrackRetentionManager
    {
        // ── 配置 ─────────────────────────────────────────────────────────────

        /// <summary>分卷保留天数，超过后归档为 ZIP（默认 365 天）。</summary>
        public static int SegmentRetentionDays = 365;

        /// <summary>AnalyticsData 目录总占用上限，超过时 FIFO 清理最旧 ZIP（默认 200 MB）。</summary>
        public static long MaxTotalBytes = 200L * 1024 * 1024;

        // ── 路径 ─────────────────────────────────────────────────────────────

        private readonly string _rootDir;
        private readonly string _segmentsDir;
        private readonly string _archivesDir;

        public TrackRetentionManager(string rootDir)
        {
            _rootDir     = rootDir;
            _segmentsDir = Path.Combine(rootDir, "Segments");
            _archivesDir = Path.Combine(rootDir, "Archives");

            Directory.CreateDirectory(_archivesDir);
        }

        // ── 公共 API ─────────────────────────────────────────────────────────

        /// <summary>
        /// 执行完整维护周期（归档过期分卷 + 容量熔断），同步执行。
        /// 建议在后台线程调用。
        /// </summary>
        public void RunMaintenance()
        {
            ArchiveOldSegments();
            EnforceQuota();
        }

        // ── 归档过期分卷 ──────────────────────────────────────────────────────

        private void ArchiveOldSegments()
        {
            if (!Directory.Exists(_segmentsDir)) return;

            DateTime cutoff = DateTime.UtcNow.AddDays(-SegmentRetentionDays);

            foreach (string segPath in Directory.GetFiles(_segmentsDir, "*.data"))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(segPath) > cutoff) continue;

                    // 按分卷创建月份命名归档包，同月的分卷追加到同一 ZIP
                    DateTime created   = File.GetCreationTimeUtc(segPath);
                    string archiveName = $"{created:yyyy_MM}_archive.zip";
                    string archivePath = Path.Combine(_archivesDir, archiveName);

                    using (var zip = ZipFile.Open(archivePath, ZipArchiveMode.Update))
                    {
                        string entryName = Path.GetFileName(segPath);
                        if (zip.GetEntry(entryName) == null) // 避免重复归档
                            zip.CreateEntryFromFile(segPath, entryName, System.IO.Compression.CompressionLevel.Optimal);
                    }

                    File.Delete(segPath);
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TrackRetentionManager] 归档分卷失败 {segPath}: {ex.Message}");
                }
            }
        }

        // ── 容量熔断 ──────────────────────────────────────────────────────────

        private void EnforceQuota()
        {
            long totalBytes = GetDirectorySize(_rootDir);
            if (totalBytes <= MaxTotalBytes) return;

            if (!Directory.Exists(_archivesDir)) return;

            // 按创建时间升序，优先删除最旧的归档包
            string[] archives = Directory.GetFiles(_archivesDir, "*.zip")
                .OrderBy(f => File.GetCreationTimeUtc(f))
                .ToArray();

            foreach (string archivePath in archives)
            {
                if (totalBytes <= MaxTotalBytes) break;

                try
                {
                    long size = new FileInfo(archivePath).Length;
                    File.Delete(archivePath);
                    totalBytes -= size;
                    Debug.Log($"[TrackRetentionManager] 已清理归档：{Path.GetFileName(archivePath)} ({size / 1024} KB)");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[TrackRetentionManager] 删除归档失败 {archivePath}: {ex.Message}");
                }
            }
        }

        // ── 工具方法 ─────────────────────────────────────────────────────────

        private static long GetDirectorySize(string path)
        {
            if (!Directory.Exists(path)) return 0L;

            long total = 0;
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try { total += new FileInfo(file).Length; }
                catch { /* 忽略被锁文件 */ }
            }
            return total;
        }
    }
}
