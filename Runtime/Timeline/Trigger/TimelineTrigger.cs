using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace EAStudio.Core.Timeline
{
    /// <summary>
    /// Timeline 触发器绑定目标。绑定到 <see cref="TimelineTriggerTrack"/>。
    /// <para>所有场景对象引用均在此配置，clip 按轨道上从左到右的顺序（0 起）对应 <see cref="clips"/> 列表。</para>
    /// </summary>
    [AddComponentMenu("EAStudio/Timeline/Timeline Trigger")]
    public class TimelineTrigger : MonoBehaviour
    {
        [Serializable]
        public class ClipConfig
        {
            [Tooltip("对应 Timeline clip 的稳定 ID，由编辑器自动维护。")]
            public string clipId;

            [Tooltip("备注，由 Timeline clip 的 label 刷新同步，用于标识轨道上对应的 clip。")]
            public string label;

            [Tooltip("播放头进入该 clip 时触发。")]
            public UnityEvent onEnter = new UnityEvent();

            [Tooltip("播放头离开该 clip 时触发。")]
            public UnityEvent onExit = new UnityEvent();

            [Tooltip("该 clip 激活期间每帧触发。关闭可节省性能。")]
            public bool tickWhileActive;

            [Tooltip("该 clip 激活期间每帧触发（需开启 tickWhileActive）。")]
            public UnityEvent onTick = new UnityEvent();
        }

        [Tooltip("按轨道上 clip 顺序（从左到右，从 0 开始）配置每个 clip 的事件。支持场景对象拖拽。")]
        public List<ClipConfig> clips = new List<ClipConfig>();

        private readonly Dictionary<string, ClipConfig> _clipLookup = new Dictionary<string, ClipConfig>();
        private bool _isLookupDirty = true;

        private void OnValidate()
        {
            MarkLookupDirty();
        }

        /// <summary>返回指定索引的 ClipConfig，越界返回 null。</summary>
        public ClipConfig GetClipConfig(int index)
            => index >= 0 && index < clips.Count ? clips[index] : null;

        /// <summary>按稳定 clip ID 查找配置，找不到时返回 null。</summary>
        public ClipConfig GetClipConfig(string clipId)
        {
            if (string.IsNullOrWhiteSpace(clipId)) return null;

            EnsureLookup();
            return _clipLookup.TryGetValue(clipId, out var config) ? config : null;
        }

        /// <summary>显式标记内部查找缓存失效。</summary>
        public void MarkLookupDirty()
        {
            _isLookupDirty = true;
        }

        private void EnsureLookup()
        {
            if (!_isLookupDirty) return;

            _clipLookup.Clear();
            foreach (var config in clips)
            {
                if (config == null || string.IsNullOrWhiteSpace(config.clipId)) continue;
                _clipLookup[config.clipId] = config;
            }

            _isLookupDirty = false;
        }
    }
}
