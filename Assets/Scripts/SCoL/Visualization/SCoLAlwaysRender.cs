using UnityEngine;

namespace SCoL.Visualization
{
    /// <summary>
    /// Forces the grid to render every frame.
    /// Useful for hover highlighting and rapid iteration.
    /// </summary>
    public class SCoLAlwaysRender : MonoBehaviour
    {
        public SCoL.SCoLRuntime runtime;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
        }

        private void LateUpdate()
        {
            if (runtime == null)
            {
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                if (runtime == null) return;
            }

            runtime.ForceRender();
        }
    }
}
