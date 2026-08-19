using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace EAStudio.Core.EventLog
{
    /// <summary>
    /// 写入内核：主线程无锁入队（纳秒级开销），后台 Worker 批量落盘到 NDJSON WAL 文件，
    /// 并在文件达到大小或时间阈值时自动滚动为只读分卷。
    /// </summary>
    public sealed class TrackStorageEngine : IDisposable
    {
        // ── 配置常量 ─────────────────────────────────────────────────────────

        /// <summary>WAL 文件超过此字节数后滚动为新分卷（2 MB）。</summary>
        public const long SegmentMaxBytes = 2 * 1024 * 1024;

        /// <summary>WAL 文件超过此秒数后强制滚动，不论文件大小（10 分钟）。</summary>
        public const double SegmentMaxAgeSeconds = 600;

        /// <summary>队列压力较低时 Worker 的休眠间隔（毫秒）。</summary>
        private const int FlushIntervalMs = 1000;

        /// <summary>队列积压达到此数量时 Worker 提前唤醒批量落盘。</summary>
        private const int FlushBatchSize = 100;

        // ── 目录布局 ─────────────────────────────────────────────────────────

        private readonly string _rootDir;
        private readonly string _activeDir;
        private readonly string _segmentsDir;

        private string ActiveWalPath => Path.Combine(_activeDir, "current.wal");

        // ── 并发原语 ─────────────────────────────────────────────────────────

        /// <summary>主线程写入、Worker 消费的无锁事件队列。</summary>
        private readonly ConcurrentQueue<TrackEnvelope> _queue = new ConcurrentQueue<TrackEnvelope>();

        /// <summary>最近事件的内存环形缓冲，供三级联合读取器的 Tier 1 使用。</summary>
        private readonly List<TrackEnvelope> _memoryBuffer = new List<TrackEnvelope>();
        private const int MemoryBufferCapacity = 200;
        private readonly object _memoryLock = new object();

        private readonly ManualResetEventSlim _wakeWorker = new ManualResetEventSlim(false);
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Thread _workerThread;

        // ── WAL 状态 ─────────────────────────────────────────────────────────

        private StreamWriter _walWriter;
        private long _walBytesWritten;
        private DateTime _walOpenedAt;
        private readonly object _walLock = new object();

        // ── 序列号计数器 ──────────────────────────────────────────────────────

        private long _seqCounter;

        // ── 生命周期 ─────────────────────────────────────────────────────────

        public TrackStorageEngine(string rootDir)
        {
            _rootDir     = rootDir;
            _activeDir   = Path.Combine(rootDir, "Active");
            _segmentsDir = Path.Combine(rootDir, "Segments");

            Directory.CreateDirectory(_activeDir);
            Directory.CreateDirectory(_segmentsDir);

            OpenWal();
            StartWorker();
        }

        // ── 公共 API ─────────────────────────────────────────────────────────

        /// <summary>
        /// 将事件推入无锁队列等待后台落盘。
        /// 若 <see cref="TrackEnvelope.isCritical"/> 为 true，则在返回前同步调用
        /// <see cref="FlushImmediate"/> 确保数据已写入磁盘。
        /// 线程安全，主线程调用开销为纳秒级。
        /// </summary>
        public void Enqueue(TrackEnvelope envelope)
        {
            envelope.seq = Interlocked.Increment(ref _seqCounter);
            _queue.Enqueue(envelope);
            _wakeWorker.Set();

            if (envelope.isCritical)
                FlushImmediate();
        }

        /// <summary>
        /// 同步排空队列并将 WAL Writer 缓冲刷入磁盘。
        /// 任意线程均可安全调用；内部加锁防止并发竞争。
        /// </summary>
        public void FlushImmediate()
        {
            lock (_walLock)
            {
                DrainQueueIntoWal();
                _walWriter?.Flush();
            }
        }

        /// <summary>
        /// 返回内存环形缓冲的快照（最新事件在前）。
        /// </summary>
        public IReadOnlyList<TrackEnvelope> GetMemorySnapshot()
        {
            lock (_memoryLock)
            {
                var snapshot = new TrackEnvelope[_memoryBuffer.Count];
                _memoryBuffer.CopyTo(snapshot);
                Array.Reverse(snapshot); // 最新在前
                return snapshot;
            }
        }

        public void Dispose()
        {
            _cts.Cancel();
            _wakeWorker.Set();
            _workerThread?.Join(TimeSpan.FromSeconds(5));
            lock (_walLock)
            {
                _walWriter?.Flush();
                _walWriter?.Dispose();
                _walWriter = null;
            }
            _cts.Dispose();
            _wakeWorker.Dispose();
        }

        // ── 后台 Worker ───────────────────────────────────────────────────────

        private void StartWorker()
        {
            _workerThread = new Thread(WorkerLoop)
            {
                IsBackground = true,
                Name         = "TrackStorageEngine.Worker",
            };
            _workerThread.Start();
        }

        private void WorkerLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                _wakeWorker.Wait(FlushIntervalMs, _cts.Token);
                _wakeWorker.Reset();

                lock (_walLock)
                {
                    DrainQueueIntoWal();
                    CheckAndRollSegment();
                }
            }

            // 关闭时最终排空队列
            lock (_walLock)
            {
                DrainQueueIntoWal();
                _walWriter?.Flush();
            }
        }

        /// <summary>调用前必须持有 <see cref="_walLock"/>。</summary>
        private void DrainQueueIntoWal()
        {
            if (_walWriter == null) return;

            int count = 0;
            while (_queue.TryDequeue(out TrackEnvelope ev))
            {
                string line = ToNdjsonLine(ev);
                _walWriter.WriteLine(line);
                _walBytesWritten += Encoding.UTF8.GetByteCount(line) + 1; // +1 为换行符

                AppendToMemoryBuffer(ev);

                if (++count >= FlushBatchSize)
                {
                    _walWriter.Flush();
                    count = 0;
                }
            }

            if (count > 0)
                _walWriter.Flush();
        }

        /// <summary>调用前必须持有 <see cref="_walLock"/>。</summary>
        private void CheckAndRollSegment()
        {
            if (_walBytesWritten < SegmentMaxBytes &&
                (DateTime.UtcNow - _walOpenedAt).TotalSeconds < SegmentMaxAgeSeconds)
                return;

            _walWriter?.Flush();
            _walWriter?.Dispose();
            _walWriter = null;

            RenameWalToSegment();
            OpenWal();
        }

        private void RenameWalToSegment()
        {
            string walPath = ActiveWalPath;
            if (!File.Exists(walPath)) return;

            long epochMs   = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long fileBytes = new FileInfo(walPath).Length;
            string segName = $"seg_{epochMs}_{fileBytes}.data";
            string segPath = Path.Combine(_segmentsDir, segName);

            File.Move(walPath, segPath);
        }

        private void OpenWal()
        {
            // 以 FileShare.ReadWrite 打开，确保 UnifiedTrackReader 可并发读取活跃 WAL
            var stream = new FileStream(
                ActiveWalPath,
                FileMode.Append,
                FileAccess.Write,
                FileShare.ReadWrite,
                bufferSize: 4096,
                useAsync: false);

            _walWriter       = new StreamWriter(stream, Encoding.UTF8, 4096, leaveOpen: false);
            _walBytesWritten = File.Exists(ActiveWalPath) ? new FileInfo(ActiveWalPath).Length : 0;
            _walOpenedAt     = DateTime.UtcNow;
        }

        // ── 工具方法 ─────────────────────────────────────────────────────────

        private void AppendToMemoryBuffer(TrackEnvelope ev)
        {
            lock (_memoryLock)
            {
                if (_memoryBuffer.Count >= MemoryBufferCapacity)
                    _memoryBuffer.RemoveAt(0); // 淘汰最旧条目
                _memoryBuffer.Add(ev);
            }
        }

        /// <summary>
        /// 手写轻量序列化器，避免 Unity JsonUtility 在热路径上的反射与分配开销。
        /// </summary>
        private static string ToNdjsonLine(TrackEnvelope ev)
        {
            var sb = new StringBuilder(256);
            sb.Append('{');
            AppendStr(sb, "id",         ev.id);        sb.Append(',');
            AppendNum(sb, "seq",        ev.seq);       sb.Append(',');
            AppendNum(sb, "ts",         ev.ts);        sb.Append(',');
            AppendStr(sb, "event",      ev.@event);    sb.Append(',');
            AppendStr(sb, "bizCode",    ev.bizCode);   sb.Append(',');
            AppendStr(sb, "user_id",    ev.userId);    sb.Append(',');
            AppendStr(sb, "session_id", ev.sessionId); sb.Append(',');

            // app 嵌套对象
            sb.Append("\"app\":{");
            AppendStr(sb, "pkg",   ev.app?.pkg);            sb.Append(',');
            AppendStr(sb, "ver",   ev.app?.ver);            sb.Append(',');
            AppendNum(sb, "build", ev.app?.build ?? 0);
            sb.Append("},");

            // device 嵌套对象
            sb.Append("\"device\":{");
            AppendStr(sb, "os",     ev.device?.os);     sb.Append(',');
            AppendStr(sb, "os_ver", ev.device?.os_ver); sb.Append(',');
            AppendStr(sb, "model",  ev.device?.model);
            sb.Append("},");

            AppendStr(sb, "props", ev.props ?? "{}");
            sb.Append('}');
            return sb.ToString();
        }

        private static void AppendStr(StringBuilder sb, string key, string value)
        {
            sb.Append('"'); sb.Append(key); sb.Append("\":\"");
            if (value != null)
                sb.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")
                               .Replace("\n", "\\n").Replace("\r", "\\r"));
            sb.Append('"');
        }

        private static void AppendNum(StringBuilder sb, string key, long value)
        {
            sb.Append('"'); sb.Append(key); sb.Append("\":"); sb.Append(value);
        }

        private static void AppendNum(StringBuilder sb, string key, int value) =>
            AppendNum(sb, key, (long)value);
    }
}
