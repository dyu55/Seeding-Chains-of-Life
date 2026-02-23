using UnityEngine;
using UnityEngine.UI;
using SCoL;
using SCoL.Inventory;
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
    SCoLInventory _inventory;
    SCoLRuntime _runtime;
    PlantVoxelRenderer _plantRenderer;
    FPSRaycastInteractor _interactor;

    void Awake()
    {
        if (cameraSource == null) cameraSource = Camera.main;
        _inventory = FindFirstObjectByType<SCoLInventory>();
        _runtime = FindFirstObjectByType<SCoLRuntime>();
        _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        _interactor = FindFirstObjectByType<FPSRaycastInteractor>();
        EnsureUI();
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
                    : "You have not discovered this item yet.") + "  [LMB collect]";
                break;
            }

            case FPSAimTargetKind.Harvestable:
            {
                title = target.root != null ? "Harvestable: " + target.root.name : "Harvestable";
                detail = "LMB harvest (adds Seed)";
                break;
            }

            case FPSAimTargetKind.LegacyPlant:
            case FPSAimTargetKind.CAPlant:
            {
                int required = _interactor != null ? Mathf.Max(1, _interactor.plantDestroyClicksRequired) : 4;
                title = target.kind == FPSAimTargetKind.LegacyPlant ? "Plant" : $"Plant Cell {target.cellX},{target.cellY}";
                detail = $"RMB x{required} to destroy";
                break;
            }

            case FPSAimTargetKind.Animal:
            {
                bool plantTool = _interactor != null && _interactor.currentTool == FPSRaycastInteractor.ApplyTool.Plant;
                title = target.animal != null ? "Animal: " + target.animal.name : "Animal";
                detail = plantTool ? "RMB feed animal" : "Press 4 then RMB to feed";
                break;
            }

            default:
                title = string.Empty;
                detail = string.Empty;
                break;
        }

        SetTargetInfo(title, detail);
    }

    void SetTargetInfo(string title, string detail)
    {
        if (_targetTitle != null) _targetTitle.text = title;
        if (_targetDetail != null) _targetDetail.text = detail;
    }

    void EnsureUI()
    {
        var canvasGO = new GameObject("FPS Crosshair (Runtime)");
        DontDestroyOnLoad(canvasGO);

        var canvas = canvasGO.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = short.MaxValue;

        canvasGO.AddComponent<CanvasScaler>();
        canvasGO.AddComponent<GraphicRaycaster>();

        var imgGO = new GameObject("Crosshair");
        imgGO.transform.SetParent(canvasGO.transform, false);

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
        titleGO.transform.SetParent(canvasGO.transform, false);
        _targetTitle = titleGO.AddComponent<Text>();
        _targetTitle.raycastTarget = false;
        _targetTitle.alignment = TextAnchor.MiddleCenter;
        _targetTitle.fontSize = targetTitleFontSize;
        _targetTitle.color = targetTitleColor;

        var detailGO = new GameObject("CrosshairTargetDetail");
        detailGO.transform.SetParent(canvasGO.transform, false);
        _targetDetail = detailGO.AddComponent<Text>();
        _targetDetail.raycastTarget = false;
        _targetDetail.alignment = TextAnchor.MiddleCenter;
        _targetDetail.fontSize = targetDetailFontSize;
        _targetDetail.color = targetDetailColor;

        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
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
        if (FindFirstObjectByType<FPSCrosshair>() != null) return;
        var go = new GameObject("FPSCrosshair (Runtime)");
        DontDestroyOnLoad(go);
        go.AddComponent<FPSCrosshair>();
    }
}
