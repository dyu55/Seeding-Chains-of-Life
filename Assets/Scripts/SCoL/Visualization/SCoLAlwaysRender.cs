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

        [Tooltip("If true, force a full grid re-render repeatedly (useful for editor iteration, expensive in builds).")]
        public bool enabledInEditor = true;

        [Tooltip("Force render every frame (editor only). If false, uses 'intervalSeconds'.")]
        public bool everyFrame = false;

        [Min(0.02f)]
        public float intervalSeconds = 0.2f;

        private float _t;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
        }

        private void LateUpdate()
        {
#if !UNITY_EDITOR
            // Never run in player builds — this is for iteration only.
            return;
#else
            if (!enabledInEditor) return;

            if (runtime == null)
            {
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                if (runtime == null) return;
            }

            if (everyFrame)
            {
                runtime.ForceRender();
                return;
            }

            _t += Time.unscaledDeltaTime;
            if (_t >= intervalSeconds)
            {
                _t = 0f;
                runtime.ForceRender();
            }
#endif
        }
    }
}
