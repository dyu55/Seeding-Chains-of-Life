using UnityEngine;

namespace SCoL.XR
{
    /// <summary>
    /// Disables the XR Simulation environment layer so grey simulation cubes/planes
    /// do not appear when running a device build on Meta Quest.
    ///
    /// Attach to any persistent GameObject in the scene (e.g. SCoLRuntime).
    /// This is a runtime guard for when the XR Simulation loader is still listed in the
    /// build's XR loader list – the simulation environment renders on Layer 30 by default.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class SCoLXRSimulationDisabler : MonoBehaviour
    {
        [Tooltip("Layer index used by XR Simulation for its environment. Default = 30.")]
        public int simulationLayer = 30;

        [Tooltip("Also strip the Simulation layer from all cameras' culling masks.")]
        public bool removeFromCamerasCullingMask = true;

        private void Awake()
        {
            // Destroy every GameObject on the simulation environment layer immediately
            DisableSimulationLayerObjects();

            // Strip layer from all cameras so even if objects appear later they won't render
            if (removeFromCamerasCullingMask)
                StripSimulationLayerFromCameras();
        }

        private void DisableSimulationLayerObjects()
        {
            // FindObjectsByType works even with DontDestroyOnLoad objects
            var all = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int layerMask = 1 << simulationLayer;
            int killed = 0;
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
                Debug.Log($"[SCoLXRSimulationDisabler] Destroyed {killed} XR Simulation environment object(s) on layer {simulationLayer}.");
        }

        private void StripSimulationLayerFromCameras()
        {
            var cams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            int layerBit = 1 << simulationLayer;
            foreach (var cam in cams)
            {
                if ((cam.cullingMask & layerBit) != 0)
                {
                    cam.cullingMask &= ~layerBit;
                    Debug.Log($"[SCoLXRSimulationDisabler] Stripped simulation layer from camera: {cam.name}");
                }
            }
        }
    }
}
