using UnityEngine;

namespace SCoL
{
    /// <summary>
    /// Drop this into a scene. On Play, it creates a grid + renderer and starts the simulation.
    /// VR interaction can call the public methods on SCoLRuntime (PlaceSeed/AddWater/Ignite).
    /// </summary>
    public class SCoLBootstrap : MonoBehaviour
    {
        public SCoLConfig config;
        public Vector3 origin;

        private SCoLRuntime _runtime;

        private void Awake()
        {
            if (config == null)
            {
                Debug.LogWarning("SCoLBootstrap: missing config. Create one via Assets -> Create -> SCoL -> Config");
                return;
            }

            _runtime = new GameObject("SCoLRuntime").AddComponent<SCoLRuntime>();
            _runtime.transform.position = Vector3.zero;
            _runtime.Init(config, origin);
        }
    }
}
