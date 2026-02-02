using UnityEngine;
using UnityEngine.XR;

namespace SCoL.XR
{
    /// <summary>
    /// Minimal, dependency-light VR interaction for Quest/OpenXR.
    ///
    /// - Ray comes from RightHand controller pose (fallback: camera forward)
    /// - Tool mode cycles with Left primary/secondary buttons
    /// - Apply tool with Right trigger button
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

        // edge detection
        private bool _prevLeftPrimary;
        private bool _prevLeftSecondary;
        private bool _prevRightTrigger;

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

            // Left hand: cycle tool
            var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            bool leftPrimary = GetBool(left, CommonUsages.primaryButton);
            bool leftSecondary = GetBool(left, CommonUsages.secondaryButton);

            if (leftPrimary && !_prevLeftPrimary)
                CycleTool(+1);
            if (leftSecondary && !_prevLeftSecondary)
                CycleTool(-1);

            _prevLeftPrimary = leftPrimary;
            _prevLeftSecondary = leftSecondary;

            // Right hand: aim + apply
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

            var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool rightTrigger = GetBool(right, CommonUsages.triggerButton);

            if (rightTrigger && !_prevRightTrigger)
            {
                if (Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    ApplyTool(hit.point);

                    // light haptic on right controller
                    TryHaptic(right, 0.25f, 0.05f);
                }
            }

            _prevRightTrigger = rightTrigger;
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
            var dev = InputDevices.GetDeviceAtXRNode(node);

            if (!dev.isValid)
                return false;

            if (!dev.TryGetFeatureValue(CommonUsages.devicePosition, out var localPos))
                return false;
            if (!dev.TryGetFeatureValue(CommonUsages.deviceRotation, out var localRot))
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

        private static bool GetBool(InputDevice device, InputFeatureUsage<bool> usage)
        {
            if (!device.isValid) return false;
            return device.TryGetFeatureValue(usage, out bool v) && v;
        }

        private static void TryHaptic(InputDevice device, float amplitude, float duration)
        {
            if (!device.isValid) return;
            if (!device.TryGetHapticCapabilities(out var caps)) return;
            if (!caps.supportsImpulse) return;
            device.SendHapticImpulse(0u, Mathf.Clamp01(amplitude), duration);
        }
    }
}
