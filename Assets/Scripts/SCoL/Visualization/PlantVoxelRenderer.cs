using System.Collections.Generic;
using System.IO;
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

        [Header("Flower Variant Selection")]
        [Tooltip("When enabled, seeding uses the selected flower variant instead of per-cell random flower selection.")]
        public bool enableManualFlowerVariantSelection = true;
        [SerializeField, Min(0)] private int selectedFlowerVariantIndex = 0;
        [Tooltip("Start with the first curated flower variant selected.")]
        public bool defaultToFirstCuratedFlower = true;

        [Header("Legacy Sunflower Variant")]
        [Tooltip("Disabled by default. Legacy Minecraft-style sunflower is no longer part of the active flower roster.")]
        public bool addMinecraftSunflowerVariant = false;
        [Tooltip("Asset path for the sunflower texture.")]
        public string sunflowerTextureAssetPath = "Assets/Models/Modeling/_Incoming/Flowers/Sunflower/Sunflower_BE7.png";
        public Vector2 sunflowerPlaneSize = new Vector2(2.70f, 4.80f);
        public float sunflowerBottomYOffset = 0.0f;
        [Tooltip("Remove black background from sunflower texture by converting near-black pixels to alpha.")]
        public bool sunflowerRemoveBlackBackground = true;
        [Range(0f, 1f)] public float sunflowerBlackCutoff = 0.20f;
        [Range(0f, 1f)] public float sunflowerBlackSoftness = 0.08f;
        [Tooltip("Additional chroma key tolerance from image border background color.")]
        [Range(0f, 1f)] public float sunflowerBackgroundTolerance = 0.18f;

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

        [Header("Flower Stage Size Enforcement")]
        [Tooltip("Normalize imported flower/sprout model sizes so Stage1 < Stage2 < Stage3 visually.")]
        public bool enforceFlowerStageHeightProgression = true;
        [Min(0.05f)] public float stage1TargetHeight = 1.80f;
        [Min(0.05f)] public float stage2TargetHeight = 2.88f;
        [Min(0.05f)] public float stage3TargetHeight = 4.08f;
        [Range(0.25f, 4f)] public float stageHeightScaleClampMin = 0.35f;
        [Range(0.25f, 4f)] public float stageHeightScaleClampMax = 2.80f;

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
        private GameObject _sunflowerTemplate;
        private Material _sunflowerMaterial;
        private Texture2D _sunflowerRuntimeTexture;

        private void Awake()
        {
            if (runtime == null) runtime = FindFirstObjectByType<SCoLRuntime>();
            if (voxelWorld == null) voxelWorld = FindFirstObjectByType<SCoL.Voxels.VoxelWorld>();

            AutoAssignVoxBoxDefaults();
            TryInjectMinecraftSunflowerVariant();
            if (defaultToFirstCuratedFlower && GetFlowerVariantCount() > 0)
                selectedFlowerVariantIndex = 0;
            EnsureFallbackMaterials();
        }

        private void OnDestroy()
        {
            if (_sunflowerRuntimeTexture != null)
                Destroy(_sunflowerRuntimeTexture);
            if (_sunflowerMaterial != null)
                Destroy(_sunflowerMaterial);
            if (_sunflowerTemplate != null)
                Destroy(_sunflowerTemplate);
        }

        private void AutoAssignVoxBoxDefaults()
        {
#if UNITY_EDITOR
            var curatedStage1 = LoadPrefabs(
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Sprout/SproutV1.obj",
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Sprout/SproutV2.obj",
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Sprout/SproutV3.obj");
            if (curatedStage1 != null && curatedStage1.Length > 0)
            {
                smallPlantPrefabs = curatedStage1;
            }
            else if (smallPlantPrefabs == null || smallPlantPrefabs.Length == 0)
            {
                smallPlantPrefabs = LoadPrefabs("Assets/Models/Modeling/_Incoming/Flowers/FlowerV2/Flower_Stage1.obj");
            }

            var curatedStage2 = LoadPrefabs(
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Sprout/SeedV1Sprout.obj",
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Sprout/SeedV2Sprout.obj",
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Sprout/SeedV3Sprout.obj");
            if (curatedStage2 != null && curatedStage2.Length > 0)
            {
                smallTreePrefabs = curatedStage2;
            }
            else if (smallTreePrefabs == null || smallTreePrefabs.Length == 0)
            {
                smallTreePrefabs = LoadPrefabs("Assets/Models/Modeling/_Incoming/Flowers/FlowerV2/Flower_Stage2.obj");
            }

            var curatedStage3 = LoadPrefabs(
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Flowers/FlowerV1.obj",
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Flowers/FlowerV2.obj",
                "Assets/Models/Modeling/_Incoming/3stageFlowers/Flowers/FlowerV3.obj");
            if (curatedStage3 != null && curatedStage3.Length > 0)
            {
                mediumTreePrefabs = curatedStage3;
            }
            else if (mediumTreePrefabs == null || mediumTreePrefabs.Length == 0)
            {
                mediumTreePrefabs = LoadPrefabs("Assets/Models/Modeling/_Incoming/Flowers/FlowerV2/Flower_FinalStage.obj");
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

        private void TryInjectMinecraftSunflowerVariant()
        {
            if (!addMinecraftSunflowerVariant)
                return;
            if (HasExplicitSeedVariantMapping())
                return;

            if (_sunflowerTemplate == null)
                _sunflowerTemplate = CreateMinecraftSunflowerTemplate();
            if (_sunflowerTemplate == null)
                return;

            smallPlantPrefabs = PrependUnique(smallPlantPrefabs, _sunflowerTemplate);
            smallTreePrefabs = PrependUnique(smallTreePrefabs, _sunflowerTemplate);
            mediumTreePrefabs = PrependUnique(mediumTreePrefabs, _sunflowerTemplate);
        }

        private bool HasExplicitSeedVariantMapping()
        {
            if (smallPlantPrefabs == null || smallPlantPrefabs.Length < 4)
                return false;

            bool hasSprout1 = false, hasSprout2 = false, hasSprout3 = false, hasStage1 = false;
            for (int i = 0; i < smallPlantPrefabs.Length; i++)
            {
                var p = smallPlantPrefabs[i];
                if (p == null) continue;
                string n = p.name.ToLowerInvariant();
                if (n.Contains("sproutv1")) hasSprout1 = true;
                else if (n.Contains("sproutv2")) hasSprout2 = true;
                else if (n.Contains("sproutv3")) hasSprout3 = true;
                else if (n.Contains("flower_stage1")) hasStage1 = true;
            }
            return hasSprout1 && hasSprout2 && hasSprout3 && hasStage1;
        }

        private GameObject CreateMinecraftSunflowerTemplate()
        {
            var tex = LoadSunflowerTexture();
            if (tex == null)
                return null;

            tex = BuildSunflowerTextureWithAlpha(tex);
            tex.filterMode = FilterMode.Point;
            tex.anisoLevel = 0;

            _sunflowerMaterial = BuildSunflowerMaterial(tex);
            if (_sunflowerMaterial == null)
                return null;

            var root = new GameObject("Minecraft_Sunflower");
            root.transform.SetParent(transform, worldPositionStays: false);
            root.SetActive(false);

            CreateSunflowerPlane(root.transform, 0f);
            CreateSunflowerPlane(root.transform, 90f);
            return root;
        }

        private void CreateSunflowerPlane(Transform parent, float yaw)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "SunflowerPlane";
            quad.transform.SetParent(parent, worldPositionStays: false);
            quad.transform.localPosition = new Vector3(0f, sunflowerBottomYOffset + sunflowerPlaneSize.y * 0.5f, 0f);
            quad.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            quad.transform.localScale = new Vector3(Mathf.Max(0.01f, sunflowerPlaneSize.x), Mathf.Max(0.01f, sunflowerPlaneSize.y), 1f);

            var r = quad.GetComponent<Renderer>();
            if (r != null)
                r.sharedMaterial = _sunflowerMaterial;

            var c = quad.GetComponent<Collider>();
            if (c != null)
                Destroy(c);
        }

        private static GameObject[] PrependUnique(GameObject[] source, GameObject head)
        {
            if (head == null)
                return source ?? System.Array.Empty<GameObject>();

            int existing = -1;
            if (source != null)
            {
                for (int i = 0; i < source.Length; i++)
                {
                    if (source[i] == head)
                    {
                        existing = i;
                        break;
                    }
                }
            }

            if (source == null || source.Length == 0)
                return new[] { head };
            if (existing == 0)
                return source;

            var list = new List<GameObject>(source.Length + 1) { head };
            for (int i = 0; i < source.Length; i++)
            {
                if (i == existing) continue;
                if (source[i] == null) continue;
                list.Add(source[i]);
            }
            return list.ToArray();
        }

        private Texture2D LoadSunflowerTexture()
        {
            Texture2D tex = null;
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(sunflowerTextureAssetPath))
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(sunflowerTextureAssetPath);
#endif
            if (tex != null)
                return tex;

            string absPath = sunflowerTextureAssetPath;
            if (absPath.StartsWith("Assets/"))
            {
                string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
                if (!string.IsNullOrEmpty(projectRoot))
                    absPath = Path.Combine(projectRoot, sunflowerTextureAssetPath);
            }

            if (File.Exists(absPath))
            {
                var bytes = File.ReadAllBytes(absPath);
                var runtimeTex = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false);
                if (runtimeTex.LoadImage(bytes))
                {
                    runtimeTex.name = "Sunflower_BE7_Runtime";
                    return runtimeTex;
                }
            }

            return null;
        }

        private Texture2D BuildSunflowerTextureWithAlpha(Texture2D source)
        {
            if (source == null)
                return null;
            if (!sunflowerRemoveBlackBackground)
                return source;

            var readable = CreateReadableCopy(source);
            if (readable == null)
                return source;

            float cutoff = Mathf.Clamp01(sunflowerBlackCutoff);
            float softness = Mathf.Clamp01(sunflowerBlackSoftness);
            float edge = Mathf.Clamp01(cutoff + Mathf.Max(0f, softness));
            Color bg = EstimateBorderBackgroundColor(readable);
            float bgTol = Mathf.Clamp01(sunflowerBackgroundTolerance);
            float bgTolSq = bgTol * bgTol;

            var px = readable.GetPixels32();
            for (int i = 0; i < px.Length; i++)
            {
                float r = px[i].r / 255f;
                float g = px[i].g / 255f;
                float b = px[i].b / 255f;
                float a = px[i].a / 255f;
                float maxRgb = Mathf.Max(r, Mathf.Max(g, b));

                float keep = 1f;
                if (maxRgb <= cutoff)
                {
                    keep = 0f;
                }
                else if (maxRgb < edge && softness > 0.0001f)
                {
                    keep = Mathf.InverseLerp(cutoff, edge, maxRgb);
                }

                float dr = r - bg.r;
                float dg = g - bg.g;
                float db = b - bg.b;
                float dSq = dr * dr + dg * dg + db * db;
                if (dSq <= bgTolSq)
                    keep = 0f;

                byte outA = (byte)Mathf.Clamp(Mathf.RoundToInt(a * keep * 255f), 0, 255);
                px[i].a = outA;
            }

            readable.SetPixels32(px);
            readable.Apply(updateMipmaps: false, makeNoLongerReadable: false);
            readable.filterMode = FilterMode.Point;
            readable.anisoLevel = 0;
            readable.name = "Sunflower_WithAlpha_Runtime";
            _sunflowerRuntimeTexture = readable;
            return readable;
        }

        private static Color EstimateBorderBackgroundColor(Texture2D tex)
        {
            if (tex == null || tex.width <= 0 || tex.height <= 0)
                return Color.black;

            int w = tex.width;
            int h = tex.height;
            Color32[] px = tex.GetPixels32();
            int count = 0;
            float sr = 0f, sg = 0f, sb = 0f;

            // Sample all edge pixels; for cutout source images this approximates background color.
            for (int x = 0; x < w; x++)
            {
                int i0 = x;
                int i1 = (h - 1) * w + x;
                sr += px[i0].r / 255f; sg += px[i0].g / 255f; sb += px[i0].b / 255f; count++;
                if (h > 1)
                {
                    sr += px[i1].r / 255f; sg += px[i1].g / 255f; sb += px[i1].b / 255f; count++;
                }
            }
            for (int y = 1; y < h - 1; y++)
            {
                int il = y * w;
                int ir = y * w + (w - 1);
                sr += px[il].r / 255f; sg += px[il].g / 255f; sb += px[il].b / 255f; count++;
                if (w > 1)
                {
                    sr += px[ir].r / 255f; sg += px[ir].g / 255f; sb += px[ir].b / 255f; count++;
                }
            }

            if (count <= 0)
                return Color.black;
            return new Color(sr / count, sg / count, sb / count, 1f);
        }

        private static Texture2D CreateReadableCopy(Texture2D src)
        {
            if (src == null)
                return null;

            var prev = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(src.width, src.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;

            var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, mipChain: false);
            copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0, false);
            copy.Apply(updateMipmaps: false, makeNoLongerReadable: false);

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return copy;
        }

        private static Material BuildSunflowerMaterial(Texture2D tex)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Unlit/Transparent Cutout");
            if (shader == null) shader = Shader.Find("Standard");
            if (shader == null) return null;

            var mat = new Material(shader) { name = "Minecraft_Sunflower_Mat" };
            mat.enableInstancing = true;

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);

            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.32f);
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 1f);
            return mat;
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

        public int GetFlowerVariantCount()
        {
            int small = CountValidPrefabs(smallPlantPrefabs);
            if (small > 0) return small;

            int stage2 = CountValidPrefabs(smallTreePrefabs);
            if (stage2 > 0) return stage2;

            int stage3 = CountValidPrefabs(mediumTreePrefabs);
            return stage3;
        }

        public string GetSelectedFlowerName()
        {
            return GetFlowerVariantName(selectedFlowerVariantIndex);
        }

        public string GetFlowerVariantDisplayName(int globalIndex)
        {
            return GetFlowerVariantName(globalIndex);
        }

        public int GetSelectedFlowerVariantIndex()
        {
            return Mathf.Max(0, selectedFlowerVariantIndex);
        }

        public void SetSelectedFlowerVariantIndex(int index)
        {
            int count = GetFlowerVariantCount();
            if (count <= 0)
            {
                selectedFlowerVariantIndex = 0;
                return;
            }

            int idx = index % count;
            if (idx < 0) idx += count;
            selectedFlowerVariantIndex = idx;
            RenderNow();
        }

        public bool CycleSelectedFlower(int delta)
        {
            int count = GetFlowerVariantCount();
            if (count <= 0)
                return false;

            int idx = selectedFlowerVariantIndex + delta;
            idx %= count;
            if (idx < 0) idx += count;
            selectedFlowerVariantIndex = idx;
            RenderNow();
            return true;
        }

        private static int CountValidPrefabs(GameObject[] prefabs)
        {
            if (prefabs == null || prefabs.Length == 0) return 0;
            int c = 0;
            for (int i = 0; i < prefabs.Length; i++)
                if (prefabs[i] != null) c++;
            return c;
        }

        private string GetFlowerVariantName(int globalIndex)
        {
            string name = TryGetFlowerNameFromStage(PlantStage.SmallPlant, globalIndex);
            if (!string.IsNullOrEmpty(name)) return name;

            name = TryGetFlowerNameFromStage(PlantStage.SmallTree, globalIndex);
            if (!string.IsNullOrEmpty(name)) return name;

            name = TryGetFlowerNameFromStage(PlantStage.MediumTree, globalIndex);
            if (!string.IsNullOrEmpty(name)) return name;

            return "Default Flower";
        }

        private string TryGetFlowerNameFromStage(PlantStage stage, int globalIndex)
        {
            int i = ResolveManualVariantIndex(stage, globalIndex);
            if (i < 0) return null;

            var prefabs = PrefabsFor(stage);
            if (prefabs == null || i >= prefabs.Length) return null;
            var p = prefabs[i];
            if (p == null) return null;
            return ToDisplayName(p.name);
        }

        private static string ToDisplayName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return "Flower";

            string lower = raw.ToLowerInvariant();
            if (lower.Contains("sproutv1") || lower.Contains("seedv1sprout") || lower.Contains("flowerv1"))
                return "Roseglow";
            if (lower.Contains("sproutv2") || lower.Contains("seedv2sprout") || lower.Contains("flowerv2"))
                return "Amberbloom";
            if (lower.Contains("sproutv3") || lower.Contains("seedv3sprout") || lower.Contains("flowerv3"))
                return "Moonpetal";

            string s = raw.Replace('_', ' ').Replace('-', ' ').Trim();
            s = s.Replace("Flower Stage1", "Flower");
            s = s.Replace("Flower Stage2", "Flower");
            s = s.Replace("Flower FinalStage", "Flower");
            s = s.Replace("Minecraft ", "");
            return s;
        }

        private static bool IsFlowerVisualStage(PlantStage stage)
        {
            return stage == PlantStage.SmallPlant || stage == PlantStage.SmallTree || stage == PlantStage.MediumTree;
        }

        private int ResolveManualVariantIndex(PlantStage stage, int globalIndex)
        {
            var prefabs = PrefabsFor(stage);
            if (prefabs == null || prefabs.Length == 0)
                return -1;

            int validCount = CountValidPrefabs(prefabs);
            if (validCount <= 0)
                return -1;

            int ordinal = globalIndex % validCount;
            if (ordinal < 0) ordinal += validCount;

            int seen = 0;
            for (int i = 0; i < prefabs.Length; i++)
            {
                if (prefabs[i] == null) continue;
                if (seen == ordinal) return i;
                seen++;
            }
            return -1;
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

        private int PickPrefabIndex(PlantStage stage, int cellIndex, CellState cell)
        {
            var prefabs = PrefabsFor(stage);
            if (prefabs == null || prefabs.Length == 0) return -1;

            if (IsFlowerVisualStage(stage))
            {
                if (cell != null && cell.FlowerVariantIndex >= 0)
                {
                    int locked = ResolveManualVariantIndex(stage, cell.FlowerVariantIndex);
                    if (locked >= 0)
                        return locked;
                }

                if (enableManualFlowerVariantSelection)
                {
                    int manual = ResolveManualVariantIndex(stage, selectedFlowerVariantIndex);
                    if (manual >= 0)
                        return manual;
                }
            }

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
            return n.Contains("flower") || n.Contains("daisy") || n.Contains("rose") || n.Contains("tulip") ||
                   n.Contains("sunflower") || n.Contains("sproutv") || n.Contains("seedv");
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

            // Runtime sunflower template is stored as inactive. Instantiated clones inherit inactive state,
            // so force activation here to guarantee newly planted variants are visible.
            if (!go.activeSelf)
                go.SetActive(true);

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

                // Backward-compat: lock variant for already-existing flower cells that were created
                // before FlowerVariantIndex was introduced.
                if (IsFlowerVisualStage(stage) &&
                    enableManualFlowerVariantSelection &&
                    cell.FlowerVariantIndex < 0)
                {
                    cell.FlowerVariantIndex = GetSelectedFlowerVariantIndex();
                }

                int idx = y * w + x;
                int variant = PickPrefabIndex(stage, idx, cell);
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
                TryNormalizeFlowerStageHeight(plantGO, stage);
                SnapBottomToY(plantGO, targetY);
            }

            foreach (var idx in stale)
            {
                if (_active.TryGetValue(idx, out var old))
                    Return(old);
                _active.Remove(idx);
            }
        }

        private void TryNormalizeFlowerStageHeight(GameObject go, PlantStage stage)
        {
            if (!enforceFlowerStageHeightProgression || go == null)
                return;
            if (!IsFlowerVisualStage(stage))
                return;

            float targetHeight = stage switch
            {
                PlantStage.SmallPlant => Mathf.Max(0.05f, stage1TargetHeight),
                PlantStage.SmallTree => Mathf.Max(0.05f, stage2TargetHeight),
                PlantStage.MediumTree => Mathf.Max(0.05f, stage3TargetHeight),
                _ => -1f
            };
            if (targetHeight <= 0f)
                return;

            if (!TryGetRendererBounds(go, out var b))
                return;
            float currentHeight = Mathf.Max(0.0001f, b.size.y);
            float k = targetHeight / currentHeight;
            float kMin = Mathf.Min(stageHeightScaleClampMin, stageHeightScaleClampMax);
            float kMax = Mathf.Max(stageHeightScaleClampMin, stageHeightScaleClampMax);
            k = Mathf.Clamp(k, kMin, kMax);

            if (!Mathf.Approximately(k, 1f))
                go.transform.localScale *= k;
        }

        private static bool TryGetRendererBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            var rs = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool has = false;
            for (int i = 0; i < rs.Length; i++)
            {
                var r = rs[i];
                if (r == null) continue;
                if (!has)
                {
                    bounds = r.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }
            return has;
        }
    }
}
