using UnityEngine;

namespace SCoL.Voxels
{
    [CreateAssetMenu(menuName = "SCoL/Voxels/World Config", fileName = "VoxelWorldConfig")]
    public class VoxelWorldConfig : ScriptableObject
    {
        [Header("World Size")]
        [Min(1)] public int worldWidth = 100;
        [Min(1)] public int worldDepth = 100;
        [Min(16)] public int worldHeight = 40;

        [Header("Chunk")]
        [Min(4)] public int chunkSize = 16;

        [Header("Terrain")]
        [Min(0)] public int seaLevel = 18;
        [Min(1)] public int baseHeight = 20;
        [Min(1)] public int heightAmplitude = 8;

        [Tooltip("Base noise scale. Smaller = larger features.")]
        [Min(0.001f)] public float noiseScale = 0.035f;

        [Header("Seed")]
        public bool useFixedSeed = true;
        public int seed = 12345;

        [Header("Streaming / Culling")]
        public bool generateColliders = true;

        [Tooltip("How far (in chunks) to keep renderers enabled around the camera.")]
        [Min(1)] public int renderDistanceChunks = 6;

        [Tooltip("How far (in chunks) to keep colliders enabled around the camera.")]
        [Min(0)] public int colliderDistanceChunks = 3;

        [Tooltip("If true, disable chunk GameObjects outside camera view frustum (extra safety on top of Unity frustum culling).")]
        public bool frustumCullChunks = true;

        [Tooltip("Seconds between streaming updates.")]
        [Min(0.02f)] public float streamingUpdateSeconds = 0.25f;
    }
}
