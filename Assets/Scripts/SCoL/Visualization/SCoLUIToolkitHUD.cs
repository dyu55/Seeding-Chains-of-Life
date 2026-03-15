using System.Text;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using SCoL.Inventory;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.Visualization
{
    /// <summary>
    /// Runtime HUD powered by UGUI + imported SimpleUIKit assets.
    /// Replaces legacy UGUI/OnGUI HUD layers and the previous UI Toolkit runtime HUD.
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLUIToolkitHUD : MonoBehaviour
    {
        public SCoLRuntime runtime;
        public Camera cameraSource;
        public float maxDistance = 50f;
        public LayerMask hitMask = ~0;
        public bool visible = true;
        [Min(0.02f)] public float updateInterval = 0.10f;

        [Header("VR / World-Space HUD")]
        [Tooltip("When XR is active, switch the canvas to World Space so it renders inside the headset.")]
        public bool useWorldSpaceInXR = true;
        [Tooltip("Distance in front of the camera for the world-space HUD panel (metres).")]
        [Range(0.3f, 3f)] public float vrHudDistance = 1.2f;
        [Tooltip("Scale of the world-space canvas (metres per canvas unit). Smaller = larger on screen.")]
        [Range(0.0002f, 0.003f)] public float vrHudScale = 0.0009f;
        [Tooltip("Offset from camera centre: +x right, +y up (metres).")]
        public Vector2 vrHudOffset = new Vector2(0f, 0f);

        [Header("SimpleUIKit")]
        public bool useSimpleUIKitPanelPrefabs = true;
        public GameObject simpleUIKitPanelPrefab;
        public GameObject simpleUIKitInnerPanelPrefab;

        [Header("Health")]
        [Min(1f)] public float defaultMaxHealth = 100f;
        public bool autoCreatePlayerHealth = true;
        public bool showHealthText = true;
        [Min(4f)] public float healthBarWidth = 440f;
        [Min(4f)] public float healthBarHeight = 30f;
        [Min(0f)] public float healthLagCatchupSpeed = 52f;

        private Canvas _canvas;
        private RectTransform _root;
        private Font _font;

        private Image _crosshairLeft;
        private Image _crosshairRight;
        private Image _crosshairTop;
        private Image _crosshairBottom;
        private Image _crosshairDot;
        private Text _statusLabel;
        private RectTransform _aimPanel;
        private Text _aimTitleLabel;
        private Text _aimDetailLabel;
        private Text _inventoryLabel;
        private RectTransform _healthTrackRect;
        private RectTransform _healthLagRect;
        private RectTransform _healthFillRect;
        private Text _healthLabel;
        private Text _healthCaptionLabel;
        private Text _healthBadgeLabel;
        private Image _healthFrame;
        private Image _healthPulse;
        private Image _healthLagFill;
        private Image _healthFill;

        private SCoLInventory _inventory;
        private PlantVoxelRenderer _plantRenderer;
        private FPSRaycastInteractor _fpsInteractor;
        private SCoLPlayerHealth _playerHealth;

        private float _nextUpdateAt;
        private float _displayHealth = -1f;
        private float _displayLagHealth = -1f;
        private readonly StringBuilder _sb = new StringBuilder(256);
        private static Sprite _fallbackWhiteUISprite;

        private static readonly Color CrosshairIdle = new Color(1f, 1f, 1f, 0.90f);
        private static readonly Color CrosshairHover = new Color(0.35f, 1f, 0.35f, 0.98f);
        private const string SimpleUIKitPanelPrefabPath = "Assets/SimpleUIKit/Prefabs/Elements/Parts/Background.prefab";
        private const string SimpleUIKitInnerPanelPrefabPath = "Assets/SimpleUIKit/Prefabs/Elements/Parts/BackgroundInner.prefab";
        private static readonly Color HealthFrameBase = new Color(0.36f, 0.26f, 0.12f, 0.96f);
        private static readonly Color HealthFrameAlert = new Color(0.70f, 0.18f, 0.14f, 0.98f);
        private static readonly Color HealthTrackBase = new Color(0.08f, 0.09f, 0.11f, 0.94f);
        private static readonly Color HealthLagColor = new Color(0.96f, 0.72f, 0.34f, 0.48f);
        private static readonly Color HealthFillLow = new Color(0.84f, 0.18f, 0.18f, 0.98f);
        private static readonly Color HealthFillHigh = new Color(0.22f, 0.78f, 0.44f, 0.98f);
        private static readonly Color HudCardBg = new Color(0.04f, 0.05f, 0.07f, 0.46f);
        private static readonly Color HudCardInset = new Color(0.10f, 0.11f, 0.15f, 0.72f);
        private static readonly Color HudTextPrimary = new Color(0.94f, 0.95f, 0.90f, 0.98f);
        private static readonly Color HudTextSecondary = new Color(0.71f, 0.76f, 0.79f, 0.96f);
        private static readonly Color HudAccentWarm = new Color(0.94f, 0.75f, 0.38f, 0.98f);
        private static readonly Color HudAccentCool = new Color(0.41f, 0.80f, 0.98f, 0.98f);
        private static readonly Color HudAccentGreen = new Color(0.51f, 0.88f, 0.57f, 0.98f);

        private void Awake()
        {
            if (runtime == null) runtime = FindFirstObjectByType<SCoLRuntime>();
            if (cameraSource == null) cameraSource = Camera.main;

            _inventory = FindFirstObjectByType<SCoLInventory>();
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
            _fpsInteractor = FindFirstObjectByType<FPSRaycastInteractor>();
            EnsurePlayerHealth();
            DisableLegacyHudObjects();

            EnsureUI();
            EnsureCanvasMode();
        }

        private void Update()
        {
            if (_canvas == null || _root == null)
                EnsureUI();
            if (_canvas == null)
                return;

            // Keep canvas mode in sync (XR may come online after Awake)
            EnsureCanvasMode();

            _canvas.enabled = visible;
            if (!visible)
                return;

            if (Time.unscaledTime < _nextUpdateAt)
            {
                UpdateHealth();
                return;
            }
            _nextUpdateAt = Time.unscaledTime + Mathf.Max(0.02f, updateInterval);

            if (runtime == null) runtime = FindFirstObjectByType<SCoLRuntime>();
            if (cameraSource == null) cameraSource = Camera.main;
            if (_inventory == null) _inventory = FindFirstObjectByType<SCoLInventory>();
            if (_plantRenderer == null) _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
            if (_fpsInteractor == null) _fpsInteractor = FindFirstObjectByType<FPSRaycastInteractor>();
            EnsurePlayerHealth();

            UpdateStatus();
            UpdateAimInfo();
            UpdateInventory();
            UpdateHealth();
        }

        // ──────────────────────────────────────────────────────────────────────
        // VR Canvas Mode Management
        // ──────────────────────────────────────────────────────────────────────

        private bool IsXRActive()
        {
            if (XRSettings.isDeviceActive) return true;

            // Controller check
            var l = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
            var r = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            if (l.isValid || r.isValid) return true;

            // XR display subsystem check
            var displays = new List<XRDisplaySubsystem>();
            SubsystemManager.GetSubsystems(displays);
            foreach (var d in displays)
                if (d.running) return true;

            // XROrigin present check
            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null && xrOrigin.Camera != null) return true;

            return false;
        }

        private bool _wasXRActive;

        private void EnsureCanvasMode()
        {
            if (_canvas == null) return;
            if (cameraSource == null) cameraSource = Camera.main;

            bool xrActive = useWorldSpaceInXR && IsXRActive();

            if (xrActive)
            {
                if (_canvas.renderMode != RenderMode.WorldSpace)
                {
                    _canvas.renderMode = RenderMode.WorldSpace;
                    _canvas.worldCamera = cameraSource;
                    // Give the canvas a size that makes sense in world-space
                    if (_root != null)
                        _root.sizeDelta = new Vector2(2000f, 1200f);
                    Debug.Log("[SCoLUIToolkitHUD] Switched to WorldSpace canvas for VR.");
                }

                // Each frame: billboard & position in front of camera
                PositionVRCanvas();
            }
            else
            {
                if (_canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                {
                    _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    _canvas.sortingOrder = short.MaxValue - 2;
                    if (_root != null)
                    {
                        _root.anchorMin = Vector2.zero;
                        _root.anchorMax = Vector2.one;
                        _root.offsetMin = Vector2.zero;
                        _root.offsetMax = Vector2.zero;
                    }
                }
            }

            _wasXRActive = xrActive;
        }

        private void PositionVRCanvas()
        {
            if (cameraSource == null) return;
            if (_canvas == null) return;

            var camT = cameraSource.transform;

            // Scale so the canvas is readable at the chosen distance
            _canvas.transform.localScale = Vector3.one * vrHudScale;

            // Position: directly in front of camera with optional offset
            Vector3 pos = camT.position
                + camT.forward * vrHudDistance
                + camT.right   * vrHudOffset.x
                + camT.up      * vrHudOffset.y;
            _canvas.transform.position = pos;

            // Billboard: face the camera
            _canvas.transform.rotation = Quaternion.LookRotation(
                pos - camT.position,
                camT.up);
        }

        private void EnsureUI()
        {
            if (_canvas != null && _root != null)
                return;

            _font = ResolveFont();

            var canvasGO = new GameObject("SCoL HUD (SimpleUIKit)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, worldPositionStays: false);

            _canvas = canvasGO.GetComponent<Canvas>();
            // Start as overlay; EnsureCanvasMode will switch to WorldSpace when XR is active
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = short.MaxValue - 2;
            _canvas.pixelPerfect = false;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            _root = canvasGO.GetComponent<RectTransform>();
            _root.anchorMin = Vector2.zero;
            _root.anchorMax = Vector2.one;
            _root.offsetMin = Vector2.zero;
            _root.offsetMax = Vector2.zero;

            TryResolveSimpleUIKitPrefabs();
            BuildPanelsAndLabels();
            BuildCrosshair();
            BuildStylizedHealthBar(_root);
        }

        private void BuildPanelsAndLabels()
        {
            var statusPanel = CreateHudCard(
                _root,
                "StatusPanel",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(26f, -26f),
                size: new Vector2(480f, 212f),
                title: "WORLD STATE",
                accent: HudAccentWarm);

            _statusLabel = CreateText(
                statusPanel,
                "StatusText",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -20f),
                size: new Vector2(-42f, -72f),
                fontSize: 20,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft);

            _aimPanel = CreateHudCard(
                _root,
                "AimHoverPanel",
                anchorMin: new Vector2(1f, 0.5f),
                anchorMax: new Vector2(1f, 0.5f),
                pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-28f, 118f),
                size: new Vector2(380f, 132f),
                title: "FOCUS",
                accent: HudAccentCool);
            _aimPanel.gameObject.SetActive(false);

            _aimTitleLabel = CreateText(
                _aimPanel,
                "AimTitle",
                anchorMin: new Vector2(0f, 0.50f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -12f),
                size: new Vector2(-34f, -40f),
                fontSize: 25,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);

            _aimDetailLabel = CreateText(
                _aimPanel,
                "AimDetail",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0.54f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -4f),
                size: new Vector2(-34f, -24f),
                fontSize: 18,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft,
                addOutline: true);

            var invPanel = CreateHudCard(
                _root,
                "InventoryPanel",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-26f, 26f),
                size: new Vector2(390f, 222f),
                title: "RESERVES",
                accent: HudAccentGreen);

            _inventoryLabel = CreateText(
                invPanel,
                "InventoryText",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -12f),
                size: new Vector2(-34f, -62f),
                fontSize: 20,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft);
        }

        private void BuildCrosshair()
        {
            _crosshairLeft = CreateCrosshairLine(_root, "CrosshairLeft", new Vector2(-18f, 0f), new Vector2(12f, 2f));
            _crosshairRight = CreateCrosshairLine(_root, "CrosshairRight", new Vector2(18f, 0f), new Vector2(12f, 2f));
            _crosshairTop = CreateCrosshairLine(_root, "CrosshairTop", new Vector2(0f, 18f), new Vector2(2f, 12f));
            _crosshairBottom = CreateCrosshairLine(_root, "CrosshairBottom", new Vector2(0f, -18f), new Vector2(2f, 12f));
            _crosshairDot = CreateCrosshairLine(_root, "CrosshairDot", Vector2.zero, new Vector2(5f, 5f));

            SetCrosshairColor(CrosshairIdle);
        }

        private void BuildStylizedHealthBar(RectTransform parent)
        {
            var panel = CreateImage(
                parent,
                "HealthBar (Stylized)",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 36f),
                size: new Vector2(690f, 146f),
                color: new Color(0.02f, 0.02f, 0.03f, 0.28f)).rectTransform;

            var panelShadow = panel.gameObject.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            panelShadow.effectDistance = new Vector2(0f, -8f);

            _healthFrame = CreateImage(
                panel,
                "OuterFrame",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(36f, 0f),
                size: new Vector2(540f, 86f),
                color: HealthFrameBase);

            CreateImage(
                _healthFrame.transform,
                "FrameInset",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(522f, 68f),
                color: new Color(0.12f, 0.09f, 0.08f, 0.86f));

            _healthPulse = CreateImage(
                _healthFrame.transform,
                "PulseGlow",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(550f, 96f),
                color: new Color(0.95f, 0.18f, 0.14f, 0f));

            var badge = CreateImage(
                panel,
                "Badge",
                anchorMin: new Vector2(0f, 0.5f),
                anchorMax: new Vector2(0f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(82f, 0f),
                size: new Vector2(94f, 94f),
                color: new Color(0.53f, 0.38f, 0.17f, 0.98f));
            badge.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            CreateImage(
                badge.transform,
                "BadgeInner",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(68f, 68f),
                color: new Color(0.16f, 0.09f, 0.08f, 0.92f));

            _healthBadgeLabel = CreateText(
                badge.transform,
                "BadgeLabel",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(96f, 38f),
                fontSize: 28,
                color: new Color(0.98f, 0.92f, 0.76f, 0.98f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
            _healthBadgeLabel.transform.localRotation = Quaternion.Euler(0f, 0f, -45f);
            _healthBadgeLabel.text = "HP";

            _healthCaptionLabel = CreateText(
                panel,
                "HealthCaption",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(176f, -20f),
                size: new Vector2(260f, 30f),
                fontSize: 18,
                color: new Color(0.94f, 0.82f, 0.62f, 0.92f),
                alignment: TextAnchor.MiddleLeft);
            _healthCaptionLabel.text = "VITALITY";

            _healthTrackRect = CreateRect(
                panel,
                "TrackRoot",
                anchorMin: new Vector2(0f, 0.5f),
                anchorMax: new Vector2(0f, 0.5f),
                pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(176f, 10f),
                size: new Vector2(healthBarWidth, healthBarHeight));

            CreateImage(
                _healthTrackRect,
                "TrackShadow",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -3f),
                size: Vector2.zero,
                color: new Color(0f, 0f, 0f, 0.25f));

            CreateImage(
                _healthTrackRect,
                "TrackBase",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: HealthTrackBase);

            CreateImage(
                _healthTrackRect,
                "TrackTopRim",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -1f),
                size: new Vector2(0f, 2f),
                color: new Color(0.96f, 0.88f, 0.66f, 0.22f));

            var fillMask = CreateRect(
                _healthTrackRect,
                "FillMask",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero);
            fillMask.gameObject.AddComponent<RectMask2D>();

            _healthLagFill = CreateImage(
                fillMask,
                "LagFill",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(healthBarWidth, 0f),
                color: HealthLagColor);
            _healthLagRect = _healthLagFill.rectTransform;

            _healthFill = CreateImage(
                fillMask,
                "HealthFill",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(healthBarWidth, 0f),
                color: HealthFillHigh);
            _healthFillRect = _healthFill.rectTransform;

            CreateImage(
                _healthFill.transform,
                "FillHighlight",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -2f),
                size: new Vector2(0f, 6f),
                color: new Color(1f, 1f, 1f, 0.18f));

            CreateSegmentTicks(_healthTrackRect, 8);

            _healthLabel = CreateText(
                panel,
                "HealthText",
                anchorMin: new Vector2(1f, 0.5f),
                anchorMax: new Vector2(1f, 0.5f),
                pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-24f, 10f),
                size: new Vector2(170f, 46f),
                fontSize: 30,
                color: new Color(0.97f, 0.95f, 0.90f, 0.98f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
        }

        private void EnsurePlayerHealth()
        {
            if (_playerHealth != null)
                return;

            _playerHealth = FindFirstObjectByType<SCoLPlayerHealth>();
            if (_playerHealth != null)
                return;
            if (!autoCreatePlayerHealth)
                return;

            GameObject target = null;

            var controller = FindFirstObjectByType<SimpleFirstPersonController>();
            if (controller != null)
                target = controller.gameObject;
            else if (cameraSource != null)
                target = cameraSource.transform.root.gameObject;

            if (target == null)
                return;

            _playerHealth = target.GetComponent<SCoLPlayerHealth>();
            if (_playerHealth == null)
                _playerHealth = target.AddComponent<SCoLPlayerHealth>();
            _playerHealth.SetMaxHealth(defaultMaxHealth, fillToMax: true);
        }

        private void UpdateStatus()
        {
            if (_statusLabel == null)
                return;

            _sb.Clear();
            _sb.AppendLine("<size=21><b>SCoL</b></size>");

            if (runtime != null)
            {
                _sb.Append("<color=#F1D598><b>Season</b></color> ");
                _sb.Append(runtime.CurrentSeason);
                _sb.Append("  <color=#F1D598><b>Weather</b></color> ");
                _sb.AppendLine(runtime.CurrentWeather.ToString());
                _sb.Append("<color=#F1D598><b>View</b></color> ");
                _sb.Append(runtime.ViewMode);
                _sb.Append("  <color=#F1D598><b>Fire Overlay</b></color> ");
                _sb.AppendLine(runtime.OverlayFire ? "ON" : "OFF");
            }

            if (_fpsInteractor != null)
            {
                _sb.Append("<color=#F1D598><b>Tool</b></color> ");
                _sb.AppendLine(_fpsInteractor.currentTool.ToString());
                if (_fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Seed)
                {
                    _sb.Append("<color=#F1D598><b>Flower</b></color> ");
                    _sb.AppendLine(_fpsInteractor.GetSelectedFlowerName());
                    if (_inventory != null)
                    {
                        int idx = _fpsInteractor.GetSelectedSeedVariantIndex();
                        _sb.Append("<color=#F1D598><b>Seed Type</b></color> ");
                        _sb.Append(_inventory.GetSeedTypeDisplayName(idx));
                        _sb.Append("  <color=#F1D598><b>Count</b></color> ");
                        _sb.AppendLine(_inventory.GetSeedTypeCount(idx).ToString());
                    }
                }
            }

            _statusLabel.text = _sb.ToString();
        }

        private void UpdateInventory()
        {
            if (_inventoryLabel == null)
                return;
            if (_inventory == null)
            {
                _inventoryLabel.text = string.Empty;
                return;
            }

            int selected = _fpsInteractor != null ? _fpsInteractor.GetSelectedSeedVariantIndex() : 0;
            _sb.Clear();
            _sb.Append("<color=#7FD390><b>Total Seeds</b></color> ");
            _sb.AppendLine(_inventory.seeds.ToString());
            _sb.Append("<color=#F0E9D7>Roseglow</color>  ");
            _sb.Append(_inventory.GetSeedTypeCount(0));
            _sb.Append("    <color=#F0E9D7>Amberbloom</color>  ");
            _sb.AppendLine(_inventory.GetSeedTypeCount(1).ToString());
            _sb.Append("<color=#F0E9D7>Moonpetal</color>  ");
            _sb.Append(_inventory.GetSeedTypeCount(2));
            _sb.Append("    <color=#7FD390><b>Selected</b></color> ");
            _sb.AppendLine(_inventory.GetSeedTypeDisplayName(selected));
            _sb.Append("<color=#67C8FF><b>Water</b></color> ");
            _sb.Append(_inventory.water);
            _sb.Append("    <color=#FF8E62><b>Fire</b></color> ");
            _sb.Append(_inventory.fire);
            _sb.Append("    <color=#8BE39E><b>Plants</b></color> ");
            _sb.Append(_inventory.plants);
            _inventoryLabel.text = _sb.ToString();
        }

        private void UpdateHealth()
        {
            if (_healthFillRect == null || _healthLagRect == null)
                return;

            float max = Mathf.Max(1f, defaultMaxHealth);
            float current = max;

            if (_playerHealth != null)
            {
                max = Mathf.Max(1f, _playerHealth.MaxHealth);
                current = Mathf.Clamp(_playerHealth.CurrentHealth, 0f, max);
            }

            if (_displayHealth < 0f)
                _displayHealth = current;
            if (_displayLagHealth < 0f)
                _displayLagHealth = current;

            float dt = Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
            float primarySpeed = max * (current > _displayHealth ? 7.5f : 11f);
            _displayHealth = Mathf.MoveTowards(_displayHealth, current, primarySpeed * dt);

            if (current >= _displayLagHealth)
                _displayLagHealth = current;
            else
                _displayLagHealth = Mathf.MoveTowards(_displayLagHealth, current, Mathf.Max(1f, healthLagCatchupSpeed) * dt);

            float t = max > 0f ? Mathf.Clamp01(_displayHealth / max) : 0f;
            float lagT = max > 0f ? Mathf.Clamp01(_displayLagHealth / max) : 0f;
            SetFillWidth(_healthFillRect, t);
            SetFillWidth(_healthLagRect, lagT);

            if (_healthFill != null)
            {
                _healthFill.color = Color.Lerp(HealthFillLow, HealthFillHigh, Mathf.SmoothStep(0f, 1f, t));
            }

            if (_healthLagFill != null)
            {
                _healthLagFill.color = Color.Lerp(
                    new Color(0.92f, 0.34f, 0.22f, 0.34f),
                    HealthLagColor,
                    Mathf.SmoothStep(0f, 1f, lagT));
            }

            if (_healthFrame != null)
            {
                _healthFrame.color = Color.Lerp(HealthFrameAlert, HealthFrameBase, Mathf.SmoothStep(0f, 1f, t));
            }

            if (_healthPulse != null)
            {
                float low = Mathf.Clamp01((0.33f - t) / 0.33f);
                float pulse = low * (0.40f + 0.60f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.4f)));
                _healthPulse.color = new Color(0.95f, 0.14f, 0.12f, pulse * 0.28f);
            }

            if (_healthLabel != null)
            {
                _healthLabel.gameObject.SetActive(showHealthText);
                if (showHealthText)
                    _healthLabel.text = $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}";
            }
        }

        private void UpdateAimInfo()
        {
            if (_crosshairLeft == null || _crosshairRight == null || _crosshairTop == null || _crosshairBottom == null)
                return;

            bool hasTarget = FPSAimTargeting.TryResolve(cameraSource, maxDistance, hitMask, runtime, _plantRenderer, out var target)
                             && target.HasActionableTarget;

            Color cross = hasTarget ? CrosshairHover : CrosshairIdle;
            SetCrosshairColor(cross);

            string title = string.Empty;
            string detail = string.Empty;

            if (hasTarget)
            {
                switch (target.kind)
                {
                    case FPSAimTargetKind.Pickup:
                    case FPSAimTargetKind.Grabbable:
                    {
                        var pickup = target.pickup != null ? target.pickup : (target.root != null ? target.root.GetComponentInParent<SCoLPickup>() : null);
                        if (pickup != null && _inventory != null)
                        {
                            title = _inventory.GetItemDisplayName(pickup.type, true);
                            detail = _inventory.GetItemDescription(pickup.type, true);
                        }
                        else
                        {
                            title = "Unknown item";
                            detail = "You have not discovered this item yet.";
                        }
                        break;
                    }
                    case FPSAimTargetKind.Harvestable:
                        title = target.root != null ? target.root.name : "Harvestable";
                        detail = "Harvestable";
                        break;
                    case FPSAimTargetKind.LegacyPlant:
                    case FPSAimTargetKind.CAPlant:
                    {
                        title = ResolvePlantHoverName(target);
                        detail = "Plant";
                        break;
                    }
                    case FPSAimTargetKind.Animal:
                    {
                        title = target.animal != null ? target.animal.name : "Animal";
                        detail = "Animal";
                        break;
                    }
                }
            }
            if (_aimPanel != null)
                _aimPanel.gameObject.SetActive(!string.IsNullOrEmpty(title));
            if (_aimTitleLabel != null)
                _aimTitleLabel.text = title;
            if (_aimDetailLabel != null)
                _aimDetailLabel.text = detail;
        }

        private string ResolvePlantHoverName(FPSAimTargetInfo target)
        {
            if (target.kind == FPSAimTargetKind.CAPlant && runtime != null && runtime.Grid != null && _plantRenderer != null)
            {
                var cell = runtime.Grid.Get(target.cellX, target.cellY);
                if (cell != null && cell.FlowerVariantIndex >= 0)
                {
                    string variantName = _plantRenderer.GetFlowerVariantDisplayName(cell.FlowerVariantIndex);
                    if (!string.IsNullOrWhiteSpace(variantName))
                        return variantName;
                }
            }

            if (target.root != null)
            {
                string raw = target.root.name;
                if (raw.StartsWith("Plant_"))
                    return "Plant";
                return raw;
            }

            return "Plant";
        }

        private static Font ResolveFont()
        {
            var f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null) return f;

            f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f != null) return f;

            return Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "PingFang SC", "Microsoft YaHei" }, 16);
        }

        private static void DisableLegacyHudObjects()
        {
            var oldCrosshair = FindFirstObjectByType<FPSCrosshair>();
            if (oldCrosshair != null)
                oldCrosshair.enabled = false;

            var oldCanvas = GameObject.Find("FPS Crosshair (Runtime)");
            if (oldCanvas != null)
                Destroy(oldCanvas);
        }

        private void TryResolveSimpleUIKitPrefabs()
        {
#if UNITY_EDITOR
            if (simpleUIKitPanelPrefab == null)
                simpleUIKitPanelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimpleUIKitPanelPrefabPath);
            if (simpleUIKitInnerPanelPrefab == null)
                simpleUIKitInnerPanelPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimpleUIKitInnerPanelPrefabPath);
