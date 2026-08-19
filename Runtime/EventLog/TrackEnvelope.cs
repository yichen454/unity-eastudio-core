using System;
using UnityEngine;

namespace EAStudio.Core.EventLog
{
    /// <summary>
    /// 单条埋点事件记录，序列化为磁盘上的一行 NDJSON。
    /// </summary>
    [Serializable]
    public sealed class TrackEnvelope
    {
        /// <summary>全局唯一事件 ID（UUID v4）。</summary>
        public string id;

        /// <summary>会话内单调递增的序列号。</summary>
        public long seq;

        /// <summary>事件创建时的 Unix 时间戳，毫秒，UTC。</summary>
        public long ts;

        /// <summary>逻辑事件名，例如 "button_click"。</summary>
        public string @event;

        /// <summary>业务域标识，用于多产品数据隔离。</summary>
        public string bizCode;

        /// <summary>由调用方传入的不透明用户标识。</summary>
        public string userId;

        /// <summary>会话标识，格式：s_{会话起始毫秒}_{随机4位十六进制}。</summary>
        public string sessionId;

        /// <summary>应用元数据（包名、版本、构建号）。</summary>
        public AppInfo app;

        /// <summary>设备元数据（OS、OS 版本、机型）。</summary>
        public DeviceInfo device;

        /// <summary>
        /// 调用方附加的扩展键值对，序列化为 JSON 字符串存储。
        /// 以原始字符串形式保留，避免每次事件都产生嵌套分配。
        /// </summary>
        public string props;

        /// <summary>为 true 时，事件入队后立即同步落盘，不经过批量缓冲。</summary>
        [NonSerialized]
        public bool isCritical;

        // ── 进程级静态环境缓存（只采集一次）──────────────────────────────────

        private static AppInfo _cachedApp;
        private static DeviceInfo _cachedDevice;

        /// <summary>
        /// 返回进程生命周期内只构建一次的 <see cref="AppInfo"/>。
        /// </summary>
        public static AppInfo GetAppInfo()
        {
            if (_cachedApp == null)
            {
                _cachedApp = new AppInfo
                {
                    pkg   = Application.identifier,
                    ver   = Application.version,
                    build = 0, // Unity 未暴露构建号，由 AnalyticsManager 在初始化时覆盖
                };
            }
            return _cachedApp;
        }

        /// <summary>
        /// 返回进程生命周期内只构建一次的 <see cref="DeviceInfo"/>。
        /// </summary>
        public static DeviceInfo GetDeviceInfo()
        {
            if (_cachedDevice == null)
            {
                _cachedDevice = new DeviceInfo
                {
                    os     = SystemInfo.operatingSystemFamily.ToString(),
                    os_ver = SystemInfo.operatingSystem,
                    model  = SystemInfo.deviceModel,
                };
            }
            return _cachedDevice;
        }
    }

    [Serializable]
    public sealed class AppInfo
    {
        public string pkg;
        public string ver;
        public int    build;
    }

    [Serializable]
    public sealed class DeviceInfo
    {
        public string os;
        public string os_ver;
        public string model;
    }
}
