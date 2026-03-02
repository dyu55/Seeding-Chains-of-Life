using UnityEngine;
using SCoL;
using SCoL.Visualization;
using SCoL.Weather;
using SCoL.Inventory;
using SCoL.Voxels;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// FPS mouse interaction: on LMB, raycast from screen center and detect objects tagged "Harvestable"
/// within a max distance.
/// 
/// No XR dependencies.
/// </summary>
public class FPSRaycastInteractor : MonoBehaviour
{
    public enum ApplyTool
    {
        Seed,
        Water,
        Fire,
        Plant
    }

    public Camera cameraSource;
    public float maxDistance = 3f;
    public LayerMask hitMask = ~0;

    [Header("Harvest")]
    public bool destroyOnHarvest = true;
    [Tooltip("Small delay so the voxelize material swap can be seen before the object disappears.")]
    public float destroyDelaySeconds = 0.06f;

    [Header("Debug")]
    public bool logHits = true;

    [Header("Apply Tool (RMB)")]
    public ApplyTool currentTool = ApplyTool.Seed;
    [Min(1)] public int spreadLayers = 3;
    [Min(0.1f)] public float spreadLayerIntervalSeconds = 1f;
    [Min(0f)] public float lingerAfterSpreadSeconds = 0.05f;
    [Min(0.1f)] public float spreadCellSize = 1f;
    [Min(0.1f)] public float blockScale = 1f;
    [Range(0.01f, 0.5f)] public float blockThickness = 0.06f;
    [Min(0.1f)] public float surfaceProbeHeight = 10f;
    [Min(0f)] public float surfaceOffset = 0.01f;
    public Color waterSpreadColor = new Color(0.18f, 0.45f, 0.95f, 0.95f);
    public Color fireSpreadColor = new Color(0.95f, 0.18f, 0.14f, 0.95f);

    [Header("Seed Growth Models (Optional)")]
    public bool useImportedPlantStageModels = true;
    public GameObject sproutStagePrefab;
    public GameObject smallStagePrefab;
    public GameObject mediumStagePrefab;
    public GameObject matureStagePrefab;
    [Tooltip("If enabled, force final flower stage to FlowerV1/Flower0.obj.")]
    public bool forceFlower0AsFinalStage = true;

    [Header("CA Runtime Integration")]
    public bool useCARuntimeSeeding = true;

    [Header("Water/Fire vs Planted Models")]
    [Min(0f)] public float waterBoostSecondsPerTile = 3f;
    public bool fireCanDestroyPlants = false;
    [Range(0f, 1f)] public float fireDestroyChance = 0.5f;
    [Min(0f)] public float fireDestroyDelaySeconds = 0.25f;
    [Range(0.05f, 1f)] public float fireBurnRadiusScale = 0.25f;
    [Min(0.1f)] public float fireExtinguishRadius = 2.0f;

    [Header("Plant Tool / Animal Feed")]
    public bool animalsFollowWhenPlantToolSelected = true;
    [Min(0.1f)] public float plantFollowRadius = 8f;
    [Min(0f)] public float plantFollowWeight = 3f;
    public bool plantFeedConsumesInventory = true;
    [Min(0.05f)] public float feedJumpHeight = 0.35f;
    [Min(0.2f)] public float feedReactionDuration = 1.2f;
    [Min(1)] public int feedJumpCount = 3;

    [Header("Plant Destroy (RMB clicks)")]
    [Min(1)] public int plantDestroyClicksRequired = 4;
    [Min(0.1f)] public float plantDestroyClickWindowSeconds = 2.0f;

    [Header("Fire Loop Audio (optional)")]
    public AudioSource fireLoopAudioSource;
    public AudioClip fireLoopClip;
    [Range(0f, 1f)] public float fireLoopVolume = 0.65f;

    [Header("Thunder Target Fire")]
    public bool thunderCanIgniteTargetedPlant = true;
    [Range(0f, 1f)] public float thunderTargetIgniteChance = 0.12f;
    [Min(0.05f)] public float thunderTargetCheckIntervalSeconds = 0.35f;

    [Header("Primary Collect (LMB)")]
    public bool collectPickupsOnRightClick = true;
    public bool collectWaterFromRegionOnRightClick = true;
    [Min(1)] public int waterCollectAmount = 1;

    [Header("Primary Plant Pickup (LMB)")]
    [Tooltip("If enabled, LMB can uproot targeted plants (legacy + CA) and convert them into Plant inventory.")]
    public bool pickPlantsOnPrimaryClick = true;
    [Min(1)] public int plantPickupAmount = 1;

