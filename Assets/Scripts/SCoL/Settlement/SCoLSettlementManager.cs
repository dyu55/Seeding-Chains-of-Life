using System.Text;
using UnityEngine;
using SCoL.Inventory;
using SCoL.Voxels;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.Settlement
{
    public enum SCoLSettlementInteractableKind
    {
        Centerpiece = 0,
        Storage = 1,
        Barrier = 2
    }

    [DisallowMultipleComponent]
    public sealed class SCoLSettlementInteractable : MonoBehaviour
    {
        public SCoLSettlementInteractableKind kind;
        public SCoLSettlementManager manager;
    }

    [DisallowMultipleComponent]
    public sealed class SCoLSettlementManager : MonoBehaviour
    {
        public static SCoLSettlementManager Instance { get; private set; }

        [Header("Runtime Placeholder Setup")]
        public bool createRuntimePlaceholders = true;
        public bool startActivated = true;
        [Min(2f)] public float spawnAheadDistance = 8f;
        [Min(2f)] public float safeZoneRadius = 8.5f;
        [Min(0.5f)] public float slowZonePadding = 2.5f;

        [Header("Placeholder Layout")]
        [Min(2f)] public float fenceHalfExtent = 12f;
        [Min(1f)] public float fenceHeight = 1.6f;
        [Min(0.1f)] public float fenceThickness = 0.35f;
        [Min(1f)] public float frontGateWidth = 4.8f;
        [Min(1f)] public float storageSideOffset = 3.5f;
        [Min(0.2f)] public float fenceSegmentLength = 4.8f;
        [Min(0.2f)] public float fenceVisualHeight = 1.35f;
        [Min(0.05f)] public float fenceVisualThickness = 0.2f;
        [Min(0f)] public float groundPadSize = 0f;
        [Min(0.05f)] public float groundPadHeight = 0.3f;
        [Min(0f)] public float groundPadLift = 0.02f;

        [Header("Centerpiece Model")]
        public GameObject centerpiecePrefab;
        [Min(0.5f)] public float centerpieceTargetFootprint = 6.2f;
        [Min(0.5f)] public float centerpieceTargetHeight = 3.8f;
        public Vector3 centerpieceModelEuler = Vector3.zero;
        [Min(2f)] public float maxDistanceFromPlayerBeforeRecentering = 18f;

        [Header("Settlement Props")]
        public GameObject storagePrefab;
        public GameObject storageOpenPrefab;
        public GameObject fenceStraightPrefab;
        public GameObject fenceCornerPrefab;
        public GameObject fenceGatePrefab;
        public bool spawnPerimeterFences = true;
        [Min(0.5f)] public float storageTargetHeight = 1.4f;
        [Min(0.5f)] public float fenceTargetHeight = 1.9f;
        [Min(0.1f)] public float storageOpenHoldSeconds = 0.8f;

        [Header("Storage Transfer")]
        [Min(1)] public int seedTransferAmount = 20;
        [Min(1)] public int waterTransferAmount = 5;
        [Min(1)] public int fireTransferAmount = 2;
        [Min(1)] public int plantTransferAmount = 5;
        [Min(1)] public int stoneTransferAmount = 10;

        [Header("Progression")]
        [Min(1)] public int level2FlowerRequirement = 6;
        [Min(1)] public int level2StoredItemsRequirement = 20;
        [Min(1)] public int level3FlowerRequirement = 12;
        [Min(1)] public int level3AnimalRequirement = 3;
        [Min(1)] public int level4FlowerRequirement = 20;
        [Min(1)] public int level4AnimalRequirement = 5;
        [Min(1f)] public float level4SurvivalSeconds = 120f;

        SCoLRuntime _runtime;
        VoxelWorld _voxelWorld;
        SCoLCampsiteSceneAnchor _sceneAnchor;
        Transform _playerRoot;
        Vector3 _centerPosition;
        Vector3 _forward = Vector3.forward;
        bool _hasPlacedRuntimeObjects;
        bool _isActivated;
        bool _playerInsideSafeZone;
        bool _didInitialRecentering;
        int _currentLevel;
        int _flowerCount;
        int _nearbyAnimalCount;
        float _activatedAt = -1f;
        float _nextRefreshAt;
        string _statusLine = "Settlement dormant";
        string _goalLine = "Find and activate the centerpiece";
        string _storageSummary = "Storage offline";
        readonly int[] _storedSeedVariants = new int[8];
        int _storedWater;
        int _storedFire;
        int _storedPlants;
        int _storedStones;

        GameObject _centerpieceRoot;
        GameObject _centerpieceVisualRoot;
        GameObject _safeZoneRingRoot;
        GameObject _groundPadRoot;
        GameObject _storageRoot;
        GameObject _storageVisualRoot;
        bool _storageIsOpen;
        float _storageOpenUntil = -1f;
        readonly System.Collections.Generic.List<GameObject> _barrierRoots = new System.Collections.Generic.List<GameObject>(8);
        readonly StringBuilder _sb = new StringBuilder(192);

        public bool IsActivated => _isActivated;
        public bool PlayerInsideSafeZone => _playerInsideSafeZone;
        public int CurrentLevel => _currentLevel;
        public int FlowerCount => _flowerCount;
        public int NearbyAnimalCount => _nearbyAnimalCount;
        public Vector3 CenterPosition => _centerPosition;
        public float SafeZoneRadius => Mathf.Max(0.1f, safeZoneRadius);
        public float SlowZonePadding => Mathf.Max(0.1f, slowZonePadding);
        public string StatusLine => _statusLine;
        public string GoalLine => _goalLine;
        public string StorageSummary => _storageSummary;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            FindReferences();
            AutoAssignSettlementPrefabs();

            if (startActivated || createRuntimePlaceholders)
            {
                _isActivated = true;
                _activatedAt = Time.time;
            }

            EnsurePlaceholders();
            RefreshSettlementState(force: true);
        }

        void Update()
        {
            FindReferences();
            EnsurePlaceholders();
            UpdateStorageVisualState();

            if (Time.time >= _nextRefreshAt)
                RefreshSettlementState(force: false);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        void AutoAssignSettlementPrefabs()
        {
            AutoAssignCenterpiecePrefab();
            AutoAssignStoragePrefab();
            AutoAssignFencePrefabs();
        }

        void AutoAssignCenterpiecePrefab()
        {
            if (centerpiecePrefab != null)
                return;

#if UNITY_EDITOR
            centerpiecePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/tent updated/tent updated.obj");
            if (centerpiecePrefab != null)
                return;

            centerpiecePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/Campsite/tentV1.obj");
            if (centerpiecePrefab != null)
                return;

            centerpiecePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/Campsite/tentV2.obj");
            if (centerpiecePrefab != null)
                return;
#endif

            centerpiecePrefab = Resources.Load<GameObject>("Campsite/tentV2");

            if (centerpiecePrefab == null)
                Debug.LogWarning("[SCoLSettlementManager] Failed to load campsite centerpiece prefab.");
        }

        void AutoAssignStoragePrefab()
        {
            if (storagePrefab != null)
                goto assign_open;

#if UNITY_EDITOR
            storagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/chest closed/chest closed.obj");
            if (storagePrefab != null)
                goto assign_open;

            storagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/Campsite/ChestV1.obj");
            if (storagePrefab != null)
                goto assign_open;

            storagePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/Crate/Crate.obj");
#endif

assign_open:
#if UNITY_EDITOR
            if (storageOpenPrefab == null)
                storageOpenPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/Chest open/Chest open.obj");
#endif
        }

        void AutoAssignFencePrefabs()
        {
            bool needsAny = fenceStraightPrefab == null || fenceCornerPrefab == null || fenceGatePrefab == null;
            if (!needsAny)
                return;

#if UNITY_EDITOR
            if (fenceStraightPrefab == null)
                fenceStraightPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/fence alone/fence alone.obj");
            if (fenceCornerPrefab == null)
                fenceCornerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/fence cornner/fence cornner.obj");
            if (fenceGatePrefab == null)
                fenceGatePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/fence gate/fence gate.obj");
#endif
        }

        public bool IsInsideSafeZone(Vector3 worldPosition)
        {
            if (!_isActivated)
                return false;

            Vector3 delta = worldPosition - _centerPosition;
            delta.y = 0f;
            return delta.sqrMagnitude <= SafeZoneRadius * SafeZoneRadius;
        }

        public float DistanceToCenterXZ(Vector3 worldPosition)
        {
            Vector3 delta = worldPosition - _centerPosition;
            delta.y = 0f;
            return delta.magnitude;
        }

        public Vector3 GetSafeZoneRepelDirection(Vector3 worldPosition)
        {
            Vector3 away = worldPosition - _centerPosition;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = _forward.sqrMagnitude > 0.0001f ? -_forward : Vector3.back;
            return away.normalized;
        }

        public bool IsProtectedCombatTarget(Transform target)
        {
            return target != null && IsInsideSafeZone(target.position);
        }

        public bool TryActivate(out string message)
        {
            if (_isActivated)
            {
                message = $"Settlement active. Level {_currentLevel}.";
                return false;
            }

            _isActivated = true;
            _activatedAt = Time.time;
            RefreshSettlementVisuals();
            RefreshSettlementState(force: true);
            message = "Settlement activated. Wolves avoid the home zone.";
            return true;
        }

        public bool TryDepositCurrentTool(SCoLInventory inventory, FPSRaycastInteractor.ApplyTool tool, int seedVariantIndex, out string message)
        {
            message = "Nothing stored.";
            if (inventory == null)
                return false;
            if (!_isActivated)
            {
                message = "Activate the centerpiece first.";
                return false;
            }

            switch (tool)
            {
                case FPSRaycastInteractor.ApplyTool.Seed:
                {
                    int amount = Mathf.Min(Mathf.Max(1, seedTransferAmount), inventory.GetSeedTypeCount(seedVariantIndex));
                    if (amount <= 0)
                    {
                        message = "No matching seeds to store.";
                        return false;
                    }
                    if (!inventory.TryConsumeSeedType(seedVariantIndex, amount))
                        return false;
                    _storedSeedVariants[Mathf.Clamp(seedVariantIndex, 0, _storedSeedVariants.Length - 1)] += amount;
                    message = $"Stored {amount} {inventory.GetSeedTypeDisplayName(seedVariantIndex)} seed(s).";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Water:
                {
                    int amount = Mathf.Min(Mathf.Max(1, waterTransferAmount), inventory.water);
                    if (amount <= 0 || !inventory.TryConsume(SCoLItemType.Water, amount))
                    {
                        message = "No water to store.";
                        return false;
                    }
                    _storedWater += amount;
                    message = $"Stored {amount} water.";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Fire:
                {
                    int amount = Mathf.Min(Mathf.Max(1, fireTransferAmount), inventory.fire);
                    if (amount <= 0 || !inventory.TryConsume(SCoLItemType.Fire, amount))
                    {
                        message = "No fire stock to store.";
                        return false;
                    }
                    _storedFire += amount;
                    message = $"Stored {amount} fire.";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Plant:
                {
                    int amount = Mathf.Min(Mathf.Max(1, plantTransferAmount), inventory.plants);
                    if (amount <= 0 || !inventory.TryConsume(SCoLItemType.Plant, amount))
                    {
                        message = "No plant stock to store.";
                        return false;
                    }
                    _storedPlants += amount;
                    message = $"Stored {amount} plant feed.";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Stone:
                {
                    int amount = Mathf.Min(Mathf.Max(1, stoneTransferAmount), inventory.stones);
                    if (amount <= 0 || !inventory.TryConsume(SCoLItemType.Stone, amount))
                    {
                        message = "No stones to store.";
                        return false;
                    }
                    _storedStones += amount;
                    message = $"Stored {amount} stone(s).";
                    break;
                }
                default:
                    return false;
            }

            RefreshSettlementState(force: true);
            PulseStorageOpen();
            return true;
        }

        public bool TryWithdrawCurrentTool(SCoLInventory inventory, FPSRaycastInteractor.ApplyTool tool, int seedVariantIndex, out string message)
        {
            message = "Nothing withdrawn.";
            if (inventory == null)
                return false;
            if (!_isActivated)
            {
                message = "Activate the centerpiece first.";
                return false;
            }

            switch (tool)
            {
                case FPSRaycastInteractor.ApplyTool.Seed:
                {
                    int idx = Mathf.Clamp(seedVariantIndex, 0, _storedSeedVariants.Length - 1);
                    int amount = Mathf.Min(Mathf.Max(1, seedTransferAmount), _storedSeedVariants[idx]);
                    if (amount <= 0)
                    {
                        message = "No matching seeds in storage.";
                        return false;
                    }
                    _storedSeedVariants[idx] -= amount;
                    inventory.AddSeedType(idx, amount);
                    message = $"Withdrew {amount} {inventory.GetSeedTypeDisplayName(idx)} seed(s).";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Water:
                {
                    int amount = Mathf.Min(Mathf.Max(1, waterTransferAmount), _storedWater);
                    if (amount <= 0)
                    {
                        message = "No water in storage.";
                        return false;
                    }
                    _storedWater -= amount;
                    inventory.Add(SCoLItemType.Water, amount);
                    message = $"Withdrew {amount} water.";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Fire:
                {
                    int amount = Mathf.Min(Mathf.Max(1, fireTransferAmount), _storedFire);
                    if (amount <= 0)
                    {
                        message = "No fire stock in storage.";
                        return false;
                    }
                    _storedFire -= amount;
                    inventory.Add(SCoLItemType.Fire, amount);
                    message = $"Withdrew {amount} fire.";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Plant:
                {
                    int amount = Mathf.Min(Mathf.Max(1, plantTransferAmount), _storedPlants);
                    if (amount <= 0)
                    {
                        message = "No plant feed in storage.";
                        return false;
                    }
                    _storedPlants -= amount;
                    inventory.Add(SCoLItemType.Plant, amount);
                    message = $"Withdrew {amount} plant feed.";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Stone:
                {
                    int amount = Mathf.Min(Mathf.Max(1, stoneTransferAmount), _storedStones);
                    if (amount <= 0)
                    {
                        message = "No stones in storage.";
                        return false;
                    }
                    _storedStones -= amount;
                    inventory.Add(SCoLItemType.Stone, amount);
                    message = $"Withdrew {amount} stone(s).";
                    break;
                }
                default:
                    return false;
            }

            RefreshSettlementState(force: true);
            PulseStorageOpen();
            return true;
        }

        public void PulseStorageOpen(float holdSeconds = -1f)
        {
            float hold = holdSeconds > 0f ? holdSeconds : Mathf.Max(0.1f, storageOpenHoldSeconds);
            _storageOpenUntil = Mathf.Max(_storageOpenUntil, Time.time + hold);
            SetStorageVisualOpen(true);
        }

        public string GetCurrentToolStorageHint(FPSRaycastInteractor interactor, SCoLInventory inventory)
        {
            if (interactor == null)
                return "Manage storage";

            return interactor.currentTool switch
            {
                FPSRaycastInteractor.ApplyTool.Seed => inventory != null ? inventory.GetSeedTypeDisplayName(interactor.GetSelectedSeedVariantIndex()) + " seeds" : "selected seeds",
                FPSRaycastInteractor.ApplyTool.Water => "water",
                FPSRaycastInteractor.ApplyTool.Fire => "fire",
                FPSRaycastInteractor.ApplyTool.Plant => "plant feed",
                FPSRaycastInteractor.ApplyTool.Stone => "stones",
                _ => "supplies"
            };
        }

        public string GetDetailedStorageSummary()
        {
            _sb.Clear();
            _sb.Append("Seed ");
            _sb.Append(TotalStoredSeeds);
            _sb.Append("  Water ");
            _sb.Append(_storedWater);
            _sb.Append("  Fire ");
            _sb.Append(_storedFire);
            _sb.Append("  Plant ");
            _sb.Append(_storedPlants);
            _sb.Append("  Stone ");
            _sb.Append(_storedStones);
            return _sb.ToString();
        }

        int TotalStoredItems => TotalStoredSeeds + _storedWater + _storedFire + _storedPlants + _storedStones;
        int TotalStoredSeeds
        {
            get
            {
                int total = 0;
                for (int i = 0; i < _storedSeedVariants.Length; i++)
                    total += _storedSeedVariants[i];
                return total;
            }
        }

        void FindReferences()
        {
            if (_runtime == null || !_runtime.isActiveAndEnabled)
                _runtime = FindFirstObjectByType<SCoLRuntime>();
            if (_voxelWorld == null || !_voxelWorld.isActiveAndEnabled)
                _voxelWorld = FindFirstObjectByType<VoxelWorld>();
            if (_sceneAnchor == null)
                _sceneAnchor = FindFirstObjectByType<SCoLCampsiteSceneAnchor>();

            if (_playerRoot == null)
            {
                var controller = FindFirstObjectByType<SimpleFirstPersonController>();
                if (controller != null)
                    _playerRoot = controller.transform;
                else if (Camera.main != null)
                    _playerRoot = Camera.main.transform.root;
            }
        }

        void EnsurePlaceholders()
        {
            if (!createRuntimePlaceholders)
                return;

            if (_sceneAnchor == null)
                FindReferences();

            if (!spawnPerimeterFences && _barrierRoots.Count > 0)
                ClearBarrierRoots();

            Vector3 anchor = ResolveAnchorPosition();
            Vector3 planarForward = ResolveForward();
            if (planarForward.sqrMagnitude < 0.0001f)
                planarForward = Vector3.forward;

            bool layoutReady = _centerpieceRoot != null && _storageRoot != null && (!spawnPerimeterFences || _barrierRoots.Count > 0);
            if (_hasPlacedRuntimeObjects && layoutReady)
            {
                if (_sceneAnchor == null)
                    TryRecenterRuntimePlaceholders(anchor);
                else
                {
                    _centerPosition = anchor;
                    _forward = planarForward;
                    _centerpieceRoot.transform.position = anchor;
                }
                return;
            }

            _centerPosition = anchor;
            _forward = planarForward;

            if (_centerpieceRoot == null)
                _centerpieceRoot = _sceneAnchor != null
                    ? BindSceneAnchorCenterpiece(_sceneAnchor, anchor, planarForward)
                    : CreateCenterpiece(anchor, planarForward);
            if (_storageRoot == null)
                _storageRoot = CreateStorage(anchor, planarForward);
            if (spawnPerimeterFences && _barrierRoots.Count == 0)
                CreateDefensePlaceholders(anchor, planarForward);

            RefreshSettlementVisuals();
            _hasPlacedRuntimeObjects = true;
        }

        void TryRecenterRuntimePlaceholders(Vector3 desiredAnchor)
        {
            if (_didInitialRecentering || _isActivated || _playerRoot == null)
                return;

            Vector3 playerPlanar = _playerRoot.position;
            playerPlanar.y = 0f;
            Vector3 centerPlanar = _centerPosition;
            centerPlanar.y = 0f;
            float planarDistance = Vector3.Distance(playerPlanar, centerPlanar);
            if (planarDistance <= Mathf.Max(2f, maxDistanceFromPlayerBeforeRecentering))
                return;

            Vector3 delta = desiredAnchor - _centerPosition;
            if (delta.sqrMagnitude <= 0.01f)
                return;

            TranslateRuntimePlaceholders(delta);
            _centerPosition += delta;
            _didInitialRecentering = true;
        }

        void TranslateRuntimePlaceholders(Vector3 delta)
        {
            if (_centerpieceRoot != null)
                _centerpieceRoot.transform.position += delta;
            if (_storageRoot != null)
                _storageRoot.transform.position += delta;
            for (int i = 0; i < _barrierRoots.Count; i++)
            {
                if (_barrierRoots[i] != null)
                    _barrierRoots[i].transform.position += delta;
            }
        }

        void ClearBarrierRoots()
        {
            for (int i = 0; i < _barrierRoots.Count; i++)
            {
                var root = _barrierRoots[i];
                if (root == null)
                    continue;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(root);
                else
                    Destroy(root);
#else
                Destroy(root);
#endif
            }
            _barrierRoots.Clear();
        }

        Vector3 ResolveForward()
        {
            if (_playerRoot != null)
            {
                Vector3 forward = _playerRoot.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                    return forward.normalized;
            }

            if (Camera.main != null)
            {
                Vector3 forward = Camera.main.transform.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.0001f)
                    return forward.normalized;
            }

            return Vector3.forward;
        }

        Vector3 ResolveAnchorPosition()
        {
            if (TryResolveVoxelWorldCenter(out var worldCenter))
                return worldCenter;

            if (_sceneAnchor != null)
            {
                Vector3 anchored = _sceneAnchor.GetAnchorPosition(_voxelWorld);
                if (TryProjectToGround(anchored, out var groundedAnchor))
                    return groundedAnchor;
                return anchored;
            }

            Vector3 origin = _playerRoot != null ? _playerRoot.position : Vector3.zero;
            Vector3 forward = ResolveForward();
            Vector3 candidate = origin + forward * Mathf.Max(2f, spawnAheadDistance);

            if (TryProjectToGround(candidate, out var grounded))
                return grounded;

            if (TryProjectToGround(origin, out grounded))
                return grounded + forward * 2f;

            candidate.y = 0f;
            return candidate;
        }

        bool TryResolveVoxelWorldCenter(out Vector3 center)
        {
            center = default;
            if (_voxelWorld == null || _voxelWorld.Config == null)
                return false;

            int x = Mathf.Clamp(_voxelWorld.Config.worldWidth / 2, 0, _voxelWorld.Config.worldWidth - 1);
            int z = Mathf.Clamp(_voxelWorld.Config.worldDepth / 2, 0, _voxelWorld.Config.worldDepth - 1);
            Vector3 candidate = _voxelWorld.OriginWorld + new Vector3(x + 0.5f, 0f, z + 0.5f);
            if (TryProjectToGround(candidate, out center))
                return true;

            center = candidate;
            return true;
        }

        GameObject BindSceneAnchorCenterpiece(SCoLCampsiteSceneAnchor anchor, Vector3 position, Vector3 forward)
        {
            if (anchor == null)
                return CreateCenterpiece(position, forward);

            var root = anchor.gameObject;
            root.name = "SettlementCenterpiece";
            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
            anchor.EnsureVisualNow();

            var marker = root.GetComponent<SCoLSettlementInteractable>();
            if (marker == null)
                marker = root.AddComponent<SCoLSettlementInteractable>();
            marker.kind = SCoLSettlementInteractableKind.Centerpiece;
            marker.manager = this;

            _centerpieceVisualRoot = anchor.GetVisualRoot();
            if (_centerpieceVisualRoot != null)
                EnsureCenterpieceCollider(root, _centerpieceVisualRoot);

            _groundPadRoot = EnsureGroundPad(root.transform);
            _safeZoneRingRoot = root.transform.Find("SafeZoneRing") != null
                ? root.transform.Find("SafeZoneRing").gameObject
                : CreateSafeZoneRing(root.transform);
            return root;
        }

        bool TryProjectToGround(Vector3 world, out Vector3 grounded)
        {
            grounded = world;

            if (_voxelWorld != null && _voxelWorld.TryGetTerrainSurfaceYAtWorld(world + Vector3.up * 4f, out float surfaceY, includeWaterSurface: false))
            {
                grounded = new Vector3(world.x, surfaceY + 0.05f, world.z);
                if (!_voxelWorld.IsWaterColumnAtWorld(grounded))
                    return true;
            }

            for (int i = 0; i < 10; i++)
            {
                float angle = i * 36f;
                float radius = 1.5f + 0.8f * i;
                Vector3 offset = Quaternion.Euler(0f, angle, 0f) * Vector3.forward * radius;
                Vector3 probe = world + offset;
                if (_voxelWorld != null && _voxelWorld.TryGetTerrainSurfaceYAtWorld(probe + Vector3.up * 4f, out surfaceY, includeWaterSurface: false))
                {
                    grounded = new Vector3(probe.x, surfaceY + 0.05f, probe.z);
                    if (!_voxelWorld.IsWaterColumnAtWorld(grounded))
                        return true;
                }
            }

            return false;
        }

        GameObject CreateCenterpiece(Vector3 position, Vector3 forward)
        {
            var root = new GameObject("SettlementCenterpiece");
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);

            var marker = root.AddComponent<SCoLSettlementInteractable>();
            marker.kind = SCoLSettlementInteractableKind.Centerpiece;
            marker.manager = this;

            if (centerpiecePrefab != null)
            {
                _centerpieceVisualRoot = Instantiate(centerpiecePrefab, root.transform, false);
                _centerpieceVisualRoot.name = "CenterpieceVisual";
                _centerpieceVisualRoot.transform.localRotation = Quaternion.Euler(centerpieceModelEuler);
                NormalizeCenterpieceVisual(root, _centerpieceVisualRoot);
                EnsureCenterpieceCollider(root, _centerpieceVisualRoot);
            }
            else
            {
                var baseObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                baseObj.name = "Base";
                baseObj.transform.SetParent(root.transform, false);
                baseObj.transform.localPosition = new Vector3(0f, 0.35f, 0f);
                baseObj.transform.localScale = new Vector3(1.35f, 0.35f, 1.35f);
                ConfigureRenderer(baseObj, new Color(0.22f, 0.18f, 0.12f, 1f));
            }

            if (centerpiecePrefab == null)
            {
                var coreObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                coreObj.name = "Core";
                coreObj.transform.SetParent(root.transform, false);
                coreObj.transform.localPosition = new Vector3(0f, 1.35f, 0f);
                coreObj.transform.localScale = new Vector3(0.72f, 0.72f, 0.72f);

                var ringObj = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ringObj.name = "Halo";
                ringObj.transform.SetParent(root.transform, false);
                ringObj.transform.localPosition = new Vector3(0f, 0.05f, 0f);
                ringObj.transform.localScale = new Vector3(SafeZoneRadius * 2f, 0.01f, SafeZoneRadius * 2f);
                var ringCollider = ringObj.GetComponent<Collider>();
                if (ringCollider != null)
                    Destroy(ringCollider);

                ConfigureRenderer(coreObj, new Color(0.30f, 0.70f, 0.92f, 1f));
            }

            _groundPadRoot = EnsureGroundPad(root.transform);
            _safeZoneRingRoot = CreateSafeZoneRing(root.transform);

            return root;
        }

        GameObject EnsureGroundPad(Transform parent)
        {
            if (parent == null)
                return null;

            if (groundPadSize <= 0.01f)
            {
                var existingPad = parent.Find("SettlementGroundPad");
                if (existingPad != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        DestroyImmediate(existingPad.gameObject);
                    else
                        Destroy(existingPad.gameObject);
#else
                    Destroy(existingPad.gameObject);
#endif
                }
                return null;
            }

            var existing = parent.Find("SettlementGroundPad");
            if (existing != null)
            {
                ConfigureGroundPad(existing.gameObject);
                return existing.gameObject;
            }

            var pad = GameObject.CreatePrimitive(PrimitiveType.Cube);
            pad.name = "SettlementGroundPad";
            pad.transform.SetParent(parent, false);
            ConfigureGroundPad(pad);
            return pad;
        }

        void ConfigureGroundPad(GameObject pad)
        {
            if (pad == null)
                return;

            float size = Mathf.Max(2f, groundPadSize);
            float height = Mathf.Max(0.05f, groundPadHeight);
            pad.transform.localPosition = new Vector3(0f, -height * 0.5f + groundPadLift, 0f);
            pad.transform.localRotation = Quaternion.identity;
            pad.transform.localScale = new Vector3(size, height, size);
            ConfigureRenderer(pad, new Color(0.36f, 0.29f, 0.18f, 1f));
        }

        GameObject CreateSafeZoneRing(Transform parent)
        {
            var ringRoot = new GameObject("SafeZoneRing");
            ringRoot.transform.SetParent(parent, false);
            ringRoot.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            ringRoot.transform.localRotation = Quaternion.identity;

            var line = ringRoot.AddComponent<LineRenderer>();
            line.loop = true;
            line.useWorldSpace = false;
            line.alignment = LineAlignment.View;
            line.widthMultiplier = 0.18f;
            line.positionCount = 48;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.textureMode = LineTextureMode.Stretch;
            line.numCapVertices = 4;
            line.numCornerVertices = 4;

            var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var material = new Material(shader);
            line.sharedMaterial = material;

            float radius = Mathf.Max(0.5f, SafeZoneRadius);
            for (int i = 0; i < line.positionCount; i++)
            {
                float t = i / (float)line.positionCount;
                float angle = t * Mathf.PI * 2f;
                line.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
            }

            return ringRoot;
        }

        void NormalizeCenterpieceVisual(GameObject root, GameObject visual)
        {
            if (root == null || visual == null || !TryGetHierarchyBounds(visual, out var bounds))
                return;

            float footprint = Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
            float height = Mathf.Max(0.01f, bounds.size.y);
            float scaleByFootprint = Mathf.Max(0.01f, centerpieceTargetFootprint) / footprint;
            float scaleByHeight = Mathf.Max(0.01f, centerpieceTargetHeight) / height;
            float uniformScale = Mathf.Min(scaleByFootprint, scaleByHeight);
            visual.transform.localScale *= uniformScale;

            if (!TryGetHierarchyBounds(visual, out bounds))
                return;

            Vector3 desiredBottomCenter = root.transform.position;
            Vector3 currentBottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            visual.transform.position += desiredBottomCenter - currentBottomCenter;
        }

        void EnsureCenterpieceCollider(GameObject root, GameObject visual)
        {
            if (root == null || visual == null || !TryGetHierarchyBounds(visual, out var bounds))
                return;

            var collider = root.GetComponent<BoxCollider>();
            if (collider == null)
                collider = root.AddComponent<BoxCollider>();

            Vector3 localCenter = root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.45f, bounds.center.z));
            collider.center = localCenter;
            collider.size = new Vector3(
                Mathf.Max(1.4f, bounds.size.x * 0.92f),
                Mathf.Max(1.8f, bounds.size.y * 0.9f),
                Mathf.Max(1.4f, bounds.size.z * 0.92f));
        }

        static bool TryGetHierarchyBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null)
                return false;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool hasBounds = false;
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

            return hasBounds;
        }

        GameObject CreateStorage(Vector3 center, Vector3 forward)
        {
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            Vector3 pos = center + right * Mathf.Max(1.5f, storageSideOffset);
            if (TryProjectToGround(pos, out var grounded))
                pos = grounded;

            var root = new GameObject("SettlementStorage");
            root.transform.SetParent(transform, false);
            root.transform.position = pos;
            root.transform.rotation = Quaternion.LookRotation(-forward, Vector3.up);

            var marker = root.AddComponent<SCoLSettlementInteractable>();
            marker.kind = SCoLSettlementInteractableKind.Storage;
            marker.manager = this;

            if (storagePrefab != null || storageOpenPrefab != null)
            {
                BuildStorageVisual(root, false);
            }
            else
            {
                var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
                box.name = "Crate";
                box.transform.SetParent(root.transform, false);
                box.transform.localPosition = new Vector3(0f, 0.55f, 0f);
                box.transform.localScale = new Vector3(1.2f, 1.0f, 1.0f);
                ConfigureRenderer(box, new Color(0.42f, 0.28f, 0.14f, 1f));

                var lid = GameObject.CreatePrimitive(PrimitiveType.Cube);
                lid.name = "Lid";
                lid.transform.SetParent(root.transform, false);
                lid.transform.localPosition = new Vector3(0f, 1.08f, 0f);
                lid.transform.localScale = new Vector3(1.28f, 0.16f, 1.08f);
                ConfigureRenderer(lid, new Color(0.66f, 0.50f, 0.24f, 1f));
            }

            return root;
        }

        void UpdateStorageVisualState()
        {
            if (_storageRoot == null)
                return;

            bool shouldBeOpen = _storageOpenUntil > 0f && Time.time <= _storageOpenUntil;
            if (_storageIsOpen != shouldBeOpen)
                SetStorageVisualOpen(shouldBeOpen);
        }

        void SetStorageVisualOpen(bool open)
        {
            if (_storageRoot == null)
                return;

            if (_storageVisualRoot == null)
            {
                BuildStorageVisual(_storageRoot, open);
                _storageIsOpen = open;
                return;
            }

            if (_storageIsOpen == open)
                return;

            GameObject desiredPrefab = open && storageOpenPrefab != null ? storageOpenPrefab : storagePrefab;
            if (desiredPrefab == null)
            {
                _storageIsOpen = open;
                return;
            }

            BuildStorageVisual(_storageRoot, open);
            _storageIsOpen = open;
            RefreshSettlementVisuals();
        }

        void BuildStorageVisual(GameObject root, bool open)
        {
            if (root == null)
                return;

            if (_storageVisualRoot != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_storageVisualRoot);
                else
                    Destroy(_storageVisualRoot);
#else
                Destroy(_storageVisualRoot);
#endif
                _storageVisualRoot = null;
            }

            GameObject prefab = open && storageOpenPrefab != null ? storageOpenPrefab : storagePrefab;
            if (prefab == null)
                return;

            var visual = Instantiate(prefab, root.transform, false);
            visual.name = "StorageVisual";
            NormalizeVisualToHeight(root, visual, Mathf.Max(0.5f, storageTargetHeight));
            EnsureCenterpieceCollider(root, visual);
            _storageVisualRoot = visual;
        }

        void CreateDefensePlaceholders(Vector3 center, Vector3 forward)
        {
            ClearBarrierRoots();
            ClearStrayBarrierObjects();

            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            float half = Mathf.Max(SafeZoneRadius + 1f, fenceHalfExtent);
            float gateWidth = Mathf.Clamp(frontGateWidth, 3.5f, half * 1.3f);
            float gateHalfWidth = Mathf.Clamp(gateWidth * 0.5f, 1.75f, half - 1.4f);
            Vector3 front = center - forward * half;
            Vector3 back = center + forward * half;
            Vector3 left = center - right * half;
            Vector3 rightSide = center + right * half;
            float cornerInset = Mathf.Clamp(Mathf.Max(0.9f, fenceSegmentLength * 0.22f), 0.9f, 1.6f);

            Vector3 frontLeft = front - right * half;
            Vector3 frontRight = front + right * half;
            Vector3 backLeft = back - right * half;
            Vector3 backRight = back + right * half;

            CreateBarrierCorner(frontLeft, Quaternion.LookRotation(-forward - right, Vector3.up));
            CreateBarrierCorner(frontRight, Quaternion.LookRotation(-forward + right, Vector3.up));
            CreateBarrierCorner(backLeft, Quaternion.LookRotation(forward - right, Vector3.up));
            CreateBarrierCorner(backRight, Quaternion.LookRotation(forward + right, Vector3.up));

            CreateFenceSide(
                backLeft + right * cornerInset,
                backRight - right * cornerInset,
                fenceStraightPrefab);
            CreateFenceSide(
                backLeft + forward * cornerInset,
                frontLeft - forward * cornerInset,
                fenceStraightPrefab);
            CreateFenceSide(
                frontRight - forward * cornerInset,
                backRight + forward * cornerInset,
                fenceStraightPrefab);
            CreateFenceSide(
                frontLeft + right * cornerInset,
                front - right * gateHalfWidth,
                fenceStraightPrefab);
            CreateFenceSide(
                front + right * gateHalfWidth,
                frontRight - right * cornerInset,
                fenceStraightPrefab);
            CreateBarrierGate(front, Quaternion.LookRotation(right, Vector3.up));
        }

        void ClearStrayBarrierObjects()
        {
            var roots = FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < roots.Length; i++)
            {
                var t = roots[i];
                if (t == null)
                    continue;

                string name = t.name;
                if (name != "SettlementBarrier" &&
                    name != "SettlementBarrierCorner" &&
                    name != "SettlementBarrierGate")
                    continue;

                if (t.parent != transform)
                    continue;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(t.gameObject);
                else
                    Destroy(t.gameObject);
#else
                Destroy(t.gameObject);
#endif
            }
        }

        void CreateFenceSide(Vector3 start, Vector3 end, GameObject prefab)
        {
            if ((end - start).sqrMagnitude < 0.25f)
                return;

            Vector3 delta = end - start;
            delta.y = 0f;
            float runLength = delta.magnitude;
            if (runLength < 0.5f)
                return;

            Vector3 dir = delta / runLength;
            Vector3 pos = (start + end) * 0.5f;
            CreateBarrierSegment(pos, Quaternion.LookRotation(dir, Vector3.up), runLength, prefab);
        }

        void CreateBarrierCorner(Vector3 position, Quaternion rotation)
        {
            var prefab = fenceCornerPrefab != null ? fenceCornerPrefab : fenceStraightPrefab;
            if (prefab == null)
                return;

            position = ProjectBarrierPosition(position);

            var root = new GameObject("SettlementBarrierCorner");
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            root.transform.rotation = rotation;

            var marker = root.AddComponent<SCoLSettlementInteractable>();
            marker.kind = SCoLSettlementInteractableKind.Barrier;
            marker.manager = this;

            var visual = Instantiate(prefab, root.transform, false);
            visual.name = "FenceCorner";
            AlignLinearVisualToForward(visual, false);
            NormalizeFenceCornerVisual(root, visual);
            EnsureCenterpieceCollider(root, visual);
            _barrierRoots.Add(root);
        }

        void CreateBarrierGate(Vector3 position, Quaternion rotation)
        {
            if (fenceGatePrefab == null)
                return;

            position = ProjectBarrierPosition(position);

            var root = new GameObject("SettlementBarrierGate");
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            root.transform.rotation = rotation;

            var visual = Instantiate(fenceGatePrefab, root.transform, false);
            visual.name = "FenceGate";
            AlignLinearVisualToForward(visual, true);
            NormalizeFenceGateVisual(root, visual, Mathf.Max(2f, frontGateWidth));
        }

        void CreateBarrierSegment(Vector3 position, Quaternion rotation, float length, GameObject prefab)
        {
            position = ProjectBarrierPosition(position);

            var root = new GameObject("SettlementBarrier");
            root.transform.SetParent(transform, false);
            root.transform.position = position;
            root.transform.rotation = rotation;

            var marker = root.AddComponent<SCoLSettlementInteractable>();
            marker.kind = SCoLSettlementInteractableKind.Barrier;
            marker.manager = this;

            if (prefab != null)
            {
                var visual = Instantiate(prefab, root.transform, false);
                visual.name = "FenceVisual";
                AlignLinearVisualToForward(visual, true);
                NormalizeFenceSegmentVisual(root, visual, Mathf.Max(0.5f, length));
                EnsureCenterpieceCollider(root, visual);
            }
            else
            {
                var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = "Barrier";
                cube.transform.SetParent(root.transform, false);
                cube.transform.localPosition = Vector3.zero;
                cube.transform.localScale = new Vector3(Mathf.Max(1f, length), Mathf.Max(1f, fenceHeight), Mathf.Max(0.1f, fenceThickness));
                ConfigureRenderer(cube, new Color(0.46f, 0.34f, 0.18f, 1f));
            }

            _barrierRoots.Add(root);
        }

        Vector3 ProjectBarrierPosition(Vector3 position)
        {
            if (_voxelWorld != null && _voxelWorld.TryGetTerrainSurfaceYAtWorld(position + Vector3.up * 4f, out float surfaceY, includeWaterSurface: false))
            {
                position.y = surfaceY + 0.05f;
                return position;
            }

            if (TryProjectToGround(position, out var grounded))
                return grounded;

            return position;
        }

        void NormalizeVisualToHeight(GameObject root, GameObject visual, float targetHeight)
        {
            if (root == null || visual == null || !TryGetHierarchyBounds(visual, out var bounds))
                return;

            float height = Mathf.Max(0.01f, bounds.size.y);
            float uniformScale = Mathf.Max(0.01f, targetHeight) / height;
            visual.transform.localScale *= uniformScale;

            if (!TryGetHierarchyBounds(visual, out bounds))
                return;

            Vector3 desiredBottomCenter = root.transform.position;
            Vector3 currentBottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            visual.transform.position += desiredBottomCenter - currentBottomCenter;
        }

        void NormalizeBarrierVisual(GameObject root, GameObject visual, float targetLength, float targetHeight)
        {
            NormalizeVisualToHeight(root, visual, targetHeight);
            if (root == null || visual == null || !TryGetHierarchyBounds(visual, out var bounds))
                return;

            float currentLength = Mathf.Max(0.01f, Mathf.Max(bounds.size.x, bounds.size.z));
            float lengthScale = Mathf.Clamp(Mathf.Max(0.1f, targetLength) / currentLength, 0.85f, 1.15f);

            if (bounds.size.z >= bounds.size.x)
                visual.transform.localScale = Vector3.Scale(visual.transform.localScale, new Vector3(1f, 1f, lengthScale));
            else
                visual.transform.localScale = Vector3.Scale(visual.transform.localScale, new Vector3(lengthScale, 1f, 1f));

            if (!TryGetHierarchyBounds(visual, out bounds))
                return;

            Vector3 desiredBottomCenter = root.transform.position;
            Vector3 currentBottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            visual.transform.position += desiredBottomCenter - currentBottomCenter;
        }

        void NormalizeFenceSegmentVisual(GameObject root, GameObject visual, float targetLength)
        {
            NormalizeFenceVisual(root, visual, targetLength, Mathf.Max(0.4f, fenceVisualHeight));
        }

        void NormalizeFenceCornerVisual(GameObject root, GameObject visual)
        {
            NormalizeFenceVisual(root, visual, Mathf.Max(0.8f, fenceSegmentLength * 0.9f), Mathf.Max(0.4f, fenceVisualHeight));
        }

        void NormalizeFenceGateVisual(GameObject root, GameObject visual, float gateWidth)
        {
            NormalizeFenceVisual(root, visual, gateWidth, Mathf.Max(0.4f, fenceVisualHeight));
        }

        void NormalizeFenceVisual(GameObject root, GameObject visual, float targetLength, float targetHeight)
        {
            if (root == null || visual == null || !TryGetHierarchyBounds(visual, out var bounds))
                return;

            bool lengthOnX = bounds.size.x >= bounds.size.z;
            float currentLength = Mathf.Max(0.01f, lengthOnX ? bounds.size.x : bounds.size.z);
            float currentThickness = Mathf.Max(0.01f, lengthOnX ? bounds.size.z : bounds.size.x);
            float currentHeight = Mathf.Max(0.01f, bounds.size.y);

            float lengthScale = Mathf.Max(0.1f, targetLength) / currentLength;
            float heightScale = Mathf.Max(0.1f, targetHeight) / currentHeight;
            float thicknessScale = Mathf.Max(0.05f, fenceVisualThickness) / currentThickness;

            Vector3 scale = visual.transform.localScale;
            scale = lengthOnX
                ? Vector3.Scale(scale, new Vector3(lengthScale, heightScale, thicknessScale))
                : Vector3.Scale(scale, new Vector3(thicknessScale, heightScale, lengthScale));
            visual.transform.localScale = scale;

            if (!TryGetHierarchyBounds(visual, out bounds))
                return;

            Vector3 desiredBottomCenter = root.transform.position;
            Vector3 currentBottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            visual.transform.position += desiredBottomCenter - currentBottomCenter;
        }

        float GetFencePrimaryLength(GameObject prefab, float targetHeight)
        {
            if (prefab == null || !TryGetHierarchyBounds(prefab, out var bounds))
                return Mathf.Max(0.5f, fenceSegmentLength);

            float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
            if (horizontal <= 0.01f)
                return Mathf.Max(0.5f, fenceSegmentLength);

            return Mathf.Max(0.5f, horizontal);
        }

        void AlignLinearVisualToForward(GameObject visual, bool preferForwardAxis)
        {
            if (visual == null || !TryGetHierarchyBounds(visual, out var bounds))
                return;

            if (!preferForwardAxis)
                return;

            if (bounds.size.x > bounds.size.z)
                visual.transform.localRotation *= Quaternion.Euler(0f, 90f, 0f);
        }

        void ConfigureRenderer(GameObject go, Color color)
        {
            if (go == null)
                return;

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null)
                return;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var material = new Material(shader);
            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color"))
                material.SetColor("_Color", color);
            if (material.HasProperty("_EmissionColor"))
            {
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", color * 0.08f);
            }

            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        void RefreshSettlementVisuals()
        {
            ApplyActivationTint(_centerpieceRoot, _isActivated
                ? new Color(0.30f, 0.82f, 0.68f, 1f)
                : new Color(0.32f, 0.38f, 0.46f, 1f));
            ApplyActivationTint(_storageRoot, _isActivated
                ? new Color(0.78f, 0.60f, 0.28f, 1f)
                : new Color(0.38f, 0.32f, 0.24f, 1f));
            RefreshSafeZoneRing();
        }

        void RefreshSafeZoneRing()
        {
            if (_safeZoneRingRoot == null)
                return;

            var line = _safeZoneRingRoot.GetComponent<LineRenderer>();
            if (line == null)
                return;

            Color color = _isActivated
                ? new Color(0.30f, 0.92f, 0.48f, 0.95f)
                : new Color(0.22f, 0.55f, 0.34f, 0.65f);
            line.startColor = color;
            line.endColor = color;
            line.enabled = true;
        }

        void ApplyActivationTint(GameObject root, Color tint)
        {
            if (root == null)
                return;

            var renderers = root.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;
                if (root == _centerpieceRoot &&
                    _centerpieceVisualRoot != null &&
                    renderer.transform.IsChildOf(_centerpieceVisualRoot.transform))
                    continue;

                var materials = renderer.materials;
                for (int j = 0; j < materials.Length; j++)
                {
                    var material = materials[j];
                    if (material == null)
                        continue;
                    if (material.HasProperty("_BaseColor"))
                        material.SetColor("_BaseColor", tint);
                    if (material.HasProperty("_Color"))
                        material.SetColor("_Color", tint);
                    if (material.HasProperty("_EmissionColor"))
                    {
                        material.EnableKeyword("_EMISSION");
                        material.SetColor("_EmissionColor", tint * (_isActivated ? 0.14f : 0.04f));
                    }
                }
                renderer.materials = materials;
            }
        }

        void RefreshSettlementState(bool force)
        {
            _nextRefreshAt = Time.time + 0.35f;

            if (_centerpieceRoot != null)
                _centerPosition = _centerpieceRoot.transform.position;

            if (_playerRoot != null)
                _playerInsideSafeZone = IsInsideSafeZone(_playerRoot.position);
            else
                _playerInsideSafeZone = false;

            _flowerCount = CountFlowers();
            _nearbyAnimalCount = CountNearbyAnimals();
            _currentLevel = ResolveLevel();
            _statusLine = ResolveStatusLine();
            _goalLine = ResolveGoalLine();
            _storageSummary = GetDetailedStorageSummary();

            if (force)
                RefreshSettlementVisuals();
        }

        int CountFlowers()
        {
            int total = 0;

            if (_runtime != null && _runtime.Grid != null)
            {
                _runtime.Grid.ForEach((x, y, cell) =>
                {
                    if (cell != null && cell.HasPlant && cell.PlantStage != PlantStage.Burnt)
                        total++;
                });
            }
            else
            {
                total = FindObjectsByType<FPSSeedGrowth>(FindObjectsSortMode.None).Length;
            }

            return total;
        }

        int CountNearbyAnimals()
        {
            int total = 0;
            var activeAgents = FPSBoidAgent.ActiveAgentsView;
            if (activeAgents == null)
                return 0;

            float radius = SafeZoneRadius + 4f;
            float radiusSqr = radius * radius;
            for (int i = 0; i < activeAgents.Count; i++)
            {
                var agent = activeAgents[i];
                if (agent == null || !agent.isActiveAndEnabled || agent.role != FPSBoidAgent.BoidRole.Prey)
                    continue;

                Vector3 delta = agent.transform.position - _centerPosition;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radiusSqr)
                    total++;
            }

            return total;
        }

        int ResolveLevel()
        {
            if (!_isActivated)
                return 0;

            int storedItems = TotalStoredItems;
            float activeSeconds = _activatedAt > 0f ? Time.time - _activatedAt : 0f;
            int level = 1;

            if (_flowerCount >= level2FlowerRequirement && storedItems >= level2StoredItemsRequirement)
                level = 2;
            if (_flowerCount >= level3FlowerRequirement && _nearbyAnimalCount >= level3AnimalRequirement)
                level = 3;
            if (_flowerCount >= level4FlowerRequirement && _nearbyAnimalCount >= level4AnimalRequirement && activeSeconds >= level4SurvivalSeconds)
                level = 4;

            return level;
        }

        string ResolveStatusLine()
        {
            if (!_isActivated)
                return "Dormant settlement";

            string zone = _playerInsideSafeZone ? "SAFE ZONE" : "OUTSIDE HOME";
            return $"{zone}  /  LEVEL {_currentLevel}";
        }

        string ResolveGoalLine()
        {
            if (!_isActivated)
                return "Activate the centerpiece";

            if (_currentLevel < 2)
                return $"Grow {level2FlowerRequirement} flowers and store {level2StoredItemsRequirement} supplies";
            if (_currentLevel < 3)
                return $"Bring {level3AnimalRequirement} animals near home and reach {level3FlowerRequirement} flowers";
            if (_currentLevel < 4)
            {
                float activeSeconds = _activatedAt > 0f ? Time.time - _activatedAt : 0f;
                float remaining = Mathf.Max(0f, level4SurvivalSeconds - activeSeconds);
                return $"Hold {level4FlowerRequirement} flowers, {level4AnimalRequirement} animals, survive {Mathf.CeilToInt(remaining)}s";
            }

            return "Settlement stabilized";
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureExists()
        {
            if (FindFirstObjectByType<SCoLSettlementManager>() != null)
                return;

            var go = new GameObject("SCoLSettlementManager");
            DontDestroyOnLoad(go);
            go.AddComponent<SCoLSettlementManager>();
        }
    }
}
