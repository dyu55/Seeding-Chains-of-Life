using UnityEngine;
using SCoL;
using SCoL.Visualization;
using SCoL.Weather;
using SCoL.Inventory;
using SCoL.Combat;
using SCoL.Voxels;
using UnityEngine.Animations;
using UnityEngine.Playables;
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
    const int MaxBurnTintRendererCount = 64;
    const string HeldWaterCanAssetPath = "Assets/Models/Modeling/_Incoming/watercan/watercan.obj";

    public enum ApplyTool
    {
        Seed,
        Water,
        Fire,
        Plant,
        Stone
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
    public ApplyTool currentTool = ApplyTool.Water;
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
    [Tooltip("Use circular rings (instead of square rings) for temporary water/fire spread VFX.")]
    public bool useCircularSpreadPattern = true;
    [Tooltip("Adds small per-tile offset for water VFX so it feels less grid-like.")]
    [Range(0f, 0.5f)] public float waterSpreadJitter = 0.16f;
    [Header("Spread Visual Style")]
    [Tooltip("Use soft ground decals (quads) for temporary water/fire spread visuals.")]
    public bool useSoftDecalSpreadVisuals = true;
    [Tooltip("When low-poly terrain visual is active, optionally hide temporary spread visuals.")]
    public bool suppressTempSpreadVisualsOnLowPoly = false;
    [Range(0f, 1f)] public float spreadVisualInnerRadius = 0.20f;
    [Range(0f, 1f)] public float spreadVisualEdgeSoftness = 0.30f;
    [Range(0.1f, 2f)] public float waterSpreadVisualScale = 1.15f;
    [Range(0.1f, 2f)] public float fireSpreadVisualScale = 1.00f;
    [Min(0f)] public float spreadVisualLift = 0.02f;
    [Header("Fire Place Models (optional)")]
    [Tooltip("If assigned, fire spread visuals will instantiate these prefabs instead of primitive blocks.")]
    public GameObject[] firePlacePrefabs;
    [Range(0.1f, 3f)] public float firePlacePrefabScale = 0.9f;
    [Range(1f, 20f)] public float firePlacePrefabSizeMultiplier = 10f;
    [Tooltip("If enabled, spawned fire models auto-play imported animation clips.")]
    public bool autoPlayFireModelAnimation = true;
    [Tooltip("Optional explicit fire animation clips. If empty, clips are auto-loaded from GroundFireV1/V2 in editor.")]
    public AnimationClip[] firePlaceAnimationClips;
    [Tooltip("Optional clips specifically for GroundFireV1.")]
    public AnimationClip[] firePlaceV1AnimationClips;
    [Tooltip("Optional clips specifically for GroundFireV2.")]
    public AnimationClip[] firePlaceV2AnimationClips;

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
    [Min(0f)] public float waterBoostSecondsPerTile = 5f;
    public bool fireCanDestroyPlants = false;
    [Range(0f, 1f)] public float fireDestroyChance = 0.5f;
    [Min(0f)] public float fireDestroyDelaySeconds = 0.25f;
    [Min(0f)] public float fireBurnToBlackDelaySeconds = 2.5f;
    [Range(0.05f, 1f)] public float fireBurnRadiusScale = 0.25f;
    [Min(0.1f)] public float fireExtinguishRadius = 2.0f;

    [Header("Plant Tool / Animal Feed")]
    public bool animalsFollowWhenPlantToolSelected = true;
    [Min(0.1f)] public float plantFollowRadius = 8f;
    [Min(0f)] public float plantFollowWeight = 3f;
    [Min(0f)] public float plantFollowFrontOffset = 1.4f;
    [Min(0.1f)] public float plantFollowStopDistance = 1.1f;
    public bool plantFeedConsumesInventory = true;
    [Min(0.05f)] public float feedJumpHeight = 0.35f;
    [Min(0.2f)] public float feedReactionDuration = 1.2f;
    [Min(1)] public int feedJumpCount = 3;

    [Header("Stone Throw (RMB)")]
    [Min(0.1f)] public float stoneThrowSpeed = 24f;
    [Min(0f)] public float stoneThrowUpwardBias = 0.08f;
    [Min(0.05f)] public float stoneSpawnForwardOffset = 0.55f;
    [Min(0f)] public float stoneSpawnVerticalOffset = 0.1f;
    [Min(0.05f)] public float stoneProjectileScale = 0.32f;
    [Min(0.1f)] public float stoneProjectileLifetime = 6f;
    [Min(0f)] public float stoneDamage = 10f;
    public GameObject[] stoneProjectilePrefabs;

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

    [Header("Held Water Can")]
    public bool showHeldWaterCan = true;
    public GameObject heldWaterCanPrefab;
    public Vector3 heldWaterCanLocalPosition = new Vector3(0.32f, -0.28f, 0.62f);
    public Vector3 heldWaterCanLocalEuler = new Vector3(12f, -24f, -12f);
    [Min(0.05f)] public float heldWaterCanScale = 0.42f;
    public Color heldWaterCanTint = new Color(0.24f, 0.68f, 0.96f, 1f);
    [Range(0f, 2f)] public float heldWaterCanEmission = 0.18f;

    [Header("Primary Plant Pickup (LMB)")]
    [Tooltip("If enabled, LMB can uproot targeted plants (legacy + CA) and convert them into Plant inventory.")]
    public bool pickPlantsOnPrimaryClick = true;
    [Min(1)] public int plantPickupAmount = 1;

    SCoL.Inventory.SCoLInventory _inventory;
    SCoLRuntime _runtime;
    PlantVoxelRenderer _plantRenderer;
    Material _waterSpreadMat;
    Material _fireSpreadMat;
    Material _stoneTrailMat;
    Texture2D _waterSpreadStampTex;
    Texture2D _fireSpreadStampTex;
    FPSSeeding.GrowthSetup _growthSetup;
    Coroutine _activeFireSpreadRoutine;
    readonly System.Collections.Generic.List<GameObject> _activeFireBlocks = new System.Collections.Generic.List<GameObject>(128);
    Vector3 _activeFireCenter;
    bool _hasActiveFire;
    WeatherSystem _weatherSystem;
    SeasonSkyboxController _seasonSkybox;
    float _nextSeasonLookupAt;
    float _nextThunderTargetCheckAt;
    VoxelWorld _voxelWorld;
    SCoLCombatHealth _playerCombatHealth;
    SpawnPickups _spawnPickups;
    GameObject _heldWaterCanInstance;
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

        currentTool = ApplyTool.Water;

        EnsureFpsFeedbackSystems();
        AutoAssignFinalFlowerStagePrefab();
        AutoAssignFirePlacePrefabs();
        AutoAssignHeldWaterCanPrefab();

        _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
        if (_inventory == null)
        {
            // Create a minimal runtime inventory if the scene doesn't include one.
            var invGO = new GameObject("SCoLInventory (Runtime)");
            DontDestroyOnLoad(invGO);
            _inventory = invGO.AddComponent<SCoL.Inventory.SCoLInventory>();
        }
        _runtime = FindFirstObjectByType<SCoLRuntime>();
        _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        EnsurePlayerCombatHealth();
        EnsureStoneProjectilePrefabs();

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
        var hud = FindFirstObjectByType<SCoL.Visualization.SCoLUIToolkitHUD>();
        if (hud == null)
        {
            hud = gameObject.GetComponent<SCoL.Visualization.SCoLUIToolkitHUD>();
            if (hud == null)
                hud = gameObject.AddComponent<SCoL.Visualization.SCoLUIToolkitHUD>();
        }
        hud.enabled = true;
        if (hud.cameraSource == null)
            hud.cameraSource = cameraSource;

        var oldCrosshair = FindFirstObjectByType<FPSCrosshair>();
        if (oldCrosshair != null)
            oldCrosshair.enabled = false;

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

    private void AutoAssignHeldWaterCanPrefab()
    {
#if UNITY_EDITOR
        if (heldWaterCanPrefab != null)
            return;
        heldWaterCanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeldWaterCanAssetPath);
#endif
    }

    private void AutoAssignFirePlacePrefabs()
    {
#if UNITY_EDITOR
        if (firePlacePrefabs != null && firePlacePrefabs.Length > 0)
        {
            for (int i = 0; i < firePlacePrefabs.Length; i++)
                if (firePlacePrefabs[i] != null)
                    return;
        }

        var a = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/Fire/GroundFireV1.fbx");
        var b = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/Fire/GroundFireV2.fbx");
        if (a == null && b == null)
            return;
        if (a != null && b != null)
            firePlacePrefabs = new[] { a, b };
        else
            firePlacePrefabs = new[] { a != null ? a : b };

        if (firePlaceAnimationClips == null || firePlaceAnimationClips.Length == 0)
        {
            var clips = new System.Collections.Generic.List<AnimationClip>(4);
            AppendAnimationClipsFromAsset(clips, "Assets/Models/Modeling/_Incoming/Fire/GroundFireV1.fbx");
            AppendAnimationClipsFromAsset(clips, "Assets/Models/Modeling/_Incoming/Fire/GroundFireV2.fbx");
            firePlaceAnimationClips = clips.ToArray();
        }

        if (firePlaceV1AnimationClips == null || firePlaceV1AnimationClips.Length == 0)
        {
            var clips = new System.Collections.Generic.List<AnimationClip>(2);
            AppendAnimationClipsFromAsset(clips, "Assets/Models/Modeling/_Incoming/Fire/GroundFireV1.fbx");
            firePlaceV1AnimationClips = clips.ToArray();
        }

        if (firePlaceV2AnimationClips == null || firePlaceV2AnimationClips.Length == 0)
        {
            var clips = new System.Collections.Generic.List<AnimationClip>(2);
            AppendAnimationClipsFromAsset(clips, "Assets/Models/Modeling/_Incoming/Fire/GroundFireV2.fbx");
            firePlaceV2AnimationClips = clips.ToArray();
        }
#endif
    }

