using UnityEngine.Playables;

namespace EA.Timeline
{
    /// <summary>
    /// Mixer behaviour for <see cref="SceneWarmupTrack"/>.
    /// Calls <see cref="SceneWarmup.LoadScene"/> when any clip becomes active.
    /// Scene unloading is left to the clip's <c>onClipEnd</c> event or manual control.
    /// </summary>
    public class SceneWarmupMixerBehaviour : PlayableBehaviour
    {
        private SceneWarmup _binding;
        private bool _wasActive;

        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
            _binding = playerData as SceneWarmup;
            if (_binding == null) return;

            bool isActive = false;
            int inputCount = playable.GetInputCount();
            for (int i = 0; i < inputCount; i++)
            {
                if (playable.GetInputWeight(i) > 0f)
                {
                    isActive = true;
                    break;
                }
            }

            if (isActive && !_wasActive)
                _binding.LoadScene();
            else if (!isActive && _wasActive)
            {
                if (_binding.unloadOnClipEnd)
                {
#if UNITY_EDITOR
                    if (!UnityEngine.Application.isPlaying)
                        _binding.UnloadScene();
                    else
#endif
                    _binding.UnloadScene();
                }
                _binding.onClipEnd.Invoke();
            }

            _wasActive = isActive;
        }

        public override void OnPlayableDestroy(Playable playable)
        {
            if (_wasActive && _binding != null)
            {
                if (_binding.unloadOnClipEnd)
                    _binding.UnloadScene();
                _binding.onClipEnd.Invoke();
                _wasActive = false;
            }
        }
    }
}
