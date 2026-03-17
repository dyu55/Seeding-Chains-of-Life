using System.Collections.Generic;
using UnityEngine;
using SCoL;
using SCoL.Inventory;
using SCoL.Visualization;
using SCoL.XR;

/// <summary>
/// Highlights actionable gameplay objects so players can read affordances before aiming directly.
/// Nearby interactables get a soft glow, and the current aim target gets a stronger pulse.
/// </summary>
[DisallowMultipleComponent]
public class FPSAimAuraHighlighter : MonoBehaviour
{
    public Camera cameraSource;
    public float maxDistance = 50f;
    public LayerMask hitMask = ~0;

    [Header("Discovery Aura")]
    [Min(1f)] public float awarenessRadius = 9f;
    [Range(1, 64)] public int maxNearbyHighlights = 24;
    [Min(0.05f)] public float refreshInterval = 0.35f;
    public Color nearbyAuraColor = new Color(0.34f, 0.90f, 1f);
    [Min(0f)] public float nearbyMinEmission = 0.16f;
    [Min(0f)] public float nearbyMaxEmission = 0.42f;
    [Range(0f, 1f)] public float nearbyTintStrength = 0.16f;

    [Header("Focus Aura")]
    public Color focusAuraColor = new Color(0.95f, 0.88f, 0.42f);
    [Min(0f)] public float focusMinEmission = 0.95f;
    [Min(0f)] public float focusMaxEmission = 2.30f;
    [Range(0f, 1f)] public float focusTintStrength = 0.28f;
    [Min(0.1f)] public float pulseSpeed = 4f;

    private sealed class AuraState
    {
        public Transform root;
        public Renderer[] renderers;
        public Material[][] originalMaterials;
        public Material[][] runtimeMaterials;
        public bool[][] hasBaseColor;
        public bool[][] hasColor;
        public Color[][] baseColors;
        public Color[][] colorValues;
        public bool isFocused;
    }

    private SCoLRuntime _runtime;
    private PlantVoxelRenderer _plantRenderer;
    private float _nextRefreshAt;

    private readonly Dictionary<int, AuraState> _activeStates = new Dictionary<int, AuraState>(32);
    private readonly List<Transform> _sceneCandidates = new List<Transform>(64);
    private readonly List<int> _pendingRemoval = new List<int>(32);
    private readonly HashSet<int> _sceneCandidateIds = new HashSet<int>();
    private readonly HashSet<int> _desiredIds = new HashSet<int>();

    private void Awake()
    {
        if (cameraSource == null) cameraSource = Camera.main;
        _runtime = FindFirstObjectByType<SCoLRuntime>();
        _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
    }

    private void OnDisable()
    {
        ClearAll();
    }

    private void Update()
    {
        if (cameraSource == null) cameraSource = Camera.main;
        if (_runtime == null) _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_plantRenderer == null) _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();

        if (Time.unscaledTime >= _nextRefreshAt)
        {
            _nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            RefreshHighlights();
        }

