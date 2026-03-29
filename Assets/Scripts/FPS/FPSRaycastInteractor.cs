using UnityEngine;
using SCoL;
using SCoL.Visualization;
using SCoL.Weather;
using SCoL.Inventory;
using SCoL.Combat;
using SCoL.Settlement;
using SCoL.Voxels;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.InputSystem;
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
    static FPSRaycastInteractor _instance;

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

    [Header("Use Tool")]
    public ApplyTool currentTool = ApplyTool.Water;
    [Min(1)] public int spreadLayers = 3;
    [Min(0.1f)] public float spreadLayerIntervalSeconds = 1f;
    [Min(0f)] public float lingerAfterSpreadSeconds = 0.05f;
    [Min(0.1f)] public float spreadCellSize = 1f;
    [Min(0.1f)] public float blockScale = 1f;
    [Range(0.01f, 0.5f)] public float blockThickness = 0.06f;
    [Min(0.1f)] public float surfaceProbeHeight = 10f;
    [Min(0f)] public float surfaceOffset = 0.01f;
    [Tooltip("Tint applied to spawned water visuals.")]
    public Color waterSpreadColor = new Color(0.12f, 0.42f, 1f, 0.95f);
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
    [Header("Water Effect")]
    [Tooltip("If assigned, water visuals will instantiate these prefabs instead of primitive decals/blocks.")]
    public GameObject[] waterPlacePrefabs;
    [Tooltip("Base scale applied to each spawned water visual before fit/scatter multipliers.")]
    [Range(0.1f, 3f)] public float waterPlacePrefabScale = 0.9f;
    [Tooltip("Extra global size multiplier for each spawned water visual.")]
    [Range(0.01f, 80f)] public float waterPlacePrefabSizeMultiplier = 0.012f;
    [Tooltip("Additional vertical offset from the hit point.")]
    [Min(0f)] public float waterPlaceSpawnHeight = 0.55f;
    [Tooltip("Additional local/world offset applied to spawned water effects.")]
    public Vector3 waterPlacePositionOffset = new Vector3(0f, 0.55f, 0f);
    [Tooltip("Additional Euler rotation applied after the default downward-facing rotation.")]
    public Vector3 waterPlaceEulerOffset = Vector3.zero;
    [Tooltip("Per-axis scale multiplier applied after fit-to-height and size multiplier.")]
    public Vector3 waterPlaceScaleMultiplier = new Vector3(0.09f, 0.09f, 0.09f);
    [Tooltip("Target fitted world height for each water visual before final scaling.")]
    [Min(0.01f)] public float waterPlaceTargetHeight = 0.002f;
    [Tooltip("How long spawned water visuals remain before cleanup.")]
    [Min(0.05f)] public float waterEffectLifetimeSeconds = 1.4f;
    [Tooltip("Playback speed for animated water effects.")]
    [Range(0.1f, 4f)] public float waterAnimationSpeed = 1f;
    [Tooltip("How many water particles/effect instances are spawned per use.")]
    [Min(1)] public int waterSingleEffectCount = 50;
    [Tooltip("How far the water particles can spread from the hit point.")]
    [Min(0f)] public float waterSingleEffectScatterRadius = 0.8f;
    [Tooltip("If enabled, spawned water models auto-play imported animation clips.")]
    public bool autoPlayWaterModelAnimation = true;
    [Tooltip("Optional explicit water animation clips. If empty, clips are auto-loaded from waterV2 in editor.")]
    public AnimationClip[] waterPlaceAnimationClips;

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
    public bool animalsFleeWhenFireToolSelected = true;
    [Min(0.1f)] public float fireRepelRadius = 10f;
    [Min(0f)] public float fireRepelWeight = 5f;
    [Min(0f)] public float fireRepelFrontOffset = 1.2f;
    public bool plantFeedConsumesInventory = true;
    public bool plantsCanHealPlayer = true;
    [Min(0f)] public float plantHealAmount = 15f;
    [Min(0.05f)] public float feedJumpHeight = 0.35f;
    [Min(0.2f)] public float feedReactionDuration = 1.2f;
    [Min(1)] public int feedJumpCount = 3;
    public GameObject animalFeedEffectPrefab;
    [Min(0.1f)] public float animalFeedEffectLifetime = 2f;
    [Min(0f)] public float fireDamageToAnimals = 20f;

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
    [Range(0f, 1f)] public float thunderBurnFlowerFractionMin = 0.40f;
    [Range(0f, 1f)] public float thunderBurnFlowerFractionMax = 0.40f;
    [Min(0f)] public float thunderBurnAutoClearSeconds = 5f;
    [Min(0f)] public float thunderBurnSpreadBlockSeconds = 20f;

    [Header("Pickup Action")]
    public bool collectPickupsOnRightClick = true;
    public bool collectWaterFromRegionOnRightClick = true;
    [Min(1)] public int waterCollectAmount = 1;

    [Header("Drop Action")]
    public bool waterToolCanBeDropped = false;
    [Min(0.5f)] public float dropForwardDistance = 1.8f;
    [Min(0f)] public float dropVerticalOffset = 0.18f;
    [Min(0f)] public float dropGroundClearance = 0.01f;

    [Header("Held Tool Visuals")]
    public bool showHeldToolVisuals = true;
    public GameObject heldWaterCanPrefab;
    public Vector3 heldWaterCanLocalPosition = new Vector3(0.32f, -0.28f, 0.62f);
    public Vector3 heldWaterCanLocalEuler = new Vector3(12f, -24f, -12f);
    [Min(0.05f)] public float heldWaterCanScale = 0.42f;
    public Color heldWaterCanTint = new Color(0.24f, 0.68f, 0.96f, 1f);
    [Range(0f, 2f)] public float heldWaterCanEmission = 0.18f;
    public GameObject heldFirePrefab;
    public Vector3 heldFireLocalPosition = new Vector3(0.34f, -0.30f, 0.58f);
    public Vector3 heldFireLocalEuler = new Vector3(18f, -32f, -18f);
    [Min(0.05f)] public float heldFireScale = 0.34f;
    public GameObject heldSeedPrefab;
    public Vector3 heldSeedLocalPosition = new Vector3(0.31f, -0.31f, 0.56f);
    public Vector3 heldSeedLocalEuler = new Vector3(14f, -18f, -12f);
    [Min(0.05f)] public float heldSeedScale = 0.12f;
    public GameObject heldPlantPrefab;
    public Vector3 heldPlantLocalPosition = new Vector3(0.30f, -0.30f, 0.56f);
    public Vector3 heldPlantLocalEuler = new Vector3(10f, -12f, -8f);
    [Min(0.05f)] public float heldPlantScale = 0.28f;
    public GameObject heldStonePrefab;
    public Vector3 heldStoneLocalPosition = new Vector3(0.34f, -0.34f, 0.56f);
    public Vector3 heldStoneLocalEuler = new Vector3(8f, -16f, -10f);
    [Min(0.05f)] public float heldStoneScale = 0.24f;

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
    WeatherSystem _subscribedWeatherSystem;
    SeasonSkyboxController _seasonSkybox;
    float _nextSeasonLookupAt;
    float _nextThunderTargetCheckAt;
    VoxelWorld _voxelWorld;
    SCoLCombatHealth _playerCombatHealth;
    SpawnPickups _spawnPickups;
    SCoLSettlementManager _settlementManager;
    GameObject _heldToolInstance;
    GameObject _heldToolSourcePrefab;
    struct PlantDestroyClickState
    {
        public int count;
        public float expiresAt;
    }

    readonly System.Collections.Generic.Dictionary<int, PlantDestroyClickState> _plantDestroyClicks = new System.Collections.Generic.Dictionary<int, PlantDestroyClickState>();

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

        currentTool = ApplyTool.Water;

        EnsureFpsFeedbackSystems();
        AutoAssignFinalFlowerStagePrefab();
        AutoAssignFirePlacePrefabs();
        AutoAssignWaterPlacePrefabs();
        AutoAssignHeldWaterCanPrefab();
        AutoAssignHeldFirePrefab();
        AutoAssignAnimalFeedEffectPrefab();

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
        _settlementManager = FindFirstObjectByType<SCoLSettlementManager>();
        RefreshWeatherSystemSubscription();
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

    void OnDisable()
    {
        UnsubscribeWeatherSystem();
        if (_instance == this)
            _instance = null;
        ClearActiveFire();
        SetHeldToolVisible(false);
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

    private void AutoAssignHeldFirePrefab()
    {
#if UNITY_EDITOR
        if (heldFirePrefab != null)
            return;
        heldFirePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/stick1/stick1.obj");
        if (heldFirePrefab == null)
            heldFirePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/stick2/stick2.obj");
#endif
    }

    private void AutoAssignAnimalFeedEffectPrefab()
    {
#if UNITY_EDITOR
        if (animalFeedEffectPrefab != null)
            return;
        animalFeedEffectPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Material/AnimalFeedEffect.prefab");
#endif
    }

    void SoftenSpawnedParticleEffect(GameObject root, Color baseColor)
    {
        if (root == null)
            return;

        var renderers = root.GetComponentsInChildren<ParticleSystemRenderer>(includeInactive: true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            renderer.renderMode = ParticleSystemRenderMode.Billboard;
#if UNITY_EDITOR
            var glowMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Material/SpringGlow.mat");
            if (glowMat != null)
                renderer.sharedMaterial = glowMat;
#endif

            var ps = renderer.GetComponent<ParticleSystem>();
            if (ps == null)
                continue;

            var main = ps.main;
            Color tinted = baseColor;
            tinted.a = 0.72f;
            main.startColor = tinted;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[]
                {
                    new GradientColorKey(baseColor, 0f),
                    new GradientColorKey(Color.Lerp(baseColor, Color.white, 0.35f), 0.55f),
                    new GradientColorKey(baseColor, 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0f),
                    new GradientAlphaKey(0.9f, 0.2f),
                    new GradientAlphaKey(0.45f, 0.7f),
                    new GradientAlphaKey(0f, 1f),
                });
            col.color = new ParticleSystem.MinMaxGradient(gradient);
        }
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

    private void AutoAssignWaterPlacePrefabs()
    {
#if UNITY_EDITOR
        if (waterPlacePrefabs != null && waterPlacePrefabs.Length > 0)
        {
            for (int i = 0; i < waterPlacePrefabs.Length; i++)
                if (waterPlacePrefabs[i] != null)
                    return;
        }

        const string waterAbcPath = "Assets/Models/Modeling/_Incoming/Water/waterV3.abc";
        const string waterObjPath = "Assets/Models/Modeling/_Incoming/Water/waterObjV3.obj";
        const string waterFbxPath = "Assets/Models/Modeling/_Incoming/Water/waterV2.fbx";

        string waterPath = waterAbcPath;
        var water = AssetDatabase.LoadAssetAtPath<GameObject>(waterPath);
        if (water == null)
        {
            waterPath = waterObjPath;
            water = AssetDatabase.LoadAssetAtPath<GameObject>(waterPath);
        }
        if (water == null)
        {
            waterPath = waterFbxPath;
            water = AssetDatabase.LoadAssetAtPath<GameObject>(waterPath);
        }
        if (water == null)
            return;

        waterPlacePrefabs = new[] { water };

        if ((waterPlaceAnimationClips == null || waterPlaceAnimationClips.Length == 0) && waterPath == waterFbxPath)
        {
            var clips = new System.Collections.Generic.List<AnimationClip>(4);
            AppendAnimationClipsFromAsset(clips, waterPath);
            waterPlaceAnimationClips = clips.ToArray();
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
        if (_settlementManager == null)
            _settlementManager = FindFirstObjectByType<SCoLSettlementManager>();
        RefreshWeatherSystemSubscription();

        if (_settlementManager != null && _settlementManager.IsStorageUiOpen)
        {
            UpdateHeldToolVisual();
            return;
        }
        HandleToolSwitchInput();
        UpdateHeldToolVisual();
        UpdatePlantAttractor();

        if (SCoL.Interaction.SCoLInteractionInput.ChestPressed())
        {
            if (_settlementManager != null &&
                _settlementManager.TryGetStorageNearAim(cameraSource, maxDistance, out var storageInteractable) &&
                TryOpenSettlementStorage(storageInteractable))
                return;

            if (FPSAimTargeting.TryResolve(cameraSource, maxDistance, hitMask, _runtime, _plantRenderer, out var chestTarget) &&
                chestTarget.kind == FPSAimTargetKind.SettlementStorage &&
                TryOpenSettlementStorage(chestTarget.settlementInteractable))
                return;
        }

        // Primary: pick up / fill / uproot
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
                    if (collectPickupsOnRightClick && TryCollectPickupAtHit(hit))
                        return;

                    if (currentTool == ApplyTool.Water &&
                        collectWaterFromRegionOnRightClick &&
                        TryCollectWaterFromRegionAtHit(hit))
                        return;

                    if (pickPlantsOnPrimaryClick && TryPickupPlantAtHit(hit))
                        return;
                }
            }
            else
            {
                if (logHits)
                    Debug.Log("[FPSRaycastInteractor] No hit");
            }
        }

        // Secondary: use active tool
        if (SCoL.Interaction.SCoLInteractionInput.SecondaryPressed())
        {
            if (!SCoL.Interaction.SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
                return;

            if (_inventory == null)
                _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
            if (_inventory == null)
                return;

            if (Physics.Raycast(ray, out var closeHit, maxDistance, hitMask, QueryTriggerInteraction.Ignore) &&
                TryHandleSettlementSecondary(closeHit))
                return;

            if (currentTool == ApplyTool.Stone)
            {
                ThrowStone(ray);
                return;
            }

            if (!Physics.Raycast(ray, out var hit, 50f, hitMask, QueryTriggerInteraction.Ignore))
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
                    if (CanCollectWaterFromRegionAtHit(hit))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] Use pickup action to fill the watering can at ponds.");
                        return;
                    }

                    bool targetingPlant = TryResolveWaterApplicationTarget(hit, out Vector3 waterPoint, out Vector3 waterNormal);
                    bool targetedCAPlant = TryResolveCAPlantCellFromWorld(hit.point, out int waterCellX, out int waterCellY);
                    var targetedLegacyPlant = hit.collider != null ? hit.collider.GetComponentInParent<FPSSeedGrowth>() : null;
                    bool winterPlantTarget = IsWinterSeasonActive() && (targetedCAPlant || targetedLegacyPlant != null);

                    // Non-plant targets still require an upward-facing ground surface.
                    if (!targetingPlant && hit.normal.y < 0.35f)
                        return;
                    if (winterPlantTarget)
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] Water growth disabled during Winter.");
                        return;
                    }

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

                    StartCoroutine(SpawnSingleWaterEffect(waterPoint, waterNormal));
                    if (_runtime == null || !_runtime.isActiveAndEnabled)
                        _runtime = FindFirstObjectByType<SCoLRuntime>();
                    if (_runtime != null)
                    {
                        int n;
                        if (targetedCAPlant)
                        {
                            _runtime.AddWaterAtCell(waterCellX, waterCellY, 1.0f);
                            n = 1;
                        }
                        else if (targetedLegacyPlant != null)
                        {
                            _runtime.AddWaterAt(waterPoint, 1.0f);
                            n = 1;
                        }
                        else
                        {
                            n = _runtime.AddWaterAroundWorld(waterPoint, radius: 1.6f, amount: 1.0f);
                        }
                        if (logHits) Debug.Log($"[FPSRaycastInteractor] Runtime water affected cells: {n}");
                    }
                    if (targetedLegacyPlant != null)
                        targetedLegacyPlant.ApplyWaterBoost(waterBoostSecondsPerTile);
                    DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.WaterDrop);
                    break;
                }

                case ApplyTool.Fire:
                {
                    var animal = hit.collider != null ? hit.collider.GetComponentInParent<FPSBoidAgent>() : null;
                    bool targetingPlant =
                        (hit.collider != null && hit.collider.GetComponentInParent<FPSSeedGrowth>() != null) ||
                        (hit.collider != null && IsHarvestableHierarchy(hit.collider.transform)) ||
                        TryResolveCAPlantCellFromWorld(hit.point, out _, out _);

                    // Allow fire on plants even if surface normal is not upward.
                    if (animal == null && !targetingPlant && hit.normal.y < 0.35f)
                        return;

                    if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Fire, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No fire to place");
                        return;
                    }

                    if (animal != null && fireDamageToAnimals > 0f)
                    {
                        var health = animal.GetComponent<SCoL.Combat.SCoLCombatHealth>();
                        if (health != null)
                        {
                            bool wasAlive = !health.IsDead;
                            health.ApplyDamage(fireDamageToAnimals);
                            if (wasAlive && health.IsDead && (health.Faction == SCoLCombatFaction.Animal || health.Faction == SCoLCombatFaction.Wolf))
                                DayNightLightingController.PlayAnimalDamageAt(animal.transform.position, 1f);
                        }
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
                        if (!plantsCanHealPlayer)
                        {
                            if (logHits) Debug.Log("[FPSRaycastInteractor] Plant feed requires targeting an animal (FPSBoidAgent).");
                            return;
                        }

                        EnsurePlayerCombatHealth();
                        if (_playerCombatHealth == null || _playerCombatHealth.IsDead)
                            return;
                        if (_playerCombatHealth.CurrentHealth >= _playerCombatHealth.MaxHealth - 0.001f)
                        {
                            if (logHits) Debug.Log("[FPSRaycastInteractor] Player health already full.");
                            return;
                        }
                        if (plantFeedConsumesInventory && !_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Plant, 1))
                        {
                            if (logHits) Debug.Log("[FPSRaycastInteractor] No plant items to eat");
                            return;
                        }

                        _playerCombatHealth.Heal(Mathf.Max(0f, plantHealAmount));
                        FPSGameFeel.VoxelBurst(cameraSource.transform.position + cameraSource.transform.forward * 0.55f, count: 8, spread: 0.45f, life: 0.45f, cubeSize: 0.035f);
                        FPSGameFeel.Shake(0.025f, 0.05f);
                        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.PickupItem);
                        if (logHits) Debug.Log($"[FPSRaycastInteractor] Player ate plant and healed {plantHealAmount:0.#} HP.");
                        return;
                    }

                    if (plantFeedConsumesInventory && !_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Plant, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No plant items to feed");
                        return;
                    }

                    animal.FeedWithPlant(feedJumpHeight, feedReactionDuration, feedJumpCount);
                    DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.Slurp);
                    SpawnTransientEffect(animalFeedEffectPrefab, feedPoint, animal.transform.rotation, animalFeedEffectLifetime);
                    FPSGameFeel.VoxelBurst(feedPoint, count: 10, spread: 0.8f, life: 0.6f, cubeSize: 0.045f);
                    FPSGameFeel.Shake(0.04f, 0.08f);
                    if (logHits) Debug.Log($"[FPSRaycastInteractor] Fed animal: {animal.name}", animal);
                    break;
                }

                case ApplyTool.Stone:
                    break;
            }
        }

        // Third action: put down / store current item
        if (SCoL.Interaction.SCoLInteractionInput.DropPressed())
        {
            if (!SCoL.Interaction.SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
                return;

            if (_inventory == null)
                _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
            if (_inventory == null)
                return;

            if (TryDropActiveItem(ray))
                return;
        }
    }

    bool TryHandleSettlementPrimary(RaycastHit hit)
    {
        var interactable = hit.collider != null ? hit.collider.GetComponentInParent<SCoLSettlementInteractable>() : null;
        if (interactable == null)
            return false;

        if (_settlementManager == null)
            _settlementManager = interactable.manager != null ? interactable.manager : FindFirstObjectByType<SCoLSettlementManager>();
        if (_settlementManager == null)
            return false;

        switch (interactable.kind)
        {
            case SCoLSettlementInteractableKind.Storage:
            {
                _settlementManager.OpenStorageUi();
                DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.ToggleSwitch);
                return true;
            }
            default:
                return false;
        }
    }

    bool TryOpenSettlementStorage(RaycastHit hit)
    {
        var interactable = hit.collider != null ? hit.collider.GetComponentInParent<SCoLSettlementInteractable>() : null;
        return TryOpenSettlementStorage(interactable);
    }

    bool TryOpenSettlementStorage(SCoLSettlementInteractable interactable)
    {
        if (interactable == null || interactable.kind != SCoLSettlementInteractableKind.Storage)
            return false;

        if (_settlementManager == null)
            _settlementManager = interactable.manager != null ? interactable.manager : FindFirstObjectByType<SCoLSettlementManager>();
        if (_settlementManager == null)
            return false;

        _settlementManager.OpenStorageUi();
        if (logHits)
            Debug.Log("[FPSRaycastInteractor] Opened storage chest.");
        return true;
    }

    bool TryHandleSettlementSecondary(RaycastHit hit)
    {
        var interactable = hit.collider != null ? hit.collider.GetComponentInParent<SCoLSettlementInteractable>() : null;
        if (interactable == null)
            return false;

        if (_settlementManager == null)
            _settlementManager = interactable.manager != null ? interactable.manager : FindFirstObjectByType<SCoLSettlementManager>();
        if (_settlementManager == null)
            return false;

        switch (interactable.kind)
        {
            case SCoLSettlementInteractableKind.Centerpiece:
            {
                bool activated = _settlementManager.TryActivate(out string message);
                if (logHits && !string.IsNullOrEmpty(message))
                    Debug.Log($"[FPSRaycastInteractor] {message}");
                DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.ToggleSwitch);
                if (activated)
                {
                    FPSGameFeel.VoxelBurst(hit.point, count: 12, spread: 1.15f, life: 0.8f, cubeSize: 0.06f);
                    FPSGameFeel.Shake(0.04f, 0.08f);
                }
                return true;
            }
            default:
                return false;
        }
    }

    bool TryHandleSettlementDrop(RaycastHit hit)
    {
        var interactable = hit.collider != null ? hit.collider.GetComponentInParent<SCoLSettlementInteractable>() : null;
        if (interactable == null || interactable.kind != SCoLSettlementInteractableKind.Storage)
            return false;

        if (_settlementManager == null)
            _settlementManager = interactable.manager != null ? interactable.manager : FindFirstObjectByType<SCoLSettlementManager>();
        if (_settlementManager == null)
            return false;

        _settlementManager.OpenStorageUi();
        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.ToggleSwitch);
        return true;
    }

    bool TryDropActiveItem(Ray aimRay)
    {
        if (_inventory == null)
            return false;

        if (Physics.Raycast(aimRay, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore) &&
            TryHandleSettlementDrop(hit))
            return true;

        if (currentTool == ApplyTool.Water && !waterToolCanBeDropped)
        {
            if (logHits) Debug.Log("[FPSRaycastInteractor] Watering can is not droppable.");
            return false;
        }

        Vector3 dropPoint = ResolveDropPoint(aimRay);
        GameObject prefab = null;
        SCoLItemType type;
        int seedVariantIndex = -1;
        bool consumed;

        switch (currentTool)
        {
            case ApplyTool.Seed:
                seedVariantIndex = GetSelectedSeedVariantIndex();
                consumed = _inventory.TryConsumeSeedType(seedVariantIndex, 1);
                if (!consumed)
                {
                    if (logHits) Debug.Log("[FPSRaycastInteractor] No selected seeds to drop.");
                    return false;
                }
                type = SCoLItemType.Seed;
                prefab = ResolveHeldSeedPrefab();
                break;
            case ApplyTool.Fire:
                consumed = _inventory.TryConsume(SCoLItemType.Fire, 1);
                if (!consumed)
                {
                    if (logHits) Debug.Log("[FPSRaycastInteractor] No branches to drop.");
                    return false;
                }
                type = SCoLItemType.Fire;
                prefab = ResolveHeldFirePrefab();
                break;
            case ApplyTool.Plant:
                consumed = _inventory.TryConsume(SCoLItemType.Plant, 1);
                if (!consumed)
                {
                    if (logHits) Debug.Log("[FPSRaycastInteractor] No plants to drop.");
                    return false;
                }
                type = SCoLItemType.Plant;
                prefab = ResolveHeldPlantPrefab();
                break;
            case ApplyTool.Stone:
                consumed = _inventory.TryConsume(SCoLItemType.Stone, 1);
                if (!consumed)
                {
                    if (logHits) Debug.Log("[FPSRaycastInteractor] No stones to drop.");
                    return false;
                }
                type = SCoLItemType.Stone;
                prefab = ResolveHeldStonePrefab();
                break;
            default:
                return false;
        }

        SpawnDroppedPickup(type, dropPoint, prefab, seedVariantIndex);
        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.ToggleSwitch);
        return true;
    }

    Vector3 ResolveDropPoint(Ray aimRay)
    {
        if (Physics.Raycast(aimRay, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            return hit.point;

        Vector3 fallbackOrigin = cameraSource != null ? cameraSource.transform.position : transform.position;
        Vector3 fallbackForward = cameraSource != null ? cameraSource.transform.forward : transform.forward;
        return fallbackOrigin + fallbackForward * Mathf.Max(0.5f, dropForwardDistance) + Vector3.up * dropVerticalOffset;
    }

    void SpawnDroppedPickup(SCoLItemType type, Vector3 worldPos, GameObject prefab, int seedVariantIndex = -1)
    {
        GameObject go = prefab != null
            ? Instantiate(prefab, worldPos + Vector3.up * Mathf.Max(0f, dropVerticalOffset), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f))
            : GameObject.CreatePrimitive(type == SCoLItemType.Seed || type == SCoLItemType.Stone ? PrimitiveType.Sphere : PrimitiveType.Capsule);

        go.name = $"Dropped_{type}";
        if (prefab == null)
        {
            go.transform.position = worldPos + Vector3.up * Mathf.Max(0f, dropVerticalOffset);
            go.transform.localScale = type switch
            {
                SCoLItemType.Seed => new Vector3(0.09f, 0.09f, 0.09f),
                SCoLItemType.Stone => new Vector3(0.22f, 0.22f, 0.22f),
                SCoLItemType.Plant => new Vector3(0.24f, 0.28f, 0.24f),
                _ => new Vector3(0.24f, 0.18f, 0.24f)
            };
        }
        else if (type == SCoLItemType.Plant)
        {
            go.transform.localScale = go.transform.localScale * 0.38f;
        }

        SetLayerRecursive(go, 0);
        EnsureDropCollider(go);

        var rb = go.GetComponent<Rigidbody>();
        if (rb == null)
            rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;

        var pickup = go.GetComponent<SCoLPickup>();
        if (pickup == null)
            pickup = go.AddComponent<SCoLPickup>();
        pickup.type = type;
        pickup.amount = 1;
        pickup.seedVariantIndex = seedVariantIndex;
        pickup.preserveExistingMaterials = prefab != null;
        pickup.ApplyVisual();

        SnapDroppedPickupToGround(go, worldPos);

        if (logHits)
            Debug.Log($"[FPSRaycastInteractor] Dropped {type}.");
    }

    void EnsureDropCollider(GameObject go)
    {
        if (go == null)
            return;

        var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
        if (colliders == null || colliders.Length == 0)
        {
            var box = go.AddComponent<BoxCollider>();
            box.enabled = true;
            box.isTrigger = false;
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

    void SnapDroppedPickupToGround(GameObject go, Vector3 aroundPos)
    {
        if (go == null || !TryGetDropBottomY(go, out float bottomY))
            return;

        float groundY = aroundPos.y;
        Vector3 terrainSample = aroundPos + Vector3.up * Mathf.Max(4f, surfaceProbeHeight);
        if (_voxelWorld == null || !_voxelWorld.isActiveAndEnabled)
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();
        if (_voxelWorld != null &&
            _voxelWorld.TryGetTerrainSurfaceYAtWorld(terrainSample, out float terrainY, includeWaterSurface: false))
        {
            groundY = terrainY;
        }
        else
        {
            var hits = Physics.RaycastAll(
                aroundPos + Vector3.up * Mathf.Max(8f, surfaceProbeHeight),
                Vector3.down,
                Mathf.Max(20f, surfaceProbeHeight * 3f),
                ~0,
                QueryTriggerInteraction.Ignore);
            bool found = false;
            float lowestY = float.PositiveInfinity;
            for (int i = 0; i < hits.Length; i++)
            {
                var probeHit = hits[i];
                if (!IsValidDropGroundHit(go, probeHit.collider))
                    continue;

                if (probeHit.point.y < lowestY)
                {
                    lowestY = probeHit.point.y;
                    found = true;
                }
            }

            if (found)
                groundY = lowestY;
        }

        float dy = (groundY + Mathf.Max(0f, dropGroundClearance)) - bottomY;
        if (!Mathf.Approximately(dy, 0f))
            go.transform.position += Vector3.up * dy;
    }

    static bool TryGetDropBottomY(GameObject go, out float bottomY)
    {
        bottomY = 0f;
        if (go == null)
            return false;

        bool hasBounds = false;
        Bounds bounds = default;
        var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
                continue;

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        if (!hasBounds)
        {
            var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; i++)
            {
                var collider = colliders[i];
                if (collider == null)
                    continue;

                if (!hasBounds)
                {
                    bounds = collider.bounds;
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
        }

        if (!hasBounds)
            return false;

        bottomY = bounds.min.y;
        return true;
    }

    static bool IsValidDropGroundHit(GameObject go, Collider collider)
    {
        if (go == null || collider == null || !collider.enabled || collider.isTrigger)
            return false;

        if (collider.transform.IsChildOf(go.transform))
            return false;

        if (collider.GetComponentInParent<SCoLPickup>() != null)
            return false;

        return true;
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
        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.PickupItem);
        Destroy(pickup.gameObject);
        return true;
    }

    bool TryCollectWaterFromRegionAtHit(RaycastHit hit)
    {
        if (_inventory == null)
            return false;
        if (!CanCollectWaterFromRegionAtHit(hit))
            return false;

        int amount = Mathf.Max(1, waterCollectAmount);
        _inventory.Add(SCoLItemType.Water, amount);
        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.WaterFill);
        if (logHits) Debug.Log($"[FPSRaycastInteractor] Collected water from region: +{amount}");
        return true;
    }

    bool CanCollectWaterFromRegionAtHit(RaycastHit hit)
    {
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
        // Thunder scorching is handled once per thunderstorm via weather phase start.
    }

    void RefreshWeatherSystemSubscription()
    {
        if (_weatherSystem == null || !_weatherSystem.isActiveAndEnabled)
            _weatherSystem = FindFirstObjectByType<WeatherSystem>();

        if (_subscribedWeatherSystem == _weatherSystem)
            return;

        UnsubscribeWeatherSystem();

        if (_weatherSystem != null)
        {
            _weatherSystem.OnPhaseStarted += HandleWeatherPhaseStarted;
            _subscribedWeatherSystem = _weatherSystem;
        }
    }

    void UnsubscribeWeatherSystem()
    {
        if (_subscribedWeatherSystem == null)
            return;
        _subscribedWeatherSystem.OnPhaseStarted -= HandleWeatherPhaseStarted;
        _subscribedWeatherSystem = null;
    }

    void HandleWeatherPhaseStarted(WeatherPhase phase)
    {
        if (phase != WeatherPhase.Thunderstorm || !thunderCanIgniteTargetedPlant)
            return;

        if (_runtime == null || !_runtime.isActiveAndEnabled)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null)
            return;

        int scorched = _runtime.ScorchRandomFlowersByFraction(
            thunderBurnFlowerFractionMin,
            thunderBurnFlowerFractionMax,
            thunderBurnAutoClearSeconds,
            thunderBurnSpreadBlockSeconds);

        if (logHits && scorched > 0)
            Debug.Log($"[FPSRaycastInteractor] Thunder scorched flowers across the world: {scorched}");
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
        bool rewardsPlantInventory = false;
        Object logContext = null;

        if (growth != null)
        {
            key = growth.GetInstanceID();
            targetLabel = growth.name;
            logContext = growth;
            rewardsPlantInventory = growth.IsMature && !growth.IsBurned;
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
            rewardsPlantInventory = IsFinalFlowerCell(cx, cy);
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
            if (rewardsPlantInventory && _inventory != null)
                _inventory.Add(SCoLItemType.Plant, 1);
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

    bool IsFinalFlowerCell(int x, int y)
    {
        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null || _runtime.Grid == null || !_runtime.Grid.InBounds(x, y))
            return false;

        var cell = _runtime.Grid.Get(x, y);
        if (cell == null || !cell.HasPlant || cell.PlantStage == SCoL.PlantStage.Burnt)
            return false;

        return cell.IsPlayerSeedLineage &&
               cell.FlowerVariantIndex >= 0 &&
               cell.PlantStage >= SCoL.PlantStage.MediumTree;
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

    void OnDestroy()
    {
        if (_heldToolInstance != null)
            Destroy(_heldToolInstance);
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
        bool fireEnabled = animalsFleeWhenFireToolSelected && currentTool == ApplyTool.Fire;
        FPSBoidAgent.SetFireRepellent(
            fireEnabled ? cameraSource.transform : null,
            fireEnabled,
            fireRepelRadius,
            fireRepelWeight,
            fireRepelFrontOffset
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
        ApplyTool previousTool = currentTool;

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

        if (currentTool == ApplyTool.Seed && TryHandleGamepadSeedVariantInput())
        {
            // D-pad left/right is reserved for seed variant switching while Seed tool is active.
        }
        else if (SCoL.Interaction.SCoLInteractionInput.ToolNextPressed())
            CycleTool(+1);
        if (!(currentTool == ApplyTool.Seed && IsGamepadDpadHorizontalPressed()) &&
            SCoL.Interaction.SCoLInteractionInput.ToolPrevPressed())
            CycleTool(-1);

        if (currentTool != previousTool)
            DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.ToggleSwitch);
    }

    bool TryHandleGamepadSeedVariantInput()
    {
        var gamepad = Gamepad.current;
        if (gamepad == null)
            return false;

        if (gamepad.dpad.right.wasPressedThisFrame)
            return TryCycleOwnedSeedVariant(+1);
        if (gamepad.dpad.left.wasPressedThisFrame)
            return TryCycleOwnedSeedVariant(-1);
        return false;
    }

    bool IsGamepadDpadHorizontalPressed()
    {
        var gamepad = Gamepad.current;
        return gamepad != null && (gamepad.dpad.left.wasPressedThisFrame || gamepad.dpad.right.wasPressedThisFrame);
    }

    void UpdateHeldToolVisual()
    {
        if (!showHeldToolVisuals || cameraSource == null)
        {
            SetHeldToolVisible(false);
            return;
        }

        GameObject desiredPrefab = ResolveHeldToolPrefab();
        if (desiredPrefab == null)
        {
            SetHeldToolVisible(false);
            return;
        }

        if (!EnsureHeldToolInstance(desiredPrefab))
            return;

        var t = _heldToolInstance.transform;
        if (t.parent != cameraSource.transform)
            t.SetParent(cameraSource.transform, false);

        switch (currentTool)
        {
            case ApplyTool.Seed:
                t.localPosition = heldSeedLocalPosition;
                t.localRotation = Quaternion.Euler(heldSeedLocalEuler);
                t.localScale = Vector3.one * Mathf.Max(0.05f, heldSeedScale);
                break;
            case ApplyTool.Water:
                t.localPosition = heldWaterCanLocalPosition;
                t.localRotation = Quaternion.Euler(heldWaterCanLocalEuler);
                t.localScale = Vector3.one * Mathf.Max(0.05f, heldWaterCanScale);
                break;
            case ApplyTool.Fire:
                t.localPosition = heldFireLocalPosition;
                t.localRotation = Quaternion.Euler(heldFireLocalEuler);
                t.localScale = Vector3.one * Mathf.Max(0.05f, heldFireScale);
                break;
            case ApplyTool.Plant:
                t.localPosition = heldPlantLocalPosition;
                t.localRotation = Quaternion.Euler(heldPlantLocalEuler);
                t.localScale = Vector3.one * Mathf.Max(0.05f, heldPlantScale);
                break;
            case ApplyTool.Stone:
                t.localPosition = heldStoneLocalPosition;
                t.localRotation = Quaternion.Euler(heldStoneLocalEuler);
                t.localScale = Vector3.one * Mathf.Max(0.05f, heldStoneScale);
                break;
        }

        SetHeldToolVisible(true);
    }

    bool EnsureHeldToolInstance(GameObject desiredPrefab)
    {
        if (_heldToolInstance != null && _heldToolSourcePrefab == desiredPrefab)
            return true;

        if (_heldToolInstance != null)
            Destroy(_heldToolInstance);

        if (desiredPrefab == null || cameraSource == null)
            return false;

        _heldToolInstance = Instantiate(desiredPrefab, cameraSource.transform, false);
        _heldToolInstance.name = $"Held{currentTool}";
        _heldToolSourcePrefab = desiredPrefab;
        SetLayerRecursive(_heldToolInstance, 2);
        StripHeldToolComponents(_heldToolInstance);
        if (currentTool == ApplyTool.Water)
            TintHeldWaterCan(_heldToolInstance);
        TryPlayHeldToolAnimation(desiredPrefab, _heldToolInstance);
        return true;
    }

    GameObject ResolveHeldToolPrefab()
    {
        switch (currentTool)
        {
            case ApplyTool.Seed:
                return ResolveHeldSeedPrefab();
            case ApplyTool.Water:
                return ResolveHeldWaterCanPrefab();
            case ApplyTool.Fire:
                return ResolveHeldFirePrefab();
            case ApplyTool.Plant:
                return ResolveHeldPlantPrefab();
            case ApplyTool.Stone:
                return ResolveHeldStonePrefab();
            default:
                return null;
        }
    }

    GameObject ResolveHeldSeedPrefab()
    {
        int selectedSeedVariant = Mathf.Max(0, GetSelectedSeedVariantIndex());

        if (_spawnPickups == null || !_spawnPickups.isActiveAndEnabled)
            _spawnPickups = FindFirstObjectByType<SpawnPickups>();
        if (_spawnPickups != null && _spawnPickups.seedPickupPrefabs != null && _spawnPickups.seedPickupPrefabs.Length > 0)
        {
            int clamped = Mathf.Clamp(selectedSeedVariant, 0, _spawnPickups.seedPickupPrefabs.Length - 1);
            var selected = _spawnPickups.seedPickupPrefabs[clamped];
            if (selected != null)
                return selected;

            for (int i = 0; i < _spawnPickups.seedPickupPrefabs.Length; i++)
            {
                if (_spawnPickups.seedPickupPrefabs[i] != null)
                    return _spawnPickups.seedPickupPrefabs[i];
            }
        }

        if (_spawnPickups != null && _spawnPickups.seedPickupPrefab != null)
            return _spawnPickups.seedPickupPrefab;

        return heldSeedPrefab;
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

    GameObject ResolveHeldFirePrefab()
    {
        if (heldFirePrefab != null)
            return heldFirePrefab;

        if (_spawnPickups == null || !_spawnPickups.isActiveAndEnabled)
            _spawnPickups = FindFirstObjectByType<SpawnPickups>();
        if (_spawnPickups != null)
        {
            if (_spawnPickups.firePickupPrefabs != null)
            {
                for (int i = 0; i < _spawnPickups.firePickupPrefabs.Length; i++)
                {
                    if (_spawnPickups.firePickupPrefabs[i] != null)
                    {
                        heldFirePrefab = _spawnPickups.firePickupPrefabs[i];
                        return heldFirePrefab;
                    }
                }
            }

            if (_spawnPickups.firePickupPrefab != null)
            {
                heldFirePrefab = _spawnPickups.firePickupPrefab;
                return heldFirePrefab;
            }
        }

        return heldFirePrefab;
    }

    GameObject ResolveHeldPlantPrefab()
    {
        if (_plantRenderer == null)
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        if (_plantRenderer == null)
            return heldPlantPrefab;

        var selected = _plantRenderer.GetSelectedFlowerVariantPrefab(SCoL.PlantStage.MediumTree);
        if (selected != null)
            return selected;
        selected = _plantRenderer.GetSelectedFlowerVariantPrefab(SCoL.PlantStage.SmallTree);
        if (selected != null)
            return selected;
        selected = _plantRenderer.GetSelectedFlowerVariantPrefab(SCoL.PlantStage.SmallPlant);
        if (selected != null)
            return selected;
        return heldPlantPrefab;
    }

    GameObject ResolveHeldStonePrefab()
    {
        if (heldStonePrefab != null)
            return heldStonePrefab;

        if (_spawnPickups == null || !_spawnPickups.isActiveAndEnabled)
            _spawnPickups = FindFirstObjectByType<SpawnPickups>();
        if (_spawnPickups != null && _spawnPickups.stonePickupPrefabs != null)
        {
            for (int i = 0; i < _spawnPickups.stonePickupPrefabs.Length; i++)
            {
                if (_spawnPickups.stonePickupPrefabs[i] != null)
                {
                    heldStonePrefab = _spawnPickups.stonePickupPrefabs[i];
                    return heldStonePrefab;
                }
            }
        }

        if (stoneProjectilePrefabs != null)
        {
            for (int i = 0; i < stoneProjectilePrefabs.Length; i++)
            {
                if (stoneProjectilePrefabs[i] != null)
                {
                    heldStonePrefab = stoneProjectilePrefabs[i];
                    return heldStonePrefab;
                }
            }
        }

        return heldStonePrefab;
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

    void SetHeldToolVisible(bool visible)
    {
        if (_heldToolInstance != null && _heldToolInstance.activeSelf != visible)
            _heldToolInstance.SetActive(visible);
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

    bool TryCycleOwnedSeedVariant(int direction = +1)
    {
        if (_inventory == null)
            _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
        if (_plantRenderer == null)
            _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
        if (_inventory == null || _plantRenderer == null)
            return TryCycleSeedFlowerVariant();

        int current = GetSelectedSeedVariantIndex();
        int variantCount = _inventory.GetSeedTypeVariantCount();
        int dir = direction >= 0 ? 1 : -1;
        for (int step = 1; step <= variantCount; step++)
        {
            int candidate = (current + step * dir) % variantCount;
            if (candidate < 0)
                candidate += variantCount;
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
        DayNightLightingController.PlayInteractionSfx(DayNightLightingController.InteractionSfx.ThrowStone);

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

    static void FitEffectToTargetHeight(GameObject go, float targetHeight)
    {
        if (go == null)
            return;

        var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
        if (renderers == null || renderers.Length == 0)
            return;

        bool hasBounds = false;
        Bounds bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null)
                continue;

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

        if (!hasBounds)
            return;

        float currentHeight = Mathf.Max(0.0001f, bounds.size.y);
        float scale = Mathf.Max(0.01f, targetHeight) / currentHeight;
        go.transform.localScale *= scale;
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

    void SpawnTransientEffect(GameObject prefab, Vector3 position, Quaternion rotation, float lifetime)
    {
        if (prefab == null)
            return;

        var go = Instantiate(prefab, position, rotation);
        SoftenSpawnedParticleEffect(go, new Color(1f, 0.65f, 0.9f, 1f));
        Destroy(go, Mathf.Max(0.1f, lifetime));
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

    System.Collections.IEnumerator SpawnSingleWaterEffect(Vector3 hitPoint, Vector3 hitNormal)
    {
        int count = Mathf.Max(1, waterSingleEffectCount);
        var spawned = new System.Collections.Generic.List<GameObject>(count);
        for (int i = 0; i < count; i++)
        {
            Vector2 offset2 = Random.insideUnitCircle * Mathf.Max(0f, waterSingleEffectScatterRadius);
            Vector3 offset = new Vector3(offset2.x, 0f, offset2.y);
            bool applyGameplay = i == 0;
            SpawnBlock(hitPoint + offset, GetWaterSpreadMat(), spawned, burnTargets: false, waterTargets: true, applyGameplayEffects: applyGameplay);
        }

        float linger = Mathf.Max(0.05f, waterEffectLifetimeSeconds);
        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] == null)
                continue;
            var alembic = spawned[i].GetComponentInChildren<AlembicModelPlayer>(includeInactive: true);
            if (alembic != null)
                linger = Mathf.Max(linger, alembic.EstimatedPlaybackSeconds + 0.05f);
        }
        yield return new WaitForSeconds(linger);

        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null)
                Destroy(spawned[i]);
        }
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

    void SpawnBlock(Vector3 pos, Material mat, System.Collections.Generic.List<GameObject> sink, bool burnTargets, bool waterTargets, bool applyGameplayEffects = true)
    {
        if (waterTargets && waterSpreadJitter > 0f)
        {
            float jitter = Mathf.Clamp(waterSpreadJitter, 0f, 0.5f) * spreadCellSize;
            pos.x += Random.Range(-jitter, jitter);
            pos.z += Random.Range(-jitter, jitter);
        }

        Vector3 surfacePos = ProjectToSurface(pos, out Vector3 surfaceNormal);

        if (burnTargets && applyGameplayEffects)
        {
            float burnRadius = Mathf.Max(0.05f, blockScale * Mathf.Clamp(fireBurnRadiusScale, 0.05f, 1f));
            BurnTargetsAtTile(surfacePos, burnRadius);
            FireAffectSeedGrowthAtTile(surfacePos, burnRadius);
        }
        else if (waterTargets && applyGameplayEffects)
        {
            WaterAffectSeedGrowthAtTile(surfacePos, Mathf.Max(0.05f, blockScale * 0.55f));
        }

        var firePrefab = burnTargets ? PickFirePlacePrefab() : null;
        var waterPrefab = waterTargets ? PickWaterPlacePrefab() : null;
        if (suppressTempSpreadVisualsOnLowPoly && IsLowPolyTerrainVisualActive() && firePrefab == null && waterPrefab == null)
            return;

        string firePrefabName = firePrefab != null ? firePrefab.name : string.Empty;
        string waterPrefabName = waterPrefab != null ? waterPrefab.name : string.Empty;
        var go = firePrefab != null
            ? Instantiate(firePrefab)
            : (waterPrefab != null
                ? new GameObject()
                : GameObject.CreatePrimitive(useSoftDecalSpreadVisuals ? PrimitiveType.Quad : (waterTargets ? PrimitiveType.Sphere : PrimitiveType.Cube)));
        string effectPrefabName = firePrefab != null ? firePrefabName : waterPrefabName;
        go.name = currentTool == ApplyTool.Water
            ? (waterPrefab != null ? $"{waterPrefabName}_WaterTempBlock" : "WaterTempBlock")
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
        else if (waterPrefab != null)
        {
            var waterVisual = Instantiate(waterPrefab, go.transform);
            float scale = Mathf.Max(0.1f, waterPlacePrefabScale * blockScale);
            Vector3 waterOffset = waterPlacePositionOffset;
            waterOffset.y += Mathf.Max(0f, waterPlaceSpawnHeight);
            go.transform.position = surfacePos + waterOffset;
            go.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Quaternion.Euler(waterPlaceEulerOffset);
            waterVisual.transform.localRotation = Quaternion.identity;
            waterVisual.transform.localPosition = Vector3.zero;
            waterVisual.transform.localScale = Vector3.one;
            go.transform.localScale = go.transform.localScale * scale;
            FitEffectToTargetHeight(waterVisual, Mathf.Max(0.1f, waterPlaceTargetHeight));
            waterVisual.transform.localScale *= Mathf.Max(1f, waterPlacePrefabSizeMultiplier);
            waterVisual.transform.localScale = Vector3.Scale(waterVisual.transform.localScale, new Vector3(
                Mathf.Max(0.01f, waterPlaceScaleMultiplier.x),
                Mathf.Max(0.01f, waterPlaceScaleMultiplier.y),
                Mathf.Max(0.01f, waterPlaceScaleMultiplier.z)));
            // Flip vertically so the motion reads as descending instead of ascending.
            var flipped = waterVisual.transform.localScale;
            flipped.y *= -1f;
            waterVisual.transform.localScale = flipped;
            TintWaterEffectBlue(waterVisual);
            TryPlayModelAnimation(waterVisual, effectPrefabName, waterTargets: true);
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
        if (firePrefab == null && waterPrefab == null && r != null && mat != null)
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

        TryPlayModelAnimation(fireGO, sourcePrefabName, waterTargets: false);
    }

    void TryPlayModelAnimation(GameObject fxGO, string sourcePrefabName, bool waterTargets)
    {
        if (fxGO == null)
            return;
        if (waterTargets && !autoPlayWaterModelAnimation)
            return;

        // If prefab already has a configured animator controller, let it run naturally.
        var animator = fxGO.GetComponentInChildren<Animator>(includeInactive: true);
        if (animator != null && animator.runtimeAnimatorController != null)
            return;

        if (TryPlayAlembicModel(fxGO, waterTargets))
            return;

        var clip = waterTargets
            ? PickWaterAnimationClipForModel(sourcePrefabName)
            : PickFireAnimationClipForModel(sourcePrefabName);
        if (clip == null)
            return;

        var player = fxGO.GetComponent<FireModelClipPlayer>();
        if (player == null)
            player = fxGO.AddComponent<FireModelClipPlayer>();
        player.Play(clip, !waterTargets, waterTargets ? waterAnimationSpeed : 1f);
    }

    bool TryPlayAlembicModel(GameObject fxGO, bool waterTargets)
    {
        if (fxGO == null || !waterTargets)
            return false;

        var alembicPlayer = FindComponentByTypeName(fxGO, "UnityEngine.Formats.Alembic.Importer.AlembicStreamPlayer");
        if (alembicPlayer == null)
            return false;

        var player = fxGO.GetComponent<AlembicModelPlayer>();
        if (player == null)
            player = fxGO.AddComponent<AlembicModelPlayer>();
        player.Bind(alembicPlayer, Mathf.Max(0.01f, waterAnimationSpeed), loop: false, reverse: true);
        return true;
    }

    static Component FindComponentByTypeName(GameObject root, string fullTypeName)
    {
        if (root == null || string.IsNullOrEmpty(fullTypeName))
            return null;

        var components = root.GetComponentsInChildren<Component>(includeInactive: true);
        for (int i = 0; i < components.Length; i++)
        {
            var c = components[i];
            if (c == null)
                continue;
            var t = c.GetType();
            if (t != null && t.FullName == fullTypeName)
                return c;
        }
        return null;
    }

    void TintWaterEffectBlue(GameObject root)
    {
        if (root == null)
            return;

        var renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null)
                continue;

            var mats = r.materials;
            for (int j = 0; j < mats.Length; j++)
            {
                var mat = mats[j];
                if (mat == null)
                    continue;

                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", waterSpreadColor);
                if (mat.HasProperty("_Color"))
                    mat.SetColor("_Color", waterSpreadColor);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", waterSpreadColor * 0.65f);
                }
            }
        }
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

    AnimationClip PickWaterAnimationClipForModel(string sourcePrefabName)
    {
        if (!string.IsNullOrEmpty(sourcePrefabName))
        {
            string n = sourcePrefabName.ToLowerInvariant();
            if (n.Contains("waterv2") || n.Contains("waterv2"))
            {
                var direct = PickAnyClip(waterPlaceAnimationClips);
                if (direct != null) return direct;
            }
        }

        return PickAnyClip(waterPlaceAnimationClips);
    }

    void TryPlayHeldToolAnimation(GameObject sourcePrefab, GameObject heldInstance)
    {
        if (heldInstance == null)
            return;

        switch (currentTool)
        {
            case ApplyTool.Fire:
                TryPlayFireModelAnimation(heldInstance, ResolveHeldAnimationSourceName(sourcePrefab, heldInstance, "groundfirev2", "groundfirev1", "firev2", "firev1"));
                break;
            case ApplyTool.Water:
                TryPlayModelAnimation(heldInstance, ResolveHeldAnimationSourceName(sourcePrefab, heldInstance, "waterv2", "water"), waterTargets: true);
                break;
        }
    }

    static string ResolveHeldAnimationSourceName(GameObject sourcePrefab, GameObject heldInstance, params string[] preferredNames)
    {
        if (heldInstance != null)
        {
            var transforms = heldInstance.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < transforms.Length; i++)
            {
                var t = transforms[i];
                if (t == null)
                    continue;

                string lower = t.name.ToLowerInvariant();
                for (int j = 0; j < preferredNames.Length; j++)
                {
                    if (!string.IsNullOrEmpty(preferredNames[j]) && lower.Contains(preferredNames[j]))
                        return t.name;
                }
            }
        }

        return sourcePrefab != null ? sourcePrefab.name : string.Empty;
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

    GameObject PickWaterPlacePrefab()
    {
        if (waterPlacePrefabs == null || waterPlacePrefabs.Length == 0)
            return null;

        int valid = 0;
        for (int i = 0; i < waterPlacePrefabs.Length; i++)
            if (waterPlacePrefabs[i] != null) valid++;
        if (valid == 0)
            return null;

        int pick = Random.Range(0, valid);
        int seen = 0;
        for (int i = 0; i < waterPlacePrefabs.Length; i++)
        {
            var p = waterPlacePrefabs[i];
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

        public void Play(AnimationClip clip, bool loop, float speed = 1f)
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
            playable.SetSpeed(Mathf.Max(0.01f, speed));
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

    sealed class AlembicModelPlayer : MonoBehaviour
    {
        Component _streamPlayer;
        System.Reflection.PropertyInfo _currentTime;
        System.Reflection.PropertyInfo _duration;
        float _speed = 1f;
        bool _loop;
        bool _reverse;
        bool _bound;
        float _durationSeconds;

        public float EstimatedPlaybackSeconds => _durationSeconds > 0f ? _durationSeconds / Mathf.Max(0.01f, _speed) : 0f;

        public void Bind(Component streamPlayer, float speed, bool loop, bool reverse)
        {
            _streamPlayer = streamPlayer;
            _speed = Mathf.Max(0.01f, speed);
            _loop = loop;
            _reverse = reverse;
            _bound = false;
            _durationSeconds = 0f;
            if (_streamPlayer == null)
                return;

            var type = _streamPlayer.GetType();
            _currentTime = type.GetProperty("CurrentTime");
            _duration = type.GetProperty("Duration");
            if (_currentTime == null || _duration == null || !_currentTime.CanRead || !_currentTime.CanWrite || !_duration.CanRead)
                return;

            _durationSeconds = ReadFloat(_streamPlayer, _duration);
            _currentTime.SetValue(_streamPlayer, _reverse ? _durationSeconds : 0f);
            _bound = true;
        }

        void Update()
        {
            if (!_bound || _streamPlayer == null)
                return;

            float duration = ReadFloat(_streamPlayer, _duration);
            if (duration <= 0.0001f)
                return;

            float t = ReadFloat(_streamPlayer, _currentTime) + (_reverse ? -1f : 1f) * Time.deltaTime * _speed;
            if (_loop)
            {
                while (t < 0f) t += duration;
                while (t > duration) t -= duration;
            }
            else if (_reverse)
            {
                if (t < 0f)
                    t = 0f;
            }
            else if (t > duration)
            {
                t = duration;
            }

            _currentTime.SetValue(_streamPlayer, t);
        }

        static float ReadFloat(Component target, System.Reflection.PropertyInfo property)
        {
            if (target == null || property == null)
                return 0f;
            object value = property.GetValue(target);
            return value is float f ? f : 0f;
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
