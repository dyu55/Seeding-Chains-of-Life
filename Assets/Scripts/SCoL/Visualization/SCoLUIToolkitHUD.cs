using System.Text;
using UnityEngine;
using UnityEngine.UI;
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

        [Header("SimpleUIKit")]
        public bool useSimpleUIKitHealthBarPrefab = true;
        public GameObject simpleUIKitHealthBarPrefab;
        public bool useSimpleUIKitPanelPrefabs = true;
        public GameObject simpleUIKitPanelPrefab;
        public GameObject simpleUIKitInnerPanelPrefab;

        [Header("Health")]
        [Min(1f)] public float defaultMaxHealth = 100f;
        public bool autoCreatePlayerHealth = true;
        public bool showHealthText = true;

        private Canvas _canvas;
        private RectTransform _root;
        private Font _font;

        private Image _crosshairH;
        private Image _crosshairV;
        private Text _statusLabel;
        private Text _aimTitleLabel;
        private Text _aimDetailLabel;
        private Text _inventoryLabel;
        private Slider _healthSlider;
        private Text _healthLabel;
        private Image _healthFill;
        private Image _healthIcon;

        private SCoLInventory _inventory;
        private PlantVoxelRenderer _plantRenderer;
        private FPSRaycastInteractor _fpsInteractor;
        private SCoLPlayerHealth _playerHealth;

        private float _nextUpdateAt;
        private readonly StringBuilder _sb = new StringBuilder(256);
        private static Sprite _fallbackWhiteUISprite;

        private static readonly Color CrosshairIdle = new Color(1f, 1f, 1f, 0.90f);
        private static readonly Color CrosshairHover = new Color(0.35f, 1f, 0.35f, 0.98f);
        private const string SimpleUIKitSliderPrefabPath = "Assets/SimpleUIKit/Prefabs/Elements/SliderScale.prefab";
        private const string SimpleUIKitPanelPrefabPath = "Assets/SimpleUIKit/Prefabs/Elements/Parts/Background.prefab";
        private const string SimpleUIKitInnerPanelPrefabPath = "Assets/SimpleUIKit/Prefabs/Elements/Parts/BackgroundInner.prefab";

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

            if (!TryCreateSimpleUIKitHealthBar(_root))
                CreateFallbackHealthBar(_root);
        }

        private void BuildPanelsAndLabels()
        {
            var statusPanel = CreatePanel(
                _root,
                "StatusPanel",
                anchorMin: new Vector2(0f, 1f),
                anchorMax: new Vector2(0f, 1f),
                pivot: new Vector2(0f, 1f),
                anchoredPos: new Vector2(12f, -12f),
                size: new Vector2(360f, 95f),
                bg: new Color(0f, 0f, 0f, 0.45f));

            _statusLabel = CreateText(
                statusPanel,
                "StatusText",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(-16f, -16f),
                fontSize: 16,
                color: new Color(0f, 0f, 0f, 0.95f),
                alignment: TextAnchor.MiddleCenter);

            // Center aim info panel intentionally disabled to keep crosshair area unobstructed.
            _aimTitleLabel = null;
            _aimDetailLabel = null;

            var invPanel = CreatePanel(
                _root,
                "InventoryPanel",
                anchorMin: new Vector2(1f, 0f),
                anchorMax: new Vector2(1f, 0f),
                pivot: new Vector2(1f, 0f),
                anchoredPos: new Vector2(-12f, 12f),
                size: new Vector2(280f, 122f),
                bg: new Color(0f, 0f, 0f, 0.45f),
                preferInnerStyle: true);

            _inventoryLabel = CreateText(
                invPanel,
                "InventoryText",
                anchorMin: new Vector2(0f, 0f),
                anchorMax: new Vector2(1f, 1f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: Vector2.zero,
                size: new Vector2(-16f, -16f),
                fontSize: 16,
                color: new Color(0f, 0f, 0f, 0.95f),
                alignment: TextAnchor.MiddleCenter);
        }

        private void BuildCrosshair()
        {
            _crosshairH = CreateCrosshairLine(_root, "CrosshairH", new Vector2(16f, 2f));
            _crosshairV = CreateCrosshairLine(_root, "CrosshairV", new Vector2(2f, 16f));
            _crosshairH.color = CrosshairIdle;
            _crosshairV.color = CrosshairIdle;
        }

        private bool TryCreateSimpleUIKitHealthBar(RectTransform parent)
        {
            if (!useSimpleUIKitHealthBarPrefab)
                return false;

            if (simpleUIKitHealthBarPrefab == null)
            {
#if UNITY_EDITOR
                simpleUIKitHealthBarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimpleUIKitSliderPrefabPath);
#endif
                if (simpleUIKitHealthBarPrefab == null)
                    return false;
            }

            var barGo = Instantiate(simpleUIKitHealthBarPrefab, parent, worldPositionStays: false);
            barGo.name = "HealthBar (SimpleUIKit)";

            var rt = barGo.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = new Vector2(0.5f, 0f);
                rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot = new Vector2(0.5f, 0f);
                rt.anchoredPosition = new Vector2(0f, 26f);
                rt.sizeDelta = new Vector2(620f, 82f);
                rt.localScale = Vector3.one;
            }

            var input = barGo.GetComponentInChildren<InputField>(true);
            if (input != null)
                input.gameObject.SetActive(false);

            // This HUD uses the prefab only as a visual style container.
            // Disable kit-specific interactive slider behaviour at runtime.
            var behaviours = barGo.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                var b = behaviours[i];
                if (b == null) continue;
                if (b.GetType().Name == "CustomSlider")
                    b.enabled = false;
            }

            _healthSlider = barGo.GetComponentInChildren<Slider>(true);
            if (_healthSlider == null)
            {
                Destroy(barGo);
                return false;
            }

            _healthSlider.interactable = false;
            _healthSlider.transition = Selectable.Transition.None;
            _healthSlider.minValue = 0f;
            _healthSlider.maxValue = Mathf.Max(1f, defaultMaxHealth);
            _healthSlider.wholeNumbers = false;

            if (_healthSlider.fillRect != null)
                _healthFill = _healthSlider.fillRect.GetComponent<Image>();

            _healthLabel = CreateText(
                barGo.transform as RectTransform,
                "HealthText",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(34f, 0f),
                size: new Vector2(240f, 36f),
                fontSize: 24,
                color: new Color(0f, 0f, 0f, 0.96f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);

            _healthIcon = CreateHealthIcon(barGo.transform as RectTransform, new Vector2(-120f, 0f), 30f);
            return true;
        }

        private void CreateFallbackHealthBar(RectTransform parent)
        {
            var panel = CreatePanel(
                parent,
                "HealthBar (Fallback)",
                anchorMin: new Vector2(0.5f, 0f),
                anchorMax: new Vector2(0.5f, 0f),
                pivot: new Vector2(0.5f, 0f),
                anchoredPos: new Vector2(0f, 26f),
                size: new Vector2(620f, 82f),
                bg: new Color(0f, 0f, 0f, 0.5f));

            var sliderGO = new GameObject("Slider", typeof(RectTransform), typeof(Slider));
            var sliderRt = sliderGO.GetComponent<RectTransform>();
            sliderGO.transform.SetParent(panel, false);
            sliderRt.anchorMin = new Vector2(0f, 0f);
            sliderRt.anchorMax = new Vector2(1f, 1f);
            sliderRt.offsetMin = new Vector2(14f, 14f);
            sliderRt.offsetMax = new Vector2(-14f, -14f);

            var bg = new GameObject("Background", typeof(RectTransform), typeof(Image));
            var bgRt = bg.GetComponent<RectTransform>();
            bg.transform.SetParent(sliderGO.transform, false);
            bgRt.anchorMin = Vector2.zero;
            bgRt.anchorMax = Vector2.one;
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0.08f, 0.08f, 0.08f, 0.9f);

            var fillArea = new GameObject("Fill Area", typeof(RectTransform));
            var fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillArea.transform.SetParent(sliderGO.transform, false);
            fillAreaRt.anchorMin = Vector2.zero;
            fillAreaRt.anchorMax = Vector2.one;
            fillAreaRt.offsetMin = new Vector2(2f, 2f);
            fillAreaRt.offsetMax = new Vector2(-2f, -2f);

            var fill = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            var fillRt = fill.GetComponent<RectTransform>();
            fill.transform.SetParent(fillArea.transform, false);
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            _healthFill = fill.GetComponent<Image>();
            _healthFill.color = new Color(0.22f, 0.88f, 0.36f, 0.96f);

            _healthSlider = sliderGO.GetComponent<Slider>();
            _healthSlider.targetGraphic = bgImg;
            _healthSlider.fillRect = fillRt;
            _healthSlider.direction = Slider.Direction.LeftToRight;
            _healthSlider.transition = Selectable.Transition.None;
            _healthSlider.interactable = false;
            _healthSlider.minValue = 0f;
            _healthSlider.maxValue = Mathf.Max(1f, defaultMaxHealth);
            _healthSlider.wholeNumbers = false;

            _healthLabel = CreateText(
                panel,
                "HealthText",
                anchorMin: new Vector2(0.5f, 0.5f),
                anchorMax: new Vector2(0.5f, 0.5f),
                pivot: new Vector2(0.5f, 0.5f),
                anchoredPos: new Vector2(34f, 0f),
                size: new Vector2(240f, 36f),
                fontSize: 24,
                color: new Color(0f, 0f, 0f, 0.96f),
                alignment: TextAnchor.MiddleCenter,
                addOutline: true);

            _healthIcon = CreateHealthIcon(panel, new Vector2(-120f, 0f), 30f);
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
            _sb.AppendLine("SCoL");

            if (runtime != null)
            {
                _sb.AppendLine($"{runtime.CurrentSeason} / {runtime.CurrentWeather}");
                _sb.AppendLine($"View: {runtime.ViewMode}   Fire: {(runtime.OverlayFire ? "ON" : "OFF")}");
            }

            if (_fpsInteractor != null)
            {
                _sb.AppendLine($"Tool: {_fpsInteractor.currentTool}");
                if (_fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Seed)
                    _sb.AppendLine($"Flower: {_fpsInteractor.GetSelectedFlowerName()}");
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

            _inventoryLabel.text = $"Seed: {_inventory.seeds}\nWater: {_inventory.water}\nFire: {_inventory.fire}\nPlant: {_inventory.plants}";
        }

        private void UpdateHealth()
        {
            if (_healthSlider == null)
                return;

            float max = Mathf.Max(1f, defaultMaxHealth);
            float current = max;

            if (_playerHealth != null)
            {
                max = Mathf.Max(1f, _playerHealth.MaxHealth);
                current = Mathf.Clamp(_playerHealth.CurrentHealth, 0f, max);
            }

            if (!Mathf.Approximately(_healthSlider.maxValue, max))
                _healthSlider.maxValue = max;

            _healthSlider.value = current;

            if (_healthFill != null)
            {
                float t = max > 0f ? current / max : 0f;
                _healthFill.color = Color.Lerp(new Color(0.92f, 0.24f, 0.20f, 0.98f), new Color(0.22f, 0.88f, 0.36f, 0.98f), t);
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
            if (_crosshairH == null || _crosshairV == null)
                return;

            bool hasTarget = FPSAimTargeting.TryResolve(cameraSource, maxDistance, hitMask, runtime, _plantRenderer, out var target)
                             && target.HasActionableTarget;

            Color cross = hasTarget ? CrosshairHover : CrosshairIdle;
            _crosshairH.color = cross;
            _crosshairV.color = cross;

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
                            title = "Item: " + _inventory.GetItemDisplayName(pickup.type, true);
                            detail = _inventory.GetItemDescription(pickup.type, true) + "  [LMB collect]";
                        }
                        else
                        {
                            title = "Item: Unknown item";
                            detail = "You have not discovered this item yet.  [LMB collect]";
                        }
                        break;
                    }
                    case FPSAimTargetKind.Harvestable:
                        title = "Harvestable";
                        detail = "LMB harvest (adds Seed)";
                        break;
                    case FPSAimTargetKind.LegacyPlant:
                    case FPSAimTargetKind.CAPlant:
                    {
                        int clicks = _fpsInteractor != null ? Mathf.Max(1, _fpsInteractor.plantDestroyClicksRequired) : 4;
                        title = target.kind == FPSAimTargetKind.LegacyPlant ? "Plant" : $"Plant Cell {target.cellX},{target.cellY}";
                        detail = $"LMB uproot (+Plant)  |  RMB x{clicks} destroy";
                        break;
                    }
                    case FPSAimTargetKind.Animal:
                    {
                        bool plantTool = _fpsInteractor != null && _fpsInteractor.currentTool == FPSRaycastInteractor.ApplyTool.Plant;
                        title = target.animal != null ? $"Animal: {target.animal.name}" : "Animal";
                        detail = plantTool ? "RMB feed animal" : "Press 4 then RMB to feed";
                        break;
                    }
                }
            }
            else if (_inventory != null && _fpsInteractor != null)
            {
                SCoLItemType item;
                string hint;
                switch (_fpsInteractor.currentTool)
                {
                    case FPSRaycastInteractor.ApplyTool.Seed:
                        item = SCoLItemType.Seed; hint = "RMB plant"; break;
                    case FPSRaycastInteractor.ApplyTool.Water:
                        item = SCoLItemType.Water; hint = "RMB apply water"; break;
                    case FPSRaycastInteractor.ApplyTool.Fire:
                        item = SCoLItemType.Fire; hint = "RMB ignite"; break;
                    default:
                        item = SCoLItemType.Plant; hint = "RMB feed animal"; break;
                }

                title = "Held: " + _inventory.GetItemDisplayName(item, true);
                if (item == SCoLItemType.Seed && _fpsInteractor != null)
                    title += $" ({_fpsInteractor.GetSelectedFlowerName()})";
                detail = _inventory.GetItemDescription(item, true) + $"  [{hint}]";
            }

            if (_aimTitleLabel != null)
                _aimTitleLabel.text = title;
            if (_aimDetailLabel != null)
                _aimDetailLabel.text = detail;
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

        private Image CreateCrosshairLine(Transform parent, string name, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.raycastTarget = false;
            return image;
        }

        private Image CreateHealthIcon(Transform parent, Vector2 anchoredPos, float size)
        {
            var go = new GameObject("HealthIcon", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = new Vector2(size, size);

            var icon = go.GetComponent<Image>();
            icon.sprite = ResolveBuiltinUISprite();
            icon.color = new Color(0.95f, 0.2f, 0.24f, 0.98f);
            icon.raycastTarget = false;
            return icon;
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
