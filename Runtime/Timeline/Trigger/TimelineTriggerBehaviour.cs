using UnityEngine;
using UnityEngine.Playables;

namespace EAStudio.Core.Timeline
{
    /// <summary>
    /// Per-clip PlayableBehaviour. Events are resolved by stable clip ID on the bound <see cref="TimelineTrigger"/>.
    /// </summary>
    public class TimelineTriggerBehaviour : PlayableBehaviour
    {
        internal string clipId;
        internal string label;

        private TimelineTrigger _binding;
        private bool _wasActive;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            var binding = playerData as TimelineTrigger;
            if (binding != null)
                _binding = binding;

            if (_binding == null) return;

            bool isActive = info.effectiveWeight > 0f;
            var config = _binding.GetClipConfig(clipId);

            if (config != null)
            {
                if (isActive && !_wasActive)
                    config.onEnter.Invoke();
                else if (!isActive && _wasActive)
                    InvokeExit(config);

                if (isActive && config.tickWhileActive)
                    config.onTick.Invoke();
            }

            _wasActive = isActive;
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (_binding == null || !_wasActive) return;

            InvokeExit(_binding.GetClipConfig(clipId));
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (_binding == null || !_wasActive) return;

            InvokeExit(_binding.GetClipConfig(clipId));
        }

        private void InvokeExit(TimelineTrigger.ClipConfig config)
        {
            config?.onExit.Invoke();
            _wasActive = false;
        }
    }
}