#if UNITY_EDITOR
    private static void AppendAnimationClipsFromAsset(System.Collections.Generic.List<AnimationClip> outClips, string path)
    {
        if (outClips == null || string.IsNullOrEmpty(path))
            return;
        var objs = AssetDatabase.LoadAllAssetRepresentationsAtPath(path);
        if (objs == null || objs.Length == 0)
            return;
        for (int i = 0; i < objs.Length; i++)
        {
            if (!(objs[i] is AnimationClip clip) || clip == null)
                continue;
            if (clip.name != null && clip.name.StartsWith("__preview__", System.StringComparison.OrdinalIgnoreCase))
                continue;
            if (!outClips.Contains(clip))
                outClips.Add(clip);
        }
    }
#endif

    void Update()
    {
        if (cameraSource == null) return;
        HandleToolSwitchInput();
        UpdateHeldToolVisual();
        UpdatePlantAttractor();
        TryIgniteTargetedPlantDuringThunder();

        // Primary: non-destructive interaction only
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

            if (_inventory == null)
                _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
            if (_inventory == null)
                return;

            if (currentTool == ApplyTool.Stone)
            {
                ThrowStone(ray);
                return;
            }

            if (!Physics.Raycast(ray, out var hit, 50f, hitMask, QueryTriggerInteraction.Ignore))
                return;

            // Allow collecting pickups with RMB too (useful for laptop/trackpad workflows).
            if (collectPickupsOnRightClick && TryCollectPickupAtHit(hit))
                return;

            if (currentTool == ApplyTool.Water && collectWaterFromRegionOnRightClick && TryCollectWaterFromRegionAtHit(hit))
                return;

            // Water and fire tools should apply to hovered plants directly instead of being hijacked
            // by the generic repeated-RMB plant destroy flow.
            if (currentTool != ApplyTool.Water &&
                currentTool != ApplyTool.Fire &&
                TryHandlePlantDestroyClick(hit))
                return;

            switch (currentTool)
            {
                case ApplyTool.Seed:
                {
                    if (IsWinterSeasonActive())
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] Planting disabled during Winter.");
                        return;
                    }
                    Vector3 plantWorldPoint = hit.point;
                    bool hasProjectedPlacement = TryResolveSeedPlacementPoint(hit, out plantWorldPoint);
                    if (!hasProjectedPlacement && hit.normal.y < 0.35f)
                        return;

                    int selectedSeedVariant = GetSelectedSeedVariantIndex();
                    if (!_inventory.TryConsumeSeedType(selectedSeedVariant, 1))
                    {
                        if (logHits) Debug.Log($"[FPSRaycastInteractor] No selected seed to plant: {_inventory.GetSeedTypeDisplayName(selectedSeedVariant)}");
                        return;
                    }

                    bool attemptedRuntime = false;
                    bool plantedByRuntime = false;
                    if (useCARuntimeSeeding)
                        plantedByRuntime = TryPlaceSeedWithRuntime(plantWorldPoint, out attemptedRuntime);

                    GameObject spawned = null;
                    if (attemptedRuntime)
                    {
                        if (!plantedByRuntime)
                        {
                            // Runtime rejected the placement (invalid tile/terrain), refund consumed seed.
                            _inventory.AddSeedType(selectedSeedVariant, 1);
                            if (logHits) Debug.Log("[FPSRaycastInteractor] Runtime seed placement rejected.");
                            return;
                        }
                    }
                    else
                    {
                        var spawnPos = plantWorldPoint + (hasProjectedPlacement ? Vector3.up * 0.02f : hit.normal * 0.02f);
                        var spawnRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(cameraSource.transform.forward, Vector3.up).normalized, Vector3.up);
                        RefreshGrowthSetup();
                        spawned = FPSSeeding.SpawnFromSeed(spawnPos, spawnRot, _growthSetup);
                    }

                    FPSGameFeel.VoxelBurst(plantWorldPoint, count: 14, spread: 1.0f, life: 0.8f, cubeSize: 0.055f);
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
                    bool targetingPlant = TryResolveWaterApplicationTarget(hit, out Vector3 waterPoint, out Vector3 waterNormal);

                    // Non-plant targets still require an upward-facing ground surface.
                    if (!targetingPlant && hit.normal.y < 0.35f)
                        return;

                    if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Water, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No water to place");
                        return;
                    }

                    if (TryExtinguishActiveFire(waterPoint))
                    {
                        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.ExtinguishFire);
                        break;
                    }

                    StartCoroutine(SpawnTransientSpread(waterPoint, waterNormal, GetWaterSpreadMat(), false, true));
                    if (_runtime == null || !_runtime.isActiveAndEnabled)
                        _runtime = FindFirstObjectByType<SCoLRuntime>();
                    if (_runtime != null)
                    {
                        int n = _runtime.AddWaterAroundWorld(waterPoint, radius: 1.6f, amount: 1.0f);
                        if (logHits) Debug.Log($"[FPSRaycastInteractor] Runtime water affected cells: {n}");
                    }
                    break;
                }

                case ApplyTool.Fire:
                {
                    bool targetingPlant =
                        (hit.collider != null && hit.collider.GetComponentInParent<FPSSeedGrowth>() != null) ||
                        (hit.collider != null && IsHarvestableHierarchy(hit.collider.transform)) ||
                        TryResolveCAPlantCellFromWorld(hit.point, out _, out _);

                    // Allow fire on plants even if surface normal is not upward.
                    if (!targetingPlant && hit.normal.y < 0.35f)
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
                        // Let fire visuals appear first, then scorch target plant into burnt black square.
                        StartCoroutine(ScorchAfterDelay(hit.point, Mathf.Max(0f, fireBurnToBlackDelaySeconds)));
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
                    Vector3 feedPoint = hit.point;
                    if (animal == null)
                        FPSAimTargeting.TryResolveAnimalNearAim(cameraSource, maxDistance, hitMask, out animal, out feedPoint);
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
                    FPSGameFeel.VoxelBurst(feedPoint, count: 10, spread: 0.8f, life: 0.6f, cubeSize: 0.045f);
                    FPSGameFeel.Shake(0.04f, 0.08f);
                    if (logHits) Debug.Log($"[FPSRaycastInteractor] Fed animal: {animal.name}", animal);
                    break;
                }

                case ApplyTool.Stone:
                    break;
            }
        }
    }

    bool TryCollectPickupAtHit(RaycastHit hit)
    {
        var pickup = hit.collider != null ? hit.collider.GetComponentInParent<SCoLPickup>() : null;
        if (pickup == null || _inventory == null)
            return false;

        int amount = Mathf.Max(1, pickup.amount);
        if (pickup.type == SCoLItemType.Seed && pickup.seedVariantIndex >= 0)
            _inventory.AddSeedType(pickup.seedVariantIndex, amount);
        else
            _inventory.Add(pickup.type, amount);

        if (pickup.type == SCoLItemType.Seed && pickup.seedVariantIndex >= 0)
        {
            if (_plantRenderer == null)
                _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
            if (_plantRenderer != null)
                _plantRenderer.SetSelectedFlowerVariantIndex(pickup.seedVariantIndex);
        }
        if (logHits)
        {
            if (pickup.type == SCoLItemType.Seed && pickup.seedVariantIndex >= 0)
                Debug.Log($"[FPSRaycastInteractor] Collected pickup: Seed {_inventory.GetSeedTypeDisplayName(pickup.seedVariantIndex)} +{amount}");
            else
                Debug.Log($"[FPSRaycastInteractor] Collected pickup: {pickup.type} +{amount}");
        }
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

        if (Random.value > Mathf.Clamp01(thunderTargetIgniteChance))
            return;

        if (_runtime == null || !_runtime.isActiveAndEnabled)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null)
            return;

        int scorched = _runtime.ScorchRandomPlants(minCount: 1, maxCount: 5, autoClearSeconds: 5f);
        if (logHits && scorched > 0)
            Debug.Log($"[FPSRaycastInteractor] Thunder scorched random flowers: {scorched}");
    }

    bool IsWinterSeasonActive()
    {
        if (Time.time >= _nextSeasonLookupAt)
        {
            if (_weatherSystem == null || !_weatherSystem.isActiveAndEnabled)
                _weatherSystem = FindFirstObjectByType<WeatherSystem>();

            if (_weatherSystem != null && _weatherSystem.seasonSource != null)
                _seasonSkybox = _weatherSystem.seasonSource;
            else if (_seasonSkybox == null || !_seasonSkybox.isActiveAndEnabled)
                _seasonSkybox = FindFirstObjectByType<SeasonSkyboxController>();

            if (_runtime == null || !_runtime.isActiveAndEnabled)
                _runtime = FindFirstObjectByType<SCoLRuntime>();

            _nextSeasonLookupAt = Time.time + 1f;
        }

        if (_seasonSkybox != null)
            return _seasonSkybox.GetCurrentSeason() == SeasonSkyboxController.Season.Winter;

        return _runtime != null && _runtime.CurrentSeason == Season.Winter;
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

        if (_plantRenderer == null)
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        int variantIndex = _plantRenderer != null ? _plantRenderer.GetSelectedFlowerVariantIndex() : -1;
        _runtime.PlaceSeedAt(worldPoint, variantIndex);

        var after = _runtime.Grid.Get(x, y);
        if (after == null)
            return false;

        if (after.PlantStage == SCoL.PlantStage.SmallPlant && after.IsPlayerSeedLineage)
            return !beforePlant || beforeStage != after.PlantStage || !beforeLineage;

        return false;
    }

    bool TryResolveSeedPlacementPoint(RaycastHit hit, out Vector3 plantWorldPoint)
    {
        plantWorldPoint = hit.point;

        if (_voxelWorld == null || !_voxelWorld.isActiveAndEnabled)
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();
        if (_voxelWorld == null)
            return hit.normal.y >= 0.35f;

        Vector3 sample = hit.point;
        sample.y = Mathf.Max(sample.y, _voxelWorld.OriginWorld.y + _voxelWorld.Config.seaLevel + 1f);

        if (_voxelWorld.TryGetTerrainSurfaceYAtWorld(sample, out float surfaceY, includeWaterSurface: false) &&
            _voxelWorld.TryWorldToColumn(sample, out int x, out int z) &&
            _voxelWorld.IsGrassSurface(x, z))
        {
            plantWorldPoint = new Vector3(sample.x, surfaceY + 0.02f, sample.z);
            return true;
        }

        return hit.normal.y >= 0.35f;
    }

    bool TryResolveWaterApplicationTarget(RaycastHit hit, out Vector3 waterPoint, out Vector3 waterNormal)
    {
        waterPoint = hit.point;
        waterNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;

        if (hit.collider == null)
            return false;

        var legacyPlant = hit.collider.GetComponentInParent<FPSSeedGrowth>();
        if (legacyPlant != null)
        {
            waterPoint = ProjectToSurface(legacyPlant.transform.position, out waterNormal);
            waterNormal = Vector3.up;
            return true;
        }

        if (TryResolveCAPlantCellFromWorld(hit.point, out int cx, out int cy))
        {
            if (_runtime == null)
                _runtime = FindFirstObjectByType<SCoLRuntime>();

            if (_runtime != null && _runtime.Grid != null)
            {
                var cellCenter = _runtime.Grid.CellCenterWorld(cx, cy);
                waterPoint = ProjectToSurface(cellCenter, out waterNormal);
            }
            else
            {
                waterPoint = ProjectToSurface(hit.point, out waterNormal);
            }

            waterNormal = Vector3.up;
            return true;
        }

        waterPoint = hit.point;
        waterNormal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
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
        SetHeldWaterCanVisible(false);
    }

    void OnDestroy()
    {
        if (_heldWaterCanInstance != null)
            Destroy(_heldWaterCanInstance);
        if (_waterSpreadMat != null) Destroy(_waterSpreadMat);
        if (_fireSpreadMat != null) Destroy(_fireSpreadMat);
        if (_waterSpreadStampTex != null) Destroy(_waterSpreadStampTex);
        if (_fireSpreadStampTex != null) Destroy(_fireSpreadStampTex);
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
            plantFollowWeight,
            plantFollowStopDistance,
            plantFollowFrontOffset
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
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(1))
        {
            // Key 1 enters Seed tool and cycles owned seed types.
            currentTool = ApplyTool.Seed;
            TryCycleOwnedSeedVariant();
        }
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(2)) currentTool = ApplyTool.Water;
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(3)) currentTool = ApplyTool.Fire;
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(4)) currentTool = ApplyTool.Plant;
        if (SCoL.Interaction.SCoLInteractionInput.ToolSlotPressed(5)) currentTool = ApplyTool.Stone;

        if (SCoL.Interaction.SCoLInteractionInput.ToolNextPressed())
            CycleTool(+1);
        if (SCoL.Interaction.SCoLInteractionInput.ToolPrevPressed())
            CycleTool(-1);
    }

    void UpdateHeldToolVisual()
    {
        bool shouldShow = showHeldWaterCan && currentTool == ApplyTool.Water && cameraSource != null;
        if (!shouldShow)
        {
            SetHeldWaterCanVisible(false);
            return;
        }

        if (!EnsureHeldWaterCanInstance())
            return;

        var t = _heldWaterCanInstance.transform;
        if (t.parent != cameraSource.transform)
            t.SetParent(cameraSource.transform, false);

        t.localPosition = heldWaterCanLocalPosition;
        t.localRotation = Quaternion.Euler(heldWaterCanLocalEuler);
        t.localScale = Vector3.one * Mathf.Max(0.05f, heldWaterCanScale);
        SetHeldWaterCanVisible(true);
    }

    bool EnsureHeldWaterCanInstance()
    {
        if (_heldWaterCanInstance != null)
            return true;

        GameObject prefab = ResolveHeldWaterCanPrefab();
        if (prefab == null || cameraSource == null)
            return false;

        _heldWaterCanInstance = Instantiate(prefab, cameraSource.transform, false);
        _heldWaterCanInstance.name = "HeldWaterCan";
        SetLayerRecursive(_heldWaterCanInstance, 2);
        StripHeldToolComponents(_heldWaterCanInstance);
        TintHeldWaterCan(_heldWaterCanInstance);
        return true;
    }

    GameObject ResolveHeldWaterCanPrefab()
    {
        if (heldWaterCanPrefab != null)
            return heldWaterCanPrefab;

        if (_spawnPickups == null || !_spawnPickups.isActiveAndEnabled)
            _spawnPickups = FindFirstObjectByType<SpawnPickups>();
        if (_spawnPickups != null)
        {
            if (_spawnPickups.waterPickupPrefabs != null)
            {
                for (int i = 0; i < _spawnPickups.waterPickupPrefabs.Length; i++)
                {
                    if (_spawnPickups.waterPickupPrefabs[i] != null)
                    {
                        heldWaterCanPrefab = _spawnPickups.waterPickupPrefabs[i];
                        return heldWaterCanPrefab;
                    }
                }
            }

            if (_spawnPickups.waterPickupPrefab != null)
            {
                heldWaterCanPrefab = _spawnPickups.waterPickupPrefab;
                return heldWaterCanPrefab;
            }
        }

#if UNITY_EDITOR
        heldWaterCanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HeldWaterCanAssetPath);
