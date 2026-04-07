using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SCoL.Visualization
{
    /// <summary>
    /// Simple view-mode toggles for the ecosystem grid.
    ///
    /// Keys (Input System):
    /// - V: cycle view mode
    /// - F: toggle fire overlay
    /// - 0: Stage view
    /// - 4: Water
    /// - 5: Heat
    /// - 6: Sunlight
    /// - 7: Durability
    /// - 8: Success
    /// </summary>
    public class SCoLViewModeController : MonoBehaviour
    {
        public SCoL.SCoLRuntime runtime;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
        }

        private void Update()
        {
            if (runtime == null)
            {
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                if (runtime == null) return;
            }

#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            if (kb == null) return;

            if (kb.vKey.wasPressedThisFrame)
            {
                Cycle(+1);
            }

            if (kb.fKey.wasPressedThisFrame)
            {
                runtime.OverlayFire = !runtime.OverlayFire;
                Debug.Log($"SCoL OverlayFire = {runtime.OverlayFire}");
            }

            if (kb.digit0Key.wasPressedThisFrame) Set(GridViewMode.Stage);
            if (kb.digit4Key.wasPressedThisFrame) Set(GridViewMode.Water);
            if (kb.digit5Key.wasPressedThisFrame) Set(GridViewMode.Heat);
            if (kb.digit6Key.wasPressedThisFrame) Set(GridViewMode.Sunlight);
            if (kb.digit7Key.wasPressedThisFrame) Set(GridViewMode.Durability);
            if (kb.digit8Key.wasPressedThisFrame) Set(GridViewMode.Success);
#endif
        }

        private void Cycle(int dir)
        {
            int t = (int)runtime.ViewMode;
            int max = System.Enum.GetValues(typeof(GridViewMode)).Length;
            t = (t + dir) % max;
            if (t < 0) t += max;
            Set((GridViewMode)t);
        }

        private void Set(GridViewMode mode)
        {
            runtime.ViewMode = mode;
            Debug.Log($"SCoL ViewMode = {mode} (V cycles, F toggles fire overlay)");
        }
    }
}
