using UnityEngine;
using System.Collections.Generic;
using SCoL.Weather;
using SCoL.Visualization;
using SCoL;
using SCoL.Combat;

/// <summary>
/// T07: Seeding loop for FPS.
/// On request (RMB), consume a Seed from SCoLInventory and spawn a plant at the raycast hit point.
/// Supports either generated voxel growth or imported per-stage prefabs.
/// </summary>
public static class FPSSeeding
{
    const string HarvestableTag = "Harvestable";
    const int MaxRuntimeSeedSpawns = 120;

    static Material _voxelMat;
    static readonly Queue<GameObject> _spawnHistory = new Queue<GameObject>(MaxRuntimeSeedSpawns + 8);

    public sealed class GrowthSetup
    {
        public GameObject sproutPrefab;
        public GameObject smallPrefab;
        public GameObject mediumPrefab;
        public GameObject maturePrefab;
    }

    public static Material GetVoxelMat()
    {
        if (_voxelMat != null) return _voxelMat;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");

        _voxelMat = new Material(shader);
        _voxelMat.name = "VoxelSeedSpawn (Runtime)";
        if (_voxelMat.HasProperty("_BaseColor")) _voxelMat.SetColor("_BaseColor", new Color(0.55f, 0.9f, 0.55f, 1f));
        if (_voxelMat.HasProperty("_Color")) _voxelMat.SetColor("_Color", new Color(0.55f, 0.9f, 0.55f, 1f));
        if (_voxelMat.HasProperty("_Smoothness")) _voxelMat.SetFloat("_Smoothness", 0.02f);
        if (_voxelMat.HasProperty("_Metallic")) _voxelMat.SetFloat("_Metallic", 0.0f);
        return _voxelMat;
    }

    public static GameObject SpawnFromSeed(Vector3 position, Quaternion rotation, GrowthSetup growthSetup = null)
    {
        return SpawnSprout(position, rotation, growthSetup);
    }

    public static GameObject SpawnSprout(Vector3 position, Quaternion rotation, GrowthSetup growthSetup = null)
    {
        var root = new GameObject("VoxelSprout (Seed)");
        root.transform.SetPositionAndRotation(position, rotation);

        var growth = root.AddComponent<FPSSeedGrowth>();
        growth.growthMaterial = GetVoxelMat();
        growth.sproutToSmallSeconds = 10f;
        growth.smallToMediumSeconds = 10f;
        growth.mediumToMatureSeconds = 10f;
        growth.rainStageTimeReductionSeconds = 5f;
        if (growthSetup != null)
        {
            growth.SetStagePrefabs(
                growthSetup.sproutPrefab,
                growthSetup.smallPrefab,
                growthSetup.mediumPrefab,
                growthSetup.maturePrefab
            );
        }
        growth.RebuildNow();

        FinalizeSpawn(root);
        return root;
    }

    public static GameObject SpawnPlant(Vector3 position, Quaternion rotation)
    {
        var root = new GameObject("VoxelPlant (Seed)");
        root.transform.SetPositionAndRotation(position, rotation);

        var mat = GetVoxelMat();

        // Stem
        int stemH = Random.Range(2, 5);
        for (int i = 0; i < stemH; i++)
            SpawnCube(root.transform, new Vector3(0, 0.1f + i * 0.18f, 0), Vector3.one * 0.16f, mat);

        // Leaves
        int leaves = Random.Range(3, 6);
        for (int i = 0; i < leaves; i++)
        {
            var p = new Vector3(Random.Range(-0.22f, 0.22f), 0.1f + (stemH - 1) * 0.18f + Random.Range(-0.05f, 0.15f), Random.Range(-0.22f, 0.22f));
            var s = Vector3.one * Random.Range(0.14f, 0.18f);
            SpawnCube(root.transform, p, s, mat);
        }

        FinalizeSpawn(root);
        return root;
    }

