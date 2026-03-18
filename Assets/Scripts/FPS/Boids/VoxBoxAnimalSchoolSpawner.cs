using System.Collections;
using UnityEngine;
using SCoL.Voxels;
using SCoL.Combat;
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
    [Min(0)] public int wolfCount = 4;
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
    [Min(0.1f)] public float eatHoldSeconds = 3f;
    public FPSBoidAgent.PlantEatAction eatAction = FPSBoidAgent.PlantEatAction.ResetToSprout;

    [Header("Debug")]
    public bool logSpawnInfo = false;

    [Header("Respawn")]
    public bool maintainPopulationByRespawning = true;
    [Min(0f)] public float respawnDelaySeconds = 4.5f;

    [Header("Death Feedback")]
    public bool enableDeathFeedback = true;
    public bool enableDeathDrops = true;
    [Min(1)] public int wolfStoneDropAmount = 2;
    [Min(1)] public int herbivorePlantDropAmount = 1;
    [Range(0f, 1f)] public float herbivoreSeedDropChance = 0.4f;

    private readonly System.Collections.Generic.List<GameObject> _spawned = new System.Collections.Generic.List<GameObject>(128);
    int _spawnSerial;
    GameObject[] _stoneDropPrefabs;

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
        EnsureFoxAndDeerPrefabs();
        ClearSpawned();
        _spawnSerial = 0;

        int spawnedCount = 0;
        int herbivoreTarget = Mathf.Max(1, animalCount);
        if (enforceMinimumAnimalCount)
            herbivoreTarget = Mathf.Max(herbivoreTarget, Mathf.Max(1, minimumAnimalCount));
        int hostileWolves = Mathf.Max(0, wolfCount);
        int totalTarget = herbivoreTarget + hostileWolves;
        for (int i = 0; i < totalTarget; i++)
        {
            bool spawnWolf = i < hostileWolves;
            bool spawned = false;
            for (int tries = 0; tries < Mathf.Max(1, maxSpawnAttemptsPerAnimal); tries++)
            {
                if (!TryPickSpawnPoint(i, totalTarget, out var pos))
                    continue;

                var prefab = PickPrefab(i, herbivoreTarget, hostileWolves, spawnWolf);
                var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                var go = SpawnAnimal(prefab, pos, rot, _spawnSerial++, spawnWolf);
                if (go == null) continue;

                spawnedCount++;
                spawned = true;
                break;
            }

            if (!spawned && logSpawnInfo)
                Debug.LogWarning($"[VoxBoxAnimalSchoolSpawner] Failed to spawn animal index {i}.", this);
        }

        if (logSpawnInfo)
            Debug.Log($"[VoxBoxAnimalSchoolSpawner] Spawned {spawnedCount}/{totalTarget} animals (wolves={hostileWolves}).", this);
    }

    GameObject SpawnAnimal(GameObject prefab, Vector3 position, Quaternion rotation, int index, bool spawnWolf)
    {
        GameObject go = null;
        if (prefab != null)
            go = Instantiate(prefab, position, rotation, transform);
        else
            go = CreateFallbackAnimal(position, rotation);

        if (go == null) return null;

        go.name = $"{(spawnWolf ? "Wolf" : "RoamingAnimal")}_{index:00}_{go.name}";

        float s = Random.Range(randomScaleRange.x, randomScaleRange.y);
        go.transform.localScale *= s;

        if (tagAsHarvestable)
            TrySetTag(go, harvestableTag);

        var boid = go.GetComponent<FPSBoidAgent>();
        if (boid == null)
            boid = go.AddComponent<FPSBoidAgent>();
        boid.role = spawnWolf ? FPSBoidAgent.BoidRole.Predator : FPSBoidAgent.BoidRole.Prey;
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
        bool isDeer = false;
        if (prefab != null)
            isDeer = prefab.name.IndexOf("deer", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (!isDeer)
            isDeer = go.name.IndexOf("deer", System.StringComparison.OrdinalIgnoreCase) >= 0;
        if (isFox)
        {
            // Foxes get stricter shoreline behavior: avoid entering water and turn around at edges.
            boid.waterAvoidWeight = 7.5f;
            boid.waterSearchRadius = 10;
            boid.waterEdgeLookAheadDistance = 1.35f;
            boid.waterEdgeTurnSpeedMultiplier = 1.45f;
            boid.waterEdgeExtraAvoidWeight = 4.5f;

            var visualSwap = go.GetComponent<AnimatedAnimalVisualSwap>();
            if (visualSwap == null)
                visualSwap = go.AddComponent<AnimatedAnimalVisualSwap>();
            visualSwap.resourceModelPath = "Animals/FoxAnimated/Fox";
            visualSwap.desiredLocalHeight = 1.9f;
            visualSwap.yawOffsetDegrees = 0f;
            visualSwap.destroyExistingVisualChildren = true;
            visualSwap.logWarnings = logSpawnInfo;
            visualSwap.ApplyNow();
        }
        else if (isDeer)
        {
            var visualSwap = go.GetComponent<AnimatedAnimalVisualSwap>();
            if (visualSwap == null)
                visualSwap = go.AddComponent<AnimatedAnimalVisualSwap>();
            visualSwap.resourceModelPath = "Animals/DeerAnimated/Deer";
            visualSwap.desiredLocalHeight = 2.25f;
            visualSwap.yawOffsetDegrees = 0f;
            visualSwap.destroyExistingVisualChildren = true;
            visualSwap.logWarnings = logSpawnInfo;
            visualSwap.ApplyNow();
        }

        boid.canEatMaturePlants = animalsEatMaturePlants;
        boid.eatPlantRange = eatPlantRange;
        boid.eatCheckIntervalSeconds = eatCheckIntervalSeconds;
        boid.eatCooldownSeconds = eatCooldownSeconds;
        boid.eatHeadTouchDistance = eatHeadTouchDistance;
        boid.eatHoldSeconds = eatHoldSeconds;
        boid.eatAction = eatAction;
        boid.canAttackOtherAnimals = spawnWolf;
        boid.canAttackPlayer = spawnWolf;
        boid.attackDamage = 10f;
        boid.attackCooldownSeconds = 1.1f;
        boid.attackRange = spawnWolf ? 1.45f : 1.1f;
        boid.attackApproachWeight = spawnWolf ? 5.2f : 0f;

        if (configureBoidBoundsFromWorld)
            ApplyWorldBounds(boid);

        EnsureAnyCollider(go);
        var health = go.GetComponent<SCoLCombatHealth>();
        if (health == null)
            health = go.AddComponent<SCoLCombatHealth>();
        health.Configure(
            spawnWolf ? SCoLCombatFaction.Wolf : SCoLCombatFaction.Animal,
            50f,
            fillToMax: true,
            showBar: true,
            destroyWhenDead: true);

        if (spawnWolf)
        {
            var wolfSwap = go.GetComponent<AnimatedAnimalVisualSwap>();
            if (wolfSwap == null)
                wolfSwap = go.AddComponent<AnimatedAnimalVisualSwap>();
            wolfSwap.resourceModelPath = "Animals/WolfAnimated/Wolf";
            wolfSwap.desiredLocalHeight = 1.95f;
            wolfSwap.yawOffsetDegrees = 0f;
            wolfSwap.destroyExistingVisualChildren = true;
            wolfSwap.logWarnings = logSpawnInfo;
            wolfSwap.ApplyNow();
        }

        var respawnRelay = go.GetComponent<SCoLAnimalRespawnRelay>();
        if (respawnRelay == null)
            respawnRelay = go.AddComponent<SCoLAnimalRespawnRelay>();
        respawnRelay.Initialize(this, spawnWolf);

        _spawned.Add(go);
        return go;
    }

    public void NotifyAnimalDeath(GameObject animalRoot, bool spawnWolf)
    {
        if (animalRoot != null)
        {
            if (enableDeathFeedback)
                PlayDeathFeedback(animalRoot.transform.position, spawnWolf);
            if (enableDeathDrops)
                SpawnDeathDrops(animalRoot.transform.position, spawnWolf);
        }

        if (animalRoot != null)
            _spawned.Remove(animalRoot);

        if (!maintainPopulationByRespawning || !isActiveAndEnabled || !Application.isPlaying)
            return;

        StartCoroutine(RespawnAnimalAfterDelay(spawnWolf));
    }

    IEnumerator RespawnAnimalAfterDelay(bool spawnWolf)
    {
        if (respawnDelaySeconds > 0f)
            yield return new WaitForSeconds(respawnDelaySeconds);

        if (voxelWorld == null)
            voxelWorld = FindFirstObjectByType<VoxelWorld>();

        TryAutoAssignAnimalPrefabs();
        EnsureFoxAndDeerPrefabs();

        GameObject spawned = null;
        int tries = Mathf.Max(4, maxSpawnAttemptsPerAnimal * 2);
        int herbivoreTarget = GetHerbivoreTargetCount();
        for (int i = 0; i < tries; i++)
        {
            Vector3 pos;
            if (!TryPickSpawnPoint(Random.Range(0, Mathf.Max(1, herbivoreTarget + Mathf.Max(0, wolfCount))), Mathf.Max(1, herbivoreTarget + Mathf.Max(0, wolfCount)), out pos))
                continue;

            var prefab = PickRespawnPrefab(spawnWolf, herbivoreTarget);
            var rot = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            spawned = SpawnAnimal(prefab, pos, rot, _spawnSerial++, spawnWolf);
            if (spawned != null)
                yield break;
        }

        if (logSpawnInfo)
            Debug.LogWarning($"[VoxBoxAnimalSchoolSpawner] Failed to respawn {(spawnWolf ? "wolf" : "animal")} after death.", this);
    }

    int GetHerbivoreTargetCount()
    {
        int herbivoreTarget = Mathf.Max(1, animalCount);
        if (enforceMinimumAnimalCount)
            herbivoreTarget = Mathf.Max(herbivoreTarget, Mathf.Max(1, minimumAnimalCount));
        return herbivoreTarget;
    }

    GameObject PickRespawnPrefab(bool spawnWolf, int herbivoreTarget)
    {
        int hostileWolves = Mathf.Max(0, wolfCount);
        if (spawnWolf)
            return PickPrefab(0, herbivoreTarget, hostileWolves, true);

        int herbivoreIndex = Random.Range(0, Mathf.Max(1, herbivoreTarget));
        return PickPrefab(herbivoreIndex, herbivoreTarget, 0, false);
    }

    void PlayDeathFeedback(Vector3 worldPos, bool spawnWolf)
    {
        Vector3 burstPos = worldPos + Vector3.up * (spawnWolf ? 0.45f : 0.3f);
        FPSGameFeel.VoxelBurst(
            burstPos,
            count: spawnWolf ? 18 : 12,
            spread: spawnWolf ? 1.15f : 0.85f,
            life: 0.65f,
            cubeSize: spawnWolf ? 0.06f : 0.05f);

        if (Camera.main != null)
        {
            Vector3 d = Camera.main.transform.position - worldPos;
            d.y = 0f;
            if (d.sqrMagnitude <= 18f * 18f)
                FPSGameFeel.Shake(spawnWolf ? 0.06f : 0.04f, spawnWolf ? 0.12f : 0.08f);
        }
    }

    void SpawnDeathDrops(Vector3 worldPos, bool spawnWolf)
    {
        if (spawnWolf)
        {
            SpawnPickupDrop(SCoL.Inventory.SCoLItemType.Stone, Mathf.Max(1, wolfStoneDropAmount), worldPos, -1, PickStoneDropPrefab());
            return;
        }

        SpawnPickupDrop(SCoL.Inventory.SCoLItemType.Plant, Mathf.Max(1, herbivorePlantDropAmount), worldPos);
        if (Random.value <= herbivoreSeedDropChance)
            SpawnPickupDrop(SCoL.Inventory.SCoLItemType.Seed, 1, worldPos + new Vector3(0.35f, 0f, -0.18f));
    }

    void SpawnPickupDrop(SCoL.Inventory.SCoLItemType type, int amount, Vector3 worldPos, int seedVariantIndex = -1, GameObject prefab = null)
    {
        GameObject go = prefab != null
            ? Instantiate(prefab, worldPos + Vector3.up * 0.16f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f))
            : GameObject.CreatePrimitive(type == SCoL.Inventory.SCoLItemType.Seed ? PrimitiveType.Sphere : PrimitiveType.Capsule);

        go.name = $"{type}_DeathDrop";
        if (prefab == null)
        {
            go.transform.position = worldPos + Vector3.up * 0.16f;
            go.transform.localScale = type == SCoL.Inventory.SCoLItemType.Seed
                ? new Vector3(0.22f, 0.22f, 0.22f)
                : new Vector3(0.24f, 0.18f, 0.24f);
        }

        var rb = go.GetComponent<Rigidbody>();
        if (rb == null)
            rb = go.AddComponent<Rigidbody>();
        rb.useGravity = false;
        rb.isKinematic = true;

        EnsureAnyCollider(go);
        var pickup = go.GetComponent<SCoL.Inventory.SCoLPickup>();
        if (pickup == null)
            pickup = go.AddComponent<SCoL.Inventory.SCoLPickup>();
        pickup.type = type;
        pickup.amount = Mathf.Max(1, amount);
        pickup.seedVariantIndex = seedVariantIndex;
        pickup.preserveExistingMaterials = prefab != null;
        pickup.ApplyVisual();
    }

    GameObject PickStoneDropPrefab()
    {
        if (_stoneDropPrefabs == null || _stoneDropPrefabs.Length == 0)
        {
            _stoneDropPrefabs = new[]
            {
                Resources.Load<GameObject>("StylizedNature/FBX/Pebble_Round_1"),
                Resources.Load<GameObject>("StylizedNature/FBX/Pebble_Round_2"),
                Resources.Load<GameObject>("StylizedNature/FBX/Pebble_Round_3")
            };
        }

        if (_stoneDropPrefabs == null || _stoneDropPrefabs.Length == 0)
            return null;

        for (int i = 0; i < 6; i++)
        {
            var pick = _stoneDropPrefabs[Random.Range(0, _stoneDropPrefabs.Length)];
            if (pick != null)
                return pick;
        }

        return null;
    }

    bool TryPickSpawnPoint(int spawnIndex, int targetCount, out Vector3 pos)
    {
        pos = transform.position;

        if (voxelWorld != null && voxelWorld.Config != null)
        {
            if (TryPickDistributedWorldColumn(spawnIndex, targetCount, out int x, out int z))
            {
                Vector3 top = voxelWorld.ColumnTopWorld(x, z);
                pos = new Vector3(top.x, top.y + groundOffset, top.z);
                return true;
            }

            for (int tries = 0; tries < 12; tries++)
            {
                x = Random.Range(0, voxelWorld.Config.worldWidth);
                z = Random.Range(0, voxelWorld.Config.worldDepth);
                if (!IsDryLandColumn(x, z))
                    continue;

                Vector3 top = voxelWorld.ColumnTopWorld(x, z);
                pos = new Vector3(top.x, top.y + groundOffset, top.z);
                return true;
            }

            return false;
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

    bool TryPickDistributedWorldColumn(int spawnIndex, int targetCount, out int x, out int z)
    {
        x = 0;
        z = 0;

        if (voxelWorld == null || voxelWorld.Config == null)
            return false;

        int worldWidth = Mathf.Max(1, voxelWorld.Config.worldWidth);
        int worldDepth = Mathf.Max(1, voxelWorld.Config.worldDepth);
        int target = Mathf.Max(1, targetCount);

        float aspect = worldWidth / (float)Mathf.Max(1, worldDepth);
        int gridX = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(target * Mathf.Max(0.25f, aspect))));
        int gridZ = Mathf.Max(1, Mathf.CeilToInt(target / (float)gridX));

        int cellIndex = Mathf.Clamp(spawnIndex, 0, target - 1);
        int cellX = cellIndex % gridX;
        int cellZ = Mathf.Min(gridZ - 1, cellIndex / gridX);

        int minX = Mathf.FloorToInt(cellX * worldWidth / (float)gridX);
        int maxX = Mathf.Max(minX, Mathf.CeilToInt((cellX + 1) * worldWidth / (float)gridX) - 1);
        int minZ = Mathf.FloorToInt(cellZ * worldDepth / (float)gridZ);
        int maxZ = Mathf.Max(minZ, Mathf.CeilToInt((cellZ + 1) * worldDepth / (float)gridZ) - 1);

        for (int tries = 0; tries < 10; tries++)
        {
            int px = Random.Range(minX, maxX + 1);
            int pz = Random.Range(minZ, maxZ + 1);
            if (!IsDryLandColumn(px, pz))
                continue;

            x = px;
            z = pz;
            return true;
        }

        int centerX = Mathf.Clamp((minX + maxX) / 2, 0, worldWidth - 1);
        int centerZ = Mathf.Clamp((minZ + maxZ) / 2, 0, worldDepth - 1);
        int maxRadius = Mathf.Max(maxX - minX, maxZ - minZ) + 6;
        for (int radius = 0; radius <= maxRadius; radius++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != radius)
                        continue;

                    int px = centerX + dx;
                    int pz = centerZ + dz;
                    if (!IsDryLandColumn(px, pz))
                        continue;

                    x = px;
                    z = pz;
                    return true;
                }
            }
        }

        return false;
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

    GameObject PickPrefab(int spawnIndex, int herbivoreTarget, int hostileWolves, bool spawnWolf)
    {
        if (animalPrefabs == null || animalPrefabs.Length == 0)
            return null;

        if (spawnWolf && TryFindNamedPrefab("wolf", out var wolfPrefab))
            return wolfPrefab;

        int herbivoreIndex = Mathf.Max(0, spawnIndex - hostileWolves);
        if (TryPickBalancedFoxDeerPrefab(herbivoreIndex, herbivoreTarget, out var balanced))
            return balanced;

        for (int i = 0; i < 8; i++)
        {
            var p = animalPrefabs[Random.Range(0, animalPrefabs.Length)];
            if (p != null) return p;
        }
        return null;
    }

    bool TryPickBalancedFoxDeerPrefab(int spawnIndex, int targetCount, out GameObject prefab)
    {
        prefab = null;
        if (animalPrefabs == null || animalPrefabs.Length < 2 || targetCount <= 1)
            return false;

        GameObject fox = null;
        GameObject deer = null;
        for (int i = 0; i < animalPrefabs.Length; i++)
        {
            var candidate = animalPrefabs[i];
            if (candidate == null)
                continue;

            string name = candidate.name ?? string.Empty;
            if (fox == null && name.IndexOf("fox", System.StringComparison.OrdinalIgnoreCase) >= 0)
                fox = candidate;
            else if (deer == null && name.IndexOf("deer", System.StringComparison.OrdinalIgnoreCase) >= 0)
                deer = candidate;
        }

        if (fox == null || deer == null)
            return false;

        int foxCount = Mathf.CeilToInt(targetCount * 0.5f);
        prefab = spawnIndex < foxCount ? fox : deer;
        return true;
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

        var existingRootCollider = root.GetComponent<Collider>();
        if (existingRootCollider != null)
            return;

        if (!TryGetRenderableBounds(root, out Bounds bounds))
        {
            root.AddComponent<CapsuleCollider>();
            return;
        }

        var capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1;

        Vector3 localCenter = root.transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.48f, bounds.center.z));
        capsule.center = localCenter;
        capsule.height = Mathf.Max(0.9f, bounds.size.y * 0.92f);
        capsule.radius = Mathf.Clamp(Mathf.Max(bounds.size.x, bounds.size.z) * 0.28f, 0.16f, capsule.height * 0.46f);
    }

    static bool TryGetRenderableBounds(GameObject root, out Bounds bounds)
    {
        var renderers = root.GetComponentsInChildren<Renderer>(true);
        if (renderers == null || renderers.Length == 0)
        {
            bounds = default;
            return false;
        }

        bool found = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null)
                continue;

            if (!found)
            {
                bounds = r.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(r.bounds);
            }
        }

        return found;
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

    void EnsureFoxAndDeerPrefabs()
    {
        var fox = FindAnimalPrefabByName("fox");
        var deer = FindAnimalPrefabByName("deer");
        var wolf = FindAnimalPrefabByName("wolf");

#if UNITY_EDITOR
        if (fox == null)
            fox = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VoxBox/Prefabs/Animals/Fox.prefab");
        if (deer == null)
            deer = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/VoxBox/Prefabs/Animals/Deer.prefab");
#endif

        if (fox == null)
            fox = Resources.Load<GameObject>("Animals/FoxAnimated/Fox");
        if (deer == null)
            deer = Resources.Load<GameObject>("Animals/DeerAnimated/Deer");
        if (wolf == null)
            wolf = Resources.Load<GameObject>("Animals/WolfAnimated/Wolf");

        var list = new System.Collections.Generic.List<GameObject>(3);
        if (fox != null) list.Add(fox);
        if (deer != null) list.Add(deer);
        if (wolf != null) list.Add(wolf);
        if (list.Count > 0)
            animalPrefabs = list.ToArray();
    }

    bool TryFindNamedPrefab(string contains, out GameObject prefab)
    {
        prefab = FindAnimalPrefabByName(contains);
        return prefab != null;
    }

    GameObject FindAnimalPrefabByName(string contains)
    {
        if (animalPrefabs == null || animalPrefabs.Length == 0 || string.IsNullOrEmpty(contains))
            return null;

        for (int i = 0; i < animalPrefabs.Length; i++)
        {
            var prefab = animalPrefabs[i];
            if (prefab == null)
                continue;

            if (prefab.name.IndexOf(contains, System.StringComparison.OrdinalIgnoreCase) >= 0)
                return prefab;
        }

        return null;
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
