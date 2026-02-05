using UnityEngine;

namespace SCoL.Voxels
{
    public class VoxelChunk
    {
        public readonly int ChunkSize;
        public readonly int Height;
        public readonly Vector2Int Coord; // chunk coord (cx, cz)

        // blocks[x,y,z]
        private readonly VoxelBlockType[,,] _blocks;

        public VoxelChunk(Vector2Int coord, int chunkSize, int height)
        {
            Coord = coord;
            ChunkSize = chunkSize;
            Height = height;
            _blocks = new VoxelBlockType[chunkSize, height, chunkSize];
        }

        public VoxelBlockType Get(int lx, int y, int lz)
        {
            if (lx < 0 || lz < 0 || y < 0 || lx >= ChunkSize || lz >= ChunkSize || y >= Height)
                return VoxelBlockType.Air;
            return _blocks[lx, y, lz];
        }

        public void Set(int lx, int y, int lz, VoxelBlockType t)
        {
            if (lx < 0 || lz < 0 || y < 0 || lx >= ChunkSize || lz >= ChunkSize || y >= Height)
                return;
            _blocks[lx, y, lz] = t;
        }
    }
}
