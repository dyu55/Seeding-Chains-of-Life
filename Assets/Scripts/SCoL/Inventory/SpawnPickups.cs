using UnityEngine;
using System.Collections;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.Inventory
{
    /// <summary>
    /// Spawns pickup objects across dry land.
    /// Supports custom imported prefabs (seed + torch/fire).
    /// </summary>
    public class SpawnPickups : MonoBehaviour
    {
        public int seedCount = 20;
        public int waterCount = 50;
        public int fireCount = 40;
        public int stoneCount = 24;

        [Header("Prefabs (optional)")]
        [Tooltip("Legacy single seed pickup prefab.")]
        public GameObject seedPickupPrefab;
        [Tooltip("Legacy single torch/branch pickup prefab.")]
        public GameObject firePickupPrefab;
        [Tooltip("Legacy single water pickup prefab.")]
        public GameObject waterPickupPrefab;
        [Tooltip("Seed pickup prefab variants (preferred). Drag seed model prefabs here.")]
        public GameObject[] seedPickupPrefabs;
        [Tooltip("Water pickup prefab variants (preferred). Drag watercan model prefabs here.")]
        public GameObject[] waterPickupPrefabs;
        [Tooltip("Fire pickup prefab variants (preferred). Drag branch/torch model prefabs here.")]
        public GameObject[] firePickupPrefabs;
        [Tooltip("Stone pickup prefab variants (preferred). Drag pebble/rock model prefabs here.")]
        public GameObject[] stonePickupPrefabs;
        [Header("Seed Variants")]
        [Tooltip("Optional seed textures for primitive fallback only.")]
        public Texture2D[] seedVariantTextures;
        public bool useSeedShapeVariants = false;

        [Header("Placement")]
        public bool scatterAcrossGrid = true;

        [Tooltip("Used when scatterAcrossGrid=false.")]
        public Vector3 center = new Vector3(0f, 1.1f, 1.2f);

        [Tooltip("Used when scatterAcrossGrid=false.")]
        public float radius = 0.8f;

        [Tooltip("Vertical offset above ground/tile for spawned pickups.")]
        public float yOffset = 0.25f;
        [Min(0.5f)] public float pickupGlobalScaleMultiplier = 1.35f;
        [Header("Ground Snap")]
        [Min(0.1f)] public float groundSnapProbeHeight = 20f;
        [Min(0.5f)] public float groundSnapProbeDistance = 80f;
        [Min(0f)] public float groundClearance = 0.01f;
        [Range(0.1f, 2f)] public float seedPickupScaleMultiplier = 0.68f;
        public Vector2 randomScaleRange = new Vector2(0.75f, 1.25f);
        [Min(1)] public int maxSpawnAttemptsPerItem = 18;
        [Tooltip("If true, never spawn primitive placeholder objects. Only assigned/imported model prefabs are allowed.")]
        public bool modelsOnly = true;

        private SCoL.SCoLRuntime _runtime;
        private SCoL.Voxels.VoxelWorld _voxelWorld;
        private static readonly PrimitiveType[] SeedShapeVariants =
        {
            PrimitiveType.Sphere,
            PrimitiveType.Capsule,
            PrimitiveType.Cube,
            PrimitiveType.Cylinder
        };

        private bool _spawned;

        private IEnumerator Start()
        {
            if (_spawned) yield break;
            _spawned = true;

            // Runtime/grid may be created after scene start; wait briefly.
            float deadline = Time.realtimeSinceStartup + 4f;
            while (Time.realtimeSinceStartup < deadline)
            {
                _runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                _voxelWorld = FindFirstObjectByType<SCoL.Voxels.VoxelWorld>();
                if (_runtime != null && _runtime.Grid != null && _voxelWorld != null && _voxelWorld.Config != null)
                    break;
                yield return null;
            }
            EnsureAutoAssignPickupPrefabs();
            EnsureSeedVariantTexturesLoaded();

            int spawnedSeeds = Spawn(SCoLItemType.Seed, seedCount, 0f);
            int spawnedWater = Spawn(SCoLItemType.Water, waterCount, 1.5f);
            int spawnedFire = Spawn(SCoLItemType.Fire, fireCount, 3.0f);
            int spawnedStones = Spawn(SCoLItemType.Stone, stoneCount, 5.5f);
            if (!modelsOnly && spawnedSeeds + spawnedWater + spawnedFire + spawnedStones == 0)
            {
                // Hard fallback for debugging/first-use: always spawn a visible cluster near player.
                SpawnFallbackClusterNearPlayer();
                spawnedSeeds = Mathf.Max(spawnedSeeds, 3);
                spawnedWater = Mathf.Max(spawnedWater, 3);
                spawnedFire = Mathf.Max(spawnedFire, 3);
                spawnedStones = Mathf.Max(spawnedStones, 3);
            }
            Debug.Log($"[SpawnPickups] Spawned Seed={spawnedSeeds}/{seedCount}, Water={spawnedWater}/{waterCount}, Fire={spawnedFire}/{fireCount}, Stone={spawnedStones}/{stoneCount}");
        }

        private int Spawn(SCoLItemType type, int count, float angleOffset)
        {
            if (count <= 0) return 0;
            int spawned = 0;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos;

                if (scatterAcrossGrid && _runtime != null && _runtime.Grid != null)
                {
                    if (!TryFindSpawnPointOnLand(out pos))
                    {
                        if (!TryFallbackSpawnNearPlayer(out pos, i, count, angleOffset))
                            continue;
                    }
                }
                else
                {
                    if (!TryFallbackSpawnNearPlayer(out pos, i, count, angleOffset))
                    {
                        float a = angleOffset + (i / (float)count) * Mathf.PI * 2f;
                        pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    }
                }

                var prefab = GetPickupPrefab(type, i);
                PrimitiveType? seedShape = null;
                if (prefab == null)
                {
                    if (modelsOnly)
                        continue;
                    if (type == SCoLItemType.Seed && useSeedShapeVariants)
                        seedShape = SeedShapeVariants[Mathf.Abs(i) % SeedShapeVariants.Length];
                }

                GameObject go = prefab != null
                    ? Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f))
                    : GameObject.CreatePrimitive(seedShape ?? PrimitiveType.Capsule);
                go.name = $"Pickup_{type}_{i}";
                if (prefab == null)
                {
                    go.transform.position = pos;
                    go.transform.localScale = DefaultShapeScale(seedShape ?? PrimitiveType.Capsule);
                }
                SetLayerRecursive(go, 0); // Default layer for raycast pickup parity.
                float s = Random.Range(Mathf.Min(randomScaleRange.x, randomScaleRange.y), Mathf.Max(randomScaleRange.x, randomScaleRange.y));
                go.transform.localScale = go.transform.localScale * s * Mathf.Max(0.5f, pickupGlobalScaleMultiplier);
                if (type == SCoLItemType.Seed)
                    go.transform.localScale *= Mathf.Max(0.1f, seedPickupScaleMultiplier);
                SnapBottomToGround(go, pos);

                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.mass = 0.15f;
                rb.isKinematic = true;

                var p = go.GetComponent<SCoLPickup>();
                if (p == null) p = go.AddComponent<SCoLPickup>();
                p.type = type;
                p.amount = 1;
                p.preserveExistingMaterials = prefab != null;
                if (type == SCoLItemType.Seed)
                    p.seedVariantIndex = ResolveSeedVariantIndexForPrefab(prefab, i);
                if (type == SCoLItemType.Seed)
                {
                    var seedTex = PickSeedVariantTexture(i);
                    if (seedTex != null) p.seedTexture = seedTex;
                }
                p.ApplyVisual();

                EnsureCollider(go);
                spawned++;
            }

            return spawned;
        }

        private GameObject GetPickupPrefab(SCoLItemType type, int variantIndex)
        {
            if (type == SCoLItemType.Seed)
            {
                var v = PickVariantPrefab(seedPickupPrefabs, variantIndex);
                if (v != null) return v;
                return seedPickupPrefab;
            }

            if (type == SCoLItemType.Fire)
            {
                var v = PickVariantPrefab(firePickupPrefabs, variantIndex);
                if (v != null) return v;
                return firePickupPrefab;
            }

            if (type == SCoLItemType.Water)
            {
                var v = PickVariantPrefab(waterPickupPrefabs, variantIndex);
                if (v != null) return v;
                return waterPickupPrefab;
            }

            if (type == SCoLItemType.Stone)
            {
                var v = PickVariantPrefab(stonePickupPrefabs, variantIndex);
                if (v != null) return v;
            }

            return null;
        }

        private bool TryFindSpawnPointOnLand(out Vector3 pos)
        {
            pos = center;
            if (_voxelWorld == null || _voxelWorld.Config == null || _runtime == null || _runtime.Grid == null)
                return false;

            int attempts = Mathf.Max(1, maxSpawnAttemptsPerItem);
            for (int i = 0; i < attempts; i++)
            {
                int x = Random.Range(0, _runtime.Grid.Width);
                int z = Random.Range(0, _runtime.Grid.Height);
                if (!_voxelWorld.IsGrassSurface(x, z))
                    continue;

                int y = _voxelWorld.GetSurfaceY(x, z);
                if (y < _voxelWorld.Config.seaLevel)
                    continue;

                pos = _voxelWorld.OriginWorld + new Vector3(x + 0.5f, y + 1f + yOffset, z + 0.5f);
                return true;
            }

            return false;
        }

        private static void EnsureCollider(GameObject go)
        {
            if (go == null) return;
            var cols = go.GetComponentsInChildren<Collider>(includeInactive: true);
            if (cols != null && cols.Length > 0)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i];
                    if (c == null) continue;
                    c.enabled = true;
                    c.isTrigger = false;
                }
                return;
            }

            var added = go.AddComponent<BoxCollider>();
            added.enabled = true;
            added.isTrigger = false;
        }

        private bool TryFallbackSpawnNearPlayer(out Vector3 pos, int i, int count, float angleOffset)
        {
            pos = center;
            var cam = Camera.main;
            Vector3 around = cam != null ? cam.transform.position : center;
            around.y = _voxelWorld != null ? _voxelWorld.OriginWorld.y + 3f : around.y;

            float a = angleOffset + (i / Mathf.Max(1f, count)) * Mathf.PI * 2f;
            Vector3 candidate = around + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Mathf.Max(2f, radius);

            if (_voxelWorld != null && _voxelWorld.Config != null &&
                _voxelWorld.TryWorldToColumn(candidate, out int x, out int z))
            {
                int y = _voxelWorld.GetSurfaceY(x, z);
                pos = _voxelWorld.OriginWorld + new Vector3(x + 0.5f, y + 1f + yOffset, z + 0.5f);
                return true;
            }

            pos = candidate + Vector3.up * Mathf.Max(0.2f, yOffset);
            return true;
        }

        private void SpawnFallbackClusterNearPlayer()
        {
            Vector3 basePos = center;
            var cam = Camera.main;
            if (cam != null)
                basePos = cam.transform.position + cam.transform.forward * 2.0f + Vector3.up * 0.3f;

            for (int i = 0; i < 3; i++)
            {
                SpawnOneAt(SCoLItemType.Seed, basePos + new Vector3(i * 0.35f, 0f, 0f), $"Pickup_Seed_Fallback_{i}");
                SpawnOneAt(SCoLItemType.Water, basePos + new Vector3(i * 0.35f, 0f, 0.225f), $"Pickup_Water_Fallback_{i}");
                SpawnOneAt(SCoLItemType.Fire, basePos + new Vector3(i * 0.35f, 0f, 0.45f), $"Pickup_Fire_Fallback_{i}");
                SpawnOneAt(SCoLItemType.Stone, basePos + new Vector3(i * 0.35f, 0f, 0.9f), $"Pickup_Stone_Fallback_{i}");
            }
        }

        private void SpawnOneAt(SCoLItemType type, Vector3 pos, string name)
        {
            var prefab = GetPickupPrefab(type, Mathf.Abs(name.GetHashCode()));
            if (prefab == null && modelsOnly)
                return;
            PrimitiveType? seedShape = null;
            if (prefab == null && type == SCoLItemType.Seed && useSeedShapeVariants)
                seedShape = SeedShapeVariants[Mathf.Abs(name.GetHashCode()) % SeedShapeVariants.Length];

            GameObject go = prefab != null
                ? Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f))
                : GameObject.CreatePrimitive(seedShape ?? PrimitiveType.Capsule);
            go.name = name;
            if (prefab == null)
            {
                go.transform.position = pos;
                go.transform.localScale = DefaultShapeScale(seedShape ?? PrimitiveType.Capsule) * 1.25f;
            }
            SetLayerRecursive(go, 0);
            go.transform.localScale = go.transform.localScale * Mathf.Max(0.5f, pickupGlobalScaleMultiplier);
            if (type == SCoLItemType.Seed)
                go.transform.localScale *= Mathf.Max(0.1f, seedPickupScaleMultiplier);
            SnapBottomToGround(go, pos);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            var p = go.GetComponent<SCoLPickup>();
            if (p == null) p = go.AddComponent<SCoLPickup>();
            p.type = type;
            p.amount = 1;
            p.preserveExistingMaterials = prefab != null;
            if (type == SCoLItemType.Seed)
                p.seedVariantIndex = ResolveSeedVariantIndexForPrefab(prefab, Mathf.Abs(name.GetHashCode()));
            if (type == SCoLItemType.Seed)
            {
                var seedTex = PickSeedVariantTexture(Mathf.Abs(name.GetHashCode()));
                if (seedTex != null) p.seedTexture = seedTex;
            }
            p.ApplyVisual();
            EnsureCollider(go);
        }

        private static GameObject PickVariantPrefab(GameObject[] variants, int variantIndex)
        {
            if (variants == null || variants.Length == 0)
                return null;

            int validCount = 0;
            for (int i = 0; i < variants.Length; i++)
                if (variants[i] != null) validCount++;
            if (validCount == 0)
                return null;

            int pick = Mathf.Abs(variantIndex) % validCount;
            int seen = 0;
            for (int i = 0; i < variants.Length; i++)
            {
                var v = variants[i];
                if (v == null) continue;
                if (seen == pick) return v;
                seen++;
            }
            return null;
        }

        private Texture2D PickSeedVariantTexture(int i)
        {
            if (seedVariantTextures == null || seedVariantTextures.Length == 0)
                return null;

            int validCount = 0;
            for (int k = 0; k < seedVariantTextures.Length; k++)
                if (seedVariantTextures[k] != null) validCount++;
            if (validCount == 0)
                return null;

            int pick = Mathf.Abs(i) % validCount;
            int seen = 0;
            for (int k = 0; k < seedVariantTextures.Length; k++)
            {
                var t = seedVariantTextures[k];
                if (t == null) continue;
                if (seen == pick) return t;
                seen++;
            }

            return null;
        }

        private void EnsureSeedVariantTexturesLoaded()
        {
            // If seed models are assigned, keep prefab materials and skip texture auto-load.
            if (seedPickupPrefabs != null)
            {
                for (int i = 0; i < seedPickupPrefabs.Length; i++)
                    if (seedPickupPrefabs[i] != null)
                        return;
            }
            if (seedPickupPrefab != null)
                return;

            bool hasAny = false;
            if (seedVariantTextures != null)
            {
                for (int i = 0; i < seedVariantTextures.Length; i++)
                {
                    if (seedVariantTextures[i] != null) { hasAny = true; break; }
                }
            }
            if (hasAny)
                return;

            var loaded = new System.Collections.Generic.List<Texture2D>(4);
#if UNITY_EDITOR
            string[] paths =
            {
                "Assets/Models/Modeling/_Incoming/seed1/material_BaseColor.jpg",
                "Assets/Models/Modeling/Squash seed/seed1.png",
                "Assets/Models/Modeling/Squash seed/seed2.png",
                "Assets/Models/Modeling/Squash seed/seed3.png",
                "Assets/Models/Modeling/Squash seed/seed4.png"
            };
            for (int i = 0; i < paths.Length; i++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(paths[i]);
                if (tex != null) loaded.Add(tex);
            }
#endif
            if (loaded.Count > 0)
                seedVariantTextures = loaded.ToArray();
        }

        private void EnsureAutoAssignPickupPrefabs()
        {
#if UNITY_EDITOR
            if ((seedPickupPrefabs == null || seedPickupPrefabs.Length == 0) && seedPickupPrefab == null)
            {
                seedPickupPrefabs = LoadPrefabArray(
                    "Assets/Models/Modeling/_Incoming/seed1/seed1.obj",
                    "Assets/Models/Modeling/_Incoming/3stageFlowers/Seed/SeedV1.obj",
                    "Assets/Models/Modeling/_Incoming/3stageFlowers/Seed/SeedV2.obj",
                    "Assets/Models/Modeling/_Incoming/3stageFlowers/Seed/SeedV3.obj",
                    "Assets/Models/Modeling/_Incoming/Seeds/lightBrownSeed.fbx",
                    "Assets/Models/Modeling/_Incoming/Seeds/brownSeed.fbx",
                    "Assets/Models/Modeling/_Incoming/Seeds/bean.fbx",
                    "Assets/Models/Modeling/_Incoming/Seeds/longSeed.fbx"
                );
            }

            if (firePickupPrefabs == null || firePickupPrefabs.Length == 0)
            {
                firePickupPrefabs = LoadPrefabArray(
                    "Assets/Models/Modeling/_Incoming/stick1/stick1.obj",
                    "Assets/Models/Modeling/_Incoming/stick2/stick2.obj",
                    "Assets/Models/Modeling/_Incoming/Branches/branch1.fbx",
                    "Assets/Models/Modeling/_Incoming/Branches/branch2.fbx",
                    "Assets/Models/Modeling/_Incoming/Props/Squash seed/Torch.glb"
                );
            }
            if (waterPickupPrefabs == null || waterPickupPrefabs.Length == 0)
            {
                waterPickupPrefabs = LoadPrefabArray(
                    "Assets/Models/Modeling/_Incoming/watercan/watercan.obj"
                );
            }
            firePickupPrefab = null;
#endif

            if (stonePickupPrefabs == null || stonePickupPrefabs.Length == 0)
            {
                stonePickupPrefabs = LoadRuntimePrefabArray(
                    "StylizedNature/FBX/Pebble_Round_1",
                    "StylizedNature/FBX/Pebble_Round_2",
                    "StylizedNature/FBX/Pebble_Round_3");
            }
        }

        private static GameObject[] LoadRuntimePrefabArray(params string[] resourcePaths)
        {
            if (resourcePaths == null || resourcePaths.Length == 0)
                return null;

            var list = new System.Collections.Generic.List<GameObject>(resourcePaths.Length);
            for (int i = 0; i < resourcePaths.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(resourcePaths[i]))
                    continue;

                var prefab = Resources.Load<GameObject>(resourcePaths[i]);
                if (prefab != null)
                    list.Add(prefab);
            }

            return list.Count > 0 ? list.ToArray() : null;
        }

        private static int ResolveSeedVariantIndexForPrefab(GameObject prefab, int fallbackSeed)
        {
            string n = (prefab != null && !string.IsNullOrEmpty(prefab.name))
                ? prefab.name.ToLowerInvariant()
                : string.Empty;

            // Requested mappings:
            // bean -> 0, brownSeed -> 1, lightBrownSeed -> 2, longSeed -> 3.
            // Keep older SeedV* names mapped for compatibility.
            if (n.Contains("seedv1")) return 0;
            if (n.Contains("seedv2")) return 1;
            if (n.Contains("seedv3")) return 2;
            if (n.Contains("seed1")) return 3;

            // Explicit mappings for imported Seeds folder names.
            if (n.Contains("bean")) return 0;
            if (n.Contains("brownseed") && !n.Contains("lightbrownseed")) return 1;
            if (n.Contains("lightbrownseed") || n.Contains("light_brownseed") || n.Contains("lightbrown_seed")) return 2;
            if (n.Contains("longseed") || n.Contains("long_seed")) return 3;

            return Mathf.Abs(fallbackSeed) % 4;
        }

        private void SnapBottomToGround(GameObject go, Vector3 aroundPos)
        {
            if (go == null)
                return;

            if (!TryGetBottomY(go, out float bottomY))
                return;

            float targetGroundY = aroundPos.y;
            Vector3 probeOrigin = new Vector3(aroundPos.x, aroundPos.y + Mathf.Max(0.1f, groundSnapProbeHeight), aroundPos.z);
            float probeDist = Mathf.Max(0.5f, groundSnapProbeDistance);
            if (Physics.Raycast(probeOrigin, Vector3.down, out var hit, probeDist, ~0, QueryTriggerInteraction.Ignore))
                targetGroundY = hit.point.y;

            float targetBottom = targetGroundY + Mathf.Max(0f, groundClearance);
            float dy = targetBottom - bottomY;
            if (!Mathf.Approximately(dy, 0f))
                go.transform.position += Vector3.up * dy;
        }

        private static bool TryGetBottomY(GameObject go, out float bottomY)
        {
            bottomY = 0f;
            if (go == null)
                return false;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool has = false;
            Bounds b = new Bounds();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (!has)
                {
                    b = r.bounds;
                    has = true;
                }
                else
                {
                    b.Encapsulate(r.bounds);
                }
            }

            if (!has)
            {
                var cols = go.GetComponentsInChildren<Collider>(includeInactive: true);
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i];
                    if (c == null) continue;
                    if (!has)
                    {
                        b = c.bounds;
                        has = true;
                    }
                    else
                    {
                        b.Encapsulate(c.bounds);
                    }
                }
            }

            if (!has)
                return false;

            bottomY = b.min.y;
            return true;
        }

        private static GameObject[] LoadPrefabArray(params string[] paths)
        {
#if UNITY_EDITOR
            var list = new System.Collections.Generic.List<GameObject>(paths != null ? paths.Length : 0);
            if (paths != null)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    var p = paths[i];
                    if (string.IsNullOrEmpty(p)) continue;
                    var g = AssetDatabase.LoadAssetAtPath<GameObject>(p);
                    if (g != null) list.Add(g);
                }
            }
            return list.ToArray();
#else
            return null;
#endif
        }

        private static Vector3 DefaultShapeScale(PrimitiveType type)
        {
            return type switch
            {
                PrimitiveType.Sphere => new Vector3(0.15f, 0.15f, 0.15f),
                PrimitiveType.Capsule => new Vector3(0.14f, 0.18f, 0.14f),
                PrimitiveType.Cube => new Vector3(0.13f, 0.13f, 0.13f),
                PrimitiveType.Cylinder => new Vector3(0.13f, 0.16f, 0.13f),
                _ => Vector3.one * 0.14f
            };
        }

        private static void SetLayerRecursive(GameObject go, int layer)
        {
            if (go == null) return;
            go.layer = layer;
            var ts = go.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < ts.Length; i++)
            {
                var t = ts[i];
                if (t != null)
                    t.gameObject.layer = layer;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureSpawnerExists()
        {
            if (FindFirstObjectByType<SpawnPickups>() != null)
                return;
            var go = new GameObject("SpawnPickups (Runtime)");
            go.AddComponent<SpawnPickups>();
        }
    }
}
