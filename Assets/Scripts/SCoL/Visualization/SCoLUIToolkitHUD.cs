using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
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
        private RectTransform _safeZoneBadge;
        private CanvasGroup _safeZoneBadgeGroup;
        private Image _safeZoneBadgeFill;
        private Text _safeZoneBadgeLabel;
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
        private CanvasGroup _introOverlayGroup;
        private Text _introTitleLabel;
        private Text _introBodyLabel;
        private Text _introHintLabel;
        private bool _introOpen = true;
        private CanvasGroup _deathOverlayGroup;
        private Text _deathTitleLabel;
        private Text _deathDetailLabel;
        private Button _respawnButton;
        private CanvasGroup _storageOverlayGroup;
        private RectTransform _storageOverlayPanel;
        private Text _storageTitleLabel;
        private Text _storageHintLabel;
        private Button _storageCloseButton;
        private RectTransform _storageChestGrid;
        private RectTransform _storagePlayerGrid;
        private Button[] _storageChestButtons;
        private Image[] _storageChestIconImages;
        private Text[] _storageChestNameLabels;
        private Text[] _storageChestCountLabels;
        private Button[] _storagePlayerButtons;
        private Image[] _storagePlayerIconImages;
        private Text[] _storagePlayerNameLabels;
        private Text[] _storagePlayerCountLabels;
        private RectTransform _storageDragGhost;
        private Text _storageDragGhostLabel;
        private Text _storageDragGhostCountLabel;
        private bool _storageDragActive;
        private bool _storageDragFromChest;
        private int _storageDragSlotIndex = -1;
        private bool _storageDragDropConsumed;
        private bool _storageSelectedChest = true;
        private int _storageSelectedSlot;
        private float _nextStorageNavigateAt;
        private bool _storageWasOpen;

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
        private readonly Dictionary<string, Sprite> _storageIconCache = new Dictionary<string, Sprite>();
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

            if (runtime == null) runtime = FindFirstObjectByType<SCoLRuntime>();
            if (cameraSource == null) cameraSource = Camera.main;
            if (_inventory == null) _inventory = FindFirstObjectByType<SCoLInventory>();
            if (_plantRenderer == null) _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
            if (_fpsInteractor == null) _fpsInteractor = FindFirstObjectByType<FPSRaycastInteractor>();
            if (_settlementManager == null) _settlementManager = FindFirstObjectByType<SCoLSettlementManager>();
            EnsurePlayerHealth();
            EnsurePlayerRespawn();
            EnsurePlayerCombatHealth();

            // These overlays need per-frame input polling so short button presses do not get dropped.
            UpdateIntroductionOverlay();
            UpdateStorageOverlay();

            if (Time.unscaledTime < _nextUpdateAt)
            {
                UpdateHealth();
                UpdateDeathOverlay();
                return;
            }
            _nextUpdateAt = Time.unscaledTime + Mathf.Max(0.02f, updateInterval);

            UpdateStatus();
            UpdateSafeZoneBadge();
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
            BuildIntroductionOverlay(_root);
            BuildDeathOverlay(_root);
            BuildStorageOverlay(_root);
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

            _safeZoneBadge = CreateImage(
                _root,
                "SafeZoneBadge",
                anchorMin: new Vector2(1f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(1f, 1f),
                anchoredPos: new Vector2(-28f, -26f),
                size: new Vector2(222f, 54f),
                color: new Color(0.10f, 0.15f, 0.12f, 0.92f)).rectTransform;
            _safeZoneBadgeGroup = _safeZoneBadge.gameObject.AddComponent<CanvasGroup>();
            _safeZoneBadgeGroup.alpha = 0f;
            _safeZoneBadgeGroup.interactable = false;
            _safeZoneBadgeGroup.blocksRaycasts = false;

            _safeZoneBadgeFill = CreateImage(
                _safeZoneBadge,
                "Inset",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(214f, 46f),
                color: new Color(0.22f, 0.72f, 0.38f, 0.96f));

            CreateImage(
                _safeZoneBadgeFill.transform,
                "Glow",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(214f, 46f),
                color: new Color(1f, 1f, 1f, 0.07f));

            _safeZoneBadgeLabel = CreateText(
                _safeZoneBadgeFill.transform,
                "Label",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(190f, 30f),
                fontSize: 24,
                color: new Color(0.94f, 1f, 0.94f, 0.98f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);

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
                anchoredPos: new Vector2(0f, 34f),
                size: new Vector2(628f, 108f),
                color: new Color(0.02f, 0.03f, 0.05f, 0.18f)).rectTransform;

            var panelShadow = panel.gameObject.AddComponent<Shadow>();
            panelShadow.effectColor = new Color(0f, 0f, 0f, 0.45f);
            panelShadow.effectDistance = new Vector2(0f, -10f);

            _healthFrame = CreateImage(
                panel,
                "OuterFrame",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(18f, 0f),
                size: new Vector2(560f, 70f),
                color: HealthFrameBase);

            CreateImage(
                _healthFrame.transform,
                "FrameInset",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(548f, 58f),
                color: new Color(0.06f, 0.08f, 0.11f, 0.92f));

            _healthPulse = CreateImage(
                _healthFrame.transform,
                "PulseGlow",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(572f, 82f),
                color: new Color(0.95f, 0.18f, 0.14f, 0f));

            var badge = CreateImage(
                panel,
                "Badge",
                anchorMin: new Vector2(0f, 0.5f),
                anchorMax: new Vector2(0f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(64f, 0f),
                size: new Vector2(86f, 56f),
                color: new Color(0.09f, 0.12f, 0.15f, 0.96f));

            CreateImage(
                badge.transform,
                "BadgeInner",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(76f, 46f),
                color: new Color(0.17f, 0.23f, 0.29f, 0.96f));

            _healthBadgeLabel = CreateText(
                badge.transform,
                "BadgeLabel",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(82f, 30f),
                fontSize: 22,
                color: new Color(0.80f, 0.91f, 1f, 0.98f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
            SetText(_healthBadgeLabel, "HP");

            _healthCaptionLabel = CreateText(
                panel,
                "HealthCaption",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(126f, -18f),
                size: new Vector2(260f, 30f),
                fontSize: 16,
                color: new Color(0.63f, 0.79f, 0.88f, 0.92f),
                alignment: TextAnchor.MiddleLeft);
            SetText(_healthCaptionLabel, "PLAYER VITALS");

            _healthTrackRect = CreateRect(
                panel,
                "TrackRoot",
                anchorMin: new Vector2(0f, 0.5f),
                anchorMax: new Vector2(0f, 0.5f),
                pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(126f, 0f),
                size: new Vector2(healthBarWidth, healthBarHeight));

            CreateImage(
                _healthTrackRect,
                "TrackShadow",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -3f),
                size: Vector2.zero,
                color: new Color(0f, 0f, 0f, 0.22f));

            CreateImage(
                _healthTrackRect,
                "TrackBase",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: new Color(0.08f, 0.11f, 0.14f, 0.96f));

            CreateImage(
                _healthTrackRect,
                "TrackTopRim",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -1f),
                size: new Vector2(0f, 3f),
                color: new Color(0.86f, 0.96f, 1f, 0.12f));

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
                size: new Vector2(0f, 5f),
                color: new Color(1f, 1f, 1f, 0.16f));

            CreateSegmentTicks(_healthTrackRect, 10);

            _healthLabel = CreateText(
                panel,
                "HealthText",
                anchorMin: new Vector2(1f, 0.5f),
                anchorMax: new Vector2(1f, 0.5f),
                pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-20f, 0f),
                size: new Vector2(160f, 40f),
                fontSize: 24,
                color: new Color(0.97f, 0.95f, 0.90f, 0.98f),
                alignment: TextAnchor.MiddleRight,
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

        private void UpdateSafeZoneBadge()
        {
            if (_safeZoneBadgeGroup == null || _safeZoneBadgeFill == null || _safeZoneBadgeLabel == null)
                return;

            bool show = _settlementManager != null && _settlementManager.IsActivated;
            _safeZoneBadgeGroup.alpha = show ? 1f : 0f;
            if (!show)
                return;

            bool inside = _settlementManager.PlayerInsideSafeZone;
            _safeZoneBadgeFill.color = inside
                ? new Color(0.21f, 0.72f, 0.38f, 0.96f)
                : new Color(0.62f, 0.46f, 0.18f, 0.94f);
            _safeZoneBadgeLabel.color = inside
                ? new Color(0.94f, 1f, 0.94f, 0.98f)
                : new Color(1f, 0.95f, 0.84f, 0.98f);
            SetText(_safeZoneBadgeLabel, inside ? "SAFE ZONE" : "OUTSIDE HOME");
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
                _healthFill.color = Color.Lerp(
                    new Color(0.94f, 0.23f, 0.21f, 0.98f),
                    new Color(0.22f, 0.84f, 0.61f, 0.98f),
                    Mathf.SmoothStep(0f, 1f, t));
            }

            if (_healthLagFill != null)
            {
                _healthLagFill.color = Color.Lerp(
                    new Color(0.95f, 0.42f, 0.30f, 0.30f),
                    new Color(0.95f, 0.78f, 0.44f, 0.42f),
                    Mathf.SmoothStep(0f, 1f, lagT));
            }

            if (_healthFrame != null)
            {
                _healthFrame.color = Color.Lerp(
                    new Color(0.58f, 0.14f, 0.15f, 0.98f),
                    new Color(0.18f, 0.24f, 0.30f, 0.98f),
                    Mathf.SmoothStep(0f, 1f, t));
            }

            if (_healthPulse != null)
            {
                float low = Mathf.Clamp01((0.33f - t) / 0.33f);
                float pulse = low * (0.40f + 0.60f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6.4f)));
                _healthPulse.color = new Color(1f, 0.16f, 0.14f, pulse * 0.24f);
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

            if (_settlementManager != null && _settlementManager.IsStorageUiOpen)
            {
                SetCrosshairColor(CrosshairIdle);
                SetAimPanelVisible(false);
                return;
            }

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
                        string summary = _settlementManager != null ? _settlementManager.StorageSummary : "Storage offline";
                        detail = _settlementManager != null && !_settlementManager.IsActivated
                            ? "Offline until the settlement core is activated."
                            : "<color=#67C8FF><b>F</b></color> Open chest\nMove items between chest and inventory\n" + summary;
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
                    return $"{GetPrimaryPromptRich(HudAccentGreen)} Pick plant   {GetSecondaryPromptRich(HudAccentGreen)} Use: feed animal / eat to heal   {GetDropPromptRich(HudAccentGreen)} Drop plant";
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

        private string GetIntroductionBody()
        {
            return "Build a safe home, grow flowers, and keep wolves away.\n\n"
                 + "Left stick moves and right stick looks.\n"
                 + "RT picks up nearby items and fills the watering can at ponds.\n"
                 + "LT uses the current item: plant seeds, water ground, start fires, feed animals, throw stones, or eat plants to heal.\n"
                 + "X drops the current item.\n"
                 + "LB / RB switches tools. When Seed is active, D-pad left / right cycles seed types.\n"
                 + "Aim at the chest and press A to open storage.";
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

        private void BuildIntroductionOverlay(RectTransform parent)
        {
            var overlay = CreateImage(
                parent,
                "IntroductionOverlay",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: new Color(0.01f, 0.01f, 0.02f, 0.74f));
            overlay.raycastTarget = true;

            _introOverlayGroup = overlay.gameObject.AddComponent<CanvasGroup>();
            _introOverlayGroup.alpha = 0f;
            _introOverlayGroup.interactable = false;
            _introOverlayGroup.blocksRaycasts = false;

            var card = CreateHudCard(
                overlay.transform,
                "IntroductionCard",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, 8f),
                size: new Vector2(760f, 430f),
                title: "INTRODUCTION",
                accent: HudAccentCool);

            _introTitleLabel = CreateText(
                card,
                "IntroductionTitle",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -70f),
                size: new Vector2(-56f, 42f),
                fontSize: 34,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);

            _introBodyLabel = CreateText(
                card,
                "IntroductionBody",
                anchorMin: new Vector2(0f, 0.5f),
                anchorMax: new Vector2(1f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, 4f),
                size: new Vector2(-72f, 208f),
                fontSize: 22,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft,
                addOutline: false);

            _introHintLabel = CreateText(
                card,
                "IntroductionHint",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 28f),
                size: new Vector2(-72f, 44f),
                fontSize: 21,
                color: HudAccentWarm,
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
        }

        private void UpdateIntroductionOverlay()
        {
            if (_introOverlayGroup == null)
                return;

            bool reopenPressed = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
            bool closePressed = Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame;

            if (!_introOpen && reopenPressed)
                _introOpen = true;
            else if (_introOpen && closePressed)
                _introOpen = false;

            _introOverlayGroup.alpha = _introOpen ? 1f : 0f;
            _introOverlayGroup.interactable = _introOpen;
            _introOverlayGroup.blocksRaycasts = _introOpen;

            if (_introTitleLabel != null)
                SetText(_introTitleLabel, "WELCOME TO YOUR SETTLEMENT");
            if (_introBodyLabel != null)
                SetText(_introBodyLabel, GetIntroductionBody());
            if (_introHintLabel != null)
                SetText(_introHintLabel, "Press B to close. Press --- to open again.");
        }

        private void BuildStorageOverlay(RectTransform parent)
        {
            var overlay = CreateImage(
                parent,
                "StorageOverlay",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: new Color(0.01f, 0.01f, 0.02f, 0.78f));
            overlay.raycastTarget = true;

            _storageOverlayGroup = overlay.gameObject.AddComponent<CanvasGroup>();
            _storageOverlayGroup.alpha = 0f;
            _storageOverlayGroup.interactable = false;
            _storageOverlayGroup.blocksRaycasts = false;

            _storageOverlayPanel = CreateHudCard(
                overlay.transform,
                "StoragePanel",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, 0f),
                size: new Vector2(980f, 760f),
                title: "STORAGE",
                accent: HudAccentCool);
            var panelImage = _storageOverlayPanel.GetComponent<Image>();
            if (panelImage != null)
                panelImage.raycastTarget = true;
            var panelDrag = _storageOverlayPanel.gameObject.AddComponent<SCoLUIDraggableWindow>();
            panelDrag.dragTarget = _storageOverlayPanel;

            _storageTitleLabel = CreateText(
                _storageOverlayPanel,
                "StorageTitle",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -58f),
                size: new Vector2(-180f, 34f),
                fontSize: 26,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);

            _storageHintLabel = CreateText(
                _storageOverlayPanel,
                "StorageHint",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -98f),
                size: new Vector2(-180f, 44f),
                fontSize: 18,
                color: HudTextSecondary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: false);

            _storageCloseButton = CreateButton(
                _storageOverlayPanel,
                "StorageClose",
                anchorMin: new Vector2(1f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(1f, 1f),
                anchoredPos: new Vector2(-28f, -28f),
                size: new Vector2(120f, 44f),
                label: "CLOSE",
                color: new Color(0.54f, 0.18f, 0.16f, 0.96f));
            _storageCloseButton.onClick.AddListener(OnStorageCloseClicked);

            var chestLabel = CreateText(
                _storageOverlayPanel,
                "ChestSectionLabel",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -154f),
                size: new Vector2(-64f, 28f),
                fontSize: 22,
                color: HudAccentWarm,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);
            SetText(chestLabel, "Chest");

            _storageChestGrid = CreateRect(
                _storageOverlayPanel,
                "ChestGrid",
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -194f),
                size: new Vector2(860f, 226f));
            var chestDropSurface = CreateImage(
                _storageChestGrid,
                "ChestDropSurface",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: new Color(0f, 0f, 0f, 0.001f));
            chestDropSurface.raycastTarget = true;
            var chestDropZone = chestDropSurface.gameObject.AddComponent<SCoLStorageUIDropZone>();
            chestDropZone.hud = this;
            chestDropZone.dropToChest = true;

            var playerLabel = CreateText(
                _storageOverlayPanel,
                "PlayerSectionLabel",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 270f),
                size: new Vector2(-64f, 28f),
                fontSize: 22,
                color: HudAccentGreen,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);
            SetText(playerLabel, "Inventory");

            _storagePlayerGrid = CreateRect(
                _storageOverlayPanel,
                "PlayerGrid",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 48f),
                size: new Vector2(860f, 226f));
            var playerDropSurface = CreateImage(
                _storagePlayerGrid,
                "PlayerDropSurface",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: new Color(0f, 0f, 0f, 0.001f));
            playerDropSurface.raycastTarget = true;
            var playerDropZone = playerDropSurface.gameObject.AddComponent<SCoLStorageUIDropZone>();
            playerDropZone.hud = this;
            playerDropZone.dropToChest = false;

            _storageChestButtons = new Button[12];
            _storageChestIconImages = new Image[12];
            _storageChestNameLabels = new Text[12];
            _storageChestCountLabels = new Text[12];
            _storagePlayerButtons = new Button[12];
            _storagePlayerIconImages = new Image[12];
            _storagePlayerNameLabels = new Text[12];
            _storagePlayerCountLabels = new Text[12];

            for (int i = 0; i < 12; i++)
            {
                int slot = i;
                CreateStorageSlot(
                    _storageChestGrid,
                    $"ChestSlot_{i}",
                    i,
                    out _storageChestButtons[i],
                    out _storageChestIconImages[i],
                    out _storageChestNameLabels[i],
                    out _storageChestCountLabels[i],
                    new Color(0.18f, 0.22f, 0.28f, 0.96f),
                    new Color(0.32f, 0.54f, 0.78f, 0.92f),
                    () => OnChestSlotClicked(slot));

                CreateStorageSlot(
                    _storagePlayerGrid,
                    $"PlayerSlot_{i}",
                    i,
                    out _storagePlayerButtons[i],
                    out _storagePlayerIconImages[i],
                    out _storagePlayerNameLabels[i],
                    out _storagePlayerCountLabels[i],
                    new Color(0.18f, 0.24f, 0.18f, 0.96f),
                    new Color(0.36f, 0.70f, 0.40f, 0.92f),
                    () => OnPlayerSlotClicked(slot));
            }

            _storageDragGhost = CreateImage(
                overlay.transform,
                "StorageDragGhost",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(196f, 64f),
                color: new Color(0.14f, 0.16f, 0.20f, 0.92f)).rectTransform;
            _storageDragGhost.gameObject.SetActive(false);
            var ghostCanvasGroup = _storageDragGhost.gameObject.AddComponent<CanvasGroup>();
            ghostCanvasGroup.blocksRaycasts = false;
            ghostCanvasGroup.interactable = false;
            CreateImage(
                _storageDragGhost,
                "GhostBorder",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: new Color(0.78f, 0.86f, 0.96f, 0.78f));
            _storageDragGhostLabel = CreateText(
                _storageDragGhost,
                "GhostName",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, 8f),
                size: new Vector2(-18f, -18f),
                fontSize: 16,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);
            _storageDragGhostCountLabel = CreateText(
                _storageDragGhost,
                "GhostCount",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-10f, 8f),
                size: new Vector2(72f, 20f),
                fontSize: 18,
                color: HudAccentWarm,
                alignment: TextAnchor.LowerRight,
                addOutline: true);
        }

        private void CreateStorageSlot(
            Transform parent,
            string name,
            int index,
            out Button button,
            out Image iconImage,
            out Text nameLabel,
            out Text countLabel,
            Color fillColor,
            Color borderColor,
            UnityEngine.Events.UnityAction onClick)
        {
            int columns = 4;
            int row = index / columns;
            int column = index % columns;
            const float slotWidth = 196f;
            const float slotHeight = 64f;
            const float stepX = 214f;
            const float stepY = 74f;
            float x = -stepX * 1.5f + column * stepX;
            float y = -row * stepY;

            var root = CreateImage(
                parent,
                name,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(x, y),
                size: new Vector2(slotWidth, slotHeight),
                color: fillColor);
            root.raycastTarget = true;

            button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = root;
            var colors = button.colors;
            colors.normalColor = fillColor;
            colors.highlightedColor = Color.Lerp(fillColor, Color.white, 0.12f);
            colors.pressedColor = Color.Lerp(fillColor, Color.black, 0.16f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(fillColor.r * 0.45f, fillColor.g * 0.45f, fillColor.b * 0.45f, 0.52f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            var dragSlot = root.gameObject.AddComponent<SCoLStorageUIDragSlot>();
            dragSlot.hud = this;
            dragSlot.slotIndex = index;
            dragSlot.fromChest = name.StartsWith("ChestSlot_");

            var dropZone = root.gameObject.AddComponent<SCoLStorageUIDropZone>();
            dropZone.hud = this;
            dropZone.dropToChest = name.StartsWith("ChestSlot_");

            var border = CreateImage(
                root.transform,
                "Border",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: Vector2.zero,
                color: borderColor);
            border.raycastTarget = false;
            border.type = Image.Type.Sliced;
            border.sprite = ResolveBuiltinUISprite();

            var inner = CreateImage(
                root.transform,
                "Inner",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(-6f, -6f),
                color: fillColor * 0.78f);
            inner.raycastTarget = false;

            iconImage = CreateImage(
                root.transform,
                "Icon",
                anchorMin: new Vector2(0f, 0.5f),
                anchorMax: new Vector2(0f, 0.5f),
                pivot: new Vector2(0f, 0.5f),
                anchoredPos: new Vector2(12f, 0f),
                size: new Vector2(46f, 46f),
                color: new Color(1f, 1f, 1f, 0.96f));
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            iconImage.gameObject.SetActive(false);

            nameLabel = CreateText(
                root.transform,
                "Name",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(18f, 8f),
                size: new Vector2(-72f, -18f),
                fontSize: 16,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);
            nameLabel.raycastTarget = false;

            countLabel = CreateText(
                root.transform,
                "Count",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-10f, 8f),
                size: new Vector2(72f, 20f),
                fontSize: 18,
                color: HudAccentWarm,
                alignment: TextAnchor.LowerRight,
                addOutline: true);
            countLabel.raycastTarget = false;
        }

        private void UpdateStorageOverlay()
        {
            if (_storageOverlayGroup == null)
                return;

            bool open = _settlementManager != null && _settlementManager.IsStorageUiOpen;
            _storageOverlayGroup.alpha = open ? 1f : 0f;
            _storageOverlayGroup.interactable = open;
            _storageOverlayGroup.blocksRaycasts = open;

            if (!open)
            {
                _storageWasOpen = false;
                return;
            }

            if (!_storageWasOpen)
            {
                _storageWasOpen = true;
                _storageSelectedChest = true;
                _storageSelectedSlot = 0;
                _nextStorageNavigateAt = 0f;
            }

            bool closePressed = SCoL.Interaction.SCoLInteractionInput.PausePressed()
                                || (Gamepad.current != null && Gamepad.current.buttonEast.wasPressedThisFrame);
            if (closePressed && _settlementManager != null)
            {
                _settlementManager.CloseStorageUi();
                return;
            }

            UpdateStorageSelectionInput();

            if (_storageTitleLabel != null)
                SetText(_storageTitleLabel, "Supply Chest");
            if (_storageHintLabel != null)
                SetText(_storageHintLabel, $"{GetStorageSelectPromptLabel()} move  {GetStorageConfirmPromptLabel()} transfer  {GetStorageSwitchPromptLabel()} switch side  {GetStorageClosePromptLabel()} closes.");

            for (int i = 0; i < 12; i++)
            {
                int chestCount = _settlementManager != null ? _settlementManager.GetStorageSlotCount(i) : 0;
                string chestLabel = _settlementManager != null ? _settlementManager.GetStorageSlotLabel(i, _inventory) : $"Slot {i + 1}";
                ApplyStorageSlotState(_storageChestButtons[i], _storageChestIconImages[i], _storageChestNameLabels[i], _storageChestCountLabels[i], chestLabel, chestCount);
                ApplyStorageSelectionVisual(_storageChestButtons[i], _storageSelectedChest && _storageSelectedSlot == i);

                int playerCount = _settlementManager != null ? _settlementManager.GetPlayerSlotCount(_inventory, i) : 0;
                string playerLabel = _settlementManager != null ? _settlementManager.GetStorageSlotLabel(i, _inventory) : $"Slot {i + 1}";
                ApplyStorageSlotState(_storagePlayerButtons[i], _storagePlayerIconImages[i], _storagePlayerNameLabels[i], _storagePlayerCountLabels[i], playerLabel, playerCount);
                ApplyStorageSelectionVisual(_storagePlayerButtons[i], !_storageSelectedChest && _storageSelectedSlot == i);
            }
        }

        private void ApplyStorageSlotState(Button button, Image iconImage, Text nameLabel, Text countLabel, string itemLabel, int count)
        {
            Sprite icon = count > 0 ? GetStorageItemIcon(itemLabel) : null;
            bool hasIcon = icon != null;

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.gameObject.SetActive(hasIcon);
            }
            if (nameLabel != null)
            {
                nameLabel.gameObject.SetActive(!hasIcon);
                if (!hasIcon)
                    SetText(nameLabel, itemLabel);
            }
            if (countLabel != null)
                SetText(countLabel, count > 0 ? count.ToString() : "-");
            if (button != null)
                button.interactable = count > 0;
        }

        private Sprite GetStorageItemIcon(string itemLabel)
        {
            if (string.IsNullOrWhiteSpace(itemLabel))
                return null;

            string key = itemLabel.Trim();
            if (_storageIconCache.TryGetValue(key, out var cached))
                return cached;

            string fileName = key switch
            {
                "Bean" => "FlowerV1_seeds.jpg",
                "BrownSeed" => "FlowerV1_seeds.jpg",
                "LightBrownSeed" => "FlowerV2_seeds.jpg",
                "LongSeed" => "FlowerV3_seeds.jpg",
                "Seed1" => "FlowerV1_seeds.jpg",
                "SeedV1" => "FlowerV1.jpg",
                "SeedV2" => "FlowerV2.jpg",
                "SeedV3" => "FlowerV3.jpg",
                "Plant" => "FlowerV3.jpg",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(fileName))
            {
                _storageIconCache[key] = null;
                return null;
            }

            var sprite = LoadStorageIconSprite(fileName);
            _storageIconCache[key] = sprite;
            return sprite;
        }

        private Sprite LoadStorageIconSprite(string fileName)
        {
            const string assetFolder = "Assets/Screenshots/Snapshot_of_models";
#if UNITY_EDITOR
            string assetPath = $"{assetFolder}/{fileName}";
            var directSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (directSprite != null)
                return directSprite;

            var textureAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (textureAsset != null)
                return Sprite.Create(textureAsset, new Rect(0f, 0f, textureAsset.width, textureAsset.height), new Vector2(0.5f, 0.5f), 100f);
#endif

            string fullPath = Path.Combine(Application.dataPath, "Screenshots/Snapshot_of_models", fileName);
            if (!File.Exists(fullPath))
                return null;

            byte[] bytes = File.ReadAllBytes(fullPath);
            if (bytes == null || bytes.Length == 0)
                return null;

            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!texture.LoadImage(bytes))
            {
                Destroy(texture);
                return null;
            }

            texture.name = $"StorageIcon_{Path.GetFileNameWithoutExtension(fileName)}";
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private void OnChestSlotClicked(int slot)
        {
            if (_settlementManager == null || _inventory == null)
                return;

            if (_settlementManager.TryWithdrawStorageSlot(_inventory, slot, out string message))
                DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.PickupItem);
            else if (!string.IsNullOrWhiteSpace(message))
                Debug.Log($"[SCoLUIToolkitHUD] {message}");
        }

        private void OnPlayerSlotClicked(int slot)
        {
            if (_settlementManager == null || _inventory == null)
                return;

            if (_settlementManager.TryStoreInventorySlot(_inventory, slot, out string message))
                DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.PickupItem);
            else if (!string.IsNullOrWhiteSpace(message))
                Debug.Log($"[SCoLUIToolkitHUD] {message}");
        }

        private void OnStorageCloseClicked()
        {
            if (_settlementManager == null)
                return;

            _settlementManager.CloseStorageUi();
        }

        void UpdateStorageSelectionInput()
        {
            if (Time.unscaledTime >= _nextStorageNavigateAt && TryReadStorageNavigate(out int dx, out int dy))
            {
                MoveStorageSelection(dx, dy);
                _nextStorageNavigateAt = Time.unscaledTime + 0.16f;
            }

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
                _storageSelectedChest = !_storageSelectedChest;

            bool confirmPressed = SCoL.Interaction.SCoLInteractionInput.ChestPressed()
                                  || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
            if (!confirmPressed || _settlementManager == null || _inventory == null)
                return;

            bool success = _storageSelectedChest
                ? _settlementManager.TryWithdrawStorageSlot(_inventory, _storageSelectedSlot, out _)
                : _settlementManager.TryStoreInventorySlot(_inventory, _storageSelectedSlot, out _);
            if (success)
                DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.PickupItem);
        }

        bool TryReadStorageNavigate(out int dx, out int dy)
        {
            dx = 0;
            dy = 0;

            var keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame || keyboard.aKey.wasPressedThisFrame) dx = -1;
                else if (keyboard.rightArrowKey.wasPressedThisFrame || keyboard.dKey.wasPressedThisFrame) dx = 1;

                if (keyboard.upArrowKey.wasPressedThisFrame || keyboard.wKey.wasPressedThisFrame) dy = -1;
                else if (keyboard.downArrowKey.wasPressedThisFrame || keyboard.sKey.wasPressedThisFrame) dy = 1;
            }

            if (dx != 0 || dy != 0)
                return true;

            var gamepad = Gamepad.current;
            if (gamepad == null)
                return false;

            Vector2 stick = gamepad.rightStick.ReadValue();
            if (Mathf.Abs(stick.x) >= 0.6f)
                dx = stick.x > 0f ? 1 : -1;
            else if (Mathf.Abs(stick.y) >= 0.6f)
                dy = stick.y > 0f ? -1 : 1;

            if (dx == 0 && dy == 0)
            {
                if (gamepad.dpad.left.wasPressedThisFrame) dx = -1;
                else if (gamepad.dpad.right.wasPressedThisFrame) dx = 1;
                else if (gamepad.dpad.up.wasPressedThisFrame) dy = -1;
                else if (gamepad.dpad.down.wasPressedThisFrame) dy = 1;
            }

            return dx != 0 || dy != 0;
        }

        void MoveStorageSelection(int dx, int dy)
        {
            const int columns = 4;
            const int rows = 3;

            int row = Mathf.Clamp(_storageSelectedSlot / columns, 0, rows - 1);
            int column = Mathf.Clamp(_storageSelectedSlot % columns, 0, columns - 1);

            if (dx != 0)
                column = Mathf.Clamp(column + dx, 0, columns - 1);

            if (dy != 0)
            {
                int nextRow = row + dy;
                if (nextRow < 0 || nextRow >= rows)
                    _storageSelectedChest = !_storageSelectedChest;
                else
                    row = nextRow;
            }

            _storageSelectedSlot = row * columns + column;
        }

        void ApplyStorageSelectionVisual(Button button, bool selected)
        {
            if (button == null)
                return;

            var image = button.targetGraphic as Image;
            if (image != null)
            {
                Color baseColor = button.interactable ? button.colors.normalColor : button.colors.disabledColor;
                image.color = selected ? Color.Lerp(baseColor, Color.white, 0.26f) : baseColor;
            }

            var border = button.transform.Find("Border");
            var borderImage = border != null ? border.GetComponent<Image>() : null;
            if (borderImage != null)
            {
                bool chestSlot = button.name.StartsWith("ChestSlot_");
                borderImage.color = selected
                    ? new Color(0.98f, 0.94f, 0.60f, 0.98f)
                    : chestSlot
                        ? new Color(0.32f, 0.54f, 0.78f, 0.92f)
                        : new Color(0.36f, 0.70f, 0.40f, 0.92f);
            }
        }

        string GetStorageSelectPromptLabel()
        {
            return UseGamepadPrompts() ? "Right Stick" : "Arrow Keys";
        }

        string GetStorageConfirmPromptLabel()
        {
            return UseGamepadPrompts() ? "A" : "F";
        }

        string GetStorageSwitchPromptLabel()
        {
            return UseGamepadPrompts() ? "D-Pad Up/Down" : "Tab";
        }

        string GetStorageClosePromptLabel()
        {
            return UseGamepadPrompts() ? "B" : "Esc";
        }

        public void BeginStorageDrag(bool fromChest, int slotIndex, PointerEventData eventData)
        {
            if (_settlementManager == null || _inventory == null || _storageDragGhost == null)
                return;

            int count = fromChest
                ? _settlementManager.GetStorageSlotCount(slotIndex)
                : _settlementManager.GetPlayerSlotCount(_inventory, slotIndex);
            if (count <= 0)
                return;

            _storageDragActive = true;
            _storageDragFromChest = fromChest;
            _storageDragSlotIndex = slotIndex;
            _storageDragDropConsumed = false;

            if (_storageDragGhostLabel != null)
                SetText(_storageDragGhostLabel, _settlementManager.GetStorageSlotLabel(slotIndex, _inventory));
            if (_storageDragGhostCountLabel != null)
                SetText(_storageDragGhostCountLabel, count.ToString());
            _storageDragGhost.gameObject.SetActive(true);
            UpdateStorageDrag(eventData);
        }

        public void UpdateStorageDrag(PointerEventData eventData)
        {
            if (!_storageDragActive || _storageDragGhost == null || _root == null || eventData == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_root, eventData.position, eventData.pressEventCamera, out var localPoint))
                _storageDragGhost.anchoredPosition = localPoint + new Vector2(18f, -18f);
        }

        public void HandleStorageDrop(bool dropToChest)
        {
            if (!_storageDragActive || _storageDragDropConsumed || _settlementManager == null || _inventory == null)
                return;

            bool success = false;
            if (_storageDragFromChest && !dropToChest)
                success = _settlementManager.TryWithdrawStorageSlot(_inventory, _storageDragSlotIndex, out _);
            else if (!_storageDragFromChest && dropToChest)
                success = _settlementManager.TryStoreInventorySlot(_inventory, _storageDragSlotIndex, out _);

            if (success)
            {
                _storageDragDropConsumed = true;
                DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.PickupItem);
            }
        }

        public void EndStorageDrag(PointerEventData eventData)
        {
            _storageDragActive = false;
            _storageDragSlotIndex = -1;
            _storageDragDropConsumed = false;
            if (_storageDragGhost != null)
                _storageDragGhost.gameObject.SetActive(false);
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
