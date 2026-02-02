using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
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

#if ENABLE_INPUT_SYSTEM
            // Always allow keyboard tool selection in editor/simulator.
            if (Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) currentTool = Tool.Seed;
                if (Keyboard.current.digit2Key.wasPressedThisFrame) currentTool = Tool.Water;
                if (Keyboard.current.digit3Key.wasPressedThisFrame) currentTool = Tool.Fire;
            }

            // Always allow mouse click apply in editor/simulator.
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (fallbackCamera == null) fallbackCamera = Camera.main;
                if (fallbackCamera != null)
                {
                    Ray mRay = fallbackCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                    if (Physics.Raycast(mRay, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                    {
                        SpawnClickMarker(hit.point, currentTool);
                        ApplyTool(hit.point);
                    }
                    else if (logMisses)
                    {
                        Debug.LogWarning("SCoLXRInteractor: Mouse raycast hit nothing. Tip: click inside the Game view first.");
                    }
                }
            }

            // Keyboard fallback apply at center (ENTER)
            if (Keyboard.current != null && (Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.numpadEnterKey.wasPressedThisFrame))
            {
                if (fallbackCamera == null) fallbackCamera = Camera.main;
                if (fallbackCamera != null)
                {
                    Ray cRay = fallbackCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
                    if (Physics.Raycast(cRay, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                    {
                        SpawnClickMarker(hit.point, currentTool);
                        ApplyTool(hit.point);
                    }
                }
            }
#endif

            // Prefer real XR devices (UnityEngine.XR). If not available, fall back to Input System XRController.
            bool hasXR = IsAnyXRDeviceValid();
            if (!hasXR)
            {
#if ENABLE_INPUT_SYSTEM
                if (TryUpdateFromInputSystemXR())
                    return;
#endif
                return;
            }

            // ---- VR controls (UnityEngine.XR) ----
            var left = XRInputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool leftPrimary = GetBool(left, XRCommonUsages.primaryButton);
            bool leftSecondary = GetBool(left, XRCommonUsages.secondaryButton);

            if (leftPrimary && !_prevLeftPrimary) CycleTool(+1);
            if (leftSecondary && !_prevLeftSecondary) CycleTool(-1);

            _prevLeftPrimary = leftPrimary;
            _prevLeftSecondary = leftSecondary;

            Pose aimPose;
            bool hasAim = TryGetAimPose(XRNode.RightHand, out aimPose);

            Ray ray;
            if (hasAim)
                ray = new Ray(aimPose.position, aimPose.rotation * Vector3.forward);
            else if (fallbackCamera != null)
                ray = new Ray(fallbackCamera.transform.position, fallbackCamera.transform.forward);
            else
                return;

            var right = XRInputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool rightTrigger = GetBool(right, XRCommonUsages.triggerButton);

            if (rightTrigger && !_prevRightTrigger)
            {
                if (Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    SpawnClickMarker(hit.point, currentTool);
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

#if ENABLE_INPUT_SYSTEM
        private bool TryUpdateFromInputSystemXR()
        {
            var left = FindXRControllerWithUsage("LeftHand");
            var right = FindXRControllerWithUsage("RightHand");
            if (left == null || right == null)
                return false;

            bool leftPrimary = ReadButton(left, "primaryButton");
            bool leftSecondary = ReadButton(left, "secondaryButton");

            if (leftPrimary && !_prevLeftPrimary)
                CycleTool(+1);
            if (leftSecondary && !_prevLeftSecondary)
                CycleTool(-1);

            _prevLeftPrimary = leftPrimary;
            _prevLeftSecondary = leftSecondary;

            // Aim from right controller pose
            Pose aimPose;
            bool hasAim = ReadPose(right, out aimPose);

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
                return true;
            }

            if (drawDebugRay)
                Debug.DrawRay(ray.origin, ray.direction * 5f, Color.green);

            bool rightTrigger = ReadTrigger(right);
            if (rightTrigger && !_prevRightTrigger)
            {
                if (Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    ApplyTool(hit.point);
                }
                else if (logMisses)
                {
                    Debug.LogWarning("SCoLXRInteractor: Raycast hit nothing (XR Device Simulator)");
                }
            }
            _prevRightTrigger = rightTrigger;

            return true;
        }

        private static UnityEngine.InputSystem.XR.XRController FindXRControllerWithUsage(string usage)
        {
            foreach (var d in InputSystem.devices)
            {
                var c = d as UnityEngine.InputSystem.XR.XRController;
                if (c == null) continue;

                foreach (var u in c.usages)
                {
                    if (string.Equals(u.ToString(), usage, System.StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
            return null;
        }

        private bool ReadPose(UnityEngine.InputSystem.XR.XRController c, out Pose pose)
        {
            pose = default;
            if (c == null) return false;

            // XRController may or may not expose these strongly typed controls depending on backend.
            var posCtrl = c.TryGetChildControl<Vector3Control>("devicePosition");
            var rotCtrl = c.TryGetChildControl<QuaternionControl>("deviceRotation");
            if (posCtrl == null || rotCtrl == null) return false;

            Vector3 pos = posCtrl.ReadValue();
            Quaternion rot = rotCtrl.ReadValue();

            if (trackingOrigin != null)
                pose = new Pose(trackingOrigin.TransformPoint(pos), trackingOrigin.rotation * rot);
            else
                pose = new Pose(pos, rot);

            return true;
        }

        private static bool ReadButton(UnityEngine.InputSystem.XR.XRController c, string controlName)
        {
            if (c == null) return false;
            var ctrl = c.TryGetChildControl<ButtonControl>(controlName);
            return ctrl != null && ctrl.isPressed;
        }

        private static bool ReadTrigger(UnityEngine.InputSystem.XR.XRController c)
        {
            if (c == null) return false;

            // Prefer a pressed-style control if present
            var pressed = c.TryGetChildControl<ButtonControl>("triggerPressed");
            if (pressed != null) return pressed.isPressed;

            // Fallback: analog trigger axis
            var axis = c.TryGetChildControl<AxisControl>("trigger");
            return axis != null && axis.ReadValue() > 0.75f;
        }
#endif

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

        private void SpawnClickMarker(Vector3 worldPoint, Tool tool)
        {
            // Visible in Scene/Game views (Editor). Harmless in builds.
            // NOTE: Ensure marker is not parented under scaled transforms (XR Origin, grid tiles, etc.).
            var marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.name = "SCoL_ClickMarker";
            marker.hideFlags = HideFlags.DontSave;

            marker.transform.SetParent(null, worldPositionStays: true);
            marker.transform.position = worldPoint + Vector3.up * 0.05f;
            marker.transform.rotation = Quaternion.identity;
            marker.transform.localScale = Vector3.one * 0.08f;

            var col = marker.GetComponent<Collider>();
            if (col != null) Destroy(col);

            var r = marker.GetComponent<Renderer>();
            if (r != null)
            {
                r.material.color = tool switch
                {
                    Tool.Seed => Color.green,
                    Tool.Water => new Color(0.2f, 0.6f, 1f),
                    Tool.Fire => Color.red,
                    _ => Color.white
                };
            }

            Destroy(marker, 0.6f);
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
