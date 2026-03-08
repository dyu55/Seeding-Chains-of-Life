using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using SCoL.Visualization;
using SCoL.Weather;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.Voxels
{
    /// <summary>
    /// Chunked voxel world with a simple heightmap-based generator + surface mesh rendering.
    /// </summary>
    [DisallowMultipleComponent]
    public class VoxelWorld : MonoBehaviour
    {
        public VoxelWorldConfig config;
        public VoxelWorldConfig Config => config;

        [Header("Rendering")]
        public Material grassMat;
        public Material dirtMat;
        public Material stoneMat;
        public Material waterMat;

        [Header("VoxBox Terrain Look")]
        [Tooltip("Use materials extracted from VoxBox tile prefabs for Grass/Dirt/Stone/Water.")]
        public bool useVoxBoxTerrainMaterials = true;
        [Tooltip("If true, VoxBox materials will replace inspector-assigned terrain materials at runtime.")]
        public bool overrideAssignedTerrainMaterials = true;
        [Tooltip("Keep original simple blue water instead of VoxBox water tile style.")]
        public bool useOriginalWaterMaterial = true;
        [Tooltip("Prefer teammate-provided Cube Grass/Dirt/Sand textures for terrain materials when available.")]
        public bool useIncomingTerrainBlockTextures = true;

        [Header("Shoreline Sand")]
        [Tooltip("Replace shoreline surface blocks with sand (stone channel) around lakes/sea.")]
        public bool enableShorelineSand = true;
        [Min(0)] public int shorelineSandMinRings = 2;
        [Min(0)] public int shorelineSandMaxRings = 3;
        [Tooltip("Noise scale controlling where 2-ring vs 3-ring shoreline appears.")]
        [Min(0.001f)] public float shorelineSandNoiseScale = 0.085f;

        [Header("Terrain Shaping")]
        [Tooltip("Keep map edges above sea level so lakes stay fully inside world bounds.")]
        [Min(0)] public int edgeLandBufferBlocks = 4;
        [Tooltip("Minimum terrain height above sea level inside edge buffer.")]
        [Min(0)] public int edgeMinHeightAboveSea = 1;
        [Tooltip("Create a few deterministic flat areas for building placement.")]
        public bool enableFlatBuildPads = true;
        [Min(0)] public int flatBuildPadCount = 4;
        [Min(2f)] public float flatBuildPadRadius = 7f;
        [Range(0f, 1f)] public float flatBuildPadBlend = 0.95f;

        [Header("Low Poly Terrain Visual")]
        [Tooltip("Render a smoothed low-poly terrain/water mesh and hide voxel cube renderers.")]
        public bool useLowPolyTerrainVisual = true;
        [Tooltip("Use the low-poly terrain mesh collider to avoid stair-step movement on smoothed visuals.")]
        public bool useLowPolyTerrainCollider = true;
        public Material lowPolyTerrainMaterial;
        public Material lowPolyWaterMaterial;
        [Min(0f)] public float lowPolyLandYOffset = 0.02f;
        [Min(0f)] public float lowPolyWaterYOffset = 0.01f;
        [Min(0.01f)] public float lowPolyUVScale = 0.20f;
        [Range(0, 6)] public int lowPolySmoothingPasses = 3;
        [Range(0f, 1f)] public float lowPolySmoothingStrength = 0.65f;
        [Range(0f, 1f)] public float lowPolyDiagonalSmoothingWeight = 0.50f;
        [Range(1, 3)] public int lowPolyTerrainSubdivisions = 2;
        [Range(1, 4)] public int lowPolyWaterSubdivisions = 4;
        [Range(0, 8)] public int lowPolyWaterMaskSmoothingPasses = 3;
        [Range(0f, 1f)] public float lowPolyWaterMaskSmoothingStrength = 0.70f;
        [Range(0.10f, 0.90f)] public float lowPolyWaterMaskThreshold = 0.34f;
        [Tooltip("Expand water mask outward before triangulation so shoreline tucks under terrain.")]
        [Range(0, 6)] public int lowPolyWaterMaskExpandPasses = 2;
        [Range(0f, 1f)] public float lowPolyWaterMaskExpandStrength = 0.85f;
        [Tooltip("Expand water slightly under shoreline terrain to hide jagged edges.")]
        [Range(0f, 1.5f)] public float lowPolyWaterHorizontalOverhang = 0.55f;
        [Tooltip("Push water surface down slightly so shoreline is embedded into terrain.")]
        [Range(0f, 0.6f)] public float lowPolyWaterEmbedDepth = 0.10f;
        [Tooltip("Render terrain mesh as two-sided to avoid under-view missing faces.")]
        public bool lowPolyDoubleSided = true;
        [Tooltip("Render water mesh as two-sided.")]
        public bool lowPolyWaterDoubleSided = false;

        [Header("Grass Props (decorations)")]
        // Default OFF: avoids auto-spawning legacy/placeholder props when entering Play mode.
        // You can re-enable in inspector if desired.
        public bool enableGrassProps = false;
        [Range(0f, 1f)] public float grassPropDensity = 0.20f;
        public int grassPropsMaxPerChunk = 256;
        [Tooltip("Extra perf guardrail: only render grass props within this many chunks from the camera.")]
        public int grassPropsDistanceChunks = 2;
        public Mesh grassPropMesh;
        public Material grassPropMaterial;

        [Header("Flora Props (flowers/trees)")]
        // Default OFF: avoids auto-spawning legacy/placeholder flora when entering Play mode.
        // You can re-enable in inspector if desired.
        public bool enableFloraProps = false;
        [Tooltip("Extra perf guardrail: only render flora props (flowers/trees) within this many chunks from the camera.")]
        public int floraPropsDistanceChunks = 2;
        [Range(0f, 1f)] public float flowerDensity = 0.02f;
        public int flowersMaxPerChunk = 32;
        public Vector2 flowerScaleRange = new Vector2(0.7f, 1.1f);
        public Mesh daisyMesh;
        public Mesh roseMesh;
        public Material flowerMaterial;

        [Range(0f, 1f)] public float treeDensity = 0.004f;
        public int treesMaxPerChunk = 12;
        public Vector2 treeScaleRange = new Vector2(0.9f, 1.4f);
        public Mesh treeMesh;
        public Material treeMaterial;

        [Header("Featured Trees (Fixed Count)")]
        [Tooltip("Spawn a fixed set of evenly distributed trees across the map.")]
        public bool enableFeaturedTrees = true;
        [Min(0)] public int featuredTreeCount = 20;
        [Tooltip("Prefer grass surface for featured tree placement.")]
        public bool featuredTreesPreferGrass = true;
        [Min(0)] public int featuredTreeSearchRadius = 10;
        public Vector2 featuredTreeScaleRange = new Vector2(4.8f, 5.2f);
        [Min(-2f)] public float featuredTreeYOffset = 0f;
        [Range(0f, 1.5f)] public float featuredTreeRootEmbedDepth = 0.22f;

        [Header("World Boundary")]
        [Tooltip("Create 4 border walls around the voxel map to prevent leaving the world.")]
        public bool enableWorldBoundary = true;
        [Min(0.5f)] public float boundaryThickness = 2f;
        [Min(2f)] public float boundaryHeight = 64f;
        [Tooltip("Bottom Y of the boundary walls relative to voxel origin.")]
        public float boundaryBottomOffset = -2f;
        [Tooltip("If true, render boundary walls. If false, keep colliders only.")]
        public bool showBoundaryWalls = false;
        public Material boundaryWallMaterial;

        [Header("Winter Overlay")]
        [Tooltip("When Winter is active, add a thin snow layer on land and an ice layer over water.")]
        public bool enableWinterSnowAndIce = true;
        [Min(0.001f)] public float winterOverlayHeightOffset = 0.02f;
        [Min(0.001f)] public float winterOverlayInset = 0.015f;
        public Color snowOverlayColor = new Color(0.98f, 0.98f, 1.0f, 0.92f);
        public Color iceOverlayColor = new Color(0.70f, 0.90f, 1.0f, 0.84f);

        [Tooltip("If true, world (0,0,0) is placed at this transform position.")]
        public bool useTransformAsOrigin = true;

        private System.Random _rng;
        private int _seed;
        private Vector2 _noiseOffset;

        private readonly Dictionary<Vector2Int, VoxelChunk> _chunks = new();
        private readonly Dictionary<Vector2Int, GameObject> _chunkGOs = new();
        private readonly Dictionary<Vector2Int, VoxelBlockType[]> _chunkSubmeshOrder = new();
        private readonly Dictionary<Vector2Int, MeshCollider> _chunkColliders = new();
        private readonly Dictionary<Vector2Int, GrassPropChunk> _chunkGrassProps = new();
        private readonly Dictionary<Vector2Int, FloraPropChunk> _chunkFloraProps = new();
        private GameObject _boundaryRoot;
        private GameObject _featuredTreesRoot;
        private GameObject _lowPolyVisualRoot;
        private Mesh _lowPolyLandMesh;
        private Mesh _lowPolyWaterMesh;
        private MeshRenderer _lowPolyWaterRenderer;
        private MeshCollider _lowPolyLandCollider;
        private float[,] _lowPolyCornerHeights;
        private GameObject _winterOverlayRoot;
        private GameObject _snowOverlayGO;
        private GameObject _iceOverlayGO;
        private GameObject _iceColliderGO;
        private Material _snowOverlayMat;
        private Material _iceOverlayMat;
        private SeasonSkyboxController _seasonSkybox;
        private WeatherSystem _weatherSystem;
        private SCoL.SCoLRuntime _runtime;
        private float _nextSeasonLookupAt;
        private bool _winterVisualsActive;
        private bool _useCubeNetGrassUV;
        private bool _useCubeNetDirtUV;
        private bool _useCubeNetStoneUV;
        private bool _waterSurfaceVisible = true;
        private Material _hiddenWaterMat;
        private readonly List<FlatBuildPad> _flatBuildPads = new();

        private struct FlatBuildPad
        {
            public Vector2 centerXZ;
            public float radius;
            public int targetHeight;
        }

        private float _streamT;
        private Texture2D _grassFaceAtlasRuntime;

        public Vector3 OriginWorld => useTransformAsOrigin ? transform.position : Vector3.zero;

        public void InitIfNeeded()
        {
            if (config == null)
            {
                config = Resources.Load<VoxelWorldConfig>("Voxels/VoxelWorldConfig_Default");
            }

            if (config == null)
            {
                Debug.LogWarning("VoxelWorld: missing config (VoxelWorldConfig). Using runtime defaults.");
                config = ScriptableObject.CreateInstance<VoxelWorldConfig>();
            }

            _seed = config.useFixedSeed ? config.seed : Environment.TickCount;
            _rng = new System.Random(_seed);
            _noiseOffset = new Vector2(_rng.Next(-100000, 100000), _rng.Next(-100000, 100000));
            InitFlatBuildPads();

            EnsureDefaultMaterials();
            EnforceWaterEdgeSmoothingDefaults();
            EnsureGrassPropAssets();
            GenerateAll();
        }

        private void Awake()
        {
            InitIfNeeded();
        }

        private void Update()
        {
            if (config == null) return;
            _streamT += Time.unscaledDeltaTime;
            if (_streamT < config.streamingUpdateSeconds) return;
            _streamT = 0f;

            StreamAroundCamera();
            UpdateWinterVisualState();
        }

        private void EnsureDefaultMaterials()
        {
            // Create simple materials if none provided.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            TryApplyVoxBoxTerrainMaterials(shader);

            _useCubeNetGrassUV = false;
            _useCubeNetDirtUV = false;
            _useCubeNetStoneUV = false;

            if (grassMat == null) grassMat = new Material(shader) { name = "Voxel_Grass" };
            grassMat.enableInstancing = true;

            if (dirtMat == null) dirtMat = new Material(shader) { name = "Voxel_Dirt" };
            dirtMat.enableInstancing = true;

            if (stoneMat == null) stoneMat = new Material(shader) { name = "Voxel_Stone" };
            stoneMat.enableInstancing = true;

            TryApplyIncomingTerrainBlockTextures(out bool incomingGrassApplied, out bool incomingDirtApplied, out bool incomingSandApplied);

            if (!incomingGrassApplied)
                ApplyGrassFaceAtlasOrFallback(grassMat);

            if (!incomingDirtApplied)
            {
                dirtMat.color = new Color(0.45f, 0.32f, 0.22f);
                ApplyTextureFromResourcesIfAvailable(dirtMat, "Voxels/s1");
            }

            // Reuse stone channel for the new sand look without changing voxel type layout.
            if (!incomingSandApplied)
            {
                stoneMat.color = new Color(0.55f, 0.55f, 0.60f);
                ApplyTextureFromResourcesIfAvailable(stoneMat, "Voxels/s1");
            }

            if (useOriginalWaterMaterial && (overrideAssignedTerrainMaterials || waterMat == null))
                waterMat = null;

            if (waterMat == null) waterMat = new Material(shader) { name = "Voxel_Water" };
            waterMat.enableInstancing = true;
            waterMat.color = new Color(0.18f, 0.35f, 0.85f, 0.85f);
            ApplyTextureFromResourcesIfAvailable(waterMat, "Voxels/w1");

            if (lowPolyTerrainMaterial == null)
            {
                lowPolyTerrainMaterial = new Material(shader) { name = "LowPoly_Terrain" };
                lowPolyTerrainMaterial.enableInstancing = true;
            }
            if (lowPolyTerrainMaterial.HasProperty("_BaseMap")) lowPolyTerrainMaterial.SetTexture("_BaseMap", null);
            if (lowPolyTerrainMaterial.HasProperty("_MainTex")) lowPolyTerrainMaterial.SetTexture("_MainTex", null);
            if (lowPolyTerrainMaterial.HasProperty("_BaseColor")) lowPolyTerrainMaterial.SetColor("_BaseColor", new Color(0.38f, 0.52f, 0.33f, 1f));
            if (lowPolyTerrainMaterial.HasProperty("_Color")) lowPolyTerrainMaterial.SetColor("_Color", new Color(0.38f, 0.52f, 0.33f, 1f));
            if (lowPolyTerrainMaterial.HasProperty("_Smoothness")) lowPolyTerrainMaterial.SetFloat("_Smoothness", 0.02f);
            if (lowPolyTerrainMaterial.HasProperty("_Glossiness")) lowPolyTerrainMaterial.SetFloat("_Glossiness", 0.02f);
            if (lowPolyTerrainMaterial.HasProperty("_Cull")) lowPolyTerrainMaterial.SetFloat("_Cull", 0f);
            if (lowPolyTerrainMaterial.HasProperty("_CullMode")) lowPolyTerrainMaterial.SetFloat("_CullMode", 0f);

            if (lowPolyWaterMaterial == null)
            {
                lowPolyWaterMaterial = new Material(shader) { name = "LowPoly_Water" };
                lowPolyWaterMaterial.enableInstancing = true;
            }
            if (lowPolyWaterMaterial.HasProperty("_BaseMap")) lowPolyWaterMaterial.SetTexture("_BaseMap", null);
            if (lowPolyWaterMaterial.HasProperty("_MainTex")) lowPolyWaterMaterial.SetTexture("_MainTex", null);
            if (lowPolyWaterMaterial.HasProperty("_BaseColor")) lowPolyWaterMaterial.SetColor("_BaseColor", new Color(0.22f, 0.42f, 0.72f, 0.88f));
            if (lowPolyWaterMaterial.HasProperty("_Color")) lowPolyWaterMaterial.SetColor("_Color", new Color(0.22f, 0.42f, 0.72f, 0.88f));
            if (lowPolyWaterMaterial.HasProperty("_Smoothness")) lowPolyWaterMaterial.SetFloat("_Smoothness", 0.06f);
            if (lowPolyWaterMaterial.HasProperty("_Glossiness")) lowPolyWaterMaterial.SetFloat("_Glossiness", 0.06f);
            if (lowPolyWaterMaterial.HasProperty("_Cull")) lowPolyWaterMaterial.SetFloat("_Cull", 0f);
            if (lowPolyWaterMaterial.HasProperty("_CullMode")) lowPolyWaterMaterial.SetFloat("_CullMode", 0f);

            if (showBoundaryWalls && boundaryWallMaterial == null)
            {
                boundaryWallMaterial = new Material(shader) { name = "Voxel_BoundaryWall" };
                boundaryWallMaterial.enableInstancing = true;
                boundaryWallMaterial.color = new Color(0.85f, 0.25f, 0.20f, 0.30f);
            }
        }

        private void EnforceWaterEdgeSmoothingDefaults()
        {
            // Keep shoreline anti-aliasing defaults even if old scene serialization has weaker values.
            lowPolyWaterSubdivisions = Mathf.Clamp(Mathf.Max(lowPolyWaterSubdivisions, 4), 1, 4);
            lowPolyWaterMaskSmoothingPasses = Mathf.Clamp(Mathf.Max(lowPolyWaterMaskSmoothingPasses, 4), 0, 8);
            lowPolyWaterMaskSmoothingStrength = Mathf.Clamp01(Mathf.Max(lowPolyWaterMaskSmoothingStrength, 0.78f));
            lowPolyWaterMaskThreshold = Mathf.Clamp(Mathf.Min(lowPolyWaterMaskThreshold, 0.24f), 0.10f, 0.90f);
            lowPolyWaterMaskExpandPasses = Mathf.Clamp(Mathf.Max(lowPolyWaterMaskExpandPasses, 2), 0, 6);
            lowPolyWaterMaskExpandStrength = Mathf.Clamp01(Mathf.Max(lowPolyWaterMaskExpandStrength, 0.85f));
            lowPolyWaterHorizontalOverhang = Mathf.Clamp(Mathf.Max(lowPolyWaterHorizontalOverhang, 0.90f), 0f, 1.5f);
            lowPolyWaterEmbedDepth = Mathf.Clamp(Mathf.Max(lowPolyWaterEmbedDepth, 0.12f), 0f, 0.6f);
        }

        private void TryApplyVoxBoxTerrainMaterials(Shader fallbackShader)
        {
            if (!useVoxBoxTerrainMaterials) return;

            // Replace only if requested or currently unset.
            if (overrideAssignedTerrainMaterials || grassMat == null)
                grassMat = LoadVoxBoxMaterialFromPrefab("Assets/VoxBox/Prefabs/Grass Tiles/Tile 6.prefab", "Voxel_Grass_VoxBox", fallbackShader);
            if (overrideAssignedTerrainMaterials || dirtMat == null)
                dirtMat = LoadVoxBoxMaterialFromPrefab("Assets/VoxBox/Prefabs/Grass Tiles/Tile 5.prefab", "Voxel_Dirt_VoxBox", fallbackShader);
            if (overrideAssignedTerrainMaterials || stoneMat == null)
                stoneMat = LoadVoxBoxMaterialFromPrefab("Assets/VoxBox/Prefabs/Tiles/Tile 7.prefab", "Voxel_Stone_VoxBox", fallbackShader);
            if (!useOriginalWaterMaterial && (overrideAssignedTerrainMaterials || waterMat == null))
                waterMat = LoadVoxBoxMaterialFromPrefab("Assets/VoxBox/Prefabs/Grass Tiles/Tile 8.prefab", "Voxel_Water_VoxBox", fallbackShader);
        }

        private static Material LoadVoxBoxMaterialFromPrefab(string prefabPath, string runtimeName, Shader fallbackShader)
        {
#if UNITY_EDITOR
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab != null)
            {
                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    var src = renderers[i] != null ? renderers[i].sharedMaterial : null;
                    if (src == null) continue;

                    var mat = new Material(src);
                    mat.name = runtimeName;
                    mat.enableInstancing = true;
                    return mat;
                }
            }
#endif

            var fallback = new Material(fallbackShader) { name = runtimeName };
            fallback.enableInstancing = true;
            return fallback;
        }

        private void EnsureGrassPropAssets()
        {
#if UNITY_EDITOR
            // Auto-load meshes/materials in editor for quick iteration.
            // Prefer the newer low-poly grass patch as the main grass prop.
            if (grassPropMesh == null)
            {
                var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>(
                    "Assets/Models/Modeling/_Incoming/grass patch low poly/tripo_convert_cb595927-be5f-4853-8a13-d5735ba4871c.obj");
                if (mesh != null) grassPropMesh = mesh;
            }

            // Only use the low-poly grass patch (no variants) to keep the look consistent.

            if (grassPropMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");

                grassPropMaterial = new Material(shader) { name = "GrassProp_Mat" };
                grassPropMaterial.enableInstancing = true;

                // Reuse the same basecolor as ground grass for now.
                var tex = Resources.Load<Texture2D>("Voxels/grass_basecolor");
                if (tex != null)
                {
                    tex.filterMode = FilterMode.Point;
                    if (grassPropMaterial.HasProperty("_BaseMap")) grassPropMaterial.SetTexture("_BaseMap", tex);
                    if (grassPropMaterial.HasProperty("_MainTex")) grassPropMaterial.SetTexture("_MainTex", tex);
                    if (grassPropMaterial.HasProperty("_BaseColor")) grassPropMaterial.SetColor("_BaseColor", Color.white);
                    if (grassPropMaterial.HasProperty("_Color")) grassPropMaterial.SetColor("_Color", Color.white);
                }
                else
                {
                    grassPropMaterial.color = new Color(0.35f, 0.85f, 0.35f);
                }
            }

            // Flowers / Trees
            if (daisyMesh == null)
            {
                var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/Modeling/_Incoming/Daisy/base.obj");
                if (mesh != null) daisyMesh = mesh;
            }
            if (roseMesh == null)
            {
                var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/Modeling/_Incoming/rose/rose.obj");
                if (mesh != null) roseMesh = mesh;
            }
            if (treeMesh == null)
            {
                const string newTreeObjPath = "Assets/Models/Modeling/_Incoming/Vegetation/Trees/Tree_New_Set/3d model.obj";
                var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(newTreeObjPath);
                if (model != null)
                {
                    var mf = model.GetComponentInChildren<MeshFilter>(true);
                    if (mf != null && mf.sharedMesh != null)
                        treeMesh = mf.sharedMesh;
                    else
                    {
                        var smr = model.GetComponentInChildren<SkinnedMeshRenderer>(true);
                        if (smr != null && smr.sharedMesh != null)
                            treeMesh = smr.sharedMesh;
                    }
                }
            }
            if (treeMesh == null)
            {
                var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/Modeling/_Incoming/Vegetation/Trees/Tree 1 Low Poly/tree 1 low poly.glb");
                if (mesh != null) treeMesh = mesh;
            }
            if (treeMesh == null)
            {
                var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/Modeling/_Incoming/Vegetation/Trees/Tree 1 Growth/Tree Growth.obj");
                if (mesh != null) treeMesh = mesh;
            }

            if (flowerMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                flowerMaterial = new Material(shader) { name = "FlowerProp_Mat" };
                flowerMaterial.enableInstancing = true;
                flowerMaterial.color = new Color(0.95f, 0.90f, 0.95f);
            }

            if (treeMaterial == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) shader = Shader.Find("Standard");
                treeMaterial = new Material(shader) { name = "TreeProp_Mat" };
                treeMaterial.enableInstancing = true;
                treeMaterial.color = new Color(0.55f, 0.75f, 0.55f);

                const string newTreeObjPath = "Assets/Models/Modeling/_Incoming/Vegetation/Trees/Tree_New_Set/3d model.obj";
                var model = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(newTreeObjPath);
                if (model != null)
                {
                    var r = model.GetComponentInChildren<Renderer>(true);
                    if (r != null && r.sharedMaterial != null)
                    {
                        treeMaterial = new Material(r.sharedMaterial) { name = "TreeProp_Mat_New" };
                        treeMaterial.enableInstancing = true;
                    }
                }
            }

            const string newTreeTexturePath = "Assets/Models/Modeling/_Incoming/Vegetation/Trees/Tree_New_Set/mesh1.jpg";
            var treeTex = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(newTreeTexturePath);
            if (treeTex != null && treeMaterial != null)
            {
                treeTex.filterMode = FilterMode.Bilinear;
                treeTex.anisoLevel = 4;
                if (treeMaterial.HasProperty("_BaseMap")) treeMaterial.SetTexture("_BaseMap", treeTex);
                if (treeMaterial.HasProperty("_MainTex")) treeMaterial.SetTexture("_MainTex", treeTex);
                if (treeMaterial.HasProperty("_BaseColor")) treeMaterial.SetColor("_BaseColor", Color.white);
                if (treeMaterial.HasProperty("_Color")) treeMaterial.SetColor("_Color", Color.white);
            }