#endif
        return heldWaterCanPrefab;
    }

    void StripHeldToolComponents(GameObject go)
    {
        if (go == null)
            return;

        var pickups = go.GetComponentsInChildren<SCoLPickup>(true);
        for (int i = 0; i < pickups.Length; i++)
        {
            if (pickups[i] != null)
                Destroy(pickups[i]);
        }

        var bodies = go.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            if (bodies[i] != null)
                Destroy(bodies[i]);
        }

        var colliders = go.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }

        var lights = go.GetComponentsInChildren<Light>(true);
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null)
                lights[i].enabled = false;
        }
    }

    void TintHeldWaterCan(GameObject go)
    {
        if (go == null)
            return;

        var renderers = go.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var mats = renderer.materials;
            bool changed = false;
            for (int j = 0; j < mats.Length; j++)
            {
                var mat = mats[j];
                if (mat == null)
                    continue;

                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", heldWaterCanTint);
                    changed = true;
                }
                if (mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", heldWaterCanTint);
                    changed = true;
                }
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", heldWaterCanTint * Mathf.Max(0f, heldWaterCanEmission));
                    changed = true;
                }
            }

            if (changed)
                renderer.materials = mats;
        }
    }

    void SetHeldWaterCanVisible(bool visible)
    {
        if (_heldWaterCanInstance != null && _heldWaterCanInstance.activeSelf != visible)
            _heldWaterCanInstance.SetActive(visible);
    }

    bool TryCycleSeedFlowerVariant()
    {
        if (_plantRenderer == null)
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        if (_plantRenderer == null)
            return false;

        bool cycled = _plantRenderer.CycleSelectedFlower(+1);
        if (!cycled)
            return false;

        _runtime?.ForceRender();

        if (logHits)
            Debug.Log($"[FPSRaycastInteractor] Seed flower switched: {GetSelectedFlowerName()}");
        return true;
    }

    bool TryCycleOwnedSeedVariant()
    {
        if (_inventory == null)
            _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
        if (_plantRenderer == null)
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        if (_inventory == null || _plantRenderer == null)
            return TryCycleSeedFlowerVariant();

        int current = GetSelectedSeedVariantIndex();
        int variantCount = 4;
        for (int step = 1; step <= variantCount; step++)
        {
            int candidate = (current + step) % variantCount;
            if (_inventory.GetSeedTypeCount(candidate) > 0)
            {
                _plantRenderer.SetSelectedFlowerVariantIndex(candidate);
                _runtime?.ForceRender();
                if (logHits) Debug.Log($"[FPSRaycastInteractor] Seed type switched: {_inventory.GetSeedTypeDisplayName(candidate)}");
                return true;
            }
        }

        // No typed seeds collected yet, fallback to previous behavior.
        return TryCycleSeedFlowerVariant();
    }

    public string GetSelectedFlowerName()
    {
        if (_plantRenderer == null)
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        if (_plantRenderer == null)
            return "Default Flower";
        return _plantRenderer.GetSelectedFlowerName();
    }

    public int GetSelectedSeedVariantIndex()
    {
        if (_plantRenderer == null)
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        if (_plantRenderer == null)
            return 0;
        return _plantRenderer.GetSelectedFlowerVariantIndex();
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

    void EnsurePlayerCombatHealth()
    {
        if (_playerCombatHealth != null)
            return;

        GameObject target = null;
        var controller = FindFirstObjectByType<SimpleFirstPersonController>();
        if (controller != null)
            target = controller.gameObject;
        else if (cameraSource != null)
            target = cameraSource.transform.root.gameObject;

        if (target == null)
            return;

        var playerHealth = target.GetComponent<SCoLPlayerHealth>();
        if (playerHealth == null)
            playerHealth = target.AddComponent<SCoLPlayerHealth>();
        playerHealth.SetMaxHealth(100f, fillToMax: true);

        _playerCombatHealth = target.GetComponent<SCoLCombatHealth>();
        if (_playerCombatHealth == null)
            _playerCombatHealth = target.AddComponent<SCoLCombatHealth>();
        _playerCombatHealth.Configure(SCoLCombatFaction.Player, 100f, fillToMax: true, showBar: false, destroyWhenDead: false);
        _playerCombatHealth.DamageInvulnerabilitySeconds = 0.85f;
    }

    void EnsureStoneProjectilePrefabs()
    {
        if (stoneProjectilePrefabs != null)
        {
            for (int i = 0; i < stoneProjectilePrefabs.Length; i++)
            {
                if (stoneProjectilePrefabs[i] != null)
                    return;
            }
        }

        stoneProjectilePrefabs = new[]
        {
            Resources.Load<GameObject>("StylizedNature/FBX/Pebble_Round_1"),
            Resources.Load<GameObject>("StylizedNature/FBX/Pebble_Round_2"),
            Resources.Load<GameObject>("StylizedNature/FBX/Pebble_Round_3")
        };
    }

    void ThrowStone(Ray aimRay)
    {
        if (_inventory == null && (_inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>()) == null)
            return;

        if (!_inventory.TryConsume(SCoLItemType.Stone, 1))
        {
            if (logHits) Debug.Log("[FPSRaycastInteractor] No stones to throw.");
            return;
        }

        EnsureStoneProjectilePrefabs();
        GameObject prefab = null;
        if (stoneProjectilePrefabs != null && stoneProjectilePrefabs.Length > 0)
            prefab = stoneProjectilePrefabs[Random.Range(0, stoneProjectilePrefabs.Length)];

        Vector3 forward = aimRay.direction.sqrMagnitude > 0.0001f ? aimRay.direction.normalized : cameraSource.transform.forward;
        forward = (forward + Vector3.up * Mathf.Max(0f, stoneThrowUpwardBias)).normalized;
        Vector3 spawnPos = aimRay.origin + cameraSource.transform.forward * Mathf.Max(0.05f, stoneSpawnForwardOffset) + Vector3.up * stoneSpawnVerticalOffset;

        GameObject go = prefab != null
            ? Instantiate(prefab, spawnPos, Quaternion.LookRotation(forward, Vector3.up))
            : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "StoneProjectile";
        go.transform.localScale = Vector3.one * Mathf.Max(0.05f, stoneProjectileScale);

        SetLayerRecursive(go, 0);
        EnsureStoneCollider(go);

        var rb = go.GetComponent<Rigidbody>();
        if (rb == null)
            rb = go.AddComponent<Rigidbody>();
        rb.useGravity = true;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.mass = 0.18f;
        rb.linearVelocity = forward * Mathf.Max(0.1f, stoneThrowSpeed);

        var trail = go.GetComponent<TrailRenderer>();
        if (trail == null)
            trail = go.AddComponent<TrailRenderer>();
        trail.time = 0.22f;
        trail.startWidth = 0.08f;
        trail.endWidth = 0.02f;
        trail.minVertexDistance = 0.03f;
        trail.material = GetStoneTrailMaterial();
        trail.startColor = new Color(0.92f, 0.92f, 0.98f, 0.90f);
        trail.endColor = new Color(0.92f, 0.92f, 0.98f, 0.02f);

        var projectile = go.GetComponent<FPSStoneProjectile>();
        if (projectile == null)
            projectile = go.AddComponent<FPSStoneProjectile>();
        projectile.lifetimeSeconds = stoneProjectileLifetime;
        projectile.damage = stoneDamage;

        IgnorePlayerCollisions(go);

        FPSGameFeel.Shake(0.02f, 0.04f);
    }

    void EnsureStoneCollider(GameObject go)
    {
        if (go == null)
            return;

        var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
        if (colliders == null || colliders.Length == 0)
        {
            var sphere = go.AddComponent<SphereCollider>();
            sphere.radius = 0.5f;
            return;
        }

        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] == null)
                continue;
            colliders[i].enabled = true;
            colliders[i].isTrigger = false;
        }
    }

    static void SetLayerRecursive(GameObject go, int layer)
    {
        if (go == null)
            return;

        go.layer = layer;
        var transforms = go.GetComponentsInChildren<Transform>(includeInactive: true);
        for (int i = 0; i < transforms.Length; i++)
        {
            var t = transforms[i];
            if (t != null)
                t.gameObject.layer = layer;
        }
    }

    void IgnorePlayerCollisions(GameObject projectile)
    {
        if (projectile == null || cameraSource == null)
            return;

        var projectileColliders = projectile.GetComponentsInChildren<Collider>(includeInactive: true);
        if (projectileColliders == null || projectileColliders.Length == 0)
            return;

        var playerRoot = cameraSource.transform.root;
        var playerColliders = playerRoot != null ? playerRoot.GetComponentsInChildren<Collider>(includeInactive: true) : null;
        if (playerColliders == null || playerColliders.Length == 0)
            return;

        for (int i = 0; i < projectileColliders.Length; i++)
        {
            var projectileCollider = projectileColliders[i];
            if (projectileCollider == null)
                continue;

            for (int j = 0; j < playerColliders.Length; j++)
            {
                var playerCollider = playerColliders[j];
                if (playerCollider == null)
                    continue;
                Physics.IgnoreCollision(projectileCollider, playerCollider, true);
            }
        }
    }

    Material GetStoneTrailMaterial()
    {
        if (_stoneTrailMat != null)
            return _stoneTrailMat;

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Standard");

        _stoneTrailMat = new Material(shader) { name = "StoneTrailMat" };
        if (_stoneTrailMat.HasProperty("_BaseColor"))
            _stoneTrailMat.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.98f, 0.85f));
        if (_stoneTrailMat.HasProperty("_Color"))
            _stoneTrailMat.SetColor("_Color", new Color(0.92f, 0.92f, 0.98f, 0.85f));
        return _stoneTrailMat;
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

                if (useCircularSpreadPattern)
                {
                    float d = Mathf.Sqrt(x * x + z * z);
                    float inner = Mathf.Max(0f, ring - 0.85f);
                    float outer = ring + 0.45f;
                    if (d < inner || d > outer)
                        continue;
                }

                SpawnBlock(center + new Vector3(x * spreadCellSize, 0f, z * spreadCellSize), mat, sink, burnTargets, waterTargets);
            }
        }
    }

    void SpawnBlock(Vector3 pos, Material mat, System.Collections.Generic.List<GameObject> sink, bool burnTargets, bool waterTargets)
    {
        if (waterTargets && waterSpreadJitter > 0f)
        {
            float jitter = Mathf.Clamp(waterSpreadJitter, 0f, 0.5f) * spreadCellSize;
            pos.x += Random.Range(-jitter, jitter);
            pos.z += Random.Range(-jitter, jitter);
        }

        Vector3 surfacePos = ProjectToSurface(pos, out Vector3 surfaceNormal);

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

        var firePrefab = burnTargets ? PickFirePlacePrefab() : null;
        if (suppressTempSpreadVisualsOnLowPoly && IsLowPolyTerrainVisualActive() && firePrefab == null)
            return;

        string firePrefabName = firePrefab != null ? firePrefab.name : string.Empty;
        var go = firePrefab != null
            ? Instantiate(firePrefab)
            : GameObject.CreatePrimitive(useSoftDecalSpreadVisuals ? PrimitiveType.Quad : (waterTargets ? PrimitiveType.Sphere : PrimitiveType.Cube));
        go.name = currentTool == ApplyTool.Water
            ? "WaterTempBlock"
            : (firePrefab != null ? $"{firePrefabName}_FireTempBlock" : "FireTempBlock");
        if (firePrefab != null)
        {
            Vector3 n = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
            float scale = Mathf.Max(0.1f, firePlacePrefabScale * blockScale * Mathf.Max(1f, firePlacePrefabSizeMultiplier));
            go.transform.position = surfacePos + n * Mathf.Max(0f, spreadVisualLift);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.up, n) * Quaternion.AngleAxis(Random.Range(0f, 360f), n);
            go.transform.localScale = go.transform.localScale * scale;
            TryPlayFireModelAnimation(go, firePrefabName);
        }
        else if (useSoftDecalSpreadVisuals)
        {
            Vector3 n = surfaceNormal.sqrMagnitude > 0.0001f ? surfaceNormal.normalized : Vector3.up;
            float scale = Mathf.Max(0.1f, blockScale * (waterTargets ? waterSpreadVisualScale : fireSpreadVisualScale));
            go.transform.position = surfacePos + n * Mathf.Max(0f, spreadVisualLift);
            go.transform.rotation = Quaternion.FromToRotation(Vector3.forward, n) * Quaternion.AngleAxis(Random.Range(0f, 360f), n);
            go.transform.localScale = new Vector3(scale, scale, 1f);
        }
        else
        {
            go.transform.position = surfacePos;
            if (waterTargets)
                go.transform.localScale = new Vector3(blockScale * 1.08f, Mathf.Max(0.01f, blockThickness * 0.45f), blockScale * 1.08f);
            else
                go.transform.localScale = new Vector3(blockScale, blockThickness, blockScale);
        }

        var r = go.GetComponent<Renderer>();
        if (firePrefab == null && r != null && mat != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        var c = go.GetComponent<Collider>();
        if (c != null)
            Destroy(c);

        sink.Add(go);
    }

    void TryPlayFireModelAnimation(GameObject fireGO, string sourcePrefabName)
    {
        if (!autoPlayFireModelAnimation || fireGO == null)
            return;

        // If prefab already has a configured animator controller, let it run naturally.
        var animator = fireGO.GetComponentInChildren<Animator>(includeInactive: true);
        if (animator != null && animator.runtimeAnimatorController != null)
            return;

        var clip = PickFireAnimationClipForModel(sourcePrefabName);
        if (clip == null)
            return;

        var player = fireGO.GetComponent<FireModelClipPlayer>();
        if (player == null)
            player = fireGO.AddComponent<FireModelClipPlayer>();
        player.Play(clip, loop: true);
    }

    AnimationClip PickFireAnimationClipForModel(string sourcePrefabName)
    {
        if (!string.IsNullOrEmpty(sourcePrefabName))
        {
            string n = sourcePrefabName.ToLowerInvariant();
            if (n.Contains("v1"))
            {
                var v1 = PickAnyClip(firePlaceV1AnimationClips);
                if (v1 != null) return v1;
            }
            if (n.Contains("v2"))
            {
                var v2 = PickAnyClip(firePlaceV2AnimationClips);
                if (v2 != null) return v2;
            }
        }

        var any = PickAnyClip(firePlaceAnimationClips);
        if (any != null) return any;
        any = PickAnyClip(firePlaceV1AnimationClips);
        if (any != null) return any;
        return PickAnyClip(firePlaceV2AnimationClips);
    }

    static AnimationClip PickAnyClip(AnimationClip[] clips)
    {
        if (clips == null || clips.Length == 0)
            return null;
        int valid = 0;
        for (int i = 0; i < clips.Length; i++)
            if (clips[i] != null) valid++;
        if (valid == 0)
            return null;
        int pick = Random.Range(0, valid);
        int seen = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            var c = clips[i];
            if (c == null) continue;
            if (seen == pick) return c;
            seen++;
        }
        return null;
    }

    AnimationClip PickFireAnimationClip()
    {
        if (firePlaceAnimationClips == null || firePlaceAnimationClips.Length == 0)
            return null;
        int valid = 0;
        for (int i = 0; i < firePlaceAnimationClips.Length; i++)
            if (firePlaceAnimationClips[i] != null) valid++;
        if (valid == 0)
            return null;
        int pick = Random.Range(0, valid);
        int seen = 0;
        for (int i = 0; i < firePlaceAnimationClips.Length; i++)
        {
            var c = firePlaceAnimationClips[i];
            if (c == null) continue;
            if (seen == pick) return c;
            seen++;
        }
        return null;
    }

    System.Collections.IEnumerator ScorchAfterDelay(Vector3 worldPoint, float delaySeconds)
    {
        if (delaySeconds > 0f)
            yield return new WaitForSeconds(delaySeconds);

        if (_runtime == null || !_runtime.isActiveAndEnabled)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null)
            yield break;

        int scorched = _runtime.ScorchAroundWorld(worldPoint, radius: 0.75f, maxPlants: 1);
        if (logHits && scorched > 0)
            Debug.Log($"[FPSRaycastInteractor] Runtime scorched cells: {scorched}");
    }

    GameObject PickFirePlacePrefab()
    {
        if (firePlacePrefabs == null || firePlacePrefabs.Length == 0)
            return null;
        int valid = 0;
        for (int i = 0; i < firePlacePrefabs.Length; i++)
            if (firePlacePrefabs[i] != null) valid++;
        if (valid == 0)
            return null;

        int pick = Random.Range(0, valid);
        int seen = 0;
        for (int i = 0; i < firePlacePrefabs.Length; i++)
        {
            var p = firePlacePrefabs[i];
            if (p == null) continue;
            if (seen == pick) return p;
            seen++;
        }
        return null;
    }

    bool IsLowPolyTerrainVisualActive()
    {
        if (_voxelWorld == null || !_voxelWorld.isActiveAndEnabled)
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();
        return _voxelWorld != null && _voxelWorld.useLowPolyTerrainVisual;
    }

    Vector3 ProjectToSurface(Vector3 p, out Vector3 normal)
    {
        normal = Vector3.up;
        Vector3 origin = new Vector3(p.x, p.y + Mathf.Max(0.1f, surfaceProbeHeight), p.z);
        float dist = Mathf.Max(0.2f, surfaceProbeHeight * 2f);
        if (Physics.Raycast(origin, Vector3.down, out var hit, dist, hitMask, QueryTriggerInteraction.Ignore))
        {
            normal = hit.normal.sqrMagnitude > 0.0001f ? hit.normal.normalized : Vector3.up;
            float y = useSoftDecalSpreadVisuals
                ? hit.point.y + Mathf.Max(0.0015f, surfaceOffset)
                : hit.point.y + blockThickness * 0.5f + surfaceOffset;
            return new Vector3(p.x, y, p.z);
        }
        return p;
    }

    void TryBurnTintTarget(RaycastHit hit)
    {
        if (hit.collider == null)
            return;
        if (!TryResolveBurnTargetRoot(hit.collider, out var target))
            return;

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

            if (!TryResolveBurnTargetRoot(col, out var root))
                continue;

            if (root == null || !seen.Add(root))
                continue;

            Vector3 p = ClosestPointOnRoot(root, tileCenter);
            Vector2 d = new Vector2(p.x - tileCenter.x, p.z - tileCenter.z);
            if (d.magnitude > tileRadius)
                continue;

            BurnTintRenderers(root.GetComponentsInChildren<Renderer>(includeInactive: true));
        }
    }

    static bool TryResolveBurnTargetRoot(Collider col, out Transform root)
    {
        root = null;
        if (col == null)
            return false;

        // Prefer authored plant root if available.
        var growth = col.GetComponentInParent<FPSSeedGrowth>();
        if (growth != null)
        {
            root = growth.transform;
            return true;
        }

        // Accept explicitly tagged gameplay entities only.
        for (Transform t = col.transform; t != null; t = t.parent)
        {
            if (t.CompareTag("Harvestable"))
            {
                root = t;
                return true;
            }
        }

        // Never fall back to "any renderer" (that can blacken terrain/sky/water chunks).
        return false;
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
        if (renderers.Length > MaxBurnTintRendererCount)
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
        _waterSpreadStampTex = BuildSpreadStampTexture("TempWaterSpreadStamp");
        _waterSpreadMat = NewSpreadMat("TempWaterSpreadMat", waterSpreadColor, _waterSpreadStampTex);
        return _waterSpreadMat;
    }

    Material GetFireSpreadMat()
    {
        if (_fireSpreadMat != null) return _fireSpreadMat;
        _fireSpreadStampTex = BuildSpreadStampTexture("TempFireSpreadStamp");
        _fireSpreadMat = NewSpreadMat("TempFireSpreadMat", fireSpreadColor, _fireSpreadStampTex);
        return _fireSpreadMat;
    }

    Texture2D BuildSpreadStampTexture(string name)
    {
        int size = 64;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false, true);
        tex.name = name;
        tex.wrapMode = TextureWrapMode.Clamp;
        tex.filterMode = FilterMode.Bilinear;

        float inner = Mathf.Clamp01(spreadVisualInnerRadius);
        float softness = Mathf.Clamp(spreadVisualEdgeSoftness, 0.001f, 1f);
        float edge0 = inner;
        float edge1 = Mathf.Clamp01(inner + softness);

        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size * 2f - 1f;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float a = 0f;
                if (r <= 1f)
                {
                    float t = Mathf.InverseLerp(edge0, edge1, r);
                    a = 1f - Mathf.SmoothStep(0f, 1f, t);
                }
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
        }
        tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        return tex;
    }

    static Material NewSpreadMat(string name, Color color, Texture2D stampTex)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null) shader = Shader.Find("Unlit/Transparent");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (stampTex != null)
        {
            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", stampTex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", stampTex);
        }
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // URP Transparent
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f); // Alpha
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent + 5;
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
    sealed class FireModelClipPlayer : MonoBehaviour
    {
        PlayableGraph _graph;
        bool _created;

        public void Play(AnimationClip clip, bool loop)
        {
            if (clip == null)
                return;
            Stop();

            var animator = GetComponentInChildren<Animator>(includeInactive: true);
            if (animator == null)
                animator = gameObject.AddComponent<Animator>();

            _graph = PlayableGraph.Create("FireModelClipPlayer");
            var output = AnimationPlayableOutput.Create(_graph, "Animation", animator);
            var playable = AnimationClipPlayable.Create(_graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            if (loop)
                playable.SetDuration(double.PositiveInfinity);
            output.SetSourcePlayable(playable);
            _graph.Play();
            _created = true;
        }

        void OnDisable() => Stop();
        void OnDestroy() => Stop();

        void Stop()
        {
            if (!_created)
                return;
            if (_graph.IsValid())
                _graph.Destroy();
            _created = false;
        }
    }

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
