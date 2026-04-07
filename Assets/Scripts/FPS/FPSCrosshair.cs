using UnityEngine;
using UnityEngine.UI;
using SCoL;
using SCoL.Inventory;
using SCoL.InputLayer;
using SCoL.Visualization;

/// <summary>
/// Simple center crosshair with FPS aim-hover target info.
/// Creates its own Canvas at runtime (no scene wiring).
/// </summary>
public class FPSCrosshair : MonoBehaviour
{
    public Camera cameraSource;
    public float maxDistance = 50f;
    public LayerMask hitMask = ~0;

    [Header("Appearance")]
    public float sizePx = 10f;
    public float thicknessPx = 2f;
    public Color idleColor = new Color(1f, 1f, 1f, 0.9f);
    public Color hoverColor = new Color(0.3f, 1f, 0.3f, 0.95f);
    public bool showTargetInfo = true;
    public Color targetTitleColor = new Color(0.95f, 0.98f, 1f, 0.95f);
    public Color targetDetailColor = new Color(0.78f, 0.92f, 1f, 0.92f);
    public int targetTitleFontSize = 16;
    public int targetDetailFontSize = 14;
    public Vector2 targetTitleOffsetPx = new Vector2(0f, -28f);
    public Vector2 targetDetailOffsetPx = new Vector2(0f, -50f);

    Image _img;
    Text _targetTitle;
    Text _targetDetail;
    GameObject _runtimeCanvas;
    SCoLInventory _inventory;
    SCoLRuntime _runtime;
    PlantVoxelRenderer _plantRenderer;
    FPSRaycastInteractor _interactor;

    void Awake()
    {
        showTargetInfo = true;
        if (cameraSource == null) cameraSource = Camera.main;
        _inventory = FindFirstObjectByType<SCoLInventory>();
        _runtime = FindFirstObjectByType<SCoLRuntime>();
        _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        _interactor = FindFirstObjectByType<FPSRaycastInteractor>();
        EnsureUI();
    }

    void OnDisable()
    {
        CleanupRuntimeCanvas();
    }

    void OnDestroy()
    {
        CleanupRuntimeCanvas();
    }

    void Update()
    {
        if (cameraSource == null || _img == null) return;
        if (_inventory == null) _inventory = FindFirstObjectByType<SCoLInventory>();
        if (_runtime == null) _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_plantRenderer == null) _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        if (_interactor == null) _interactor = FindFirstObjectByType<FPSRaycastInteractor>();

        bool hover = TryGetAimTarget(out var target) && target.HasActionableTarget;
        _img.color = hover ? hoverColor : idleColor;

