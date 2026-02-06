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
        public Material grassMaterial;

        [Header("Distribution")]
        [Range(0f, 1f)] public float density = 0.20f; // chance per grass column
        public int maxPerChunk = 256;
        public Vector2 randomOffsetXZ = new Vector2(0.35f, 0.35f);
        public Vector2 scaleRange = new Vector2(0.6f, 1.2f);

        private readonly List<Matrix4x4> _matrices = new();

        public void Rebuild(int seed)
        {
            _matrices.Clear();
            if (world == null || world.Config == null) return;
            if (grassMesh == null || grassMaterial == null) return;

            int cs = world.Config.chunkSize;
            int baseX = chunkCoord.x * cs;
            int baseZ = chunkCoord.y * cs;

            var rng = new System.Random(Hash(seed, chunkCoord.x, chunkCoord.y));

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

                float ox = (float)(rng.NextDouble() * 2.0 - 1.0) * randomOffsetXZ.x;
                float oz = (float)(rng.NextDouble() * 2.0 - 1.0) * randomOffsetXZ.y;

                float yaw = (float)rng.NextDouble() * 360f;
                float s = Mathf.Lerp(scaleRange.x, scaleRange.y, (float)rng.NextDouble());

                Vector3 pos = world.OriginWorld + new Vector3(x + 0.5f + ox, y + 1.0f, z + 0.5f + oz);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                var scale = Vector3.one * s;

                _matrices.Add(Matrix4x4.TRS(pos, rot, scale));
                if (_matrices.Count >= maxPerChunk)
                    return;
            }
        }

        private void LateUpdate()
        {
            if (grassMesh == null || grassMaterial == null) return;
            if (_matrices.Count == 0) return;

            // Draw in batches of 1023 (Unity limit per call)
            int i = 0;
            while (i < _matrices.Count)
            {
                int n = Mathf.Min(1023, _matrices.Count - i);
                Graphics.DrawMeshInstanced(grassMesh, 0, grassMaterial, _matrices.GetRange(i, n), null,
                    UnityEngine.Rendering.ShadowCastingMode.Off, receiveShadows: false, layer: gameObject.layer);
                i += n;
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
