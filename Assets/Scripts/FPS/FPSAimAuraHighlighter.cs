using System.Collections.Generic;
using UnityEngine;
using SCoL;
using SCoL.Inventory;
using SCoL.Visualization;
using SCoL.XR;

/// <summary>
/// Marks actionable gameplay objects with floating world-space icons so they read differently from scenery.
/// Nearby interactables get a subtle marker, and the current aim target gets a brighter focus marker.
/// </summary>
[DisallowMultipleComponent]
public class FPSAimAuraHighlighter : MonoBehaviour
{
    static FPSAimAuraHighlighter _instance;

    public Camera cameraSource;
    public float maxDistance = 50f;
    public LayerMask hitMask = ~0;

    [Header("Discovery Marker")]
    [Min(1f)] public float awarenessRadius = 9f;
    [Range(1, 64)] public int maxNearbyHighlights = 24;
    [Min(0.05f)] public float refreshInterval = 0.35f;
    public Color nearbyMarkerColor = new Color(0.98f, 0.88f, 0.32f, 0.92f);
    [Min(0.1f)] public float nearbyMinScale = 0.20f;
    [Min(0.1f)] public float nearbyMaxScale = 0.24f;

    [Header("Focus Marker")]
    public Color focusMarkerColor = new Color(0.98f, 0.88f, 0.32f, 1f);
    public Color waterFocusMarkerColor = new Color(0.24f, 0.58f, 1f, 1f);
    public Color fireFocusMarkerColor = new Color(1f, 0.28f, 0.20f, 1f);
    [Min(0.1f)] public float focusMinScale = 0.28f;
    [Min(0.1f)] public float focusMaxScale = 0.34f;
    [Min(0.1f)] public float pulseSpeed = 4f;

    [Header("Placement")]
    [Min(0f)] public float minWorldYOffset = 0.45f;
    [Min(0f)] public float boundsYOffset = 0.22f;
    [Min(0f)] public float stemHeight = 0.26f;
    [Min(0f)] public float markerClusterRadius = 1.6f;

    sealed class MarkerState
    {
        public Transform target;
        public Renderer[] sourceRenderers;
        public Transform markerRoot;
        public Transform halo;
        public Transform diamond;
        public Transform stem;
        public Material haloMaterial;
        public Material diamondMaterial;
        public Material stemMaterial;
        public bool isFocused;
        public bool isPlant;
    }

    SCoLRuntime _runtime;
    PlantVoxelRenderer _plantRenderer;
    FPSRaycastInteractor _fpsInteractor;
    SCoLToolController _xrToolController;
    SCoLXRInteractor _xrInteractor;
    Camera _cam;
    float _nextRefreshAt;

    readonly Dictionary<int, MarkerState> _activeStates = new Dictionary<int, MarkerState>(32);
    readonly List<Transform> _sceneCandidates = new List<Transform>(64);
    readonly List<int> _pendingRemoval = new List<int>(32);
    readonly List<Vector3> _reservedMarkerPositions = new List<Vector3>(32);
    readonly HashSet<int> _sceneCandidateIds = new HashSet<int>();
    readonly HashSet<int> _desiredIds = new HashSet<int>();

    void Awake()
    {
        if (_instance != null && _instance != this)
        {
            enabled = false;
            gameObject.SetActive(false);
            return;
        }

        _instance = this;
        if (cameraSource == null)
            cameraSource = Camera.main;
        _runtime = FindFirstObjectByType<SCoLRuntime>();
        _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
    }

    void OnDisable()
    {
        if (_instance == this)
            _instance = null;
        ClearAll();
    }

    void Update()
    {
        if (cameraSource == null)
            cameraSource = Camera.main;
        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_plantRenderer == null)
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        if (_fpsInteractor == null)
            _fpsInteractor = FindFirstObjectByType<FPSRaycastInteractor>();
        if (_xrToolController == null)
            _xrToolController = FindFirstObjectByType<SCoLToolController>();
        if (_xrInteractor == null)
            _xrInteractor = FindFirstObjectByType<SCoLXRInteractor>();
        if (_cam == null)
            _cam = cameraSource != null ? cameraSource : Camera.main;

