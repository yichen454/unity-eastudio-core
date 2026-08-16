using UnityEngine.Playables;

namespace EAStudio.Core.Timeline
{
    /// <summary>
    /// Compatibility mixer for <see cref="TimelineTriggerTrack"/>.
    /// Runtime clip events are handled by <see cref="TimelineTriggerBehaviour"/>.
    /// </summary>
    public class TimelineTriggerMixerBehaviour : PlayableBehaviour
    {
        public override void ProcessFrame(Playable playable, FrameData info, object playerData)
        {
        }
    }
}
