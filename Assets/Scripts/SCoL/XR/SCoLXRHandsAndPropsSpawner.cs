using UnityEngine;
using UnityEngine.XR;

namespace SCoL.XR
{
    /// <summary>
    /// Spawns XR Controller prefabs from the XRI Starter Assets at runtime.
    /// Falls back to simple sphere hands if the prefabs are not found.
    /// Attach this to XR Origin (VR).
    /// </summary>
    public class SCoLXRHandsAndPropsSpawner : MonoBehaviour
    {
        public Transform trackingOrigin;

        [Header("Controller Prefabs (auto-loaded from Starter Assets)")]
        [Tooltip("If set, use this prefab for the left hand. Otherwise auto-loads from Starter Assets path.")]
        public GameObject leftControllerPrefab;
        [Tooltip("If set, use this prefab for the right hand. Otherwise auto-loads from Starter Assets path.")]
        public GameObject rightControllerPrefab;

        [Header("Fallback Hands")]
        public float handVisualScale = 0.08f;

        private bool _spawned;

        private const string LEFT_PREFAB_PATH = "Assets/Samples/XR Interaction Toolkit/3.3.1/Starter Assets/Prefabs/Controllers/XR Controller Left.prefab";
        private const string RIGHT_PREFAB_PATH = "Assets/Samples/XR Interaction Toolkit/3.3.1/Starter Assets/Prefabs/Controllers/XR Controller Right.prefab";

        private void Awake()
        {
            if (trackingOrigin == null)
                trackingOrigin = transform;
        }

        private void Start()
        {
            if (_spawned) return;
            _spawned = true;

            // Ensure the turn provider setup script is on this GameObject
            if (GetComponent<SCoLXRTurnSetup>() == null)
                gameObject.AddComponent<SCoLXRTurnSetup>();

            // Try to find Camera Offset to parent the controllers under
            Transform cameraOffset = transform.Find("Camera Offset");
            Transform parentTransform = cameraOffset != null ? cameraOffset : transform;

            // Try loading prefabs from Starter Assets if not assigned in inspector
            if (leftControllerPrefab == null || rightControllerPrefab == null)
            {
#if UNITY_EDITOR
                if (leftControllerPrefab == null)
                    leftControllerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(LEFT_PREFAB_PATH);
                if (rightControllerPrefab == null)
                    rightControllerPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(RIGHT_PREFAB_PATH);
#endif
            }

            // Spawn real controller prefabs if available
            if (leftControllerPrefab != null)
            {
                var leftGO = Instantiate(leftControllerPrefab, parentTransform);
                leftGO.name = "XR Controller Left";
                leftGO.transform.localPosition = Vector3.zero;
                leftGO.transform.localRotation = Quaternion.identity;
                Debug.Log("[SCoLXRHandsAndPropsSpawner] Spawned XR Controller Left from Starter Assets prefab.");
            }
            else
            {
                // Fallback: spawn a tracked sphere
                SpawnFallbackHand("LeftHand", XRNode.LeftHand, new Color(0.4f, 0.7f, 0.9f), parentTransform);
                Debug.LogWarning("[SCoLXRHandsAndPropsSpawner] Left controller prefab not found, using fallback sphere.");
            }

            if (rightControllerPrefab != null)
            {
                var rightGO = Instantiate(rightControllerPrefab, parentTransform);
                rightGO.name = "XR Controller Right";
                rightGO.transform.localPosition = Vector3.zero;
                rightGO.transform.localRotation = Quaternion.identity;
                Debug.Log("[SCoLXRHandsAndPropsSpawner] Spawned XR Controller Right from Starter Assets prefab.");
            }
            else
            {
                SpawnFallbackHand("RightHand", XRNode.RightHand, new Color(0.9f, 0.7f, 0.4f), parentTransform);
                Debug.LogWarning("[SCoLXRHandsAndPropsSpawner] Right controller prefab not found, using fallback sphere.");
            }
        }

        private void SpawnFallbackHand(string name, XRNode node, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * handVisualScale;

            var r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = color;

            var col = go.GetComponent<SphereCollider>();
            col.isTrigger = true;

            var grabber = go.AddComponent<SCoLHandGrabber>();
            grabber.hand = node;
            grabber.trackingOrigin = trackingOrigin;
        }
    }
}