        if (Time.unscaledTime >= _nextRefreshAt)
        {
            _nextRefreshAt = Time.unscaledTime + Mathf.Max(0.05f, refreshInterval);
            RefreshHighlights();
        }

        UpdateMarkers();
    }

    void RefreshHighlights()
    {
        Transform focusedRoot = null;
        if (FPSAimTargeting.TryResolve(cameraSource, maxDistance, hitMask, _runtime, _plantRenderer, out var target) &&
            target.HasActionableTarget)
        {
            focusedRoot = target.root;
        }

        CollectSceneCandidates();
        _desiredIds.Clear();
        _reservedMarkerPositions.Clear();

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
            if (state == null || state.target == null || !_desiredIds.Contains(kvp.Key))
                _pendingRemoval.Add(kvp.Key);
        }

        for (int i = 0; i < _pendingRemoval.Count; i++)
            RemoveState(_pendingRemoval[i]);
    }

    void UpdateMarkers()
    {
        if (_activeStates.Count == 0 || _cam == null)
            return;

        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * Mathf.Max(0.1f, pulseSpeed));
        foreach (var kvp in _activeStates)
        {
            var state = kvp.Value;
            if (state == null || state.target == null || state.markerRoot == null)
                continue;

            Vector3 worldPos = ResolveMarkerPosition(state);
            state.markerRoot.position = worldPos;
            state.markerRoot.rotation = Quaternion.LookRotation(_cam.transform.forward, Vector3.up);

            float markerScale = state.isFocused
                ? Mathf.Lerp(focusMinScale, focusMaxScale, pulse)
                : Mathf.Lerp(nearbyMinScale, nearbyMaxScale, pulse);
            state.markerRoot.localScale = Vector3.one * markerScale;

            ApplyMarkerVisual(state, pulse);
        }
    }

    void ApplyMarkerVisual(MarkerState state, float pulse)
    {
        Color color = nearbyMarkerColor;
        if (state.isFocused)
            color = state.isPlant ? ResolveFocusedPlantMarkerColor() : focusMarkerColor;
        float haloAlpha = state.isFocused ? Mathf.Lerp(0.28f, 0.48f, pulse) : Mathf.Lerp(0.14f, 0.24f, pulse);
        float solidAlpha = state.isFocused ? Mathf.Lerp(0.92f, 1f, pulse) : Mathf.Lerp(0.72f, 0.88f, pulse);

        if (state.halo != null)
            state.halo.localScale = Vector3.one * (state.isFocused ? Mathf.Lerp(1.45f, 1.75f, pulse) : Mathf.Lerp(1.20f, 1.38f, pulse));
        if (state.diamond != null)
            state.diamond.localScale = Vector3.one * (state.isFocused ? Mathf.Lerp(1f, 1.12f, pulse) : Mathf.Lerp(0.92f, 1f, pulse));

        SetMaterialColor(state.haloMaterial, new Color(color.r, color.g, color.b, haloAlpha));
        SetMaterialColor(state.diamondMaterial, new Color(color.r, color.g, color.b, solidAlpha));
        SetMaterialColor(state.stemMaterial, new Color(color.r, color.g, color.b, solidAlpha * 0.85f));
    }

    Color ResolveFocusedPlantMarkerColor()
    {
        if (_fpsInteractor != null)
        {
            switch (_fpsInteractor.currentTool)
            {
                case FPSRaycastInteractor.ApplyTool.Water:
                    return waterFocusMarkerColor;
                case FPSRaycastInteractor.ApplyTool.Fire:
                    return fireFocusMarkerColor;
                default:
                    return focusMarkerColor;
            }
        }

        if (_xrToolController != null)
        {
            switch (_xrToolController.currentTool)
            {
                case SCoLToolController.Tool.Water:
                    return waterFocusMarkerColor;
                case SCoLToolController.Tool.Fire:
                    return fireFocusMarkerColor;
                default:
                    return focusMarkerColor;
            }
        }

        if (_xrInteractor != null)
        {
            switch (_xrInteractor.currentTool)
            {
                case SCoLXRInteractor.Tool.Water:
                    return waterFocusMarkerColor;
                case SCoLXRInteractor.Tool.Fire:
                    return fireFocusMarkerColor;
                default:
                    return focusMarkerColor;
            }
        }

        return focusMarkerColor;
    }

    static void SetMaterialColor(Material material, Color color)
    {
        if (material == null)
            return;
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
    }

    Vector3 ResolveMarkerPosition(MarkerState state)
    {
        if (state == null || state.target == null)
            return Vector3.zero;

        if (TryGetRenderableBounds(state, out Bounds bounds))
            return new Vector3(bounds.center.x, bounds.max.y + Mathf.Max(minWorldYOffset, boundsYOffset), bounds.center.z);

        return state.target.position + Vector3.up * Mathf.Max(0.35f, minWorldYOffset);
    }

    bool TryGetRenderableBounds(MarkerState state, out Bounds bounds)
    {
        bounds = default;
        if (state == null || state.sourceRenderers == null || state.sourceRenderers.Length == 0)
            return false;

        bool found = false;
        for (int i = 0; i < state.sourceRenderers.Length; i++)
        {
            var renderer = state.sourceRenderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    void CollectSceneCandidates()
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

        var transforms = FindObjectsByType<Transform>(FindObjectsSortMode.None);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t == null || !t.name.StartsWith("Plant_"))
                continue;
            if (t.parent != null && t.parent.name.StartsWith("Plant_"))
                continue;
            AddSceneCandidate(t);
        }
    }

    void AddSceneCandidate(Transform root)
    {
        root = NormalizeMarkerRoot(root);
        if (root == null || !root.gameObject.activeInHierarchy)
            return;

        int id = root.GetInstanceID();
        if (!_sceneCandidateIds.Add(id))
            return;

        _sceneCandidates.Add(root);
    }

    void AddDesiredRoot(Transform root, bool isFocused)
    {
        root = NormalizeMarkerRoot(root);
        if (root == null)
            return;

        if (IsMarkerClusterOccupied(root, isFocused))
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
        ReserveMarkerCluster(root);
    }

    bool IsMarkerClusterOccupied(Transform root, bool isFocused)
    {
        if (isFocused)
            return false;

        if (!TryGetCandidateMarkerPosition(root, out var position))
            position = root.position;

        float radius = Mathf.Max(0f, markerClusterRadius);
        if (radius <= 0.01f)
            return false;

        float radiusSqr = radius * radius;
        for (int i = 0; i < _reservedMarkerPositions.Count; i++)
        {
            Vector3 delta = _reservedMarkerPositions[i] - position;
            delta.y = 0f;
            if (delta.sqrMagnitude <= radiusSqr)
                return true;
        }

        return false;
    }

    void ReserveMarkerCluster(Transform root)
    {
        if (root == null)
            return;

        if (!TryGetCandidateMarkerPosition(root, out var position))
            position = root.position;
        _reservedMarkerPositions.Add(position);
    }

    bool TryGetCandidateMarkerPosition(Transform root, out Vector3 position)
    {
        position = Vector3.zero;
        if (root == null)
            return false;

        if (TryGetRenderableBounds(root, out var bounds))
        {
            position = new Vector3(bounds.center.x, bounds.max.y + Mathf.Max(minWorldYOffset, boundsYOffset), bounds.center.z);
            return true;
        }

        position = root.position + Vector3.up * Mathf.Max(0.35f, minWorldYOffset);
        return true;
    }

    MarkerState CreateState(Transform root)
    {
        if (root == null)
            return null;

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
        if (renderers == null || renderers.Length == 0)
            return null;

        var haloMaterial = NewMarkerMaterial("InteractableMarker_Halo");
        var diamondMaterial = NewMarkerMaterial("InteractableMarker_Diamond");
        var stemMaterial = NewMarkerMaterial("InteractableMarker_Stem");

        var markerRoot = new GameObject(root.name + "_InteractableMarker").transform;
        markerRoot.SetParent(transform, false);

        var halo = CreateQuad("Halo", markerRoot, new Vector2(0.34f, 0.34f), new Vector3(0f, 0f, 0.02f), Vector3.zero, haloMaterial);
        var diamond = CreateQuad("Diamond", markerRoot, new Vector2(0.18f, 0.18f), Vector3.zero, new Vector3(0f, 0f, 45f), diamondMaterial);
        var stem = CreateQuad("Stem", markerRoot, new Vector2(0.035f, stemHeight), new Vector3(0f, -0.17f, 0.01f), Vector3.zero, stemMaterial);

        return new MarkerState
        {
            target = root,
            sourceRenderers = renderers,
            markerRoot = markerRoot,
            halo = halo,
            diamond = diamond,
            stem = stem,
            haloMaterial = haloMaterial,
            diamondMaterial = diamondMaterial,
            stemMaterial = stemMaterial,
            isFocused = false,
            isPlant = IsPlantRoot(root)
        };
    }

    static Transform NormalizeMarkerRoot(Transform root)
    {
        if (root == null)
            return null;

        var legacyPlant = root.GetComponentInParent<FPSSeedGrowth>();
        if (legacyPlant != null)
            return legacyPlant.transform;

        var pickup = root.GetComponentInParent<SCoLPickup>();
        if (pickup != null)
            return pickup.transform;

        var animal = root.GetComponentInParent<FPSBoidAgent>();
        if (animal != null)
            return animal.transform;

        var grabbable = root.GetComponentInParent<SCoLGrabbable>();
        if (grabbable != null)
            return grabbable.transform;

        Transform normalized = root;
        while (normalized.parent != null && normalized.parent.name.StartsWith("Plant_"))
            normalized = normalized.parent;
        return normalized;
    }

    static bool IsPlantRoot(Transform root)
    {
        if (root == null)
            return false;

        if (root.GetComponentInParent<FPSSeedGrowth>() != null)
            return true;

        return root.name.StartsWith("Plant_");
    }

    static bool TryGetRenderableBounds(Transform root, out Bounds bounds)
    {
        bounds = default;
        if (root == null)
            return false;

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: false);
        if (renderers == null || renderers.Length == 0)
            return false;

        bool found = false;
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    static Transform CreateQuad(string name, Transform parent, Vector2 size, Vector3 localPos, Vector3 localEuler, Material material)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPos;
        go.transform.localRotation = Quaternion.Euler(localEuler);
        go.transform.localScale = new Vector3(size.x, size.y, 1f);

        var collider = go.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        var renderer = go.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        return go.transform;
    }

    static Material NewMarkerMaterial(string name)
    {
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Standard");

        var material = new Material(shader) { name = name };
        material.renderQueue = 3100;
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0f);
        return material;
    }

    bool IsWithinAwareness(Transform root)
    {
        if (cameraSource == null || root == null)
            return false;

        var camPos = cameraSource.transform.position;
        float radiusSq = awarenessRadius * awarenessRadius;
        return (root.position - camPos).sqrMagnitude <= radiusSq;
    }

    void RemoveState(int id)
    {
        if (!_activeStates.TryGetValue(id, out var state))
            return;

        RestoreState(state);
        _activeStates.Remove(id);
    }

    void ClearAll()
    {
        foreach (var kvp in _activeStates)
            RestoreState(kvp.Value);

        _activeStates.Clear();
        _pendingRemoval.Clear();
        _desiredIds.Clear();
        _sceneCandidateIds.Clear();
        _sceneCandidates.Clear();
        _reservedMarkerPositions.Clear();
    }

    static void RestoreState(MarkerState state)
    {
        if (state == null)
            return;

        if (state.haloMaterial != null)
            Destroy(state.haloMaterial);
        if (state.diamondMaterial != null)
            Destroy(state.diamondMaterial);
        if (state.stemMaterial != null)
            Destroy(state.stemMaterial);
        if (state.markerRoot != null)
            Destroy(state.markerRoot.gameObject);
    }
}
