using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// Avoid name collision between UnityEngine.XR.InputDevice and UnityEngine.InputSystem.InputDevice
using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace SCoL.XR
{
    /// <summary>
    /// Minimal, dependency-light VR interaction for Quest/OpenXR.
    ///
    /// VR:
    /// - Aim ray from RightHand controller pose (fallback: camera forward)
    /// - Apply tool with Right trigger button
    /// - Cycle tools with Left primary/secondary buttons
    ///
    /// Editor fallback (no headset):
    /// - 1/2/3 select tool (Seed/Water/Fire)
    /// - Left mouse click to apply at screen center ray
    ///
    /// Tools:
    /// - Seed: PlaceSeedAt(hit)
    /// - Water: AddWaterAt(hit, waterAmount)
    /// - Fire: IgniteAt(hit, fireFuel)
    /// </summary>
    public class SCoLXRInteractor : MonoBehaviour
    {
        public enum Tool
        {
            Seed,
            Water,
            Fire
        }

        [Header("Refs")]
        [Tooltip("Optional. If empty, will auto-find SCoLRuntime in scene.")]
        public SCoL.SCoLRuntime runtime;

        [Tooltip("Used to convert XR device local pose to world. Usually the 'XR Origin (VR)' transform.")]
        public Transform trackingOrigin;

        [Tooltip("Fallback for aiming when controller pose is unavailable.")]
        public Camera fallbackCamera;

        [Header("Ray")]
        public float rayLength = 50f;
        public LayerMask hitLayers = ~0;
        public bool drawDebugRay = true;

        [Header("Tool")]
        public Tool currentTool = Tool.Seed;
        [Range(0.01f, 1f)] public float waterAmount = 0.25f;
        [Range(0.01f, 1f)] public float fireFuel = 0.8f;

        [Header("Debug")]
        public bool logMisses = false;

        // edge detection
        private bool _prevLeftPrimary;
        private bool _prevLeftSecondary;
        private bool _prevRightTrigger;
        private bool _prevMouse;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();

            if (trackingOrigin == null)
            {
                // best-effort: XR Origin is often the parent of the main camera
                if (Camera.main != null) trackingOrigin = Camera.main.transform.parent;
            }

            if (fallbackCamera == null)
                fallbackCamera = Camera.main;
        }

        private void Update()
        {
            if (runtime == null)
            {
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                if (runtime == null) return;
            }

            // ---- Editor fallback controls (no XR device) ----
            if (!IsAnyXRDeviceValid())
            {
                HandleEditorFallback();
                return;
            }

            // ---- VR controls ----
            var left = XRInputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool leftPrimary = GetBool(left, XRCommonUsages.primaryButton);
            bool leftSecondary = GetBool(left, XRCommonUsages.secondaryButton);

            if (leftPrimary && !_prevLeftPrimary)
                CycleTool(+1);
            if (leftSecondary && !_prevLeftSecondary)
                CycleTool(-1);

            _prevLeftPrimary = leftPrimary;
            _prevLeftSecondary = leftSecondary;

            // Aim
            Pose aimPose;
            bool hasAim = TryGetAimPose(XRNode.RightHand, out aimPose);

            Ray ray;
            if (hasAim)
            {
                ray = new Ray(aimPose.position, aimPose.rotation * Vector3.forward);
            }
            else if (fallbackCamera != null)
            {
                ray = new Ray(fallbackCamera.transform.position, fallbackCamera.transform.forward);
            }
            else
            {
                return;
            }

            if (drawDebugRay)
                Debug.DrawRay(ray.origin, ray.direction * 5f, Color.cyan);

            var right = XRInputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool rightTrigger = GetBool(right, XRCommonUsages.triggerButton);

            if (rightTrigger && !_prevRightTrigger)
            {
                if (Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    ApplyTool(hit.point);
                    TryHaptic(right, 0.25f, 0.05f);
                }
                else if (logMisses)
                {
                    Debug.LogWarning("SCoLXRInteractor: Raycast hit nothing (VR)");
                }
            }

            _prevRightTrigger = rightTrigger;
        }

        private void HandleEditorFallback()
        {
#if ENABLE_INPUT_SYSTEM
            // Keyboard tool select
            if (Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) currentTool = Tool.Seed;
                if (Keyboard.current.digit2Key.wasPressedThisFrame) currentTool = Tool.Water;
                if (Keyboard.current.digit3Key.wasPressedThisFrame) currentTool = Tool.Fire;
            }

            // Mouse click apply
            bool mousePressed = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
            if (mousePressed)
            {
                if (fallbackCamera == null) fallbackCamera = Camera.main;
                if (fallbackCamera == null) return;

                Vector2 pos = Mouse.current.position.ReadValue();
                Ray ray = fallbackCamera.ScreenPointToRay(pos);
                if (drawDebugRay)
                    Debug.DrawRay(ray.origin, ray.direction * 5f, Color.magenta);

                if (Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    if (logMisses)
                        Debug.Log($"SCoLXRInteractor: Hit '{hit.collider.gameObject.name}' at {hit.point} (Editor)");
                    ApplyTool(hit.point);
                }
                else if (logMisses)
                {
                    Debug.LogWarning("SCoLXRInteractor: Raycast hit nothing (Editor). Tip: click on a collider (SCoL_Ground) and ensure it isn't disabled.");
                }
            }
#else
            // Legacy Input Manager fallback (only if project uses it)
            if (Input.GetKeyDown(KeyCode.Alpha1)) currentTool = Tool.Seed;
            if (Input.GetKeyDown(KeyCode.Alpha2)) currentTool = Tool.Water;
            if (Input.GetKeyDown(KeyCode.Alpha3)) currentTool = Tool.Fire;

            bool mouse = Input.GetMouseButtonDown(0);
            if (mouse)
            {
                if (fallbackCamera == null) fallbackCamera = Camera.main;
                if (fallbackCamera == null) return;

                Ray ray = fallbackCamera.ScreenPointToRay(Input.mousePosition);
                if (drawDebugRay)
                    Debug.DrawRay(ray.origin, ray.direction * 5f, Color.magenta);

                if (Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    if (logMisses)
                        Debug.Log($"SCoLXRInteractor: Hit '{hit.collider.gameObject.name}' at {hit.point} (Editor)");
                    ApplyTool(hit.point);
                }
                else if (logMisses)
                {
                    Debug.LogWarning("SCoLXRInteractor: Raycast hit nothing (Editor). Tip: click on a collider (SCoL_Ground) and ensure it isn't disabled.");
                }
            }
#endif
        }

        private bool IsAnyXRDeviceValid()
        {
            // If either controller is valid, assume we are in XR runtime.
            var l = XRInputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var r = XRInputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return l.isValid || r.isValid;
        }

        private void ApplyTool(Vector3 worldPoint)
        {
            switch (currentTool)
            {
                case Tool.Seed:
                    runtime.PlaceSeedAt(worldPoint);
                    break;
                case Tool.Water:
                    runtime.AddWaterAt(worldPoint, waterAmount);
                    break;
                case Tool.Fire:
                    runtime.IgniteAt(worldPoint, fireFuel);
                    break;
            }
        }

        private void CycleTool(int dir)
        {
            int t = (int)currentTool;
            int max = System.Enum.GetValues(typeof(Tool)).Length;
            t = (t + dir) % max;
            if (t < 0) t += max;
            currentTool = (Tool)t;

            Debug.Log($"SCoL Tool: {currentTool} (Left primary=next, secondary=prev)");
        }

        private bool TryGetAimPose(XRNode node, out Pose pose)
        {
            pose = default;
            var dev = XRInputDevices.GetDeviceAtXRNode(node);

            if (!dev.isValid)
                return false;

            if (!dev.TryGetFeatureValue(XRCommonUsages.devicePosition, out var localPos))
                return false;
            if (!dev.TryGetFeatureValue(XRCommonUsages.deviceRotation, out var localRot))
                return false;

            if (trackingOrigin != null)
            {
                pose = new Pose(trackingOrigin.TransformPoint(localPos), trackingOrigin.rotation * localRot);
            }
            else
            {
                pose = new Pose(localPos, localRot);
            }

            return true;
        }

        private static bool GetBool(XRInputDevice device, InputFeatureUsage<bool> usage)
        {
            if (!device.isValid) return false;
            return device.TryGetFeatureValue(usage, out bool v) && v;
        }

        private static void TryHaptic(XRInputDevice device, float amplitude, float duration)
        {
            if (!device.isValid) return;
            if (!device.TryGetHapticCapabilities(out var caps)) return;
            if (!caps.supportsImpulse) return;
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
        }
    }
}
