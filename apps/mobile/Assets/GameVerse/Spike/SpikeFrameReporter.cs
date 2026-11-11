using System.Runtime.InteropServices;
using UnityEngine;

namespace GameVerse.Spike
{
    /// <summary>
    /// Prompt 0.7 spike (throwaway, deleted with ADR-0002): publishes Time.frameCount to the page every frame
    /// (window.__unityFrames, see Plugins/SpikeFrames.jslib) so /spike reports the frames Unity actually rendered.
    /// </summary>
    public sealed class SpikeFrameReporter : MonoBehaviour
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        private static extern void GV_SpikeFrame(int frame);

        private void Update()
        {
            GV_SpikeFrame(Time.frameCount);
        }
#endif
    }
}
