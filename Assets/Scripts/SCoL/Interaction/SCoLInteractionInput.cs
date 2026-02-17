using UnityEngine;

namespace SCoL.Interaction
{
    /// <summary>
    /// T09: VR migration prep.
    /// Centralizes "core interaction" input + aim-ray generation behind preprocessor switches.
    /// 
    /// FPS today (mouse/keyboard). Later, define USE_VR and implement XR controller bindings
    /// without rewriting the gameplay interaction logic.
    /// </summary>
    public static class SCoLInteractionInput
    {
        /// <summary>Primary action: harvest/assimilate/apply.</summary>
        public static bool PrimaryPressed()
        {
#if USE_VR
            // TODO (Quest 3): bind to XR controller trigger / primary action.
            // Keep gameplay code calling PrimaryPressed() only.
            return false;
#else
            return Input.GetMouseButtonDown(0);
#endif
        }

        /// <summary>Secondary action: seed/spawn/create.</summary>
        public static bool SecondaryPressed()
        {
#if USE_VR
            // TODO (Quest 3): bind to XR controller secondary action.
            return false;
#else
            return Input.GetMouseButtonDown(1);
#endif
        }

        /// <summary>
        /// Where interactions should originate from.
        /// FPS: screen center ray.
        /// VR: should become controller ray or gaze ray.
        /// </summary>
        public static bool TryGetAimRay(Camera fallbackCamera, out Ray ray)
        {
#if USE_VR
            // TODO (Quest 3): return controller ray (e.g., XRRayInteractor origin+direction).
            // Fallback to camera center if controller not available.
#endif
            var cam = fallbackCamera != null ? fallbackCamera : Camera.main;
            if (cam == null)
            {
                ray = default;
                return false;
            }

            ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            return true;
        }
    }
}
