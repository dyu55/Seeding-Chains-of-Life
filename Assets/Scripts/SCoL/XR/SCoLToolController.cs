using UnityEngine;
using UnityEngine.XR;
using Unity.XR.CoreUtils;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
#endif

namespace SCoL.XR
{
    /// <summary>
    /// Controller-driven tool selection + use, backed by inventory.
    ///
    /// Design (reasonable defaults):
    /// - Left Primary Button: next tool
    /// - Left Secondary Button: previous tool
    /// - Right Trigger: use (either pick up an item if aiming at pickup, or apply tool to grid)
    ///
    /// Inventory rules:
    /// - Picking up a green/blue/red pickup increments Seed/Water/Fire count.
    /// - Using a tool consumes 1 from inventory; if zero, nothing happens.
    ///
    /// Works with XR Device Simulator (Input System) by reading XRSimulatedController/XRController controls.
    /// </summary>
    public class SCoLToolController : MonoBehaviour
    {
        public enum Tool
        {
            Seed,
            Water,
            Fire
        }

        [Header("Refs")]
        public SCoL.SCoLRuntime runtime;
        public SCoL.Inventory.SCoLInventory inventory;
        public Transform trackingOrigin;
        public Camera fallbackCamera;

        [Header("Ray")]
        public float rayLength = 50f;
        public LayerMask hitLayers = ~0;

        [Header("Tool")]
        public Tool currentTool = Tool.Seed;
        public float waterAmount = 0.35f;

        private bool _prevLeftPrimary;
        private bool _prevLeftSecondary;
        private bool _prevRightTrigger;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
            if (inventory == null)
                inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
            if (inventory == null)
                inventory = new GameObject("SCoLInventory").AddComponent<SCoL.Inventory.SCoLInventory>();

            if (fallbackCamera == null)
                fallbackCamera = Camera.main;

            if (trackingOrigin == null)
            {
                // Prefer the XROrigin transform (world-space origin for tracked poses)
                var xrOrigin = FindFirstObjectByType<XROrigin>();
                if (xrOrigin != null)
                    trackingOrigin = xrOrigin.transform;
                else if (Camera.main != null)
                    trackingOrigin = Camera.main.transform.root;
            }

            // Safety: if the LayerMask is accidentally set to Nothing in the inspector,
            // rays will never hit and VR will feel "dead".
            if (hitLayers.value == 0)
                hitLayers = ~0;
        }