        if (showTargetInfo)
            UpdateTargetInfo(target);
        else
            SetTargetInfo(string.Empty, string.Empty);
    }

    bool TryGetAimTarget(out FPSAimTargetInfo target)
    {
        return FPSAimTargeting.TryResolve(cameraSource, maxDistance, hitMask, _runtime, _plantRenderer, out target);
    }

    void UpdateTargetInfo(FPSAimTargetInfo target)
    {
        if (!target.HasActionableTarget)
        {
            if (TryGetHeldItemInfo(out string heldTitle, out string heldDetail))
                SetTargetInfo(heldTitle, heldDetail);
            else
                SetTargetInfo(string.Empty, string.Empty);
            return;
        }

        string title;
        string detail;

        switch (target.kind)
        {
            case FPSAimTargetKind.Pickup:
            {
                if (target.pickup == null)
                {
                    SetTargetInfo(string.Empty, string.Empty);
                    return;
                }

                var type = target.pickup.type;
                title = "Item: " + (_inventory != null
                    ? _inventory.GetItemDisplayName(type, unknownIfUndiscovered: true)
                    : "Unknown item");
                detail = (_inventory != null
                    ? _inventory.GetItemDescription(type, unknownIfUndiscovered: true)
                    : "You have not discovered this item yet.") + $"  [{GetPrimaryPromptLabel()} pick up]";
                break;
            }

            case FPSAimTargetKind.Harvestable:
            {
                title = target.root != null ? "Harvestable: " + target.root.name : "Harvestable";
                detail = "No direct click action";
                break;
            }

            case FPSAimTargetKind.LegacyPlant:
            case FPSAimTargetKind.CAPlant:
            {
                title = target.kind == FPSAimTargetKind.LegacyPlant ? "Plant" : $"Plant Cell {target.cellX},{target.cellY}";
                detail = _interactor != null && _interactor.currentTool == FPSRaycastInteractor.ApplyTool.Water
                    ? $"{GetPrimaryPromptLabel()} pick plant  /  {GetSecondaryPromptLabel()} water"
                    : $"{GetPrimaryPromptLabel()} pick plant";
                break;
            }

            case FPSAimTargetKind.Animal:
            {
                bool plantTool = _interactor != null && _interactor.currentTool == FPSRaycastInteractor.ApplyTool.Plant;
                bool stoneTool = _interactor != null && _interactor.currentTool == FPSRaycastInteractor.ApplyTool.Stone;
                title = target.animal != null ? "Animal: " + GetAnimalDisplayName(target.animal) : "Animal";
                detail = stoneTool
                    ? $"{GetSecondaryPromptLabel()} use tool: throw stone"
                    : (plantTool
                        ? $"{GetSecondaryPromptLabel()} use tool: feed animal"
                        : $"Use {GetToolSwitchPromptLabel()} to pick Plant or Stone");
                break;
            }

            case FPSAimTargetKind.Grabbable:
            {
                var p = target.root != null ? target.root.GetComponentInParent<SCoLPickup>() : null;
                if (p != null && _inventory != null)
                {
                    title = "Item: " + _inventory.GetItemDisplayName(p.type, unknownIfUndiscovered: true);
                    detail = _inventory.GetItemDescription(p.type, unknownIfUndiscovered: true) + $"  [{GetPrimaryPromptLabel()} pick up]";
                }
                else
                {
                    title = "Item: Unknown item";
                    detail = $"You have not discovered this item yet.  [{GetPrimaryPromptLabel()} pick up]";
                }
                break;
            }

            default:
                title = string.Empty;
                detail = string.Empty;
                break;
        }

        SetTargetInfo(title, detail);
    }

    static string GetAnimalDisplayName(FPSBoidAgent animal)
    {
        if (animal == null || string.IsNullOrWhiteSpace(animal.name))
            return "Animal";

        string lower = animal.name.ToLowerInvariant();
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

    bool TryGetHeldItemInfo(out string title, out string detail)
    {
        title = string.Empty;
        detail = string.Empty;
        if (_interactor == null || _inventory == null)
            return false;

        SCoLItemType itemType;
        string actionHint;
        switch (_interactor.currentTool)
        {
            case FPSRaycastInteractor.ApplyTool.Seed:
                itemType = SCoLItemType.Seed;
                actionHint = $"{GetSecondaryPromptLabel()} plant  /  {GetDropPromptLabel()} drop";
                break;
            case FPSRaycastInteractor.ApplyTool.Water:
                itemType = SCoLItemType.Water;
                actionHint = $"{GetPrimaryPromptLabel()} fill at pond  /  {GetSecondaryPromptLabel()} water";
                break;
            case FPSRaycastInteractor.ApplyTool.Fire:
                itemType = SCoLItemType.Fire;
                actionHint = $"{GetSecondaryPromptLabel()} ignite  /  {GetDropPromptLabel()} drop";
                break;
            case FPSRaycastInteractor.ApplyTool.Plant:
                itemType = SCoLItemType.Plant;
                actionHint = $"{GetPrimaryPromptLabel()} pick plant  /  {GetSecondaryPromptLabel()} feed animal";
                break;
            case FPSRaycastInteractor.ApplyTool.Stone:
                itemType = SCoLItemType.Stone;
                actionHint = $"{GetSecondaryPromptLabel()} throw stone  /  {GetDropPromptLabel()} drop";
                break;
            default:
                return false;
        }

        title = "Held: " + _inventory.GetItemDisplayName(itemType, unknownIfUndiscovered: true);
        detail = _inventory.GetItemDescription(itemType, unknownIfUndiscovered: true) + $"  [{actionHint}]";
        return true;
    }

    bool UseGamepadPrompts()
    {
        return GameplayInputFacade.Player != null && GameplayInputFacade.Player.UseGamepadPrompts;
    }

    string GetPrimaryPromptLabel() => UseGamepadPrompts() ? "RT" : "LMB";
    string GetSecondaryPromptLabel() => UseGamepadPrompts() ? "LT" : "RMB";
    string GetDropPromptLabel() => UseGamepadPrompts() ? "X" : "Q";
    string GetToolSwitchPromptLabel() => UseGamepadPrompts() ? "LB/RB or D-Pad" : "Wheel or 1-5";

    void SetTargetInfo(string title, string detail)
    {
        if (_targetTitle != null) _targetTitle.text = title;
        if (_targetDetail != null) _targetDetail.text = detail;
    }

    void EnsureUI()
    {
        if (_img != null && _targetTitle != null && _targetDetail != null)
            return;

        _runtimeCanvas = new GameObject("FPS Crosshair (Runtime)");
        DontDestroyOnLoad(_runtimeCanvas);

        var canvas = _runtimeCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        _runtimeCanvas.AddComponent<CanvasScaler>();
        _runtimeCanvas.AddComponent<GraphicRaycaster>();

        var imgGO = new GameObject("Crosshair");
        imgGO.transform.SetParent(_runtimeCanvas.transform, false);

        _img = imgGO.AddComponent<Image>();
        _img.raycastTarget = false;
        _img.sprite = BuildCrossSprite(Mathf.RoundToInt(sizePx), Mathf.RoundToInt(thicknessPx));
        _img.color = idleColor;

        var rt = _img.rectTransform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(sizePx, sizePx);

        var titleGO = new GameObject("CrosshairTargetTitle");
        titleGO.transform.SetParent(_runtimeCanvas.transform, false);
        _targetTitle = titleGO.AddComponent<Text>();
        _targetTitle.raycastTarget = false;
        _targetTitle.alignment = TextAnchor.MiddleCenter;
        _targetTitle.fontSize = targetTitleFontSize;
        _targetTitle.color = targetTitleColor;
        var titleOutline = titleGO.AddComponent<Outline>();
        titleOutline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        titleOutline.effectDistance = new Vector2(1f, -1f);

        var detailGO = new GameObject("CrosshairTargetDetail");
        detailGO.transform.SetParent(_runtimeCanvas.transform, false);
        _targetDetail = detailGO.AddComponent<Text>();
        _targetDetail.raycastTarget = false;
        _targetDetail.alignment = TextAnchor.MiddleCenter;
        _targetDetail.fontSize = targetDetailFontSize;
        _targetDetail.color = targetDetailColor;
        var detailOutline = detailGO.AddComponent<Outline>();
        detailOutline.effectColor = new Color(0f, 0f, 0f, 0.85f);
        detailOutline.effectDistance = new Vector2(1f, -1f);

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (font == null)
            font = Font.CreateDynamicFontFromOSFont(new[] { "Arial", "Helvetica", "PingFang SC", "Microsoft YaHei" }, Mathf.Max(targetTitleFontSize, targetDetailFontSize));
        _targetTitle.font = font;
        _targetDetail.font = font;

        var titleRt = _targetTitle.rectTransform;
        titleRt.anchorMin = new Vector2(0.5f, 0.5f);
        titleRt.anchorMax = new Vector2(0.5f, 0.5f);
        titleRt.anchoredPosition = targetTitleOffsetPx;
        titleRt.sizeDelta = new Vector2(860f, 26f);

        var detailRt = _targetDetail.rectTransform;
        detailRt.anchorMin = new Vector2(0.5f, 0.5f);
        detailRt.anchorMax = new Vector2(0.5f, 0.5f);
        detailRt.anchoredPosition = targetDetailOffsetPx;
        detailRt.sizeDelta = new Vector2(980f, 24f);
    }

    void CleanupRuntimeCanvas()
    {
        if (_runtimeCanvas != null)
        {
            Destroy(_runtimeCanvas);
            _runtimeCanvas = null;
        }
    }

    static Sprite BuildCrossSprite(int size, int thickness)
    {
        size = Mathf.Clamp(size, 6, 64);
        thickness = Mathf.Clamp(thickness, 1, size / 2);

        var tex = new Texture2D(size, size, TextureFormat.ARGB32, mipChain: false);
        tex.filterMode = FilterMode.Point;

        var clear = new Color(1f, 1f, 1f, 0f);
        var white = new Color(1f, 1f, 1f, 1f);

        var pixels = new Color[size * size];
        for (int i = 0; i < pixels.Length; i++) pixels[i] = clear;

        int mid = size / 2;
        int halfT = thickness / 2;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool vert = Mathf.Abs(x - mid) <= halfT;
                bool horiz = Mathf.Abs(y - mid) <= halfT;
                if (vert || horiz)
                    pixels[y * size + x] = white;
            }
        }

        tex.SetPixels(pixels);
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);

        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit: size);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        if (FindFirstObjectByType<SCoL.Visualization.SCoLUIToolkitHUD>() != null) return;
        if (FindFirstObjectByType<FPSCrosshair>() != null) return;
        var go = new GameObject("FPSCrosshair (Runtime)");
        DontDestroyOnLoad(go);
        go.AddComponent<FPSCrosshair>();
    }
}
