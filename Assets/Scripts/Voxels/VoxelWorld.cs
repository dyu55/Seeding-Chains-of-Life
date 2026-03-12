using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using SCoL.Visualization;
using SCoL.Weather;
using UnityEngine.SceneManagement;
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
        private const string StylizedTerrainPreviewSceneName = "StylizedTerrainPreview";

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
        [Tooltip("Spawn a fixed set of randomly distributed trees across the map.")]
        public bool enableFeaturedTrees = true;
        [Min(0)] public int featuredTreeCount = 100;
        [Tooltip("Prefer grass surface for featured tree placement.")]
        public bool featuredTreesPreferGrass = true;
        [Min(0)] public int featuredTreeSearchRadius = 10;
        [Tooltip("Target world-space height range for imported featured tree variants.")]
        public Vector2 featuredTreeTargetHeightRange = new Vector2(7.5f, 9.0f);
        public Vector2 featuredTreeScaleRange = new Vector2(4.8f, 5.2f);
        [Min(-2f)] public float featuredTreeYOffset = 0f;
        [Range(0f, 1.5f)] public float featuredTreeRootEmbedDepth = 0.55f;
        [Header("Featured Tree Clusters")]
        [Range(0f, 1f)] public float featuredTreeClusterChance = 0.32f;
        [Min(2)] public int featuredTreeClusterSameMin = 4;
        [Min(2)] public int featuredTreeClusterSameMax = 5;
        [Min(0)] public int featuredTreeClusterAccentCount = 1;
        [Min(1)] public int featuredTreeClusterRadius = 5;
        [Min(0.5f)] public float featuredTreeMinSpacing = 2.6f;
        [Min(0)] public int featuredTreeWaterBufferRadius = 2;
        [Min(0.5f)] public float featuredTreeRockClearRadius = 2.2f;
        [Header("Pink Tree Grove")]
        public bool enablePinkTreeGrove = true;
        [Min(0)] public int pinkTreeCount = 26;
        [Range(0.10f, 0.50f)] public float pinkTreeGroveWidthRatio = 0.22f;
        [Range(0.18f, 0.70f)] public float pinkTreeGroveDepthRatio = 0.42f;
        [Min(2)] public int pinkTreeEdgeMargin = 5;
        [Min(1f)] public float pinkTreeMinSpacing = 4.8f;
        public Vector2 pinkTreeTargetHeightRange = new Vector2(7.0f, 8.5f);

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
        [Min(0.001f)] public float winterOverlayUvScale = 0.085f;
        [Min(0.02f)] public float winterSnowBuildSpeed = 0.30f;
        [Min(0.02f)] public float winterSnowMeltSpeed = 0.18f;
        [Range(0.1f, 1f)] public float winterIceFrozenThreshold = 0.82f;
        [Range(0f, 0.8f)] public float winterIceExtraOverhang = 0.18f;
        [Range(0f, 0.2f)] public float winterIceExtraEmbed = 0.0f;
        [Range(-0.25f, 0.1f)] public float winterIceThresholdBias = -0.08f;
        [Range(4, 8)] public int winterIceSubdivisions = 6;
        [Range(0, 6)] public int winterIceMaskExpandPasses = 2;
        [Range(0, 8)] public int winterIceMaskSmoothingPasses = 3;
        [Range(0f, 1f)] public float winterIceMaskSmoothingStrength = 0.82f;
        public Color snowOverlayColor = new Color(0.98f, 0.98f, 1.0f, 0.92f);
        public Color iceOverlayColor = new Color(0.78f, 0.86f, 0.93f, 0.88f);

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
        private Mesh _lowPolyLandColliderMesh;
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
        private Texture2D _snowOverlayTex;
        private Texture2D _iceOverlayTex;
        private SeasonSkyboxController _seasonSkybox;
        private WeatherSystem _weatherSystem;
        private SCoL.SCoLRuntime _runtime;
        private float _nextSeasonLookupAt;
        private float _winterVisualAmount;
        private bool _winterVisualsActive;
        private const int WinterSnowRenderQueue = 3006;
        private const int WinterIceRenderQueue = 3010;
        private const float FrozenLowPolySnowLift = 0.08f;
        private bool _useCubeNetGrassUV;
        private bool _useCubeNetDirtUV;
        private bool _useCubeNetStoneUV;
        private bool _waterSurfaceVisible = true;
        private Material _hiddenWaterMat;
        private Color _lowPolyTerrainBaseColor = new Color(0.50f, 0.63f, 0.37f, 1f);
        private readonly List<FlatBuildPad> _flatBuildPads = new();
        private Mesh[] _stylizedGrassMeshes;
        private Mesh[] _incomingGroundGrassMeshes;
        private Mesh[] _stylizedFlowerMeshes;
        private Mesh[] _stylizedBushMeshes;
        private Mesh[] _stylizedPlantMeshes;
        private Mesh[] _stylizedMushroomMeshes;
        private Mesh[] _stylizedRockMeshes;
        private Mesh[] _stylizedPebbleMeshes;
        private Mesh[] _stylizedPathRockMeshes;
        private Material _stylizedGrassMaterial;
        private Material[] _incomingGroundGrassMaterials;
        private Material _stylizedFlowerMaterial;
        private Material _stylizedLeafMaterial;
        private Material _stylizedMushroomMaterial;
        private Material _stylizedRockMaterial;
        private Material _stylizedPathRockMaterial;

        private struct FlatBuildPad
        {
            public Vector2 centerXZ;
            public float radius;
            public int targetHeight;
        }

        private struct TreeVariant
        {
            public GameObject prefab;
            public float sourceHeight;
            public Material materialOverride;
            public Texture2D textureOverride;
        }

        private float _streamT;
        private Texture2D _grassFaceAtlasRuntime;
        private TreeVariant[] _featuredTreeVariants;
        private TreeVariant[] _pinkTreeVariants;

        public Vector3 OriginWorld => useTransformAsOrigin ? transform.position : Vector3.zero;

        public void InitIfNeeded()
        {
            if (config == null)
            {
                config = Resources.Load<VoxelWorldConfig>(ResolveDefaultConfigResourcePath());
            }

            if (config == null)
            {
                Debug.LogWarning("VoxelWorld: missing config (VoxelWorldConfig). Using runtime defaults.");
                config = ScriptableObject.CreateInstance<VoxelWorldConfig>();
            }

            ApplySceneProfileOverrides();
            _seed = config.useFixedSeed ? config.seed : Environment.TickCount;
            _rng = new System.Random(_seed);
            _noiseOffset = new Vector2(_rng.Next(-100000, 100000), _rng.Next(-100000, 100000));
            InitFlatBuildPads();

            EnsureDefaultMaterials();
            EnforceWaterEdgeSmoothingDefaults();
            EnsureGrassPropAssets();
            GenerateAll();
        }

        private static bool IsStylizedTerrainPreviewScene()
        {
            var scene = SceneManager.GetActiveScene();
            return scene.IsValid() &&
                   string.Equals(scene.name, StylizedTerrainPreviewSceneName, StringComparison.Ordinal);
        }

        private static string ResolveDefaultConfigResourcePath()
        {
            return IsStylizedTerrainPreviewScene()
                ? "Voxels/VoxelWorldConfig_StylizedPreview"
                : "Voxels/VoxelWorldConfig_Default";
        }

        private void ApplySceneProfileOverrides()
        {
            if (!IsStylizedTerrainPreviewScene())
                return;

            useVoxBoxTerrainMaterials = false;
            useIncomingTerrainBlockTextures = false;
            useOriginalWaterMaterial = true;

            enableShorelineSand = false;
            enableFlatBuildPads = false;
            edgeLandBufferBlocks = Mathf.Max(edgeLandBufferBlocks, 8);
            edgeMinHeightAboveSea = Mathf.Max(edgeMinHeightAboveSea, 2);

            useLowPolyTerrainVisual = true;
            useLowPolyTerrainCollider = true;
            lowPolySmoothingPasses = Mathf.Max(lowPolySmoothingPasses, 5);
            lowPolySmoothingStrength = Mathf.Max(lowPolySmoothingStrength, 0.78f);
            lowPolyDiagonalSmoothingWeight = Mathf.Max(lowPolyDiagonalSmoothingWeight, 0.58f);
            lowPolyTerrainSubdivisions = Mathf.Max(lowPolyTerrainSubdivisions, 3);
            lowPolyWaterSubdivisions = Mathf.Max(lowPolyWaterSubdivisions, 4);
            lowPolyWaterHorizontalOverhang = Mathf.Max(lowPolyWaterHorizontalOverhang, 1.0f);
            lowPolyWaterEmbedDepth = Mathf.Max(lowPolyWaterEmbedDepth, 0.14f);
            lowPolyUVScale = Mathf.Min(lowPolyUVScale, 0.16f);

            enableGrassProps = true;
            enableFloraProps = true;
            grassPropDensity = Mathf.Max(grassPropDensity, 0.26f);
            grassPropsMaxPerChunk = Mathf.Max(grassPropsMaxPerChunk, 220);
            flowerDensity = Mathf.Max(flowerDensity, 0.028f);

            enableFeaturedTrees = true;
            featuredTreeCount = Mathf.Max(featuredTreeCount, 72);
            featuredTreeClusterChance = Mathf.Max(featuredTreeClusterChance, 0.42f);
            featuredTreeClusterRadius = Mathf.Max(featuredTreeClusterRadius, 6);
            featuredTreeMinSpacing = Mathf.Max(featuredTreeMinSpacing, 3.0f);
            featuredTreeWaterBufferRadius = Mathf.Max(featuredTreeWaterBufferRadius, 3);

            enablePinkTreeGrove = true;
            pinkTreeCount = Mathf.Max(pinkTreeCount, 34);
            pinkTreeGroveWidthRatio = Mathf.Max(pinkTreeGroveWidthRatio, 0.28f);
            pinkTreeGroveDepthRatio = Mathf.Max(pinkTreeGroveDepthRatio, 0.48f);
            pinkTreeMinSpacing = Mathf.Max(pinkTreeMinSpacing, 5.2f);
        }

        private void Awake()
        {
            InitIfNeeded();
        }

        private void Update()
        {
            if (config == null) return;

            UpdateWinterVisualState();

            _streamT += Time.unscaledDeltaTime;
            if (_streamT < config.streamingUpdateSeconds) return;
            _streamT = 0f;

            StreamAroundCamera();
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
            if (waterMat.HasProperty("_Surface")) waterMat.SetFloat("_Surface", 1f);
            if (waterMat.HasProperty("_Blend")) waterMat.SetFloat("_Blend", 0f);
            if (waterMat.HasProperty("_SrcBlend")) waterMat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (waterMat.HasProperty("_DstBlend")) waterMat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (waterMat.HasProperty("_ZWrite")) waterMat.SetFloat("_ZWrite", 0f);
            if (waterMat.HasProperty("_Smoothness")) waterMat.SetFloat("_Smoothness", 0.10f);
            if (waterMat.HasProperty("_Glossiness")) waterMat.SetFloat("_Glossiness", 0.10f);
            if (waterMat.HasProperty("_Cull")) waterMat.SetFloat("_Cull", 0f);
            if (waterMat.HasProperty("_CullMode")) waterMat.SetFloat("_CullMode", 0f);
            waterMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            ApplyPlaneWaterAnimatedMaterial(waterMat, "Voxel_Water_Animated");

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
            ApplyPlaneWaterAnimatedMaterial(lowPolyWaterMaterial, "LowPoly_Water_Animated");

            if (IsStylizedTerrainPreviewScene())
            {
                if (grassMat != null)
                {
                    if (grassMat.HasProperty("_BaseMap")) grassMat.SetTexture("_BaseMap", null);
                    if (grassMat.HasProperty("_MainTex")) grassMat.SetTexture("_MainTex", null);
                    if (grassMat.HasProperty("_BaseColor")) grassMat.SetColor("_BaseColor", new Color(0.48f, 0.63f, 0.34f, 1f));
                    if (grassMat.HasProperty("_Color")) grassMat.SetColor("_Color", new Color(0.48f, 0.63f, 0.34f, 1f));
                }

                if (dirtMat != null)
                {
                    if (dirtMat.HasProperty("_BaseMap")) dirtMat.SetTexture("_BaseMap", null);
                    if (dirtMat.HasProperty("_MainTex")) dirtMat.SetTexture("_MainTex", null);
                    if (dirtMat.HasProperty("_BaseColor")) dirtMat.SetColor("_BaseColor", new Color(0.58f, 0.46f, 0.30f, 1f));
                    if (dirtMat.HasProperty("_Color")) dirtMat.SetColor("_Color", new Color(0.58f, 0.46f, 0.30f, 1f));
                }

                if (stoneMat != null)
                {
                    if (stoneMat.HasProperty("_BaseMap")) stoneMat.SetTexture("_BaseMap", null);
                    if (stoneMat.HasProperty("_MainTex")) stoneMat.SetTexture("_MainTex", null);
                    if (stoneMat.HasProperty("_BaseColor")) stoneMat.SetColor("_BaseColor", new Color(0.52f, 0.50f, 0.46f, 1f));
                    if (stoneMat.HasProperty("_Color")) stoneMat.SetColor("_Color", new Color(0.52f, 0.50f, 0.46f, 1f));
                }

                if (lowPolyTerrainMaterial != null)
                {
                    if (lowPolyTerrainMaterial.HasProperty("_BaseColor")) lowPolyTerrainMaterial.SetColor("_BaseColor", new Color(0.50f, 0.63f, 0.37f, 1f));
                    if (lowPolyTerrainMaterial.HasProperty("_Color")) lowPolyTerrainMaterial.SetColor("_Color", new Color(0.50f, 0.63f, 0.37f, 1f));
                    if (lowPolyTerrainMaterial.HasProperty("_Smoothness")) lowPolyTerrainMaterial.SetFloat("_Smoothness", 0.01f);
                    if (lowPolyTerrainMaterial.HasProperty("_Glossiness")) lowPolyTerrainMaterial.SetFloat("_Glossiness", 0.01f);
                }

                if (waterMat != null)
                {
                    if (waterMat.HasProperty("_BaseColor")) waterMat.SetColor("_BaseColor", new Color(0.17f, 0.45f, 0.50f, 0.82f));
                    if (waterMat.HasProperty("_Color")) waterMat.SetColor("_Color", new Color(0.17f, 0.45f, 0.50f, 0.82f));
                }

                if (lowPolyWaterMaterial != null)
                {
                    if (lowPolyWaterMaterial.HasProperty("_BaseColor")) lowPolyWaterMaterial.SetColor("_BaseColor", new Color(0.20f, 0.52f, 0.56f, 0.84f));
                    if (lowPolyWaterMaterial.HasProperty("_Color")) lowPolyWaterMaterial.SetColor("_Color", new Color(0.20f, 0.52f, 0.56f, 0.84f));
                }
            }

            if (showBoundaryWalls && boundaryWallMaterial == null)
            {
                boundaryWallMaterial = new Material(shader) { name = "Voxel_BoundaryWall" };
                boundaryWallMaterial.enableInstancing = true;
                boundaryWallMaterial.color = new Color(0.85f, 0.25f, 0.20f, 0.30f);
            }

            CacheTerrainBaseColors();
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
            lowPolyWaterHorizontalOverhang = Mathf.Clamp(lowPolyWaterHorizontalOverhang, 0.68f, 0.88f);
            lowPolyWaterEmbedDepth = Mathf.Clamp(lowPolyWaterEmbedDepth, 0f, 0.025f);
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
            EnsureStylizedNatureAssets();

            if (_incomingGroundGrassMeshes != null && _incomingGroundGrassMeshes.Length > 0)
            {
                enableGrassProps = false;
            }
            else if (_stylizedGrassMeshes != null && _stylizedGrassMeshes.Length > 0)
            {
                enableGrassProps = true;
                grassPropMesh = _stylizedGrassMeshes[0];
                grassPropMaterial = _stylizedGrassMaterial;
                grassPropDensity = Mathf.Max(grassPropDensity, 0.16f);
                grassPropsMaxPerChunk = Mathf.Max(grassPropsMaxPerChunk, 180);
            }

            if ((_stylizedFlowerMeshes != null && _stylizedFlowerMeshes.Length > 0) ||
                (_stylizedBushMeshes != null && _stylizedBushMeshes.Length > 0) ||
                (_stylizedPlantMeshes != null && _stylizedPlantMeshes.Length > 0) ||
                (_stylizedMushroomMeshes != null && _stylizedMushroomMeshes.Length > 0) ||
                (_stylizedRockMeshes != null && _stylizedRockMeshes.Length > 0) ||
                (_stylizedPebbleMeshes != null && _stylizedPebbleMeshes.Length > 0) ||
                (_stylizedPathRockMeshes != null && _stylizedPathRockMeshes.Length > 0))
            {
                enableFloraProps = true;
            }

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

            EnsureFeaturedTreeVariants();
            EnsurePinkTreeVariants();

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

#if UNITY_EDITOR
        private void EnsureStylizedNatureAssets()
        {
            if ((_stylizedGrassMeshes != null && _stylizedGrassMeshes.Length > 0) ||
                (_incomingGroundGrassMeshes != null && _incomingGroundGrassMeshes.Length > 0))
                return;

            LoadIncomingReplacementGrassAssets();

            if (_incomingGroundGrassMeshes == null || _incomingGroundGrassMeshes.Length == 0)
            {
                _stylizedGrassMeshes = LoadStylizedNatureMeshes(
                    "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Grass_Common_Short.fbx",
                    "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Grass_Wispy_Short.fbx");
            }

            _stylizedFlowerMeshes = LoadStylizedNatureMeshes(
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Flower_3_Group.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Flower_4_Group.fbx");

            _stylizedBushMeshes = LoadStylizedNatureMeshes(
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Bush_Common.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Bush_Common_Flowers.fbx");

            _stylizedPlantMeshes = LoadStylizedNatureMeshes(
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Clover_1.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Clover_2.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Fern_1.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Plant_1.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Plant_7.fbx");

            _stylizedMushroomMeshes = LoadStylizedNatureMeshes(
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Mushroom_Common.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Mushroom_Laetiporus.fbx");

            _stylizedRockMeshes = LoadStylizedNatureMeshes(
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Rock_Medium_1.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Rock_Medium_2.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Rock_Medium_3.fbx");

            _stylizedPebbleMeshes = LoadStylizedNatureMeshes(
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Pebble_Round_1.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Pebble_Round_2.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/Pebble_Round_3.fbx");

            _stylizedPathRockMeshes = LoadStylizedNatureMeshes(
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/RockPath_Round_Thin.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/RockPath_Round_Wide.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/RockPath_Square_Thin.fbx",
                "Assets/Models/Modeling/_Incoming/StylizedNature/FBX/RockPath_Square_Wide.fbx");

            _stylizedGrassMaterial = CreateStylizedNatureMaterial(
                "StylizedNature_Grass",
                "Assets/Models/Modeling/_Incoming/StylizedNature/Textures/Grass.png",
                new Color(0.95f, 1f, 0.95f),
                alphaClipped: true);

            _stylizedFlowerMaterial = CreateStylizedNatureMaterial(
                "StylizedNature_Flowers",
                "Assets/Models/Modeling/_Incoming/StylizedNature/Textures/Flowers.png",
                Color.white,
                alphaClipped: true);

            _stylizedLeafMaterial = CreateStylizedNatureMaterial(
                "StylizedNature_Leaves",
                "Assets/Models/Modeling/_Incoming/StylizedNature/Textures/Leaves.png",
                Color.white,
                alphaClipped: true);

            _stylizedMushroomMaterial = CreateStylizedNatureMaterial(
                "StylizedNature_Mushrooms",
                "Assets/Models/Modeling/_Incoming/StylizedNature/Textures/Mushrooms.png",
                Color.white,
                alphaClipped: true);

            _stylizedRockMaterial = CreateStylizedNatureMaterial(
                "StylizedNature_Rocks",
                "Assets/Models/Modeling/_Incoming/StylizedNature/Textures/Rocks_Diffuse.png",
                Color.white);

            _stylizedPathRockMaterial = CreateStylizedNatureMaterial(
                "StylizedNature_PathRocks",
                "Assets/Models/Modeling/_Incoming/StylizedNature/Textures/PathRocks_Diffuse.png",
                Color.white);
        }

        private void LoadIncomingReplacementGrassAssets()
        {
            string[] paths =
            {
                "Assets/Models/Modeling/_Incoming/grassa/grassa.obj",
                "Assets/Models/Modeling/_Incoming/grassb/grassb.obj",
                "Assets/Models/Modeling/_Incoming/grassc/grassc.obj"
            };

            var meshes = new List<Mesh>(paths.Length);
            var materials = new List<Material>(paths.Length);

            for (int i = 0; i < paths.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null)
                    continue;

                var mesh = LoadFirstMeshFromModel(paths[i]);
                var material = BuildImportedModelMaterial(prefab, $"IncomingGrass_{i}_Mat");
                if (mesh == null || material == null)
                    continue;

                meshes.Add(mesh);
                materials.Add(material);
            }

            _incomingGroundGrassMeshes = meshes.ToArray();
            _incomingGroundGrassMaterials = materials.ToArray();
        }

        private static Mesh[] LoadStylizedNatureMeshes(params string[] assetPaths)
        {
            var meshes = new List<Mesh>();
            if (assetPaths == null)
                return meshes.ToArray();

            for (int i = 0; i < assetPaths.Length; i++)
            {
                var mesh = LoadFirstMeshFromModel(assetPaths[i]);
                if (mesh != null && !meshes.Contains(mesh))
                    meshes.Add(mesh);
            }

            return meshes.ToArray();
        }

        private static Mesh LoadFirstMeshFromModel(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab != null)
            {
                var mf = prefab.GetComponentInChildren<MeshFilter>(true);
                if (mf != null && mf.sharedMesh != null)
                    return mf.sharedMesh;

                var smr = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                if (smr != null && smr.sharedMesh != null)
                    return smr.sharedMesh;
            }

            var directMesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
            if (directMesh != null)
                return directMesh;

            var assets = AssetDatabase.LoadAllAssetsAtPath(assetPath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is Mesh mesh)
                    return mesh;
            }

            return null;
        }

        private static Material CreateStylizedNatureMaterial(string matName, string texturePath, Color tint, bool alphaClipped = false)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = matName };
            mat.enableInstancing = true;

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (tex != null)
            {
                tex.filterMode = FilterMode.Bilinear;
                tex.anisoLevel = 4;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);

            if (alphaClipped)
            {
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 1f);
                if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0.35f);
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
            }

            return mat;
        }

        private static void ApplyPlaneWaterAnimatedMaterial(Material mat, string matName)
        {
            if (mat == null)
                return;

            var shader = Shader.Find("SCoL/StylizedAnimatedWater");
            if (shader != null && mat.shader != shader)
            {
                mat.shader = shader;
                mat.name = matName;
            }

            var normalTex = LoadAnimatedWaterNormalTexture();

            if (normalTex != null)
            {
                normalTex.filterMode = FilterMode.Bilinear;
                normalTex.anisoLevel = 2;
                if (mat.HasProperty("_NormalMap")) mat.SetTexture("_NormalMap", normalTex);
                if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", normalTex);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", new Color(0.10f, 0.24f, 0.44f, 1f));
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", new Color(0.10f, 0.24f, 0.44f, 1f));
            if (mat.HasProperty("_NormalStrength")) mat.SetFloat("_NormalStrength", 0.38f);
            if (mat.HasProperty("_NormalTiling")) mat.SetFloat("_NormalTiling", 0.42f);
            if (mat.HasProperty("_Opacity")) mat.SetFloat("_Opacity", 0.64f);
            if (mat.HasProperty("_HighlightStrength")) mat.SetFloat("_HighlightStrength", 0.06f);
            if (mat.HasProperty("_FresnelPower")) mat.SetFloat("_FresnelPower", 5.5f);
            if (mat.HasProperty("_FlowContrast")) mat.SetFloat("_FlowContrast", 0.32f);
            if (mat.HasProperty("_LightingStrength")) mat.SetFloat("_LightingStrength", 1.0f);
            if (mat.HasProperty("_ScrollA")) mat.SetVector("_ScrollA", new Vector4(0.115f, 0.035f, 0f, 0f));
            if (mat.HasProperty("_ScrollB")) mat.SetVector("_ScrollB", new Vector4(-0.075f, 0.085f, 0f, 0f));
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private static Texture2D LoadAnimatedWaterNormalTexture()
        {
            var tex = Resources.Load<Texture2D>("Water/plane_water_normal");
            if (tex != null)
            {
                tex.filterMode = FilterMode.Bilinear;
                tex.anisoLevel = 2;
                return tex;
            }

#if UNITY_EDITOR
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>(
                "Assets/Models/Modeling/_Incoming/Water/plane_water_low_gltf/textures/water_normal.png");
            if (tex != null)
            {
                tex.filterMode = FilterMode.Bilinear;
                tex.anisoLevel = 2;
            }
            return tex;
#else
            return null;
#endif
        }

        private void EnsureFeaturedTreeVariants()
        {
            if (_featuredTreeVariants != null && _featuredTreeVariants.Length > 0)
                return;

            string[] paths =
            {
                "Assets/Models/Modeling/_Incoming/tree1/smalltree.obj",
                "Assets/Models/Modeling/_Incoming/tree2/small tree 2.obj",
                "Assets/Models/Modeling/_Incoming/tree3/tree3.obj",
                "Assets/Models/Modeling/_Incoming/tree4/tree4.obj",
                "Assets/Models/Modeling/_Incoming/tree5/3d model.obj",
                "Assets/Models/Modeling/_Incoming/tree6/tree6.obj",
                "Assets/Models/Modeling/_Incoming/tree7/tree7.obj",
                "Assets/Models/Modeling/_Incoming/tree8/tree8.obj"
            };

            var variants = new List<TreeVariant>(paths.Length);
            for (int i = 0; i < paths.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null)
                    continue;

                float height = EstimatePrefabHeight(prefab);
                if (height <= 0.01f)
                    continue;

                variants.Add(new TreeVariant
                {
                    prefab = prefab,
                    sourceHeight = height,
                    materialOverride = BuildTreeVariantMaterial(prefab),
                    textureOverride = FindTreeVariantTexture(prefab)
                });
            }

            _featuredTreeVariants = variants.ToArray();

            if (_featuredTreeVariants.Length == 0)
            {
                string[] fallbackPaths =
                {
                    "Assets/Models/Modeling/_Incoming/tree1/smalltree.obj",
                    "Assets/Models/Modeling/_Incoming/tree2/small tree 2.obj",
                    "Assets/Models/Modeling/_Incoming/tree3/tree3.obj",
                    "Assets/Models/Modeling/_Incoming/tree4/tree4.obj",
                    "Assets/Models/Modeling/_Incoming/tree5/3d model.obj",
                    "Assets/Models/Modeling/_Incoming/tree6/tree6.obj",
                    "Assets/Models/Modeling/_Incoming/tree7/tree7.obj",
                    "Assets/Models/Modeling/_Incoming/tree8/tree8.obj"
                };

                variants = new List<TreeVariant>(fallbackPaths.Length);
                for (int i = 0; i < fallbackPaths.Length; i++)
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(fallbackPaths[i]);
                    if (prefab == null)
                        continue;

                    float height = EstimatePrefabHeight(prefab);
                    if (height <= 0.01f)
                        continue;

                    variants.Add(new TreeVariant
                    {
                        prefab = prefab,
                        sourceHeight = height,
                        materialOverride = BuildTreeVariantMaterial(prefab),
                        textureOverride = FindTreeVariantTexture(prefab)
                    });
                }

                _featuredTreeVariants = variants.ToArray();
            }

            if ((treeMesh == null || treeMaterial == null) && _featuredTreeVariants.Length > 0)
            {
                var samplePrefab = _featuredTreeVariants[0].prefab;
                if (samplePrefab != null)
                {
                    if (treeMesh == null)
                    {
                        var mf = samplePrefab.GetComponentInChildren<MeshFilter>(true);
                        if (mf != null && mf.sharedMesh != null)
                            treeMesh = mf.sharedMesh;
                        else
                        {
                            var smr = samplePrefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
                            if (smr != null && smr.sharedMesh != null)
                                treeMesh = smr.sharedMesh;
                        }
                    }

                    if (treeMaterial == null)
                    {
                        var r = samplePrefab.GetComponentInChildren<Renderer>(true);
                        if (r != null && r.sharedMaterial != null)
                        {
                            treeMaterial = new Material(r.sharedMaterial) { name = "TreeProp_Mat_FromVariant" };
                            treeMaterial.enableInstancing = true;
                        }
                    }
                }
            }
        }

        private void EnsurePinkTreeVariants()
        {
            if (_pinkTreeVariants != null && _pinkTreeVariants.Length > 0)
                return;

            string[] paths =
            {
                "Assets/Models/Modeling/_Incoming/pinktree1/pinktree1.obj",
                "Assets/Models/Modeling/_Incoming/pinktree2/pinktree2.obj"
            };

            var variants = new List<TreeVariant>(paths.Length);
            for (int i = 0; i < paths.Length; i++)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(paths[i]);
                if (prefab == null)
                    continue;

                float height = EstimatePrefabHeight(prefab);
                if (height <= 0.01f)
                    continue;

                variants.Add(new TreeVariant
                {
                    prefab = prefab,
                    sourceHeight = height,
                    materialOverride = BuildTreeVariantMaterial(prefab),
                    textureOverride = FindTreeVariantTexture(prefab)
                });
            }

            _pinkTreeVariants = variants.ToArray();
        }

        private static float EstimatePrefabHeight(GameObject prefab)
        {
            if (!TryGetRenderableBounds(prefab, out Bounds bounds))
                return 0f;
            return Mathf.Max(0.01f, bounds.size.y);
        }

        private static Material BuildTreeVariantMaterial(GameObject prefab)
        {
            return BuildImportedModelMaterial(prefab, prefab != null ? $"{prefab.name}_RuntimeTreeMat" : "RuntimeTreeMat");
        }

        private static Material BuildImportedModelMaterial(GameObject prefab, string materialName)
        {
            if (prefab == null)
                return null;

            var r = prefab.GetComponentInChildren<Renderer>(true);
            if (r == null || r.sharedMaterial == null)
                return null;

            var mat = new Material(r.sharedMaterial)
            {
                name = materialName,
                enableInstancing = true
            };

            var tex = FindTextureInPrefabDirectory(prefab);
            if (tex != null)
            {
                tex.filterMode = FilterMode.Bilinear;
                tex.anisoLevel = 4;
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            return mat;
        }

        private static Texture2D FindTreeVariantTexture(GameObject prefab)
        {
            return FindTextureInPrefabDirectory(prefab);
        }

        private static Texture2D FindTextureInPrefabDirectory(GameObject prefab)
        {
            if (prefab == null)
                return null;

            string prefabPath = AssetDatabase.GetAssetPath(prefab);
            if (string.IsNullOrEmpty(prefabPath))
                return null;

            string dir = System.IO.Path.GetDirectoryName(prefabPath);
            if (string.IsNullOrEmpty(dir))
                return null;

            string[] candidates =
            {
                System.IO.Path.Combine(dir, "material_BaseColor.jpg").Replace("\\", "/"),
                System.IO.Path.Combine(dir, "mesh1.jpg").Replace("\\", "/"),
                System.IO.Path.Combine(dir, "material_BaseColor.png").Replace("\\", "/"),
                System.IO.Path.Combine(dir, "mesh1.png").Replace("\\", "/"),
                System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileName(dir)}.jpg").Replace("\\", "/"),
                System.IO.Path.Combine(dir, $"{System.IO.Path.GetFileName(dir)}.png").Replace("\\", "/")
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(candidates[i]);
                if (tex != null)
                    return tex;
            }

            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { dir });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null)
                    return tex;
            }

            return null;
        }
