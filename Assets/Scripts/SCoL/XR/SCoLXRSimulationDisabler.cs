using System.Collections;
using UnityEngine;

namespace SCoL.XR
{
    /// <summary>
    /// Destroys XR Device Simulator / AR Foundation Simulation environment objects
    /// (the grey cubes that appear on Quest builds when SimulationLoader is still active).
    ///
    /// Runs continuously for the first several seconds after Start so it catches
    /// objects that are spawned asynchronously by the simulation subsystem.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SCoLXRSimulationDisabler : MonoBehaviour
    {
        [Tooltip("Layer index used by XR Simulation for its environment objects. Default = 30.")]
        public int simulationLayer = 30;

        [Tooltip("Strip the Simulation layer from all cameras culling masks.")]
        public bool removeFromCamerasCullingMask = true;

        [Tooltip("How many seconds to keep checking for late-spawned simulation objects.")]
        public float watchDurationSeconds = 6f;

        [Tooltip("How often (seconds) to re-check while watching.")]
        public float checkIntervalSeconds = 0.25f;

        private void Start()
        {
            // Immediate pass
            RunCleanup();
            // Repeat for a few seconds to catch async-spawned objects
            StartCoroutine(WatchRoutine());
        }

        private IEnumerator WatchRoutine()
        {
            float elapsed = 0f;
            while (elapsed < watchDurationSeconds)
            {
                yield return new WaitForSeconds(checkIntervalSeconds);
                elapsed += checkIntervalSeconds;
                RunCleanup();
            }
        }

        private void RunCleanup()
        {
            int layerBit = 1 << simulationLayer;
            int killed = 0;

            // Destroy objects on the simulation layer
            var all = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var go in all)
            {
                if (go == null || go == gameObject) continue;
                if (go.layer == simulationLayer)
                {
                    Destroy(go);
                    killed++;
                }
            }

            if (killed > 0)
                Debug.Log("[SCoLXRSimulationDisabler] Destroyed " + killed +
                          " XR Simulation object(s) on layer " + simulationLayer + ".");

            // Strip layer from camera culling masks
            if (removeFromCamerasCullingMask)
            {
                var cams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (var cam in cams)
                {
                    if ((cam.cullingMask & layerBit) != 0)
                    {
                        cam.cullingMask &= ~layerBit;
                    }
                }
            }
        }
    }
}
