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
    [Tooltip("Offset (in blocks) from sea level. Keep values negative to stay below surface.")]
    public Vector2 spawnYOffsetFromSea = new Vector2(-0.85f, -0.25f);
    public bool spawnOnStart = true;

    [Header("Interaction")]
    public bool tagAsHarvestable = false;
    public string harvestableTag = "Harvestable";
    public bool disableFishColliders = true;

    [Header("Debug")]
    public bool logSpawnInfo = false;

    private readonly List<Vector3> _waterAnchors = new List<Vector3>(512);
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
        for (int i = 0; i < Mathf.Max(1, fishCount); i++)
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
            Debug.Log($"[VoxBoxFishSchool] Spawned {spawnedCount} fish from {_waterAnchors.Count} water anchors.", this);
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
        float s = Random.Range(spawnScaleRange.x, spawnScaleRange.y);
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

        for (int z = 0; z < depth; z += step)
        {
            for (int x = 0; x < width; x += step)
            {
                if (voxelWorld.GetBlock(x, seaLevel, z) != VoxelBlockType.Water)
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

    private void TryAutoAssignFishPrefab()
    {
        if (fishPrefab != null) return;
#if UNITY_EDITOR
        fishPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VoxBox/Prefabs/Sea Creatures/Fish.prefab");
#endif
    }

    public bool IsInWaterColumn(Vector3 worldPos)
    {
        if (voxelWorld == null || voxelWorld.Config == null)
            return false;

        if (!voxelWorld.TryWorldToColumn(worldPos, out int x, out int z))
            return false;

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

    [HideInInspector] public VoxelWorld voxelWorld;
    [HideInInspector] public VoxBoxFishSchool school;
    [HideInInspector] public Vector3 homeAnchor;
    [HideInInspector] public Vector3 velocity;

    private static readonly List<VoxBoxFishBoidAgent> Active = new List<VoxBoxFishBoidAgent>(256);
    private float _wanderSeed;

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

        if (velocity.sqrMagnitude > 0.0001f)
        {
            var targetRot = Quaternion.LookRotation(velocity.normalized, Vector3.up);
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

            if (!school.IsInWaterColumn(myPos))
            {
                if (school.TryGetNearestWaterAnchor(myPos, out var anchor))
                    accel += SteerTowards(anchor - myPos) * waterReturnWeight;
                else
                    accel += SteerTowards(homeAnchor - myPos) * waterReturnWeight;
            }
        }

        if (voxelWorld != null && voxelWorld.Config != null)
        {
            float targetY = voxelWorld.OriginWorld.y + voxelWorld.Config.seaLevel - Mathf.Abs(cruiseDepthBelowSea);
            float yDelta = Mathf.Clamp(targetY - myPos.y, -1f, 1f);
            accel += Vector3.up * (yDelta * depthHoldWeight);
        }

        if (accel.magnitude > maxForce)
            accel = accel.normalized * maxForce;

        return accel;
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
