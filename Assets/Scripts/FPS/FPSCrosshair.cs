using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Simple center crosshair that changes color when aiming at a Harvestable within range.
/// Creates its own Canvas at runtime (no scene wiring).
/// </summary>
public class FPSCrosshair : MonoBehaviour
{
    public Camera cameraSource;
    public float maxDistance = 3f;
    public LayerMask hitMask = ~0;

    [Header("Appearance")]
    public float sizePx = 10f;
    public float thicknessPx = 2f;
    public Color idleColor = new Color(1f, 1f, 1f, 0.9f);
    public Color hoverColor = new Color(0.3f, 1f, 0.3f, 0.95f);

    Image _img;

    void Awake()
    {
        if (cameraSource == null) cameraSource = Camera.main;
        EnsureUI();
    }

    void Update()
    {
        if (cameraSource == null || _img == null) return;

        bool hover = IsHoveringHarvestable();
        _img.color = hover ? hoverColor : idleColor;
    }

    bool IsHoveringHarvestable()
    {
        var ray = cameraSource.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        if (!Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            return false;

        for (var t = hit.collider != null ? hit.collider.transform : null; t != null; t = t.parent)
            if (t.gameObject.CompareTag("Harvestable")) return true;

        return false;
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
