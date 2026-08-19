using System;
using System.Collections.Generic;
using UnityEngine;

namespace EAStudio.Core.EventLog
{
    /// <summary>
    /// MonoBehaviour 单例，负责：组装静态设备/应用上下文、注册生命周期防护钩子，
    /// 并向外暴露 <see cref="Track"/> 埋点公共 API。
    /// 将此组件挂载到常驻根 GameObject 即可启用整个埋点系统。
    /// </summary>
    public sealed class AnalyticsManager : MonoBehaviour
    {
        // ── Inspector 配置 ────────────────────────────────────────────────────

        [Tooltip("业务域标识，区分多产品数据")]
        [SerializeField] private string bizCode = "default";

        [Tooltip("可选：覆盖 Application.version 中无法获取的 build number")]
        [SerializeField] private int buildNumber;

        [Tooltip("可选：覆盖用户 ID（运行时可通过 SetUserId() 修改）")]
        [SerializeField] private string defaultUserId = "anonymous";

        // ── 单例 ─────────────────────────────────────────────────────────────

        private static AnalyticsManager _instance;

        /// <summary>返回当前活跃的单例，未初始化时为 null。</summary>
        public static AnalyticsManager Instance => _instance;

        // ── 内部状态 ─────────────────────────────────────────────────────────

        private TrackStorageEngine _storage;
        private string _sessionId;
        private string _userId;

        // ── Unity 生命周期 ────────────────────────────────────────────────────

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        private void Initialize()
        {
            string rootDir = System.IO.Path.Combine(Application.persistentDataPath, "AnalyticsData");
            _storage   = new TrackStorageEngine(rootDir);
            _sessionId = BuildSessionId();
            _userId    = defaultUserId;

            // 将 Inspector 配置的 buildNumber 写入静态环境缓存
            TrackEnvelope.GetAppInfo().build = buildNumber;

            // 崩溃防护：注册未捕获异常处理器
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        }

        private void OnApplicationPause(bool isPaused)
        {
            // 切后台时立即同步落盘，防止系统回收进程导致数据丢失
            if (isPaused)
                _storage?.FlushImmediate();
        }

        private void OnApplicationQuit()
        {
            AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
            _storage?.FlushImmediate();
            _storage?.Dispose();
            _storage = null;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                _storage?.Dispose();
                _storage  = null;
                _instance = null;
            }
        }

        // ── 公共 API ─────────────────────────────────────────────────────────

        /// <summary>
        /// 记录一条埋点事件到当前会话。
        /// </summary>
        /// <param name="eventName">逻辑事件名，例如 "level_start"。</param>
        /// <param name="props">可选扩展属性，以 JSON 对象字符串形式传入。</param>
        /// <param name="isCritical">
        /// 为 true 时在返回前同步落盘（谨慎使用，会短暂阻塞调用线程）。
        /// </param>
        public static void Track(string eventName, string props = null, bool isCritical = false)
        {
            if (_instance == null || _instance._storage == null)
            {
                Debug.LogWarning("[AnalyticsManager] 未初始化，事件已丢弃：" + eventName);
                return;
            }
            _instance.EnqueueEvent(eventName, props, isCritical);
        }

        /// <summary>运行时覆盖当前用户标识。</summary>
        public static void SetUserId(string userId)
        {
            if (_instance != null)
                _instance._userId = userId ?? "anonymous";
        }

        /// <summary>返回内存环形缓冲的只读快照。</summary>
        public static IReadOnlyList<TrackEnvelope> GetMemorySnapshot() =>
            _instance?._storage?.GetMemorySnapshot() ?? Array.Empty<TrackEnvelope>();

        // ── 内部工具方法 ──────────────────────────────────────────────────────

        private void EnqueueEvent(string eventName, string props, bool isCritical)
        {
            var ev = new TrackEnvelope
            {
                id         = Guid.NewGuid().ToString("D"),
                ts         = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                @event     = eventName,
                bizCode    = bizCode,
                userId     = _userId,
                sessionId  = _sessionId,
                app        = TrackEnvelope.GetAppInfo(),
                device     = TrackEnvelope.GetDeviceInfo(),
                props      = props ?? "{}",
                isCritical = isCritical,
            };
            _storage.Enqueue(ev);
        }

        /// <summary>构建本次会话 ID：s_{起始毫秒}_{随机4位十六进制}。</summary>
        private static string BuildSessionId()
        {
            long ms     = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string rand = UnityEngine.Random.Range(0x1000, 0xFFFF).ToString("x4");
            return $"s_{ms}_{rand}";
        }

        /// <summary>未捕获异常时触发紧急落盘。</summary>
        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs args)
        {
            _instance?._storage?.FlushImmediate();
        }
    }
}
