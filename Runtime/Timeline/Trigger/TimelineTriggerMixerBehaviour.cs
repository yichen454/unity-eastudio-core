using UnityEngine.Playables;

namespace EA.Timeline
{
    /// <summary>
    /// <see cref="TimelineTriggerTrack"/> 的 Mixer。clip 级事件从 <see cref="TimelineTrigger.clips"/> 按索引读取。
    /// </summary>
    public class TimelineTriggerMixerBehaviour : PlayableBehaviour
    {
        private TimelineTrigger _binding;
        private bool[] _wasInputActive;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            _binding = playerData as TimelineTrigger;
            if (_binding == null) return;

            EnsureWasInputActive(playable);
            UpdateClipStates(playable);
        }

        public override void OnBehaviourPause(Playable playable, FrameData info)
        {
            if (_binding == null || _wasInputActive == null) return;

            for (int i = 0; i < _wasInputActive.Length; i++)
            {
                if (_wasInputActive[i])
                    InvokeExit(i);
                _wasInputActive[i] = false;
            }
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (_binding == null) return;

            EnsureWasInputActive(playable);
            for (int i = 0; i < _wasInputActive.Length; i++)
            {
                if (_wasInputActive[i])
                    InvokeExit(i);
            }
        }

        private void EnsureWasInputActive(Playable playable)
        {
            int inputCount = playable.GetInputCount();
            if (_wasInputActive != null && _wasInputActive.Length == inputCount)
                return;

            if (_wasInputActive != null)
            {
                for (int i = 0; i < _wasInputActive.Length; i++)
                {
                    if (_wasInputActive[i])
                        InvokeExit(i);
                }
            }

            _wasInputActive = new bool[inputCount];
            for (int i = 0; i < inputCount; i++)
                _wasInputActive[i] = playable.GetInputWeight(i) > 0f;
        }

        private void UpdateClipStates(Playable playable)
        {
            int inputCount = playable.GetInputCount();
            for (int i = 0; i < inputCount; i++)
            {
                bool active = playable.GetInputWeight(i) > 0f;
                var cfg = _binding.GetClipConfig(i);

                if (cfg != null)
                {
                    if (active && !_wasInputActive[i])
                        cfg.onEnter.Invoke();
                    else if (!active && _wasInputActive[i])
                        InvokeExit(i);

                    if (active && cfg.tickWhileActive)
                        cfg.onTick.Invoke();
                }

                _wasInputActive[i] = active;
            }
        }

        private void InvokeExit(int clipIndex)
        {
            _binding.GetClipConfig(clipIndex)?.onExit.Invoke();
        }
    }
}
