using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCoL.Voxels
{
    /// <summary>
    /// Draws instanced grass props for one chunk.
    /// Attached to the chunk GameObject so it is disabled together with streaming.
    /// </summary>
    [DisallowMultipleComponent]
    public class GrassPropChunk : MonoBehaviour
    {
        public VoxelWorld world;
        public Vector2Int chunkCoord;

        [Header("Asset")]
        public Mesh grassMesh;
        [Tooltip("Optional additional meshes to vary visuals. If empty, grassMesh is used.")]
        public Mesh[] grassMeshVariants;
        public Material grassMaterial;

        [Header("Distribution")]
        [Range(0f, 1f)] public float density = 0.20f; // chance per grass column
        public int maxPerChunk = 256;
        public bool snapToGrid = true;
        [Tooltip("If true, uses exact block-center placement with no random offset/rotation/scale.")]
        public bool strictGridPlacement = true;

        public Vector2 randomOffsetXZ = new Vector2(0.35f, 0.35f);
        public Vector2 scaleRange = new Vector2(0.6f, 1.2f);

        private readonly Dictionary<Mesh, List<Matrix4x4>> _matricesByMesh = new();

        public void Rebuild(int seed)
        {
            _matricesByMesh.Clear();
            if (world == null || world.Config == null) return;
            if (grassMaterial == null) return;

            // Build mesh list
            var meshes = new List<Mesh>();
            if (grassMesh != null) meshes.Add(grassMesh);
            if (grassMeshVariants != null)
            {
                foreach (var m in grassMeshVariants)
                    if (m != null && !meshes.Contains(m)) meshes.Add(m);
            }
            if (meshes.Count == 0) return;

            int cs = world.Config.chunkSize;
            int baseX = chunkCoord.x * cs;
            int baseZ = chunkCoord.y * cs;

            var rng = new System.Random(Hash(seed, chunkCoord.x, chunkCoord.y));

            // Pre-create per-mesh matrix lists
            foreach (var m in meshes)
                _matricesByMesh[m] = new List<Matrix4x4>(Mathf.Min(maxPerChunk, 64));

            for (int lz = 0; lz < cs; lz++)
            for (int lx = 0; lx < cs; lx++)
            {
                int x = baseX + lx;
                int z = baseZ + lz;
                if (x < 0 || z < 0 || x >= world.Config.worldWidth || z >= world.Config.worldDepth)
                    continue;

                if (!world.IsGrassSurface(x, z))
                    continue;

                if (rng.NextDouble() > density)
                    continue;

                int y = world.GetSurfaceY(x, z);

                float ox = 0f;
                float oz = 0f;
                float yaw = 0f;
                float s = 1f;

                if (!strictGridPlacement)
                {
                    ox = (float)(rng.NextDouble() * 2.0 - 1.0) * randomOffsetXZ.x;
                    oz = (float)(rng.NextDouble() * 2.0 - 1.0) * randomOffsetXZ.y;
                    yaw = (float)rng.NextDouble() * 360f;
                    s = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble());
                }

                // Strict placement: align to voxel cell centers.
                Vector3 pos = world.OriginWorld + new Vector3(x + 0.5f + ox, y + 1.0f, z + 0.5f + oz);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var scale = Vector3.one * s;

                // Choose a mesh variant
                Mesh chosen = meshes[rng.Next(meshes.Count)];
                _matricesByMesh[chosen].Add(Matrix4x4.TRS(pos, rot, scale));

                // Respect max per chunk across all variants
                int total = 0;
                foreach (var kv in _matricesByMesh) total += kv.Value.Count;
                if (total >= maxPerChunk)
                    return;
            }
        }

        private void LateUpdate()
        {
            if (grassMaterial == null) return;
            if (_matricesByMesh.Count == 0) return;

            foreach (var kv in _matricesByMesh)
            {
                Mesh mesh = kv.Key;
                List<Matrix4x4> matrices = kv.Value;
                if (mesh == null || matrices == null || matrices.Count == 0) continue;

                // Draw in batches of 1023 (Unity limit per call)
                int i = 0;
                while (i < matrices.Count)
                {
                    int n = Mathf.Min(1023, matrices.Count - i);
                    Graphics.DrawMeshInstanced(mesh, 0, grassMaterial, matrices.GetRange(i, n), null,
                        UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows: false, layer: gameObject.layer);
                    i += n;
                }
            }
        }

        private static int Hash(int seed, int x, int z)
        {
            unchecked
            {
                int h = seed;
                h = (h * 397) ^ x;
                h = (h * 397) ^ z;
                return h;
            }
        }
    }
}
