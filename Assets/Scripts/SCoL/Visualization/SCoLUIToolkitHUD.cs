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
        private RectTransform _statusPanel;
        private RectTransform _aimPanel;
        private CanvasGroup _aimCanvasGroup;
        private Text _aimTitleLabel;
        private Text _aimDetailLabel;
        private RectTransform _inventoryPanel;
        private Text _inventoryLabel;
        private Image _inventoryHeldIcon;
        private Text _inventoryHeldLabel;
        private Image _inventorySeedIcon;
        private Text _inventorySeedLabel;
        private Image _inventoryWaterIcon;
        private Text _inventoryWaterLabel;
        private Image _inventoryFireIcon;
        private Text _inventoryFireLabel;
        private Image _inventoryPlantIcon;
        private Text _inventoryPlantLabel;
        private Image _inventoryStoneIcon;
        private Text _inventoryStoneLabel;
        private Text _inventoryTypesLabel;
        private Text _inventoryStorageLabel;
        private RectTransform _toolArcPanel;
        private Image _toolArcBase;
        private Image _toolActiveFrame;
        private Image _toolActiveIcon;
        private Text _toolSummaryLabel;
        private Image[] _toolSlotBorders;
        private Image[] _toolSlotFills;
        private Image[] _toolSlotActiveFrames;
        private Image[] _toolIconImages;
        private Text[] _toolSlotKeyLabels;
        private Text[] _toolSlotNameLabels;
        private RectTransform _healthTrackRect;
        private RectTransform _healthLagRect;
        private RectTransform _healthFillRect;
        private Text _healthLabel;
        private Text _healthCaptionLabel;
        private Text _healthBadgeLabel;
        private RectTransform _healthPanel;
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
        private Image _storagePanelArt;
        private bool _useCustomStorageSkin;
        private Text _storageTitleLabel;
        private Text _storageHintLabel;
        private Button _storageCloseButton;
        private Image _storageCloseIcon;
        private RectTransform _storageDetailPanel;
        private Text _storageDetailTitleLabel;
        private Text _storageDetailCountLabel;
        private Text _storageDetailBodyLabel;
        private RectTransform _storageChestGrid;
        private RectTransform _storagePlayerGrid;
        private RectTransform _storageChestBoard;
        private RectTransform _storagePlayerBoard;
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
        private readonly Dictionary<string, Sprite> _customUISpriteCache = new Dictionary<string, Sprite>();
        private readonly Dictionary<string, Sprite> _storageIconCache = new Dictionary<string, Sprite>();
        private static Sprite _fallbackWhiteUISprite;
        private const string CustomUIFolderAssetPath = "Assets/CustomUI";
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
        private static readonly Color BackpackArtTint = new Color(0.10f, 0.12f, 0.11f, 0.96f);
        private static readonly Color ChestArtTint = new Color(0.98f, 0.97f, 0.94f, 0.96f);
        private static readonly Color CloseButtonBg = new Color(0.02f, 0.03f, 0.04f, 0.42f);

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
            _statusPanel = CreateHudCard(
                _root,
                "StatusPanel",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(34f, -30f),
                size: new Vector2(484f, 236f),
                title: "WORLD LOOP",
                accent: HudAccentWarm);

            _statusHeroLabel = CreateText(
                _statusPanel,
                "StatusHero",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -64f),
                size: new Vector2(-42f, 34f),
                fontSize: 19,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);

            _statusLabel = CreateText(
                _statusPanel,
                "StatusText",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -104f),
                size: new Vector2(-42f, -116f),
                fontSize: 14,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft);

            _safeZoneBadge = CreateImage(
                _root,
                "SafeZoneBadge",
                anchorMin: new Vector2(1f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(1f, 1f),
                anchoredPos: new Vector2(-34f, -30f),
                size: new Vector2(252f, 62f),
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
                size: new Vector2(244f, 54f),
                color: new Color(0.22f, 0.72f, 0.38f, 0.96f));

            CreateImage(
                _safeZoneBadgeFill.transform,
                "Glow",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(244f, 54f),
                color: new Color(1f, 1f, 1f, 0.07f));

            _safeZoneBadgeLabel = CreateText(
                _safeZoneBadgeFill.transform,
                "Label",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(214f, 34f),
                fontSize: 27,
                color: new Color(0.94f, 1f, 0.94f, 0.98f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);

            _aimPanel = CreateHudCard(
                _root,
                "AimHoverPanel",
                anchorMin: new Vector2(1f, 0.5f),
                anchorMax: new Vector2(1f, 0.5f),
                pivot: new Vector2(1f, 0.5f),
                anchoredPos: new Vector2(-76f, -8f),
                size: new Vector2(420f, 164f),
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
                anchoredPos: new Vector2(0f, -18f),
                size: new Vector2(-38f, -58f),
                fontSize: 26,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);

            _aimDetailLabel = CreateText(
                _aimPanel,
                "AimDetail",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0.54f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -10f),
                size: new Vector2(-38f, -26f),
                fontSize: 18,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft,
                addOutline: true);

            bool useBackpackSkin = false;
            _inventoryPanel = CreateHudCard(
                _root,
                "InventoryPanel",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-34f, 34f),
                size: new Vector2(382f, 210f),
                title: "RESERVES",
                accent: HudAccentGreen);
            _inventoryLabel = CreateText(
                _inventoryPanel,
                "InventoryText",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -62f),
                size: new Vector2(-34f, -86f),
                fontSize: 22,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft);

            _inventoryHeldIcon = CreateImage(
                _inventoryPanel,
                "InventoryHeldIcon",
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: useBackpackSkin ? new Vector2(0f, -82f) : new Vector2(0f, -58f),
                size: useBackpackSkin ? new Vector2(54f, 54f) : new Vector2(0f, 0f),
                color: Color.white);
            _inventoryHeldIcon.preserveAspect = true;
            _inventoryHeldIcon.gameObject.SetActive(false);

            _inventoryHeldLabel = CreateText(
                _inventoryPanel,
                "InventoryHeldLabel",
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: useBackpackSkin ? new Vector2(0f, -102f) : new Vector2(0f, -12f),
                size: useBackpackSkin ? new Vector2(196f, 34f) : new Vector2(-34f, -62f),
                fontSize: useBackpackSkin ? 24 : 21,
                color: new Color(0.95f, 0.96f, 0.92f, 0.98f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
            _inventoryHeldLabel.gameObject.SetActive(false);

            _inventorySeedIcon = CreateImage(_inventoryPanel, "InventorySeedIcon", new Vector2(0.28f, 1f), new Vector2(0.28f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(0f, -166f) : new Vector2(0f, 0f), new Vector2(26f, 26f), Color.white);
            _inventoryWaterIcon = CreateImage(_inventoryPanel, "InventoryWaterIcon", new Vector2(0.50f, 1f), new Vector2(0.50f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(-28f, -166f) : new Vector2(0f, 0f), new Vector2(24f, 24f), Color.white);
            _inventoryFireIcon = CreateImage(_inventoryPanel, "InventoryFireIcon", new Vector2(0.50f, 1f), new Vector2(0.50f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(-28f, -212f) : new Vector2(0f, 0f), new Vector2(24f, 24f), Color.white);
            _inventoryPlantIcon = CreateImage(_inventoryPanel, "InventoryPlantIcon", new Vector2(0.72f, 1f), new Vector2(0.72f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(-28f, -166f) : new Vector2(0f, 0f), new Vector2(24f, 24f), Color.white);
            _inventoryStoneIcon = CreateImage(_inventoryPanel, "InventoryStoneIcon", new Vector2(0.72f, 1f), new Vector2(0.72f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(-28f, -212f) : new Vector2(0f, 0f), new Vector2(24f, 24f), Color.white);

            _inventorySeedIcon.preserveAspect = _inventoryWaterIcon.preserveAspect = _inventoryFireIcon.preserveAspect = _inventoryPlantIcon.preserveAspect = _inventoryStoneIcon.preserveAspect = true;
            _inventorySeedIcon.gameObject.SetActive(false);
            _inventoryWaterIcon.gameObject.SetActive(false);
            _inventoryFireIcon.gameObject.SetActive(false);
            _inventoryPlantIcon.gameObject.SetActive(false);
            _inventoryStoneIcon.gameObject.SetActive(false);

            _inventorySeedLabel = CreateText(_inventoryPanel, "InventorySeedLabel", new Vector2(0.28f, 1f), new Vector2(0.28f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(0f, -196f) : new Vector2(0f, 0f), new Vector2(116f, 44f), 22, HudAccentGreen, TextAnchor.MiddleCenter, true);
            _inventoryWaterLabel = CreateText(_inventoryPanel, "InventoryWaterLabel", new Vector2(0.50f, 1f), new Vector2(0.50f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(12f, -166f) : new Vector2(0f, 0f), new Vector2(90f, 22f), 18, new Color(0.53f, 0.82f, 1f, 0.98f), TextAnchor.MiddleLeft, true);
            _inventoryFireLabel = CreateText(_inventoryPanel, "InventoryFireLabel", new Vector2(0.50f, 1f), new Vector2(0.50f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(12f, -212f) : new Vector2(0f, 0f), new Vector2(90f, 22f), 18, new Color(1f, 0.58f, 0.42f, 0.98f), TextAnchor.MiddleLeft, true);
            _inventoryPlantLabel = CreateText(_inventoryPanel, "InventoryPlantLabel", new Vector2(0.72f, 1f), new Vector2(0.72f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(12f, -166f) : new Vector2(0f, 0f), new Vector2(90f, 22f), 18, new Color(0.54f, 0.90f, 0.56f, 0.98f), TextAnchor.MiddleLeft, true);
            _inventoryStoneLabel = CreateText(_inventoryPanel, "InventoryStoneLabel", new Vector2(0.72f, 1f), new Vector2(0.72f, 1f), new Vector2(0.5f, 0.5f), useBackpackSkin ? new Vector2(12f, -212f) : new Vector2(0f, 0f), new Vector2(90f, 22f), 18, new Color(0.90f, 0.92f, 0.94f, 0.98f), TextAnchor.MiddleLeft, true);

            _inventoryTypesLabel = CreateText(
                _inventoryPanel,
                "InventoryTypesLabel",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: useBackpackSkin ? new Vector2(0f, 108f) : new Vector2(0f, 0f),
                size: useBackpackSkin ? new Vector2(304f, 84f) : new Vector2(0f, 0f),
                fontSize: 15,
                color: new Color(0.92f, 0.93f, 0.95f, 0.94f),
                alignment: TextAnchor.UpperLeft,
                addOutline: true);

            _inventoryStorageLabel = CreateText(
                _inventoryPanel,
                "InventoryStorageLabel",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: useBackpackSkin ? new Vector2(0f, 24f) : new Vector2(0f, 0f),
                size: useBackpackSkin ? new Vector2(304f, 24f) : new Vector2(0f, 0f),
                fontSize: 14,
                color: new Color(0.96f, 0.83f, 0.52f, 0.96f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
            _inventorySeedLabel.gameObject.SetActive(false);
            _inventoryWaterLabel.gameObject.SetActive(false);
            _inventoryFireLabel.gameObject.SetActive(false);
            _inventoryPlantLabel.gameObject.SetActive(false);
            _inventoryStoneLabel.gameObject.SetActive(false);
            _inventoryTypesLabel.gameObject.SetActive(false);
            _inventoryStorageLabel.gameObject.SetActive(false);

            _toolArcPanel = CreateRect(
                _root,
                "ToolbeltArc",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 0f),
                pivot: new Vector2(0f, 0f),
                anchoredPos: new Vector2(18f, 26f),
                size: new Vector2(232f, 188f));

            var toolArcSprite = GetCustomUISprite("ActiveItem");
            if (toolArcSprite != null)
            {
                _toolArcBase = CreateDecorativeSprite(
                    _toolArcPanel,
                    "ToolArcBase",
                    toolArcSprite,
                    anchorMin: new Vector2(0f, 0f),
                    anchorMax: new Vector2(0f, 0f),
                    pivot: new Vector2(0f, 0f),
                    anchoredPos: Vector2.zero,
                    size: new Vector2(208f, 208f),
                    color: new Color(0.28f, 0.66f, 0.82f, 0.30f));
            }

            _toolSummaryLabel = CreateText(
                _toolArcPanel,
                "ToolSummary",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 0f),
                pivot: new Vector2(0f, 0f),
                anchoredPos: new Vector2(22f, 10f),
                size: new Vector2(150f, 22f),
                fontSize: 16,
                color: new Color(0.90f, 0.95f, 0.97f, 0.92f),
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);

            var activeFrameSprite = GetCustomUISprite("ActiveItem");
            if (activeFrameSprite != null)
            {
                _toolActiveFrame = CreateDecorativeSprite(
                    _toolArcPanel,
                    "ToolActiveFrame",
                    activeFrameSprite,
                    anchorMin: new Vector2(0f, 0f),
                    anchorMax: new Vector2(0f, 0f),
                    pivot: new Vector2(0.5f, 0.5f),
                    anchoredPos: new Vector2(92f, 82f),
                    size: new Vector2(108f, 108f),
                    color: new Color(0.40f, 0.82f, 0.95f, 0.82f));
            }

            _toolActiveIcon = CreateImage(
                _toolArcPanel,
                "ToolActiveIcon",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 0f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(92f, 82f),
                size: new Vector2(62f, 62f),
                color: Color.white);
            _toolActiveIcon.preserveAspect = true;
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
            _healthPanel = panel;

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
                _sb.AppendLine(ShortenStatusGoal(_settlementManager.GoalLine));
            }

            SetText(_statusLabel, _sb.ToString());
        }

        private static string ShortenStatusGoal(string goalLine)
        {
            if (string.IsNullOrWhiteSpace(goalLine))
                return string.Empty;

            string goal = goalLine.Trim();
            goal = goal.Replace("animals near home", "animals");
            goal = goal.Replace("flowers", "flw");
            goal = goal.Replace("survive", "survive");
            return goal;
        }

        private void UpdateInventory()
        {
            if (_inventoryLabel == null)
                return;

            bool storageOpen = _settlementManager != null && _settlementManager.IsStorageUiOpen;
            if (_inventoryPanel != null)
                _inventoryPanel.gameObject.SetActive(!storageOpen);
            if (storageOpen)
                return;

            if (_inventory == null)
            {
                SetText(_inventoryLabel, string.Empty);
                return;
            }

            int selected = _fpsInteractor != null ? _fpsInteractor.GetSelectedSeedVariantIndex() : 0;
            SetText(_inventoryLabel, BuildCompactInventoryHudText(selected));
        }

        private string BuildCompactInventoryHudText(int selectedSeedVariant)
        {
            if (_inventory == null)
                return string.Empty;

            _sb.Clear();
            _sb.Append("<size=21><color=#7FD390><b>Seeds</b></color> ");
            _sb.Append(_inventory.seeds);
            _sb.AppendLine("</size>");
            _sb.Append("<color=#7FD390><b>Held</b></color> ");
            _sb.AppendLine(_inventory.GetSeedTypeDisplayName(selectedSeedVariant));
            _sb.Append("<color=#67C8FF><b>Water</b></color> ");
            _sb.Append(_inventory.water);
            _sb.Append("   <color=#FF8E62><b>Fire</b></color> ");
            _sb.AppendLine(_inventory.fire.ToString());
            _sb.Append("<color=#8BE39E><b>Plants</b></color> ");
            _sb.Append(_inventory.plants);
            _sb.AppendLine();
            _sb.Append("<color=#D2D5DE><b>Stone</b></color> ");
            _sb.Append(_inventory.stones.ToString());
            return _sb.ToString();
        }

        private string BuildCompactSeedTypeSummary()
        {
            if (_inventory == null)
                return string.Empty;

            return $"Bean {_inventory.GetSeedTypeCount(0)}  Ember {_inventory.GetSeedTypeCount(1)}  Moon {_inventory.GetSeedTypeCount(2)}\n"
                 + $"Long {_inventory.GetSeedTypeCount(3)}  Wild {_inventory.GetSeedTypeCount(4)}  Rose {_inventory.GetSeedTypeCount(5)}\n"
                 + $"Amber {_inventory.GetSeedTypeCount(6)}  Moonpetal {_inventory.GetSeedTypeCount(7)}";
        }

        private string BuildCompactStorageHudSummary()
        {
            if (_settlementManager == null || string.IsNullOrWhiteSpace(_settlementManager.StorageSummary))
                return string.Empty;

            return _settlementManager.StorageSummary
                .Replace("Storage ", string.Empty)
                .Replace("Seed ", "S")
                .Replace("Water ", " W")
                .Replace("Fire ", " F")
                .Replace("Plant ", " P")
                .Replace("Stone ", " St");
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
            if (_toolActiveIcon == null)
                return;

            var activeTool = _fpsInteractor != null ? _fpsInteractor.currentTool : FPSRaycastInteractor.ApplyTool.Seed;
            if (_toolSummaryLabel != null)
                SetText(_toolSummaryLabel, GetToolLabel(activeTool).ToUpperInvariant());

            Color accent = GetToolAccent(activeTool);
            float pulse = 0.72f + 0.16f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f));

            _toolActiveIcon.sprite = GetToolIconSprite(activeTool);
            _toolActiveIcon.color = Color.white;

            if (_toolActiveFrame != null)
            {
                _toolActiveFrame.color = new Color(accent.r, accent.g, accent.b, pulse);
                _toolActiveFrame.rectTransform.localScale = Vector3.one * (1.02f + 0.04f * (0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 4.2f)));
            }
        }

        private string GetHeldInventoryLabel(int selectedSeedVariant)
        {
            if (_inventory == null)
                return string.Empty;

            if (_fpsInteractor == null)
                return _inventory.GetSeedTypeDisplayName(selectedSeedVariant);

            if (_fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Seed)
                return _inventory.GetSeedTypeDisplayName(selectedSeedVariant);

            return TryMapToolToItemType(_fpsInteractor.currentTool, out var itemType)
                ? _inventory.GetItemDisplayName(itemType, unknownIfUndiscovered: true)
                : GetToolLabel(_fpsInteractor.currentTool);
        }

        private Sprite GetHeldInventoryIcon(int selectedSeedVariant)
        {
            if (_fpsInteractor == null)
                return GetSeedVariantIconSprite(selectedSeedVariant);

            return _fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Seed
                ? GetSeedVariantIconSprite(selectedSeedVariant)
                : GetToolIconSprite(_fpsInteractor.currentTool);
        }

        private Sprite GetSeedVariantIconSprite(int variantIndex)
        {
            string key = $"SeedVariant_{variantIndex}";
            if (_customUISpriteCache.TryGetValue(key, out var cached))
                return cached;

            string assetPath = variantIndex switch
            {
                0 => "Assets/Screenshots/Snapshot_of_models/bean_.png",
                1 => "Assets/Screenshots/Snapshot_of_models/ember_seed.png",
                2 => "Assets/Screenshots/Snapshot_of_models/moon_seed.png",
                3 => "Assets/Screenshots/Snapshot_of_models/LongSeed.png",
                4 => "Assets/Screenshots/Snapshot_of_models/FlowerV1_seeds.png",
                5 => "Assets/Screenshots/Snapshot_of_models/FlowerV1.png",
                6 => "Assets/Screenshots/Snapshot_of_models/FlowerV2.png",
                7 => "Assets/Screenshots/Snapshot_of_models/FlowerV3.png",
                _ => "Assets/Screenshots/Snapshot_of_models/FlowerV1_seeds.png"
            };

            var sprite = LoadProjectSprite(assetPath, $"SeedVariant_{variantIndex}_", removeFlatBackground: true);
            _customUISpriteCache[key] = sprite;
            return sprite;
        }

        private string BuildInventoryTypesGrid()
        {
            if (_inventory == null)
                return string.Empty;

            _sb.Clear();
            _sb.Append("<b>TYPES</b>\n");
            int printed = 0;
            for (int i = 0; i < _inventory.GetSeedTypeVariantCount(); i++)
            {
                int count = _inventory.GetSeedTypeCount(i);
                if (count <= 0)
                    continue;

                if (printed > 0)
                    _sb.Append(printed % 2 == 0 ? '\n' : "    ");

                _sb.Append(GetShortSeedTypeLabel(i)).Append(' ').Append(count);
                printed++;
            }

            if (printed == 0)
                _sb.Append("None");

            return _sb.ToString();
        }

        private string BuildCompactStorageSummary()
        {
            if (_settlementManager == null || string.IsNullOrWhiteSpace(_settlementManager.StorageSummary))
                return string.Empty;

            string summary = _settlementManager.StorageSummary
                .Replace("Storage ", string.Empty)
                .Replace("Seed ", "S")
                .Replace("Water ", " W")
                .Replace("Fire ", " F")
                .Replace("Plant ", " P")
                .Replace("Stone ", " St");
            return $"STORE {summary}";
        }

        private string GetShortSeedTypeLabel(int variantIndex)
        {
            return variantIndex switch
            {
                0 => "Bean",
                1 => "Brown",
                2 => "Light",
                3 => "Long",
                4 => "Seed1",
                5 => "V1",
                6 => "V2",
                7 => "V3",
                _ => _inventory != null ? _inventory.GetSeedTypeDisplayName(variantIndex) : $"S{variantIndex + 1}"
            };
        }

        private static bool TryMapToolToItemType(FPSRaycastInteractor.ApplyTool tool, out SCoLItemType itemType)
        {
            switch (tool)
            {
                case FPSRaycastInteractor.ApplyTool.Seed:
                    itemType = SCoLItemType.Seed;
                    return true;
                case FPSRaycastInteractor.ApplyTool.Water:
                    itemType = SCoLItemType.Water;
                    return true;
                case FPSRaycastInteractor.ApplyTool.Fire:
                    itemType = SCoLItemType.Fire;
                    return true;
                case FPSRaycastInteractor.ApplyTool.Plant:
                    itemType = SCoLItemType.Plant;
                    return true;
                case FPSRaycastInteractor.ApplyTool.Stone:
                    itemType = SCoLItemType.Stone;
                    return true;
                default:
                    itemType = SCoLItemType.Seed;
                    return false;
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
                        title = target.animal != null ? GetAnimalDisplayName(target.animal.name) : "Animal";
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
                        if (!IsDirectHoverOnSettlementStorage(target.settlementInteractable))
                            break;

                        title = "Supply Chest";
                        detail = _settlementManager != null && !_settlementManager.IsActivated
                            ? "Offline until the settlement core is activated."
                            : $"{GetChestPromptRich(HudAccentCool)} Open storage\nMove items between chest and pack.";
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

        private bool IsDirectHoverOnSettlementStorage(SCoLSettlementInteractable storageInteractable)
        {
            if (cameraSource == null || storageInteractable == null)
                return false;

            if (!SCoL.Interaction.SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
                return false;

            if (!Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
                return false;

            var hitInteractable = hit.collider != null ? hit.collider.GetComponentInParent<SCoLSettlementInteractable>() : null;
            return hitInteractable == storageInteractable;
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

        private string GetChestPromptRich() => GetChestPromptRich(HudAccentCool);

        private string GetChestPromptRich(Color color)
        {
            string label = UseGamepadPrompts() ? "A" : "F";
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
            const float radius = 110f;
            const float startAngle = 104f;
            const float endAngle = 8f;
            int toolCount = System.Enum.GetValues(typeof(FPSRaycastInteractor.ApplyTool)).Length;
            float t = toolCount > 1 ? slotIndex / (float)(toolCount - 1) : 0f;
            float angleDeg = Mathf.Lerp(startAngle, endAngle, t);
            float angleRad = angleDeg * Mathf.Deg2Rad;
            Vector2 center = new Vector2(34f, 22f);
            Vector2 iconPos = center + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius;

            var border = CreateImage(
                parent,
                $"ToolIcon_{slotIndex + 1}",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(0f, 0f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: iconPos,
                size: new Vector2(54f, 54f),
                color: new Color(0f, 0f, 0f, 0f));
            _toolSlotBorders[slotIndex] = border;
            border.rectTransform.localScale = Vector3.one * 0.92f;

            var activeFrameSprite = GetCustomUISprite("ActiveItem");
            if (activeFrameSprite != null && _toolSlotActiveFrames != null && slotIndex < _toolSlotActiveFrames.Length)
            {
                _toolSlotActiveFrames[slotIndex] = CreateDecorativeSprite(
                    border.transform,
                    "ActiveFrame",
                    activeFrameSprite,
                    anchorMin: new Vector2(0.5f, 0.5f),
                    anchorMax: new Vector2(0.5f, 0.5f),
                    pivot: new Vector2(0.5f, 0.5f),
                    anchoredPos: Vector2.zero,
                    size: new Vector2(88f, 88f),
                    color: new Color(1f, 1f, 1f, 0f));
                if (_toolSlotActiveFrames[slotIndex] != null)
                    _toolSlotActiveFrames[slotIndex].enabled = false;
            }

            _toolIconImages[slotIndex] = CreateDecorativeSprite(
                border.transform,
                "Icon",
                GetToolIconSprite((FPSRaycastInteractor.ApplyTool)slotIndex),
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(38f, 38f),
                color: new Color(0.86f, 0.89f, 0.92f, 0.68f));

            _toolSlotKeyLabels[slotIndex] = null;
            _toolSlotNameLabels[slotIndex] = null;
            _toolSlotFills[slotIndex] = null;
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

        private static void StripHudCardChrome(RectTransform card)
        {
            if (card == null)
                return;

            string[] chromeNames = { "CardInset", "AccentBar", "AccentPill", "CardTitle" };
            for (int i = 0; i < chromeNames.Length; i++)
            {
                var child = card.Find(chromeNames[i]);
                if (child != null)
                    child.gameObject.SetActive(false);
            }
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
                anchoredPos: new Vector2(0f, 18f),
                size: new Vector2(820f, 500f),
                title: "INTRODUCTION",
                accent: HudAccentCool);

            _introTitleLabel = CreateText(
                card,
                "IntroductionTitle",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -82f),
                size: new Vector2(-64f, 46f),
                fontSize: 34,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);

            _introBodyLabel = CreateText(
                card,
                "IntroductionBody",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -136f),
                size: new Vector2(-76f, -210f),
                fontSize: 20,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft,
                addOutline: false);

            _introHintLabel = CreateText(
                card,
                "IntroductionHint",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 22f),
                size: new Vector2(-76f, 52f),
                fontSize: 20,
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
                size: new Vector2(1100f, 820f),
                title: string.Empty,
                accent: HudAccentCool);
            var panelImage = _storageOverlayPanel.GetComponent<Image>();
            if (panelImage != null)
            {
                panelImage.raycastTarget = true;
                panelImage.color = new Color(0f, 0f, 0f, 0f);
            }

            _useCustomStorageSkin = true;
            StripHudCardChrome(_storageOverlayPanel);
            var panelShadow = _storageOverlayPanel.GetComponent<Shadow>();
            if (panelShadow != null)
            {
                panelShadow.enabled = true;
                panelShadow.effectColor = new Color(0f, 0f, 0f, 0.42f);
                panelShadow.effectDistance = new Vector2(0f, -12f);
            }
            CreateImage(
                _storageOverlayPanel,
                "StoragePanelInset",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, -8f),
                size: new Vector2(1060f, 742f),
                color: new Color(0.06f, 0.07f, 0.10f, 0.90f));
            var panelDrag = _storageOverlayPanel.gameObject.AddComponent<SCoLUIDraggableWindow>();
            panelDrag.dragTarget = _storageOverlayPanel;

            _storageTitleLabel = CreateText(
                _storageOverlayPanel,
                "StorageTitle",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -30f),
                size: new Vector2(-120f, 36f),
                fontSize: 31,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);

            _storageHintLabel = CreateText(
                _storageOverlayPanel,
                "StorageHint",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -70f),
                size: new Vector2(-180f, 28f),
                fontSize: 19,
                color: new Color(0.88f, 0.90f, 0.92f, 0.90f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: false);

            _storageCloseButton = CreateButton(
                _storageOverlayPanel,
                "StorageClose",
                anchorMin: new Vector2(1f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(1f, 1f),
                anchoredPos: new Vector2(-24f, -20f),
                size: new Vector2(84f, 84f),
                label: string.Empty,
                color: CloseButtonBg);
            SetButtonIcon(_storageCloseButton, GetCustomUISprite("CloseInventory"), new Vector2(48f, 48f), new Color(0.98f, 0.97f, 0.92f, 0.98f));
            _storageCloseButton.onClick.AddListener(OnStorageCloseClicked);

            _storageChestBoard = CreateImage(
                _storageOverlayPanel,
                "ChestBoard",
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(-316f, -118f),
                size: new Vector2(338f, 620f),
                color: new Color(0.13f, 0.12f, 0.10f, 0.96f)).rectTransform;
            CreateImage(
                _storageChestBoard,
                "ChestBoardInner",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(316f, 596f),
                color: new Color(0.18f, 0.16f, 0.13f, 0.98f));

            var chestLabel = CreateText(
                _storageChestBoard,
                "ChestSectionLabel",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -22f),
                size: new Vector2(-28f, 32f),
                fontSize: 24,
                color: HudAccentWarm,
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
            SetText(chestLabel, "Chest");

            _storageChestGrid = CreateRect(
                _storageChestBoard,
                "ChestGrid",
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -88f),
                size: new Vector2(294f, 500f));
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

            _storagePlayerBoard = CreateImage(
                _storageOverlayPanel,
                "PlayerBoard",
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(32f, -118f),
                size: new Vector2(338f, 620f),
                color: new Color(0.09f, 0.12f, 0.10f, 0.96f)).rectTransform;
            CreateImage(
                _storagePlayerBoard,
                "PlayerBoardInner",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(316f, 596f),
                color: new Color(0.13f, 0.18f, 0.14f, 0.98f));

            var playerLabel = CreateText(
                _storagePlayerBoard,
                "PlayerSectionLabel",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -22f),
                size: new Vector2(-28f, 32f),
                fontSize: 24,
                color: HudAccentGreen,
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);
            SetText(playerLabel, "Inventory");

            _storagePlayerGrid = CreateRect(
                _storagePlayerBoard,
                "PlayerGrid",
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -88f),
                size: new Vector2(294f, 500f));
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

            _storageDetailPanel = CreateImage(
                overlay.transform,
                "StorageDetailPanel",
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(380f, -396f),
                size: new Vector2(282f, 250f),
                color: new Color(0.07f, 0.08f, 0.11f, 0.94f)).rectTransform;
            var detailShadow = _storageDetailPanel.gameObject.AddComponent<Shadow>();
            detailShadow.effectColor = new Color(0f, 0f, 0f, 0.44f);
            detailShadow.effectDistance = new Vector2(0f, -10f);
            CreateImage(
                _storageDetailPanel,
                "StorageDetailInset",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(262f, 230f),
                color: new Color(0.12f, 0.13f, 0.17f, 0.98f));
            _storageDetailTitleLabel = CreateText(
                _storageDetailPanel,
                "StorageDetailTitle",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -18f),
                size: new Vector2(-32f, 30f),
                fontSize: 24,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);
            _storageDetailCountLabel = CreateText(
                _storageDetailPanel,
                "StorageDetailCount",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -54f),
                size: new Vector2(-32f, 26f),
                fontSize: 18,
                color: HudAccentWarm,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);
            _storageDetailBodyLabel = CreateText(
                _storageDetailPanel,
                "StorageDetailBody",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: new Vector2(0f, -92f),
                size: new Vector2(-32f, -108f),
                fontSize: 17,
                color: HudTextSecondary,
                alignment: TextAnchor.UpperLeft,
                addOutline: false);

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
                size: new Vector2(236f, 78f),
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
                size: new Vector2(-22f, -22f),
                fontSize: 19,
                color: HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);
            _storageDragGhostCountLabel = CreateText(
                _storageDragGhost,
                "GhostCount",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-12f, 10f),
                size: new Vector2(86f, 24f),
                fontSize: 21,
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
            bool useMinimalSkin = _useCustomStorageSkin;
            bool chestSide = name.StartsWith("ChestSlot_");

            float slotWidth = useMinimalSkin ? 138f : 196f;
            float slotHeight = useMinimalSkin ? 90f : 64f;
            Vector2 slotPos;
            if (useMinimalSkin)
            {
                slotPos = GetCustomStorageSlotPosition(chestSide, index);
            }
            else
            {
                int columns = 4;
                int row = index / columns;
                int column = index % columns;
                const float stepX = 214f;
                const float stepY = 74f;
                float x = -stepX * 1.5f + column * stepX;
                float y = -row * stepY;
                slotPos = new Vector2(x, y);
            }

            var root = CreateImage(
                parent,
                name,
                anchorMin: new Vector2(0.5f, 1f),
                anchorMax: new Vector2(0.5f, 1f),
                pivot: new Vector2(0.5f, 1f),
                anchoredPos: slotPos,
                size: new Vector2(slotWidth, slotHeight),
                color: useMinimalSkin ? new Color(0f, 0f, 0f, 0.001f) : fillColor);
            root.raycastTarget = true;

            button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = root;
            var colors = button.colors;
            colors.normalColor = useMinimalSkin ? new Color(0f, 0f, 0f, 0.001f) : fillColor;
            colors.highlightedColor = useMinimalSkin ? new Color(1f, 1f, 1f, 0.06f) : Color.Lerp(fillColor, Color.white, 0.12f);
            colors.pressedColor = useMinimalSkin ? new Color(1f, 1f, 1f, 0.12f) : Color.Lerp(fillColor, Color.black, 0.16f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = useMinimalSkin
                ? new Color(1f, 1f, 1f, 0.015f)
                : new Color(fillColor.r * 0.45f, fillColor.g * 0.45f, fillColor.b * 0.45f, 0.52f);
            button.colors = colors;
            button.onClick.AddListener(onClick);

            var dragSlot = root.gameObject.AddComponent<SCoLStorageUIDragSlot>();
            dragSlot.hud = this;
            dragSlot.slotIndex = index;
            dragSlot.fromChest = name.StartsWith("ChestSlot_");

            var dropZone = root.gameObject.AddComponent<SCoLStorageUIDropZone>();
            dropZone.hud = this;
            dropZone.dropToChest = name.StartsWith("ChestSlot_");

            Image selectionFrame = CreateImage(
                root.transform,
                "SelectionFrame",
                anchorMin: Vector2.zero,
                anchorMax: Vector2.one,
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: useMinimalSkin ? new Vector2(8f, 8f) : Vector2.zero,
                color: useMinimalSkin ? new Color(1f, 1f, 1f, 0f) : borderColor);
            selectionFrame.raycastTarget = false;
            selectionFrame.type = Image.Type.Sliced;
            selectionFrame.sprite = ResolveBuiltinUISprite();

            if (!useMinimalSkin)
            {
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
            }

            iconImage = CreateImage(
                root.transform,
                "Icon",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(0f, useMinimalSkin ? 0f : 0f),
                size: useMinimalSkin ? new Vector2(76f, 56f) : new Vector2(46f, 46f),
                color: new Color(1f, 1f, 1f, 0.96f));
            iconImage.raycastTarget = false;
            iconImage.preserveAspect = true;
            iconImage.gameObject.SetActive(false);

            nameLabel = CreateText(
                root.transform,
                "Name",
                anchorMin: useMinimalSkin ? new Vector2(0f, 1f) : new Vector2(0f, 0f),
                anchorMax: useMinimalSkin ? new Vector2(1f, 1f) : new Vector2(1f, 1f),
                pivot: useMinimalSkin ? new Vector2(0f, 1f) : new Vector2(0.5f, 0.5f),
                anchoredPos: useMinimalSkin ? new Vector2(10f, -10f) : new Vector2(18f, 8f),
                size: useMinimalSkin ? new Vector2(-20f, 20f) : new Vector2(-72f, -18f),
                fontSize: useMinimalSkin ? 11 : 16,
                color: useMinimalSkin ? new Color(0.95f, 0.95f, 0.90f, 0.98f) : HudTextPrimary,
                alignment: TextAnchor.MiddleLeft,
                addOutline: true);
            nameLabel.raycastTarget = false;
            if (useMinimalSkin)
                nameLabel.gameObject.SetActive(false);

            countLabel = CreateText(
                root.transform,
                "Count",
                anchorMin: new Vector2(1f, 1f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(1f, 1f),
                anchoredPos: new Vector2(-12f, -10f),
                size: useMinimalSkin ? new Vector2(74f, 28f) : new Vector2(72f, 20f),
                fontSize: useMinimalSkin ? 20 : 18,
                color: useMinimalSkin ? new Color(0.95f, 0.78f, 0.34f, 0.98f) : HudAccentWarm,
                alignment: TextAnchor.UpperRight,
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
            if (open)
            {
                _introOpen = false;
                if (_introOverlayGroup != null)
                {
                    _introOverlayGroup.alpha = 0f;
                    _introOverlayGroup.interactable = false;
                    _introOverlayGroup.blocksRaycasts = false;
                }
            }
            if (_inventoryPanel != null)
            {
                _inventoryPanel.gameObject.SetActive(!open);
                var invCanvas = _inventoryPanel.GetComponent<CanvasGroup>();
                if (invCanvas == null)
                    invCanvas = _inventoryPanel.gameObject.AddComponent<CanvasGroup>();
                invCanvas.alpha = open ? 0f : 1f;
                invCanvas.interactable = !open;
                invCanvas.blocksRaycasts = !open;
            }
            if (_statusPanel != null)
                _statusPanel.gameObject.SetActive(!open);
            if (_toolArcPanel != null)
                _toolArcPanel.gameObject.SetActive(!open);
            if (_aimPanel != null)
                _aimPanel.gameObject.SetActive(!open);
            if (_safeZoneBadge != null)
                _safeZoneBadge.gameObject.SetActive(!open);
            if (_healthPanel != null)
                _healthPanel.gameObject.SetActive(!open);

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

            UpdateStorageDetailPanel();
        }

        private void UpdateStorageDetailPanel()
        {
            if (_storageDetailTitleLabel == null || _settlementManager == null || _inventory == null)
                return;

            int slot = Mathf.Clamp(_storageSelectedSlot, 0, 11);
            string label = _settlementManager.GetStorageSlotLabel(slot, _inventory);
            int count = _storageSelectedChest
                ? _settlementManager.GetStorageSlotCount(slot)
                : _settlementManager.GetPlayerSlotCount(_inventory, slot);

            SetText(_storageDetailTitleLabel, label);
            SetText(_storageDetailCountLabel, $"Count: {Mathf.Max(0, count)}");
            SetText(_storageDetailBodyLabel, GetStorageItemDescription(label));
        }

        private void ApplyStorageSlotState(Button button, Image iconImage, Text nameLabel, Text countLabel, string itemLabel, int count)
        {
            Sprite icon = count > 0 ? GetStorageItemIcon(itemLabel) : null;
            bool hasIcon = icon != null;

            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.color = _useCustomStorageSkin
                    ? count > 0 ? GetStorageItemIconColor(itemLabel) : new Color(1f, 1f, 1f, 0f)
                    : new Color(1f, 1f, 1f, 0.96f);
                iconImage.gameObject.SetActive(hasIcon);
            }
            if (nameLabel != null)
            {
                bool showName = _useCustomStorageSkin ? false : !hasIcon;
                nameLabel.gameObject.SetActive(showName);
                if (showName)
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
                "Bean" => "bean_.png",
                "BrownSeed" => "ember_seed.png",
                "Ember Seed" => "ember_seed.png",
                "LightBrownSeed" => "moon_seed.png",
                "Moon Seed" => "moon_seed.png",
                "LongSeed" => "LongSeed.png",
                "Long Seed" => "LongSeed.png",
                "Seed1" => "FlowerV1_seeds.png",
                "Wild Seed" => "FlowerV1_seeds.png",
                "SeedV1" => "FlowerV1_seeds.png",
                "Roseglow" => "FlowerV1_seeds.png",
                "SeedV2" => "FlowerV2_seeds.png",
                "Amberbloom" => "FlowerV2_seeds.png",
                "SeedV3" => "FlowerV3_seeds.png",
                "Moonpetal" => "FlowerV3_seeds.png",
                "Plant" => "FlowerV3.png",
                _ => null
            };

            Sprite sprite = string.IsNullOrWhiteSpace(fileName) ? key switch
            {
                "Water" => GetToolIconSprite(FPSRaycastInteractor.ApplyTool.Water),
                "Fire" => GetToolIconSprite(FPSRaycastInteractor.ApplyTool.Fire),
                "Stone" => GetToolIconSprite(FPSRaycastInteractor.ApplyTool.Stone),
                _ => null
            } : LoadStorageIconSprite(fileName);

            if (sprite == null)
            {
                _storageIconCache[key] = null;
                return null;
            }

            _storageIconCache[key] = sprite;
            return sprite;
        }

        private static Color GetStorageItemIconColor(string itemLabel)
        {
            return itemLabel switch
            {
                "Water" => new Color(0.52f, 0.85f, 1f, 0.98f),
                "Fire" => new Color(1f, 0.60f, 0.36f, 0.98f),
                "Stone" => new Color(0.82f, 0.84f, 0.88f, 0.98f),
                _ => new Color(1f, 1f, 1f, 0.96f)
            };
        }

        private Sprite LoadStorageIconSprite(string fileName)
        {
            const string assetFolder = "Assets/Screenshots/Snapshot_of_models";
            return LoadSpriteFromAssetOrDisk(assetFolder, Path.Combine(Application.dataPath, "Screenshots/Snapshot_of_models"), fileName, "StorageIcon_", removeFlatBackground: true);
        }

        private Sprite GetCustomUISprite(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
                return null;

            if (_customUISpriteCache.TryGetValue(spriteName, out var cached))
                return cached;

            var sprite = LoadSpriteFromAssetOrDisk(
                CustomUIFolderAssetPath,
                Path.Combine(Application.dataPath, "CustomUI"),
                $"{spriteName}.png",
                "CustomUI_");
            _customUISpriteCache[spriteName] = sprite;
            return sprite;
        }

        private Sprite GetToolIconSprite(FPSRaycastInteractor.ApplyTool tool)
        {
            string key = $"ToolIcon_{tool}";
            if (_customUISpriteCache.TryGetValue(key, out var cached))
                return cached;

            Sprite sprite = tool switch
            {
                FPSRaycastInteractor.ApplyTool.Seed => LoadProjectSprite("Assets/Screenshots/Snapshot_of_models/FlowerV1_seeds.png", "ToolSeed_", removeFlatBackground: true),
                FPSRaycastInteractor.ApplyTool.Water => LoadProjectSprite("Assets/Screenshots/Snapshot_of_models/water.png", "ToolWater_", removeFlatBackground: true)
                                                          ?? LoadProjectSprite("Assets/Screenshots/Snapshot_of_models/FlowerV2_seeds.png", "ToolWaterFallback_", removeFlatBackground: true),
                FPSRaycastInteractor.ApplyTool.Fire => LoadProjectSprite("Assets/SimpleUIKit/Images/ItemIcons/Examples/Wand.png", "ToolFireFallback_")
                                                         ?? LoadProjectSprite("Assets/Screenshots/Snapshot_of_models/Stick1.png", "ToolFireFallback2_", removeFlatBackground: true),
                FPSRaycastInteractor.ApplyTool.Plant => LoadProjectSprite("Assets/Screenshots/Snapshot_of_models/FlowerV3.png", "ToolPlant_", removeFlatBackground: true),
                FPSRaycastInteractor.ApplyTool.Stone => LoadProjectSprite("Assets/SimpleUIKit/Images/ItemIcons/Examples/Coins.png", "ToolStoneFallback_")
                                                          ?? LoadProjectSprite("Assets/Screenshots/Snapshot_of_models/Stick2.png", "ToolStoneFallback2_", removeFlatBackground: true),
                _ => null
            };

            _customUISpriteCache[key] = sprite;
            return sprite;
        }

        private static Sprite LoadProjectSprite(string assetPath, string textureNamePrefix, bool removeFlatBackground = false)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

#if UNITY_EDITOR
            var directSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (directSprite != null && !removeFlatBackground)
                return directSprite;

            var textureAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (textureAsset != null && !removeFlatBackground)
                return Sprite.Create(textureAsset, new Rect(0f, 0f, textureAsset.width, textureAsset.height), new Vector2(0.5f, 0.5f), 100f);
#endif

            string relativePath = assetPath.StartsWith("Assets/") ? assetPath.Substring("Assets/".Length) : assetPath;
            string fullPath = Path.Combine(Application.dataPath, relativePath);
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

            if (removeFlatBackground)
                RemoveFlatBackground(texture);

            texture.name = $"{textureNamePrefix}{Path.GetFileNameWithoutExtension(assetPath)}";
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static Sprite LoadEditorAssetPreviewSprite(string assetPath, string textureNamePrefix)
        {
#if UNITY_EDITOR
            var asset = AssetDatabase.LoadAssetAtPath<Object>(assetPath);
            if (asset == null)
                return null;

            var preview = AssetPreview.GetAssetPreview(asset);
            if (preview == null)
                preview = AssetPreview.GetMiniThumbnail(asset);
            if (preview == null)
                return null;

            return Sprite.Create(preview, new Rect(0f, 0f, preview.width, preview.height), new Vector2(0.5f, 0.5f), 100f);
#else
            return null;
#endif
        }

        private static Sprite LoadSpriteFromAssetOrDisk(string assetFolder, string diskFolder, string fileName, string textureNamePrefix, bool removeFlatBackground = false)
        {
#if UNITY_EDITOR
            string assetPath = $"{assetFolder}/{fileName}";
            var directSprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (directSprite != null && !removeFlatBackground)
                return directSprite;

            var textureAsset = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (textureAsset != null && !removeFlatBackground)
                return Sprite.Create(textureAsset, new Rect(0f, 0f, textureAsset.width, textureAsset.height), new Vector2(0.5f, 0.5f), 100f);
#endif

            string fullPath = Path.Combine(diskFolder, fileName);
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

            if (removeFlatBackground)
                RemoveFlatBackground(texture);

            texture.name = $"{textureNamePrefix}{Path.GetFileNameWithoutExtension(fileName)}";
            return Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
        }

        private static void RemoveFlatBackground(Texture2D texture)
        {
            if (texture == null || !texture.isReadable)
                return;

            int w = texture.width;
            int h = texture.height;
            if (w < 2 || h < 2)
                return;

            Color bg = (
                texture.GetPixel(0, 0) +
                texture.GetPixel(w - 1, 0) +
                texture.GetPixel(0, h - 1) +
                texture.GetPixel(w - 1, h - 1)) * 0.25f;

            var pixels = texture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                Color c = pixels[i];
                float diff = Mathf.Abs(c.r - bg.r) + Mathf.Abs(c.g - bg.g) + Mathf.Abs(c.b - bg.b);
                if (diff <= 0.18f)
                    pixels[i].a = 0f;
            }

            texture.SetPixels(pixels);
            texture.Apply(updateMipmaps: false, makeNoLongerReadable: false);
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

        private static Vector2 GetCustomStorageSlotPosition(bool chestSide, int index)
        {
            Vector2[] layout =
            {
                new Vector2(-78f, 0f),    new Vector2(78f, 0f),
                new Vector2(-78f, -82f),  new Vector2(78f, -82f),
                new Vector2(-78f, -164f), new Vector2(78f, -164f),
                new Vector2(-78f, -246f), new Vector2(78f, -246f),
                new Vector2(-78f, -328f), new Vector2(78f, -328f),
                new Vector2(-78f, -410f), new Vector2(78f, -410f),
            };

            if (index < 0 || index >= layout.Length)
                return Vector2.zero;
            return layout[index];
        }

        private static string GetAnimalDisplayName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return "Animal";

            string lower = rawName.ToLowerInvariant();
            if (lower.Contains("wolf")) return "Wolf";
            if (lower.Contains("deer")) return "Deer";
            if (lower.Contains("fox")) return "Fox";
            if (lower.Contains("rabbit")) return "Rabbit";
            if (lower.Contains("dog")) return "Dog";
            if (lower.Contains("cat")) return "Cat";
            if (lower.Contains("horse")) return "Horse";
            if (lower.Contains("bear")) return "Bear";
            if (lower.Contains("bison")) return "Bison";
            if (lower.Contains("giraffe")) return "Giraffe";
            if (lower.Contains("elephant")) return "Elephant";
            if (lower.Contains("lion")) return "Lion";
            if (lower.Contains("tiger")) return "Tiger";
            if (lower.Contains("cheetah")) return "Cheetah";
            return "Animal";
        }

        private string GetStorageItemDescription(string itemLabel)
        {
            if (string.IsNullOrWhiteSpace(itemLabel))
                return "No item selected.";

            return itemLabel switch
            {
                "Bean" => "A hardy starter seed. Good for early planting and steady growth.",
                "Ember Seed" => "A warm-climate seed that grows into a brighter flower variant.",
                "Moon Seed" => "A cooler-toned seed with a softer bloom silhouette.",
                "Long Seed" => "A slender seed that grows into a taller flower profile.",
                "Wild Seed" => "A rough field seed collected from older growth lines.",
                "Roseglow" => "A curated seed line that grows into a vivid rose-toned bloom.",
                "Amberbloom" => "A golden seed line suited for brighter flower patches.",
                "Moonpetal" => "A pale seed line that grows into a cooler-toned flower.",
                "Water" => "Used to water soil, support growth, and calm flames.",
                "Fire" => "Used to ignite targets and control hostile threats.",
                "Plant" => "Harvested plant matter. Feed it to nearby animals.",
                "Stone" => "A throwable resource used to distract or damage animals.",
                _ => "Stored resource ready to move between the chest and your pack."
            };
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

            if (gamepad.dpad.left.wasPressedThisFrame) dx = -1;
            else if (gamepad.dpad.right.wasPressedThisFrame) dx = 1;
            else if (gamepad.dpad.up.wasPressedThisFrame) dy = -1;
            else if (gamepad.dpad.down.wasPressedThisFrame) dy = 1;

            return dx != 0 || dy != 0;
        }

        void MoveStorageSelection(int dx, int dy)
        {
            const int columns = 2;
            const int rows = 6;

            int row = Mathf.Clamp(_storageSelectedSlot / columns, 0, rows - 1);
            int column = Mathf.Clamp(_storageSelectedSlot % columns, 0, columns - 1);

            if (dx != 0)
            {
                int nextColumn = column + dx;
                if (nextColumn < 0 || nextColumn >= columns)
                    _storageSelectedChest = !_storageSelectedChest;
                else
                    column = nextColumn;
            }

            if (dy != 0)
            {
                row = Mathf.Clamp(row + dy, 0, rows - 1);
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
                image.color = _useCustomStorageSkin
                    ? baseColor
                    : selected ? Color.Lerp(baseColor, Color.white, 0.26f) : baseColor;
            }

            var border = button.transform.Find("SelectionFrame");
            var borderImage = border != null ? border.GetComponent<Image>() : null;
            if (borderImage != null)
            {
                bool chestSlot = button.name.StartsWith("ChestSlot_");
                borderImage.color = _useCustomStorageSkin
                    ? selected
                        ? new Color(0.98f, 0.94f, 0.60f, 0.92f)
                        : new Color(1f, 1f, 1f, 0f)
                    : selected
                        ? new Color(0.98f, 0.94f, 0.60f, 0.98f)
                        : chestSlot
                            ? new Color(0.32f, 0.54f, 0.78f, 0.92f)
                            : new Color(0.36f, 0.70f, 0.40f, 0.92f);
            }
        }

        string GetStorageSelectPromptLabel()
        {
            return UseGamepadPrompts() ? "D-Pad" : "Arrow Keys";
        }

        string GetStorageConfirmPromptLabel()
        {
            return UseGamepadPrompts() ? "A" : "F";
        }

        string GetStorageSwitchPromptLabel()
        {
            return UseGamepadPrompts() ? "D-Pad Left/Right" : "Tab";
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

        private Image CreateDecorativeSprite(Transform parent, string name, Sprite sprite, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPos, Vector2 size, Color color)
        {
            if (sprite == null)
                return null;

            var image = CreateImage(parent, name, anchorMin, anchorMax, pivot, anchoredPos, size, color);
            image.sprite = sprite;
            image.preserveAspect = true;
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

        private void SetButtonIcon(Button button, Sprite iconSprite, Vector2 size, Color color)
        {
            if (button == null)
                return;

            var label = button.transform.Find("ButtonLabel");
            if (label != null)
            {
                var labelText = label.GetComponent<Text>();
                if (labelText != null)
                    labelText.text = string.Empty;
            }

            if (iconSprite == null)
                return;

            _storageCloseIcon = CreateDecorativeSprite(
                button.transform,
                "ButtonIcon",
                iconSprite,
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: size,
                color: color);
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
