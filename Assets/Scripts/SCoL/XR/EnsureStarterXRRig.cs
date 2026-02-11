using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.XR
{
    /// <summary>
    /// Editor-only helper: when testing in Play Mode (especially with XR Device Simulator),
    /// ensure the XRI Starter Assets rig exists in the scene.
    ///
    /// This avoids manual scene wiring and makes XRGrabInteractable usable.
    /// </summary>
    public class EnsureStarterXRRig : MonoBehaviour
    {
        [Header("Rig Source")]
        [Tooltip("Optional direct reference to the XR Origin (XR Rig) prefab. If set, works in builds too.")]
        public GameObject rigPrefab;

        [Tooltip("(Editor helper) Path to the Starter Assets XR Rig prefab.")]
        public string rigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.3.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

        [Tooltip("(Optional) Resources fallback name (without extension). Example: put the prefab under Assets/Resources/XR Origin (XR Rig).prefab")]
        public string rigPrefabResourcesName = "XR Origin (XR Rig)";

        [Tooltip("If present, disable the legacy rig GameObject with this exact name.")]
        public string disableLegacyRigName = "XR Origin (VR)";

        [Tooltip("Spawn position for the rig.")]
        public Vector3 spawnPosition = new Vector3(0f, 0f, -1.5f);

        [Tooltip("Only run in Play Mode.")]
        public bool onlyInPlayMode = true;

        private static bool s_spawned;

        private void Awake()
        {
            if (s_spawned) return;

            if (onlyInPlayMode && !Application.isPlaying)
                return;

            // Disable legacy rig if present
            var legacy = GameObject.Find(disableLegacyRigName);
            if (legacy != null)
                legacy.SetActive(false);

            // Resolve prefab
            var prefab = rigPrefab;

#if UNITY_EDITOR
            if (prefab == null)
            {
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rigPrefabPath);
            }

            if (prefab == null)
            {
                // Fallback: search anywhere in the project (covers different XRI sample versions/paths)
                var guids = AssetDatabase.FindAssets("XR Origin (XR Rig) t:Prefab");
                foreach (var guid in guids)
                {
                    var p = AssetDatabase.GUIDToAssetPath(guid);
                    if (p.Contains("XR Interaction Toolkit") && p.Contains("Starter Assets") && p.EndsWith("XR Origin (XR Rig).prefab"))
                    {
                        prefab = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                        if (prefab != null)
                        {
                            rigPrefabPath = p; // keep for debugging
                            break;
                        }
                    }
                }
            }
#endif

            if (prefab == null && !string.IsNullOrWhiteSpace(rigPrefabResourcesName))
            {
                prefab = Resources.Load<GameObject>(rigPrefabResourcesName);
            }

            if (prefab == null)
            {
                Debug.LogError(
                    "EnsureStarterXRRig: Could not resolve XR rig prefab. " +
                    "Assign 'rigPrefab' in the inspector, or import XRI Starter Assets, " +
                    $"or update 'rigPrefabPath' (currently '{rigPrefabPath}'), " +
                    $"or place the prefab under Resources as '{rigPrefabResourcesName}'.");
                return;
            }

            var rig = Instantiate(prefab);
            rig.name = "XR Origin (XR Rig)";
            rig.transform.position = spawnPosition;

            s_spawned = true;
        }
    }
}
