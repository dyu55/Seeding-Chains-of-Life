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
        public int fontSize = 18;

        [Tooltip("Toggle HUD visibility at runtime.")]
        public bool visible = true;

        [Tooltip("Update interval (seconds). Lower = more responsive, higher = less GC/CPU.")]
        [Min(0.02f)]
        public float updateInterval = 0.15f;

        [Header("Sections")]
        public bool showTool = true;
        public bool showSeasonWeather = true;
        public bool showViewMode = true;
        public bool showAimCell = true;
        public bool showControlsHelp = false;

        [Tooltip("Optional font override. If left null, the HUD will use Unity's built-in LegacyRuntime.ttf (Unity 2023+/6000 compatible).")]
        public Font overrideFont;

        [Header("Aim")]
        public float rayLength = 50f;
        public LayerMask hitLayers = ~0;

        private Text _text;
        private Camera _cam;
        private float _t;
        private readonly StringBuilder _sb = new StringBuilder(512);

        private SCoL.XR.SCoLXRInteractor _tool;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();

            _cam = Camera.main;
            _tool = FindFirstObjectByType<SCoL.XR.SCoLXRInteractor>();

            CreateCanvasIfMissing();
        }

        private void Update()
        {
            // Toggle visibility (H)
#if ENABLE_INPUT_SYSTEM
            if (Keyboard.current != null && Keyboard.current.hKey.wasPressedThisFrame)
                visible = !visible;
#endif

            if (_text != null)
                _text.enabled = visible;

            if (!visible)
                return;

            _t += Time.unscaledDeltaTime;
            if (_t < updateInterval)
                return;
            _t = 0f;

            if (runtime == null)
            {
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                if (runtime == null) return;
            }

            if (_tool == null)
                _tool = FindFirstObjectByType<SCoL.XR.SCoLXRInteractor>();

            if (_cam == null) _cam = Camera.main;

            _sb.Clear();

            // Header
            _sb.AppendLine("SCoL");

            // Tool
            if (showTool && _tool != null)
                _sb.AppendLine($"Tool: {_tool.currentTool}");

            if (showSeasonWeather)
                _sb.AppendLine($"{runtime.CurrentSeason} / {runtime.CurrentWeather}");

            if (showViewMode)
                _sb.AppendLine($"View: {runtime.ViewMode}   Fire: {(runtime.OverlayFire ? "ON" : "OFF")}");

            // Cell under aim (compact)
            if (showAimCell)
            {
                if (TryGetAimRay(out var ray) && Physics.Raycast(ray, out var hit, rayLength, hitLayers, QueryTriggerInteraction.Ignore))
                {
                    if (runtime.TryWorldToCell(hit.point, out int cx, out int cy))
                    {
                        var c = runtime.Grid.Get(cx, cy);
                        _sb.AppendLine($"Cell {cx},{cy}  {c.PlantStage}  Fire:{(c.IsOnFire ? "Y" : "N")}");
                        _sb.AppendLine($"W:{c.Water:0.00} S:{c.Sunlight:0.00} H:{c.Heat:0.00}  D:{c.Durability:0.00}  ✓:{c.Success:0.00}");
                    }
                    else
                    {
                        _sb.AppendLine("Cell: out of bounds");
                    }
                }
                else
                {
                    _sb.AppendLine("Aim: no hit");
                }
            }

            if (showControlsHelp)
            {
                _sb.AppendLine();
                _sb.AppendLine("H: toggle HUD");
                _sb.AppendLine("V: cycle view | F: fire overlay");
                _sb.AppendLine("1/2/3: tool | LMB: apply (Editor)");
            }

            _text.text = _sb.ToString();
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
            // Avoid duplicates if domain reload is off or this component is toggled.
            var existing = transform.Find("SCoL_HUD");
            if (existing != null)
            {
                _text = existing.GetComponentInChildren<Text>(includeInactive: true);
                if (_text != null) return;
            }

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

            // Unity 2023+/6000: Arial.ttf is no longer a valid built-in font.
            // Use LegacyRuntime.ttf as the built-in fallback.
            var font = overrideFont;
            if (font == null)
            {
                try
                {
                    font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                }
                catch
                {
                    font = null;
                }
            }
            _text.font = font;

            _text.fontSize = fontSize;
            _text.color = textColor;
            _text.supportRichText = false;
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
