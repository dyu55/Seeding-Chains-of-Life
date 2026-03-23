using System.Collections.Generic;
using System.Text;
using UnityEngine;
using SCoL.Inventory;
using SCoL.Visualization;
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

        [Header("Setup")]
        public bool createRuntimePlaceholders = true;
        public bool startActivated = true;
        [Min(2f)] public float spawnAheadDistance = 8f;
        [Min(2f)] public float safeZoneRadius = 8.5f;
        [Min(0.5f)] public float slowZonePadding = 2.5f;

        [Header("Layout")]
        [Min(4f)] public float fenceHalfExtent = 11f;
        [Min(2f)] public float frontGateWidth = 4.8f;
        [Min(0f)] public float storageSideOffset = 0f;
        [Min(0.2f)] public float storageForwardOffset = 0.9f;
        public bool useStorageWorldPositionOverride = true;
        public Vector3 storageWorldPositionOverride = new Vector3(90.3f, 23.07f, 89.58f);
        [Min(0.5f)] public float fenceTargetHeight = 1.9f;
        [Min(0.05f)] public float fenceVisualThickness = 0.18f;
        [Min(0.2f)] public float fenceCornerFootprint = 1.6f;
        [Min(0.5f)] public float storageTargetHeight = 1.4f;
        [Min(0.5f)] public float centerpieceTargetFootprint = 17.6f;
        [Min(0.5f)] public float centerpieceTargetHeight = 10.4f;
        public Vector3 centerpieceModelEuler = Vector3.zero;
        [Min(0.1f)] public float storageOpenHoldSeconds = 0.8f;

        [Header("Prefabs")]
        public GameObject centerpiecePrefab;
        public GameObject storagePrefab;
        public GameObject storageOpenPrefab;
        public GameObject fenceStraightPrefab;
        public GameObject fenceCornerPrefab;
        public GameObject fenceGatePrefab;
        public bool spawnPerimeterFences = true;

        [Header("Storage")]
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

        VoxelWorld _voxelWorld;
        SCoLCampsiteSceneAnchor _sceneAnchor;
        Transform _playerRoot;
        Vector3 _centerPosition;
        Vector3 _forward = Vector3.forward;
        bool _built;
        bool _isActivated;
        bool _playerInsideSafeZone;
        int _currentLevel = 1;
        int _flowerCount;
        int _nearbyAnimalCount;
        float _activatedAt = -1f;
        float _nextRefreshAt;
        float _storageOpenUntil = -1f;

        readonly int[] _storedSeedVariants = new int[8];
        int _storedWater;
        int _storedFire;
        int _storedPlants;
        int _storedStones;
        bool _settlementAreaCleared;

        string _statusLine = "Outside Home / Level 1";
        string _goalLine = "Grow 6 flowers and store 20 supplies";
        string _storageSummary = "Storage offline";

        GameObject _centerpieceRoot;
        GameObject _centerpieceVisualRoot;
        GameObject _storageRoot;
        GameObject _storageVisualRoot;
        GameObject _safeZoneRingRoot;
        bool _storageIsOpen;
        bool _storageVisualInitialized;
        readonly List<GameObject> _barrierRoots = new List<GameObject>(10);
        readonly StringBuilder _sb = new StringBuilder(128);
        static readonly Quaternion FenceVisualQuarterTurn = Quaternion.Euler(0f, 90f, 0f);

        Texture2D _fenceStraightTexture;
        Texture2D _fenceCornerTexture;
        Texture2D _fenceGateTexture;
        Texture2D _storageClosedTexture;
        Texture2D _storageOpenTexture;
        Texture2D _tentTexture;

        static readonly Dictionary<string, Material> MaterialCache = new Dictionary<string, Material>();

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
            _isActivated = startActivated;
            if (_isActivated)
                _activatedAt = Time.time;

            FindReferences();
            AutoAssignAssets();
            EnsureBuilt();
            RefreshSettlementState(force: true);
        }

        void Update()
        {
            FindReferences();
            AutoAssignAssets();
            EnsureBuilt();
            UpdateStorageVisualState();

            if (Time.time >= _nextRefreshAt)
                RefreshSettlementState(force: false);

            if (_built && _isActivated)
                KeepAnimalsOutOfSettlement();
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }

        public bool IsInsideSafeZone(Vector3 worldPosition)
        {
            if (!_isActivated)
                return false;
            return IsInsideFenceBounds(worldPosition, 0f);
        }

        public float DistanceToCenterXZ(Vector3 worldPosition)
        {
            Vector3 local = GetSettlementLocal(worldPosition);
            float half = Mathf.Max(1f, fenceHalfExtent);
            float dx = Mathf.Max(0f, Mathf.Abs(local.x) - half);
            float dz = Mathf.Max(0f, Mathf.Abs(local.z) - half);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        public Vector3 GetSafeZoneRepelDirection(Vector3 worldPosition)
        {
            Vector3 local = GetSettlementLocal(worldPosition);
            float half = Mathf.Max(1f, fenceHalfExtent);
            float marginX = half - Mathf.Abs(local.x);
            float marginZ = half - Mathf.Abs(local.z);

            Vector3 away = marginX < marginZ
                ? GetSettlementRight() * (local.x >= 0f ? 1f : -1f)
                : _forward * (local.z >= 0f ? 1f : -1f);
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f)
                away = _forward;
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
            RefreshRingVisual();
            RefreshSettlementState(force: true);
            message = "Settlement activated. Wolves avoid the home zone.";
            return true;
        }

        public bool TryDepositCurrentTool(SCoLInventory inventory, FPSRaycastInteractor.ApplyTool tool, int seedVariantIndex, out string message)
        {
            message = "Nothing stored.";
            if (inventory == null)
                return false;

            switch (tool)
            {
                case FPSRaycastInteractor.ApplyTool.Seed:
                {
                    int amount = Mathf.Min(seedTransferAmount, inventory.GetSeedTypeCount(seedVariantIndex));
                    if (amount <= 0 || !inventory.TryConsumeSeedType(seedVariantIndex, amount))
                    {
                        message = "No matching seeds to store.";
                        return false;
                    }
                    _storedSeedVariants[Mathf.Clamp(seedVariantIndex, 0, _storedSeedVariants.Length - 1)] += amount;
                    message = $"Stored {amount} {inventory.GetSeedTypeDisplayName(seedVariantIndex)} seed(s).";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Water:
                {
                    int amount = Mathf.Min(waterTransferAmount, inventory.water);
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
                    int amount = Mathf.Min(fireTransferAmount, inventory.fire);
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
                    int amount = Mathf.Min(plantTransferAmount, inventory.plants);
                    if (amount <= 0 || !inventory.TryConsume(SCoLItemType.Plant, amount))
                    {
                        message = "No plants to store.";
                        return false;
                    }
                    _storedPlants += amount;
                    message = $"Stored {amount} plants.";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Stone:
                {
                    int amount = Mathf.Min(stoneTransferAmount, inventory.stones);
                    if (amount <= 0 || !inventory.TryConsume(SCoLItemType.Stone, amount))
                    {
                        message = "No stones to store.";
                        return false;
                    }
                    _storedStones += amount;
                    message = $"Stored {amount} stones.";
                    break;
                }
                default:
                    return false;
            }

            OpenStorageTemporarily();
            RefreshSettlementState(force: true);
            return true;
        }

        public bool TryWithdrawCurrentTool(SCoLInventory inventory, FPSRaycastInteractor.ApplyTool tool, int seedVariantIndex, out string message)
        {
            message = "Storage empty.";
            if (inventory == null)
                return false;

            switch (tool)
            {
                case FPSRaycastInteractor.ApplyTool.Seed:
                {
                    int idx = Mathf.Clamp(seedVariantIndex, 0, _storedSeedVariants.Length - 1);
                    int amount = Mathf.Min(seedTransferAmount, _storedSeedVariants[idx]);
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
                    int amount = Mathf.Min(waterTransferAmount, _storedWater);
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
                    int amount = Mathf.Min(fireTransferAmount, _storedFire);
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
                    int amount = Mathf.Min(plantTransferAmount, _storedPlants);
                    if (amount <= 0)
                    {
                        message = "No plants in storage.";
                        return false;
                    }
                    _storedPlants -= amount;
                    inventory.Add(SCoLItemType.Plant, amount);
                    message = $"Withdrew {amount} plants.";
                    break;
                }
                case FPSRaycastInteractor.ApplyTool.Stone:
                {
                    int amount = Mathf.Min(stoneTransferAmount, _storedStones);
                    if (amount <= 0)
                    {
                        message = "No stones in storage.";
                        return false;
                    }
                    _storedStones -= amount;
                    inventory.Add(SCoLItemType.Stone, amount);
                    message = $"Withdrew {amount} stones.";
                    break;
                }
                default:
                    return false;
            }

            OpenStorageTemporarily();
            RefreshSettlementState(force: true);
            return true;
        }

        public void PulseStorageOpen(float seconds)
        {
            _storageOpenUntil = Mathf.Max(_storageOpenUntil, Time.time + Mathf.Max(0.05f, seconds));
            SetStorageVisual(true);
        }

        public string GetCurrentToolStorageHint(FPSRaycastInteractor interactor, SCoLInventory inventory)
        {
            if (interactor == null)
                return "supplies";

            return interactor.currentTool switch
            {
                FPSRaycastInteractor.ApplyTool.Seed => inventory != null ? inventory.GetSeedTypeDisplayName(interactor.GetSelectedSeedVariantIndex()) + " seeds" : "seeds",
                FPSRaycastInteractor.ApplyTool.Water => "water",
                FPSRaycastInteractor.ApplyTool.Fire => "fire",
                FPSRaycastInteractor.ApplyTool.Plant => "plants",
                FPSRaycastInteractor.ApplyTool.Stone => "stones",
                _ => "supplies"
            };
        }

        void FindReferences()
        {
            if (_voxelWorld == null)
                _voxelWorld = FindFirstObjectByType<VoxelWorld>();
            if (_sceneAnchor == null)
                _sceneAnchor = FindFirstObjectByType<SCoLCampsiteSceneAnchor>();
            if (_playerRoot == null)
            {
                var interactor = FindFirstObjectByType<FPSRaycastInteractor>();
                if (interactor != null)
                    _playerRoot = interactor.transform.root;
                else if (Camera.main != null)
                    _playerRoot = Camera.main.transform.root;
            }
        }

        void AutoAssignAssets()
        {
            AutoAssignCenterpiecePrefab();
            AutoAssignStoragePrefabs();
            AutoAssignFencePrefabs();
            AutoAssignTextures();
        }

        void AutoAssignCenterpiecePrefab()
        {
            if (centerpiecePrefab != null)
                return;

#if UNITY_EDITOR
            centerpiecePrefab =
                LoadEditorPrefab(
                    "Assets/Models/Modeling/_Incoming/tent updated/tent updated 2.obj",
                    "Assets/Models/Modeling/_Incoming/tent updated/tent updated.obj",
                    "Assets/Models/Modeling/_Incoming/Campsite/tentV2.obj",
                    "Assets/Models/Modeling/_Incoming/Campsite/tentV1.obj");
#endif
        }

        void AutoAssignStoragePrefabs()
        {
#if UNITY_EDITOR
            if (storagePrefab == null)
            {
                storagePrefab =
                    LoadEditorPrefab(
                        "Assets/Models/Modeling/_Incoming/chest closed/chest closed 2.obj",
                        "Assets/Models/Modeling/_Incoming/chest closed/chest closed.obj",
                        "Assets/Models/Modeling/_Incoming/Crate/Crate 2.obj",
                        "Assets/Models/Modeling/_Incoming/Crate/Crate.obj");
            }

            if (storageOpenPrefab == null)
            {
                storageOpenPrefab =
                    LoadEditorPrefab(
                        "Assets/Models/Modeling/_Incoming/Chest open/Chest open 2.obj",
                        "Assets/Models/Modeling/_Incoming/Chest open/Chest open.obj");
            }
#endif
        }

        void AutoAssignFencePrefabs()
        {
#if UNITY_EDITOR
            fenceStraightPrefab =
                LoadEditorPrefab(
                    "Assets/Models/Modeling/_Incoming/fence alone/fence alone 2.obj",
                    "Assets/Models/Modeling/_Incoming/fence alone/fence alone.obj",
                    "Assets/Models/Modeling/_Incoming/fence  alone 2/fence  alone 2.obj",
                    AssetDatabase.GetAssetPath(fenceStraightPrefab));

            fenceCornerPrefab =
                LoadEditorPrefab(
                    "Assets/Models/Modeling/_Incoming/fence cornner/fence cornner 2.obj",
                    "Assets/Models/Modeling/_Incoming/fence cornner/fence cornner.obj",
                    "Assets/Models/Modeling/_Incoming/Cornner fence 2/Cornner fence 2.obj",
                    AssetDatabase.GetAssetPath(fenceCornerPrefab));

            fenceGatePrefab =
                LoadEditorPrefab(
                    "Assets/Models/Modeling/_Incoming/fence gate/fence gate 2.obj",
                    "Assets/Models/Modeling/_Incoming/fence gate/fence gate.obj",
                    "Assets/Models/Modeling/_Incoming/fence gate 2/fence gate 2.obj",
                    AssetDatabase.GetAssetPath(fenceGatePrefab));
#endif
        }

        void AutoAssignTextures()
        {
#if UNITY_EDITOR
            if (_tentTexture == null)
            {
                _tentTexture = LoadEditorTexture(
                    "Assets/Models/Modeling/_Incoming/tent updated/tent updated 2.jpg",
                    "Assets/Models/Modeling/_Incoming/tent updated/tent updated.jpg");
            }

            if (_storageClosedTexture == null)
            {
                _storageClosedTexture = LoadEditorTexture(
                    "Assets/Models/Modeling/_Incoming/chest closed/chest closed 2.jpg",
                    "Assets/Models/Modeling/_Incoming/chest closed/chest closed.jpg",
                    "Assets/Models/Modeling/_Incoming/Crate/Crate 2.jpg",
                    "Assets/Models/Modeling/_Incoming/Crate/Crate.jpg");
            }

            if (_storageOpenTexture == null)
            {
                _storageOpenTexture = LoadEditorTexture(
                    "Assets/Models/Modeling/_Incoming/Chest open/Chest open 2.jpg",
                    "Assets/Models/Modeling/_Incoming/Chest open/Chest open.jpg");
            }

            if (_fenceStraightTexture == null)
            {
                _fenceStraightTexture = LoadEditorTexture(
                    "Assets/Models/Modeling/_Incoming/fence alone/fence alone 2.jpg",
                    "Assets/Models/Modeling/_Incoming/fence alone/fence alone.jpg",
                    "Assets/Models/Modeling/_Incoming/fence  alone 2/fence  alone 2.jpg");
            }

            if (_fenceCornerTexture == null)
            {
                _fenceCornerTexture = LoadEditorTexture(
                    "Assets/Models/Modeling/_Incoming/fence cornner/fence cornner 2.jpg",
                    "Assets/Models/Modeling/_Incoming/fence cornner/fence cornner.jpg",
                    "Assets/Models/Modeling/_Incoming/Cornner fence 2/Cornner fence 2.jpg");
            }

            if (_fenceGateTexture == null)
            {
                _fenceGateTexture = LoadEditorTexture(
                    "Assets/Models/Modeling/_Incoming/fence gate/fence gate 2.jpg",
                    "Assets/Models/Modeling/_Incoming/fence gate/fence gate.jpg",
                    "Assets/Models/Modeling/_Incoming/fence gate 2/fence gate 2.jpg");
            }
#endif
        }

        void EnsureBuilt()
        {
            if (!createRuntimePlaceholders)
                return;

            ResolveCenterAndForward();

            bool missingCore = _centerpieceRoot == null || _storageRoot == null;
            bool missingFence = spawnPerimeterFences && _barrierRoots.Count == 0;
            if (_built && !missingCore && !missingFence)
                return;

            ClearBuiltObjects();
            BuildCenterpiece();
            BuildStorage();
            if (spawnPerimeterFences)
                BuildFence();
            ClearSettlementInterior();

            _built = true;
        }

        void ResolveCenterAndForward()
        {
            Vector3 forward = Vector3.forward;
            if (_playerRoot != null)
            {
                forward = _playerRoot.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude > 0.001f)
                    forward.Normalize();
            }
            _forward = forward.sqrMagnitude > 0.001f ? forward : Vector3.forward;

            if (_voxelWorld != null && _voxelWorld.Config != null)
            {
                Vector3 center = _voxelWorld.OriginWorld + new Vector3(_voxelWorld.Config.worldWidth * 0.5f, 0f, _voxelWorld.Config.worldDepth * 0.5f);
                _centerPosition = ProjectToGround(center);
                _forward = Vector3.forward;
                if (_sceneAnchor != null)
                    _sceneAnchor.transform.position = _centerPosition;
                return;
            }

            if (_sceneAnchor != null)
            {
                _centerPosition = ProjectToGround(_sceneAnchor.transform.position);
                _sceneAnchor.transform.position = _centerPosition;
                return;
            }

            if (_playerRoot != null)
            {
                _centerPosition = ProjectToGround(_playerRoot.position + _forward * spawnAheadDistance);
                return;
            }

            _centerPosition = transform.position;
        }

        void BuildCenterpiece()
        {
            if (_sceneAnchor != null)
            {
                _sceneAnchor.transform.position = _centerPosition;
                _sceneAnchor.EnsureVisualNow();
                _centerpieceRoot = _sceneAnchor.gameObject;
                _centerpieceVisualRoot = _sceneAnchor.GetVisualRoot();
                _centerpieceRoot.name = "SettlementCenterpiece";
                ApplyInteractable(_centerpieceRoot, SCoLSettlementInteractableKind.Centerpiece);
                if (_centerpieceVisualRoot != null)
                {
                    _centerpieceVisualRoot.transform.localRotation = Quaternion.Euler(centerpieceModelEuler);
                    NormalizeToFootprintAndHeight(_centerpieceRoot, _centerpieceVisualRoot, centerpieceTargetFootprint, centerpieceTargetHeight);
                    ApplyTextureToRenderers(_centerpieceVisualRoot, _tentTexture, "SettlementTentMat");
                    EnsureColliderFromVisual(_centerpieceRoot, _centerpieceVisualRoot, true);
                }
                return;
            }

            _centerpieceRoot = new GameObject("SettlementCenterpiece");
            _centerpieceRoot.transform.SetParent(transform, false);
            _centerpieceRoot.transform.position = _centerPosition;
            ApplyInteractable(_centerpieceRoot, SCoLSettlementInteractableKind.Centerpiece);

            if (centerpiecePrefab != null)
            {
                _centerpieceVisualRoot = Instantiate(centerpiecePrefab, _centerpieceRoot.transform, false);
                _centerpieceVisualRoot.name = "CampsiteVisual";
                _centerpieceVisualRoot.transform.localRotation = Quaternion.Euler(centerpieceModelEuler);
                NormalizeToFootprintAndHeight(_centerpieceRoot, _centerpieceVisualRoot, centerpieceTargetFootprint, centerpieceTargetHeight);
                ApplyTextureToRenderers(_centerpieceVisualRoot, _tentTexture, "SettlementTentMat");
                EnsureColliderFromVisual(_centerpieceRoot, _centerpieceVisualRoot, true);
            }
        }

        void BuildStorage()
        {
            _storageRoot = new GameObject("SettlementStorage");
            _storageRoot.transform.SetParent(transform, false);
            Vector3 right = Vector3.Cross(Vector3.up, _forward).normalized;
            Vector3 storagePos = useStorageWorldPositionOverride
                ? storageWorldPositionOverride
                : ProjectToGround(_centerPosition + right * storageSideOffset + _forward * storageForwardOffset);
            _storageRoot.transform.position = storagePos;
            _storageRoot.transform.rotation = Quaternion.LookRotation(-_forward, Vector3.up);
            ApplyInteractable(_storageRoot, SCoLSettlementInteractableKind.Storage);
            SetStorageVisual(false, true);
        }

        void BuildFence()
        {
            Vector3 right = Vector3.Cross(Vector3.up, _forward).normalized;
            float half = Mathf.Max(SafeZoneRadius + 1.5f, fenceHalfExtent);
            float gateHalf = Mathf.Clamp(frontGateWidth * 0.5f, 1.5f, half - 1f);
            float cornerPad = 0.7f;

            Vector3 front = _centerPosition - _forward * half;
            Vector3 back = _centerPosition + _forward * half;
            Vector3 frontLeft = front - right * half;
            Vector3 frontRight = front + right * half;
            Vector3 backLeft = back - right * half;
            Vector3 backRight = back + right * half;

            CreateCorner(frontLeft, -_forward - right);
            CreateCorner(frontRight, -_forward + right);
            CreateCorner(backLeft, _forward - right);
            CreateCorner(backRight, _forward + right);

            CreateSide("SettlementBarrier_Back", backLeft + right * cornerPad, backRight - right * cornerPad, fenceStraightPrefab, _fenceStraightTexture);
            CreateSide("SettlementBarrier_Left", frontLeft + _forward * cornerPad, backLeft - _forward * cornerPad, fenceStraightPrefab, _fenceStraightTexture);
            CreateSide("SettlementBarrier_Right", frontRight + _forward * cornerPad, backRight - _forward * cornerPad, fenceStraightPrefab, _fenceStraightTexture);
            CreateSide("SettlementBarrier_FrontLeft", frontLeft + right * cornerPad, front - right * gateHalf, fenceStraightPrefab, _fenceStraightTexture);
        }

        void CreateSide(string name, Vector3 start, Vector3 end, GameObject prefab, Texture2D texture)
        {
            if (prefab == null)
                return;

            Vector3 delta = end - start;
            delta.y = 0f;
            float length = delta.magnitude;
            if (length < 0.2f)
                return;

            Vector3 dir = delta / length;
            GameObject root = new GameObject(name);
            root.transform.SetParent(transform, false);
            root.transform.position = ProjectToGround((start + end) * 0.5f);
            root.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);
            ApplyInteractable(root, SCoLSettlementInteractableKind.Barrier);

            GameObject visual = Instantiate(prefab, root.transform, false);
            visual.name = "FenceVisual";
            visual.transform.localRotation = FenceVisualQuarterTurn;
            NormalizeLinearVisual(root, visual, length, fenceTargetHeight, fenceVisualThickness);
            ApplyTextureToRenderers(visual, texture, name + "_Mat");
            EnsureColliderFromVisual(root, visual, false);
            _barrierRoots.Add(root);
        }

        void CreateCorner(Vector3 position, Vector3 facing)
        {
            if (fenceCornerPrefab == null)
                return;

            GameObject root = new GameObject("SettlementBarrierCorner");
            root.transform.SetParent(transform, false);
            root.transform.position = ProjectToGround(position);
            root.transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            ApplyInteractable(root, SCoLSettlementInteractableKind.Barrier);

            GameObject visual = Instantiate(fenceCornerPrefab, root.transform, false);
            visual.name = "FenceCorner";
            visual.transform.localRotation = FenceVisualQuarterTurn;
            NormalizeCornerVisual(root, visual, fenceCornerFootprint, fenceTargetHeight);
            ApplyTextureToRenderers(visual, _fenceCornerTexture, "SettlementFenceCornerMat");
            EnsureColliderFromVisual(root, visual, false);
            _barrierRoots.Add(root);
        }

        void CreateGate(Vector3 position, Vector3 right)
        {
            if (fenceGatePrefab == null)
                return;

            GameObject root = new GameObject("SettlementBarrierGate");
            root.transform.SetParent(transform, false);
            root.transform.position = ProjectToGround(position);
            root.transform.rotation = Quaternion.LookRotation(right.normalized, Vector3.up);

            GameObject visual = Instantiate(fenceGatePrefab, root.transform, false);
            visual.name = "FenceGate";
            visual.transform.localRotation = FenceVisualQuarterTurn;
            NormalizeLinearVisual(root, visual, Mathf.Max(2f, frontGateWidth), fenceTargetHeight, fenceVisualThickness);
            ApplyTextureToRenderers(visual, _fenceGateTexture, "SettlementFenceGateMat");
            _barrierRoots.Add(root);
        }

        void SetStorageVisual(bool open, bool force = false)
        {
            if (_storageRoot == null)
                return;
            if (!force && _storageIsOpen == open)
                return;

            bool previousOpen = _storageIsOpen;
            _storageIsOpen = open;
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
            }

            GameObject prefab = open && storageOpenPrefab != null ? storageOpenPrefab : storagePrefab;
            if (prefab == null)
                return;

            _storageVisualRoot = Instantiate(prefab, _storageRoot.transform, false);
            _storageVisualRoot.name = open ? "StorageVisualOpen" : "StorageVisualClosed";
            NormalizeToHeight(_storageRoot, _storageVisualRoot, storageTargetHeight);
            ApplyTextureToRenderers(_storageVisualRoot, open ? _storageOpenTexture : _storageClosedTexture, open ? "StorageOpenMat" : "StorageClosedMat");
            EnsureColliderFromVisual(_storageRoot, _storageVisualRoot, true);

            if (Application.isPlaying && _storageVisualInitialized && previousOpen != open)
            {
                DayNightLightingController.PlayInteractionSfx(open
                    ? DayNightLightingController.InteractionSfx.ChestOpen
                    : DayNightLightingController.InteractionSfx.ChestClose);
            }

            _storageVisualInitialized = true;
        }

        void UpdateStorageVisualState()
        {
            bool shouldOpen = Time.time < _storageOpenUntil;
            SetStorageVisual(shouldOpen);
        }

        void OpenStorageTemporarily()
        {
            _storageOpenUntil = Time.time + Mathf.Max(0.1f, storageOpenHoldSeconds);
            SetStorageVisual(true);
        }

        void RefreshSettlementState(bool force)
        {
            _nextRefreshAt = Time.time + (force ? 0.25f : 0.75f);

            _playerInsideSafeZone = _playerRoot != null && IsInsideSafeZone(_playerRoot.position);
            _flowerCount = CountFlowers();
            _nearbyAnimalCount = CountNearbyAnimals();
            _currentLevel = ResolveLevel();
            _statusLine = _playerInsideSafeZone
                ? $"Safe Zone / Level {_currentLevel}"
                : $"Outside Home / Level {_currentLevel}";
            _goalLine = ResolveGoalLine();
            _storageSummary = BuildStorageSummary();
        }

        void RefreshRingVisual() { }

        int CountFlowers()
        {
            var roots = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            int count = 0;
            for (int i = 0; i < roots.Length; i++)
            {
                var t = roots[i];
                if (t == null || !t.name.StartsWith("Plant_"))
                    continue;
                if (t.parent != null && t.parent.name.StartsWith("Plant_"))
                    continue;
                count++;
            }
            return count;
        }

        int CountNearbyAnimals()
        {
            float radiusSq = Mathf.Pow(SafeZoneRadius + 8f, 2f);
            int count = 0;
            var agents = FPSBoidAgent.ActiveAgentsView;
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || agent.role != FPSBoidAgent.BoidRole.Prey)
                    continue;

                Vector3 delta = agent.transform.position - _centerPosition;
                delta.y = 0f;
                if (delta.sqrMagnitude <= radiusSq)
                    count++;
            }
            return count;
        }

        bool IsInsideFenceBounds(Vector3 worldPosition, float padding)
        {
            Vector3 local = GetSettlementLocal(worldPosition);
            float half = Mathf.Max(1f, fenceHalfExtent) + padding;
            return Mathf.Abs(local.x) <= half && Mathf.Abs(local.z) <= half;
        }

        Vector3 GetSettlementLocal(Vector3 worldPosition)
        {
            Vector3 delta = worldPosition - _centerPosition;
            delta.y = 0f;
            Vector3 right = GetSettlementRight();
            return new Vector3(Vector3.Dot(delta, right), 0f, Vector3.Dot(delta, _forward));
        }

        Vector3 GetSettlementRight()
        {
            Vector3 right = Vector3.Cross(Vector3.up, _forward);
            if (right.sqrMagnitude < 0.0001f)
                right = Vector3.right;
            return right.normalized;
        }

        void ClearSettlementInterior()
        {
            if (_settlementAreaCleared)
                return;

            _settlementAreaCleared = true;
            ClearNamedObjectsInsideSettlement("FeaturedTree_");
            ClearNamedObjectsInsideSettlement("StylizedRock_");
            ClearNamedObjectsInsideSettlement("StylizedPebble_");
            ClearNamedObjectsInsideSettlement("StylizedPathRock_");
            KeepAnimalsOutOfSettlement();
        }

        void ClearNamedObjectsInsideSettlement(string prefix)
        {
            var roots = FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < roots.Length; i++)
            {
                var t = roots[i];
                if (t == null || !t.name.StartsWith(prefix))
                    continue;
                if (!IsInsideFenceBounds(t.position, 0.5f))
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

        void KeepAnimalsOutOfSettlement()
        {
            var agents = FPSBoidAgent.ActiveAgentsView;
            if (agents == null)
                return;

            Vector3 exitBase = _centerPosition - _forward * (fenceHalfExtent + 7f);
            float sideStep = Mathf.Max(2f, frontGateWidth * 0.5f);
            int moved = 0;
            for (int i = 0; i < agents.Count; i++)
            {
                var agent = agents[i];
                if (agent == null || !IsInsideFenceBounds(agent.transform.position, 0.25f))
                    continue;

                float lane = ((moved & 1) == 0 ? -1f : 1f) * sideStep * (1 + moved / 2);
                agent.transform.position = ProjectToGround(exitBase + GetSettlementRight() * lane);
                moved++;
            }
        }

        int ResolveLevel()
        {
            int storedItems = TotalStoredItems();
            float activeSeconds = _activatedAt > 0f ? Time.time - _activatedAt : 0f;
            if (_flowerCount >= level4FlowerRequirement &&
                _nearbyAnimalCount >= level4AnimalRequirement &&
                activeSeconds >= level4SurvivalSeconds)
                return 4;
            if (_flowerCount >= level3FlowerRequirement &&
                _nearbyAnimalCount >= level3AnimalRequirement)
                return 3;
            if (_flowerCount >= level2FlowerRequirement &&
                storedItems >= level2StoredItemsRequirement)
                return 2;
            return 1;
        }

        string ResolveGoalLine()
        {
            if (_currentLevel <= 1)
                return $"Grow {level2FlowerRequirement} flowers and store {level2StoredItemsRequirement} supplies";
            if (_currentLevel == 2)
                return $"Bring {level3AnimalRequirement} animals near home and reach {level3FlowerRequirement} flowers";
            if (_currentLevel == 3)
            {
                float remaining = Mathf.Max(0f, level4SurvivalSeconds - Mathf.Max(0f, Time.time - _activatedAt));
                return $"Hold {level4FlowerRequirement} flowers, {level4AnimalRequirement} animals, survive {Mathf.CeilToInt(remaining)}s";
            }
            return "Settlement thriving";
        }

        string BuildStorageSummary()
        {
            _sb.Clear();
            _sb.Append("Storage ");
            int totalSeeds = 0;
            for (int i = 0; i < _storedSeedVariants.Length; i++)
                totalSeeds += _storedSeedVariants[i];
            _sb.Append("Seed ").Append(totalSeeds);
            _sb.Append("  Water ").Append(_storedWater);
            _sb.Append("  Fire ").Append(_storedFire);
            _sb.Append("  Plant ").Append(_storedPlants);
            _sb.Append("  Stone ").Append(_storedStones);
            return _sb.ToString();
        }

        int TotalStoredItems()
        {
            int totalSeeds = 0;
            for (int i = 0; i < _storedSeedVariants.Length; i++)
                totalSeeds += _storedSeedVariants[i];
            return totalSeeds + _storedWater + _storedFire + _storedPlants + _storedStones;
        }

        void ApplyInteractable(GameObject target, SCoLSettlementInteractableKind kind)
        {
            if (target == null)
                return;
            var interactable = target.GetComponent<SCoLSettlementInteractable>();
            if (interactable == null)
                interactable = target.AddComponent<SCoLSettlementInteractable>();
            interactable.kind = kind;
            interactable.manager = this;
        }

        void ClearBuiltObjects()
        {
            for (int i = transform.childCount - 1; i >= 0; i--)
            {
                var child = transform.GetChild(i);
                if (child == null)
                    continue;

                string childName = child.name;
                if (!childName.StartsWith("SettlementBarrier") &&
                    childName != "SettlementStorage" &&
                    childName != "SettlementSafeZoneRing")
                    continue;

#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(child.gameObject);
                else
                    Destroy(child.gameObject);
#else
                Destroy(child.gameObject);
#endif
            }

            for (int i = 0; i < _barrierRoots.Count; i++)
            {
                if (_barrierRoots[i] == null)
                    continue;
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_barrierRoots[i]);
                else
                    Destroy(_barrierRoots[i]);
#else
                Destroy(_barrierRoots[i]);
#endif
            }
            _barrierRoots.Clear();

            if (_storageRoot != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_storageRoot);
                else
                    Destroy(_storageRoot);
#else
                Destroy(_storageRoot);
#endif
            }
            _storageRoot = null;
            _storageVisualRoot = null;

            if (_safeZoneRingRoot != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_safeZoneRingRoot);
                else
                    Destroy(_safeZoneRingRoot);
