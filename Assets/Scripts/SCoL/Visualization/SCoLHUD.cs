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
    /// Works in Editor and in XR.
    /// Note: In XR, ScreenSpaceOverlay may not render in the HMD; this HUD uses ScreenSpaceCamera bound to the main (XR) camera.
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

        [Header("Inventory")]
        public bool showInventory = true;
        [Tooltip("Anchor inventory panel to bottom-right.")]
        public Vector2 inventoryOffset = new Vector2(-16, 16);
        public int inventoryFontSize = 18;

        [Tooltip("Optional font override. If left null, the HUD will use Unity's built-in LegacyRuntime.ttf (Unity 2023+/6000 compatible).")]
        public Font overrideFont;

        [Header("Aim")]
        public float rayLength = 50f;
        public LayerMask hitLayers = ~0;

        private Text _text;
        private Text _invText;
        private Camera _cam;
        private float _t;
        private readonly StringBuilder _sb = new StringBuilder(512);

        private SCoL.XR.SCoLToolController _toolController;
        private SCoL.XR.SCoLXRInteractor _xrInteractor;
        private SCoL.Inventory.SCoLInventory _inventory;

        private void Awake()
        {
            if (runtime == null)
                runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();

            _cam = Camera.main;
            _toolController = FindFirstObjectByType<SCoL.XR.SCoLToolController>();
            _xrInteractor = FindFirstObjectByType<SCoL.XR.SCoLXRInteractor>();
            _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();

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

            if (_toolController == null)
                _toolController = FindFirstObjectByType<SCoL.XR.SCoLToolController>();
            if (_xrInteractor == null)
                _xrInteractor = FindFirstObjectByType<SCoL.XR.SCoLXRInteractor>();
            if (_inventory == null)
                _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();

            if (_cam == null) _cam = Camera.main;

            _sb.Clear();

            // Header
            _sb.AppendLine("SCoL");

            // Tool (prefer inventory-backed controller if present)
            if (showTool)
            {
                if (_toolController != null)
                    _sb.AppendLine($"Tool: {_toolController.currentTool}");
                else if (_xrInteractor != null)
                    _sb.AppendLine($"Tool: {_xrInteractor.currentTool}");
            }

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

            // Inventory panel (bottom-right)
            if (_invText != null)
            {
                _invText.enabled = visible && showInventory && _inventory != null;
                if (showInventory && _inventory != null)
                {
                    _invText.text = $"Seed: {_inventory.seeds}\nWater: {_inventory.water}\nFire: {_inventory.fire}";
                }
            }
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

            // ScreenSpaceOverlay often won't appear in HMD for XR.
            // Use ScreenSpaceCamera and bind to the (XR) main camera.
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.sortingOrder = 1000;

            if (_cam == null) _cam = Camera.main;
            canvas.worldCamera = _cam;
            canvas.planeDistance = 1.0f;

            var scaler = canvasGO.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 1.0f;

            var raycaster = canvasGO.AddComponent<GraphicRaycaster>();
            raycaster.ignoreReversedGraphics = true;
            raycaster.blockingObjects = GraphicRaycaster.BlockingObjects.None;

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(canvasGO.transform, false);

            _text = textGO.AddComponent<Text>();

            // Inventory panel (bottom-right)
            var invGO = new GameObject("Inventory");
            invGO.transform.SetParent(canvasGO.transform, false);
            _invText = invGO.AddComponent<Text>();

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

            // Inventory style/position
            _invText.font = font;
            _invText.fontSize = inventoryFontSize;
            _invText.color = textColor;
            _invText.supportRichText = false;
            _invText.alignment = TextAnchor.LowerRight;
            _invText.horizontalOverflow = HorizontalWrapMode.Overflow;
            _invText.verticalOverflow = VerticalWrapMode.Overflow;

            var irt = _invText.rectTransform;
            irt.anchorMin = new Vector2(1, 0);
            irt.anchorMax = new Vector2(1, 0);
            irt.pivot = new Vector2(1, 0);
            irt.anchoredPosition = new Vector2(inventoryOffset.x, inventoryOffset.y);
            irt.sizeDelta = new Vector2(220, 120);
        }
    }
}
