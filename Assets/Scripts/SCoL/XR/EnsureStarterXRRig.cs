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
        [Tooltip("Path to the Starter Assets XR Rig prefab.")]
        public string rigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.3.1/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";

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

#if UNITY_EDITOR
            // Disable legacy rig if present
            var legacy = GameObject.Find(disableLegacyRigName);
            if (legacy != null)
                legacy.SetActive(false);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(rigPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"EnsureStarterXRRig: Could not load rig prefab at '{rigPrefabPath}'. Check Starter Assets import.");
                return;
            }

            var rig = Instantiate(prefab);
            rig.name = "XR Origin (XR Rig)";
            rig.transform.position = spawnPosition;

            s_spawned = true;
#else
            // In builds, do nothing.
#endif
        }
    }
}