#endif
        }

        private RectTransform CreateHudCard(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, string title, Color accent)
        {
            var card = CreateImage(parent, name, anchorMin, anchorMax, pivot, anchoredPos, size, HudCardBg).rectTransform;

            var shadow = card.gameObject.AddComponent<Shadow>();
            shadow.effectColor = new Color(0f, 0f, 0f, 0.38f);
            shadow.effectDistance = new Vector2(0f, -7f);

            CreateImage(
                card,
                "CardInset",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: size - new Vector2(8f, 8f),
                color: HudCardInset);

            CreateImage(
                card,
                "AccentBar",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(16f, -14f),
                size: new Vector2(82f, 4f),
                color: accent);

            CreateImage(
                card,
                "AccentPill",
                anchorMin: new Vector2(1f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(1f, 1f),
                anchoredPos: new Vector2(-16f, -14f),
                size: new Vector2(44f, 12f),
                color: new Color(accent.r, accent.g, accent.b, 0.18f));

            CreateText(
                card,
                "CardTitle",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(18f, -28f),
                size: new Vector2(-36f, 26f),
                fontSize: 16,
                color: accent,
                alignment: TextAnchor.MiddleLeft).text = title;

            return card;
        }

        private RectTransform CreatePanel(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color bg, bool preferInnerStyle = false)
        {
            GameObject go;
            if (useSimpleUIKitPanelPrefabs)
            {
                var panelPrefab = preferInnerStyle && simpleUIKitInnerPanelPrefab != null ? simpleUIKitInnerPanelPrefab : simpleUIKitPanelPrefab;
                if (panelPrefab != null)
                {
                    go = Instantiate(panelPrefab, parent, worldPositionStays: false);
                    go.name = name;
                }
                else
                {
                    go = new GameObject(name, typeof(RectTransform), typeof(Image));
                }
            }
            else
            {
                go = new GameObject(name, typeof(RectTransform), typeof(Image));
            }

            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.GetComponent<Image>();
            if (img != null)
            {
                if (!useSimpleUIKitPanelPrefabs || (simpleUIKitPanelPrefab == null && simpleUIKitInnerPanelPrefab == null))
                    img.color = bg;
                img.raycastTarget = false;
            }

            // HUD container visuals should never block gameplay raycasts.
            var graphics = go.GetComponentsInChildren<Graphic>(true);
            for (int i = 0; i < graphics.Length; i++)
            {
                if (graphics[i] != null)
                    graphics[i].raycastTarget = false;
            }
            return rt;
        }

        private Text CreateText(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, int fontSize, Color color, TextAnchor alignment, bool addOutline = false)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.color = color;
            text.alignment = alignment;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            if (addOutline)
            {
                var outline = go.AddComponent<Outline>();
                outline.effectColor = new Color(0f, 0f, 0f, 0.85f);
                outline.effectDistance = new Vector2(1f, -1f);
            }

            return text;
        }

        private Image CreateCrosshairLine(Transform parent, string name, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.sprite = ResolveBuiltinUISprite();
            image.raycastTarget = false;
            return image;
        }

        private void SetCrosshairColor(Color color)
        {
            if (_crosshairLeft != null) _crosshairLeft.color = color;
            if (_crosshairRight != null) _crosshairRight.color = color;
            if (_crosshairTop != null) _crosshairTop.color = color;
            if (_crosshairBottom != null) _crosshairBottom.color = color;
            if (_crosshairDot != null) _crosshairDot.color = Color.Lerp(color, Color.white, 0.25f);
        }

        private RectTransform CreateRect(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;
            return rt;
        }

        private Image CreateImage(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.pivot = pivot;
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.sprite = ResolveBuiltinUISprite();
            image.type = Image.Type.Simple;
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private void CreateSegmentTicks(RectTransform parent, int count)
        {
            count = Mathf.Max(1, count);
            for (int i = 1; i < count; i++)
            {
                float x = healthBarWidth * i / count;
                CreateImage(
                    parent,
                    $"Tick_{i}",
                    anchorMin: new Vector2(0f, 0f),
                    anchorMax: new Vector2(0f, 1f),
                    pivot: new Vector2(0.5f, 0.5f),
                    anchoredPos: new Vector2(x, 0f),
                    size: new Vector2(2f, 0f),
                    color: new Color(1f, 1f, 1f, 0.08f));
            }
        }

        private void SetFillWidth(RectTransform target, float normalized)
        {
            if (target == null)
                return;

            normalized = Mathf.Clamp01(normalized);
            target.sizeDelta = new Vector2(Mathf.Max(0f, healthBarWidth * normalized), 0f);
        }

        private static Sprite ResolveBuiltinUISprite()
        {
            if (_fallbackWhiteUISprite != null)
                return _fallbackWhiteUISprite;

            var tex = new Texture2D(1, 1, TextureFormat.RGBA32, mipChain: false)
            {
                name = "SCoL_HUD_WhiteSpriteTex",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            tex.SetPixel(0, 0, Color.white);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

            _fallbackWhiteUISprite = Sprite.Create(
                tex,
                new Rect(0f, 0f, 1f, 1f),
                new Vector2(0.5f, 0.5f),
                1f);
            _fallbackWhiteUISprite.name = "SCoL_HUD_WhiteSprite";
            return _fallbackWhiteUISprite;
        }
    }
}