        UpdateEmissionPulse();
    }

    private void RefreshHighlights()
    {
        Transform focusedRoot = null;
        if (FPSAimTargeting.TryResolve(cameraSource, maxDistance, hitMask, _runtime, _plantRenderer, out var target) &&
            target.HasActionableTarget)
        {
            focusedRoot = target.root;
        }

        CollectSceneCandidates();
        _desiredIds.Clear();

        if (focusedRoot != null)
            AddDesiredRoot(focusedRoot, isFocused: true);

        int nearbyCount = 0;
        for (int i = 0; i < _sceneCandidates.Count; i++)
        {
            var root = _sceneCandidates[i];
            if (root == null || root == focusedRoot)
                continue;
            if (nearbyCount >= maxNearbyHighlights)
                break;
            if (!IsWithinAwareness(root))
                continue;

            AddDesiredRoot(root, isFocused: false);
            nearbyCount++;
        }

        _pendingRemoval.Clear();
        foreach (var kvp in _activeStates)
        {
            var state = kvp.Value;
            if (state == null || state.root == null || !_desiredIds.Contains(kvp.Key))
                _pendingRemoval.Add(kvp.Key);
        }

        for (int i = 0; i < _pendingRemoval.Count; i++)
            RemoveState(_pendingRemoval[i]);
    }

    private void CollectSceneCandidates()
    {
        _sceneCandidates.Clear();
        _sceneCandidateIds.Clear();

        var pickups = FindObjectsByType<SCoLPickup>(FindObjectsSortMode.None);
        for (int i = 0; i < pickups.Length; i++)
            AddSceneCandidate(pickups[i] != null ? pickups[i].transform : null);

        var grabbables = FindObjectsByType<SCoLGrabbable>(FindObjectsSortMode.None);
        for (int i = 0; i < grabbables.Length; i++)
            AddSceneCandidate(grabbables[i] != null ? grabbables[i].transform : null);

        var legacyPlants = FindObjectsByType<FPSSeedGrowth>(FindObjectsSortMode.None);
        for (int i = 0; i < legacyPlants.Length; i++)
            AddSceneCandidate(legacyPlants[i] != null ? legacyPlants[i].transform : null);

        var animals = FindObjectsByType<FPSBoidAgent>(FindObjectsSortMode.None);
        for (int i = 0; i < animals.Length; i++)
            AddSceneCandidate(animals[i] != null ? animals[i].transform : null);

        var harvestables = GameObject.FindGameObjectsWithTag("Harvestable");
        for (int i = 0; i < harvestables.Length; i++)
            AddSceneCandidate(harvestables[i] != null ? harvestables[i].transform : null);

        // CA plants are spawned as named runtime roots by PlantVoxelRenderer.
        var transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || !t.name.StartsWith("Plant_"))
                continue;
            AddSceneCandidate(t);
        }
    }

    private void AddSceneCandidate(Transform root)
    {
        if (root == null || !root.gameObject.activeInHierarchy)
            return;

        int id = root.GetInstanceID();
        if (!_sceneCandidateIds.Add(id))
            return;

        _sceneCandidates.Add(root);
    }

    private void AddDesiredRoot(Transform root, bool isFocused)
    {
        if (root == null)
            return;

        int id = root.GetInstanceID();
        _desiredIds.Add(id);

        if (!_activeStates.TryGetValue(id, out var state))
        {
            state = CreateState(root);
            if (state == null)
                return;
            _activeStates.Add(id, state);
        }

        state.isFocused = isFocused;
    }

    private AuraState CreateState(Transform root)
    {
        if (root == null)
            return null;

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers == null || renderers.Length == 0)
            return null;

        var rendererList = new List<Renderer>(renderers.Length);
        var originalList = new List<Material[]>(renderers.Length);
        var runtimeList = new List<Material[]>(renderers.Length);
        var hasBaseColorList = new List<bool[]>(renderers.Length);
        var hasColorList = new List<bool[]>(renderers.Length);
        var baseColorList = new List<Color[]>(renderers.Length);
        var colorValueList = new List<Color[]>(renderers.Length);

        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            var originals = renderer.sharedMaterials;
            if (originals == null || originals.Length == 0)
                continue;

            var runtimeMaterials = new Material[originals.Length];
            var hasBaseColor = new bool[originals.Length];
            var hasColor = new bool[originals.Length];
            var baseColors = new Color[originals.Length];
            var colorValues = new Color[originals.Length];
            bool hasAnyMaterial = false;

            for (int m = 0; m < originals.Length; m++)
            {
                var original = originals[m];
                if (original == null)
                    continue;

                var runtime = new Material(original)
                {
                    name = original.name + "_InteractableAura"
                };

                if (runtime.HasProperty("_EmissionColor"))
                    runtime.EnableKeyword("_EMISSION");

                hasBaseColor[m] = runtime.HasProperty("_BaseColor");
                hasColor[m] = runtime.HasProperty("_Color");
                if (hasBaseColor[m])
                    baseColors[m] = runtime.GetColor("_BaseColor");
                if (hasColor[m])
                    colorValues[m] = runtime.GetColor("_Color");

                runtimeMaterials[m] = runtime;
                hasAnyMaterial = true;
            }

            if (!hasAnyMaterial)
                continue;

            renderer.sharedMaterials = runtimeMaterials;
            rendererList.Add(renderer);
            originalList.Add(originals);
            runtimeList.Add(runtimeMaterials);
            hasBaseColorList.Add(hasBaseColor);
            hasColorList.Add(hasColor);
            baseColorList.Add(baseColors);
            colorValueList.Add(colorValues);
        }

        if (rendererList.Count == 0)
            return null;

        return new AuraState
        {
            root = root,
            renderers = rendererList.ToArray(),
            originalMaterials = originalList.ToArray(),
            runtimeMaterials = runtimeList.ToArray(),
            hasBaseColor = hasBaseColorList.ToArray(),
            hasColor = hasColorList.ToArray(),
            baseColors = baseColorList.ToArray(),
            colorValues = colorValueList.ToArray(),
            isFocused = false
        };
    }

    private void UpdateEmissionPulse()
    {
        if (_activeStates.Count == 0)
            return;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.Max(0.1f, pulseSpeed));

        foreach (var kvp in _activeStates)
        {
            var state = kvp.Value;
            if (state == null || state.root == null)
                continue;

            Color auraColor = state.isFocused ? focusAuraColor : nearbyAuraColor;
            float intensity = state.isFocused
                ? Mathf.Lerp(focusMinEmission, focusMaxEmission, pulse)
                : Mathf.Lerp(nearbyMinEmission, nearbyMaxEmission, pulse);
            float tintStrength = state.isFocused
                ? Mathf.Lerp(focusTintStrength * 0.7f, focusTintStrength, pulse)
                : Mathf.Lerp(nearbyTintStrength * 0.7f, nearbyTintStrength, pulse);

            ApplyAura(state, auraColor, intensity, tintStrength);
        }
    }

    private static void ApplyAura(AuraState state, Color auraColor, float intensity, float tintStrength)
    {
        if (state == null || state.runtimeMaterials == null)
            return;

        var emissionColor = auraColor * intensity;
        for (int i = 0; i < state.runtimeMaterials.Length; i++)
        {
            var mats = state.runtimeMaterials[i];
            if (mats == null)
                continue;

            for (int m = 0; m < mats.Length; m++)
            {
                var mat = mats[m];
                if (mat == null)
                    continue;

                if (mat.HasProperty("_EmissionColor"))
                    mat.SetColor("_EmissionColor", emissionColor);

                if (state.hasBaseColor != null &&
                    i < state.hasBaseColor.Length &&
                    state.hasBaseColor[i] != null &&
                    m < state.hasBaseColor[i].Length &&
                    state.hasBaseColor[i][m] &&
                    state.baseColors != null &&
                    i < state.baseColors.Length &&
                    state.baseColors[i] != null &&
                    m < state.baseColors[i].Length)
                {
                    var original = state.baseColors[i][m];
                    mat.SetColor("_BaseColor", Color.Lerp(original, Color.Lerp(original, auraColor, 0.55f), tintStrength));
                }

                if (state.hasColor != null &&
                    i < state.hasColor.Length &&
                    state.hasColor[i] != null &&
                    m < state.hasColor[i].Length &&
                    state.hasColor[i][m] &&
                    state.colorValues != null &&
                    i < state.colorValues.Length &&
                    state.colorValues[i] != null &&
                    m < state.colorValues[i].Length)
                {
                    var original = state.colorValues[i][m];
                    mat.SetColor("_Color", Color.Lerp(original, Color.Lerp(original, auraColor, 0.55f), tintStrength));
                }
            }
        }
    }

    private bool IsWithinAwareness(Transform root)
    {
        if (cameraSource == null || root == null)
            return false;

        var camPos = cameraSource.transform.position;
        float radiusSq = awarenessRadius * awarenessRadius;
        return (root.position - camPos).sqrMagnitude <= radiusSq;
    }

    private void RemoveState(int id)
    {
        if (!_activeStates.TryGetValue(id, out var state))
            return;

        RestoreState(state);
        _activeStates.Remove(id);
    }

    private void ClearAll()
    {
        foreach (var kvp in _activeStates)
            RestoreState(kvp.Value);

        _activeStates.Clear();
        _pendingRemoval.Clear();
        _desiredIds.Clear();
        _sceneCandidateIds.Clear();
        _sceneCandidates.Clear();
    }

    private static void RestoreState(AuraState state)
    {
        if (state == null)
            return;

        if (state.renderers != null && state.originalMaterials != null)
        {
            for (int i = 0; i < state.renderers.Length; i++)
            {
                var renderer = state.renderers[i];
                if (renderer != null && i < state.originalMaterials.Length)
                    renderer.sharedMaterials = state.originalMaterials[i];
            }
        }

        if (state.runtimeMaterials == null)
            return;

        for (int i = 0; i < state.runtimeMaterials.Length; i++)
        {
            var mats = state.runtimeMaterials[i];
            if (mats == null)
                continue;

            for (int m = 0; m < mats.Length; m++)
            {
                if (mats[m] != null)
                    Destroy(mats[m]);
            }
        }
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
