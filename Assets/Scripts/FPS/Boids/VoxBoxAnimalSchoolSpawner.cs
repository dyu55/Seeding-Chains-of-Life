using System.Collections;
using UnityEngine;
using SCoL.Voxels;
#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public class VoxBoxAnimalSchoolSpawner : MonoBehaviour
{
    [Header("References")]
    public VoxelWorld voxelWorld;
    public GameObject[] animalPrefabs;

    [Header("Spawn")]
    [Min(1)] public int animalCount = 24;
    [Tooltip("Guarantee a denser ecosystem even if old scene serialization still stores a lower animalCount.")]
    public bool enforceMinimumAnimalCount = true;
    [Min(1)] public int minimumAnimalCount = 24;
    public bool spawnOnStart = true;
    [Min(1)] public int maxSpawnAttemptsPerAnimal = 8;
    [Min(0f)] public float groundOffset = 0.02f;
    [Min(0.1f)] public float raycastHeight = 120f;
    public Vector2 randomScaleRange = new Vector2(0.35f, 0.65f);
    public LayerMask groundMask = ~0;

    [Header("Roaming")]
    [Tooltip("If true, all spawned animals roam within world-sized bounds from VoxelWorld config.")]
    public bool configureBoidBoundsFromWorld = true;
    [Min(0f)] public float boundsPadding = 4f;
    [Min(0f)] public float boidBaseSpeed = 1.6f;
    [Min(0f)] public float boidNeighborRadius = 4f;

    [Header("Interaction")]
    public bool tagAsHarvestable = true;
    public string harvestableTag = "Harvestable";

    [Header("Plant Eating")]
    public bool animalsEatMaturePlants = true;
    [Min(0.1f)] public float eatPlantRange = 1.15f;
    [Min(0.1f)] public float eatCheckIntervalSeconds = 0.4f;
    [Min(0f)] public float eatCooldownSeconds = 2.2f;
    [Min(0.05f)] public float eatHeadTouchDistance = 0.25f;
    [Min(0.1f)] public float eatHoldSeconds = 2f;
    public FPSBoidAgent.PlantEatAction eatAction = FPSBoidAgent.PlantEatAction.ResetToSprout;

    [Header("Debug")]
    public bool logSpawnInfo = false;

    private readonly System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>(128);

    IEnumerator Start()
    {
        if (!spawnOnStart) yield break;
        yield return SpawnWhenReady();
    }

    [ContextMenu("Respawn Animals")]
    public void RespawnAnimals()
    {
        StopAllCoroutines();
        StartCoroutine(SpawnWhenReady());
    }

    IEnumerator SpawnWhenReady()
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

        TryAutoAssignAnimalPrefabs();
        ClearSpawned();

        int spawnedCount = 0;
        int target = Mathf.Max(1, animalCount);
        if (enforceMinimumAnimalCount)
            target = Mathf.Max(target, Mathf.Max(1, minimumAnimalCount));
        for (int i = 0; i < target; i++)
        {
            bool spawned = false;
            for (int tries = 0; tries < Mathf.Max(1, maxSpawnAttemptsPerAnimal); tries++)
            {
                if (!TryPickSpawnPoint(out var pos))
                    continue;

                var prefab = PickPrefab();
                var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                var go = SpawnAnimal(prefab, pos, rot, i);
                if (go == null) continue;

                spawnedCount++;
                spawned = true;
                break;
            }

            if (!spawned && logSpawnInfo)
                Debug.LogWarning($"[VoxBoxAnimalSchoolSpawner] Failed to spawn animal index {i}.", this);
        }

        if (logSpawnInfo)
            Debug.Log($"[VoxBoxAnimalSchoolSpawner] Spawned {spawnedCount}/{target} animals.", this);
    }

    GameObject SpawnAnimal(GameObject prefab, Vector3 position, Quaternion rotation, int index)
    {
        GameObject go = null;
        if (prefab != null)
            go = Instantiate(prefab, position, rotation, transform);
        else
            go = CreateFallbackAnimal(position, rotation);

        if (go == null) return null;

        go.name = $"RoamingAnimal_{index:00}_{go.name}";

        float s = Random.Range(randomScaleRange.x, randomScaleRange.y);
        go.transform.localScale *= s;

        if (tagAsHarvestable)
            TrySetTag(go, harvestableTag);

        var boid = go.GetComponent<FPSBoidAgent>();
        if (boid == null)
            boid = go.AddComponent<FPSBoidAgent>();
        boid.role = FPSBoidAgent.BoidRole.Prey;
        boid.maxSpeed = boidBaseSpeed;
        boid.neighborRadius = boidNeighborRadius;
        boid.separationWeight = 0.12f;
        boid.alignmentWeight = 0f;
        boid.cohesionWeight = 0f;
        boid.useWander = true;
        boid.wanderWeight = 2f;
        boid.drag = 0.12f;
        boid.constrainToGround = true;
        boid.groundMask = groundMask;
        boid.groundOffset = groundOffset;
        boid.voxelWorld = voxelWorld;
        boid.avoidWaterColumns = true;
        boid.waterAvoidWeight = 3.4f;
        boid.waterSearchRadius = 6;
        boid.hardTurnAtWaterEdge = true;
        boid.waterEdgeLookAheadDistance = 1.1f;
        boid.waterEdgeTurnSpeedMultiplier = 1.2f;
        boid.waterEdgeExtraAvoidWeight = 3.0f;

        bool isFox = false;
        if (prefab != null)
            isFox = prefab.name.IndexOf("fox", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isFox)
            isFox = go.name.IndexOf("fox", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (isFox)
        {
            // Foxes get stricter shoreline behavior: avoid entering water and turn around at edges.
            boid.waterAvoidWeight = 7.5f;
            boid.waterSearchRadius = 10;
            boid.waterEdgeLookAheadDistance = 1.35f;
            boid.waterEdgeTurnSpeedMultiplier = 1.45f;
            boid.waterEdgeExtraAvoidWeight = 4.5f;
        }

        boid.canEatMaturePlants = animalsEatMaturePlants;
        boid.eatPlantRange = eatPlantRange;
        boid.eatCheckIntervalSeconds = eatCheckIntervalSeconds;
        boid.eatCooldownSeconds = eatCooldownSeconds;
        boid.eatHeadTouchDistance = eatHeadTouchDistance;
        boid.eatHoldSeconds = eatHoldSeconds;
        boid.eatAction = eatAction;

        if (configureBoidBoundsFromWorld)
            ApplyWorldBounds(boid);

        EnsureAnyCollider(go);
        _spawned.Add(go);
        return go;
    }

    bool TryPickSpawnPoint(out Vector3 pos)
    {
        pos = transform.position;

        if (voxelWorld != null && voxelWorld.Config != null)
        {
            int x = Random.Range(0, voxelWorld.Config.worldWidth);
            int z = Random.Range(0, voxelWorld.Config.worldDepth);
            if (!IsDryLandColumn(x, z))
                return false;

            Vector3 top = voxelWorld.ColumnTopWorld(x, z);
            pos = new Vector3(top.x, top.y + groundOffset, top.z);
            return true;
        }

        // Fallback when VoxelWorld is not present.
        Vector2 r = Random.insideUnitCircle * 25f;
        Vector3 fallbackOrigin = transform.position + new Vector3(r.x, raycastHeight, r.y);
        if (Physics.Raycast(fallbackOrigin, Vector3.down, out var hit2, raycastHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
        {
            pos = hit2.point + Vector3.up * groundOffset;
            return true;
        }

        pos = transform.position + new Vector3(r.x, 0f, r.y);
        return true;
    }

    bool IsDryLandColumn(int x, int z)
    {
        if (voxelWorld == null || voxelWorld.Config == null)
            return false;
        if (x < 0 || z < 0 || x >= voxelWorld.Config.worldWidth || z >= voxelWorld.Config.worldDepth)
            return false;

        int surfaceY = voxelWorld.GetSurfaceY(x, z);
        if (surfaceY < voxelWorld.Config.seaLevel)
            return false;

        var surfaceType = voxelWorld.GetBlock(x, surfaceY, z);
        if (surfaceType != VoxelBlockType.Grass &&
            surfaceType != VoxelBlockType.Dirt &&
            surfaceType != VoxelBlockType.Stone)
            return false;

        int aboveY = surfaceY + 1;
        if (aboveY < voxelWorld.Config.worldHeight &&
            voxelWorld.GetBlock(x, aboveY, z) == VoxelBlockType.Water)
            return false;

        return true;
    }

    GameObject PickPrefab()
    {
        if (animalPrefabs == null || animalPrefabs.Length == 0)
            return null;

        for (int i = 0; i < 8; i++)
        {
            var p = animalPrefabs[Random.Range(0, animalPrefabs.Length)];
            if (p != null) return p;
        }
        return null;
    }

    void ApplyWorldBounds(FPSBoidAgent boid)
    {
        if (boid == null || voxelWorld == null || voxelWorld.Config == null)
            return;

        var cfg = voxelWorld.Config;
        float worldW = cfg.worldWidth;
        float worldD = cfg.worldDepth;
        float minY = voxelWorld.OriginWorld.y + 0.2f;
        float maxY = voxelWorld.OriginWorld.y + Mathf.Max(2f, cfg.worldHeight * 0.35f);

        boid.useBounds = true;
        boid.boundsCenter = voxelWorld.OriginWorld + new Vector3(worldW * 0.5f, (minY + maxY) * 0.5f - voxelWorld.OriginWorld.y, worldD * 0.5f);
        boid.boundsSize = new Vector3(
            Mathf.Max(2f, worldW - boundsPadding * 2f),
            Mathf.Max(2f, maxY - minY),
            Mathf.Max(2f, worldD - boundsPadding * 2f)
        );
    }

    GameObject CreateFallbackAnimal(Vector3 position, Quaternion rotation)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.transform.SetPositionAndRotation(position, rotation);
        go.transform.SetParent(transform, true);

        var r = go.GetComponent<Renderer>();
        if (r != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader) { name = "FallbackAnimalMat" };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.85f, 0.72f, 0.45f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.85f, 0.72f, 0.45f));
            r.sharedMaterial = mat;
        }

        return go;
    }

    void EnsureAnyCollider(GameObject root)
    {
        if (root == null) return;
        if (root.GetComponentInChildren<Collider>() != null)
            return;

        var mf = root.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true;
            return;
        }

        root.AddComponent<BoxCollider>();
    }

    void TrySetTag(GameObject go, string tagValue)
    {
        if (go == null) return;
        try
        {
            go.tag = tagValue;
        }
        catch (UnityException)
        {
            Debug.LogWarning($"[VoxBoxAnimalSchoolSpawner] Tag '{tagValue}' does not exist.", go);
        }
    }

    void ClearSpawned()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i]);
        }
        _spawned.Clear();
    }

    void TryAutoAssignAnimalPrefabs()
    {
        if (animalPrefabs != null && animalPrefabs.Length > 0)
            return;

#if UNITY_EDITOR
        string[] paths =
        {
            "Assets/VoxBox/Prefabs/Animals/Rabbit.prefab",
            "Assets/VoxBox/Prefabs/Animals/Fox.prefab",
            "Assets/VoxBox/Prefabs/Animals/Deer.prefab",
            "Assets/VoxBox/Prefabs/Animals/Dog.prefab",
            "Assets/VoxBox/Prefabs/Animals/Cat.prefab",
            "Assets/VoxBox/Prefabs/Animals/Bear.prefab",
            "Assets/VoxBox/Prefabs/Animals/Horse.prefab",
            "Assets/VoxBox/Prefabs/Animals/Bison.prefab",
            "Assets/VoxBox/Prefabs/Animals/Giraffe.prefab",
            "Assets/VoxBox/Prefabs/Animals/Elephant.prefab",
            "Assets/VoxBox/Prefabs/Animals/Lion.prefab",
            "Assets/VoxBox/Prefabs/Animals/Tiger.prefab",
            "Assets/VoxBox/Prefabs/Animals/Cheetah.prefab",
        };

        var list = new System.Collections.Generic.List<GameObject>(paths.Length);
        for (int i = 0; i < paths.Length; i++)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
            if (p != null) list.Add(p);
        }

        if (list.Count > 0)
            animalPrefabs = list.ToArray();
#endif
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        if (FindFirstObjectByType<VoxBoxAnimalSchoolSpawner>() != null)
            return;

        var go = new GameObject("VoxBoxAnimalSchoolSpawner (Runtime)");
        go.AddComponent<VoxBoxAnimalSchoolSpawner>();
    }
}
