using System.Collections.Generic;
using UnityEngine;
using SCoL;
using SCoL.Visualization;

/// <summary>
/// Adds a pulsing glow aura to the currently aimed interactable target.
/// Uses center-screen ray (FPS aim-hover).
/// </summary>
[DisallowMultipleComponent]
public class FPSAimAuraHighlighter : MonoBehaviour
{
    public Camera cameraSource;
    public float maxDistance = 50f;
    public LayerMask hitMask = ~0;

    [Header("Aura")]
    public Color auraColor = new Color(0.35f, 0.95f, 1f);
    [Min(0f)] public float minEmission = 0.7f;
    [Min(0f)] public float maxEmission = 1.9f;
    [Min(0.1f)] public float pulseSpeed = 4f;

    private SCoLRuntime _runtime;
    private PlantVoxelRenderer _plantRenderer;
    private Transform _currentRoot;

    private readonly List<Renderer> _renderers = new List<Renderer>(32);
    private readonly List<Material[]> _originalMaterials = new List<Material[]>(32);
    private readonly List<Material[]> _runtimeMaterials = new List<Material[]>(32);

    private void Awake()
    {
        if (cameraSource == null) cameraSource = Camera.main;
        _runtime = FindFirstObjectByType<SCoLRuntime>();
        _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
    }

    private void OnDisable()
    {
        ClearCurrent();
    }

    private void Update()
    {
        if (cameraSource == null) cameraSource = Camera.main;
        if (_runtime == null) _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_plantRenderer == null) _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();

        if (!FPSAimTargeting.TryResolve(cameraSource, maxDistance, hitMask, _runtime, _plantRenderer, out var target) ||
            !target.HasActionableTarget ||
            target.root == null)
        {
            if (_currentRoot != null)
                ClearCurrent();
            return;
        }

        if (_currentRoot != target.root)
        {
            ClearCurrent();
            SetCurrent(target.root);
        }

        UpdateEmissionPulse();
    }

    private void SetCurrent(Transform root)
    {
        _currentRoot = root;
        _renderers.Clear();
        _originalMaterials.Clear();
        _runtimeMaterials.Clear();

        var children = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        for (int i = 0; i < children.Length; i++)
        {
            var r = children[i];
            if (r == null) continue;

            var originals = r.sharedMaterials;
            if (originals == null || originals.Length == 0) continue;

            var runtime = new Material[originals.Length];
            for (int m = 0; m < originals.Length; m++)
            {
                if (originals[m] == null)
                {
                    runtime[m] = null;
                    continue;
                }

                var mat = new Material(originals[m]);
                mat.name = originals[m].name + "_AimAuraRuntime";
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", auraColor * minEmission);
                }
                runtime[m] = mat;
            }

            r.sharedMaterials = runtime;
            _renderers.Add(r);
            _originalMaterials.Add(originals);
            _runtimeMaterials.Add(runtime);
        }
    }

    private void UpdateEmissionPulse()
    {
        float t = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.Max(0.1f, pulseSpeed));
        float intensity = Mathf.Lerp(minEmission, maxEmission, t);
        var color = auraColor * intensity;

        for (int i = 0; i < _runtimeMaterials.Count; i++)
        {
            var mats = _runtimeMaterials[i];
            if (mats == null) continue;

            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null || !mat.HasProperty("_EmissionColor"))
                    continue;
                mat.SetColor("_EmissionColor", color);
            }
        }
    }

    private void ClearCurrent()
    {
        for (int i = 0; i < _renderers.Count; i++)
        {
            var r = _renderers[i];
            if (r != null && i < _originalMaterials.Count)
                r.sharedMaterials = _originalMaterials[i];

            if (i < _runtimeMaterials.Count && _runtimeMaterials[i] != null)
            {
                var mats = _runtimeMaterials[i];
                for (int m = 0; m < mats.Length; m++)
                {
                    if (mats[m] != null)
                        Destroy(mats[m]);
                }
            }
        }

        _renderers.Clear();
        _originalMaterials.Clear();
        _runtimeMaterials.Clear();
        _currentRoot = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (FindFirstObjectByType<FPSAimAuraHighlighter>() != null)
            return;

        var go = new GameObject("FPSAimAuraHighlighter (Runtime)");
        DontDestroyOnLoad(go);
        go.AddComponent<FPSAimAuraHighlighter>();
    }
}
