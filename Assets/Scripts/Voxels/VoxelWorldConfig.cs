using UnityEngine;

namespace SCoL.Voxels
{
    [CreateAssetMenu(menuName = "SCoL/Voxels/World Config", fileName = "VoxelWorldConfig")]
    public class VoxelWorldConfig : ScriptableObject
    {
        [Header("World Size")]
        [Min(1)] public int worldWidth = 100;
        [Min(1)] public int worldDepth = 100;
        [Min(16)] public int worldHeight = 64;

        [Header("Chunk")]
        [Min(4)] public int chunkSize = 16;

        [Header("Terrain")]
        [Min(0)] public int seaLevel = 20;
        [Min(1)] public int baseHeight = 18;
        [Min(1)] public int heightAmplitude = 18;

        [Tooltip("Base noise scale. Smaller = larger features.")]
        [Min(0.001f)] public float noiseScale = 0.035f;

        [Header("Seed")]
        public bool useFixedSeed = true;
        public int seed = 12345;

        [Header("Rendering")]
        public bool generateColliders = true;
    }
}
