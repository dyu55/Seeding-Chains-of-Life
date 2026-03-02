using UnityEngine;

namespace SCoL.InputLayer
{
    public enum AimMode
    {
        Auto,
        CameraCenter,
        XrRightController,
        XrGaze
    }

    /// <summary>
    /// Provides interaction ray independent of FPS/VR mode.
    /// </summary>
    [DisallowMultipleComponent]
    public class InteractionAimProvider : MonoBehaviour, IAimProvider
    {
        public AimMode mode = AimMode.Auto;
        [Tooltip("Optional FPS camera source. If null, fallback camera or Camera.main is used.")]
        public Camera fpsCamera;
        [Tooltip("XR right-hand ray origin (future Quest path).")]
        public Transform xrRightAimOrigin;
        [Tooltip("XR gaze fallback origin (usually HMD center eye anchor).")]
        public Transform xrGazeOrigin;
        public Vector2 fpsViewportPoint = new Vector2(0.5f, 0.5f);

        public Transform CurrentAimOrigin { get; private set; }

        public bool TryGetAimRay(Camera fallbackCamera, out Ray ray)
        {
            var origin = ResolveAimOrigin();
            if (origin != null)
            {
                CurrentAimOrigin = origin;
                ray = new Ray(origin.position, origin.forward);
                return true;
            }

            Camera cam = fpsCamera != null ? fpsCamera : fallbackCamera;
            if (cam == null) cam = Camera.main;
            if (cam == null)
            {
                CurrentAimOrigin = null;
                ray = default;
                return false;
            }

            CurrentAimOrigin = cam.transform;
            ray = cam.ViewportPointToRay(new Vector3(fpsViewportPoint.x, fpsViewportPoint.y, 0f));
            return true;
        }

        private Transform ResolveAimOrigin()
        {
            switch (mode)
            {
                case AimMode.XrRightController:
                    return IsActive(xrRightAimOrigin) ? xrRightAimOrigin : null;
                case AimMode.XrGaze:
                    return IsActive(xrGazeOrigin) ? xrGazeOrigin : null;
                case AimMode.CameraCenter:
                    return null;
                case AimMode.Auto:
                default:
                    if (IsActive(xrRightAimOrigin)) return xrRightAimOrigin;
                    if (IsActive(xrGazeOrigin)) return xrGazeOrigin;
                    return null;
            }
        }

        private static bool IsActive(Transform t)
        {
            return t != null && t.gameObject.activeInHierarchy;
        }
    }
}