#endif

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
            PruneRockTreeOverlaps();
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
            if (_lowPolyLandColliderMesh != null)
                Destroy(_lowPolyLandColliderMesh);
            _lowPolyLandColliderMesh = null;
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
            if (_snowOverlayTex != null)
                Destroy(_snowOverlayTex);
            _snowOverlayTex = null;
            if (_iceOverlayTex != null)
                Destroy(_iceOverlayTex);
            _iceOverlayTex = null;
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

            Shader snowShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (snowShader == null) snowShader = Shader.Find("Unlit/Texture");
            if (snowShader == null) snowShader = Shader.Find("Universal Render Pipeline/Lit");
            if (snowShader == null) snowShader = Shader.Find("Standard");

            Shader iceShader = Shader.Find("Universal Render Pipeline/Lit");
            if (iceShader == null) iceShader = Shader.Find("Standard");

            if (_snowOverlayMat == null || _snowOverlayMat.shader != snowShader)
            {
                _snowOverlayMat = new Material(snowShader) { name = "Winter_SnowOverlay" };
                _snowOverlayMat.enableInstancing = true;
            }
            if (_snowOverlayTex == null)
                _snowOverlayTex = CreateWinterOverlayTexture(
                    "Winter_SnowOverlayTex",
                    new Color(0.88f, 0.90f, 0.95f, 0.72f),
                    new Color(1f, 1f, 1f, 1f));
            ConfigureWinterOverlayMaterial(_snowOverlayMat, GetEffectiveSnowOverlayColor(), _snowOverlayTex, 0.02f, WinterSnowRenderQueue, opaqueMode: false);

            if (_iceOverlayMat == null || _iceOverlayMat.shader != iceShader)
            {
                _iceOverlayMat = new Material(iceShader) { name = "Winter_IceOverlay" };
                _iceOverlayMat.enableInstancing = true;
            }
            if (_iceOverlayTex == null)
                _iceOverlayTex = CreateWinterOverlayTexture(
                    "Winter_IceOverlayTex",
                    new Color(0.60f, 0.72f, 0.82f, 0.52f),
                    new Color(0.86f, 0.92f, 0.98f, 0.84f));
            ConfigureWinterOverlayMaterial(_iceOverlayMat, GetEffectiveIceOverlayColor(), _iceOverlayTex, 0.36f, WinterIceRenderQueue, opaqueMode: false);

            if (_snowOverlayGO == null)
                _snowOverlayGO = CreateOverlayGO("SnowOverlay", _snowOverlayMat, sortingOrder: 20);
            else
                UpdateOverlayRenderer(_snowOverlayGO, _snowOverlayMat, 20);
            if (_iceOverlayGO == null)
                _iceOverlayGO = CreateOverlayGO("IceOverlay", _iceOverlayMat, sortingOrder: 10);
            else
                UpdateOverlayRenderer(_iceOverlayGO, _iceOverlayMat, 10);
            if (_iceColliderGO == null)
                _iceColliderGO = CreateIceColliderGO("IceCollider");

            ApplyWinterOverlayAmount(_winterVisualAmount);
        }

        private GameObject CreateOverlayGO(string name, Material mat, int sortingOrder)
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
            mr.sortingOrder = sortingOrder;
            return go;
        }

        private static void UpdateOverlayRenderer(GameObject go, Material mat, int sortingOrder)
        {
            if (go == null)
                return;

            var mr = go.GetComponent<MeshRenderer>();
            if (mr == null)
                return;

            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.sortingOrder = sortingOrder;
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

            if (!includeWaterColumns &&
                useLowPolyTerrainVisual &&
                _lowPolyLandMesh != null &&
                _lowPolyLandMesh.vertexCount > 0)
            {
                if (IsWinterSurfaceFrozen)
                {
                    CopyElevatedMeshInto(mesh, _lowPolyLandMesh, Mathf.Max(FrozenLowPolySnowLift, winterOverlayHeightOffset), doubleSided: lowPolyDoubleSided);
                    return;
                }

                BuildLowPolyCornerMaps(out var snowLandCorners, out var snowWaterMask);
                var snowLandMesh = BuildLowPolyLandSnowOverlayMesh(snowLandCorners, snowWaterMask);
                CopyElevatedMeshInto(mesh, snowLandMesh, Mathf.Max(0.001f, winterOverlayHeightOffset), doubleSided: lowPolyDoubleSided);
                Destroy(snowLandMesh);
                return;
            }

            if (includeWaterColumns &&
                useLowPolyTerrainVisual &&
                _lowPolyWaterMesh != null &&
                _lowPolyWaterMesh.vertexCount > 0)
            {
                BuildLowPolyCornerMaps(out _, out var iceWaterMask);
                PrepareWinterIceMask(iceWaterMask);
                var iceMesh = BuildLowPolyWaterMesh(
                    iceWaterMask,
                    extraOverhang: Mathf.Max(0f, winterIceExtraOverhang),
                    extraEmbedDepth: Mathf.Max(0f, winterIceExtraEmbed),
                    additionalYOffset: Mathf.Max(0.001f, winterOverlayHeightOffset) + 0.012f,
                    thresholdBias: winterIceThresholdBias,
                    subOverride: Mathf.Max(lowPolyWaterSubdivisions, winterIceSubdivisions));
                CopyMeshInto(mesh, iceMesh);
                Destroy(iceMesh);
                return;
            }

            float inset = includeWaterColumns ? Mathf.Clamp(winterOverlayInset, 0.001f, 0.49f) : 0f;
            float h = Mathf.Max(0.001f, winterOverlayHeightOffset);
            float uvScale = Mathf.Max(0.001f, winterOverlayUvScale);
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

                uvs.Add(new Vector2(x0 * uvScale, z0 * uvScale));
                uvs.Add(new Vector2(x1 * uvScale, z0 * uvScale));
                uvs.Add(new Vector2(x1 * uvScale, z1 * uvScale));
                uvs.Add(new Vector2(x0 * uvScale, z1 * uvScale));

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

            if (useLowPolyTerrainVisual &&
                _lowPolyWaterMesh != null &&
                _lowPolyWaterMesh.vertexCount > 0)
            {
                BuildLowPolyCornerMaps(out _, out var iceWaterMask);
                PrepareWinterIceMask(iceWaterMask);
                var iceMesh = BuildLowPolyWaterMesh(
                    iceWaterMask,
                    extraOverhang: Mathf.Max(0f, winterIceExtraOverhang),
                    extraEmbedDepth: Mathf.Max(0f, winterIceExtraEmbed),
                    additionalYOffset: Mathf.Max(0.001f, winterOverlayHeightOffset) + 0.015f,
                    thresholdBias: winterIceThresholdBias,
                    subOverride: Mathf.Max(lowPolyWaterSubdivisions, winterIceSubdivisions));
                CopyMeshInto(mesh, iceMesh);
                Destroy(iceMesh);
                mc.sharedMesh = null;
                mc.sharedMesh = mesh;
                return;
            }

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

        private static void CopyElevatedMeshInto(Mesh target, Mesh source, float yOffset, bool doubleSided)
        {
            if (target == null || source == null)
                return;

            var srcVerts = source.vertices;
            var verts = new Vector3[srcVerts.Length];
            for (int i = 0; i < srcVerts.Length; i++)
                verts[i] = srcVerts[i] + (Vector3.up * yOffset);

            target.Clear();
            target.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            target.vertices = verts;
            target.uv = source.uv;
            target.normals = null;

            var srcTris = source.triangles;
            if (!doubleSided)
            {
                target.triangles = srcTris;
            }
            else
            {
                var tris = new int[srcTris.Length * 2];
                Array.Copy(srcTris, tris, srcTris.Length);
                for (int i = 0; i < srcTris.Length; i += 3)
                {
                    int dst = srcTris.Length + i;
                    tris[dst + 0] = srcTris[i + 0];
                    tris[dst + 1] = srcTris[i + 2];
                    tris[dst + 2] = srcTris[i + 1];
                }
                target.triangles = tris;
            }

            target.RecalculateNormals();
            target.RecalculateBounds();
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
                _winterVisualAmount = 0f;
                _winterVisualsActive = false;
                SetWinterVisualState(0f, visualsActive: false, iceFrozen: false);
                return;
            }

            float targetAmount = GetWinterTargetAmount();
            float nextAmount = targetAmount;
            if (!force && Application.isPlaying)
            {
                float speed = targetAmount >= _winterVisualAmount ? winterSnowBuildSpeed : winterSnowMeltSpeed;
                nextAmount = Mathf.MoveTowards(_winterVisualAmount, targetAmount, Mathf.Max(0.02f, speed) * Time.unscaledDeltaTime);
            }

            bool visualsActive = nextAmount > 0.001f;
            bool iceFrozen = nextAmount >= winterIceFrozenThreshold && targetAmount > 0.001f;
            if (!force &&
                Mathf.Approximately(nextAmount, _winterVisualAmount) &&
                iceFrozen == _winterVisualsActive)
                return;

            _winterVisualAmount = nextAmount;
            _winterVisualsActive = iceFrozen;
            SetWinterVisualState(nextAmount, visualsActive, iceFrozen);
        }

        private void SetWinterVisualState(float amount, bool visualsActive, bool iceFrozen)
        {
            if (_winterOverlayRoot == null && visualsActive)
                EnsureWinterOverlayObjects();
            UpdateWinterOverlayMaterialModes(iceFrozen);
            ApplyWinterOverlayAmount(amount);
            ApplyWinterTerrainTint(amount);
            if (_winterOverlayRoot != null && _winterOverlayRoot.activeSelf != visualsActive)
                _winterOverlayRoot.SetActive(visualsActive);
            if (_iceColliderGO != null)
            {
                var mc = _iceColliderGO.GetComponent<MeshCollider>();
                if (mc != null) mc.enabled = iceFrozen;
            }

            RefreshWaterSurfaceVisibility();
        }

        private float GetWinterTargetAmount()
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

            float target = 0f;
            if (_seasonSkybox != null && _seasonSkybox.GetCurrentSeason() == SeasonSkyboxController.Season.Winter)
                target = 1f;
            if (_weatherSystem != null && _weatherSystem.CurrentPhase == WeatherPhase.Snow)
                target = Mathf.Max(target, Mathf.Clamp01(Mathf.Max(0.15f, _weatherSystem.Intensity01)));
            if (_runtime != null && _runtime.CurrentSeason == SCoL.Season.Winter)
                target = 1f;

            return target;
        }

        private void CacheTerrainBaseColors()
        {
            _lowPolyTerrainBaseColor = GetMaterialColor(lowPolyTerrainMaterial, _lowPolyTerrainBaseColor);
        }

        private void ApplyWinterTerrainTint(float amount)
        {
            if (lowPolyTerrainMaterial == null)
                return;

            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount));
            var winterTint = new Color(0.90f, 0.93f, 0.95f, 1f);
            var color = Color.Lerp(_lowPolyTerrainBaseColor, winterTint, Mathf.Clamp01(t * 0.9f));
            SetMaterialColor(lowPolyTerrainMaterial, color);
        }

        private void ApplyWinterOverlayAmount(float amount)
        {
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(amount));
            ApplyOverlayAlpha(_snowOverlayMat, GetEffectiveSnowOverlayColor(), t);
            ApplyOverlayAlpha(_iceOverlayMat, GetEffectiveIceOverlayColor(), t);
        }

        private static Color GetMaterialColor(Material mat, Color fallback)
        {
            if (mat == null)
                return fallback;
            if (mat.HasProperty("_BaseColor"))
                return mat.GetColor("_BaseColor");
            if (mat.HasProperty("_Color"))
                return mat.GetColor("_Color");
            return fallback;
        }

        private static void SetMaterialColor(Material mat, Color color)
        {
            if (mat == null)
                return;
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
        }

        private void UpdateWinterOverlayMaterialModes(bool iceFrozen)
        {
            ConfigureWinterOverlayMaterial(
                _snowOverlayMat,
                GetEffectiveSnowOverlayColor(),
                _snowOverlayTex,
                0.02f,
                iceFrozen ? 2006 : WinterSnowRenderQueue,
                opaqueMode: iceFrozen);

            ConfigureWinterOverlayMaterial(
                _iceOverlayMat,
                GetEffectiveIceOverlayColor(),
                _iceOverlayTex,
                0.36f,
                iceFrozen ? 2010 : WinterIceRenderQueue,
                opaqueMode: iceFrozen);
        }

        private static void ApplyOverlayAlpha(Material mat, Color baseColor, float amount)
        {
            if (mat == null)
                return;

            var tint = baseColor;
            tint.a *= amount;
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
        }

        private Color GetEffectiveSnowOverlayColor()
        {
            var tint = snowOverlayColor;
            tint.a = Mathf.Clamp(tint.a, 0.80f, 1f);
            return tint;
        }

        private Color GetEffectiveIceOverlayColor()
        {
            var tint = iceOverlayColor;
            tint.r = Mathf.Clamp(tint.r, 0.72f, 0.86f);
            tint.g = Mathf.Clamp(tint.g, Mathf.Max(tint.r + 0.04f, 0.80f), 0.90f);
            tint.b = Mathf.Clamp(tint.b, Mathf.Max(tint.g + 0.04f, 0.88f), 0.97f);
            tint.a = Mathf.Clamp(tint.a, 0.82f, 0.95f);
            return tint;
        }

        private static void ConfigureWinterOverlayMaterial(Material mat, Color tint, Texture2D tex, float smoothness, int renderQueue, bool opaqueMode)
        {
            if (mat == null)
                return;

            bool looksLikeUnlit = mat.shader != null &&
                (mat.shader.name.Contains("Unlit", StringComparison.OrdinalIgnoreCase) ||
                 mat.shader.name.Contains("/Particles/", StringComparison.OrdinalIgnoreCase));

            if (tex != null)
            {
                if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex);
                if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex);
            }

            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", tint);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", tint);
            if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", opaqueMode ? 0f : 1f);
            if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
            if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", opaqueMode ? (float)UnityEngine.Rendering.BlendMode.One : (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", opaqueMode ? (float)UnityEngine.Rendering.BlendMode.Zero : (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", opaqueMode ? 1f : 0f);
            if (!looksLikeUnlit)
            {
                if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", smoothness);
                if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", smoothness);
            }
            if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f);
            if (mat.HasProperty("_CullMode")) mat.SetFloat("_CullMode", 0f);
            if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);
            if (mat.HasProperty("_Cutoff")) mat.SetFloat("_Cutoff", 0f);

            mat.renderQueue = renderQueue;
            if (opaqueMode)
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
            else
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
        }

        private static Texture2D CreateWinterOverlayTexture(string name, Color lowColor, Color highColor)
        {
            const int size = 64;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = name,
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Bilinear,
                anisoLevel = 2
            };

            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)(size - 1);
                float v = y / (float)(size - 1);
                float broad = Mathf.PerlinNoise(u * 3.7f + 11.2f, v * 3.7f + 5.4f);
                float detail = Mathf.PerlinNoise(u * 8.1f + 3.1f, v * 8.1f + 17.7f);
                float n = Mathf.Clamp01((broad * 0.72f) + (detail * 0.28f));
                n = Mathf.SmoothStep(0.12f, 0.92f, n);
                pixels[(y * size) + x] = Color.Lerp(lowColor, highColor, n);
            }

            tex.SetPixels(pixels);
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            return tex;
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
            ApplyWaterSurfaceVisibilityToChunk(cc, mr, _waterSurfaceVisible && !IsWinterSurfaceFrozen);

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
                gp.grassMeshVariants = _stylizedGrassMeshes != null && _stylizedGrassMeshes.Length > 1
                    ? _stylizedGrassMeshes
                    : null;
                gp.grassMaterial = grassPropMaterial;
                gp.density = grassPropDensity;
                gp.maxPerChunk = grassPropsMaxPerChunk;
                gp.strictGridPlacement = false;
                gp.randomOffsetXZ = new Vector2(0.42f, 0.42f);
                gp.scaleRange = new Vector2(0.14f, 0.24f);
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

                if (_incomingGroundGrassMeshes != null && _incomingGroundGrassMaterials != null)
                {
                    int incomingCount = Mathf.Min(_incomingGroundGrassMeshes.Length, _incomingGroundGrassMaterials.Length);
                    for (int i = 0; i < incomingCount; i++)
                    {
                        if (_incomingGroundGrassMeshes[i] == null || _incomingGroundGrassMaterials[i] == null)
                            continue;

                        props.Add(new FloraPropChunk.Prop
                        {
                            name = $"IncomingGrass_{i}",
                            mesh = _incomingGroundGrassMeshes[i],
                            material = _incomingGroundGrassMaterials[i],
                            density = 0.065f,
                            maxPerChunk = 18,
                            onlyOnGrass = true,
                            requireAboveSeaLevel = true,
                            strictGridPlacement = false,
                            randomOffsetXZ = new Vector2(0.18f, 0.18f),
                            scaleRange = new Vector2(0.38f, 0.62f),
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 0,
                            alignToSmoothedTerrain = true,
                            embedDepth = 0.08f
                        });
                    }
                }

                if (_stylizedBushMeshes != null && _stylizedBushMeshes.Length > 0 && _stylizedLeafMaterial != null)
                {
                    for (int i = 0; i < _stylizedBushMeshes.Length; i++)
                    {
                        props.Add(new FloraPropChunk.Prop
                        {
                            name = $"StylizedBush_{i}",
                            mesh = _stylizedBushMeshes[i],
                            material = _stylizedLeafMaterial,
                            density = 0.012f,
                            maxPerChunk = 6,
                            onlyOnGrass = true,
                            requireAboveSeaLevel = true,
                            strictGridPlacement = false,
                            randomOffsetXZ = new Vector2(0.18f, 0.18f),
                            scaleRange = new Vector2(0.035f, 0.055f),
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 1,
                            alignToSmoothedTerrain = true,
                            embedDepth = 0.08f
                        });
                    }
                }

                if (_stylizedPlantMeshes != null && _stylizedPlantMeshes.Length > 0 && _stylizedLeafMaterial != null)
                {
                    for (int i = 0; i < _stylizedPlantMeshes.Length; i++)
                    {
                        Vector2 scaleRange = i >= 3
                            ? new Vector2(0.05f, 0.09f)
                            : new Vector2(0.18f, 0.32f);

                        props.Add(new FloraPropChunk.Prop
                        {
                            name = $"StylizedPlant_{i}",
                            mesh = _stylizedPlantMeshes[i],
                            material = _stylizedLeafMaterial,
                            density = 0.020f,
                            maxPerChunk = 10,
                            onlyOnGrass = true,
                            requireAboveSeaLevel = true,
                            strictGridPlacement = false,
                            randomOffsetXZ = new Vector2(0.18f, 0.18f),
                            scaleRange = scaleRange,
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 0,
                            alignToSmoothedTerrain = true,
                            embedDepth = 0.10f
                        });
                    }
                }

                if (_stylizedMushroomMeshes != null && _stylizedMushroomMeshes.Length > 0 && _stylizedMushroomMaterial != null)
                {
                    for (int i = 0; i < _stylizedMushroomMeshes.Length; i++)
                    {
                        props.Add(new FloraPropChunk.Prop
                        {
                            name = $"StylizedMushroom_{i}",
                            mesh = _stylizedMushroomMeshes[i],
                            material = _stylizedMushroomMaterial,
                            density = 0.009f,
                            maxPerChunk = 5,
                            onlyOnGrass = true,
                            requireAboveSeaLevel = true,
                            strictGridPlacement = false,
                            randomOffsetXZ = new Vector2(0.26f, 0.26f),
                            scaleRange = new Vector2(0.82f, 1.08f),
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 0,
                            alignToSmoothedTerrain = true,
                            embedDepth = 0.08f
                        });
                    }
                }

                if (_stylizedRockMeshes != null && _stylizedRockMeshes.Length > 0 && _stylizedRockMaterial != null)
                {
                    for (int i = 0; i < _stylizedRockMeshes.Length; i++)
                    {
                        props.Add(new FloraPropChunk.Prop
                        {
                            name = $"StylizedRock_{i}",
                            mesh = _stylizedRockMeshes[i],
                            material = _stylizedRockMaterial,
                            density = 0.0025f,
                            maxPerChunk = 5,
                            onlyOnGrass = false,
                            requireAboveSeaLevel = true,
                            strictGridPlacement = false,
                            randomOffsetXZ = new Vector2(0.22f, 0.22f),
                            scaleRange = new Vector2(0.16f, 1.35f),
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 0,
                            instantiateAsObject = true,
                            colliderMode = FloraPropChunk.ColliderMode.Box,
                            embedDepth = 0.34f,
                            alignToSmoothedTerrain = true
                        });
                    }
                }

                if (_stylizedPebbleMeshes != null && _stylizedPebbleMeshes.Length > 0 && _stylizedRockMaterial != null)
                {
                    for (int i = 0; i < _stylizedPebbleMeshes.Length; i++)
                    {
                        props.Add(new FloraPropChunk.Prop
                        {
                            name = $"StylizedPebble_{i}",
                            mesh = _stylizedPebbleMeshes[i],
                            material = _stylizedRockMaterial,
                            density = 0.0035f,
                            maxPerChunk = 7,
                            onlyOnGrass = false,
                            requireAboveSeaLevel = true,
                            strictGridPlacement = false,
                            randomOffsetXZ = new Vector2(0.18f, 0.18f),
                            scaleRange = new Vector2(0.04f, 0.20f),
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 0,
                            alignToSmoothedTerrain = true,
                            embedDepth = 0.08f
                        });
                    }
                }

                if (_stylizedPathRockMeshes != null && _stylizedPathRockMeshes.Length > 0 && _stylizedPathRockMaterial != null)
                {
                    for (int i = 0; i < _stylizedPathRockMeshes.Length; i++)
                    {
                        props.Add(new FloraPropChunk.Prop
                        {
                            name = $"StylizedPathRock_{i}",
                            mesh = _stylizedPathRockMeshes[i],
                            material = _stylizedPathRockMaterial,
                            density = 0.0020f,
                            maxPerChunk = 4,
                            onlyOnGrass = false,
                            requireAboveSeaLevel = true,
                            strictGridPlacement = false,
                            randomOffsetXZ = new Vector2(0.16f, 0.16f),
                            scaleRange = new Vector2(0.05f, 0.20f),
                            avoidSteepSlopes = true,
                            maxNeighborDelta = 0,
                            alignToSmoothedTerrain = true,
                            embedDepth = 0.08f
                        });
                    }
                }

                // Flowers
                if (props.Count == 0 && flowerMaterial != null)
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
            if (_lowPolyLandColliderMesh != null)
                Destroy(_lowPolyLandColliderMesh);
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
                _lowPolyLandColliderMesh = BuildLowPolyLandMesh(cornerHeights);
                _lowPolyLandCollider.sharedMesh = _lowPolyLandColliderMesh;
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
            ExpandWaterMask(mask, Mathf.Clamp(lowPolyWaterMaskExpandPasses, 0, 6), Mathf.Clamp01(lowPolyWaterMaskExpandStrength));
        }

        private void PrepareWinterIceMask(float[,] mask)
        {
            if (mask == null)
                return;

            ExpandWaterMask(mask, Mathf.Clamp(winterIceMaskExpandPasses, 0, 6), Mathf.Clamp01(Mathf.Max(lowPolyWaterMaskExpandStrength, 0.9f)));

            int smoothPasses = Mathf.Clamp(winterIceMaskSmoothingPasses, 0, 8);
            if (smoothPasses > 0)
            {
                SmoothScalarField(
                    mask,
                    smoothPasses,
                    Mathf.Clamp01(winterIceMaskSmoothingStrength),
                    Mathf.Max(0.5f, lowPolyDiagonalSmoothingWeight));
            }
        }

        private void ExpandWaterMask(float[,] mask, int passes, float strength)
        {
            if (mask == null || passes <= 0 || strength <= 0f)
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

        private Mesh BuildLowPolyLandSnowOverlayMesh(float[,] cornerHeights, float[,] cornerWaterMask)
        {
            int w = config.worldWidth;
            int d = config.worldDepth;
            int sub = Mathf.Clamp(lowPolyTerrainSubdivisions, 1, 3);
            float uvScale = Mathf.Max(0.01f, lowPolyUVScale);
            float waterThreshold = Mathf.Clamp(lowPolyWaterMaskThreshold, 0.01f, 0.99f);
            // Let snow reach slightly closer to the shoreline so frozen lakes do not get a bare green ring.
            float landThreshold = Mathf.Clamp((1f - waterThreshold) - 0.18f, 0.50f, 0.85f);

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

                    float m00 = cornerWaterMask != null ? EvalMask(cornerWaterMask, x, z, u0, v0) : 0f;
                    float m10 = cornerWaterMask != null ? EvalMask(cornerWaterMask, x, z, u1, v0) : 0f;
                    float m01 = cornerWaterMask != null ? EvalMask(cornerWaterMask, x, z, u0, v1) : 0f;
                    float m11 = cornerWaterMask != null ? EvalMask(cornerWaterMask, x, z, u1, v1) : 0f;
                    float land00 = 1f - m00;
                    float land10 = 1f - m10;
                    float land01 = 1f - m01;
                    float land11 = 1f - m11;
                    float landMax = Mathf.Max(Mathf.Max(land00, land10), Mathf.Max(land01, land11));
                    if (landMax < landThreshold)
                        continue;

                    Vector3 p00 = EvalLandPoint(cornerHeights, x, z, u0, v0);
                    Vector3 p10 = EvalLandPoint(cornerHeights, x, z, u1, v0);
                    Vector3 p01 = EvalLandPoint(cornerHeights, x, z, u0, v1);
                    Vector3 p11 = EvalLandPoint(cornerHeights, x, z, u1, v1);

                    Vector2 uv00 = new Vector2((x + u0) * uvScale, (z + v0) * uvScale);
                    Vector2 uv10 = new Vector2((x + u1) * uvScale, (z + v0) * uvScale);
                    Vector2 uv01 = new Vector2((x + u0) * uvScale, (z + v1) * uvScale);
                    Vector2 uv11 = new Vector2((x + u1) * uvScale, (z + v1) * uvScale);

                    AddClippedScalarTri(verts, tris, uvs, p00, p10, p11, uv00, uv10, uv11, land00, land10, land11, landThreshold, lowPolyDoubleSided);
                    AddClippedScalarTri(verts, tris, uvs, p00, p11, p01, uv00, uv11, uv01, land00, land11, land01, landThreshold, lowPolyDoubleSided);
                }
            }

            var mesh = new Mesh { name = "LowPoly_LandSnowOverlay" };
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.SetUVs(0, uvs);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private Mesh BuildLowPolyWaterMesh(float[,] cornerWaterMask, float extraOverhang = 0f, float extraEmbedDepth = 0f, float additionalYOffset = 0f, float thresholdBias = 0f, int subOverride = -1)
        {
            int w = config.worldWidth;
            int d = config.worldDepth;
            int sub = Mathf.Clamp(subOverride > 0 ? subOverride : lowPolyWaterSubdivisions, 1, 8);
            float threshold = Mathf.Clamp(lowPolyWaterMaskThreshold + thresholdBias, 0.01f, 0.99f);
            float uvScale = Mathf.Max(0.01f, lowPolyUVScale);
            float seaY = Mathf.Clamp(
                config.seaLevel + 1f + lowPolyWaterYOffset + additionalYOffset - Mathf.Max(0f, lowPolyWaterEmbedDepth + extraEmbedDepth),
                0f,
                config.worldHeight + 8f);
            float overhangPerSubQuad = Mathf.Max(0f, lowPolyWaterHorizontalOverhang + extraOverhang) / sub;

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
                    float mMax = Mathf.Max(Mathf.Max(m00, m10), Mathf.Max(m01, m11));
                    if (mMax < threshold)
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

                    AddClippedScalarTri(verts, tris, uvs, p00, p10, p11, uv00, uv10, uv11, m00, m10, m11, threshold, lowPolyWaterDoubleSided);
                    AddClippedScalarTri(verts, tris, uvs, p00, p11, p01, uv00, uv11, uv01, m00, m11, m01, threshold, lowPolyWaterDoubleSided);
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

        private static void CopyMeshInto(Mesh dest, Mesh source)
        {
            if (dest == null || source == null)
                return;

            dest.Clear();
            dest.indexFormat = source.indexFormat;
            dest.vertices = source.vertices;
            dest.normals = source.normals;
            dest.uv = source.uv;
            dest.triangles = source.triangles;
            dest.bounds = source.bounds;
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

        private static void AddClippedScalarTri(
            List<Vector3> verts, List<int> tris, List<Vector2> uvs,
            Vector3 a, Vector3 b, Vector3 c,
            Vector2 uva, Vector2 uvb, Vector2 uvc,
            float sa, float sb, float sc,
            float threshold,
            bool doubleSided)
        {
            var polyPos = new List<Vector3>(4) { a, b, c };
            var polyUv = new List<Vector2>(4) { uva, uvb, uvc };
            var polyS = new List<float>(4) { sa, sb, sc };

            ClipScalarPolygon(polyPos, polyUv, polyS, threshold);
            if (polyPos.Count < 3)
                return;

            for (int i = 1; i < polyPos.Count - 1; i++)
            {
                AddFlatTri(
                    verts, tris, uvs,
                    polyPos[0], polyPos[i], polyPos[i + 1],
                    polyUv[0], polyUv[i], polyUv[i + 1],
                    doubleSided);
            }
        }

        private static void ClipScalarPolygon(List<Vector3> polyPos, List<Vector2> polyUv, List<float> polyS, float threshold)
        {
            if (polyPos == null || polyUv == null || polyS == null)
                return;

            var outPos = new List<Vector3>(polyPos.Count + 1);
            var outUv = new List<Vector2>(polyUv.Count + 1);
            var outS = new List<float>(polyS.Count + 1);

            for (int i = 0; i < polyPos.Count; i++)
            {
                int next = (i + 1) % polyPos.Count;
                Vector3 p0 = polyPos[i];
                Vector3 p1 = polyPos[next];
                Vector2 uv0 = polyUv[i];
                Vector2 uv1 = polyUv[next];
                float s0 = polyS[i];
                float s1 = polyS[next];

                bool in0 = s0 >= threshold;
                bool in1 = s1 >= threshold;

                if (in0)
                {
                    outPos.Add(p0);
                    outUv.Add(uv0);
                    outS.Add(s0);
                }

                if (in0 == in1 || Mathf.Approximately(s0, s1))
                    continue;

                float t = Mathf.InverseLerp(s0, s1, threshold);
                outPos.Add(Vector3.LerpUnclamped(p0, p1, t));
                outUv.Add(Vector2.LerpUnclamped(uv0, uv1, t));
                outS.Add(threshold);
            }

            polyPos.Clear();
            polyUv.Clear();
            polyS.Clear();

            polyPos.AddRange(outPos);
            polyUv.AddRange(outUv);
            polyS.AddRange(outS);
        }

        private void BuildFeaturedTrees()
        {
            bool hasVariantPool = _featuredTreeVariants != null && _featuredTreeVariants.Length > 0;
            bool hasFallbackTree = treeMesh != null && treeMaterial != null;
            if (!enableFeaturedTrees || config == null || (!hasVariantPool && !hasFallbackTree))
                return;

            if (_featuredTreesRoot != null)
                Destroy(_featuredTreesRoot);

            _featuredTreesRoot = new GameObject("FeaturedTrees");
            _featuredTreesRoot.transform.SetParent(transform, worldPositionStays: true);
            _featuredTreesRoot.transform.position = OriginWorld;

            int target = Mathf.Max(0, featuredTreeCount);
            if (target <= 0)
                return;

            var used = new HashSet<int>(target * 2);
            var placedPositions = new List<Vector2>(target);
            var prng = new System.Random(unchecked(_seed * 397) ^ 0x34A7F1);
            int placed = 0;

            int safety = 0;
            int maxAttempts = Mathf.Max(256, target * 120);
            while (placed < target && safety++ < maxAttempts)
            {
                int sx = prng.Next(0, Mathf.Max(1, config.worldWidth));
                int sz = prng.Next(0, Mathf.Max(1, config.worldDepth));
                if (!TryFindFeaturedTreeColumn(sx, sz, out int px, out int pz))
                    continue;

                int clusterPlaced = 0;
                if (hasVariantPool &&
                    placed < target - 2 &&
                    prng.NextDouble() < Mathf.Clamp01(featuredTreeClusterChance))
                {
                    clusterPlaced = TryPlaceFeaturedTreeCluster(placed, px, pz, prng, used, placedPositions, target - placed);
                }

                if (clusterPlaced > 0)
                {
                    placed += clusterPlaced;
                    continue;
                }

                int key = ColumnKey(px, pz);
                if (!used.Add(key))
                    continue;
                if (!HasTreeSpacing(px, pz, placedPositions))
                {
                    used.Remove(key);
                    continue;
                }

                PlaceFeaturedTree(placed, px, pz, prng, null);
                placedPositions.Add(new Vector2(px, pz));
                placed++;
            }

            if (enablePinkTreeGrove && _pinkTreeVariants != null && _pinkTreeVariants.Length > 0)
            {
                PlacePinkTreeGrove(placed, prng, used, placedPositions);
            }

            if (placed < target)
            {
                Debug.LogWarning($"[VoxelWorld] Featured trees placed {placed}/{target}. Consider lowering constraints or search radius.", this);
            }
        }

        private void PlacePinkTreeGrove(int startIndex, System.Random prng, HashSet<int> used, List<Vector2> placedPositions)
        {
            if (config == null || _pinkTreeVariants == null || _pinkTreeVariants.Length == 0)
                return;

            int count = Mathf.Max(0, pinkTreeCount);
            if (count <= 0)
                return;

            var rect = GetPinkTreeGroveRect();
            int placed = 0;
            int attempts = 0;
            int maxAttempts = Mathf.Max(240, count * 40);

            while (placed < count && attempts++ < maxAttempts)
            {
                int sx = prng.Next(rect.xMin, rect.xMax);
                int sz = prng.Next(rect.yMin, rect.yMax);
                if (!TryFindFeaturedTreeColumnInRect(sx, sz, rect, out int px, out int pz))
                    continue;

                int key = ColumnKey(px, pz);
                if (used.Contains(key))
                    continue;
                if (!HasTreeSpacing(px, pz, placedPositions, pinkTreeMinSpacing))
                    continue;

                var variant = _pinkTreeVariants[prng.Next(0, _pinkTreeVariants.Length)];
                PlaceFeaturedTree(startIndex + placed, px, pz, prng, variant, pinkTreeTargetHeightRange);
                used.Add(key);
                placedPositions.Add(new Vector2(px, pz));
                placed++;
            }
        }

        private RectInt GetPinkTreeGroveRect()
        {
            int margin = Mathf.Max(2, pinkTreeEdgeMargin);
            int width = Mathf.Clamp(Mathf.RoundToInt(config.worldWidth * pinkTreeGroveWidthRatio), 8, Mathf.Max(8, config.worldWidth - margin * 2));
            int depth = Mathf.Clamp(Mathf.RoundToInt(config.worldDepth * pinkTreeGroveDepthRatio), 10, Mathf.Max(10, config.worldDepth - margin * 2));

            int xMin = Mathf.Clamp(config.worldWidth - margin - width, margin, Mathf.Max(margin, config.worldWidth - margin - 1));

            int zCenter = Mathf.RoundToInt(config.worldDepth * 0.5f);
            int zMin = Mathf.Clamp(zCenter - depth / 2, margin, Mathf.Max(margin, config.worldDepth - margin - depth));

            return new RectInt(xMin, zMin, Mathf.Max(1, width), Mathf.Max(1, depth));
        }

        private bool TryFindFeaturedTreeColumnInRect(int centerX, int centerZ, RectInt rect, out int outX, out int outZ)
        {
            outX = centerX;
            outZ = centerZ;

            int maxR = Mathf.Max(0, featuredTreeSearchRadius);
            for (int pass = 0; pass < 2; pass++)
            {
                bool requireGrass = pass == 0;

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
                            if (!rect.Contains(new Vector2Int(x, z)))
                                continue;
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
            if (IsNearWaterColumn(x, z, Mathf.Max(0, featuredTreeWaterBufferRadius))) return false;

            return true;
        }

        private bool IsNearWaterColumn(int centerX, int centerZ, int radius)
        {
            if (config == null || radius <= 0)
                return false;

            for (int dz = -radius; dz <= radius; dz++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int x = centerX + dx;
                    int z = centerZ + dz;
                    if (x < 0 || z < 0 || x >= config.worldWidth || z >= config.worldDepth)
                        continue;

                    int y = GetSurfaceY(x, z);
                    if (y < 0 || y >= config.worldHeight)
                        continue;

                    if (GetBlock(x, y, z) == VoxelBlockType.Water)
                        return true;

                    int above = y + 1;
                    if (above < config.worldHeight && GetBlock(x, above, z) == VoxelBlockType.Water)
                        return true;

                    int sea = Mathf.Clamp(config.seaLevel, 0, config.worldHeight - 1);
                    if (GetBlock(x, sea, z) == VoxelBlockType.Water)
                        return true;
                }
            }

            return false;
        }

        private int TryPlaceFeaturedTreeCluster(int startIndex, int centerX, int centerZ, System.Random prng, HashSet<int> used, List<Vector2> placedPositions, int remainingCapacity)
        {
            if (_featuredTreeVariants == null || _featuredTreeVariants.Length <= 1 || remainingCapacity <= 0)
                return 0;

            int sameMin = Mathf.Max(2, featuredTreeClusterSameMin);
            int sameMax = Mathf.Max(sameMin, featuredTreeClusterSameMax);
            int sameCount = prng.Next(sameMin, sameMax + 1);
            int accentCount = Mathf.Max(0, featuredTreeClusterAccentCount);
            int desired = Mathf.Min(remainingCapacity, sameCount + accentCount);
            if (desired <= 1)
                return 0;

            int mainVariantIndex = prng.Next(0, _featuredTreeVariants.Length);
            int accentVariantIndex = (mainVariantIndex + 1 + prng.Next(0, _featuredTreeVariants.Length - 1)) % _featuredTreeVariants.Length;

            var candidates = new List<(int x, int z)>(desired + 3);
            int maxOffset = Mathf.Max(1, featuredTreeClusterRadius);
            int localSafety = 0;
            int localMax = Mathf.Max(32, desired * 20);

            int centerKey = ColumnKey(centerX, centerZ);
            if (!used.Contains(centerKey) && HasTreeSpacing(centerX, centerZ, placedPositions))
                candidates.Add((centerX, centerZ));

            while (candidates.Count < desired && localSafety++ < localMax)
            {
                int ox = prng.Next(-maxOffset, maxOffset + 1);
                int oz = prng.Next(-maxOffset, maxOffset + 1);
                if (ox == 0 && oz == 0)
                    continue;

                int sx = centerX + ox;
                int sz = centerZ + oz;
                if (!TryFindFeaturedTreeColumn(sx, sz, out int px, out int pz))
                    continue;

                int key = ColumnKey(px, pz);
                if (used.Contains(key))
                    continue;
                if (!HasTreeSpacing(px, pz, placedPositions))
                    continue;

                bool duplicate = false;
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].x == px && candidates[i].z == pz)
                    {
                        duplicate = true;
                        break;
                    }
                }
                if (duplicate)
                    continue;
                if (!HasTreeSpacing(px, pz, candidates))
                    continue;

                candidates.Add((px, pz));
            }

            if (candidates.Count < Mathf.Min(3, desired))
                return 0;

            int placed = 0;
            int sameToPlace = Mathf.Min(sameCount, candidates.Count);
            int accentToPlace = Mathf.Min(accentCount, candidates.Count - sameToPlace);

            for (int i = 0; i < sameToPlace; i++)
            {
                int key = ColumnKey(candidates[i].x, candidates[i].z);
                if (!used.Add(key))
                    continue;
                PlaceFeaturedTree(startIndex + placed, candidates[i].x, candidates[i].z, prng, _featuredTreeVariants[mainVariantIndex]);
                placedPositions.Add(new Vector2(candidates[i].x, candidates[i].z));
                placed++;
            }

            for (int i = 0; i < accentToPlace; i++)
            {
                int idx = sameToPlace + i;
                if (idx >= candidates.Count)
                    break;
                int key = ColumnKey(candidates[idx].x, candidates[idx].z);
                if (!used.Add(key))
                    continue;
                PlaceFeaturedTree(startIndex + placed, candidates[idx].x, candidates[idx].z, prng, _featuredTreeVariants[accentVariantIndex]);
                placedPositions.Add(new Vector2(candidates[idx].x, candidates[idx].z));
                placed++;
            }

            return placed;
        }

        private static int ColumnKey(int x, int z)
        {
            unchecked
            {
                return (x * 73856093) ^ (z * 19349663);
            }
        }

        private bool HasTreeSpacing(int x, int z, List<Vector2> placedPositions)
        {
            return HasTreeSpacing(x, z, placedPositions, featuredTreeMinSpacing);
        }

        private bool HasTreeSpacing(int x, int z, List<Vector2> placedPositions, float minSpacing)
        {
            minSpacing = Mathf.Max(0.5f, minSpacing);
            float minSq = minSpacing * minSpacing;
            var p = new Vector2(x, z);
            for (int i = 0; i < placedPositions.Count; i++)
            {
                if ((placedPositions[i] - p).sqrMagnitude < minSq)
                    return false;
            }
            return true;
        }

        private bool HasTreeSpacing(int x, int z, List<(int x, int z)> candidates)
        {
            float minSpacing = Mathf.Max(0.5f, featuredTreeMinSpacing);
            float minSq = minSpacing * minSpacing;
            var p = new Vector2(x, z);
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = new Vector2(candidates[i].x, candidates[i].z);
                if ((c - p).sqrMagnitude < minSq)
                    return false;
            }
            return true;
        }

        private void PlaceFeaturedTree(int index, int x, int z, System.Random prng, TreeVariant? forcedVariant)
        {
            PlaceFeaturedTree(index, x, z, prng, forcedVariant, featuredTreeTargetHeightRange);
        }

        private void PlaceFeaturedTree(int index, int x, int z, System.Random prng, TreeVariant? forcedVariant, Vector2 targetHeightRange)
        {
            float surfaceY = OriginWorld.y + GetSurfaceY(x, z) + 1f;
            Vector3 sample = OriginWorld + new Vector3(x + 0.5f, surfaceY + 2f, z + 0.5f);
            if (TryGetTerrainSurfaceYAtWorld(sample, out float smoothY, includeWaterSurface: false))
                surfaceY = smoothY;
            float yaw = (float)prng.NextDouble() * 360f;
            Vector3 targetPos = new Vector3(
                OriginWorld.x + x + 0.5f,
                surfaceY + featuredTreeYOffset - Mathf.Max(0f, featuredTreeRootEmbedDepth),
                OriginWorld.z + z + 0.5f);

            if (_featuredTreeVariants != null && _featuredTreeVariants.Length > 0)
            {
                var variant = forcedVariant ?? _featuredTreeVariants[prng.Next(0, _featuredTreeVariants.Length)];
                if (variant.prefab != null)
                {
                    var go = Instantiate(variant.prefab, _featuredTreesRoot.transform);
                    go.name = $"FeaturedTree_{index:000}_{variant.prefab.name}";
                    go.transform.SetPositionAndRotation(targetPos, Quaternion.Euler(0f, yaw, 0f));

                    float desiredHeight = Mathf.Lerp(
                        Mathf.Min(targetHeightRange.x, targetHeightRange.y),
                        Mathf.Max(targetHeightRange.x, targetHeightRange.y),
                        (float)prng.NextDouble());
                    float normalizeScale = desiredHeight / Mathf.Max(0.01f, variant.sourceHeight);
                    go.transform.localScale = Vector3.one * Mathf.Max(0.01f, normalizeScale);

                    SnapInstanceBottomToY(go, targetPos.y);
                    DisableInstancePhysics(go);
                    AddTreeCollider(go);
                    ApplyTreeVariantMaterial(go, variant);
                    ApplyTreeInstanceRenderSettings(go);
                    go.isStatic = true;
                    return;
                }
            }

            var fallback = new GameObject($"FeaturedTree_{index:000}");
            fallback.transform.SetParent(_featuredTreesRoot.transform, worldPositionStays: true);
            fallback.transform.position = targetPos;
            fallback.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            float s = Mathf.Lerp(featuredTreeScaleRange.x, featuredTreeScaleRange.y, (float)prng.NextDouble());
            fallback.transform.localScale = Vector3.one * Mathf.Max(0.01f, s);
            fallback.isStatic = true;

            var mf = fallback.AddComponent<MeshFilter>();
            mf.sharedMesh = treeMesh;

            var mr = fallback.AddComponent<MeshRenderer>();
            mr.sharedMaterial = treeMaterial;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;
            AddTreeCollider(fallback);
        }

        private static void DisableInstancePhysics(GameObject go)
        {
            if (go == null)
                return;

            var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < colliders.Length; i++)
                if (colliders[i] != null)
                    colliders[i].enabled = false;

            var rigidbodies = go.GetComponentsInChildren<Rigidbody>(includeInactive: true);
            for (int i = 0; i < rigidbodies.Length; i++)
                if (rigidbodies[i] != null)
                    Destroy(rigidbodies[i]);
        }

        private static void AddTreeCollider(GameObject go)
        {
            if (go == null || !TryGetRenderableBounds(go, out Bounds bounds))
                return;

            var existing = go.GetComponentsInChildren<Collider>(includeInactive: true);
            for (int i = 0; i < existing.Length; i++)
            {
                if (existing[i] != null)
                    Destroy(existing[i]);
            }

            var cc = go.AddComponent<CapsuleCollider>();
            float trunkRadius = Mathf.Clamp(Mathf.Min(bounds.size.x, bounds.size.z) * 0.055f, 0.14f, 0.30f);
            float trunkHeight = Mathf.Clamp(bounds.size.y * 0.34f, 1.5f, 2.6f);

            Vector3 localCenter = go.transform.InverseTransformPoint(new Vector3(
                bounds.center.x,
                bounds.min.y + trunkHeight * 0.5f,
                bounds.center.z));

            cc.center = localCenter;
            cc.radius = trunkRadius;
            cc.height = Mathf.Max(trunkHeight, trunkRadius * 2f + 0.05f);
            cc.direction = 1;
            cc.isTrigger = false;
        }

        private static void ApplyTreeInstanceRenderSettings(GameObject go)
        {
            if (go == null)
                return;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                if (renderers[i] == null)
                    continue;
                renderers[i].shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderers[i].receiveShadows = false;
            }
        }

        private static void ApplyTreeVariantMaterial(GameObject go, TreeVariant variant)
        {
            if (go == null)
                return;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;

                Material source = variant.materialOverride != null ? variant.materialOverride : r.sharedMaterial;
                if (source == null)
                    continue;

                var mat = new Material(source) { name = $"{source.name}_Instance" };
                mat.enableInstancing = true;
                if (variant.textureOverride != null)
                {
                    if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", variant.textureOverride);
                    if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", variant.textureOverride);
                }
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
                r.sharedMaterial = mat;
            }
        }

        private static bool TryGetRenderableBounds(GameObject go, out Bounds bounds)
        {
            bounds = default;
            if (go == null)
                return false;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool hasAny = false;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null)
                    continue;
                if (!hasAny)
                {
                    bounds = r.bounds;
                    hasAny = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return hasAny;
        }

        private static void SnapInstanceBottomToY(GameObject go, float targetY)
        {
            if (!TryGetRenderableBounds(go, out Bounds bounds))
                return;

            float dy = targetY - bounds.min.y;
            if (Mathf.Abs(dy) > 0.0005f)
                go.transform.position += Vector3.up * dy;
        }

        private void PruneRockTreeOverlaps()
        {
            if (_featuredTreesRoot == null || _chunkFloraProps.Count == 0)
                return;

            float clearRadius = Mathf.Max(0.5f, featuredTreeRockClearRadius);
            float baseSq = clearRadius * clearRadius;
            var treePositions = new List<Vector2>(_featuredTreesRoot.transform.childCount);

            for (int i = 0; i < _featuredTreesRoot.transform.childCount; i++)
            {
                var child = _featuredTreesRoot.transform.GetChild(i);
                if (child == null)
                    continue;
                treePositions.Add(new Vector2(child.position.x, child.position.z));
            }

            if (treePositions.Count == 0)
                return;

            foreach (var kv in _chunkFloraProps)
            {
                var flora = kv.Value;
                if (flora == null)
                    continue;

                flora.RemoveSpawnedObjects(go =>
                {
                    if (go == null || !go.name.StartsWith("StylizedRock_", StringComparison.Ordinal))
                        return false;

                    float thresholdSq = baseSq;
                    if (TryGetRenderableBounds(go, out Bounds rockBounds))
                    {
                        float rockRadius = Mathf.Max(rockBounds.extents.x, rockBounds.extents.z);
                        float threshold = clearRadius + rockRadius * 0.45f;
                        thresholdSq = threshold * threshold;
                    }

                    Vector2 rockPos = new Vector2(go.transform.position.x, go.transform.position.z);
                    for (int i = 0; i < treePositions.Count; i++)
                    {
                        if ((treePositions[i] - rockPos).sqrMagnitude < thresholdSq)
                            return true;
                    }

                    return false;
                });
            }
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

        public bool IsFrozenWaterColumnAtWorld(Vector3 world)
        {
            return IsWinterSurfaceFrozen && IsWaterColumnAtWorld(world);
        }

        public bool TryGetVisibleWaterSurfaceYAtWorld(Vector3 world, out float surfaceY)
        {
            surfaceY = 0f;
            if (config == null || !IsWaterColumnAtWorld(world))
                return false;

            if (TryGetFrozenWaterSurfaceYAtWorld(world, out surfaceY))
                return true;

            surfaceY = OriginWorld.y + Mathf.Clamp(
                config.seaLevel + 1f + lowPolyWaterYOffset - Mathf.Max(0f, lowPolyWaterEmbedDepth),
                0f,
                config.worldHeight + 8f);
            return true;
        }

        public bool TryGetFrozenWaterSurfaceYAtWorld(Vector3 world, out float surfaceY)
        {
            surfaceY = 0f;
            if (config == null || !IsFrozenWaterColumnAtWorld(world))
                return false;

            surfaceY = OriginWorld.y + Mathf.Clamp(
                config.seaLevel + 1f + lowPolyWaterYOffset + Mathf.Max(0.001f, winterOverlayHeightOffset) + 0.015f - Mathf.Max(0f, lowPolyWaterEmbedDepth + winterIceExtraEmbed),
                0f,
                config.worldHeight + 8f);
            return true;
        }

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
            RefreshWaterSurfaceVisibility();
        }

        private void RefreshWaterSurfaceVisibility()
        {
            bool effectiveVisible = _waterSurfaceVisible && !IsWinterSurfaceFrozen;
            if (_lowPolyWaterRenderer != null)
                _lowPolyWaterRenderer.enabled = effectiveVisible;
            foreach (var kv in _chunkGOs)
            {
                if (kv.Value == null)
                    continue;
                var mr = kv.Value.GetComponent<MeshRenderer>();
                if (mr == null)
                    continue;
                ApplyWaterSurfaceVisibilityToChunk(kv.Key, mr, effectiveVisible);
            }
        }

        private void ApplyWaterSurfaceVisibilityToChunk(Vector2Int cc, MeshRenderer mr, bool effectiveVisible)
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

                var desired = effectiveVisible ? waterMat : (hiddenMat ??= EnsureHiddenWaterMaterial());
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