#else
                Destroy(_safeZoneRingRoot);
#endif
            }
            _safeZoneRingRoot = null;

            if (_sceneAnchor == null && _centerpieceRoot != null)
            {
#if UNITY_EDITOR
                if (!Application.isPlaying)
                    DestroyImmediate(_centerpieceRoot);
                else
                    Destroy(_centerpieceRoot);
#else
                Destroy(_centerpieceRoot);
#endif
            }

            if (_sceneAnchor != null)
            {
                var interactable = _sceneAnchor.GetComponent<SCoLSettlementInteractable>();
                if (interactable != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                        DestroyImmediate(interactable);
                    else
                        Destroy(interactable);
#else
                    Destroy(interactable);
#endif
                }
            }

            _centerpieceRoot = null;
            _centerpieceVisualRoot = null;
        }

        Vector3 ProjectToGround(Vector3 position)
        {
            if (_voxelWorld != null && _voxelWorld.TryGetTerrainSurfaceYAtWorld(position + Vector3.up * 6f, out float surfaceY, includeWaterSurface: false))
                return new Vector3(position.x, surfaceY + 0.05f, position.z);

            if (Physics.Raycast(position + Vector3.up * 30f, Vector3.down, out var hit, 100f, ~0, QueryTriggerInteraction.Ignore))
                return hit.point + Vector3.up * 0.05f;

            return position;
        }

        void NormalizeToHeight(GameObject root, GameObject visual, float targetHeight)
        {
            if (root == null || visual == null || !TryGetHierarchyLocalBounds(visual, out var bounds))
                return;

            float heightScale = Mathf.Max(0.1f, targetHeight) / Mathf.Max(0.001f, bounds.size.y);
            visual.transform.localScale *= heightScale;
            ReanchorBottomCenter(root, visual);
        }

        void NormalizeToFootprintAndHeight(GameObject root, GameObject visual, float targetFootprint, float targetHeight)
        {
            if (root == null || visual == null || !TryGetHierarchyLocalBounds(visual, out var bounds))
                return;

            float horizontal = Mathf.Max(bounds.size.x, bounds.size.z);
            float horizontalScale = Mathf.Max(0.1f, targetFootprint) / Mathf.Max(0.001f, horizontal);
            float heightScale = Mathf.Max(0.1f, targetHeight) / Mathf.Max(0.001f, bounds.size.y);
            float scale = Mathf.Min(horizontalScale, heightScale);
            visual.transform.localScale *= scale;
            ReanchorBottomCenter(root, visual);
        }

        void NormalizeLinearVisual(GameObject root, GameObject visual, float targetLength, float targetHeight, float targetThickness)
        {
            if (root == null || visual == null || !TryGetHierarchyLocalBounds(visual, out var bounds))
                return;

            bool lengthOnX = bounds.size.x >= bounds.size.z;
            float currentLength = Mathf.Max(0.001f, lengthOnX ? bounds.size.x : bounds.size.z);
            float currentDepth = Mathf.Max(0.001f, lengthOnX ? bounds.size.z : bounds.size.x);
            float currentHeight = Mathf.Max(0.001f, bounds.size.y);

            Vector3 scale = visual.transform.localScale;
            if (lengthOnX)
                scale = Vector3.Scale(scale, new Vector3(targetLength / currentLength, targetHeight / currentHeight, targetThickness / currentDepth));
            else
                scale = Vector3.Scale(scale, new Vector3(targetThickness / currentDepth, targetHeight / currentHeight, targetLength / currentLength));
            visual.transform.localScale = scale;

            ReanchorBottomCenter(root, visual);
        }

        void NormalizeCornerVisual(GameObject root, GameObject visual, float targetFootprint, float targetHeight)
        {
            if (root == null || visual == null || !TryGetHierarchyLocalBounds(visual, out var bounds))
                return;

            float currentWidth = Mathf.Max(0.001f, bounds.size.x);
            float currentDepth = Mathf.Max(0.001f, bounds.size.z);
            float currentHeight = Mathf.Max(0.001f, bounds.size.y);

            Vector3 scale = visual.transform.localScale;
            scale = Vector3.Scale(scale, new Vector3(
                Mathf.Max(0.1f, targetFootprint) / currentWidth,
                Mathf.Max(0.1f, targetHeight) / currentHeight,
                Mathf.Max(0.1f, targetFootprint) / currentDepth));
            visual.transform.localScale = scale;

            ReanchorBottomCenter(root, visual);
        }

        void ReanchorBottomCenter(GameObject root, GameObject visual)
        {
            if (root == null || visual == null || !TryGetHierarchyBounds(visual, out var bounds))
                return;

            Vector3 desired = root.transform.position;
            Vector3 current = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            visual.transform.position += desired - current;
        }

        void EnsureColliderFromVisual(GameObject root, GameObject visual, bool includeGateFront)
        {
            if (root == null || visual == null || !TryGetHierarchyBounds(visual, out var bounds))
                return;

            var box = root.GetComponent<BoxCollider>();
            if (box == null)
                box = root.AddComponent<BoxCollider>();

            var interactable = root.GetComponent<SCoLSettlementInteractable>();
            Vector3 center = root.transform.InverseTransformPoint(bounds.center);
            Vector3 size = bounds.size;

            if (interactable != null && interactable.kind == SCoLSettlementInteractableKind.Centerpiece)
            {
                float footprint = Mathf.Clamp(Mathf.Min(size.x, size.z), 2.2f, 4.2f);
                float height = Mathf.Clamp(size.y * 0.22f, 1.4f, 2.6f);
                box.center = new Vector3(center.x, height * 0.5f, center.z);
                box.size = new Vector3(footprint, height, footprint);
                return;
            }

            if (!includeGateFront)
                size.z = Mathf.Max(0.05f, size.z * 0.6f);
            box.center = center;
            box.size = size;
        }

        void ApplyTextureToRenderers(GameObject visual, Texture2D texture, string materialKey)
        {
            if (visual == null || texture == null)
                return;

            if (!MaterialCache.TryGetValue(materialKey, out var mat) || mat == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                mat = new Material(shader);
                mat.name = materialKey;
                if (mat.HasProperty("_BaseMap"))
                    mat.SetTexture("_BaseMap", texture);
                if (mat.HasProperty("_MainTex"))
                    mat.SetTexture("_MainTex", texture);
                MaterialCache[materialKey] = mat;
            }

            var renderers = visual.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
                renderers[i].sharedMaterial = mat;
        }

        static bool TryGetHierarchyBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null)
                return false;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                if (!found)
                {
                    bounds = renderers[i].bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }
            }
            return found;
        }

        static bool TryGetHierarchyLocalBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null)
                return false;

            var renderers = go.GetComponentsInChildren<Renderer>(true);
            if (renderers == null || renderers.Length == 0)
                return false;

            Matrix4x4 worldToLocal = go.transform.worldToLocalMatrix;
            bool found = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                Bounds worldBounds = renderer.bounds;
                Vector3 extents = worldBounds.extents;
                Vector3 center = worldBounds.center;

                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                {
                    Vector3 corner = center + Vector3.Scale(extents, new Vector3(x, y, z));
                    Vector3 localCorner = worldToLocal.MultiplyPoint3x4(corner);
                    if (!found)
                    {
                        bounds = new Bounds(localCorner, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(localCorner);
                    }
                }
            }

            return found;
        }

#if UNITY_EDITOR
        static GameObject LoadEditorPrefab(params string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(paths[i]))
                    continue;
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab != null)
                    return prefab;
            }
            return null;
        }

        static Texture2D LoadEditorTexture(params string[] paths)
        {
            for (int i = 0; i < paths.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(paths[i]))
                    continue;
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
                if (texture != null)
                    return texture;
            }
            return null;
        }
#endif
    }
}