#endif
        }

        private static void ApplyTextureFromResourcesIfAvailable(Material m, params string[] resourcePaths)
        {
            if (m == null) return;

            Texture2D tex = null;
            if (resourcePaths != null)
            {
                for (int i = 0; i < resourcePaths.Length; i++)
                {
                    var path = resourcePaths[i];
                    if (string.IsNullOrEmpty(path)) continue;
                    tex = Resources.Load<Texture2D>(path);
                    if (tex != null) break;
                }
            }
            if (tex == null)
                return;

            ApplyTextureToMaterial(m, tex);
        }

        private static bool ApplyTextureToMaterial(Material m, Texture2D tex)
        {
            if (m == null || tex == null)
                return false;

            tex.filterMode = FilterMode.Point;
            tex.anisoLevel = 0;

            if (m.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex"))
                m.SetTexture("_MainTex", tex);

            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", Color.white);

            return true;
        }

        private void TryApplyIncomingTerrainBlockTextures(out bool grassApplied, out bool dirtApplied, out bool sandApplied)
        {
            grassApplied = false;
            dirtApplied = false;
            sandApplied = false;

            if (!useIncomingTerrainBlockTextures)
                return;

            var grassTex = LoadEditorOrResourceTexture(
                "Assets/Models/Modeling/_Incoming/Terrain/Cube Grass.png",
                "Voxels/cube_grass");
            var dirtTex = LoadEditorOrResourceTexture(
                "Assets/Models/Modeling/_Incoming/Terrain/Cube Dirt.png",
                "Voxels/cube_dirt");
            var sandTex = LoadEditorOrResourceTexture(
                "Assets/Models/Modeling/_Incoming/Terrain/Cube Sand.png",
                "Voxels/cube_sand");

            var resolvedDirt = dirtTex != null ? dirtTex : (sandTex != null ? sandTex : grassTex);
            var resolvedSand = sandTex != null ? sandTex : (dirtTex != null ? dirtTex : grassTex);

            if (grassMat != null && grassTex != null)
            {
                grassApplied = ApplyTextureToMaterial(grassMat, grassTex);
                _useCubeNetGrassUV = grassApplied;
            }

            if (dirtMat != null && resolvedDirt != null)
            {
                dirtApplied = ApplyTextureToMaterial(dirtMat, resolvedDirt);
                _useCubeNetDirtUV = dirtApplied;
            }

            // Stone channel is used as the terrain third layer; map it to sand if provided.
            if (stoneMat != null && resolvedSand != null)
            {
                sandApplied = ApplyTextureToMaterial(stoneMat, resolvedSand);
                _useCubeNetStoneUV = sandApplied;
            }
        }

        private static Texture2D LoadEditorOrResourceTexture(string editorAssetPath, params string[] resourcePaths)
        {
#if UNITY_EDITOR
            if (!string.IsNullOrEmpty(editorAssetPath))
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(editorAssetPath);
                if (tex != null)
                    return tex;
            }
#endif
            if (resourcePaths == null)
                return null;

            for (int i = 0; i < resourcePaths.Length; i++)
            {
                var path = resourcePaths[i];
                if (string.IsNullOrEmpty(path)) continue;
                var tex = Resources.Load<Texture2D>(path);
                if (tex != null) return tex;
            }

            return null;
        }

        private void ApplyGrassFaceAtlasOrFallback(Material m)
        {
            if (m == null) return;

            var side = Resources.Load<Texture2D>("Voxels/grassside");
            var top = Resources.Load<Texture2D>("Voxels/topgrass");
            var bottom = Resources.Load<Texture2D>("Voxels/grassbot");

            // If no face textures are provided, keep old single-texture fallback behavior.
            if (side == null && top == null && bottom == null)
            {
                ApplyTextureFromResourcesIfAvailable(m, "Voxels/grass", "Voxels/grass_basecolor");
                return;
            }

            if (side == null) side = top != null ? top : bottom;
            if (top == null) top = side != null ? side : bottom;
            if (bottom == null) bottom = side != null ? side : top;
            if (side == null || top == null || bottom == null)
            {
                ApplyTextureFromResourcesIfAvailable(m, "Voxels/grass", "Voxels/grass_basecolor");
                return;
            }

            _grassFaceAtlasRuntime = BuildHorizontalAtlas(side, top, bottom, "Runtime_GrassFaceAtlas");
            if (_grassFaceAtlasRuntime == null)
            {
                ApplyTextureFromResourcesIfAvailable(m, "Voxels/grass", "Voxels/grass_basecolor");
                return;
            }

            if (m.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", _grassFaceAtlasRuntime);
            if (m.HasProperty("_MainTex"))
                m.SetTexture("_MainTex", _grassFaceAtlasRuntime);
            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", Color.white);
        }

        private static Texture2D BuildHorizontalAtlas(Texture2D side, Texture2D top, Texture2D bottom, string texName)
        {
            if (side == null || top == null || bottom == null)
                return null;

            int tileW = Mathf.Max(1, Mathf.Max(side.width, Mathf.Max(top.width, bottom.width)));
            int tileH = Mathf.Max(1, Mathf.Max(side.height, Mathf.Max(top.height, bottom.height)));

            var rt = RenderTexture.GetTemporary(tileW * 3, tileH, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;

            GL.PushMatrix();
            GL.LoadPixelMatrix(0, rt.width, rt.height, 0);
            GL.Clear(true, true, Color.clear);

            Graphics.DrawTexture(new Rect(0, 0, tileW, tileH), side);
            Graphics.DrawTexture(new Rect(tileW, 0, tileW, tileH), top);
            Graphics.DrawTexture(new Rect(tileW * 2, 0, tileW, tileH), bottom);

            GL.PopMatrix();

            var atlas = new Texture2D(tileW * 3, tileH, TextureFormat.RGBA32, false);
            atlas.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0, false);
            atlas.filterMode = FilterMode.Point;
            atlas.anisoLevel = 0;
            atlas.name = texName;
            atlas.Apply(false, true);

            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return atlas;
        }

        public bool UseCubeNetUVFor(VoxelBlockType t)
        {
            return t switch
            {
                VoxelBlockType.Grass => _useCubeNetGrassUV,
                VoxelBlockType.Dirt => _useCubeNetDirtUV,
                VoxelBlockType.Stone => _useCubeNetStoneUV,
                _ => false
            };
        }

        public void GenerateAll()
        {
            ClearWorldObjects();
            _chunks.Clear();

            int cs = config.chunkSize;
            int chunksX = Mathf.CeilToInt(config.worldWidth / (float)cs);
            int chunksZ = Mathf.CeilToInt(config.worldDepth / (float)cs);
            var coords = new List<Vector2Int>(chunksX * chunksZ);

            for (int cz = 0; cz < chunksZ; cz++)
            for (int cx = 0; cx < chunksX; cx++)
            {
                var cc = new Vector2Int(cx, cz);
                var chunk = new VoxelChunk(cc, cs, config.worldHeight);
                _chunks[cc] = chunk;
                coords.Add(cc);

                FillChunkTerrain(chunk);
            }

            ApplyShorelineSandRings();

            for (int i = 0; i < coords.Count; i++)
                BuildChunkGO(coords[i]);

            RebuildLowPolyVisuals();
            BuildFeaturedTrees();
            BuildWorldBoundary();
            RebuildWinterOverlayMeshes();
            UpdateWinterVisualState(force: true);
        }

        private void ClearWorldObjects()
        {
            foreach (var kv in _chunkGOs)
            {
                if (kv.Value != null)
                    Destroy(kv.Value);
            }
            _chunkGOs.Clear();
            _chunkSubmeshOrder.Clear();
            _chunkColliders.Clear();
            _chunkGrassProps.Clear();
            _chunkFloraProps.Clear();
            if (_lowPolyVisualRoot != null)
                Destroy(_lowPolyVisualRoot);
            _lowPolyVisualRoot = null;
            if (_lowPolyLandMesh != null)
                Destroy(_lowPolyLandMesh);
            _lowPolyLandMesh = null;
            if (_lowPolyWaterMesh != null)
                Destroy(_lowPolyWaterMesh);
            _lowPolyWaterMesh = null;
            _lowPolyWaterRenderer = null;
            _lowPolyLandCollider = null;
            _lowPolyCornerHeights = null;
            if (_featuredTreesRoot != null)
                Destroy(_featuredTreesRoot);
            _featuredTreesRoot = null;
            if (_boundaryRoot != null)
                Destroy(_boundaryRoot);
            _boundaryRoot = null;
            if (_winterOverlayRoot != null)
                Destroy(_winterOverlayRoot);
            _winterOverlayRoot = null;
            _snowOverlayGO = null;
            _iceOverlayGO = null;
            _iceColliderGO = null;
        }

        private void RebuildWinterOverlayMeshes()
        {
            if (!enableWinterSnowAndIce || config == null)
                return;

            EnsureWinterOverlayObjects();
            BuildOverlayMeshInto(_snowOverlayGO, includeWaterColumns: false);
            BuildOverlayMeshInto(_iceOverlayGO, includeWaterColumns: true);
            BuildIceColliderMesh();
        }

        private void EnsureWinterOverlayObjects()
        {
            if (_winterOverlayRoot == null)
            {
                _winterOverlayRoot = new GameObject("WinterOverlays");
                _winterOverlayRoot.transform.SetParent(transform, worldPositionStays: true);
                _winterOverlayRoot.transform.position = OriginWorld;
                _winterOverlayRoot.SetActive(false);
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (_snowOverlayMat == null)
            {
                _snowOverlayMat = new Material(shader) { name = "Winter_SnowOverlay" };
                _snowOverlayMat.enableInstancing = true;
            }
            if (_snowOverlayMat.HasProperty("_BaseColor")) _snowOverlayMat.SetColor("_BaseColor", snowOverlayColor);
            if (_snowOverlayMat.HasProperty("_Color")) _snowOverlayMat.SetColor("_Color", snowOverlayColor);

            if (_iceOverlayMat == null)
            {
                _iceOverlayMat = new Material(shader) { name = "Winter_IceOverlay" };
                _iceOverlayMat.enableInstancing = true;
            }
            if (_iceOverlayMat.HasProperty("_BaseColor")) _iceOverlayMat.SetColor("_BaseColor", iceOverlayColor);
            if (_iceOverlayMat.HasProperty("_Color")) _iceOverlayMat.SetColor("_Color", iceOverlayColor);

            if (_snowOverlayGO == null)
                _snowOverlayGO = CreateOverlayGO("SnowOverlay", _snowOverlayMat);
            if (_iceOverlayGO == null)
                _iceOverlayGO = CreateOverlayGO("IceOverlay", _iceOverlayMat);
            if (_iceColliderGO == null)
                _iceColliderGO = CreateIceColliderGO("IceCollider");
        }

        private GameObject CreateOverlayGO(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_winterOverlayRoot.transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = $"{name}_Mesh" };
            mf.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        private GameObject CreateIceColliderGO(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_winterOverlayRoot.transform, worldPositionStays: false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = new Mesh { name = $"{name}_Mesh" };
            mf.sharedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            var mc = go.AddComponent<MeshCollider>();
            mc.cookingOptions = MeshColliderCookingOptions.None;
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = false;
            mc.enabled = false;
            return go;
        }

        private void BuildOverlayMeshInto(GameObject overlayGO, bool includeWaterColumns)
        {
            if (overlayGO == null || config == null)
                return;

            var mf = overlayGO.GetComponent<MeshFilter>();
            if (mf == null)
                return;
            if (mf.sharedMesh == null)
                mf.sharedMesh = new Mesh { name = overlayGO.name + "_Mesh" };
            var mesh = mf.sharedMesh;
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            float inset = Mathf.Clamp(winterOverlayInset, 0.001f, 0.49f);
            float h = Mathf.Max(0.001f, winterOverlayHeightOffset);
            float[,] landCorners = null;
            if (!includeWaterColumns)
            {
                float[,] dummyWaterMask;
                BuildLowPolyCornerMaps(out landCorners, out dummyWaterMask);
            }

            var verts = new List<Vector3>(config.worldWidth * config.worldDepth * 4);
            var tris = new List<int>(config.worldWidth * config.worldDepth * 6);
            var uvs = new List<Vector2>(config.worldWidth * config.worldDepth * 4);

            for (int z = 0; z < config.worldDepth; z++)
            for (int x = 0; x < config.worldWidth; x++)
            {
                if (!TryGetColumnTopBlock(x, z, out int yTop, out VoxelBlockType topType))
                    continue;

                bool isWater = topType == VoxelBlockType.Water;
                if (includeWaterColumns != isWater)
                    continue;

                float x0 = x + inset;
                float x1 = x + 1f - inset;
                float z0 = z + inset;
                float z1 = z + 1f - inset;
                float y00;
                float y10;
                float y11;
                float y01;
                if (!includeWaterColumns && landCorners != null)
                {
                    // Conform snow to smoothed terrain surface so it follows curved landscape.
                    y00 = landCorners[x, z] + h;
                    y10 = landCorners[x + 1, z] + h;
                    y11 = landCorners[x + 1, z + 1] + h;
                    y01 = landCorners[x, z + 1] + h;
                }
                else
                {
                    float y = yTop + 1f + h;
                    y00 = y10 = y11 = y01 = y;
                }

                int v = verts.Count;
                verts.Add(new Vector3(x0, y00, z0));
                verts.Add(new Vector3(x1, y10, z0));
                verts.Add(new Vector3(x1, y11, z1));
                verts.Add(new Vector3(x0, y01, z1));

                uvs.Add(new Vector2(0f, 0f));
                uvs.Add(new Vector2(1f, 0f));
                uvs.Add(new Vector2(1f, 1f));
                uvs.Add(new Vector2(0f, 1f));

                tris.Add(v + 0);
                tris.Add(v + 2);
                tris.Add(v + 1);
                tris.Add(v + 0);
                tris.Add(v + 3);
                tris.Add(v + 2);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private void BuildIceColliderMesh()
        {
            if (_iceColliderGO == null || config == null)
                return;

            var mf = _iceColliderGO.GetComponent<MeshFilter>();
            var mc = _iceColliderGO.GetComponent<MeshCollider>();
            if (mf == null || mc == null)
                return;

            if (mf.sharedMesh == null)
                mf.sharedMesh = new Mesh { name = "IceCollider_Mesh" };
            var mesh = mf.sharedMesh;
            mesh.Clear();
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            float inset = Mathf.Clamp(winterOverlayInset, 0.001f, 0.49f);
            float h = Mathf.Max(0.001f, winterOverlayHeightOffset) + 0.005f;

            var verts = new List<Vector3>(config.worldWidth * config.worldDepth * 4);
            var tris = new List<int>(config.worldWidth * config.worldDepth * 12);

            for (int z = 0; z < config.worldDepth; z++)
            for (int x = 0; x < config.worldWidth; x++)
            {
                if (!TryGetColumnTopBlock(x, z, out int yTop, out VoxelBlockType topType))
                    continue;
                if (topType != VoxelBlockType.Water)
                    continue;

                float y = yTop + 1f + h;
                float x0 = x + inset;
                float x1 = x + 1f - inset;
                float z0 = z + inset;
                float z1 = z + 1f - inset;

                int v = verts.Count;
                verts.Add(new Vector3(x0, y, z0));
                verts.Add(new Vector3(x1, y, z0));
                verts.Add(new Vector3(x1, y, z1));
                verts.Add(new Vector3(x0, y, z1));

                // Top face
                tris.Add(v + 0); tris.Add(v + 2); tris.Add(v + 1);
                tris.Add(v + 0); tris.Add(v + 3); tris.Add(v + 2);
                // Bottom face for robust CC collision from either side
                tris.Add(v + 0); tris.Add(v + 1); tris.Add(v + 2);
                tris.Add(v + 0); tris.Add(v + 2); tris.Add(v + 3);
            }

            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mc.sharedMesh = null;
            mc.sharedMesh = mesh;
        }

        private bool TryGetColumnTopBlock(int x, int z, out int yTop, out VoxelBlockType topType)
        {
            yTop = 0;
            topType = VoxelBlockType.Air;
            if (config == null)
                return false;

            for (int y = config.worldHeight - 1; y >= 0; y--)
            {
                var t = GetBlock(x, y, z);
                if (t == VoxelBlockType.Air)
                    continue;

                yTop = y;
                topType = t;
                return true;
            }

            return false;
        }

        private void UpdateWinterVisualState(bool force = false)
        {
            if (!enableWinterSnowAndIce)
            {
                SetWinterVisualActive(false);
                return;
            }

            bool winterActive = IsWinterActive();
            if (!force && winterActive == _winterVisualsActive)
                return;

            _winterVisualsActive = winterActive;
            SetWinterVisualActive(winterActive);
        }

        private void SetWinterVisualActive(bool active)
        {
            if (_winterOverlayRoot == null && active)
                EnsureWinterOverlayObjects();
            if (_winterOverlayRoot != null && _winterOverlayRoot.activeSelf != active)
                _winterOverlayRoot.SetActive(active);
            if (_iceColliderGO != null)
            {
                var mc = _iceColliderGO.GetComponent<MeshCollider>();
                if (mc != null) mc.enabled = active;
            }
        }

        private bool IsWinterActive()
        {
            if (Time.unscaledTime >= _nextSeasonLookupAt)
            {
                if (_seasonSkybox == null || !_seasonSkybox.isActiveAndEnabled)
                    _seasonSkybox = FindFirstObjectByType<SeasonSkyboxController>();
                if (_weatherSystem == null || !_weatherSystem.isActiveAndEnabled)
                    _weatherSystem = FindFirstObjectByType<WeatherSystem>();
                if (_runtime == null || !_runtime.isActiveAndEnabled)
                    _runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
                _nextSeasonLookupAt = Time.unscaledTime + 1f;
            }

            if (_seasonSkybox != null && _seasonSkybox.GetCurrentSeason() == SeasonSkyboxController.Season.Winter)
                return true;
            if (_weatherSystem != null && _weatherSystem.CurrentPhase == WeatherPhase.Snow)
                return true;
            if (_runtime != null && _runtime.CurrentSeason == SCoL.Season.Winter)
                return true;

            return false;
        }

        private float Noise(float x, float z)
        {
            float nx = (x + _noiseOffset.x) * config.noiseScale;
            float nz = (z + _noiseOffset.y) * config.noiseScale;
            return Mathf.PerlinNoise(nx, nz);
        }

        private int HeightAt(int x, int z)
        {
            float n = Noise(x, z);
            // light FBM-ish: add a smaller octave
            n = 0.75f * n + 0.25f * Mathf.PerlinNoise((x + _noiseOffset.x) * config.noiseScale * 2.2f, (z + _noiseOffset.y) * config.noiseScale * 2.2f);
            float hRaw = config.baseHeight + ((n - 0.5f) * 2f * config.heightAmplitude);
            if (enableFlatBuildPads)
                hRaw = ApplyFlatBuildPads(x, z, hRaw);
            int h = Mathf.RoundToInt(hRaw);
            if (IsInsideEdgeLandBuffer(x, z))
            {
                int minH = config.seaLevel + Mathf.Max(0, edgeMinHeightAboveSea);
                h = Mathf.Max(h, minH);
            }
            return Mathf.Clamp(h, 1, config.worldHeight - 2);
        }

        private bool IsInsideEdgeLandBuffer(int x, int z)
        {
            int b = Mathf.Max(0, edgeLandBufferBlocks);
            if (b <= 0)
                return false;
            if (x < b || z < b)
                return true;
            if (x >= config.worldWidth - b || z >= config.worldDepth - b)
                return true;
            return false;
        }

        private float ApplyFlatBuildPads(int x, int z, float height)
        {
            if (_flatBuildPads == null || _flatBuildPads.Count == 0)
                return height;

            Vector2 p = new Vector2(x + 0.5f, z + 0.5f);
            float outH = height;
            float blendStrength = Mathf.Clamp01(flatBuildPadBlend);

            for (int i = 0; i < _flatBuildPads.Count; i++)
            {
                var pad = _flatBuildPads[i];
                float r = Mathf.Max(1f, pad.radius);
                float d = Vector2.Distance(p, pad.centerXZ);
                if (d > r)
                    continue;

                float t = 1f - Mathf.Clamp01(d / r);
                // Strong flatten in center, soft blend near edge.
                float w = t * t * blendStrength;
                outH = Mathf.Lerp(outH, pad.targetHeight, w);
            }

            return outH;
        }

        private void InitFlatBuildPads()
        {
            _flatBuildPads.Clear();
            if (!enableFlatBuildPads || config == null)
                return;

            int count = Mathf.Max(0, flatBuildPadCount);
            if (count <= 0)
                return;

            float r = Mathf.Max(2f, flatBuildPadRadius);
            float margin = Mathf.Max(r + 2f, edgeLandBufferBlocks + 2f);
            float minX = margin;
            float maxX = Mathf.Max(minX + 1f, config.worldWidth - margin);
            float minZ = margin;
            float maxZ = Mathf.Max(minZ + 1f, config.worldDepth - margin);
            int minH = Mathf.Clamp(config.seaLevel + Mathf.Max(2, edgeMinHeightAboveSea + 1), 1, config.worldHeight - 2);

            for (int i = 0; i < count; i++)
            {
                float cx = Mathf.Lerp(minX, maxX, (i + 1f) / (count + 1f));
                float cz = Mathf.Lerp(minZ, maxZ, Mathf.Repeat((i * 0.37f) + 0.23f, 1f));
                // small deterministic jitter from seeded RNG
                cx += (float)(_rng.NextDouble() * 2.0 - 1.0) * Mathf.Min(6f, r * 0.6f);
                cz += (float)(_rng.NextDouble() * 2.0 - 1.0) * Mathf.Min(6f, r * 0.6f);
                cx = Mathf.Clamp(cx, minX, maxX);
                cz = Mathf.Clamp(cz, minZ, maxZ);

                int target = Mathf.Clamp(config.baseHeight + _rng.Next(-1, 2), minH, config.worldHeight - 2);
                _flatBuildPads.Add(new FlatBuildPad
                {
                    centerXZ = new Vector2(cx, cz),
                    radius = r,
                    targetHeight = target
                });
            }
        }

        private void FillChunkTerrain(VoxelChunk chunk)
        {
            int cs = config.chunkSize;
            int baseX = chunk.Coord.x * cs;
            int baseZ = chunk.Coord.y * cs;

            for (int lz = 0; lz < cs; lz++)
            for (int lx = 0; lx < cs; lx++)
            {
                int x = baseX + lx;
                int z = baseZ + lz;
                if (x < 0 || z < 0 || x >= config.worldWidth || z >= config.worldDepth)
                    continue;

                int h = HeightAt(x, z);

                // base fill
                for (int y = 0; y <= h; y++)
                {
                    VoxelBlockType t;
                    if (y == h) t = VoxelBlockType.Grass;
                    else if (y >= h - 3) t = VoxelBlockType.Dirt;
                    else t = VoxelBlockType.Stone;

                    chunk.Set(lx, y, lz, t);
                }

                // water
                if (h < config.seaLevel)
                {
                    for (int y = h + 1; y <= config.seaLevel; y++)
                        chunk.Set(lx, y, lz, VoxelBlockType.Water);
                }
            }
        }

        private void ApplyShorelineSandRings()
        {
            if (!enableShorelineSand || config == null)
                return;

            int minR = Mathf.Max(0, shorelineSandMinRings);
            int maxR = Mathf.Max(minR, shorelineSandMaxRings);
            if (maxR <= 0)
                return;

            int w = config.worldWidth;
            int d = config.worldDepth;
            var waterColumns = new bool[w, d];

            for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                waterColumns[x, z] = IsWaterColumn(x, z);
            }

            for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                if (waterColumns[x, z])
                    continue;

                int surfaceY = GetSurfaceY(x, z);
                if (surfaceY < 0)
                    continue;

                var top = GetBlock(x, surfaceY, z);
                if (top == VoxelBlockType.Air || top == VoxelBlockType.Water)
                    continue;

                int rings = ResolveShorelineRingCount(x, z, minR, maxR);
                if (rings <= 0)
                    continue;

                int nearestWater = DistanceToNearestWaterChebyshev(x, z, rings, waterColumns);
                if (nearestWater <= 0 || nearestWater > rings)
                    continue;

                // Stone channel currently maps to sand look in this project.
                SetBlock(x, surfaceY, z, VoxelBlockType.Stone);
            }
        }

        private bool IsWaterColumn(int x, int z)
        {
            if (x < 0 || z < 0 || x >= config.worldWidth || z >= config.worldDepth)
                return false;

            int sea = Mathf.Clamp(config.seaLevel, 0, config.worldHeight - 1);
            if (GetBlock(x, sea, z) == VoxelBlockType.Water)
                return true;

            int y0 = Mathf.Max(0, sea - 2);
            int y1 = Mathf.Min(config.worldHeight - 1, sea + 1);
            for (int y = y0; y <= y1; y++)
            {
                if (GetBlock(x, y, z) == VoxelBlockType.Water)
                    return true;
            }
            return false;
        }

        private int ResolveShorelineRingCount(int x, int z, int minR, int maxR)
        {
            if (maxR <= minR)
                return minR;

            float nx = (x + _noiseOffset.x + 173.7f) * shorelineSandNoiseScale;
            float nz = (z + _noiseOffset.y - 91.3f) * shorelineSandNoiseScale;
            float n = Mathf.PerlinNoise(nx, nz);

            int span = maxR - minR + 1;
            int ring = minR + Mathf.FloorToInt(n * span);
            return Mathf.Clamp(ring, minR, maxR);
        }

        private static int DistanceToNearestWaterChebyshev(int x, int z, int maxRadius, bool[,] waterColumns)
        {
            int width = waterColumns.GetLength(0);
            int depth = waterColumns.GetLength(1);

            for (int r = 1; r <= maxRadius; r++)
            {
                int xMin = Mathf.Max(0, x - r);
                int xMax = Mathf.Min(width - 1, x + r);
                int zMin = Mathf.Max(0, z - r);
                int zMax = Mathf.Min(depth - 1, z + r);

                for (int nz = zMin; nz <= zMax; nz++)
                for (int nx = xMin; nx <= xMax; nx++)
                {
                    int dx = Mathf.Abs(nx - x);
                    int dz = Mathf.Abs(nz - z);
                    if (Mathf.Max(dx, dz) != r)
                        continue;

                    if (waterColumns[nx, nz])
                        return r;
                }
            }
            return int.MaxValue;
        }

        private void BuildChunkGO(Vector2Int cc)
        {
            var go = new GameObject($"Chunk_{cc.x}_{cc.y}");
            go.transform.SetParent(transform, worldPositionStays: true);
            go.transform.position = OriginWorld;

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            // VR perf defaults: no shadows for voxel terrain.
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var mesh = VoxelMesher.BuildChunkMesh(this, cc, includeWater: true);
            mf.sharedMesh = mesh;

            // Determine which block types were used as submeshes, in same order as mesher (sorted).
            // We reconstruct by scanning triangles per submesh isn't possible; so we mirror mesher sorting:
            // Air is never emitted, so we just assign materials for known types that exist in mesh.
            // For now: Grass, Dirt, Stone, Water.
            var mats = new List<Material>();
            var order = new List<VoxelBlockType>();

            // Submesh count matches types present, but we don't know which ones. We approximate by re-running a cheap scan.
            var used = GetUsedTypesInChunk(cc);
            used.Sort();
            foreach (var t in used)
            {
                order.Add(t);
                mats.Add(MaterialFor(t));
            }

            _chunkSubmeshOrder[cc] = order.ToArray();
            mr.sharedMaterials = mats.ToArray();
            ApplyWaterSurfaceVisibilityToChunk(cc, mr);

            if (config.generateColliders)
            {
                var mc = go.AddComponent<MeshCollider>();
                // Build collider from solid blocks only, so player can enter water instead of walking on it.
                var colliderMesh = VoxelMesher.BuildChunkMesh(this, cc, includeWater: false);
                mc.sharedMesh = colliderMesh;
                _chunkColliders[cc] = mc;
            }

            _chunkGOs[cc] = go;

            // Grass props (decorations) are attached to the chunk GO so streaming toggles them.
            if (enableGrassProps && grassPropMesh != null && grassPropMaterial != null)
            {
                var gp = go.AddComponent<GrassPropChunk>();
                gp.world = this;
                gp.chunkCoord = cc;
                gp.grassMesh = grassPropMesh;
                gp.grassMeshVariants = null;
                gp.grassMaterial = grassPropMaterial;
                gp.density = grassPropDensity;
                gp.maxPerChunk = grassPropsMaxPerChunk;
                gp.strictGridPlacement = true;
                gp.Rebuild(_seed);
                _chunkGrassProps[cc] = gp;
            }

            // Flora props (flowers/trees)
            if (enableFloraProps)
            {
                var fp = go.AddComponent<FloraPropChunk>();
                fp.world = this;
                fp.chunkCoord = cc;

                var props = new List<FloraPropChunk.Prop>();

                // Flowers
                if (flowerMaterial != null)
                {
                    if (daisyMesh != null)
                    {
                        props.Add(new FloraPropChunk.Prop
                        {
                            name = "Daisy",
                            mesh = daisyMesh,
                            material = flowerMaterial,
                            density = flowerDensity,
                            maxPerChunk = flowersMaxPerChunk,
                            onlyOnGrass = true,
                            requireAboveSeaLevel = true,
                            scaleRange = flowerScaleRange,
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 1
                        });
                    }
                    if (roseMesh != null)
                    {
                        props.Add(new FloraPropChunk.Prop
                        {
                            name = "Rose",
                            mesh = roseMesh,
                            material = flowerMaterial,
                            density = flowerDensity * 0.6f,
                            maxPerChunk = Mathf.Max(1, flowersMaxPerChunk / 2),
                            onlyOnGrass = true,
                            requireAboveSeaLevel = true,
                            scaleRange = new Vector2(flowerScaleRange.x * 0.9f, flowerScaleRange.y * 1.15f),
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 1
                        });
                    }
                }

                // Trees
                bool allowChunkTreeProps = !(enableFeaturedTrees && featuredTreeCount > 0);
                if (allowChunkTreeProps && treeMesh != null && treeMaterial != null)
                {
                    props.Add(new FloraPropChunk.Prop
                    {
                        name = "Tree",
                        mesh = treeMesh,
                        material = treeMaterial,
                        density = treeDensity,
                        maxPerChunk = treesMaxPerChunk,
                        onlyOnGrass = true,
                        requireAboveSeaLevel = true,
                        scaleRange = treeScaleRange,
                        avoidSteepSlopes = true,
                        maxNeighborDelta = 2
                    });
                }

                fp.props = props.ToArray();
                fp.Rebuild(_seed);
                _chunkFloraProps[cc] = fp;
            }
        }

        private void RebuildLowPolyVisuals()
        {
            if (!useLowPolyTerrainVisual || config == null)
            {
                ApplyLowPolyChunkVisibility(false);
                return;
            }

            if (_lowPolyVisualRoot != null)
                Destroy(_lowPolyVisualRoot);
            _lowPolyVisualRoot = new GameObject("LowPolyVisual");
            _lowPolyVisualRoot.transform.SetParent(transform, worldPositionStays: true);
            _lowPolyVisualRoot.transform.position = OriginWorld;

            var landGO = new GameObject("Land");
            landGO.transform.SetParent(_lowPolyVisualRoot.transform, worldPositionStays: false);
            var landMF = landGO.AddComponent<MeshFilter>();
            var landMR = landGO.AddComponent<MeshRenderer>();
            landMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            landMR.receiveShadows = true;
            landMR.sharedMaterial = lowPolyTerrainMaterial != null ? lowPolyTerrainMaterial : grassMat;

            var waterGO = new GameObject("Water");
            waterGO.transform.SetParent(_lowPolyVisualRoot.transform, worldPositionStays: false);
            var waterMF = waterGO.AddComponent<MeshFilter>();
            var waterMR = waterGO.AddComponent<MeshRenderer>();
            waterMR.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterMR.receiveShadows = false;
            waterMR.sharedMaterial = lowPolyWaterMaterial != null ? lowPolyWaterMaterial : waterMat;
            _lowPolyWaterRenderer = waterMR;

            if (_lowPolyLandMesh != null)
                Destroy(_lowPolyLandMesh);
            if (_lowPolyWaterMesh != null)
                Destroy(_lowPolyWaterMesh);

            BuildLowPolyCornerMaps(out var cornerHeights, out var cornerWaterMask);
            _lowPolyCornerHeights = cornerHeights;

            _lowPolyLandMesh = BuildLowPolyLandMesh(cornerHeights);
            landMF.sharedMesh = _lowPolyLandMesh;

            if (useLowPolyTerrainCollider)
            {
                _lowPolyLandCollider = landGO.AddComponent<MeshCollider>();
                _lowPolyLandCollider.convex = false;
                _lowPolyLandCollider.sharedMesh = _lowPolyLandMesh;
            }

            _lowPolyWaterMesh = BuildLowPolyWaterMesh(cornerWaterMask);
            waterMF.sharedMesh = _lowPolyWaterMesh;

            ApplyLowPolyChunkVisibility(true);
            SetWaterSurfaceVisible(_waterSurfaceVisible);
        }

        private void ApplyLowPolyChunkVisibility(bool lowPolyActive)
        {
            foreach (var kv in _chunkGOs)
            {
                var go = kv.Value;
                if (go == null)
                    continue;

                var mr = go.GetComponent<MeshRenderer>();
                if (mr != null)
                    mr.enabled = !lowPolyActive;
            }

            bool keepChunkCollider = !(lowPolyActive && useLowPolyTerrainCollider);
            foreach (var kv in _chunkColliders)
            {
                var mc = kv.Value;
                if (mc != null)
                    mc.enabled = keepChunkCollider;
            }
        }

        private void BuildLowPolyCornerMaps(out float[,] cornerHeights, out float[,] cornerWaterMask)
        {
            int w = config.worldWidth;
            int d = config.worldDepth;
            int sea = Mathf.Clamp(config.seaLevel, 0, config.worldHeight - 1);

            cornerHeights = new float[w + 1, d + 1];
            cornerWaterMask = new float[w + 1, d + 1];

            for (int z = 0; z <= d; z++)
            for (int x = 0; x <= w; x++)
            {
                float hSum = 0f;
                float waterSum = 0f;
                int count = 0;

                for (int oz = -1; oz <= 0; oz++)
                for (int ox = -1; ox <= 0; ox++)
                {
                    int cx = x + ox;
                    int cz = z + oz;
                    if (cx < 0 || cz < 0 || cx >= w || cz >= d)
                        continue;

                    hSum += GetSurfaceY(cx, cz) + 1f;
                    waterSum += GetBlock(cx, sea, cz) == VoxelBlockType.Water ? 1f : 0f;
                    count++;
                }

                if (count <= 0)
                {
                    cornerHeights[x, z] = sea + 1f;
                    cornerWaterMask[x, z] = 0f;
                }
                else
                {
                    cornerHeights[x, z] = (hSum / count) + lowPolyLandYOffset;
                    cornerWaterMask[x, z] = waterSum / count;
                }
            }

            SmoothCornerHeights(cornerHeights);
            SmoothWaterMask(cornerWaterMask);
            ExpandWaterMask(cornerWaterMask);
        }

        private void SmoothCornerHeights(float[,] heights)
        {
            int passes = Mathf.Clamp(lowPolySmoothingPasses, 0, 8);
            if (passes <= 0)
                return;
            SmoothScalarField(heights, passes, Mathf.Clamp01(lowPolySmoothingStrength), Mathf.Clamp01(lowPolyDiagonalSmoothingWeight));
        }

        private void SmoothWaterMask(float[,] mask)
        {
            int passes = Mathf.Clamp(lowPolyWaterMaskSmoothingPasses, 0, 8);
            if (passes <= 0)
                return;
            SmoothScalarField(mask, passes, Mathf.Clamp01(lowPolyWaterMaskSmoothingStrength), 0.5f);
        }

        private void ExpandWaterMask(float[,] mask)
        {
            int passes = Mathf.Clamp(lowPolyWaterMaskExpandPasses, 0, 6);
            if (passes <= 0)
                return;

            float strength = Mathf.Clamp01(lowPolyWaterMaskExpandStrength);
            if (strength <= 0f)
                return;

            int w = mask.GetLength(0);
            int d = mask.GetLength(1);
            var tmp = new float[w, d];

            for (int p = 0; p < passes; p++)
            {
                for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    float center = mask[x, z];
                    float maxN = center;
                    maxN = Mathf.Max(maxN, Sample(mask, x - 1, z));
                    maxN = Mathf.Max(maxN, Sample(mask, x + 1, z));
                    maxN = Mathf.Max(maxN, Sample(mask, x, z - 1));
                    maxN = Mathf.Max(maxN, Sample(mask, x, z + 1));
                    maxN = Mathf.Max(maxN, Sample(mask, x - 1, z - 1));
                    maxN = Mathf.Max(maxN, Sample(mask, x + 1, z - 1));
                    maxN = Mathf.Max(maxN, Sample(mask, x - 1, z + 1));
                    maxN = Mathf.Max(maxN, Sample(mask, x + 1, z + 1));

                    tmp[x, z] = Mathf.Lerp(center, maxN, strength);
                }

                for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                    mask[x, z] = tmp[x, z];
            }
        }

        private static float Sample(float[,] f, int x, int z)
        {
            int w = f.GetLength(0);
            int d = f.GetLength(1);
            if (x < 0 || z < 0 || x >= w || z >= d)
                return 0f;
            return f[x, z];
        }

        private static void SmoothScalarField(float[,] field, int passes, float strength, float diagWeight)
        {
            int w = field.GetLength(0);
            int d = field.GetLength(1);
            var tmp = new float[w, d];

            for (int p = 0; p < passes; p++)
            {
                for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                {
                    float center = field[x, z];
                    float sum = 0f;
                    float weight = 0f;

                    AddNeighbor(field, x - 1, z, 1f, ref sum, ref weight);
                    AddNeighbor(field, x + 1, z, 1f, ref sum, ref weight);
                    AddNeighbor(field, x, z - 1, 1f, ref sum, ref weight);
                    AddNeighbor(field, x, z + 1, 1f, ref sum, ref weight);
                    AddNeighbor(field, x - 1, z - 1, diagWeight, ref sum, ref weight);
                    AddNeighbor(field, x + 1, z - 1, diagWeight, ref sum, ref weight);
                    AddNeighbor(field, x - 1, z + 1, diagWeight, ref sum, ref weight);
                    AddNeighbor(field, x + 1, z + 1, diagWeight, ref sum, ref weight);

                    float avg = weight > 0.0001f ? (sum / weight) : center;
                    tmp[x, z] = Mathf.Lerp(center, avg, strength);
                }

                for (int z = 0; z < d; z++)
                for (int x = 0; x < w; x++)
                    field[x, z] = tmp[x, z];
            }
        }

        private static void AddNeighbor(float[,] heights, int x, int z, float w, ref float sum, ref float weight)
        {
            if (w <= 0f)
                return;
            int sx = heights.GetLength(0);
            int sz = heights.GetLength(1);
            if (x < 0 || z < 0 || x >= sx || z >= sz)
                return;
            sum += heights[x, z] * w;
            weight += w;
        }

        private Mesh BuildLowPolyLandMesh(float[,] cornerHeights)
        {
            int w = config.worldWidth;
            int d = config.worldDepth;
            int sub = Mathf.Clamp(lowPolyTerrainSubdivisions, 1, 3);
            float uvScale = Mathf.Max(0.01f, lowPolyUVScale);

            var verts = new List<Vector3>(w * d * sub * sub * 4);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>(w * d * sub * sub * 6);

            for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                for (int sz = 0; sz < sub; sz++)
                for (int sx = 0; sx < sub; sx++)
                {
                    float u0 = sx / (float)sub;
                    float u1 = (sx + 1) / (float)sub;
                    float v0 = sz / (float)sub;
                    float v1 = (sz + 1) / (float)sub;

                    Vector3 p00 = EvalLandPoint(cornerHeights, x, z, u0, v0);
                    Vector3 p10 = EvalLandPoint(cornerHeights, x, z, u1, v0);
                    Vector3 p01 = EvalLandPoint(cornerHeights, x, z, u0, v1);
                    Vector3 p11 = EvalLandPoint(cornerHeights, x, z, u1, v1);

                    Vector2 uv00 = new Vector2((x + u0) * uvScale, (z + v0) * uvScale);
                    Vector2 uv10 = new Vector2((x + u1) * uvScale, (z + v0) * uvScale);
                    Vector2 uv01 = new Vector2((x + u0) * uvScale, (z + v1) * uvScale);
                    Vector2 uv11 = new Vector2((x + u1) * uvScale, (z + v1) * uvScale);

                    AddFlatTri(verts, tris, uvs, p00, p10, p11, uv00, uv10, uv11, lowPolyDoubleSided);
                    AddFlatTri(verts, tris, uvs, p00, p11, p01, uv00, uv11, uv01, lowPolyDoubleSided);
                }
            }

            var mesh = new Mesh { name = "LowPoly_Land" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Vector3 EvalLandPoint(float[,] cornerHeights, int cellX, int cellZ, float u, float v)
        {
            float h00 = cornerHeights[cellX, cellZ];
            float h10 = cornerHeights[cellX + 1, cellZ];
            float h01 = cornerHeights[cellX, cellZ + 1];
            float h11 = cornerHeights[cellX + 1, cellZ + 1];

            float hx0 = Mathf.Lerp(h00, h10, u);
            float hx1 = Mathf.Lerp(h01, h11, u);
            float h = Mathf.Lerp(hx0, hx1, v);
            return new Vector3(cellX + u, h, cellZ + v);
        }

        private Mesh BuildLowPolyWaterMesh(float[,] cornerWaterMask)
        {
            int w = config.worldWidth;
            int d = config.worldDepth;
            int sub = Mathf.Clamp(lowPolyWaterSubdivisions, 1, 4);
            float threshold = Mathf.Clamp(lowPolyWaterMaskThreshold, 0.01f, 0.99f);
            float uvScale = Mathf.Max(0.01f, lowPolyUVScale);
            float seaY = Mathf.Clamp(config.seaLevel + 1f + lowPolyWaterYOffset - Mathf.Max(0f, lowPolyWaterEmbedDepth), 0f, config.worldHeight + 8f);
            float overhangPerSubQuad = Mathf.Max(0f, lowPolyWaterHorizontalOverhang) / sub;

            var verts = new List<Vector3>(w * d * sub * sub * 4);
            var uvs = new List<Vector2>(verts.Capacity);
            var tris = new List<int>(w * d * sub * sub * 6);

            for (int z = 0; z < d; z++)
            for (int x = 0; x < w; x++)
            {
                for (int sz = 0; sz < sub; sz++)
                for (int sx = 0; sx < sub; sx++)
                {
                    float u0 = sx / (float)sub;
                    float u1 = (sx + 1) / (float)sub;
                    float v0 = sz / (float)sub;
                    float v1 = (sz + 1) / (float)sub;

                    float m00 = EvalMask(cornerWaterMask, x, z, u0, v0);
                    float m10 = EvalMask(cornerWaterMask, x, z, u1, v0);
                    float m01 = EvalMask(cornerWaterMask, x, z, u0, v1);
                    float m11 = EvalMask(cornerWaterMask, x, z, u1, v1);
                    float mAvg = (m00 + m10 + m01 + m11) * 0.25f;
                    if (mAvg < threshold)
                        continue;

                    float x0 = (x + u0) - overhangPerSubQuad;
                    float x1 = (x + u1) + overhangPerSubQuad;
                    float z0 = (z + v0) - overhangPerSubQuad;
                    float z1 = (z + v1) + overhangPerSubQuad;

                    Vector3 p00 = new Vector3(x0, seaY, z0);
                    Vector3 p10 = new Vector3(x1, seaY, z0);
                    Vector3 p01 = new Vector3(x0, seaY, z1);
                    Vector3 p11 = new Vector3(x1, seaY, z1);

                    Vector2 uv00 = new Vector2((x + u0) * uvScale, (z + v0) * uvScale);
                    Vector2 uv10 = new Vector2((x + u1) * uvScale, (z + v0) * uvScale);
                    Vector2 uv01 = new Vector2((x + u0) * uvScale, (z + v1) * uvScale);
                    Vector2 uv11 = new Vector2((x + u1) * uvScale, (z + v1) * uvScale);

                    AddFlatTri(verts, tris, uvs, p00, p10, p11, uv00, uv10, uv11, lowPolyWaterDoubleSided);
                    AddFlatTri(verts, tris, uvs, p00, p11, p01, uv00, uv11, uv01, lowPolyWaterDoubleSided);
                }
            }

            var mesh = new Mesh { name = "LowPoly_Water" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float EvalMask(float[,] cornerMask, int cellX, int cellZ, float u, float v)
        {
            float m00 = cornerMask[cellX, cellZ];
            float m10 = cornerMask[cellX + 1, cellZ];
            float m01 = cornerMask[cellX, cellZ + 1];
            float m11 = cornerMask[cellX + 1, cellZ + 1];
            float mx0 = Mathf.Lerp(m00, m10, u);
            float mx1 = Mathf.Lerp(m01, m11, u);
            return Mathf.Lerp(mx0, mx1, v);
        }

        private static void AddFlatTri(
            List<Vector3> verts, List<int> tris, List<Vector2> uvs,
            Vector3 a, Vector3 b, Vector3 c,
            Vector2 uva, Vector2 uvb, Vector2 uvc,
            bool doubleSided)
        {
            int i0 = verts.Count;
            verts.Add(a); verts.Add(b); verts.Add(c);
            uvs.Add(uva); uvs.Add(uvb); uvs.Add(uvc);
            tris.Add(i0 + 0); tris.Add(i0 + 1); tris.Add(i0 + 2);

            if (!doubleSided)
                return;

            int j0 = verts.Count;
            verts.Add(a); verts.Add(c); verts.Add(b);
            uvs.Add(uva); uvs.Add(uvc); uvs.Add(uvb);
            tris.Add(j0 + 0); tris.Add(j0 + 1); tris.Add(j0 + 2);
        }

        private void BuildFeaturedTrees()
        {
            if (!enableFeaturedTrees || config == null || treeMesh == null || treeMaterial == null)
                return;

            if (_featuredTreesRoot != null)
                Destroy(_featuredTreesRoot);

            _featuredTreesRoot = new GameObject("FeaturedTrees");
            _featuredTreesRoot.transform.SetParent(transform, worldPositionStays: true);
            _featuredTreesRoot.transform.position = OriginWorld;

            int target = Mathf.Max(0, featuredTreeCount);
            if (target <= 0)
                return;

            float aspect = config.worldDepth > 0 ? (config.worldWidth / (float)config.worldDepth) : 1f;
            int cols = Mathf.Max(1, Mathf.RoundToInt(Mathf.Sqrt(target * Mathf.Max(0.25f, aspect))));
            int rows = Mathf.Max(1, Mathf.CeilToInt(target / (float)cols));
            float stepX = config.worldWidth / (float)cols;
            float stepZ = config.worldDepth / (float)rows;

            var used = new HashSet<int>(target * 2);
            var prng = new System.Random(unchecked(_seed * 397) ^ 0x34A7F1);
            int placed = 0;

            for (int rz = 0; rz < rows && placed < target; rz++)
            {
                for (int cx = 0; cx < cols && placed < target; cx++)
                {
                    int sx = Mathf.Clamp(Mathf.FloorToInt((cx + 0.5f) * stepX), 0, config.worldWidth - 1);
                    int sz = Mathf.Clamp(Mathf.FloorToInt((rz + 0.5f) * stepZ), 0, config.worldDepth - 1);

                    if (!TryFindFeaturedTreeColumn(sx, sz, out int px, out int pz))
                        continue;

                    int key = px + pz * config.worldWidth;
                    if (!used.Add(key))
                        continue;

                    PlaceFeaturedTree(placed, px, pz, prng);
                    placed++;
                }
            }

            int safety = 0;
            int maxAttempts = Mathf.Max(64, target * 40);
            while (placed < target && safety++ < maxAttempts)
            {
                int sx = prng.Next(0, Mathf.Max(1, config.worldWidth));
                int sz = prng.Next(0, Mathf.Max(1, config.worldDepth));
                if (!TryFindFeaturedTreeColumn(sx, sz, out int px, out int pz))
                    continue;

                int key = px + pz * config.worldWidth;
                if (!used.Add(key))
                    continue;

                PlaceFeaturedTree(placed, px, pz, prng);
                placed++;
            }

            if (placed < target)
            {
                Debug.LogWarning($"[VoxelWorld] Featured trees placed {placed}/{target}. Consider lowering constraints or search radius.", this);
            }
        }

        private bool TryFindFeaturedTreeColumn(int centerX, int centerZ, out int outX, out int outZ)
        {
            outX = centerX;
            outZ = centerZ;

            int maxR = Mathf.Max(0, featuredTreeSearchRadius);
            bool preferGrass = featuredTreesPreferGrass;

            for (int pass = 0; pass < (preferGrass ? 2 : 1); pass++)
            {
                bool requireGrass = preferGrass && pass == 0;

                for (int r = 0; r <= maxR; r++)
                {
                    for (int dz = -r; dz <= r; dz++)
                    {
                        for (int dx = -r; dx <= r; dx++)
                        {
                            if (r > 0 && Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r)
                                continue;

                            int x = centerX + dx;
                            int z = centerZ + dz;
                            if (!IsValidFeaturedTreeColumn(x, z, requireGrass))
                                continue;

                            outX = x;
                            outZ = z;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool IsValidFeaturedTreeColumn(int x, int z, bool requireGrass)
        {
            if (config == null)
                return false;
            if (x < 0 || z < 0 || x >= config.worldWidth || z >= config.worldDepth)
                return false;

            int y = GetSurfaceY(x, z);
            if (y <= config.seaLevel)
                return false;

            var t = GetBlock(x, y, z);
            if (t == VoxelBlockType.Air || t == VoxelBlockType.Water)
                return false;
            if (requireGrass && t != VoxelBlockType.Grass)
                return false;

            int above = y + 1;
            if (above < config.worldHeight && GetBlock(x, above, z) == VoxelBlockType.Water)
                return false;

            const int maxNeighborDelta = 3;
            if (Mathf.Abs(GetSurfaceY(x + 1, z) - y) > maxNeighborDelta) return false;
            if (Mathf.Abs(GetSurfaceY(x - 1, z) - y) > maxNeighborDelta) return false;
            if (Mathf.Abs(GetSurfaceY(x, z + 1) - y) > maxNeighborDelta) return false;
            if (Mathf.Abs(GetSurfaceY(x, z - 1) - y) > maxNeighborDelta) return false;

            return true;
        }

        private void PlaceFeaturedTree(int index, int x, int z, System.Random prng)
        {
            float surfaceY = OriginWorld.y + GetSurfaceY(x, z) + 1f;
            Vector3 sample = OriginWorld + new Vector3(x + 0.5f, surfaceY + 2f, z + 0.5f);
            if (TryGetTerrainSurfaceYAtWorld(sample, out float smoothY, includeWaterSurface: false))
                surfaceY = smoothY;

            var go = new GameObject($"FeaturedTree_{index:00}");
            go.transform.SetParent(_featuredTreesRoot.transform, worldPositionStays: true);

            float s = Mathf.Lerp(featuredTreeScaleRange.x, featuredTreeScaleRange.y, (float)prng.NextDouble());
            float yaw = (float)prng.NextDouble() * 360f;
            go.transform.position = new Vector3(
                OriginWorld.x + x + 0.5f,
                surfaceY + featuredTreeYOffset - Mathf.Max(0f, featuredTreeRootEmbedDepth),
                OriginWorld.z + z + 0.5f);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
            go.isStatic = true;

            var mf = go.AddComponent<MeshFilter>();
            mf.sharedMesh = treeMesh;

            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = treeMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
        }

        private void BuildWorldBoundary()
        {
            if (!enableWorldBoundary || config == null)
                return;

            if (_boundaryRoot != null)
                Destroy(_boundaryRoot);

            _boundaryRoot = new GameObject("WorldBoundary");
            _boundaryRoot.transform.SetParent(transform, worldPositionStays: true);
            _boundaryRoot.transform.position = OriginWorld;

            float t = Mathf.Max(0.5f, boundaryThickness);
            float h = Mathf.Max(2f, boundaryHeight);
            float yBottom = boundaryBottomOffset;
            float yCenter = yBottom + h * 0.5f;
            float w = Mathf.Max(1f, config.worldWidth);
            float d = Mathf.Max(1f, config.worldDepth);

            CreateBoundaryWall("West",  new Vector3(-t * 0.5f, yCenter, d * 0.5f), new Vector3(t, h, d + t * 2f));
            CreateBoundaryWall("East",  new Vector3(w + t * 0.5f, yCenter, d * 0.5f), new Vector3(t, h, d + t * 2f));
            CreateBoundaryWall("South", new Vector3(w * 0.5f, yCenter, -t * 0.5f), new Vector3(w + t * 2f, h, t));
            CreateBoundaryWall("North", new Vector3(w * 0.5f, yCenter, d + t * 0.5f), new Vector3(w + t * 2f, h, t));
        }

        private void CreateBoundaryWall(string name, Vector3 localCenter, Vector3 localSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_boundaryRoot.transform, worldPositionStays: false);
            go.transform.localPosition = localCenter;
            go.transform.localRotation = Quaternion.identity;

            var collider = go.AddComponent<BoxCollider>();
            collider.size = localSize;
            collider.isTrigger = false;

            if (!showBoundaryWalls)
                return;

            var mf = go.AddComponent<MeshFilter>();
            var mr = go.AddComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sharedMaterial = boundaryWallMaterial != null ? boundaryWallMaterial : stoneMat;

            var mesh = new Mesh { name = $"BoundaryWall_{name}" };
            BuildBoxMesh(mesh, localSize);
            mf.sharedMesh = mesh;
        }

        private static void BuildBoxMesh(Mesh mesh, Vector3 size)
        {
            var hx = size.x * 0.5f;
            var hy = size.y * 0.5f;
            var hz = size.z * 0.5f;

            var verts = new[]
            {
                new Vector3(-hx, -hy, -hz), new Vector3(hx, -hy, -hz), new Vector3(hx, hy, -hz), new Vector3(-hx, hy, -hz),
                new Vector3(-hx, -hy, hz),  new Vector3(hx, -hy, hz),  new Vector3(hx, hy, hz),  new Vector3(-hx, hy, hz)
            };

            var tris = new[]
            {
                0,2,1, 0,3,2, // back
                4,5,6, 4,6,7, // front
                0,1,5, 0,5,4, // bottom
                3,7,6, 3,6,2, // top
                1,2,6, 1,6,5, // right
                0,4,7, 0,7,3  // left
            };

            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
        }

        private List<VoxelBlockType> GetUsedTypesInChunk(Vector2Int cc)
        {
            // A tiny scan: for each block, if it would emit at least one face, mark it as used.
            // This keeps materials aligned with the mesher's sorted order.
            var used = new HashSet<VoxelBlockType>();
            int cs = config.chunkSize;
            int baseX = cc.x * cs;
            int baseZ = cc.y * cs;

            for (int lz = 0; lz < cs; lz++)
            for (int lx = 0; lx < cs; lx++)
            for (int y = 0; y < config.worldHeight; y++)
            {
                int wx = baseX + lx;
                int wz = baseZ + lz;
                var t = GetBlock(wx, y, wz);
                if (t == VoxelBlockType.Air) continue;

                bool visible =
                    WouldRenderFace(t, GetBlock(wx + 1, y, wz)) ||
                    WouldRenderFace(t, GetBlock(wx - 1, y, wz)) ||
                    WouldRenderFace(t, GetBlock(wx, y + 1, wz)) ||
                    WouldRenderFace(t, GetBlock(wx, y - 1, wz)) ||
                    WouldRenderFace(t, GetBlock(wx, y, wz + 1)) ||
                    WouldRenderFace(t, GetBlock(wx, y, wz - 1));

                if (visible)
                {
                    used.Add(t);
                }
            }

            var list = new List<VoxelBlockType>(used);
            return list;
        }

        private static bool WouldRenderFace(VoxelBlockType self, VoxelBlockType neighbor)
        {
            if (self == VoxelBlockType.Water)
                return neighbor != VoxelBlockType.Water;

            return neighbor == VoxelBlockType.Air;
        }

        private Material MaterialFor(VoxelBlockType t)
        {
            return t switch
            {
                VoxelBlockType.Grass => grassMat,
                VoxelBlockType.Dirt => dirtMat,
                VoxelBlockType.Stone => stoneMat,
                VoxelBlockType.Water => waterMat,
                _ => stoneMat
            };
        }

        public bool InBounds(int x, int y, int z)
        {
            return x >= 0 && z >= 0 && y >= 0 && x < config.worldWidth && z < config.worldDepth && y < config.worldHeight;
        }

        public VoxelBlockType GetBlock(int x, int y, int z)
        {
            if (!InBounds(x, y, z)) return VoxelBlockType.Air;

            int cs = config.chunkSize;
            int cx = x / cs;
            int cz = z / cs;
            int lx = x - cx * cs;
            int lz = z - cz * cs;

            var cc = new Vector2Int(cx, cz);
            if (!_chunks.TryGetValue(cc, out var chunk))
                return VoxelBlockType.Air;

            return chunk.Get(lx, y, lz);
        }

        public void SetBlock(int x, int y, int z, VoxelBlockType t)
        {
            if (!InBounds(x, y, z)) return;

            int cs = config.chunkSize;
            int cx = x / cs;
            int cz = z / cs;
            int lx = x - cx * cs;
            int lz = z - cz * cs;

            var cc = new Vector2Int(cx, cz);
            if (!_chunks.TryGetValue(cc, out var chunk))
                return;

            chunk.Set(lx, y, lz, t);
        }

        public int GetSurfaceY(int x, int z)
        {
            if (x < 0 || z < 0 || x >= config.worldWidth || z >= config.worldDepth)
                return 0;

            for (int y = config.worldHeight - 1; y >= 0; y--)
            {
                var t = GetBlock(x, y, z);
                if (t != VoxelBlockType.Air && t != VoxelBlockType.Water)
                    return y;
            }
            return 0;
        }

        public bool IsGrassSurface(int x, int z)
        {
            int y = GetSurfaceY(x, z);
            return GetBlock(x, y, z) == VoxelBlockType.Grass;
        }

        public bool TryWorldToColumn(Vector3 world, out int x, out int z)
        {
            var local = world - OriginWorld;
            x = Mathf.FloorToInt(local.x);
            z = Mathf.FloorToInt(local.z);
            return x >= 0 && z >= 0 && x < config.worldWidth && z < config.worldDepth;
        }

        public bool IsWinterSurfaceFrozen => enableWinterSnowAndIce && _winterVisualsActive;

        public bool IsWaterColumnAtWorld(Vector3 world)
        {
            if (config == null)
                return false;
            if (!TryWorldToColumn(world, out int x, out int z))
                return false;

            int sea = Mathf.Clamp(config.seaLevel, 0, config.worldHeight - 1);
            if (GetBlock(x, sea, z) == VoxelBlockType.Water)
                return true;

            int top = GetSurfaceY(x, z) + 1;
            if (top >= 0 && top < config.worldHeight && GetBlock(x, top, z) == VoxelBlockType.Water)
                return true;

            return false;
        }

        /// <summary>
        /// Returns world-space terrain surface Y at the given world position.
        /// Surface Y is the top face (blockY + 1), not the block index itself.
        /// </summary>
        public bool TryGetTerrainSurfaceYAtWorld(Vector3 worldPos, out float surfaceY, bool includeWaterSurface = false)
        {
            surfaceY = 0f;
            if (config == null)
                return false;
            if (!TryWorldToColumn(worldPos, out int x, out int z))
                return false;

            if (useLowPolyTerrainVisual &&
                _lowPolyCornerHeights != null &&
                TrySampleLowPolyHeightAtWorld(worldPos, out float lowPolyY))
            {
                surfaceY = lowPolyY;
                return true;
            }

            for (int y = config.worldHeight - 1; y >= 0; y--)
            {
                var t = GetBlock(x, y, z);
                if (t == VoxelBlockType.Air)
                    continue;
                if (!includeWaterSurface && t == VoxelBlockType.Water)
                    continue;

                surfaceY = OriginWorld.y + y + 1f;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Toggle visible water surface rendering while keeping gameplay voxels/colliders intact.
        /// </summary>
        public void SetWaterSurfaceVisible(bool visible)
        {
            _waterSurfaceVisible = visible;
            if (_lowPolyWaterRenderer != null)
                _lowPolyWaterRenderer.enabled = visible;
            foreach (var kv in _chunkGOs)
            {
                if (kv.Value == null)
                    continue;
                var mr = kv.Value.GetComponent<MeshRenderer>();
                if (mr == null)
                    continue;
                ApplyWaterSurfaceVisibilityToChunk(kv.Key, mr);
            }
        }

        private void ApplyWaterSurfaceVisibilityToChunk(Vector2Int cc, MeshRenderer mr)
        {
            if (mr == null)
                return;
            if (!_chunkSubmeshOrder.TryGetValue(cc, out var order) || order == null || order.Length == 0)
                return;

            var mats = mr.sharedMaterials;
            if (mats == null || mats.Length == 0)
                return;

            bool dirty = false;
            Material hiddenMat = null;
            for (int i = 0; i < mats.Length && i < order.Length; i++)
            {
                if (order[i] != VoxelBlockType.Water)
                    continue;

                var desired = _waterSurfaceVisible ? waterMat : (hiddenMat ??= EnsureHiddenWaterMaterial());
                if (mats[i] != desired)
                {
                    mats[i] = desired;
                    dirty = true;
                }
            }

            if (dirty)
                mr.sharedMaterials = mats;
        }

        private Material EnsureHiddenWaterMaterial()
        {
            if (_hiddenWaterMat != null)
                return _hiddenWaterMat;

            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Transparent");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null && waterMat != null) shader = waterMat.shader;
            if (shader == null) shader = Shader.Find("Standard");

            var m = new Material(shader) { name = "Voxel_Water_Hidden" };
            m.enableInstancing = true;

            Color clear = new Color(0f, 0f, 0f, 0f);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", clear);
            if (m.HasProperty("_Color")) m.SetColor("_Color", clear);
            if (m.HasProperty("_Surface")) m.SetFloat("_Surface", 1f); // URP Transparent
            if (m.HasProperty("_ZWrite")) m.SetFloat("_ZWrite", 0f);
            if (m.HasProperty("_SrcBlend")) m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (m.HasProperty("_DstBlend")) m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

            _hiddenWaterMat = m;
            return _hiddenWaterMat;
        }

        private bool TrySampleLowPolyHeightAtWorld(Vector3 worldPos, out float y)
        {
            y = 0f;
            if (_lowPolyCornerHeights == null || config == null)
                return false;

            Vector3 local = worldPos - OriginWorld;
            float x = local.x;
            float z = local.z;
            if (x < 0f || z < 0f || x > config.worldWidth || z > config.worldDepth)
                return false;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, config.worldWidth - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, config.worldDepth - 1);
            int x1 = Mathf.Min(x0 + 1, config.worldWidth);
            int z1 = Mathf.Min(z0 + 1, config.worldDepth);
            float tx = Mathf.Clamp01(x - x0);
            float tz = Mathf.Clamp01(z - z0);

            float h00 = _lowPolyCornerHeights[x0, z0];
            float h10 = _lowPolyCornerHeights[x1, z0];
            float h01 = _lowPolyCornerHeights[x0, z1];
            float h11 = _lowPolyCornerHeights[x1, z1];

            float hx0 = Mathf.Lerp(h00, h10, tx);
            float hx1 = Mathf.Lerp(h01, h11, tx);
            y = OriginWorld.y + Mathf.Lerp(hx0, hx1, tz);
            return true;
        }

        public Vector3 ColumnTopWorld(int x, int z)
        {
            int y = GetSurfaceY(x, z);
            return OriginWorld + new Vector3(x + 0.5f, y + 1.0f, z + 0.5f);
        }

        public void ForceEnableChunksAtWorld(Vector3 worldPos, int renderRadiusChunks = 1, int colliderRadiusChunks = 1)
        {
            int cs = config.chunkSize;
            Vector3 local = worldPos - OriginWorld;
            int cx = Mathf.FloorToInt(local.x / cs);
            int cz = Mathf.FloorToInt(local.z / cs);

            for (int dz = -renderRadiusChunks; dz <= renderRadiusChunks; dz++)
            for (int dx = -renderRadiusChunks; dx <= renderRadiusChunks; dx++)
            {
                var cc = new Vector2Int(cx + dx, cz + dz);
                if (_chunkGOs.TryGetValue(cc, out var go) && go != null)
                    go.SetActive(true);
            }

            for (int dz = -colliderRadiusChunks; dz <= colliderRadiusChunks; dz++)
            for (int dx = -colliderRadiusChunks; dx <= colliderRadiusChunks; dx++)
            {
                var cc = new Vector2Int(cx + dx, cz + dz);
                if (_chunkColliders.TryGetValue(cc, out var mc) && mc != null)
                    mc.enabled = true;
            }
        }

        private void StreamAroundCamera()
        {
            if (_chunkGOs.Count == 0) return;

            Camera frustumCam = Camera.main;
            var xrOrigin = FindFirstObjectByType<Unity.XR.CoreUtils.XROrigin>();
            bool xrActive = UnityEngine.XR.XRSettings.isDeviceActive;

            Vector3 focusPos;
            if (xrActive && xrOrigin != null)
            {
                // In headset play, stream around the rig root for stable chunk loading.
                focusPos = xrOrigin.transform.position;
            }
            else
            {
                // In desktop/laptop play, prefer the FPS controller transform over any XR rig camera.
                var fps = FindFirstObjectByType<SimpleFirstPersonController>();
                if (fps != null)
                {
                    var focusTf = fps.cameraPivot != null ? fps.cameraPivot : fps.transform;
                    focusPos = focusTf.position;
                    frustumCam = focusTf.GetComponent<Camera>();
                }
                else if (frustumCam != null && (xrOrigin == null || !frustumCam.transform.IsChildOf(xrOrigin.transform)))
                {
                    focusPos = frustumCam.transform.position;
                }
                else
                {
                    // Fallback: find any non-XR enabled camera.
                    Camera[] cams = FindObjectsByType<Camera>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    Camera nonXrCam = null;
                    for (int i = 0; i < cams.Length; i++)
                    {
                        var c = cams[i];
                        if (c == null || !c.isActiveAndEnabled) continue;
                        if (xrOrigin != null && c.transform.IsChildOf(xrOrigin.transform)) continue;
                        nonXrCam = c;
                        break;
                    }

                    if (nonXrCam != null)
                    {
                        frustumCam = nonXrCam;
                        focusPos = nonXrCam.transform.position;
                    }
                    else if (xrOrigin != null)
                    {
                        // Last fallback if no gameplay camera is available.
                        focusPos = xrOrigin.transform.position;
                    }
                    else
                    {
                        return;
                    }
                }
            }

            int cs = config.chunkSize;
            Vector3 local = focusPos - OriginWorld;
            int camChunkX = Mathf.FloorToInt(local.x / cs);
            int camChunkZ = Mathf.FloorToInt(local.z / cs);

            var planes = (config.frustumCullChunks && frustumCam != null) ? GeometryUtility.CalculateFrustumPlanes(frustumCam) : null;

            foreach (var kv in _chunkGOs)
            {
                var cc = kv.Key;
                var go = kv.Value;
                if (go == null) continue;

                int dx = Mathf.Abs(cc.x - camChunkX);
                int dz = Mathf.Abs(cc.y - camChunkZ);
                int dist = Mathf.Max(dx, dz); // Chebyshev distance in chunk grid

                bool withinRender = dist <= config.renderDistanceChunks;

                // Optional frustum check for far chunks
                if (withinRender && planes != null)
                {
                    // chunk bounds in world space
                    var center = OriginWorld + new Vector3((cc.x * cs) + cs * 0.5f, config.worldHeight * 0.5f, (cc.y * cs) + cs * 0.5f);
                    var size = new Vector3(cs, config.worldHeight, cs);
                    var b = new Bounds(center, size);
                    withinRender = GeometryUtility.TestPlanesAABB(planes, b);
                }

                if (go.activeSelf != withinRender)
                    go.SetActive(withinRender);

                // Props guardrails for stable framerate: keep decorative instancing close to camera.
                if (withinRender)
                {
                    if (_chunkGrassProps.TryGetValue(cc, out var gp) && gp != null)
                    {
                        bool withinGrassProps = dist <= Mathf.Max(0, grassPropsDistanceChunks);
                        if (gp.enabled != withinGrassProps) gp.enabled = withinGrassProps;
                    }
                    if (_chunkFloraProps.TryGetValue(cc, out var fp) && fp != null)
                    {
                        bool withinFloraProps = dist <= Mathf.Max(0, floraPropsDistanceChunks);
                        if (fp.enabled != withinFloraProps) fp.enabled = withinFloraProps;
                    }
                }

                if (!config.generateColliders) continue;

                bool withinCollider = dist <= config.colliderDistanceChunks;
                if (useLowPolyTerrainVisual && useLowPolyTerrainCollider)
                    withinCollider = false;
                if (_chunkColliders.TryGetValue(cc, out var mc) && mc != null)
                {
                    if (mc.enabled != withinCollider)
                        mc.enabled = withinCollider;
                }
            }
        }
    }
}
