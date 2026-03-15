using System.Collections;
using UnityEngine;
using UnityEngine.XR;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SCoL.XR
{
    /// <summary>
    /// Attached to XR Origin (VR).
    /// 1. Ensures the XRI Input Action Asset is loaded and enabled (fixes controllers not responding).
    /// 2. Spawns XR Controller visual prefabs under Camera Offset for hand visibility.
    ///
    /// Works in both Editor and Device Builds via Resources.Load.
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

        [Header("Input Actions")]
        [Tooltip("Name of the XRI input actions asset in Resources. Leave blank to skip.")]
        public string xriInputActionsResourcesName = "XRI Default Input Actions";

        [Header("Fallback Hand Sphere")]
        public float handVisualScale = 0.06f;

        // Legacy field names kept for scene serialization compatibility
        [HideInInspector] public bool spawnHands = true;
        [HideInInspector] public bool spawnProps;
        [HideInInspector] public int propCount;
        [HideInInspector] public Vector3 propsCenter;
        [HideInInspector] public float propsRadius;

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

            // Step 1: Activate Input Actions so controllers respond
            EnsureInputActionsEnabled();

            // Step 2: Ensure turn provider
            if (GetComponent<SCoLXRTurnSetup>() == null)
                gameObject.AddComponent<SCoLXRTurnSetup>();

            // Step 3: Spawn hand visuals
            Transform cameraOffset = transform.Find("Camera Offset");
            Transform parentTransform = cameraOffset != null ? cameraOffset : transform;

            ResolveControllerPrefab(ref leftControllerPrefab, leftResourcesName);
            ResolveControllerPrefab(ref rightControllerPrefab, rightResourcesName);

            SpawnHand("Left",  XRNode.LeftHand,  leftControllerPrefab,  new Color(0.4f, 0.7f, 0.9f), parentTransform);
            SpawnHand("Right", XRNode.RightHand, rightControllerPrefab, new Color(0.9f, 0.7f, 0.4f), parentTransform);
        }

        /// <summary>
        /// Ensures the XRI Default Input Actions asset is loaded into the InputActionManager
        /// and all actions are enabled. This is the fix for "controllers not responding".
        /// </summary>
        private void EnsureInputActionsEnabled()
        {
#if ENABLE_INPUT_SYSTEM
            // Try to find the InputActionManager on this rig
            var manager = GetComponent<UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager>();

            // Look for the XRI input actions asset in Resources
            var xriActions = Resources.Load<InputActionAsset>(xriInputActionsResourcesName);

            if (xriActions == null)
            {
                // Also try the default project asset name
                xriActions = Resources.Load<InputActionAsset>("InputSystem_Actions");
            }

#if UNITY_EDITOR
            if (xriActions == null)
            {
                // Editor fallback: search via AssetDatabase
                var guids = UnityEditor.AssetDatabase.FindAssets("XRI Default Input Actions t:InputActionAsset");
                foreach (var guid in guids)
                {
                    var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    xriActions = UnityEditor.AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
                    if (xriActions != null) break;
                }
            }
#endif

            if (xriActions != null)
            {
                // Enable all action maps
                xriActions.Enable();
                Debug.Log("[SCoLXRHandsAndPropsSpawner] Enabled XRI input actions: " + xriActions.name);

                // Wire into InputActionManager if present and not already wired
                if (manager != null)
                {
                    var field = typeof(UnityEngine.XR.Interaction.Toolkit.Inputs.InputActionManager)
                        .GetField("m_ActionAssets",
                            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (field != null)
                    {
                        var existing = field.GetValue(manager) as System.Collections.Generic.List<InputActionAsset>;
                        if (existing == null)
                        {
                            existing = new System.Collections.Generic.List<InputActionAsset>();
                            field.SetValue(manager, existing);
                        }
                        if (!existing.Contains(xriActions))
                        {
                            existing.Add(xriActions);
                            Debug.Log("[SCoLXRHandsAndPropsSpawner] Wired XRI actions into InputActionManager.");
                        }
                    }
                }
            }
            else
            {
                Debug.LogWarning("[SCoLXRHandsAndPropsSpawner] Could not find XRI Input Actions asset. " +
                                 "Place 'XRI Default Input Actions.inputactions' in a Resources folder.");
            }
#endif
        }

        private static void ResolveControllerPrefab(ref GameObject prefab, string resourcesName)
        {
            if (prefab != null) return;

#if UNITY_EDITOR
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
#endif
            if (prefab == null && !string.IsNullOrEmpty(resourcesName))
                prefab = Resources.Load<GameObject>(resourcesName);
        }

        private void SpawnHand(string side, XRNode node, GameObject prefab, Color fallbackColor, Transform parent)
        {
            if (prefab != null)
            {
                var go = Instantiate(prefab, parent);
                go.name = "XR Controller " + side;
                go.transform.localPosition = Vector3.zero;
                go.transform.localRotation = Quaternion.identity;
                Debug.Log("[SCoLXRHandsAndPropsSpawner] Spawned XR Controller " + side + " from prefab.");
            }
            else
            {
                SpawnFallbackHand(side, node, fallbackColor, parent);
                string resName = (side == "Left") ? leftResourcesName : rightResourcesName;
                Debug.LogWarning("[SCoLXRHandsAndPropsSpawner] " + side +
                                 " controller prefab not found - using fallback sphere. " +
                                 "Add '" + resName + ".prefab' to a Resources folder.");
            }
        }

        private void SpawnFallbackHand(string side, XRNode node, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = side + "Hand_Fallback";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * handVisualScale;

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = new Material(r.sharedMaterial);
                r.material.color = color;
            }

            var col = go.GetComponent<SphereCollider>();
            if (col != null) col.isTrigger = true;

            var grabber = go.AddComponent<SCoLHandGrabber>();
            grabber.hand = node;
            grabber.trackingOrigin = trackingOrigin;
        }
    }
}
