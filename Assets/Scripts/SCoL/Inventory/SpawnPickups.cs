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
        public int seedCount = 12;
        public int fireCount = 8;

        [Header("Prefabs (optional)")]
        [Tooltip("If assigned, used for seed pickups.")]
        public GameObject seedPickupPrefab;
        [Tooltip("If assigned, used for torch/fire pickups.")]
        public GameObject firePickupPrefab;
        [Header("Seed Variants")]
        [Tooltip("Optional seed textures. If empty, tries auto-load: seed1..seed4 from Squash seed folder.")]
        public Texture2D[] seedVariantTextures;
        public bool useSeedShapeVariants = true;

        [Header("Placement")]
        public bool scatterAcrossGrid = true;

        [Tooltip("Used when scatterAcrossGrid=false.")]
        public Vector3 center = new Vector3(0f, 1.1f, 1.2f);

        [Tooltip("Used when scatterAcrossGrid=false.")]
        public float radius = 0.8f;

        [Tooltip("Vertical offset above ground/tile for spawned pickups.")]
        public float yOffset = 0.25f;
        [Min(0.5f)] public float pickupGlobalScaleMultiplier = 1.35f;
        public Vector2 randomScaleRange = new Vector2(0.75f, 1.25f);
        [Min(1)] public int maxSpawnAttemptsPerItem = 18;

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
            EnsureSeedVariantTexturesLoaded();

            int spawnedSeeds = Spawn(SCoLItemType.Seed, seedCount, 0f);
            int spawnedFire = Spawn(SCoLItemType.Fire, fireCount, 3.0f);
            if (spawnedSeeds + spawnedFire == 0)
            {
                // Hard fallback for debugging/first-use: always spawn a visible cluster near player.
                SpawnFallbackClusterNearPlayer();
                spawnedSeeds = Mathf.Max(spawnedSeeds, 3);
                spawnedFire = Mathf.Max(spawnedFire, 3);
            }
            Debug.Log($"[SpawnPickups] Spawned Seed={spawnedSeeds}/{seedCount}, Fire={spawnedFire}/{fireCount}");
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

                var prefab = GetPickupPrefab(type);
                PrimitiveType? seedShape = null;
                if (prefab == null && type == SCoLItemType.Seed && useSeedShapeVariants)
                    seedShape = SeedShapeVariants[Mathf.Abs(i) % SeedShapeVariants.Length];

                GameObject go = prefab != null
                    ? Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f))
                    : GameObject.CreatePrimitive(seedShape ?? PrimitiveType.Capsule);
                go.name = $"Pickup_{type}_{i}";
                if (prefab == null)
                {
                    go.transform.position = pos;
                    go.transform.localScale = DefaultShapeScale(seedShape ?? PrimitiveType.Capsule);
                }
                float s = Random.Range(Mathf.Min(randomScaleRange.x, randomScaleRange.y), Mathf.Max(randomScaleRange.x, randomScaleRange.y));
                go.transform.localScale = go.transform.localScale * s * Mathf.Max(0.5f, pickupGlobalScaleMultiplier);

                var rb = go.GetComponent<Rigidbody>();
                if (rb == null) rb = go.AddComponent<Rigidbody>();
                rb.useGravity = false;
                rb.mass = 0.15f;
                rb.isKinematic = true;

                var p = go.GetComponent<SCoLPickup>();
                if (p == null) p = go.AddComponent<SCoLPickup>();
                p.type = type;
                p.amount = 1;
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

        private GameObject GetPickupPrefab(SCoLItemType type)
        {
            return type switch
            {
                SCoLItemType.Seed => seedPickupPrefab,
                SCoLItemType.Fire => firePickupPrefab,
                _ => null
            };
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
            var c = go.GetComponentInChildren<Collider>();
            if (c != null) return;
            go.AddComponent<BoxCollider>();
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
                SpawnOneAt(SCoLItemType.Fire, basePos + new Vector3(i * 0.35f, 0f, 0.45f), $"Pickup_Fire_Fallback_{i}");
            }
        }

        private void SpawnOneAt(SCoLItemType type, Vector3 pos, string name)
        {
            var prefab = GetPickupPrefab(type);
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
            go.transform.localScale = go.transform.localScale * Mathf.Max(0.5f, pickupGlobalScaleMultiplier);

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            var p = go.GetComponent<SCoLPickup>();
            if (p == null) p = go.AddComponent<SCoLPickup>();
            p.type = type;
            p.amount = 1;
            if (type == SCoLItemType.Seed)
            {
                var seedTex = PickSeedVariantTexture(Mathf.Abs(name.GetHashCode()));
                if (seedTex != null) p.seedTexture = seedTex;
            }
            p.ApplyVisual();
            EnsureCollider(go);
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