    SCoL.Inventory.SCoLInventory _inventory;
    SCoLRuntime _runtime;
    Material _waterSpreadMat;
    Material _fireSpreadMat;
    FPSSeeding.GrowthSetup _growthSetup;
    Coroutine _activeFireSpreadRoutine;
    readonly System.Collections.Generic.List<GameObject> _activeFireBlocks = new System.Collections.Generic.List<GameObject>(128);
    Vector3 _activeFireCenter;
    bool _hasActiveFire;
    WeatherSystem _weatherSystem;
    float _nextThunderTargetCheckAt;
    VoxelWorld _voxelWorld;
    struct PlantDestroyClickState
    {
        public int count;
        public float expiresAt;
    }

    readonly System.Collections.Generic.Dictionary<int, PlantDestroyClickState> _plantDestroyClicks = new System.Collections.Generic.Dictionary<int, PlantDestroyClickState>();

    void Awake()
    {
        if (cameraSource == null)
            cameraSource = Camera.main;

        EnsureFpsFeedbackSystems();
        AutoAssignFinalFlowerStagePrefab();

        _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
        if (_inventory == null)
        {
            // Create a minimal runtime inventory if the scene doesn't include one.
            var invGO = new GameObject("SCoLInventory (Runtime)");
            DontDestroyOnLoad(invGO);
            _inventory = invGO.AddComponent<SCoL.Inventory.SCoLInventory>();
        }
        _runtime = FindFirstObjectByType<SCoLRuntime>();

        _growthSetup = new FPSSeeding.GrowthSetup();
        RefreshGrowthSetup();

        if (fireLoopAudioSource != null)
        {
            fireLoopAudioSource.loop = true;
            fireLoopAudioSource.playOnAwake = false;
            fireLoopAudioSource.spatialBlend = 0f;
            fireLoopAudioSource.volume = Mathf.Clamp01(fireLoopVolume);
        }
    }

    private void EnsureFpsFeedbackSystems()
    {
        var crosshair = FindFirstObjectByType<FPSCrosshair>();
        if (crosshair == null)
        {
            crosshair = gameObject.GetComponent<FPSCrosshair>();
            if (crosshair == null)
                crosshair = gameObject.AddComponent<FPSCrosshair>();
        }
        crosshair.enabled = true;
        crosshair.showTargetInfo = true;
        if (crosshair.cameraSource == null)
            crosshair.cameraSource = cameraSource;

        var aura = FindFirstObjectByType<FPSAimAuraHighlighter>();
        if (aura == null)
        {
            aura = gameObject.GetComponent<FPSAimAuraHighlighter>();
            if (aura == null)
                aura = gameObject.AddComponent<FPSAimAuraHighlighter>();
        }
        aura.enabled = true;
        if (aura.cameraSource == null)
            aura.cameraSource = cameraSource;
    }

    private void AutoAssignFinalFlowerStagePrefab()
    {
#if UNITY_EDITOR
        if (!forceFlower0AsFinalStage)
            return;
        const string flower0Path = "Assets/Models/Modeling/_Incoming/Flowers/FlowerV1/Flower0.obj";
        var flower0 = AssetDatabase.LoadAssetAtPath<GameObject>(flower0Path);
        if (flower0 != null)
            matureStagePrefab = flower0;
#endif
    }

