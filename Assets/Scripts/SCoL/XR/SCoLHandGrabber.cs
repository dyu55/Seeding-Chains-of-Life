using UnityEngine;
using UnityEngine.XR;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
#endif

using XRInputDevice = UnityEngine.XR.InputDevice;
using XRInputDevices = UnityEngine.XR.InputDevices;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

namespace SCoL.XR
{
    /// <summary>
    /// Tracks an XRNode (Left/Right hand) and allows grabbing nearby SCoLGrabbable objects
    /// using grip button.
    ///
    /// This avoids needing XR Interaction Toolkit input action assets.
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLHandGrabber : MonoBehaviour
    {
        public XRNode hand = XRNode.LeftHand;
        public Transform trackingOrigin;

        [Header("Grab")]
        public float grabRadius = 0.12f;
        public LayerMask grabbableLayers = ~0;

        private XRInputDevice _device;
        private readonly Collider[] _overlaps = new Collider[16];

        private SCoLGrabbable _held;
        private Rigidbody _heldRb;

        private bool _prevGrip;

        private void Awake()
        {
            if (trackingOrigin == null && Camera.main != null)
                trackingOrigin = Camera.main.transform.parent;
        }

        private void Update()
        {
            // Prefer UnityEngine.XR devices. If not valid (XR Device Simulator), use Input System XRController.
            _device = XRInputDevices.GetDeviceAtXRNode(hand);

            bool hasXR = _device.isValid;

            if (hasXR)
            {
                UpdatePoseXR(_device);
                bool grip = GetBool(_device, XRCommonUsages.gripButton);
                HandleGrip(grip);
                return;
            }

#if ENABLE_INPUT_SYSTEM
            var ctrl = FindXRControllerWithUsage(hand == XRNode.LeftHand ? "LeftHand" : "RightHand");
            if (ctrl != null)
            {
                UpdatePoseInputSystem(ctrl);
                bool grip = ReadBool(ctrl, ctrl.gripButton);
                HandleGrip(grip);
                return;
            }
#endif
        }

        private void HandleGrip(bool grip)
        {
            if (grip && !_prevGrip)
                TryGrab();
            if (!grip && _prevGrip)
                Release();
            _prevGrip = grip;
        }

        private void UpdatePoseXR(XRInputDevice dev)
        {
            if (!dev.TryGetFeatureValue(XRCommonUsages.devicePosition, out var localPos)) return;
            if (!dev.TryGetFeatureValue(XRCommonUsages.deviceRotation, out var localRot)) return;

            if (trackingOrigin != null)
            {
                transform.position = trackingOrigin.TransformPoint(localPos);
                transform.rotation = trackingOrigin.rotation * localRot;
            }
            else
            {
                transform.position = localPos;
                transform.rotation = localRot;
            }
        }

#if ENABLE_INPUT_SYSTEM
        private static UnityEngine.InputSystem.XR.XRController FindXRControllerWithUsage(string usage)
        {
            foreach (var d in InputSystem.devices)
            {
                var c = d as UnityEngine.InputSystem.XR.XRController;
                if (c == null) continue;

                // Avoid InternedString dependency; ReadOnlyArray doesn't have LINQ Contains by default.
                foreach (var u in c.usages)
                {
                    if (string.Equals(u.ToString(), usage, System.StringComparison.OrdinalIgnoreCase))
                        return c;
                }
            }
            return null;
        }

        private void UpdatePoseInputSystem(UnityEngine.InputSystem.XR.XRController c)
        {
            Vector3 pos = c.devicePosition.ReadValue();
            Quaternion rot = c.deviceRotation.ReadValue();

            if (trackingOrigin != null)
            {
                transform.position = trackingOrigin.TransformPoint(pos);
                transform.rotation = trackingOrigin.rotation * rot;
            }
            else
            {
                transform.position = pos;
                transform.rotation = rot;
            }
        }

        private static bool ReadBool(UnityEngine.InputSystem.XR.XRController c, ButtonControl b)
        {
            if (c == null || b == null) return false;
            return b.isPressed;
        }
#endif

        private void TryGrab()
        {
            if (_held != null) return;

            int n = Physics.OverlapSphereNonAlloc(transform.position, grabRadius, _overlaps, grabbableLayers, QueryTriggerInteraction.Collide);
            float best = float.PositiveInfinity;
            SCoLGrabbable bestG = null;
            Rigidbody bestRb = null;

            for (int i = 0; i < n; i++)
            {
                var col = _overlaps[i];
                if (col == null) continue;

                var g = col.GetComponentInParent<SCoLGrabbable>();
                if (g == null || g.isGrabbed) continue;

                float d = (g.transform.position - transform.position).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    bestG = g;
                    bestRb = g.GetComponent<Rigidbody>();
                }
            }

            if (bestG == null) return;

            _held = bestG;
            _held.isGrabbed = true;
            _heldRb = bestRb;

            if (_heldRb != null)
            {
                _heldRb.isKinematic = true;
                _heldRb.linearVelocity = Vector3.zero;
                _heldRb.angularVelocity = Vector3.zero;
            }

            // parent to hand
            Transform gp = _held.grabPoint != null ? _held.grabPoint : _held.transform;
            _held.transform.SetParent(transform, worldPositionStays: true);
            _held.transform.position = transform.position;
            _held.transform.rotation = transform.rotation;
        }

        private void Release()
        {
            if (_held == null) return;

            _held.transform.SetParent(null, worldPositionStays: true);
            _held.isGrabbed = false;

            if (_heldRb != null)
            {
                _heldRb.isKinematic = false;
            }

            _held = null;
            _heldRb = null;
        }

        private static bool GetBool(XRInputDevice device, InputFeatureUsage<bool> usage)
        {
            if (!device.isValid) return false;
            return device.TryGetFeatureValue(usage, out bool v) && v;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.35f);
            Gizmos.DrawSphere(transform.position, grabRadius);
        }
    }
}
