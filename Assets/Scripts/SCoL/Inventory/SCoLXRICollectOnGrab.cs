using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;

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

        private XRBaseInteractable _interactable;
        private SCoLPickup _pickup;

        private void Awake()
        {
            _pickup = GetComponent<SCoLPickup>();
            if (inventory == null)
                inventory = FindFirstObjectByType<SCoLInventory>();

            // Ensure an interactable exists
            _interactable = GetComponent<XRBaseInteractable>();
            if (_interactable == null)
            {
                // Grab interactable gives best default behavior with Starter Assets
                _interactable = gameObject.AddComponent<XRGrabInteractable>();
            }

            _interactable.selectEntered.AddListener(OnSelectEntered);
        }

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
    }
}
