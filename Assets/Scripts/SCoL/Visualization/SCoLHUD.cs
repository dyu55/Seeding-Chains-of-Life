using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace SCoL.Visualization
{
    /// <summary>
    /// Simple on-screen HUD to make prototype state readable.
    /// Displays:
    /// - Tool (from SCoLXRInteractor if present)
    /// - Season / Weather
    /// - View mode
    /// - Cell under aim (stage + continuous vars)
    ///
    /// Works in Editor and in XR (screen-space overlay).
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLHUD : MonoBehaviour
    {
        public SCoL.SCoLRuntime runtime;

        [Header("UI")]
        public Color textColor = Color.white;
        public int fontSize = 22;

        [Header("Aim")]
        public float rayLength = 50f;
        public LayerMask hitLayers = ~0;

        private Text _text;
        private Camera _cam;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();

            _cam = Camera.main;

            CreateCanvasIfMissing();
        }

        private void Update()
        {
            if (runtime == null)
            {
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                if (runtime == null) return;
            }

            if (_cam == null) _cam = Camera.main;

            var sb = new StringBuilder(256);

            // Tool
            var tool = FindFirstObjectByType<SCoL.XR.SCoLXRInteractor>();
            if (tool != null)
                sb.AppendLine($"Tool: {tool.currentTool}");

            sb.AppendLine($"Season: {runtime.CurrentSeason}   Weather: {runtime.CurrentWeather}");
            sb.AppendLine($"View: {runtime.ViewMode}   FireOverlay: {(runtime.OverlayFire ? "ON" : "OFF")}");

            // Cell under aim
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

            _text.text = sb.ToString();
        }

        private bool TryGetAimRay(out Ray ray)
        {
            // XR: camera forward is a decent default.
            // Editor: mouse ray.
            if (_cam == null)
            {
                ray = default;
                return false;
            }

#if ENABLE_INPUT_SYSTEM
            // If mouse exists and we are not in an active XR controller state, use mouse position
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

        private void CreateCanvasIfMissing()
        {
            // Make a simple overlay canvas
            var canvasGO = new GameObject("SCoL_HUD");
            canvasGO.transform.SetParent(transform, false);

            var canvas = canvasGO.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 1000;

            canvasGO.AddComponent<CanvasScaler>();
            canvasGO.AddComponent<GraphicRaycaster>();

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(canvasGO.transform, false);

            _text = textGO.AddComponent<Text>();
            _text.font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            _text.fontSize = fontSize;
            _text.color = textColor;
            _text.alignment = TextAnchor.UpperLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Overflow;
            _text.verticalOverflow = VerticalWrapMode.Overflow;

            var rt = _text.rectTransform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(12, -12);
            rt.sizeDelta = new Vector2(900, 600);
        }
    }
}
