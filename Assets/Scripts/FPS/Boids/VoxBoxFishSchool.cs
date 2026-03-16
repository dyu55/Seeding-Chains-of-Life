using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using SCoL.Voxels;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class VoxBoxFishSchool : MonoBehaviour
{
    [Header("References")]
    public VoxelWorld voxelWorld;
    public GameObject fishPrefab;

    [Header("Spawn")]
    [Min(1)] public int fishCount = 24;
    [Min(1)] public int waterSampleStep = 3;
    public Vector2 spawnScaleRange = new Vector2(0.45f, 0.75f);
    [Tooltip("Prefer enclosed inland lakes (water regions not connected to map edge).")]
    public bool spawnOnlyInEnclosedLakes = true;
    [Tooltip("If true and no enclosed lakes exist, fall back to any water region.")]
    public bool fallbackToAnyWaterIfNoLakes = true;
    [Header("Runtime Multipliers")]
    [Tooltip("Final spawned fish count = fishCount * fishCountMultiplier.")]
    [Min(0.1f)] public float fishCountMultiplier = 4f;
    [Tooltip("Final fish scale multiplier. 0.333 means 3x smaller fish.")]
    [Min(0.01f)] public float fishScaleMultiplier = 1f / 3f;
    [Tooltip("Offset (in blocks) from sea level. Keep values negative to stay below surface.")]
    public Vector2 spawnYOffsetFromSea = new Vector2(-0.85f, -0.25f);
    public bool spawnOnStart = true;

    [Header("Interaction")]
    public bool tagAsHarvestable = false;
    public string harvestableTag = "Harvestable";
    public bool disableFishColliders = true;
    [Tooltip("If true, auto-replace legacy VoxBox fish prefab with local goldfish model for lake spawning.")]
    public bool preferGoldfishModel = true;

    [Header("Debug")]
    public bool logSpawnInfo = false;

    private readonly List<Vector3> _waterAnchors = new List<Vector3>(512);
    private bool[,] _allowedWaterColumns;
    private readonly List<VoxBoxFishBoidAgent> _spawned = new List<VoxBoxFishBoidAgent>(64);
    private Bounds _waterBounds;
    private bool _hasWaterBounds;

    public IReadOnlyList<Vector3> WaterAnchors => _waterAnchors;
    public bool HasWaterBounds => _hasWaterBounds;
    public Bounds WaterBounds => _waterBounds;

    private IEnumerator Start()
    {
        if (!spawnOnStart) yield break;
        yield return SpawnWhenReady();
    }

    [ContextMenu("Respawn Fish")]
    public void RespawnFish()
    {
        StopAllCoroutines();
        StartCoroutine(SpawnWhenReady());
    }

    private IEnumerator SpawnWhenReady()
    {
        float timeoutAt = Time.realtimeSinceStartup + 6f;

        while (Time.realtimeSinceStartup < timeoutAt)
        {
            if (voxelWorld == null)
                voxelWorld = FindFirstObjectByType<VoxelWorld>();

            if (voxelWorld != null && voxelWorld.Config != null)
                break;

            yield return null;
        }

        if (voxelWorld == null || voxelWorld.Config == null)
        {
            Debug.LogWarning("[VoxBoxFishSchool] VoxelWorld not ready; fish school not spawned.", this);
            yield break;
        }

        TryAutoAssignFishPrefab();
        BuildWaterAnchors();
        ClearSpawned();

        if (_waterAnchors.Count == 0)
        {
            Debug.LogWarning("[VoxBoxFishSchool] No water columns found in voxel world.");
            yield break;
        }

        int spawnedCount = 0;
        // Keep an explicit runtime floor so already-serialized scene values also get a denser school.
        float effectiveCountMultiplier = Mathf.Max(4f, fishCountMultiplier);
        int targetCount = Mathf.Max(1, Mathf.RoundToInt(effectiveCountMultiplier * fishCount));
        for (int i = 0; i < targetCount; i++)
        {
            if (!TryPickSpawnPoint(out var spawnPos))
                continue;

            var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            var fish = SpawnFish(spawnPos, rot, i);
            if (fish == null) continue;

            spawnedCount++;
        }

        if (logSpawnInfo)
        {
            Debug.Log($"[VoxBoxFishSchool] Spawned {spawnedCount}/{targetCount} fish from {_waterAnchors.Count} water anchors.", this);
        }
    }

    private GameObject SpawnFish(Vector3 position, Quaternion rotation, int index)
    {
        GameObject fish;
        if (fishPrefab != null)
        {
            fish = Instantiate(fishPrefab, position, rotation, transform);
        }
        else
        {
            fish = CreateFallbackFish(position, rotation);
        }

        if (fish == null) return null;

        fish.name = $"Fish_{index:00}";
        float baseScale = Random.Range(spawnScaleRange.x, spawnScaleRange.y);
        float s = baseScale * Mathf.Max(0.01f, fishScaleMultiplier);
        fish.transform.localScale *= s;

        if (disableFishColliders)
            DisableAllColliders(fish);

        if (tagAsHarvestable)
            TrySetTag(fish, harvestableTag);

        var boid = fish.GetComponent<VoxBoxFishBoidAgent>();
        if (boid == null)
            boid = fish.AddComponent<VoxBoxFishBoidAgent>();

        boid.school = this;
        boid.voxelWorld = voxelWorld;
        boid.homeAnchor = _waterAnchors[Random.Range(0, _waterAnchors.Count)];

        _spawned.Add(boid);
        return fish;
    }

    private GameObject CreateFallbackFish(Vector3 position, Quaternion rotation)
    {
        var fish = GameObject.CreatePrimitive(PrimitiveType.Cube);
        fish.transform.SetParent(transform, worldPositionStays: false);
        fish.transform.SetPositionAndRotation(position, rotation);
        fish.transform.localScale = new Vector3(0.35f, 0.18f, 0.65f);

        var renderer = fish.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = "FallbackFishMat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.30f, 0.75f, 0.90f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.30f, 0.75f, 0.90f));
            renderer.sharedMaterial = mat;
        }

        return fish;
    }

    private void ClearSpawned()
    {
        _spawned.Clear();
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            var child = transform.GetChild(i);
            if (child != null)
                Destroy(child.gameObject);
        }
    }

    private bool TryPickSpawnPoint(out Vector3 worldPos)
    {
        worldPos = default;
        if (_waterAnchors.Count == 0) return false;

        var anchor = _waterAnchors[Random.Range(0, _waterAnchors.Count)];
        float jitter = Mathf.Max(0.35f, waterSampleStep * 0.30f);

        worldPos = anchor + new Vector3(
            Random.Range(-jitter, jitter),
            Random.Range(-0.15f, 0.15f),
            Random.Range(-jitter, jitter));

        if (!IsInWaterColumn(worldPos))
            worldPos = anchor;

        return true;
    }

    private void BuildWaterAnchors()
    {
        _waterAnchors.Clear();
        _hasWaterBounds = false;
        _allowedWaterColumns = null;

        if (voxelWorld == null || voxelWorld.Config == null)
            return;

        int width = voxelWorld.Config.worldWidth;
        int depth = voxelWorld.Config.worldDepth;
        int seaLevel = voxelWorld.Config.seaLevel;
        int step = Mathf.Max(1, waterSampleStep);

        float minOffset = Mathf.Min(spawnYOffsetFromSea.x, spawnYOffsetFromSea.y);
        float maxOffset = Mathf.Max(spawnYOffsetFromSea.x, spawnYOffsetFromSea.y);
        float midOffset = (minOffset + maxOffset) * 0.5f;

        bool hasBounds = false;
        Vector3 min = Vector3.zero;
        Vector3 max = Vector3.zero;

        bool[,] waterMask = new bool[width, depth];
        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
            waterMask[x, z] = voxelWorld.GetBlock(x, seaLevel, z) == VoxelBlockType.Water;

        _allowedWaterColumns = BuildAllowedWaterMask(waterMask, spawnOnlyInEnclosedLakes, fallbackToAnyWaterIfNoLakes);

        for (int z = 0; z < depth; z += step)
        for (int x = 0; x < width; x += step)
        {
            if (_allowedWaterColumns == null || !_allowedWaterColumns[x, z])
                continue;

            Vector3 anchor = voxelWorld.OriginWorld + new Vector3(x + 0.5f, seaLevel + midOffset, z + 0.5f);
            _waterAnchors.Add(anchor);

            if (!hasBounds)
            {
                min = max = anchor;
                hasBounds = true;
            }
            else
            {
                min = Vector3.Min(min, anchor);
                max = Vector3.Max(max, anchor);
            }
        }

        if (!hasBounds) return;

        float yMin = voxelWorld.OriginWorld.y + seaLevel + minOffset - 0.4f;
        float yMax = voxelWorld.OriginWorld.y + seaLevel + maxOffset + 0.4f;

        var center = (min + max) * 0.5f;
        center.y = (yMin + yMax) * 0.5f;

        var size = new Vector3(
            Mathf.Max(2f, max.x - min.x + 2f),
            Mathf.Max(1.5f, yMax - yMin),
            Mathf.Max(2f, max.z - min.z + 2f));

        _waterBounds = new Bounds(center, size);
        _hasWaterBounds = true;
    }

    private static bool[,] BuildAllowedWaterMask(bool[,] waterMask, bool enclosedOnly, bool fallbackAny)
    {
        if (waterMask == null)
            return null;

        int width = waterMask.GetLength(0);
        int depth = waterMask.GetLength(1);
        if (width <= 0 || depth <= 0)
            return waterMask;

        if (!enclosedOnly)
            return waterMask;

        bool[,] visited = new bool[width, depth];
        bool[,] allowed = new bool[width, depth];
        var queue = new Queue<Vector2Int>(256);
        var region = new List<Vector2Int>(512);
        bool hasEnclosed = false;
        var dirs = new[]
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };

        for (int z = 0; z < depth; z++)
        for (int x = 0; x < width; x++)
        {
            if (visited[x, z] || !waterMask[x, z])
                continue;

            region.Clear();
            queue.Clear();
            queue.Enqueue(new Vector2Int(x, z));
            visited[x, z] = true;
            bool touchesEdge = false;

            while (queue.Count > 0)
            {
                var p = queue.Dequeue();
                region.Add(p);
                if (p.x == 0 || p.y == 0 || p.x == width - 1 || p.y == depth - 1)
                    touchesEdge = true;

                for (int i = 0; i < dirs.Length; i++)
                {
                    int nx = p.x + dirs[i].x;
                    int nz = p.y + dirs[i].y;
                    if (nx < 0 || nz < 0 || nx >= width || nz >= depth)
                        continue;
                    if (visited[nx, nz] || !waterMask[nx, nz])
                        continue;
                    visited[nx, nz] = true;
                    queue.Enqueue(new Vector2Int(nx, nz));
                }
            }

            if (!touchesEdge)
            {
                hasEnclosed = true;
                for (int i = 0; i < region.Count; i++)
                {
                    var p = region[i];
                    allowed[p.x, p.y] = true;
                }
            }
        }

        if (!hasEnclosed && fallbackAny)
            return waterMask;
        return hasEnclosed ? allowed : waterMask;
    }

    private void TryAutoAssignFishPrefab()
    {
        if (!preferGoldfishModel && fishPrefab != null) return;
#if UNITY_EDITOR
        bool shouldAssignGoldfish = fishPrefab == null;
        if (!shouldAssignGoldfish && preferGoldfishModel)
        {
            string n = fishPrefab.name != null ? fishPrefab.name.ToLowerInvariant() : string.Empty;
            string p = AssetDatabase.GetAssetPath(fishPrefab);
            string lp = string.IsNullOrEmpty(p) ? string.Empty : p.ToLowerInvariant();
            shouldAssignGoldfish =
                n == "fish" ||
                n.Contains("voxbox") ||
                lp.Contains("/voxbox/");
        }

        if (shouldAssignGoldfish)
        {
            var goldfish = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/goldfish/goldfish.obj");
            if (goldfish != null)
            {
                fishPrefab = goldfish;
                return;
            }
        }

        if (fishPrefab == null)
            fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VoxBox/Prefabs/Sea Creatures/Fish.prefab");
#endif
    }

    public bool IsInWaterColumn(Vector3 worldPos)
    {
        if (voxelWorld == null || voxelWorld.Config == null)
            return false;

        if (!voxelWorld.TryWorldToColumn(worldPos, out int x, out int z))
            return false;

        if (_allowedWaterColumns != null)
        {
            int w = _allowedWaterColumns.GetLength(0);
            int d = _allowedWaterColumns.GetLength(1);
            if (x < 0 || z < 0 || x >= w || z >= d || !_allowedWaterColumns[x, z])
                return false;
        }

        int localY = Mathf.Clamp(
            Mathf.FloorToInt(worldPos.y - voxelWorld.OriginWorld.y),
            0,
            voxelWorld.Config.worldHeight - 1);

        if (voxelWorld.GetBlock(x, localY, z) == VoxelBlockType.Water)
            return true;

        int sea = voxelWorld.Config.seaLevel;
        return voxelWorld.GetBlock(x, sea, z) == VoxelBlockType.Water;
    }

    public bool TryGetNearestWaterAnchor(Vector3 worldPos, out Vector3 nearest)
    {
        nearest = worldPos;
        if (_waterAnchors.Count == 0) return false;

        float best = float.PositiveInfinity;
        for (int i = 0; i < _waterAnchors.Count; i++)
        {
            float d = (worldPos - _waterAnchors[i]).sqrMagnitude;
            if (d < best)
            {
                best = d;
                nearest = _waterAnchors[i];
            }
        }
        return true;
    }

    public bool TryGetRoamWaterAnchor(Vector3 worldPos, Vector3 preferredForward, float minDistance, float maxDistance, out Vector3 anchor)
    {
        anchor = worldPos;
        if (_waterAnchors.Count == 0)
            return false;

        float minDistSq = Mathf.Max(0.01f, minDistance) * Mathf.Max(0.01f, minDistance);
        float maxDist = Mathf.Max(minDistance + 0.25f, maxDistance);
        float maxDistSq = maxDist * maxDist;

        Vector3 planarForward = new Vector3(preferredForward.x, 0f, preferredForward.z);
        if (planarForward.sqrMagnitude > 0.0001f)
            planarForward.Normalize();
        else
            planarForward = Random.insideUnitSphere;

        bool foundPreferred = false;
        float bestScore = float.NegativeInfinity;
        int samples = Mathf.Clamp(_waterAnchors.Count, 10, 36);

        for (int i = 0; i < samples; i++)
        {
            Vector3 candidate = _waterAnchors[Random.Range(0, _waterAnchors.Count)];
            Vector3 delta = candidate - worldPos;
            float distSq = delta.sqrMagnitude;
            if (distSq < minDistSq || distSq > maxDistSq)
                continue;

            Vector3 planarDelta = new Vector3(delta.x, 0f, delta.z);
            if (planarDelta.sqrMagnitude < 0.0001f)
                continue;

            float heading = Vector3.Dot(planarForward, planarDelta.normalized);
            float distScore = Mathf.InverseLerp(minDistSq, maxDistSq, distSq);
            float score = heading * 1.35f + distScore * 0.65f + Random.Range(0f, 0.25f);
            if (score <= bestScore)
                continue;

            bestScore = score;
            anchor = candidate;
            foundPreferred = true;
        }

        if (foundPreferred)
            return true;

        return TryGetNearestWaterAnchor(worldPos, out anchor);
    }

    private static void DisableAllColliders(GameObject go)
    {
        var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
        for (int i = 0; i < colliders.Length; i++)
        {
            if (colliders[i] != null)
                colliders[i].enabled = false;
        }
    }

    private static void TrySetTag(GameObject go, string tagName)
    {
        if (go == null) return;
        try
        {
            go.tag = tagName;
        }
        catch (UnityException)
        {
            // Tag not defined. Keep running without hard failure.
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExists()
    {
        if (Object.FindFirstObjectByType<VoxBoxFishSchool>() != null) return;
        var go = new GameObject("VoxBoxFishSchool");
        go.AddComponent<VoxBoxFishSchool>();
    }
}

[DisallowMultipleComponent]
public class VoxBoxFishBoidAgent : MonoBehaviour
{
    [Header("Neighborhood")]
    public float neighborRadius = 3.5f;
    public float separationRadius = 1.2f;

    [Header("Forces")]
    public float separationWeight = 1.8f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 1.1f;
    public float wanderWeight = 0.5f;
    public float waterReturnWeight = 2.2f;
    public float boundsWeight = 1.0f;
    public float depthHoldWeight = 1.3f;

    [Header("Motion")]
    public float maxSpeed = 2.2f;
    public float minSpeed = 0.7f;
    public float maxForce = 5.0f;
    public float drag = 0.35f;
    public float rotationLerp = 8f;
    public float cruiseDepthBelowSea = 0.55f;
    [Range(0f, 35f)] public float maxPitchAngle = 12f;
    [Header("Lake Clamp")]
    [Tooltip("Hard-correct fish back toward nearest valid water anchor when they leave water.")]
    public bool hardClampToLake = true;
    [Range(1f, 30f)] public float hardClampLerpSpeed = 10f;
    [Min(0.05f)] public float terrainClearance = 0.28f;
    [Min(0.05f)] public float surfaceClearance = 0.18f;
    [Min(0.01f)] public float surfaceSoftBand = 0.22f;
    [Min(0.1f)] public float surfacePushDownSpeed = 0.55f;
    [Min(0.05f)] public float horizontalRecenterDistance = 0.85f;
    [Min(0.1f)] public float anchorReachDistance = 0.75f;
    [Min(0.1f)] public float retargetAnchorInterval = 3.5f;
    [Min(0.25f)] public float anchorMinTravelDistance = 2.5f;
    [Min(0.5f)] public float anchorMaxTravelDistance = 10f;
    [Min(0.25f)] public float stuckRetargetSeconds = 1.25f;

    [HideInInspector] public VoxelWorld voxelWorld;
    [HideInInspector] public VoxBoxFishSchool school;
    [HideInInspector] public Vector3 homeAnchor;
    [HideInInspector] public Vector3 velocity;

    private static readonly List<VoxBoxFishBoidAgent> Active = new List<VoxBoxFishBoidAgent>(256);
    private float _wanderSeed;
    private Vector3 _currentAnchor;
    private float _nextAnchorRetargetAt;
    private Vector3 _lastSamplePosition;
    private float _stuckTimer;

    private void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    private void Start()
    {
        if (velocity.sqrMagnitude < 0.01f)
            velocity = Random.onUnitSphere * (maxSpeed * 0.6f);

        _wanderSeed = Random.Range(1f, 10000f);
        _currentAnchor = homeAnchor;
        _nextAnchorRetargetAt = Time.time + Random.Range(0.5f, Mathf.Max(0.6f, retargetAnchorInterval));
        _lastSamplePosition = transform.position;
        _stuckTimer = 0f;
    }

    private void Update()
    {
        Vector3 accel = ComputeAcceleration();
        velocity += accel * Time.deltaTime;

        velocity = Vector3.Lerp(velocity, Vector3.zero, drag * Time.deltaTime);

        float speed = velocity.magnitude;
        if (speed > maxSpeed)
            velocity = velocity / speed * maxSpeed;
        else if (speed < minSpeed)
            velocity = velocity.sqrMagnitude > 0.0001f ? velocity.normalized * minSpeed : transform.forward * minSpeed;

        transform.position += velocity * Time.deltaTime;

        if (school != null && hardClampToLake)
            EnforceWaterVolume();

        Vector3 planarDelta = Vector3.ProjectOnPlane(transform.position - _lastSamplePosition, Vector3.up);
        if (planarDelta.sqrMagnitude < 0.0008f)
            _stuckTimer += Time.deltaTime;
        else
            _stuckTimer = Mathf.Max(0f, _stuckTimer - Time.deltaTime * 2.5f);
        _lastSamplePosition = transform.position;

        if (_stuckTimer >= Mathf.Max(0.25f, stuckRetargetSeconds))
        {
            ForceRetargetAnchor(transform.position, boostVelocity: true);
            _stuckTimer = 0f;
        }

        if (velocity.sqrMagnitude > 0.0001f)
        {
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
            if (planar.sqrMagnitude < 0.0001f)
                planar = transform.forward;
            else
                planar.Normalize();

            float planarSpeed = Mathf.Max(0.0001f, new Vector2(velocity.x, velocity.z).magnitude);
            float pitch = Mathf.Atan2(velocity.y, planarSpeed) * Mathf.Rad2Deg;
            pitch = Mathf.Clamp(pitch, -Mathf.Abs(maxPitchAngle), Mathf.Abs(maxPitchAngle));

            var targetRot = Quaternion.LookRotation(planar, Vector3.up) * Quaternion.Euler(-pitch, 0f, 0f);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRot, rotationLerp * Time.deltaTime);
        }
    }

    private Vector3 ComputeAcceleration()
    {
        Vector3 myPos = transform.position;
        float neighborRadiusSqr = neighborRadius * neighborRadius;
        float separationRadiusSqr = separationRadius * separationRadius;

        Vector3 separation = Vector3.zero;
        Vector3 alignment = Vector3.zero;
        Vector3 cohesionCenter = Vector3.zero;

        int count = 0;
        int sepCount = 0;

        for (int i = 0; i < Active.Count; i++)
        {
            var other = Active[i];
            if (other == null || other == this) continue;

            Vector3 toOther = other.transform.position - myPos;
            float dSqr = toOther.sqrMagnitude;
            if (dSqr <= 0.000001f || dSqr > neighborRadiusSqr) continue;

            alignment += other.velocity;
            cohesionCenter += other.transform.position;
            count++;

            if (dSqr < separationRadiusSqr)
            {
                separation += (-toOther) / dSqr;
                sepCount++;
            }
        }

        Vector3 accel = Vector3.zero;

        if (sepCount > 0)
        {
            separation /= sepCount;
            accel += SteerTowards(separation) * separationWeight;
        }

        if (count > 0)
        {
            alignment /= count;
            accel += SteerTowards(alignment) * alignmentWeight;

            cohesionCenter /= count;
            accel += SteerTowards(cohesionCenter - myPos) * cohesionWeight;
        }

        float t = Time.time;
        var wander = new Vector3(
            Mathf.PerlinNoise(_wanderSeed, t * 0.33f) - 0.5f,
            Mathf.PerlinNoise(_wanderSeed + 19.3f, t * 0.55f) - 0.5f,
            Mathf.PerlinNoise(_wanderSeed + 53.7f, t * 0.41f) - 0.5f);
        accel += SteerTowards(wander) * wanderWeight;

        if (school != null)
        {
            if (school.HasWaterBounds && !school.WaterBounds.Contains(myPos))
            {
                accel += SteerTowards(school.WaterBounds.center - myPos) * boundsWeight;
            }

            UpdateAnchorTarget(myPos);
            Vector3 toAnchor = _currentAnchor - myPos;
            toAnchor.y *= 0.35f;
            if (toAnchor.sqrMagnitude > Mathf.Max(0.1f, anchorReachDistance) * Mathf.Max(0.1f, anchorReachDistance))
                accel += SteerTowards(toAnchor) * (waterReturnWeight * 0.42f);
        }

        if (voxelWorld != null && voxelWorld.Config != null)
        {
            float targetY = voxelWorld.OriginWorld.y + voxelWorld.Config.seaLevel - Mathf.Abs(cruiseDepthBelowSea);
            float maxAllowedTargetY = voxelWorld.OriginWorld.y + voxelWorld.Config.seaLevel - Mathf.Max(0.08f, surfaceClearance + surfaceSoftBand * 0.5f);
            targetY = Mathf.Min(targetY, maxAllowedTargetY);
            float yDelta = Mathf.Clamp(targetY - myPos.y, -1f, 1f);
            accel += Vector3.up * (yDelta * depthHoldWeight);

                float waterSurfaceY = voxelWorld.OriginWorld.y + voxelWorld.Config.seaLevel - Mathf.Max(0.05f, surfaceClearance);
                float softBand = Mathf.Max(0.01f, surfaceSoftBand);
                if (myPos.y > waterSurfaceY - softBand)
                {
                    float surfaceT = Mathf.InverseLerp(waterSurfaceY - softBand, waterSurfaceY, myPos.y);
                    accel += Vector3.down * (depthHoldWeight * 1.8f * surfaceT);
                }
            }

        if (accel.magnitude > maxForce)
            accel = accel.normalized * maxForce;

        return accel;
    }

    private void EnforceWaterVolume()
    {
        if (school == null || voxelWorld == null || voxelWorld.Config == null)
            return;

        Vector3 p = transform.position;
        bool inWater = school.IsInWaterColumn(p);

        float waterSurfaceY = voxelWorld.OriginWorld.y + voxelWorld.Config.seaLevel - Mathf.Max(0.05f, surfaceClearance);
        float terrainY = float.NegativeInfinity;
        bool hasTerrain = voxelWorld.TryGetTerrainSurfaceYAtWorld(p, out terrainY, includeWaterSurface: false);
        float minY = hasTerrain ? terrainY + Mathf.Max(0.05f, terrainClearance) : float.NegativeInfinity;
        bool tooLow = hasTerrain && p.y < minY;
        bool tooHigh = p.y > waterSurfaceY;
        bool invalidShallowColumn = hasTerrain && minY >= waterSurfaceY - 0.02f;

        bool needsHorizontalRecovery = !inWater || invalidShallowColumn;
        bool needsVerticalClamp = tooLow || tooHigh;

        if (needsHorizontalRecovery || needsVerticalClamp)
        {
            if (school.TryGetNearestWaterAnchor(p, out var nearest))
            {
                _currentAnchor = nearest;
                _nextAnchorRetargetAt = Time.time + Mathf.Max(0.5f, retargetAnchorInterval * 0.5f);
                float t = Mathf.Clamp01(Time.deltaTime * Mathf.Max(1f, hardClampLerpSpeed));
                if (needsHorizontalRecovery)
                {
                    Vector3 target = nearest;
                    if (voxelWorld.TryGetTerrainSurfaceYAtWorld(nearest, out float nearestTerrainY, includeWaterSurface: false))
                        target.y = Mathf.Clamp(target.y, nearestTerrainY + Mathf.Max(0.05f, terrainClearance), waterSurfaceY);
                    else
                        target.y = Mathf.Min(target.y, waterSurfaceY);

                    p = Vector3.Lerp(p, target, t);
                }
                else
                {
                    p.y = Mathf.Clamp(p.y, minY, waterSurfaceY);
                }

                transform.position = p;

                Vector3 toAnchor = nearest - transform.position;
                Vector3 toAnchorPlanar = new Vector3(toAnchor.x, 0f, toAnchor.z);
                float planarDist = toAnchorPlanar.magnitude;
                if (needsHorizontalRecovery && planarDist > Mathf.Max(0.05f, horizontalRecenterDistance))
                {
                    Vector3 dir = toAnchorPlanar.normalized;
                    float speed = Mathf.Max(minSpeed, velocity.magnitude);
                    Vector3 desired = new Vector3(dir.x * speed, velocity.y * 0.35f, dir.z * speed);
                    velocity = Vector3.Lerp(velocity, desired, t);
                }
                else if (needsVerticalClamp)
                {
                    velocity.y = Mathf.Lerp(velocity.y, 0f, t);
                }

                if (tooHigh)
                {
                    float downSpeed = Mathf.Max(0.1f, surfacePushDownSpeed);
                    velocity.y = Mathf.Min(velocity.y, -downSpeed);
                }
            }
            else
            {
                if (tooHigh)
                    p.y = waterSurfaceY;
                if (tooLow)
                    p.y = minY;
                transform.position = p;
                if (tooHigh)
                    velocity.y = Mathf.Min(velocity.y, -Mathf.Max(0.1f, surfacePushDownSpeed));
            }
            return;
        }

        p.y = Mathf.Clamp(p.y, minY, waterSurfaceY);
        transform.position = p;
        if (p.y >= waterSurfaceY - Mathf.Max(0.01f, surfaceSoftBand * 0.35f))
            velocity.y = Mathf.Min(velocity.y, -Mathf.Max(0.05f, surfacePushDownSpeed * 0.6f));
    }

    private void UpdateAnchorTarget(Vector3 myPos)
    {
        if (school == null)
            return;

        if (_currentAnchor == Vector3.zero)
            _currentAnchor = homeAnchor;

        float reach = Mathf.Max(0.1f, anchorReachDistance);
        float reachSq = reach * reach;
        bool reached = (_currentAnchor - myPos).sqrMagnitude <= reachSq;
        bool timedOut = Time.time >= _nextAnchorRetargetAt;

        if (!reached && !timedOut)
            return;

        ForceRetargetAnchor(myPos, boostVelocity: false);
    }

    private void ForceRetargetAnchor(Vector3 myPos, bool boostVelocity)
    {
        Vector3 preferredForward = velocity.sqrMagnitude > 0.001f ? velocity : transform.forward;
        if (!school.TryGetRoamWaterAnchor(
                myPos,
                preferredForward,
                Mathf.Max(0.25f, anchorMinTravelDistance),
                Mathf.Max(anchorMinTravelDistance + 0.25f, anchorMaxTravelDistance),
                out var nextAnchor))
        {
            nextAnchor = homeAnchor;
        }

        Vector3 jitter = new Vector3(
            Random.Range(-0.55f, 0.55f),
            Random.Range(-0.08f, 0.08f),
            Random.Range(-0.55f, 0.55f));
        _currentAnchor = nextAnchor + jitter;
        _nextAnchorRetargetAt = Time.time + Random.Range(
            Mathf.Max(0.8f, retargetAnchorInterval * 0.65f),
            Mathf.Max(1.0f, retargetAnchorInterval * 1.35f));

        if (!boostVelocity)
            return;

        Vector3 toAnchor = _currentAnchor - myPos;
        Vector3 planar = new Vector3(toAnchor.x, 0f, toAnchor.z);
        if (planar.sqrMagnitude < 0.001f)
            return;

        Vector3 dir = planar.normalized;
        float speed = Mathf.Clamp(Mathf.Max(minSpeed * 1.2f, velocity.magnitude), minSpeed, maxSpeed);
        velocity = new Vector3(dir.x * speed, Mathf.Clamp(velocity.y, -0.18f, 0.18f), dir.z * speed);
    }

    private Vector3 SteerTowards(Vector3 desired)
    {
        if (desired.sqrMagnitude < 0.0001f)
            return Vector3.zero;

        Vector3 desiredVel = desired.normalized * maxSpeed;
        Vector3 steer = desiredVel - velocity;
        float mag = steer.magnitude;
        if (mag > maxForce)
            steer = steer / mag * maxForce;
        return steer;
    }
}
