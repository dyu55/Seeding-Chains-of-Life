using UnityEngine;
using UnityEngine.XR;

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

            if (trackingOrigin == null && Camera.main != null)
                trackingOrigin = Camera.main.transform.parent;
        }

        private void Update()
        {
            // Input System XR (XR Device Simulator)
#if ENABLE_INPUT_SYSTEM
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
                        // Pickup first
                        var pickup = hit.collider.GetComponentInParent<SCoL.Inventory.SCoLPickup>();
                        if (pickup != null)
                        {
                            inventory.Add(pickup.type, pickup.amount);
                            Destroy(pickup.gameObject);
                        }
                        else
                        {
                            UseToolAt(hit.point);
                        }
                    }
                }
                _prevRightTrigger = rt;
            }
#endif
        }

        private void UseToolAt(Vector3 worldPoint)
        {
            if (runtime == null) return;

            switch (currentTool)
            {
                case Tool.Seed:
                    if (!inventory.TryConsume(SCoL.Inventory.SCoLItemType.Seed, 1)) return;
                    runtime.PlaceSeedAt(worldPoint);
                    break;
                case Tool.Water:
                    if (!inventory.TryConsume(SCoL.Inventory.SCoLItemType.Water, 1)) return;
                    runtime.AddWaterAt(worldPoint, waterAmount);
                    break;
                case Tool.Fire:
                    if (!inventory.TryConsume(SCoL.Inventory.SCoLItemType.Fire, 1)) return;
                    runtime.IgniteAt(worldPoint, 1.0f);
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
    }
}
