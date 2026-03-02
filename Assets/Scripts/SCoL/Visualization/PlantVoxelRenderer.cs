using System.Collections.Generic;
using UnityEngine;
using SCoL;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.Visualization
{
    /// <summary>
    /// Renders cellular-automata plant stages with VoxBox prefabs on top of voxel columns.
    /// Falls back to colored cubes if prefabs are not available.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlantVoxelRenderer : MonoBehaviour
    {
        public SCoLRuntime runtime;
        public SCoL.Voxels.VoxelWorld voxelWorld;

        [Header("VoxBox Prefabs (optional overrides)")]
        public GameObject[] smallPlantPrefabs;
        public GameObject[] smallTreePrefabs;
        public GameObject[] mediumTreePrefabs;
        public GameObject[] largeTreePrefabs;

        [Header("Scale")]
        public Vector2 smallPlantScaleRange = new Vector2(0.28f, 0.45f);
        public Vector2 smallTreeScaleRange = new Vector2(0.85f, 1.10f);
        public Vector2 mediumTreeScaleRange = new Vector2(1.10f, 1.35f);
        public Vector2 largeTreeScaleRange = new Vector2(1.35f, 1.70f);
        public Vector2 burntScaleRange = new Vector2(0.20f, 0.30f);
        [Tooltip("Global size multiplier for small flowers/plants. 0.5 = half size.")]
        [Range(0.1f, 2f)] public float smallPlantBaseScaleMultiplier = 0.5f;
        [Tooltip("Max additional flower scale from watering. 0.1 = +10%.")]
        [Range(0f, 1f)] public float wateredSmallPlantScaleBonus = 0.10f;

        [Header("Placement")]
        public float smallPlantYOffset = 0.00f;
        public float treeYOffset = 0.00f;

        [Header("Fallback Colors")]
        public Color fallbackSmallPlantColor = new Color(0.35f, 0.88f, 0.42f);
        public Color fallbackSmallTreeColor = new Color(0.44f, 0.74f, 0.36f);
        public Color fallbackMediumTreeColor = new Color(0.35f, 0.62f, 0.31f);
        public Color fallbackLargeTreeColor = new Color(0.26f, 0.48f, 0.26f);
        public Color fallbackBurntColor = new Color(0.08f, 0.08f, 0.08f, 1f);

        struct ActivePlant
        {
            public GameObject go;
            public PlantStage stage;
            public int variant;
        }

        private readonly Dictionary<int, ActivePlant> _active = new();
        private readonly Dictionary<string, Stack<GameObject>> _pool = new();
        private readonly Dictionary<PlantStage, Material> _fallbackMats = new();

        private void Awake()
        {
            if (runtime == null) runtime = FindFirstObjectByType<SCoLRuntime>();
            if (voxelWorld == null) voxelWorld = FindFirstObjectByType<SCoL.Voxels.VoxelWorld>();

            AutoAssignVoxBoxDefaults();
            EnsureFallbackMaterials();
        }

        private void AutoAssignVoxBoxDefaults()
        {
#if UNITY_EDITOR
            const string flowerV1FinalStagePath = "Assets/Models/Modeling/_Incoming/Flowers/FlowerV1/Flower0.obj";

            if (smallPlantPrefabs == null || smallPlantPrefabs.Length == 0)
            {
                var stage1 = LoadPrefabs("Assets/Models/Modeling/_Incoming/Flowers/FlowerV2/Flower_Stage1.obj");
                smallPlantPrefabs = (stage1 != null && stage1.Length > 0)
                    ? stage1
                    : LoadPrefabs(flowerV1FinalStagePath);
            }

            if (smallTreePrefabs == null || smallTreePrefabs.Length == 0)
            {
                var stage2 = LoadPrefabs("Assets/Models/Modeling/_Incoming/Flowers/FlowerV2/Flower_Stage2.obj");
                smallTreePrefabs = (stage2 != null && stage2.Length > 0)
                    ? stage2
                    : LoadPrefabs(
                        "Assets/VoxBox/Prefabs/Trees/Tree 1.prefab",
                        "Assets/VoxBox/Prefabs/Trees/Tree 2.prefab");
            }

            if (mediumTreePrefabs == null || mediumTreePrefabs.Length == 0)
            {
                // Final floral stage: prefer FlowerV1/Flower0 (team-approved final model).
                var stage3 = LoadPrefabs(flowerV1FinalStagePath);
                mediumTreePrefabs = (stage3 != null && stage3.Length > 0)
                    ? stage3
                    : LoadPrefabs("Assets/Models/Modeling/_Incoming/Flowers/FlowerV2/Flower_FinalStage.obj");
            }

            if (mediumTreePrefabs == null || mediumTreePrefabs.Length == 0)
            {
                mediumTreePrefabs = LoadPrefabs(
                        "Assets/VoxBox/Prefabs/Trees/Tree 3.prefab",
                        "Assets/VoxBox/Prefabs/Trees/Tree 4.prefab");
            }

            if (largeTreePrefabs == null || largeTreePrefabs.Length == 0)
            {
                largeTreePrefabs = LoadPrefabs(
                    "Assets/VoxBox/Prefabs/Trees/Tree 4.prefab",
                    "Assets/VoxBox/Prefabs/Trees/Tree 5.prefab");
            }
#endif
        }

        private static GameObject[] LoadPrefabs(params string[] assetPaths)
        {
            var result = new List<GameObject>(assetPaths.Length);
#if UNITY_EDITOR
            for (int i = 0; i < assetPaths.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPaths[i]);
                if (prefab != null) result.Add(prefab);
            }
#endif
            return result.ToArray();
        }

        private void EnsureFallbackMaterials()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            _fallbackMats[PlantStage.SmallPlant] = NewFallbackMat(shader, "PlantFallback_SmallPlant", fallbackSmallPlantColor);
            _fallbackMats[PlantStage.SmallTree] = NewFallbackMat(shader, "PlantFallback_SmallTree", fallbackSmallTreeColor);
            _fallbackMats[PlantStage.MediumTree] = NewFallbackMat(shader, "PlantFallback_MediumTree", fallbackMediumTreeColor);
            _fallbackMats[PlantStage.LargeTree] = NewFallbackMat(shader, "PlantFallback_LargeTree", fallbackLargeTreeColor);
            _fallbackMats[PlantStage.Burnt] = NewFallbackMat(shader, "PlantFallback_Burnt", fallbackBurntColor);
        }

        private static Material NewFallbackMat(Shader shader, string name, Color color)
        {
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            return mat;
        }

        private static bool IsRenderableStage(PlantStage stage)
        {
            return stage == PlantStage.SmallPlant
                || stage == PlantStage.SmallTree
                || stage == PlantStage.MediumTree
                || stage == PlantStage.LargeTree
                || stage == PlantStage.Burnt;
        }

        private GameObject[] PrefabsFor(PlantStage stage)
        {
            return stage switch
            {
                PlantStage.SmallPlant => smallPlantPrefabs,
                PlantStage.SmallTree => smallTreePrefabs,
                PlantStage.MediumTree => mediumTreePrefabs,
                PlantStage.LargeTree => largeTreePrefabs,
                PlantStage.Burnt => null,
                _ => null
            };
        }

        private int PickPrefabIndex(PlantStage stage, int cellIndex)
        {
            var prefabs = PrefabsFor(stage);
            if (prefabs == null || prefabs.Length == 0) return -1;

            int validCount = 0;
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] != null) validCount++;
            }
            if (validCount == 0) return -1;

            int pick = PositiveHash(cellIndex + (int)stage * 19349663) % validCount;
            int seen = 0;
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null) continue;
                if (seen == pick) return i;
                seen++;
            }
            return -1;
        }

        private static int PositiveHash(int value)
        {
            unchecked
            {
                int h = value;
                h ^= (h << 13);
                h ^= (h >> 17);
                h ^= (h << 5);
                return h & int.MaxValue;
            }
        }

        private static float Hash01(int value)
        {
            return PositiveHash(value) / (float)int.MaxValue;
        }

        private bool IsFlowerPrefabVariant(PlantStage stage, int variant)
        {
            var prefabs = PrefabsFor(stage);
            if (prefabs == null || variant < 0 || variant >= prefabs.Length || prefabs[variant] == null)
                return false;

            string n = prefabs[variant].name;
            if (string.IsNullOrEmpty(n))
                return false;
            n = n.ToLowerInvariant();
            return n.Contains("flower") || n.Contains("daisy") || n.Contains("rose") || n.Contains("tulip");
        }

        private float ScaleFor(PlantStage stage, int cellIndex, CellState cell, int variant)
        {
            Vector2 range = stage switch
            {
                PlantStage.SmallPlant => smallPlantScaleRange,
                PlantStage.SmallTree => smallTreeScaleRange,
                PlantStage.MediumTree => mediumTreeScaleRange,
                PlantStage.LargeTree => largeTreeScaleRange,
                PlantStage.Burnt => burntScaleRange,
                _ => Vector2.one
            };
            if (range.y < range.x) range = new Vector2(range.y, range.x);

            float t = Hash01(cellIndex * 92821 + (int)stage * 1511);
            float scale = Mathf.Lerp(range.x, range.y, t);
            bool flowerStageModel = IsFlowerPrefabVariant(stage, variant);
            if (stage == PlantStage.SmallPlant)
            {
                // Keep flowers smaller overall, then allow a bounded +10% bump when watered.
                scale *= Mathf.Max(0.01f, smallPlantBaseScaleMultiplier);
                float waterBoost = 1f + Mathf.Clamp01(cell.WaterVisual) * Mathf.Max(0f, wateredSmallPlantScaleBonus);
                scale *= waterBoost;
            }
            else if (flowerStageModel && stage == PlantStage.SmallTree)
            {
                // Stage2 flower should stay close to Stage1 size, just visibly larger.
                float baseSmall = Mathf.Lerp(smallPlantScaleRange.x, smallPlantScaleRange.y, t) * Mathf.Max(0.01f, smallPlantBaseScaleMultiplier);
                scale = baseSmall * 1.25f;
            }
            else if (flowerStageModel && stage == PlantStage.MediumTree)
            {
                // Stage3 flower remains floral, not tree-sized.
                float baseSmall = Mathf.Lerp(smallPlantScaleRange.x, smallPlantScaleRange.y, t) * Mathf.Max(0.01f, smallPlantBaseScaleMultiplier);
                scale = baseSmall * 1.45f;
            }
            return scale;
        }

        private float YOffsetFor(PlantStage stage)
        {
            return (stage == PlantStage.SmallPlant || stage == PlantStage.Burnt) ? smallPlantYOffset : treeYOffset;
        }

        private string PoolKey(PlantStage stage, int variant)
        {
            return ((int)stage).ToString() + ":" + variant.ToString();
        }

        private GameObject CreateFallback(PlantStage stage)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "PlantFallback";

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null && _fallbackMats.TryGetValue(stage, out var mat))
                renderer.sharedMaterial = mat;

            var collider = go.GetComponent<Collider>();
            if (collider != null) Destroy(collider);

            return go;
        }

        private static void DisableCollisions(GameObject go)
        {
            var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; i++)
            {
                if (colliders[i] != null)
                    colliders[i].enabled = false;
            }

            var rigidbodies = go.GetComponentsInChildren<Rigidbody>(includeInactive: true);
            for (int i = 0; i < rigidbodies.Length; i++)
            {
                if (rigidbodies[i] != null)
                    Destroy(rigidbodies[i]);
            }
        }

        private GameObject Rent(PlantStage stage, int variant)
        {
            string key = PoolKey(stage, variant);

            if (_pool.TryGetValue(key, out var stack) && stack.Count > 0)
            {
                var reused = stack.Pop();
                if (reused != null)
                {
                    reused.SetActive(true);
                    return reused;
                }
            }

            GameObject go = null;
            if (variant >= 0)
            {
                var prefabs = PrefabsFor(stage);
                if (prefabs != null && variant < prefabs.Length)
                {
                    var prefab = prefabs[variant];
                    if (prefab != null)
                        go = Instantiate(prefab, transform);
                }
            }

            if (go == null)
                go = CreateFallback(stage);

            go.transform.SetParent(transform, worldPositionStays: true);
            DisableCollisions(go);
            return go;
        }

        private void Return(ActivePlant entry)
        {
            if (entry.go == null) return;

            string key = PoolKey(entry.stage, entry.variant);
            if (!_pool.TryGetValue(key, out var stack))
            {
                stack = new Stack<GameObject>();
                _pool[key] = stack;
            }

            entry.go.SetActive(false);
            stack.Push(entry.go);
        }

        private bool IsDryPlantableColumn(int x, int z)
        {
            if (voxelWorld == null || voxelWorld.Config == null)
                return true;

            if (!voxelWorld.IsGrassSurface(x, z))
                return false;

            int surfaceY = voxelWorld.GetSurfaceY(x, z);
            if (surfaceY < voxelWorld.Config.seaLevel)
                return false;

            int aboveY = surfaceY + 1;
            if (aboveY < voxelWorld.Config.worldHeight &&
                voxelWorld.GetBlock(x, aboveY, z) == SCoL.Voxels.VoxelBlockType.Water)
                return false;

            return true;
        }

        private static bool TryGetRenderBoundsMinY(GameObject go, out float minY)
        {
            minY = 0f;
            if (go == null) return false;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool hasAny = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                if (!hasAny)
                {
                    minY = r.bounds.min.y;
                    hasAny = true;
                }
                else
                {
                    minY = Mathf.Min(minY, r.bounds.min.y);
                }
            }
            return hasAny;
        }

        private static void SnapBottomToY(GameObject go, float targetY)
        {
            if (!TryGetRenderBoundsMinY(go, out float minY))
                return;

            float dy = targetY - minY;
            if (Mathf.Abs(dy) > 0.0005f)
                go.transform.position += Vector3.up * dy;
        }

        public bool TryGetActivePlantGameObject(int x, int y, out GameObject go)
        {
            go = null;
            if (runtime == null || runtime.Grid == null)
                return false;
            if (!runtime.Grid.InBounds(x, y))
                return false;

            int idx = y * runtime.Grid.Width + x;
            if (!_active.TryGetValue(idx, out var active))
                return false;
            if (active.go == null || !active.go.activeInHierarchy)
                return false;

            go = active.go;
            return true;
        }

        public void RenderNow()
        {
            if (runtime == null || runtime.Grid == null) return;

            int w = runtime.Grid.Width;
            int h = runtime.Grid.Height;

            var stale = new HashSet<int>(_active.Keys);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                var cell = runtime.Grid.Get(x, y);
                var stage = cell.PlantStage;
                if (!IsRenderableStage(stage))
                    continue;
                if (voxelWorld != null && !IsDryPlantableColumn(x, y))
                    continue;

                int idx = y * w + x;
                int variant = PickPrefabIndex(stage, idx);
                stale.Remove(idx);

                if (!_active.TryGetValue(idx, out var active) || active.go == null || active.stage != stage || active.variant != variant)
                {
                    if (_active.TryGetValue(idx, out var old))
                        Return(old);

                    var go = Rent(stage, variant);
                    go.name = $"Plant_{stage}_{x}_{y}";

                    active = new ActivePlant
                    {
                        go = go,
                        stage = stage,
                        variant = variant
                    };
                    _active[idx] = active;
                }

                var plantGO = active.go;
                float yaw = (PositiveHash(idx * 834927 + (int)stage * 97) % 4) * 90f;
                float scale = ScaleFor(stage, idx, cell, variant);
                float targetY;
                Vector3 targetPos;
                if (voxelWorld != null)
                {
                    var pos = voxelWorld.ColumnTopWorld(x, y);
                    targetY = pos.y + YOffsetFor(stage);
                    targetPos = new Vector3(pos.x, targetY, pos.z);
                }
                else
                {
                    var pos = runtime.Grid.CellCenterWorld(x, y) + Vector3.up * YOffsetFor(stage);
                    targetY = pos.y;
                    targetPos = pos;
                }

                plantGO.transform.SetPositionAndRotation(targetPos, Quaternion.Euler(0f, yaw, 0f));
                plantGO.transform.localScale = Vector3.one * scale;
                SnapBottomToY(plantGO, targetY);
            }

            foreach (var idx in stale)
            {
                if (_active.TryGetValue(idx, out var old))
                    Return(old);
                _active.Remove(idx);
            }
        }
    }
}
