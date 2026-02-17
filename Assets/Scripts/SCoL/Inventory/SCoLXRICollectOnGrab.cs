using UnityEngine;

#if USE_VR
using UnityEngine.XR.Interaction.Toolkit;
#endif

namespace SCoL.Inventory
{
    /// <summary>
    /// Makes a pickup work with XR Interaction Toolkit:
    /// when the object is grabbed/selected, it is converted into inventory and destroyed.
    ///
    /// This is used because the Starter Assets rig + XR Device Simulator already supports
    /// selecting/grabbing XR interactables reliably.
    /// </summary>
    [RequireComponent(typeof(SCoLPickup))]
    [DisallowMultipleComponent]
    public class SCoLXRICollectOnGrab : MonoBehaviour
    {
        public SCoLInventory inventory;

#if USE_VR
        private UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable _interactable;
#endif
        private SCoLPickup _pickup;

        private void Awake()
        {
            // FPS-only build: do NOT add XRGrabInteractable.
            // (Pickup collection will be handled by the FPS raycast interaction path.)
#if !USE_VR
            enabled = false;
            return;
#else
            _pickup = GetComponent<SCoLPickup>();
            if (inventory == null)
                inventory = FindFirstObjectByType<SCoLInventory>();

            // Ensure an interactable exists
            _interactable = GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>();
            if (_interactable == null)
            {
                _interactable = gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
            }

            _interactable.selectEntered.AddListener(OnSelectEntered);
#endif
        }

#if USE_VR
        private void OnDestroy()
        {
            if (_interactable != null)
                _interactable.selectEntered.RemoveListener(OnSelectEntered);
        }

        private void OnSelectEntered(SelectEnterEventArgs args)
        {
            if (inventory == null)
                inventory = FindFirstObjectByType<SCoLInventory>();
            if (inventory == null) return;

            inventory.Add(_pickup.type, _pickup.amount);
            Destroy(gameObject);
        }
#endif
    }
}
