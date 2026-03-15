using UnityEngine;
using UnityEngine.XR;

namespace SCoL.XR
{
    /// <summary>
    /// Spawns XR Controller prefabs at runtime.
    /// Works in both Editor and Device Builds:
    ///   - First tries inspector-assigned prefabs
    ///   - Then tries Resources.Load("XR Controller Left" / "XR Controller Right")
    ///   - Falls back to tracked spheres if nothing found
    /// Attach to XR Origin (VR).
    /// </summary>
    public class SCoLXRHandsAndPropsSpawner : MonoBehaviour
    {
        public Transform trackingOrigin;

        [Header("Controller Prefabs")]
        [Tooltip("Assign the XR Controller Left prefab directly (drag from Project).")]
        public GameObject leftControllerPrefab;
        [Tooltip("Assign the XR Controller Right prefab directly (drag from Project).")]
        public GameObject rightControllerPrefab;

        [Header("Resources Fallback Names")]
        [Tooltip("Name (without extension) of the left controller prefab inside any Resources folder.")]
        public string leftResourcesName = "XR Controller Left";
        [Tooltip("Name (without extension) of the right controller prefab inside any Resources folder.")]
        public string rightResourcesName = "XR Controller Right";

        [Header("Fallback Hand Sphere")]
        public float handVisualScale = 0.06f;

        private bool _spawned;

        private void Awake()
        {
            if (trackingOrigin == null)
                trackingOrigin = transform;
        }

        private void Start()
        {
            if (_spawned) return;
            _spawned = true;

            // Ensure turn provider is on the rig
            if (GetComponent<SCoLXRTurnSetup>() == null)
                gameObject.AddComponent<SCoLXRTurnSetup>();

            // Find the "Camera Offset" child to parent hands under (standard XRI hierarchy)
            Transform cameraOffset = transform.Find("Camera Offset");
            Transform parentTransform = cameraOffset != null ? cameraOffset : transform;

            // Resolve prefabs: Inspector → Resources folder → fallback sphere
            ResolveControllerPrefab(ref leftControllerPrefab, leftResourcesName);
            ResolveControllerPrefab(ref rightControllerPrefab, rightResourcesName);

            SpawnHand("Left", XRNode.LeftHand, leftControllerPrefab, new Color(0.4f, 0.7f, 0.9f), parentTransform);
            SpawnHand("Right", XRNode.RightHand, rightControllerPrefab, new Color(0.9f, 0.7f, 0.4f), parentTransform);
        }

        private static void ResolveControllerPrefab(ref GameObject prefab, string resourcesName)
        {
            if (prefab != null) return;

#if UNITY_EDITOR
            // In Editor: also try AssetDatabase for convenience
            if (prefab == null)
            {
                var guids = UnityEditor.AssetDatabase.FindAssets(resourcesName + " t:Prefab");
                foreach (var guid in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(resourcesName + ".prefab"))
                    {
                        prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
                        if (prefab != null) return;
                    }
                }
            }
#endif
            // Always try Resources.Load (works in builds too)
            if (prefab == null && !string.IsNullOrEmpty(resourcesName))
                prefab = Resources.Load<GameObject>(resourcesName);
        }

        private void SpawnHand(string side, XRNode node, GameObject prefab, Color fallbackColor, Transform parent)
        {
            if (prefab != null)
            {
                var go = Instantiate(prefab, parent);
                go.name = $"XR Controller {side}";
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                Debug.Log($"[SCoLXRHandsAndPropsSpawner] Spawned XR Controller {side} from prefab.");
            }
            else
            {
                SpawnFallbackHand(side, node, fallbackColor, parent);
                Debug.LogWarning($"[SCoLXRHandsAndPropsSpawner] {side} controller prefab not found – using fallback sphere. " +
                                 $"Add '{side == "Left" ? leftResourcesName : rightResourcesName}.prefab' to a Resources folder.");
            }
        }

        private void SpawnFallbackHand(string side, XRNode node, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = $"{side}Hand_Fallback";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * handVisualScale;

            // Assign a visible color
            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                // Use a new material instance so we don't share across fallbacks
                r.material = new Material(r.sharedMaterial);
                r.material.color = color;
            }

            // No physics collision needed for the hand visual
            var col = go.GetComponent<SphereCollider>();
            if (col != null) col.isTrigger = true;

            // Add the grabber for interaction; trackingOrigin is the XR origin transform
            var grabber = go.AddComponent<SCoLHandGrabber>();
            grabber.hand = node;
            grabber.trackingOrigin = trackingOrigin;
        }
    }
}
