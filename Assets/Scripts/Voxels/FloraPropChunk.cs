using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCoL.Voxels
{
    /// <summary>
    /// Draws instanced flora props (flowers/trees/etc.) for one chunk.
    /// Attached to the chunk GameObject so it is disabled together with streaming.
    /// </summary>
    [DisallowMultipleComponent]
    public class FloraPropChunk : MonoBehaviour
    {
        [Serializable]
        public class Prop
        {
            public string name;
            public Mesh mesh;
            public Material material;

            [Range(0f, 1f)] public float density = 0.02f;
            public int maxPerChunk = 32;

            [Tooltip("Only place on grass surface blocks.")]
            public bool onlyOnGrass = true;

            [Tooltip("Do not place at/under sea level.")]
            public bool requireAboveSeaLevel = true;

            [Header("Transform")]
            public Vector2 randomOffsetXZ = new Vector2(0.35f, 0.35f);
            public Vector2 scaleRange = new Vector2(0.9f, 1.2f);

            [Header("Optional slope filter")]
            [Tooltip("If enabled, will avoid steep areas by comparing neighbor surface heights.")]
            public bool avoidSteepSlopes = false;
            [Tooltip("Max allowed neighbor height delta (in blocks) between adjacent columns.")]
            public int maxNeighborDelta = 1;
        }

        public VoxelWorld world;
        public Vector2Int chunkCoord;

        [Header("Props")]
        public Prop[] props;

        private readonly Dictionary<Mesh, List<Matrix4x4>> _matricesByMesh = new();
        private readonly Dictionary<Mesh, Material> _materialByMesh = new();

        public void Rebuild(int seed)
        {
            _matricesByMesh.Clear();
            _materialByMesh.Clear();

            if (world == null || world.Config == null) return;
            if (props == null || props.Length == 0) return;

            int cs = world.Config.chunkSize;
            int baseX = chunkCoord.x * cs;
            int baseZ = chunkCoord.y * cs;

            for (int pi = 0; pi < props.Length; pi++)
            {
                var p = props[pi];
                if (p == null || p.mesh == null || p.material == null) continue;
                if (p.density <= 0f || p.maxPerChunk <= 0) continue;

                var matrices = new List<Matrix4x4>(Mathf.Min(p.maxPerChunk, 64));
                var rng = new System.Random(Hash(seed, chunkCoord.x, chunkCoord.y, pi));

                for (int lz = 0; lz < cs; lz++)
                for (int lx = 0; lx < cs; lx++)
                {
                    int x = baseX + lx;
                    int z = baseZ + lz;
                    if (x < 0 || z < 0 || x >= world.Config.worldWidth || z >= world.Config.worldDepth)
                        continue;

                    if (p.onlyOnGrass && !world.IsGrassSurface(x, z))
                        continue;

                    int ySurface = world.GetSurfaceY(x, z);
                    if (p.requireAboveSeaLevel && ySurface <= world.Config.seaLevel)
                        continue;

                    if (p.avoidSteepSlopes && IsSteepAt(x, z, ySurface, p.maxNeighborDelta))
                        continue;

                    if (rng.NextDouble() > p.density)
                        continue;

                    float ox = (float)(rng.NextDouble() * 2.0 - 1.0) * p.randomOffsetXZ.x;
                    float oz = (float)(rng.NextDouble() * 2.0 - 1.0) * p.randomOffsetXZ.y;
                    float yaw = (float)rng.NextDouble() * 360f;
                    float s = Mathf.Lerp(p.scaleRange.x, p.scaleRange.y, (float)rng.NextDouble());

                    Vector3 pos = world.OriginWorld + new Vector3(x + 0.5f + ox, ySurface + 1.0f, z + 0.5f + oz);
                    var rot = Quaternion.Euler(0f, yaw, 0f);
                    var scale = Vector3.one * s;

                    matrices.Add(Matrix4x4.TRS(pos, rot, scale));
                    if (matrices.Count >= p.maxPerChunk)
                        break;
                }

                if (matrices.Count > 0)
                {
                    _matricesByMesh[p.mesh] = matrices;
                    _materialByMesh[p.mesh] = p.material;
                }
            }
        }

        private bool IsSteepAt(int x, int z, int y, int maxDelta)
        {
            // Compare neighbor surface heights. If too different, treat as steep.
            int yx1 = world.GetSurfaceY(x + 1, z);
            int yx0 = world.GetSurfaceY(x - 1, z);
            int yz1 = world.GetSurfaceY(x, z + 1);
            int yz0 = world.GetSurfaceY(x, z - 1);

            return Mathf.Abs(yx1 - y) > maxDelta ||
                   Mathf.Abs(yx0 - y) > maxDelta ||
                   Mathf.Abs(yz1 - y) > maxDelta ||
                   Mathf.Abs(yz0 - y) > maxDelta;
        }

        private void LateUpdate()
        {
            if (_matricesByMesh.Count == 0) return;

            foreach (var kv in _matricesByMesh)
            {
                Mesh mesh = kv.Key;
                List<Matrix4x4> matrices = kv.Value;
                if (mesh == null || matrices == null || matrices.Count == 0) continue;

                if (!_materialByMesh.TryGetValue(mesh, out var mat) || mat == null) continue;

                int i = 0;
                while (i < matrices.Count)
                {
                    int n = Mathf.Min(1023, matrices.Count - i);
                    Graphics.DrawMeshInstanced(mesh, 0, mat, matrices.GetRange(i, n), null,
                        UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows: false, layer: gameObject.layer);
                    i += n;
                }
            }
        }

        private static int Hash(int seed, int x, int z, int salt)
        {
            unchecked
            {
                int h = seed;
                h = (h * 397) ^ x;
                h = (h * 397) ^ z;
                h = (h * 397) ^ salt;
                return h;
            }
        }
    }
}
