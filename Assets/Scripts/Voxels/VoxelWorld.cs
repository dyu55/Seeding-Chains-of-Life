using System;
using System.Collections.Generic;
using UnityEngine;

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

        [Tooltip("If true, world (0,0,0) is placed at this transform position.")]
        public bool useTransformAsOrigin = true;

        private System.Random _rng;
        private Vector2 _noiseOffset;

        private readonly Dictionary<Vector2Int, VoxelChunk> _chunks = new();
        private readonly Dictionary<Vector2Int, GameObject> _chunkGOs = new();
        private readonly Dictionary<Vector2Int, VoxelBlockType[]> _chunkSubmeshOrder = new();

        public Vector3 OriginWorld => useTransformAsOrigin ? transform.position : Vector3.zero;

        public void InitIfNeeded()
        {
            if (config == null)
            {
                Debug.LogWarning("VoxelWorld: missing config (VoxelWorldConfig). Using defaults.");
                config = ScriptableObject.CreateInstance<VoxelWorldConfig>();
            }

            int seed = config.useFixedSeed ? config.seed : Environment.TickCount;
            _rng = new System.Random(seed);
            _noiseOffset = new Vector2(_rng.Next(-100000, 100000), _rng.Next(-100000, 100000));

            EnsureDefaultMaterials();
            GenerateAll();
        }

        private void Awake()
        {
            InitIfNeeded();
        }

        private void EnsureDefaultMaterials()
        {
            // Create simple materials if none provided.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            if (grassMat == null) grassMat = new Material(shader) { name = "Voxel_Grass" };
            grassMat.enableInstancing = true;
            ApplyGrassTextureIfAvailable(grassMat);

            if (dirtMat == null) dirtMat = new Material(shader) { name = "Voxel_Dirt" };
            dirtMat.enableInstancing = true;
            dirtMat.color = new Color(0.45f, 0.32f, 0.22f);

            if (stoneMat == null) stoneMat = new Material(shader) { name = "Voxel_Stone" };
            stoneMat.enableInstancing = true;
            stoneMat.color = new Color(0.55f, 0.55f, 0.60f);

            if (waterMat == null) waterMat = new Material(shader) { name = "Voxel_Water" };
            waterMat.enableInstancing = true;
            waterMat.color = new Color(0.18f, 0.35f, 0.85f, 0.85f);
        }

        private static void ApplyGrassTextureIfAvailable(Material m)
        {
            // If a user assigned a material in inspector, don't override it.
            if (m == null) return;

            // Try load from Resources so this works in builds.
            // File: Assets/Resources/Voxels/grass_basecolor.jpg
            var tex = Resources.Load<Texture2D>("Voxels/grass_basecolor");
            if (tex == null)
            {
                // fallback: readable green
                m.color = new Color(0.25f, 0.80f, 0.25f);
                return;
            }

            // Pixel-ish look: point sampling.
            tex.filterMode = FilterMode.Point;
            tex.anisoLevel = 0;

            // URP Lit: _BaseMap; Standard: _MainTex
            if (m.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", tex);
            if (m.HasProperty("_MainTex"))
                m.SetTexture("_MainTex", tex);

            if (m.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", Color.white);
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", Color.white);
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

            var mesh = VoxelMesher.BuildChunkMesh(this, cc);
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
                mc.sharedMesh = mesh;
            }

            _chunkGOs[cc] = go;
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

                // if any neighbor is air, it's surface and will render
                if (GetBlock(wx + 1, y, wz) == VoxelBlockType.Air ||
                    GetBlock(wx - 1, y, wz) == VoxelBlockType.Air ||
                    GetBlock(wx, y + 1, wz) == VoxelBlockType.Air ||
                    GetBlock(wx, y - 1, wz) == VoxelBlockType.Air ||
                    GetBlock(wx, y, wz + 1) == VoxelBlockType.Air ||
                    GetBlock(wx, y, wz - 1) == VoxelBlockType.Air)
                {
                    used.Add(t);
                }
            }

            var list = new List<VoxelBlockType>(used);
            return list;
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
    }
}