    public static GameObject SpawnAnimal(Vector3 position, Quaternion rotation)
    {
        var root = new GameObject("VoxelAnimal (Seed)");
        root.transform.SetPositionAndRotation(position, rotation);

        var mat = GetVoxelMat();

        // Body
        SpawnCube(root.transform, new Vector3(0, 0.14f, 0), new Vector3(0.28f, 0.18f, 0.18f), mat);
        // Head
        SpawnCube(root.transform, new Vector3(0.22f, 0.18f, 0), new Vector3(0.16f, 0.14f, 0.14f), mat);
        // Legs
        SpawnCube(root.transform, new Vector3(-0.09f, 0.03f, -0.06f), new Vector3(0.06f, 0.06f, 0.06f), mat);
        SpawnCube(root.transform, new Vector3(-0.09f, 0.03f, 0.06f), new Vector3(0.06f, 0.06f, 0.06f), mat);
        SpawnCube(root.transform, new Vector3(0.09f, 0.03f, -0.06f), new Vector3(0.06f, 0.06f, 0.06f), mat);
        SpawnCube(root.transform, new Vector3(0.09f, 0.03f, 0.06f), new Vector3(0.06f, 0.06f, 0.06f), mat);

        // Boids agent (core flocking + predator/prey)
        var boid = root.AddComponent<FPSBoidAgent>();
        boid.role = (Random.value < 0.18f) ? FPSBoidAgent.BoidRole.Predator : FPSBoidAgent.BoidRole.Prey;

        // Tint predators slightly red for readability
        if (boid.role == FPSBoidAgent.BoidRole.Predator)
            TintAll(root, new Color(0.85f, 0.35f, 0.35f, 1f));

        FinalizeSpawn(root);
        return root;
    }

    static void SpawnCube(Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPos;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = localScale;

        if (cube.TryGetComponent<Renderer>(out var r))
            r.sharedMaterial = mat;

        // no collision for visuals; keep root collision decisions separate
        Object.Destroy(cube.GetComponent<Collider>());
    }

    static void TintAll(GameObject root, Color c)
    {
        var rs = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var r in rs)
        {
            if (r == null) continue;
            var m = r.sharedMaterial;
            if (m == null) continue;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }

    static void FinalizeSpawn(GameObject root)
    {
        if (root == null) return;

        TrySetHarvestableTag(root);
        EnsureRootCollider(root);
        RegisterSpawn(root);
    }

    static void TrySetHarvestableTag(GameObject go)
    {
        if (go == null) return;
        try
        {
            go.tag = HarvestableTag;
        }
        catch (UnityException)
        {
            // Tag not defined in TagManager; keep gameplay running without hard failure.
        }
    }

    static void EnsureRootCollider(GameObject root)
    {
        if (root == null) return;

        float worldRadius = 0.25f;
        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        Vector3 rootPos = root.transform.position;

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var b = r.bounds;
            float extent = b.extents.magnitude;
            float candidate = Vector3.Distance(rootPos, b.center) + extent;
            if (candidate > worldRadius) worldRadius = candidate;
        }

        float lossyMax = Mathf.Max(Mathf.Abs(root.transform.lossyScale.x), Mathf.Abs(root.transform.lossyScale.y), Mathf.Abs(root.transform.lossyScale.z));
        if (lossyMax < 0.0001f) lossyMax = 1f;

        var sc = root.GetComponent<SphereCollider>();
        if (sc == null)
            sc = root.AddComponent<SphereCollider>();
        sc.radius = Mathf.Clamp(worldRadius / lossyMax, 0.12f, 8f);
        sc.center = Vector3.zero;
    }

    static void RegisterSpawn(GameObject spawned)
    {
        if (spawned == null) return;

        _spawnHistory.Enqueue(spawned);
        while (_spawnHistory.Count > MaxRuntimeSeedSpawns)
        {
            var old = _spawnHistory.Dequeue();
            if (old != null)
                Object.Destroy(old);
        }
    }
}

public sealed class FPSSeedGrowth : MonoBehaviour
{
    [HideInInspector] public Material growthMaterial;
    [HideInInspector] public GameObject sproutPrefab;
    [HideInInspector] public GameObject smallPrefab;
    [HideInInspector] public GameObject mediumPrefab;
    [HideInInspector] public GameObject maturePrefab;