    void Update()
    {
        if (cameraSource == null) return;
        HandleToolSwitchInput();
        UpdatePlantAttractor();
        TryIgniteTargetedPlantDuringThunder();

        // Primary: collect/pickup/harvest
        if (SCoL.Interaction.SCoLInteractionInput.PrimaryPressed())
        {
            if (!SCoL.Interaction.SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
                return;

            if (Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            {
                if (_inventory == null)
                    _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();

                if (_inventory != null)
                {
                    // LMB priority #1: collect world pickups (seed/torch/etc).
                    if (collectPickupsOnRightClick && TryCollectPickupAtHit(hit))
                        return;

                    // LMB priority #2: collect water from region, but not when frozen.
                    if (collectWaterFromRegionOnRightClick && TryCollectWaterFromRegionAtHit(hit))
                        return;

                    // LMB priority #3: uproot plant into inventory.
                    if (pickPlantsOnPrimaryClick && TryPickupPlantAtHit(hit))
                        return;
                }

                var go = hit.collider != null ? hit.collider.gameObject : null;

                // Walk up parents to find a Harvestable root (colliders are often on child meshes).
                GameObject harvestable = null;
                for (var t = hit.collider != null ? hit.collider.transform : null; t != null; t = t.parent)
                {
                    if (t.gameObject.CompareTag("Harvestable")) { harvestable = t.gameObject; break; }
                }

                if (harvestable != null)
                {
                    if (logHits)
                        Debug.Log($"[FPSRaycastInteractor] Harvestable hit: {harvestable.name} (dist={hit.distance:0.00})", harvestable);

                    // T04: voxelize/assimilate effect
                    VoxelAssimilator.Assimilate(harvestable);

                    // T06: game feel (burst + shake)
                    FPSGameFeel.VoxelBurst(hit.point);
                    FPSGameFeel.Shake();

                    // T05: harvest -> add Voxel Seed to inventory and remove object
                    if (harvestable.GetComponent<FPSHarvestedMarker>() == null)
                    {
                        harvestable.AddComponent<FPSHarvestedMarker>();
                        _inventory.Add(SCoL.Inventory.SCoLItemType.Seed, 1);

                        if (destroyOnHarvest)
                        {
                            // Hide immediately, destroy shortly after.
                            SetRenderersEnabled(harvestable, false);
                            SetCollidersEnabled(harvestable, false);
                            StartCoroutine(DestroyLater(harvestable, destroyDelaySeconds));
                        }
                    }
                }
                else
                {
                    if (logHits)
                        Debug.Log($"[FPSRaycastInteractor] Hit non-harvestable: {(go != null ? go.name : "<null>")} (dist={hit.distance:0.00})");
                }
            }
            else
            {
                if (logHits)
                    Debug.Log("[FPSRaycastInteractor] No hit");
            }
        }

        // Secondary: apply selected tool (RMB)
        if (SCoL.Interaction.SCoLInteractionInput.SecondaryPressed())
        {
            if (!SCoL.Interaction.SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
                return;

            if (!Physics.Raycast(ray, out var hit, 50f, hitMask, QueryTriggerInteraction.Ignore))
                return;

            if (_inventory == null)
                _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
            if (_inventory == null)
                return;

            if (TryHandlePlantDestroyClick(hit))
                return;

            switch (currentTool)
            {
                case ApplyTool.Seed:
                {
                    // treat upward-facing surfaces as ground
                    if (hit.normal.y < 0.35f)
                        return;

                    if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Seed, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No seeds to plant");
                        return;
                    }

                    bool attemptedRuntime = false;
                    bool plantedByRuntime = false;
                    if (useCARuntimeSeeding)
                        plantedByRuntime = TryPlaceSeedWithRuntime(hit.point, out attemptedRuntime);

                    GameObject spawned = null;
                    if (attemptedRuntime)
                    {
                        if (!plantedByRuntime)
                        {
                            // Runtime rejected the placement (invalid tile/terrain), refund consumed seed.
                            _inventory.Add(SCoL.Inventory.SCoLItemType.Seed, 1);
                            if (logHits) Debug.Log("[FPSRaycastInteractor] Runtime seed placement rejected.");
                            return;
                        }
                    }
                    else
                    {
                        var spawnPos = hit.point + hit.normal * 0.02f;
                        var spawnRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(cameraSource.transform.forward, Vector3.up).normalized, Vector3.up);
                        RefreshGrowthSetup();
                        spawned = FPSSeeding.SpawnFromSeed(spawnPos, spawnRot, _growthSetup);
                    }

                    FPSGameFeel.VoxelBurst(hit.point, count: 14, spread: 1.0f, life: 0.8f, cubeSize: 0.055f);
                    FPSGameFeel.Shake(0.05f, 0.10f);

                    if (logHits)
                    {
                        if (attemptedRuntime)
                            Debug.Log("[FPSRaycastInteractor] Planted via SCoLRuntime CA.");
                        else if (spawned != null)
                            Debug.Log($"[FPSRaycastInteractor] Planted: {spawned.name}");
                    }
                    DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.PlantSeed);
                    break;
                }

                case ApplyTool.Water:
                {
                    // treat upward-facing surfaces as ground
                    if (hit.normal.y < 0.35f)
                        return;

                    if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Water, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No water to place");
                        return;
                    }

                    if (TryExtinguishActiveFire(hit.point))
                    {
                        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.ExtinguishFire);
                        break;
                    }

                    StartCoroutine(SpawnTransientSpread(hit.point, hit.normal, GetWaterSpreadMat(), false, true));
                    if (_runtime == null || !_runtime.isActiveAndEnabled)
                        _runtime = FindFirstObjectByType<SCoLRuntime>();
                    if (_runtime != null)
                    {
                        int n = _runtime.AddWaterAroundWorld(hit.point, radius: 1.6f, amount: 1.0f);
                        if (logHits) Debug.Log($"[FPSRaycastInteractor] Runtime water affected cells: {n}");
                    }
                    break;
                }

                case ApplyTool.Fire:
                {
                    // treat upward-facing surfaces as ground
                    if (hit.normal.y < 0.35f)
                        return;

                    if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Fire, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No fire to place");
                        return;
                    }

                    TryBurnTintTarget(hit);
                    StartFireSpread(hit.point, hit.normal);
                    if (_runtime == null || !_runtime.isActiveAndEnabled)
                        _runtime = FindFirstObjectByType<SCoLRuntime>();
                    if (_runtime != null)
                    {
                        int n = _runtime.IgniteAroundWorld(hit.point, radius: 1.35f, fuel: 1.0f);
                        if (logHits) Debug.Log($"[FPSRaycastInteractor] Runtime ignite affected cells: {n}");
                    }
                    // Avoid overlapping a long one-shot fire clip with the managed fire loop.
                    if (fireLoopAudioSource == null || fireLoopClip == null)
                        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.PlaceFire);
                    break;
                }

                case ApplyTool.Plant:
                {
                    var animal = hit.collider != null ? hit.collider.GetComponentInParent<FPSBoidAgent>() : null;
                    if (animal == null)
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] Plant feed requires targeting an animal (FPSBoidAgent).");
                        return;
                    }

                    if (plantFeedConsumesInventory && !_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Plant, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No plant items to feed");
                        return;
                    }

                    animal.FeedWithPlant(feedJumpHeight, feedReactionDuration, feedJumpCount);
                    FPSGameFeel.VoxelBurst(hit.point, count: 10, spread: 0.8f, life: 0.6f, cubeSize: 0.045f);
                    FPSGameFeel.Shake(0.04f, 0.08f);
                    if (logHits) Debug.Log($"[FPSRaycastInteractor] Fed animal: {animal.name}", animal);
                    break;
                }
            }
        }
    }

    bool TryCollectPickupAtHit(RaycastHit hit)
    {
        var pickup = hit.collider != null ? hit.collider.GetComponentInParent<SCoLPickup>() : null;
        if (pickup == null || _inventory == null)
            return false;

        _inventory.Add(pickup.type, Mathf.Max(1, pickup.amount));
        if (logHits) Debug.Log($"[FPSRaycastInteractor] Collected pickup: {pickup.type} +{Mathf.Max(1, pickup.amount)}");
        Destroy(pickup.gameObject);
        return true;
    }

    bool TryCollectWaterFromRegionAtHit(RaycastHit hit)
    {
        if (_inventory == null)
            return false;

        if (_voxelWorld == null || !_voxelWorld.isActiveAndEnabled)
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();
        if (_voxelWorld == null)
            return false;

        Vector3 p = hit.point;
        if (!_voxelWorld.IsWaterColumnAtWorld(p))
            return false;
        if (_voxelWorld.IsWinterSurfaceFrozen)
        {
            if (logHits) Debug.Log("[FPSRaycastInteractor] Water is frozen (ice), cannot collect.");
            return false;
        }

        int amount = Mathf.Max(1, waterCollectAmount);
        _inventory.Add(SCoLItemType.Water, amount);
        if (logHits) Debug.Log($"[FPSRaycastInteractor] Collected water from region: +{amount}");
        return true;
    }

    bool TryPickupPlantAtHit(RaycastHit hit)
    {
        if (_inventory == null)
            return false;
        if (hit.collider == null)
            return false;

        if (hit.collider.GetComponentInParent<SCoLPickup>() != null ||
            hit.collider.GetComponentInParent<FPSBoidAgent>() != null ||
            IsHarvestableHierarchy(hit.collider.transform))
            return false;

        var growth = hit.collider.GetComponentInParent<FPSSeedGrowth>();
        if (growth != null)
        {
            int amount = Mathf.Max(1, plantPickupAmount);
            _inventory.Add(SCoLItemType.Plant, amount);

            VoxelAssimilator.Assimilate(growth.gameObject);
            FPSGameFeel.VoxelBurst(hit.point, count: 10, spread: 0.9f, life: 0.7f, cubeSize: 0.05f);
            FPSGameFeel.Shake(0.04f, 0.08f);
            DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.DestroySeed);
            Destroy(growth.gameObject);

            if (logHits) Debug.Log($"[FPSRaycastInteractor] Picked plant (legacy) +{amount} Plant: {growth.name}", growth);
            return true;
        }

        if (!TryResolveCAPlantCellFromWorld(hit.point, out int cx, out int cy))
            return false;

        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null)
            return false;

        if (!_runtime.TryDestroyPlantAtCell(cx, cy))
            return false;

        int caAmount = Mathf.Max(1, plantPickupAmount);
        _inventory.Add(SCoLItemType.Plant, caAmount);
        FPSGameFeel.VoxelBurst(hit.point, count: 9, spread: 0.85f, life: 0.65f, cubeSize: 0.05f);
        FPSGameFeel.Shake(0.03f, 0.06f);
        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.DestroySeed);

        if (logHits) Debug.Log($"[FPSRaycastInteractor] Picked plant (CA) +{caAmount} Plant: ({cx},{cy})");
        return true;
    }

    void TryIgniteTargetedPlantDuringThunder()
    {
        if (!thunderCanIgniteTargetedPlant)
            return;
        if (Time.time < _nextThunderTargetCheckAt)
            return;
        _nextThunderTargetCheckAt = Time.time + Mathf.Max(0.05f, thunderTargetCheckIntervalSeconds);

        if (_weatherSystem == null || !_weatherSystem.isActiveAndEnabled)
            _weatherSystem = FindFirstObjectByType<WeatherSystem>();
        if (_weatherSystem == null || _weatherSystem.CurrentPhase != WeatherPhase.Thunderstorm)
            return;

        if (!SCoL.Interaction.SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
            return;
        if (!Physics.Raycast(ray, out var hit, 50f, hitMask, QueryTriggerInteraction.Ignore))
            return;

        var growth = hit.collider != null ? hit.collider.GetComponentInParent<FPSSeedGrowth>() : null;
        if (growth == null || growth.IsBurned)
            return;
        if (Random.value > Mathf.Clamp01(thunderTargetIgniteChance))
            return;

        growth.ApplyFire(destroyOnFire: false, destroyChance: 0f, destroyDelaySeconds: 0f);
    }

    bool TryHandlePlantDestroyClick(RaycastHit hit)
    {
        if (hit.collider == null) return false;

        var growth = hit.collider.GetComponentInParent<FPSSeedGrowth>();
        int key;
        string targetLabel;
        System.Action destroyAction;
        Object logContext = null;

        if (growth != null)
        {
            key = growth.GetInstanceID();
            targetLabel = growth.name;
            logContext = growth;
            destroyAction = () =>
            {
                if (growth != null)
                    Destroy(growth.gameObject);
            };
        }
        else if (hit.collider.GetComponentInParent<SCoL.Inventory.SCoLPickup>() != null ||
                 hit.collider.GetComponentInParent<FPSBoidAgent>() != null ||
                 IsHarvestableHierarchy(hit.collider.transform))
        {
            return false;
        }
        else if (TryResolveCAPlantCellFromWorld(hit.point, out int cx, out int cy))
        {
            key = HashCAPlantCellKey(cx, cy);
            targetLabel = $"CACell({cx},{cy})";
            destroyAction = () =>
            {
                if (_runtime == null)
                    _runtime = FindFirstObjectByType<SCoLRuntime>();
                if (_runtime != null)
                    _runtime.TryDestroyPlantAtCell(cx, cy);
            };
        }
        else
        {
            return false;
        }

        float now = Time.time;
        PlantDestroyClickState state;
        if (!_plantDestroyClicks.TryGetValue(key, out state))
            state = new PlantDestroyClickState { count = 0, expiresAt = 0f };

        int next = (now <= state.expiresAt) ? state.count + 1 : 1;
        _plantDestroyClicks[key] = new PlantDestroyClickState
        {
            count = next,
            expiresAt = now + Mathf.Max(0.1f, plantDestroyClickWindowSeconds)
        };

        if (next >= Mathf.Max(1, plantDestroyClicksRequired))
        {
            _plantDestroyClicks.Remove(key);
            destroyAction?.Invoke();
            DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.DestroySeed);
            if (logHits) Debug.Log($"[FPSRaycastInteractor] Plant destroyed by repeated RMB clicks: {targetLabel}", logContext);
        }
        else if (logHits)
        {
            Debug.Log($"[FPSRaycastInteractor] Plant destroy progress: {next}/{Mathf.Max(1, plantDestroyClicksRequired)} on {targetLabel}", logContext);
        }

        return true;
    }

    bool TryPlaceSeedWithRuntime(Vector3 worldPoint, out bool attemptedRuntime)
    {
        attemptedRuntime = false;
        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null || _runtime.Grid == null)
            return false;

        attemptedRuntime = true;
        if (!_runtime.TryWorldToCell(worldPoint, out int x, out int y))
            return false;

        var before = _runtime.Grid.Get(x, y);
        var beforeStage = before != null ? before.PlantStage : SCoL.PlantStage.Empty;
        bool beforePlant = before != null && before.HasPlant;
        bool beforeLineage = before != null && before.IsPlayerSeedLineage;

        _runtime.PlaceSeedAt(worldPoint);

        var after = _runtime.Grid.Get(x, y);
        if (after == null)
            return false;

        if (after.PlantStage == SCoL.PlantStage.SmallPlant && after.IsPlayerSeedLineage)
            return !beforePlant || beforeStage != after.PlantStage || !beforeLineage;

        return false;
    }

    bool TryResolveCAPlantCellFromWorld(Vector3 worldPoint, out int x, out int y)
    {
        x = y = -1;
        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null || _runtime.Grid == null)
            return false;
        if (!_runtime.TryWorldToCell(worldPoint, out x, out y))
            return false;
        var c = _runtime.Grid.Get(x, y);
        return c != null && c.HasPlant;
    }

    static bool IsHarvestableHierarchy(Transform t)
    {
        for (var p = t; p != null; p = p.parent)
        {
            if (p.gameObject.CompareTag("Harvestable"))
                return true;
        }
        return false;
    }

    static int HashCAPlantCellKey(int x, int y)
    {
        unchecked
        {
            int h = 0x4F1BBCDC;
            h = (h * 397) ^ x;
            h = (h * 397) ^ y;
            return h;
        }
    }

    void StartFireSpread(Vector3 hitPoint, Vector3 hitNormal)
    {
        ClearActiveFire();
        _activeFireCenter = SnapToVoxelCenter(hitPoint + hitNormal * 0.55f);
        _hasActiveFire = true;
        StartFireLoopAudio();
        _activeFireSpreadRoutine = StartCoroutine(SpawnTransientSpread(hitPoint, hitNormal, GetFireSpreadMat(), true, false, _activeFireBlocks, () =>
        {
            _activeFireSpreadRoutine = null;
            _hasActiveFire = false;
            _activeFireBlocks.Clear();
            StopFireLoopAudio();
        }));
    }

    bool TryExtinguishActiveFire(Vector3 atPoint)
    {
        if (!_hasActiveFire)
            return false;

        var d = new Vector2(atPoint.x - _activeFireCenter.x, atPoint.z - _activeFireCenter.z);
        if (d.magnitude > Mathf.Max(0.1f, fireExtinguishRadius))
            return false;

        ClearActiveFire();
        _hasActiveFire = false;
        if (logHits) Debug.Log("[FPSRaycastInteractor] Fire extinguished by water.");
        return true;
    }

    void ClearActiveFire()
    {
        _hasActiveFire = false;
        if (_activeFireSpreadRoutine != null)
        {
            StopCoroutine(_activeFireSpreadRoutine);
            _activeFireSpreadRoutine = null;
        }
        for (int i = 0; i < _activeFireBlocks.Count; i++)
        {
            if (_activeFireBlocks[i] != null)
                Destroy(_activeFireBlocks[i]);
        }
        _activeFireBlocks.Clear();
        StopFireLoopAudio();
        DayNightLightingController.StopInteractionSfx();
    }

    void OnDisable()
    {
        ClearActiveFire();
    }

    void StartFireLoopAudio()
    {
        if (fireLoopAudioSource == null || fireLoopClip == null)
            return;

        fireLoopAudioSource.clip = fireLoopClip;
        fireLoopAudioSource.loop = true;
        fireLoopAudioSource.volume = Mathf.Clamp01(fireLoopVolume);
        fireLoopAudioSource.spatialBlend = 0f;
        if (!fireLoopAudioSource.isPlaying)
            fireLoopAudioSource.Play();
    }

    void StopFireLoopAudio()
    {
        if (fireLoopAudioSource == null)
            return;
        if (fireLoopAudioSource.isPlaying)
            fireLoopAudioSource.Stop();
    }

    void UpdatePlantAttractor()
    {
        bool enabled = animalsFollowWhenPlantToolSelected && currentTool == ApplyTool.Plant;
        FPSBoidAgent.SetPlantAttractor(
            enabled ? cameraSource.transform : null,
            enabled,
            plantFollowRadius,
            plantFollowWeight
        );
    }

    void RefreshGrowthSetup()
    {
        if (_growthSetup == null)
            _growthSetup = new FPSSeeding.GrowthSetup();

        if (!useImportedPlantStageModels)
        {
            _growthSetup.sproutPrefab = null;
            _growthSetup.smallPrefab = null;
            _growthSetup.mediumPrefab = null;
            _growthSetup.maturePrefab = null;
            return;
        }

        _growthSetup.sproutPrefab = sproutStagePrefab;
        _growthSetup.smallPrefab = smallStagePrefab;
        _growthSetup.mediumPrefab = mediumStagePrefab;
        _growthSetup.maturePrefab = matureStagePrefab;
    }

    void HandleToolSwitchInput()
    {
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(1)) currentTool = ApplyTool.Seed;
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(2)) currentTool = ApplyTool.Water;
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(3)) currentTool = ApplyTool.Fire;
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(4)) currentTool = ApplyTool.Plant;

        if (SCoL.Interaction.SCoLInteractionInput.ToolNextPressed())
            CycleTool(+1);
        if (SCoL.Interaction.SCoLInteractionInput.ToolPrevPressed())
            CycleTool(-1);
    }

    void CycleTool(int delta)
    {
        int count = System.Enum.GetValues(typeof(ApplyTool)).Length;
        if (count <= 0)
            return;
        int idx = (int)currentTool;
        idx = (idx + delta) % count;
        if (idx < 0) idx += count;
        currentTool = (ApplyTool)idx;
    }

    System.Collections.IEnumerator SpawnTransientSpread(
        Vector3 hitPoint,
        Vector3 hitNormal,
        Material mat,
        bool burnTargets = false,
        bool waterTargets = false,
        System.Collections.Generic.List<GameObject> externalSink = null,
        System.Action onDone = null)
    {
        var spawned = externalSink ?? new System.Collections.Generic.List<GameObject>(64);
        if (externalSink != null)
            externalSink.Clear();

        Vector3 center = SnapToVoxelCenter(hitPoint + hitNormal * 0.55f);
        SpawnBlock(center, mat, spawned, burnTargets, waterTargets);

        int layers = Mathf.Max(1, spreadLayers);
        float dt = Mathf.Max(0.1f, spreadLayerIntervalSeconds);
        for (int layer = 1; layer <= layers; layer++)
        {
            yield return new WaitForSeconds(dt);
            SpawnRing(center, layer, mat, spawned, burnTargets, waterTargets);
        }

        if (lingerAfterSpreadSeconds > 0f)
            yield return new WaitForSeconds(lingerAfterSpreadSeconds);

        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null)
                Destroy(spawned[i]);
        }
        onDone?.Invoke();
    }

    void SpawnRing(Vector3 center, int ring, Material mat, System.Collections.Generic.List<GameObject> sink, bool burnTargets, bool waterTargets)
    {
        for (int z = -ring; z <= ring; z++)
        {
            for (int x = -ring; x <= ring; x++)
            {
                if (Mathf.Abs(x) != ring && Mathf.Abs(z) != ring)
                    continue;

                SpawnBlock(center + new Vector3(x * spreadCellSize, 0f, z * spreadCellSize), mat, sink, burnTargets, waterTargets);
            }
        }
    }

    void SpawnBlock(Vector3 pos, Material mat, System.Collections.Generic.List<GameObject> sink, bool burnTargets, bool waterTargets)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = currentTool == ApplyTool.Water ? "WaterTempBlock" : "FireTempBlock";
        Vector3 surfacePos = ProjectToSurface(pos);
        go.transform.position = surfacePos;
        go.transform.localScale = new Vector3(blockScale, blockThickness, blockScale);

        var r = go.GetComponent<Renderer>();
        if (r != null && mat != null)
            r.sharedMaterial = mat;

        var c = go.GetComponent<Collider>();
        if (c != null)
            Destroy(c);

        if (burnTargets)
        {
            float burnRadius = Mathf.Max(0.05f, blockScale * Mathf.Clamp(fireBurnRadiusScale, 0.05f, 1f));
            BurnTargetsAtTile(surfacePos, burnRadius);
            FireAffectSeedGrowthAtTile(surfacePos, burnRadius);
        }
        else if (waterTargets)
        {
            WaterAffectSeedGrowthAtTile(surfacePos, Mathf.Max(0.05f, blockScale * 0.55f));
        }

        sink.Add(go);
    }

    Vector3 ProjectToSurface(Vector3 p)
    {
        Vector3 origin = new Vector3(p.x, p.y + Mathf.Max(0.1f, surfaceProbeHeight), p.z);
        float dist = Mathf.Max(0.2f, surfaceProbeHeight * 2f);
        if (Physics.Raycast(origin, Vector3.down, out var hit, dist, hitMask, QueryTriggerInteraction.Ignore))
        {
            float y = hit.point.y + blockThickness * 0.5f + surfaceOffset;
            return new Vector3(p.x, y, p.z);
        }
        return p;
    }

    void TryBurnTintTarget(RaycastHit hit)
    {
        Transform target = null;
        for (Transform t = hit.collider != null ? hit.collider.transform : null; t != null; t = t.parent)
        {
            if (t.CompareTag("Harvestable"))
            {
                target = t;
                break;
            }
        }

        if (target == null && hit.collider != null)
        {
            var anyRenderer = hit.collider.GetComponentInParent<Renderer>();
            if (anyRenderer != null)
                target = anyRenderer.transform;
        }

        if (target == null)
            return;

        BurnTintRenderers(target.GetComponentsInChildren<Renderer>(includeInactive: true));
    }

    void BurnTargetsAtTile(Vector3 tileCenter, float tileRadius)
    {
        var hits = Physics.OverlapSphere(tileCenter, Mathf.Max(0.05f, tileRadius), ~0, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return;

        var seen = new System.Collections.Generic.HashSet<Transform>();
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            Transform root = null;
            for (Transform t = col.transform; t != null; t = t.parent)
            {
                if (t.CompareTag("Harvestable"))
                {
                    root = t;
                    break;
                }
            }

            if (root == null)
            {
                var anyRenderer = col.GetComponentInParent<Renderer>();
                if (anyRenderer != null)
                    root = anyRenderer.transform;
            }

            if (root == null || !seen.Add(root))
                continue;

            Vector3 p = ClosestPointOnRoot(root, tileCenter);
            Vector2 d = new Vector2(p.x - tileCenter.x, p.z - tileCenter.z);
            if (d.magnitude > tileRadius)
                continue;

            BurnTintRenderers(root.GetComponentsInChildren<Renderer>(includeInactive: true));
        }
    }

    void WaterAffectSeedGrowthAtTile(Vector3 tileCenter, float tileRadius)
    {
        if (waterBoostSecondsPerTile <= 0f)
            return;

        var roots = CollectGrowthRootsAtTile(tileCenter, tileRadius);
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] != null)
                roots[i].ApplyWaterBoost(waterBoostSecondsPerTile);
        }
    }

    void FireAffectSeedGrowthAtTile(Vector3 tileCenter, float tileRadius)
    {
        var roots = CollectGrowthRootsAtTile(tileCenter, tileRadius);
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] != null)
                roots[i].ApplyFire(fireCanDestroyPlants, fireDestroyChance, fireDestroyDelaySeconds);
        }
    }

    System.Collections.Generic.List<FPSSeedGrowth> CollectGrowthRootsAtTile(Vector3 tileCenter, float tileRadius)
    {
        var outList = new System.Collections.Generic.List<FPSSeedGrowth>(16);
        float radius = Mathf.Max(0.25f, tileRadius + 0.95f);
        FPSSeedGrowth.CollectNearby(tileCenter, radius, outList);
        return outList;
    }

    static Vector3 ClosestPointOnRoot(Transform root, Vector3 from)
    {
        if (root == null)
            return Vector3.zero;

        var cols = root.GetComponentsInChildren<Collider>(includeInactive: true);
        if (cols != null && cols.Length > 0)
        {
            bool has = false;
            Vector3 best = root.position;
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null) continue;

                // Physics.ClosestPoint only supports primitive colliders and convex MeshCollider.
                if (c is MeshCollider mc && !mc.convex)
                    continue;
                if (!(c is BoxCollider) &&
                    !(c is SphereCollider) &&
                    !(c is CapsuleCollider) &&
                    !(c is MeshCollider))
                    continue;

                Vector3 p;
                try
                {
                    p = c.ClosestPoint(from);
                }
                catch (System.Exception)
                {
                    continue;
                }

                float d = (p - from).sqrMagnitude;
                if (!has || d < bestSq)
                {
                    has = true;
                    bestSq = d;
                    best = p;
                }
            }
            if (has) return best;
        }

        return root.position;
    }

    static void BurnTintRenderers(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var m = r.material; // per-instance runtime material
            if (m == null) continue;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.black);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.black);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
        }
    }

    Vector3 SnapToVoxelCenter(Vector3 p)
    {
        return new Vector3(
            Mathf.Floor(p.x) + 0.5f,
            Mathf.Floor(p.y) + 0.5f,
            Mathf.Floor(p.z) + 0.5f
        );
    }

    Material GetWaterSpreadMat()
    {
        if (_waterSpreadMat != null) return _waterSpreadMat;
        _waterSpreadMat = NewSpreadMat("TempWaterSpreadMat", waterSpreadColor);
        return _waterSpreadMat;
    }

    Material GetFireSpreadMat()
    {
        if (_fireSpreadMat != null) return _fireSpreadMat;
        _fireSpreadMat = NewSpreadMat("TempFireSpreadMat", fireSpreadColor);
        return _fireSpreadMat;
    }

    static Material NewSpreadMat(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        return mat;
    }

    System.Collections.IEnumerator DestroyLater(GameObject go, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        if (go != null)
            Destroy(go);
    }

    static void SetRenderersEnabled(GameObject go, bool enabled)
    {
        var rs = go.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var r in rs) if (r != null) r.enabled = enabled;
    }

    static void SetCollidersEnabled(GameObject go, bool enabled)
    {
        var cs = go.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var c in cs) if (c != null) c.enabled = enabled;
    }

    sealed class FPSHarvestedMarker : MonoBehaviour { }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        // If the scene doesn't have one, create a lightweight runtime interactor.
        if (FindFirstObjectByType<FPSRaycastInteractor>() != null) return;

        var go = new GameObject("FPSRaycastInteractor (Runtime)");
        DontDestroyOnLoad(go);
        go.AddComponent<FPSRaycastInteractor>();
    }
}
