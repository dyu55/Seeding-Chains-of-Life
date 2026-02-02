using System.Text;
using UnityEngine;
using UnityEngine.XR;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SCoL.Visualization
{
    /// <summary>
    /// HUD using OnGUI (no UGUI/TextMeshPro dependencies).
    /// Intended for fast prototyping/debugging in Editor & XR Device Simulator.
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLOnGUIHUD : MonoBehaviour
    {
        public SCoL.SCoLRuntime runtime;

        public int fontSize = 16;
        public Color textColor = Color.white;

        public float rayLength = 50f;
        public LayerMask hitLayers = ~0;

        private Camera _cam;
        private GUIStyle _style;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
            _cam = Camera.main;
        }

        private void OnGUI()
        {
            // Force visible order
            GUI.depth = -1000;

            if (_style == null)
            {
                _style = new GUIStyle(GUI.skin.label)
                {
                    fontSize = fontSize,
                    normal = { textColor = textColor },
                    richText = false
                };
            }

            if (runtime == null)
            {
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                if (runtime == null) return;
            }

            if (_cam == null) _cam = Camera.main;

            var sb = new StringBuilder(256);

            var tool = FindFirstObjectByType<SCoL.XR.SCoLXRInteractor>();
            if (tool != null)
                sb.AppendLine($"Tool: {tool.currentTool}");

            sb.AppendLine($"Season: {runtime.CurrentSeason}   Weather: {runtime.CurrentWeather}");
            sb.AppendLine($"View: {runtime.ViewMode}   FireOverlay: {(runtime.OverlayFire ? "ON" : "OFF")}");

            if (TryGetAimRay(out var ray) && Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
            {
                if (runtime.TryWorldToCell(hit.point, out int cx, out int cy))
                {
                    var c = runtime.Grid.Get(cx, cy);
                    sb.AppendLine($"Cell: ({cx},{cy})  Stage: {c.PlantStage}  OnFire: {(c.IsOnFire ? "YES" : "NO")}");
                    sb.AppendLine($"Water: {c.Water:0.00}  Sun: {c.Sunlight:0.00}  Heat: {c.Heat:0.00}");
                    sb.AppendLine($"Durability: {c.Durability:0.00}  Success: {c.Success:0.00}");
                }
                else
                {
                    sb.AppendLine("Cell: (out of bounds)");
                }
            }
            else
            {
                sb.AppendLine("Aim: (no hit)");
            }

            sb.AppendLine();
            sb.AppendLine("Controls:");
            sb.AppendLine("- View: V cycle | F toggle fire overlay | 0/4/5/6/7/8 set mode");
            sb.AppendLine("- Tool (Editor): 1/2/3 select | LMB apply");

            // Semi-transparent background
            var bg = new Color(0f, 0f, 0f, 0.55f);
            var old = GUI.color;
            GUI.color = bg;
            GUI.Box(new Rect(6, 6, 920, 520), GUIContent.none);
            GUI.color = old;

            GUI.Label(new Rect(10, 10, 900, 600), sb.ToString(), _style);
        }

        private bool TryGetAimRay(out Ray ray)
        {
            if (_cam == null)
            {
                ray = default;
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            if (Mouse.current != null && !IsXRControllerValid())
            {
                ray = _cam.ScreenPointToRay(Mouse.current.position.ReadValue());
                return true;
            }
#endif

            ray = new Ray(_cam.transform.position, _cam.transform.forward);
            return true;
        }

        private bool IsXRControllerValid()
        {
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var r = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            return l.isValid || r.isValid;
        }
    }
}