    [Header("Growth Timing")]
    [Min(0.1f)] public float sproutToSmallSeconds = 10f;
    [Min(0.1f)] public float smallToMediumSeconds = 10f;
    [Min(0.1f)] public float mediumToMatureSeconds = 10f;
    [Min(0f)] public float maxStoredWaterBoostSeconds = 12f;

    [Header("Weather Response")]
    [Min(0f)] public float rainStageTimeReductionSeconds = 5f;
    public bool pauseGrowthDuringSnow = true;

    [Header("Stomp Interaction")]
    public bool destroyWhenPlayerStepsOnTop = true;
    [Min(1)] public int stompHitsToDestroy = 3;
    [Min(0.02f)] public float stompCheckIntervalSeconds = 0.1f;
    [Min(0f)] public float stompTopTolerance = 0.15f;
    [Min(0f)] public float stompFootAllowanceAboveTop = 0.45f;
    [Min(0.05f)] public float stompHorizontalPadding = 0.20f;
    [Header("Fire Burn Visual")]
    [Min(0.03f)] public float fireBurnBlockWidth = 0.18f;
    [Min(0.08f)] public float fireBurnBlockHeight = 0.48f;
    [Min(0.1f)] public float fireBurnDisappearSeconds = 4f;

    private int _stage; // 0=sprout, 1=small, 2=medium, 3=mature
    private float _stageTimer;
    private float _waterBoostSeconds;
    private bool _burned;
    private Coroutine _destroyRoutine;

    static readonly List<FPSSeedGrowth> _allPlants = new List<FPSSeedGrowth>(256);
    static WeatherSystem _weatherSystem;
    static SeasonSkyboxController _seasonSkybox;
    static SCoLRuntime _runtime;
    static float _nextWeatherLookupAt;
    static float _nextSeasonLookupAt;
    static float _nextRuntimeLookupAt;
    static SimpleFirstPersonController _playerController;
    static CharacterController _playerCharacterController;
    static float _nextPlayerLookupAt;
    float _nextStompCheckAt;
    int _stompHitCount;

    void OnEnable()
    {
        if (!_allPlants.Contains(this))
            _allPlants.Add(this);
    }

    void OnDisable()
    {
        _allPlants.Remove(this);
    }

    void Start()
    {
        if (growthMaterial == null)
            growthMaterial = FPSSeeding.GetVoxelMat();

        var combatHealth = GetComponent<SCoLCombatHealth>();
        if (combatHealth == null)
            combatHealth = gameObject.AddComponent<SCoLCombatHealth>();
        combatHealth.Configure(SCoLCombatFaction.Plant, 50f, fillToMax: true, showBar: true, destroyWhenDead: true);

        if (transform.childCount == 0)
            RebuildNow();
    }

    public void RebuildNow()
    {
        BuildStage(_stage);
    }

    public bool IsMature => _stage >= 3;
    public bool IsBurned => _burned;

    public static int CollectNearby(Vector3 center, float radius, System.Collections.Generic.List<FPSSeedGrowth> results)
    {
        if (results == null) return 0;
        results.Clear();
        float r = Mathf.Max(0.01f, radius);
        float rSqr = r * r;

        for (int i = 0; i < _allPlants.Count; i++)
        {
            var p = _allPlants[i];
            if (p == null || !p.isActiveAndEnabled)
                continue;
            var d = p.transform.position - center;
            d.y = 0f;
            if (d.sqrMagnitude <= rSqr)
                results.Add(p);
        }

        return results.Count;
    }

    public void SetStagePrefabs(GameObject sprout, GameObject small, GameObject medium, GameObject mature)
    {
        sproutPrefab = sprout;
        smallPrefab = small;
        mediumPrefab = medium;
        maturePrefab = mature;
    }

    public void ApplyWaterBoost(float seconds)
    {
        if (seconds <= 0f || _stage >= 3 || _burned) return;
        _waterBoostSeconds = Mathf.Clamp(_waterBoostSeconds + seconds, 0f, Mathf.Max(0f, maxStoredWaterBoostSeconds));
    }