        private void Update()
        {
#if ENABLE_INPUT_SYSTEM
            // Always allow keyboard tool switching (works even if simulator input isn't configured)
            if (Keyboard.current != null)
            {
                if (Keyboard.current.digit1Key.wasPressedThisFrame) currentTool = Tool.Seed;
                if (Keyboard.current.digit2Key.wasPressedThisFrame) currentTool = Tool.Water;
                if (Keyboard.current.digit3Key.wasPressedThisFrame) currentTool = Tool.Fire;
            }
#endif

            // --- Auto-pickup: if your pointer is over a pickup, collect immediately (no click).
            if (TryGetToolAimRay(out var autoRay))
            {
                if (Physics.Raycast(autoRay, out var autoHit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    if (TryPickup(autoHit))
                        return; // consumed this frame
                }
            }

#if ENABLE_INPUT_SYSTEM
            // Mouse: Left click = use tool/apply at hit point (if not a pickup)
            if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            {
                if (fallbackCamera == null) fallbackCamera = Camera.main;
                if (fallbackCamera != null)
                {
                    Ray ray = fallbackCamera.ScreenPointToRay(Mouse.current.position.ReadValue());
                    if (Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                    {
                        HandleHit(hit);
                    }
                }
                return;
            }

            // Input System XR (XR Device Simulator): controller buttons
            var left = FindXRControllerWithUsage("LeftHand");
            var right = FindXRControllerWithUsage("RightHand");

            if (left != null)
            {
                bool lp = ReadButton(left, "primaryButton");
                bool ls = ReadButton(left, "secondaryButton");

                if (lp && !_prevLeftPrimary) CycleTool(+1);
                if (ls && !_prevLeftSecondary) CycleTool(-1);

                _prevLeftPrimary = lp;
                _prevLeftSecondary = ls;
            }

            if (right != null)
            {
                bool rt = ReadTrigger(right);
                if (rt && !_prevRightTrigger)
                {
                    if (TryGetAimRay(right, out var ray) && Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                    {
                        HandleHit(hit);
                    }
                }
                _prevRightTrigger = rt;
                return;
            }
#endif

            // UnityEngine.XR fallback (Quest Link / OpenXR): controller buttons
            var leftXR = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var rightXR = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

            // Tool cycling: allow either controller face buttons.
            // Typical mapping: Right A/B = primary/secondary; Left X/Y = primary/secondary.
            bool leftPrimaryXR = GetBool(leftXR, UnityEngine.XR.CommonUsages.primaryButton);
            bool leftSecondaryXR = GetBool(leftXR, UnityEngine.XR.CommonUsages.secondaryButton);
            bool rightPrimaryXR = GetBool(rightXR, UnityEngine.XR.CommonUsages.primaryButton);
            bool rightSecondaryXR = GetBool(rightXR, UnityEngine.XR.CommonUsages.secondaryButton);

            bool next = leftPrimaryXR || rightPrimaryXR;
            bool prev = leftSecondaryXR || rightSecondaryXR;

            if (next && !_prevLeftPrimary) CycleTool(+1);
            if (prev && !_prevLeftSecondary) CycleTool(-1);

            _prevLeftPrimary = next;
            _prevLeftSecondary = prev;

            bool rightTriggerXR = GetBool(rightXR, UnityEngine.XR.CommonUsages.triggerButton) || GetTrigger(rightXR);
            if (rightTriggerXR && !_prevRightTrigger)
            {
                if (TryGetToolAimRay(out var ray) && Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                    HandleHit(hit);
            }
            _prevRightTrigger = rightTriggerXR;
        }

        private bool TryPickup(RaycastHit hit)
        {
            var pickup = hit.collider.GetComponentInParent<SCoL.Inventory.SCoLPickup>();
            if (pickup == null) return false;

            inventory.Add(pickup.type, pickup.amount);
            Destroy(pickup.gameObject);
            return true;
        }

        private void HandleHit(RaycastHit hit)
        {
            // Pickup first
            if (TryPickup(hit))
                return;

            UseToolAt(hit.point);
        }

        private void UseToolAt(Vector3 worldPoint)
        {
            if (runtime == null || runtime.Grid == null) return;
            if (!runtime.TryWorldToCell(worldPoint, out int x, out int y)) return;

            var cell = runtime.Grid.Get(x, y);

            switch (currentTool)
            {
                case Tool.Seed:
                    // Allow planting on empty OR burnt/scorched tiles.
                    if (cell.PlantStage != SCoL.PlantStage.Empty && cell.PlantStage != SCoL.PlantStage.Burnt)
                        return;
                    if (!inventory.TryConsume(SCoL.Inventory.SCoLItemType.Seed, 1)) return;
                    runtime.PlaceSeedAt(worldPoint);
                    break;

                case Tool.Water:
                    // Water always provides visible darkening feedback.
                    if (!inventory.TryConsume(SCoL.Inventory.SCoLItemType.Water, 1)) return;
                    runtime.AddWaterAt(worldPoint, waterAmount);
                    break;

                case Tool.Fire:
                    if (!inventory.TryConsume(SCoL.Inventory.SCoLItemType.Fire, 1)) return;
                    runtime.IgniteAt(worldPoint, 1.0f);
                    break;
            }

            runtime.ForceRender();
        }

        private void CycleTool(int dir)
        {
            int t = (int)currentTool;
            int max = System.Enum.GetValues(typeof(Tool)).Length;
            t = (t + dir) % max;
            if (t < 0) t += max;
            currentTool = (Tool)t;
        }

        /// <summary>
        /// Returns an aim ray that matches what the tool controller uses.
        /// </summary>
        public bool TryGetToolAimRay(out Ray ray)
        {
#if ENABLE_INPUT_SYSTEM
            // Prefer mouse in editor
            if (Mouse.current != null)
            {
                var cam = fallbackCamera != null ? fallbackCamera : Camera.main;
                if (cam != null)
                {
                    ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                    return true;
                }
            }

            // Try right simulated controller pose
            var right = FindXRControllerWithUsage("RightHand");
            if (right != null && TryGetAimRay(right, out ray))
                return true;
#endif

            // UnityEngine.XR pose (Quest Link / OpenXR)
            if (TryGetXRNodeRay(XRNode.RightHand, out ray))
                return true;

            var c = fallbackCamera != null ? fallbackCamera : Camera.main;
            if (c != null)
            {
                ray = new Ray(c.transform.position, c.transform.forward);
                return true;
            }

            ray = default;
            return false;
        }

#if ENABLE_INPUT_SYSTEM
        private bool TryGetAimRay(UnityEngine.InputSystem.XR.XRController right, out Ray ray)
        {
            // Prefer controller pose
            if (ReadPose(right, out var pose))
            {
                ray = new Ray(pose.position, pose.rotation * Vector3.forward);
                return true;
            }

            // Fallback: camera forward
            if (fallbackCamera != null)
            {
                ray = new Ray(fallbackCamera.transform.position, fallbackCamera.transform.forward);
                return true;
            }

            ray = default;
            return false;
        }

        private bool ReadPose(UnityEngine.InputSystem.XR.XRController c, out Pose pose)
        {
            pose = default;
            if (c == null) return false;
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

        private static bool ReadButton(UnityEngine.InputSystem.XR.XRController c, string controlName)
        {
            if (c == null) return false;
            var ctrl = c.TryGetChildControl<ButtonControl>(controlName);
            return ctrl != null && ctrl.isPressed;
        }

        private static bool ReadTrigger(UnityEngine.InputSystem.XR.XRController c)
        {
            if (c == null) return false;
            var pressed = c.TryGetChildControl<ButtonControl>("triggerButton");
            if (pressed != null) return pressed.isPressed;

            var pressed2 = c.TryGetChildControl<ButtonControl>("triggerPressed");
            if (pressed2 != null) return pressed2.isPressed;

            var axis = c.TryGetChildControl<AxisControl>("trigger");
            return axis != null && axis.ReadValue() > 0.75f;
        }
#endif

        private bool TryGetXRNodeRay(XRNode node, out Ray ray)
        {
            ray = default;
            var dev = InputDevices.GetDeviceAtXRNode(node);
            if (!dev.isValid) return false;

            if (!dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.devicePosition, out var localPos)) return false;
            if (!dev.TryGetFeatureValue(UnityEngine.XR.CommonUsages.deviceRotation, out var localRot)) return false;

            Vector3 pos = localPos;
            Quaternion rot = localRot;
            if (trackingOrigin != null)
            {
                pos = trackingOrigin.TransformPoint(localPos);
                rot = trackingOrigin.rotation * localRot;
            }

            ray = new Ray(pos, rot * Vector3.forward);
            return true;
        }

        private static bool GetBool(UnityEngine.XR.InputDevice device, InputFeatureUsage<bool> usage)
        {
            if (!device.isValid) return false;
            return device.TryGetFeatureValue(usage, out bool v) && v;
        }

        private static bool GetTrigger(UnityEngine.XR.InputDevice device, float threshold = 0.75f)
        {
            if (!device.isValid) return false;
            // Some runtimes don't expose triggerButton reliably; use analog trigger axis.
            if (device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.trigger, out float v))
                return v >= threshold;
            return false;
        }
    }
}
