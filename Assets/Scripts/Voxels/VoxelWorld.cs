using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
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

            EnsureDefaultMaterials();
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
        }

        private void EnsureDefaultMaterials()
        {
            // Create simple materials if none provided.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            TryApplyVoxBoxTerrainMaterials(shader);

            if (grassMat == null) grassMat = new Material(shader) { name = "Voxel_Grass" };
            grassMat.enableInstancing = true;
            ApplyGrassFaceAtlasOrFallback(grassMat);

            if (dirtMat == null) dirtMat = new Material(shader) { name = "Voxel_Dirt" };
            dirtMat.enableInstancing = true;
            dirtMat.color = new Color(0.45f, 0.32f, 0.22f);
            ApplyTextureFromResourcesIfAvailable(dirtMat, "Voxels/s1");

            if (stoneMat == null) stoneMat = new Material(shader) { name = "Voxel_Stone" };
            stoneMat.enableInstancing = true;
            stoneMat.color = new Color(0.55f, 0.55f, 0.60f);
            ApplyTextureFromResourcesIfAvailable(stoneMat, "Voxels/s1");

            if (useOriginalWaterMaterial && (overrideAssignedTerrainMaterials || waterMat == null))
                waterMat = null;

            if (waterMat == null) waterMat = new Material(shader) { name = "Voxel_Water" };
            waterMat.enableInstancing = true;
            waterMat.color = new Color(0.18f, 0.35f, 0.85f, 0.85f);
            ApplyTextureFromResourcesIfAvailable(waterMat, "Voxels/w1");

            if (showBoundaryWalls && boundaryWallMaterial == null)
            {
                boundaryWallMaterial = new Material(shader) { name = "Voxel_BoundaryWall" };
                boundaryWallMaterial.enableInstancing = true;
                boundaryWallMaterial.color = new Color(0.85f, 0.25f, 0.20f, 0.30f);
            }
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
                var mesh = UnityEditor.AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Models/Modeling/_Incoming/Tree 1 Growth/Tree Growth.obj");
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

        public void GenerateAll()
        {
            ClearWorldObjects();
            _chunks.Clear();

            int cs = config.chunkSize;
            int chunksX = Mathf.CeilToInt(config.worldWidth / (float)cs);
            int chunksZ = Mathf.CeilToInt(config.worldDepth / (float)cs);

            for (int cz = 0; cz < chunksZ; cz++)
            for (int cx = 0; cx < chunksX; cx++)
            {
                var cc = new Vector2Int(cx, cz);
                var chunk = new VoxelChunk(cc, cs, config.worldHeight);
                _chunks[cc] = chunk;

                FillChunkTerrain(chunk);
                BuildChunkGO(cc);
            }

            BuildWorldBoundary();
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
            if (_boundaryRoot != null)
                Destroy(_boundaryRoot);
            _boundaryRoot = null;
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
            int h = config.baseHeight + Mathf.RoundToInt((n - 0.5f) * 2f * config.heightAmplitude);
            return Mathf.Clamp(h, 1, config.worldHeight - 2);
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
                if (treeMesh != null && treeMaterial != null)
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
                if (_chunkColliders.TryGetValue(cc, out var mc) && mc != null)
                {
                    if (mc.enabled != withinCollider)
                        mc.enabled = withinCollider;
                }
            }
        }
    }
}