    public void ApplyFire(bool destroyOnFire, float destroyChance, float destroyDelaySeconds)
    {
        if (_burned) return;
        _burned = true;
        BuildFireBurnBlock();
        float burnLifeSeconds = Mathf.Max(0.1f, fireBurnDisappearSeconds);
        if (destroyOnFire && Random.value <= Mathf.Clamp01(destroyChance))
            burnLifeSeconds = Mathf.Min(burnLifeSeconds, Mathf.Max(0f, destroyDelaySeconds));

        if (_destroyRoutine != null)
            StopCoroutine(_destroyRoutine);
        _destroyRoutine = StartCoroutine(DestroyAfterDelay(burnLifeSeconds));
    }

    public void ResetToSprout(bool clearBurn = true)
    {
        _stage = 0;
        _stageTimer = 0f;
        _waterBoostSeconds = 0f;
        _stompHitCount = 0;
        if (clearBurn)
            _burned = false;
        BuildStage(_stage);
    }

    void Update()
    {
        if (_burned) return;

        TryHandlePlayerStomp();

        var phase = ResolveWeatherPhase();

        if (_stage >= 3) return;
        if (IsWinterSeasonActive()) return;
        if (pauseGrowthDuringSnow && phase == WeatherPhase.Snow) return;

        float delta = Time.deltaTime;

        if (_waterBoostSeconds > 0f)
        {
            // Apply accumulated water boost as immediate stage-time progress.
            delta += _waterBoostSeconds;
            _waterBoostSeconds = 0f;
        }

        _stageTimer += delta;
        while (_stage < 3 && _stageTimer >= CurrentStageDuration())
        {
            _stageTimer -= CurrentStageDuration();
            _stage++;
            BuildStage(_stage);
            if (_burned)
                BurnTintRenderers();
        }
    }

    WeatherPhase ResolveWeatherPhase()
    {
        // Keep weather lookup lightweight; refresh once per second if missing.
        if (_weatherSystem == null && Time.time >= _nextWeatherLookupAt)
        {
            _weatherSystem = FindFirstObjectByType<WeatherSystem>();
            _nextWeatherLookupAt = Time.time + 1f;
        }

        return _weatherSystem != null ? _weatherSystem.CurrentPhase : WeatherPhase.Clear;
    }

    bool IsWinterSeasonActive()
    {
        if (Time.time >= _nextWeatherLookupAt && (_weatherSystem == null || !_weatherSystem.isActiveAndEnabled))
            _weatherSystem = FindFirstObjectByType<WeatherSystem>();

        if (Time.time >= _nextSeasonLookupAt)
        {
            if (_weatherSystem != null && _weatherSystem.seasonSource != null)
                _seasonSkybox = _weatherSystem.seasonSource;
            else if (_seasonSkybox == null || !_seasonSkybox.isActiveAndEnabled)
                _seasonSkybox = FindFirstObjectByType<SeasonSkyboxController>();

            _nextSeasonLookupAt = Time.time + 1f;
        }

        if (Time.time >= _nextRuntimeLookupAt && (_runtime == null || !_runtime.isActiveAndEnabled))
        {
            _runtime = FindFirstObjectByType<SCoLRuntime>();
            _nextRuntimeLookupAt = Time.time + 1f;
        }

        if (_seasonSkybox != null)
            return _seasonSkybox.GetCurrentSeason() == SeasonSkyboxController.Season.Winter;

        return _runtime != null && _runtime.CurrentSeason == Season.Winter;
    }

