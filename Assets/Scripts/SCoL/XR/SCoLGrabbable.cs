using UnityEngine;

namespace SCoL.XR
{
    /// <summary>
    /// Marker component for simple custom grabbing (no XR Interaction Toolkit actions required).
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLGrabbable : MonoBehaviour
    {
        public Transform grabPoint;

        [HideInInspector] public bool isGrabbed;

        private void Reset()
        {
            grabPoint = transform;
        }
    }
}
