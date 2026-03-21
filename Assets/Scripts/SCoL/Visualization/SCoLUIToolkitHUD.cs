using System.Text;
using UnityEngine;
using UnityEngine.UI;
using SCoL.Inventory;
using SCoL.Combat;
using SCoL.InputLayer;
using SCoL.Settlement;
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
        private Text _statusHeroLabel;
        private Text _statusLabel;
        private RectTransform _aimPanel;
        private CanvasGroup _aimCanvasGroup;
        private Text _aimTitleLabel;
        private Text _aimDetailLabel;
        private Text _inventoryLabel;
        private Text _toolSummaryLabel;
        private Image[] _toolSlotBorders;
        private Image[] _toolSlotFills;
        private Text[] _toolSlotKeyLabels;
        private Text[] _toolSlotNameLabels;
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
        private Image _hurtOverlay;
        private Text _invulnerableLabel;
        private CanvasGroup _deathOverlayGroup;
        private Text _deathTitleLabel;
        private Text _deathDetailLabel;
        private Button _respawnButton;

        private SCoLInventory _inventory;
        private PlantVoxelRenderer _plantRenderer;
        private FPSRaycastInteractor _fpsInteractor;
        private SCoLPlayerHealth _playerHealth;
        private SCoLPlayerRespawn _playerRespawn;
        private SCoLCombatHealth _playerCombatHealth;
        private SCoLSettlementManager _settlementManager;

        private float _nextUpdateAt;
        private float _displayHealth = -1f;
        private float _displayLagHealth = -1f;
        private float _lastObservedHealth = -1f;
        private float _hurtFlashUntil;
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
        private static readonly Color HudPanelMuted = new Color(0.07f, 0.08f, 0.10f, 0.82f);
        private static readonly Color HudSlotIdleBorder = new Color(1f, 1f, 1f, 0.10f);
        private static readonly Color HudSlotIdleFill = new Color(0.07f, 0.08f, 0.10f, 0.92f);

        private void Awake()
        {
            if (runtime == null) runtime = FindFirstObjectByType<SCoLRuntime>();
            if (cameraSource == null) cameraSource = Camera.main;

            _inventory = FindFirstObjectByType<SCoLInventory>();
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
            _fpsInteractor = FindFirstObjectByType<FPSRaycastInteractor>();
            _settlementManager = FindFirstObjectByType<SCoLSettlementManager>();
            EnsurePlayerHealth();
            EnsurePlayerRespawn();
            DisableLegacyHudObjects();

            EnsureUI();
        }

        private void Update()
        {
            if (_canvas == null || _root == null)
                EnsureUI();
            if (_canvas == null)
                return;

            _canvas.enabled = visible;
            if (!visible)
                return;

            if (Time.unscaledTime < _nextUpdateAt)
            {
                UpdateHealth();
                UpdateDeathOverlay();
                return;
            }
            _nextUpdateAt = Time.unscaledTime + Mathf.Max(0.02f, updateInterval);

            if (runtime == null) runtime = FindFirstObjectByType<SCoLRuntime>();
            if (cameraSource == null) cameraSource = Camera.main;
            if (_inventory == null) _inventory = FindFirstObjectByType<SCoLInventory>();
            if (_plantRenderer == null) _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
            if (_fpsInteractor == null) _fpsInteractor = FindFirstObjectByType<FPSRaycastInteractor>();
            if (_settlementManager == null) _settlementManager = FindFirstObjectByType<SCoLSettlementManager>();
            EnsurePlayerHealth();
            EnsurePlayerRespawn();
            EnsurePlayerCombatHealth();

            UpdateStatus();
            UpdateAimInfo();
            UpdateInventory();
            UpdateToolbelt();
            UpdateHealth();
            UpdateDeathOverlay();
            Canvas.ForceUpdateCanvases();
        }

        private void EnsureUI()
        {
            if (_canvas != null && _root != null)
                return;

            _font = ResolveFont();

            var canvasGO = new GameObject("SCoL HUD (SimpleUIKit)", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(transform, worldPositionStays: false);

            _canvas = canvasGO.GetComponent<Canvas>();
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
            BuildHurtOverlay(_root);
            BuildDeathOverlay(_root);
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
                size: new Vector2(452f, 204f),
                title: "WORLD LOOP",
                accent: HudAccentWarm);

            _statusHeroLabel = CreateText(
                statusPanel,
                "StatusHero",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -54f),
                size: new Vector2(-40f, 38f),
                fontSize: 29,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);

            _statusLabel = CreateText(
                statusPanel,
                "StatusText",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0.68f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -8f),
                size: new Vector2(-40f, -16f),
                fontSize: 19,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft);

            _aimPanel = CreateHudCard(
                _root,
                "AimHoverPanel",
                anchorMin: new Vector2(1f, 0.5f),
                anchorMax: new Vector2(1f, 0.5f),
                pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-28f, 122f),
                size: new Vector2(430f, 146f),
                title: "FOCUS",
                accent: HudAccentCool);
            _aimCanvasGroup = _aimPanel.gameObject.AddComponent<CanvasGroup>();
            _aimCanvasGroup.alpha = 0f;

            _aimTitleLabel = CreateText(
                _aimPanel,
                "AimTitle",
                anchorMin: new Vector2(0f, 0.50f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -14f),
                size: new Vector2(-34f, -42f),
                fontSize: 28,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);

            _aimDetailLabel = CreateText(
                _aimPanel,
                "AimDetail",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0.54f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -6f),
                size: new Vector2(-34f, -20f),
                fontSize: 19,
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
                size: new Vector2(418f, 222f),
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
                fontSize: 21,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft);

            var toolPanel = CreateHudCard(
                _root,
                "ToolbeltPanel",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 166f),
                size: new Vector2(760f, 146f),
                title: "TOOLS",
                accent: HudAccentCool);

            _toolSummaryLabel = CreateText(
                toolPanel,
                "ToolSummary",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -36f),
                size: new Vector2(-40f, 20f),
                fontSize: 15,
                color: HudTextSecondary,
                alignment: TextAnchor.MiddleCenter);

            int toolCount = System.Enum.GetValues(typeof(FPSRaycastInteractor.ApplyTool)).Length;
            _toolSlotBorders = new Image[toolCount];
            _toolSlotFills = new Image[toolCount];
            _toolSlotKeyLabels = new Text[toolCount];
            _toolSlotNameLabels = new Text[toolCount];

            var slotRow = CreateRect(
                toolPanel,
                "ToolSlots",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 18f),
                size: new Vector2(toolCount * 124f + Mathf.Max(0, toolCount - 1) * 10f + 32f, 56f));

            const float slotWidth = 124f;
            const float slotGap = 10f;
            float totalWidth = (slotWidth * toolCount) + (slotGap * Mathf.Max(0, toolCount - 1));
            float startX = -totalWidth * 0.5f + (slotWidth * 0.5f);
            for (int i = 0; i < toolCount; i++)
            {
                float x = startX + i * (slotWidth + slotGap);
                CreateToolSlot(slotRow, i, x, slotWidth);
            }
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
            SetText(_healthBadgeLabel, "HP");

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
            SetText(_healthCaptionLabel, "VITALITY");

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

        private void EnsurePlayerRespawn()
        {
            if (_playerRespawn != null)
                return;

            _playerRespawn = FindFirstObjectByType<SCoLPlayerRespawn>();
            if (_playerRespawn != null)
                return;

            GameObject target = null;
            if (_playerHealth != null)
                target = _playerHealth.gameObject;
            else
            {
                var controller = FindFirstObjectByType<SimpleFirstPersonController>();
                if (controller != null)
                    target = controller.gameObject;
                else if (cameraSource != null)
                    target = cameraSource.transform.root.gameObject;
            }

            if (target == null)
                return;

            _playerRespawn = target.GetComponent<SCoLPlayerRespawn>();
            if (_playerRespawn == null)
                _playerRespawn = target.AddComponent<SCoLPlayerRespawn>();
        }

        private void EnsurePlayerCombatHealth()
        {
            if (_playerCombatHealth != null)
                return;

            GameObject target = null;
            if (_playerHealth != null)
                target = _playerHealth.gameObject;
            else if (_playerRespawn != null)
                target = _playerRespawn.gameObject;
            else
            {
                var controller = FindFirstObjectByType<SimpleFirstPersonController>();
                if (controller != null)
                    target = controller.gameObject;
                else if (cameraSource != null)
                    target = cameraSource.transform.root.gameObject;
            }

            if (target == null)
                return;

            _playerCombatHealth = target.GetComponent<SCoLCombatHealth>();
        }

        private void UpdateStatus()
        {
            if (_statusHeroLabel == null || _statusLabel == null)
                return;

            _sb.Clear();

            if (runtime != null)
            {
                SetText(_statusHeroLabel, $"{runtime.CurrentSeason.ToString().ToUpperInvariant()}  /  {runtime.CurrentWeather.ToString().ToUpperInvariant()}");
                _sb.Append("<color=#F1D598><b>View</b></color> ");
                _sb.AppendLine(runtime.ViewMode.ToString().ToUpperInvariant());
                _sb.Append("<color=#F1D598><b>Fire</b></color> ");
                _sb.AppendLine(runtime.OverlayFire ? "ACTIVE" : "OFF");
            }
            else
            {
                SetText(_statusHeroLabel, "WORLD STATE");
            }

            if (_fpsInteractor != null)
            {
                _sb.Append("<color=#F1D598><b>Tool</b></color> ");
                _sb.AppendLine(GetToolLabel(_fpsInteractor.currentTool));
                if (_fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Seed)
                {
                    _sb.Append("<color=#F1D598><b>Focus</b></color> ");
                    _sb.AppendLine(_fpsInteractor.GetSelectedFlowerName());
                }
            }

            if (_settlementManager != null)
            {
                _sb.Append("<color=#67C8FF><b>Zone</b></color> ");
                _sb.AppendLine(_settlementManager.StatusLine.ToUpperInvariant());
                _sb.Append("<color=#8BE39E><b>Goal</b></color> ");
                _sb.AppendLine(_settlementManager.GoalLine);
            }

            SetText(_statusLabel, _sb.ToString());
        }

        private void UpdateInventory()
        {
            if (_inventoryLabel == null)
                return;
            if (_inventory == null)
            {
                SetText(_inventoryLabel, string.Empty);
                return;
            }

            int selected = _fpsInteractor != null ? _fpsInteractor.GetSelectedSeedVariantIndex() : 0;
            _sb.Clear();
            _sb.Append("<size=24><color=#7FD390><b>Seeds</b></color> ");
            _sb.Append(_inventory.seeds);
            _sb.AppendLine("</size>");
            _sb.Append("<color=#F4DFA2><b>Types</b></color> ");
            _sb.AppendLine(_inventory.GetSeedTypeSummary());
            _sb.Append("<color=#7FD390><b>Held</b></color> ");
            _sb.AppendLine(_inventory.GetSeedTypeDisplayName(selected));
            _sb.Append("<color=#67C8FF><b>Water</b></color> ");
            _sb.Append(_inventory.water);
            _sb.Append("    <color=#FF8E62><b>Fire</b></color> ");
            _sb.Append(_inventory.fire);
            _sb.Append("    <color=#8BE39E><b>Plants</b></color> ");
            _sb.Append(_inventory.plants);
            _sb.Append("    <color=#D2D5DE><b>Stone</b></color> ");
            _sb.Append(_inventory.stones);
            if (_settlementManager != null)
            {
                _sb.AppendLine();
                _sb.Append("<color=#F4DFA2><b>Storage</b></color> ");
                _sb.Append(_settlementManager.StorageSummary);
            }
            SetText(_inventoryLabel, _sb.ToString());
        }

        private void UpdateToolbelt()
        {
            if (_toolSummaryLabel == null || _toolSlotBorders == null || _toolSlotFills == null)
                return;

            var activeTool = _fpsInteractor != null ? _fpsInteractor.currentTool : FPSRaycastInteractor.ApplyTool.Seed;
            SetText(_toolSummaryLabel, GetToolSummary(activeTool));

            for (int i = 0; i < _toolSlotBorders.Length; i++)
            {
                var tool = (FPSRaycastInteractor.ApplyTool)i;
                bool selected = tool == activeTool;
                Color accent = GetToolAccent(tool);
                float pulse = selected ? (0.78f + 0.18f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f))) : 0f;

                if (_toolSlotBorders[i] != null)
                    _toolSlotBorders[i].color = selected ? accent : HudSlotIdleBorder;
                if (_toolSlotFills[i] != null)
                    _toolSlotFills[i].color = selected
                        ? new Color(accent.r * 0.22f, accent.g * 0.22f, accent.b * 0.22f, pulse)
                        : HudSlotIdleFill;
                if (_toolSlotKeyLabels[i] != null)
                {
                    SetText(_toolSlotKeyLabels[i], GetToolSlotKeyLabel(i));
                    _toolSlotKeyLabels[i].color = selected ? HudTextPrimary : new Color(1f, 1f, 1f, 0.55f);
                }
                if (_toolSlotNameLabels[i] != null)
                    _toolSlotNameLabels[i].color = selected ? HudTextPrimary : HudTextSecondary;
            }
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

            if (_lastObservedHealth < 0f)
            {
                _lastObservedHealth = current;
            }
            else if (current < _lastObservedHealth - 0.01f)
            {
                _hurtFlashUntil = Time.unscaledTime + 0.34f;
                FPSGameFeel.Shake(0.045f, 0.09f);
                _lastObservedHealth = current;
            }
            else
            {
                _lastObservedHealth = current;
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
                    SetText(_healthLabel, $"{Mathf.RoundToInt(current)} / {Mathf.RoundToInt(max)}");
            }

            UpdateHurtOverlay();
        }

        private void UpdateDeathOverlay()
        {
            if (_deathOverlayGroup == null)
                return;

            bool isDead = _playerRespawn != null && _playerRespawn.IsDead;
            _deathOverlayGroup.alpha = isDead ? 1f : 0f;
            _deathOverlayGroup.interactable = false;
            _deathOverlayGroup.blocksRaycasts = false;

            if (_deathTitleLabel != null)
                SetText(_deathTitleLabel, "YOU DIED");
            if (_deathDetailLabel != null)
                SetText(_deathDetailLabel, isDead ? $"Wolves and the wild got you.\n{GetRespawnPromptDetail()}" : string.Empty);
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
                            detail = $"{_inventory.GetItemDescription(pickup.type, true)}\n{GetPrimaryPromptRich()} Pick up";
                        }
                        else
                        {
                            title = "Unknown item";
                            detail = $"You have not discovered this item yet.\n{GetPrimaryPromptRich()} Pick up";
                        }
                        break;
                    }
                    case FPSAimTargetKind.Harvestable:
                        title = target.root != null ? target.root.name : "Harvestable";
                        detail = "No direct click action";
                        break;
                    case FPSAimTargetKind.LegacyPlant:
                    case FPSAimTargetKind.CAPlant:
                    {
                        title = ResolvePlantHoverName(target);
                        string pickLine = $"{GetPrimaryPromptRich(HudAccentGreen)} Pick plant";
                        if (_fpsInteractor != null && _fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Water)
                            detail = $"{pickLine}\n{GetSecondaryPromptRich(HudAccentCool)} Water plant";
                        else if (_fpsInteractor != null && _fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Fire)
                            detail = $"{pickLine}\n{GetSecondaryPromptRich(new Color(1f, 0.56f, 0.38f, 0.98f))} Start fire";
                        else
                            detail = pickLine;
                        break;
                    }
                    case FPSAimTargetKind.Animal:
                    {
                        title = target.animal != null ? target.animal.name : "Animal";
                        bool plantTool = _fpsInteractor != null && _fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Plant;
                        bool stoneTool = _fpsInteractor != null && _fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Stone;
                        detail = stoneTool
                            ? $"{GetSecondaryPromptRich()} Use tool: throw stone"
                            : plantTool
                            ? $"{GetSecondaryPromptRich()} Use tool: feed animal"
                            : GetAnimalPromptDetail();
                        break;
                    }
                    case FPSAimTargetKind.SettlementCenterpiece:
                    {
                        title = "Settlement Core";
                        if (_settlementManager != null && !_settlementManager.IsActivated)
                            detail = $"{GetSecondaryPromptRich(HudAccentGreen)} Activate safe zone";
                        else if (_settlementManager != null)
                            detail = $"Level {_settlementManager.CurrentLevel}  /  {_settlementManager.GoalLine}";
                        else
                            detail = "Settlement anchor";
                        break;
                    }
                    case FPSAimTargetKind.SettlementStorage:
                    {
                        title = "Supply Crate";
                        string toolLabel = _settlementManager != null
                            ? _settlementManager.GetCurrentToolStorageHint(_fpsInteractor, _inventory)
                            : "supplies";
                        string summary = _settlementManager != null ? _settlementManager.StorageSummary : "Storage offline";
                        detail = _settlementManager != null && !_settlementManager.IsActivated
                            ? "Offline until the settlement core is activated."
                            : $"{GetPrimaryPromptRich(HudAccentCool)} Withdraw {toolLabel}\n{GetDropPromptRich(HudAccentGreen)} Store {toolLabel}\n{summary}";
                        break;
                    }
                    case FPSAimTargetKind.SettlementBarrier:
                    {
                        title = "Settlement Fence";
                        detail = _settlementManager != null && _settlementManager.IsActivated
                            ? "Home perimeter. Wolves slow down and avoid this zone."
                            : "Barrier shell. Activate the core to power the safe zone.";
                        break;
                    }
                }
            }

            SetAimPanelVisible(!string.IsNullOrEmpty(title));
            if (_aimTitleLabel != null)
                SetText(_aimTitleLabel, title);
            if (_aimDetailLabel != null)
                SetText(_aimDetailLabel, detail);
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
            var f = Font.CreateDynamicFontFromOSFont(
                new[] { "Avenir Next", "Trebuchet MS", "Arial", "Helvetica", "PingFang SC", "Microsoft YaHei" },
                18);
            if (f != null) return f;

            f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (f != null) return f;

            f = Resources.GetBuiltinResource<Font>("Arial.ttf");
            if (f != null) return f;

            return Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "PingFang SC", "Microsoft YaHei" }, 16);
        }

        private void SetAimPanelVisible(bool isVisible)
        {
            if (_aimCanvasGroup == null)
                return;

            _aimCanvasGroup.alpha = isVisible ? 1f : 0f;
            _aimCanvasGroup.interactable = false;
            _aimCanvasGroup.blocksRaycasts = false;
        }

        private string GetToolSummary(FPSRaycastInteractor.ApplyTool tool)
        {
            switch (tool)
            {
                case FPSRaycastInteractor.ApplyTool.Seed:
                {
                    string flowerName = _fpsInteractor != null ? _fpsInteractor.GetSelectedFlowerName() : "Roseglow";
                    int count = _inventory != null && _fpsInteractor != null
                        ? _inventory.GetSeedTypeCount(_fpsInteractor.GetSelectedSeedVariantIndex())
                        : 0;
                    return $"{GetSecondaryPromptRich(HudAccentWarm)} Use: plant {flowerName}   {GetDropPromptRich(HudAccentGreen)} Drop seed   Stock {count}";
                }
                case FPSRaycastInteractor.ApplyTool.Water:
                    return $"{GetPrimaryPromptRich(HudAccentCool)} Pick up: fill at pond   {GetSecondaryPromptRich(HudAccentCool)} Use: water ground";
                case FPSRaycastInteractor.ApplyTool.Fire:
                    return $"{GetSecondaryPromptRich(new Color(1f, 0.56f, 0.38f, 0.98f))} Use: start fire   {GetDropPromptRich(HudAccentGreen)} Drop branch";
                case FPSRaycastInteractor.ApplyTool.Plant:
                    return $"{GetPrimaryPromptRich(HudAccentGreen)} Pick plant   {GetSecondaryPromptRich(HudAccentGreen)} Use: feed animal   {GetDropPromptRich(HudAccentGreen)} Drop plant";
                case FPSRaycastInteractor.ApplyTool.Stone:
                    return $"{GetSecondaryPromptRich(new Color(0.82f, 0.84f, 0.92f, 0.98f))} Use: throw stone   {GetDropPromptRich(HudAccentGreen)} Drop stone";
                default:
                    return $"{GetToolSwitchPromptRich()} Switch tools.";
            }
        }

        private bool UseGamepadPrompts()
        {
            return GameplayInputFacade.Player != null && GameplayInputFacade.Player.UseGamepadPrompts;
        }

        private string GetPrimaryPromptRich() => GetPrimaryPromptRich(HudAccentCool);

        private string GetPrimaryPromptRich(Color color)
        {
            string label = UseGamepadPrompts() ? "RT" : "LMB";
            return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{label}</b></color>";
        }

        private string GetSecondaryPromptRich() => GetSecondaryPromptRich(new Color(1f, 0.69f, 0.51f, 0.98f));

        private string GetSecondaryPromptRich(Color color)
        {
            string label = UseGamepadPrompts() ? "LT" : "RMB";
            return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{label}</b></color>";
        }

        private string GetDropPromptRich() => GetDropPromptRich(HudAccentGreen);

        private string GetDropPromptRich(Color color)
        {
            string label = UseGamepadPrompts() ? "X" : "Q";
            return $"<color=#{ColorUtility.ToHtmlStringRGB(color)}><b>{label}</b></color>";
        }

        private string GetToolSwitchPromptRich()
        {
            if (!UseGamepadPrompts())
                return "<color=#F1D598><b>Wheel</b></color> or <color=#F1D598><b>1-5</b></color>";
            return "<color=#F1D598><b>LB/RB</b></color> or <color=#F1D598><b>D-PAD</b></color>";
        }

        private string GetAnimalPromptDetail()
        {
            if (UseGamepadPrompts())
                return $"Use {GetToolSwitchPromptRich()} to pick Plant or Stone, then {GetSecondaryPromptRich()} use it";
            return $"Use {GetToolSwitchPromptRich()} to pick Plant or Stone, then {GetSecondaryPromptRich()} use it";
        }

        private string GetRespawnPromptDetail()
        {
            if (UseGamepadPrompts())
                return "Press the <color=#F1D598><b>Y</b></color> button to respawn at a new location with full health.";
            return "Press <color=#F1D598><b>Y</b></color> to respawn at a new location with full health.";
        }

        private string GetToolSlotKeyLabel(int slotIndex)
        {
            if (UseGamepadPrompts())
                return string.Empty;
            return (slotIndex + 1).ToString();
        }

        private string GetToolLabel(FPSRaycastInteractor.ApplyTool tool)
        {
            switch (tool)
            {
                case FPSRaycastInteractor.ApplyTool.Seed:
                    return "Seed";
                case FPSRaycastInteractor.ApplyTool.Water:
                    return "Water";
                case FPSRaycastInteractor.ApplyTool.Fire:
                    return "Fire";
                case FPSRaycastInteractor.ApplyTool.Plant:
                    return "Plant";
                case FPSRaycastInteractor.ApplyTool.Stone:
                    return "Stone";
                default:
                    return tool.ToString();
            }
        }

        private Color GetToolAccent(FPSRaycastInteractor.ApplyTool tool)
        {
            switch (tool)
            {
                case FPSRaycastInteractor.ApplyTool.Seed:
                    return HudAccentWarm;
                case FPSRaycastInteractor.ApplyTool.Water:
                    return HudAccentCool;
                case FPSRaycastInteractor.ApplyTool.Fire:
                    return new Color(1f, 0.56f, 0.38f, 0.98f);
                case FPSRaycastInteractor.ApplyTool.Plant:
                    return HudAccentGreen;
                case FPSRaycastInteractor.ApplyTool.Stone:
                    return new Color(0.82f, 0.84f, 0.92f, 0.98f);
                default:
                    return HudTextSecondary;
            }
        }

        private void CreateToolSlot(RectTransform parent, int slotIndex, float anchoredX, float width)
        {
            var border = CreateImage(
                parent,
                $"ToolSlot_{slotIndex + 1}",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(anchoredX, 0f),
                size: new Vector2(width, 56f),
                color: HudSlotIdleBorder);
            _toolSlotBorders[slotIndex] = border;

            var fill = CreateImage(
                border.transform,
                "Fill",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(width - 4f, 52f),
                color: HudSlotIdleFill);
            _toolSlotFills[slotIndex] = fill;

            CreateImage(
                fill.transform,
                "TopRim",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -1f),
                size: new Vector2(0f, 2f),
                color: new Color(1f, 1f, 1f, 0.10f));

            _toolSlotKeyLabels[slotIndex] = CreateText(
                fill.transform,
                "Key",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(10f, -8f),
                size: new Vector2(28f, 18f),
                fontSize: 14,
                color: new Color(1f, 1f, 1f, 0.55f),
                alignment: TextAnchor.MiddleLeft);
            SetText(_toolSlotKeyLabels[slotIndex], GetToolSlotKeyLabel(slotIndex));

            _toolSlotNameLabels[slotIndex] = CreateText(
                fill.transform,
                "Name",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, 6f),
                size: new Vector2(width - 24f, 22f),
                fontSize: 20,
                color: HudTextSecondary,
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
            SetText(_toolSlotNameLabels[slotIndex], GetToolLabel((FPSRaycastInteractor.ApplyTool)slotIndex).ToUpperInvariant());
        }

        private static void SetText(Text label, string value)
        {
            if (label == null)
                return;

            value ??= string.Empty;
            label.text = value;

            if (label.font != null && value.Length > 0)
                label.font.RequestCharactersInTexture(value, label.fontSize, label.fontStyle);

            label.SetAllDirty();
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

        private void BuildDeathOverlay(RectTransform parent)
        {
            var overlay = CreateImage(
                parent,
                "DeathOverlay",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: new Color(0.01f, 0.01f, 0.02f, 0.72f));
            overlay.raycastTarget = false;

            _deathOverlayGroup = overlay.gameObject.AddComponent<CanvasGroup>();
            _deathOverlayGroup.alpha = 0f;
            _deathOverlayGroup.interactable = false;
            _deathOverlayGroup.blocksRaycasts = false;

            var card = CreateHudCard(
                overlay.transform,
                "DeathCard",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, 10f),
                size: new Vector2(480f, 260f),
                title: "RESPAWN",
                accent: new Color(0.98f, 0.50f, 0.40f, 0.98f));

            _deathTitleLabel = CreateText(
                card,
                "DeathTitle",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -72f),
                size: new Vector2(-46f, 46f),
                fontSize: 36,
                color: new Color(0.98f, 0.92f, 0.90f, 0.98f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);

            _deathDetailLabel = CreateText(
                card,
                "DeathDetail",
                anchorMin: new Vector2(0f, 0.5f),
                anchorMax: new Vector2(1f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -8f),
                size: new Vector2(-56f, 110f),
                fontSize: 22,
                color: HudTextSecondary,
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
        }

        private void BuildHurtOverlay(RectTransform parent)
        {
            _hurtOverlay = CreateImage(
                parent,
                "HurtOverlay",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: new Color(0.78f, 0.08f, 0.06f, 0f));

            _invulnerableLabel = CreateText(
                parent,
                "InvulnerableLabel",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 196f),
                size: new Vector2(260f, 28f),
                fontSize: 18,
                color: new Color(1f, 0.88f, 0.82f, 0.9f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
            _invulnerableLabel.gameObject.SetActive(false);
        }

        private void UpdateHurtOverlay()
        {
            if (_hurtOverlay == null)
                return;

            float flashT = Mathf.Clamp01((_hurtFlashUntil - Time.unscaledTime) / 0.34f);
            float flashAlpha = flashT * flashT * 0.34f;
            bool invulnerable = _playerCombatHealth != null &&
                                _playerCombatHealth.IsDamageInvulnerable &&
                                !(_playerRespawn != null && _playerRespawn.IsDead);
            float invulnerablePulse = invulnerable
                ? (0.06f + 0.04f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 14f)))
                : 0f;

            _hurtOverlay.color = new Color(0.78f, 0.08f, 0.06f, Mathf.Max(flashAlpha, invulnerablePulse));

            if (_invulnerableLabel != null)
            {
                _invulnerableLabel.gameObject.SetActive(invulnerable);
                if (invulnerable)
                    SetText(_invulnerableLabel, "RECOVERING");
            }
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

        private Button CreateButton(Transform parent, string name, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, string label, Color color)
        {
            var image = CreateImage(parent, name, anchorMin, anchorMax, pivot, anchoredPos, size, color);
            image.raycastTarget = true;

            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.14f);
            colors.pressedColor = Color.Lerp(color, Color.black, 0.12f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(color.r * 0.45f, color.g * 0.45f, color.b * 0.45f, 0.42f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.targetGraphic = image;

            CreateImage(
                image.transform,
                "ButtonTopRim",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -2f),
                size: new Vector2(0f, 3f),
                color: new Color(1f, 1f, 1f, 0.18f));

            CreateText(
                image.transform,
                "ButtonLabel",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                fontSize: 22,
                color: new Color(0.97f, 0.98f, 0.94f, 0.98f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true).text = label;

            return button;
        }

        private void OnRespawnClicked()
        {
            if (_playerRespawn == null)
                return;

            if (_playerRespawn.RespawnAtRandomLocation())
                UpdateDeathOverlay();
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