    void TryHandlePlayerStomp()
    {
        if (!destroyWhenPlayerStepsOnTop)
            return;
        if (Time.time < _nextStompCheckAt)
            return;
        _nextStompCheckAt = Time.time + Mathf.Max(0.02f, stompCheckIntervalSeconds);

        if (!TryResolvePlayer(out Vector3 playerPos, out float playerFootY))
            return;
        if (!TryGetPlantBounds(out Bounds plantBounds))
            return;

        float horizontalRadius = Mathf.Max(plantBounds.extents.x, plantBounds.extents.z) + Mathf.Max(0.05f, stompHorizontalPadding);
        Vector2 plantXZ = new Vector2(plantBounds.center.x, plantBounds.center.z);
        Vector2 playerXZ = new Vector2(playerPos.x, playerPos.z);
        if ((playerXZ - plantXZ).sqrMagnitude > horizontalRadius * horizontalRadius)
            return;

        float topY = plantBounds.max.y;
        if (playerFootY < topY - Mathf.Max(0f, stompTopTolerance))
            return;
        if (playerFootY > topY + Mathf.Max(0f, stompFootAllowanceAboveTop))
            return;

        _stompHitCount = Mathf.Min(Mathf.Max(1, stompHitsToDestroy), _stompHitCount + 1);
        if (_stompHitCount < Mathf.Max(1, stompHitsToDestroy))
            return;

        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.DestroySeed);
        Destroy(gameObject);
    }

    static bool TryResolvePlayer(out Vector3 playerPos, out float playerFootY)
    {
        playerPos = Vector3.zero;
        playerFootY = 0f;

        if ((_playerController == null || !_playerController.isActiveAndEnabled) && Time.time >= _nextPlayerLookupAt)
        {
            _playerController = Object.FindFirstObjectByType<SimpleFirstPersonController>();
            _playerCharacterController = _playerController != null ? _playerController.GetComponent<CharacterController>() : null;
            _nextPlayerLookupAt = Time.time + 1f;
        }

        if (_playerController == null || !_playerController.isActiveAndEnabled)
            return false;

        playerPos = _playerController.transform.position;
        playerFootY = _playerCharacterController != null ? _playerCharacterController.bounds.min.y : playerPos.y;
        return true;
    }

    public bool TryGetPlantBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;

        var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            if (!hasBounds)
            {
                bounds = r.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        if (hasBounds)
            return true;

        var c = GetComponent<Collider>();
        if (c != null)
        {
            bounds = c.bounds;
            return true;
        }

        return false;
    }

    float CurrentStageDuration()
    {
        float duration;
        switch (_stage)
        {
            case 0: duration = Mathf.Max(0.1f, sproutToSmallSeconds); break;
            case 1: duration = Mathf.Max(0.1f, smallToMediumSeconds); break;
            default: duration = Mathf.Max(0.1f, mediumToMatureSeconds); break;
        }

        if (ResolveWeatherPhase() == WeatherPhase.Rain)
            duration = Mathf.Max(0.1f, duration - Mathf.Max(0f, rainStageTimeReductionSeconds));

        return duration;
    }

    void BuildStage(int stage)
    {
        if (growthMaterial == null)
            growthMaterial = FPSSeeding.GetVoxelMat();

        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        if (TrySpawnStagePrefab(stage))
        {
            RefreshRootCollider();
            return;
        }

        // Stem grows taller every stage.
        int stem = Mathf.Clamp(stage + 1, 1, 4);
        for (int i = 0; i < stem; i++)
            SpawnCube(new Vector3(0f, 0.08f + i * 0.16f, 0f), new Vector3(0.12f, 0.14f, 0.12f));

        // Stage-based leaves/canopy for a clear "stacking up" look.
        if (stage >= 0)
        {
            SpawnCube(new Vector3(0.10f, 0.12f, 0f), new Vector3(0.10f, 0.08f, 0.10f));
            SpawnCube(new Vector3(-0.10f, 0.16f, 0f), new Vector3(0.10f, 0.08f, 0.10f));
        }

        if (stage >= 1)
        {
            SpawnCube(new Vector3(0f, 0.24f, 0.10f), new Vector3(0.11f, 0.08f, 0.11f));
            SpawnCube(new Vector3(0f, 0.28f, -0.10f), new Vector3(0.11f, 0.08f, 0.11f));
        }

        if (stage >= 2)
        {
            SpawnCube(new Vector3(0.12f, 0.42f, 0.10f), new Vector3(0.13f, 0.09f, 0.13f));
            SpawnCube(new Vector3(-0.12f, 0.46f, 0.10f), new Vector3(0.13f, 0.09f, 0.13f));
            SpawnCube(new Vector3(0f, 0.50f, -0.12f), new Vector3(0.13f, 0.09f, 0.13f));
        }

        if (stage >= 3)
        {
            SpawnCube(new Vector3(0f, 0.62f, 0f), new Vector3(0.20f, 0.12f, 0.20f));
            SpawnCube(new Vector3(0.18f, 0.58f, 0.02f), new Vector3(0.14f, 0.10f, 0.14f));
            SpawnCube(new Vector3(-0.18f, 0.58f, -0.02f), new Vector3(0.14f, 0.10f, 0.14f));
        }

        RefreshRootCollider();
    }

    bool TrySpawnStagePrefab(int stage)
    {
        var prefab = GetStagePrefab(stage);
        if (prefab == null)
            return false;

        var spawned = Instantiate(prefab, transform);
        spawned.name = prefab.name;
        spawned.transform.localPosition = Vector3.zero;
        spawned.transform.localRotation = Quaternion.identity;
        spawned.transform.localScale = Vector3.one;
        return true;
    }

    GameObject GetStagePrefab(int stage)
    {
        switch (stage)
        {
            case 0: return sproutPrefab;
            case 1: return smallPrefab;
            case 2: return mediumPrefab;
            case 3: return maturePrefab;
            default: return null;
        }
    }

    void RefreshRootCollider()
    {
        var root = gameObject;
        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        float worldRadius = 0.25f;
        Vector3 rootPos = root.transform.position;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var b = r.bounds;
            float extent = b.extents.magnitude;
            float candidate = Vector3.Distance(rootPos, b.center) + extent;
            if (candidate > worldRadius) worldRadius = candidate;
        }

        float lossyMax = Mathf.Max(Mathf.Abs(root.transform.lossyScale.x), Mathf.Abs(root.transform.lossyScale.y), Mathf.Abs(root.transform.lossyScale.z));
        if (lossyMax < 0.0001f) lossyMax = 1f;

        var sc = root.GetComponent<SphereCollider>();
        if (sc == null) sc = root.AddComponent<SphereCollider>();
        sc.radius = Mathf.Clamp(worldRadius / lossyMax, 0.12f, 8f);
        sc.center = Vector3.zero;
    }

    void BurnTintRenderers()
    {
        var rs = GetComponentsInChildren<Renderer>(includeInactive: true);
        for (int i = 0; i < rs.Length; i++)
        {
            var r = rs[i];
            if (r == null) continue;
            var m = r.material;
            if (m == null) continue;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.black);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.black);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
        }
    }

    void BuildFireBurnBlock()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
            Destroy(transform.GetChild(i).gameObject);

        float w = Mathf.Max(0.03f, fireBurnBlockWidth);
        float h = Mathf.Max(0.08f, fireBurnBlockHeight);

        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "BurnedByFire";
        cube.transform.SetParent(transform, false);
        cube.transform.localPosition = new Vector3(0f, h * 0.5f, 0f);
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = new Vector3(w, h, w);

        var r = cube.GetComponent<Renderer>();
        if (r != null)
        {
            Material mat;
            if (growthMaterial != null)
            {
                mat = new Material(growthMaterial) { name = "BurnedByFire_Mat" };
            }
            else
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                mat = new Material(shader) { name = "BurnedByFire_Mat" };
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.black);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.black);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            r.sharedMaterial = mat;
        }

        var c = cube.GetComponent<Collider>();
        if (c != null)
            Destroy(c);

        RefreshRootCollider();
    }

    System.Collections.IEnumerator DestroyAfterDelay(float seconds)
    {
        if (seconds > 0f)
            yield return new WaitForSeconds(seconds);
        _destroyRoutine = null;
        if (gameObject != null)
            Destroy(gameObject);
    }

    void SpawnCube(Vector3 localPos, Vector3 localScale)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(transform, false);
        cube.transform.localPosition = localPos;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = localScale;

        var r = cube.GetComponent<Renderer>();
        if (r != null && growthMaterial != null)
            r.sharedMaterial = growthMaterial;

        var c = cube.GetComponent<Collider>();
        if (c != null)
            Destroy(c);
    }
}

public sealed class FPSSeedAnimalIdle : MonoBehaviour
{
    Vector3 _basePos;
    float _t;

    void Start() => _basePos = transform.position;

    void Update()
    {
        _t += Time.deltaTime;
        transform.position = _basePos + new Vector3(0, Mathf.Sin(_t * 3.5f) * 0.03f, 0);
        transform.Rotate(0f, Mathf.Sin(_t * 1.3f) * 20f * Time.deltaTime, 0f);
    }
}
