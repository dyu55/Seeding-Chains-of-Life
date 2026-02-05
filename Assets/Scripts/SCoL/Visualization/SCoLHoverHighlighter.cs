using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace SCoL.Visualization
{
    /// <summary>
    /// Highlights the tile under the current aim ray by temporarily boosting its brightness.
    /// Intended for XR ray aiming so users know which cell will be affected.
    /// </summary>
    public class SCoLHoverHighlighter : MonoBehaviour
    {
        public SCoL.SCoLRuntime runtime;

        [Header("Aim")]
        public float rayLength = 50f;
        public LayerMask hitLayers = ~0;

        [Header("Highlight")]
        [Range(1.0f, 2.0f)] public float brightness = 1.6f;

        private int _lastX = -1;
        private int _lastY = -1;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
        }

        private void Update()
        {
            if (runtime == null || runtime.Grid == null) return;

            // Use the same aim ray as the tool controller (matches what will be affected)
            var tool = FindFirstObjectByType<SCoL.XR.SCoLToolController>();
            if (tool != null)
            {
                if (!tool.TryGetToolAimRay(out var toolRay))
                    return;

                // Use the tool controller's ray settings so highlight matches actual interaction.
                float len = tool.rayLength;
                var layers = tool.hitLayers.value != 0 ? tool.hitLayers : hitLayers;
                if (len <= 0f) len = rayLength;

                if (Physics.Raycast(toolRay, out var hit1, len, layers, QueryTriggerInteraction.Ignore))
                {
                    if (runtime.TryWorldToCell(hit1.point, out int x, out int y))
                    {
                        if (x != _lastX || y != _lastY)
                        {
                            _lastX = x;
                            _lastY = y;
                            runtime.ForceRender();
                        }
                    }
                }

                return;
            }

            // XR fallback: aim from right-hand controller pose (prefer pointer pose)
            var right = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.RightHand);
            if (right.isValid)
            {
                var pointerPosUsage = new UnityEngine.XR.InputFeatureUsage<Vector3>("PointerPosition");
                var pointerRotUsage = new UnityEngine.XR.InputFeatureUsage<Quaternion>("PointerRotation");

                if ((right.TryGetFeatureValue(pointerPosUsage, out var p) || right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out p)) &&
                    (right.TryGetFeatureValue(pointerRotUsage, out var q) || right.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out q)))
                {
                    var rayXR = new Ray(p, q * Vector3.forward);
                    if (Physics.Raycast(rayXR, out var hitXR, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                    {
                        if (runtime.TryWorldToCell(hitXR.point, out int x, out int y))
                        {
                            if (x != _lastX || y != _lastY)
                            {
                                _lastX = x;
                                _lastY = y;
                                runtime.ForceRender();
                            }
                        }
                    }
                    return;
                }

                return;
            }

            if (!TryGetAimRay(out var aimRay))
                return;

            if (Physics.Raycast(aimRay, out var hit2, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
            {
                if (runtime.TryWorldToCell(hit2.point, out int x, out int y))
                {
                    if (x != _lastX || y != _lastY)
                    {
                        _lastX = x;
                        _lastY = y;
                        runtime.ForceRender();
                    }
                }
            }
        }

        private bool TryGetAimRay(out Ray ray)
        {
            // Prefer mouse position in editor.
#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null)
            {
                var cam = Camera.main;
                if (cam != null)
                {
                    ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                    return true;
                }
            }
#endif
            var c = Camera.main;
            if (c != null)
            {
                ray = new Ray(c.transform.position, c.transform.forward);
                return true;
            }

            ray = default;
            return false;
        }

        public bool IsHoveredCell(int x, int y) => x == _lastX && y == _lastY;
        public float HoverBrightness => brightness;
    }
}
