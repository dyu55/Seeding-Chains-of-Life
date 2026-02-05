using UnityEngine;
using SCoL;

namespace SCoL.Visualization
{
    /// <summary>
    /// Renders plants (flowers) as simple cubes at the voxel surface height.
    /// This is a placeholder until you swap in real block models.
    /// </summary>
    [DisallowMultipleComponent]
    public class PlantVoxelRenderer : MonoBehaviour
    {
        public SCoLRuntime runtime;
        public SCoL.Voxels.VoxelWorld voxelWorld;

        [Header("Appearance")]
        public Color flowerColor = new Color(0.95f, 0.20f, 0.75f);
        public Vector3 flowerScale = new Vector3(0.35f, 0.35f, 0.35f);

        private GameObject[] _flowers;
        private Material _mat;

        private void Awake()
        {
            if (runtime == null) runtime = FindFirstObjectByType<SCoLRuntime>();
            if (voxelWorld == null) voxelWorld = FindFirstObjectByType<SCoL.Voxels.VoxelWorld>();

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            _mat = new Material(shader) { name = "FlowerPlaceholder" };
            _mat.color = flowerColor;
        }

        public void Ensure()
        {
            if (runtime == null || runtime.Grid == null) return;

            int n = runtime.Grid.Width * runtime.Grid.Height;
            if (_flowers != null && _flowers.Length == n) return;

            _flowers = new GameObject[n];
            for (int i = 0; i < n; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Flower_{i}";
                go.transform.SetParent(transform, worldPositionStays: true);
                go.transform.localScale = flowerScale;
                var r = go.GetComponent<Renderer>();
                if (r != null) r.sharedMaterial = _mat;
                // Don't collide; VR locomotion should collide with terrain, not with every flower.
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.SetActive(false);
                _flowers[i] = go;
            }
        }

        public void RenderNow()
        {
            if (runtime == null || runtime.Grid == null) return;
            Ensure();

            int w = runtime.Grid.Width;
            int h = runtime.Grid.Height;

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                var cell = runtime.Grid.Get(x, y);
                var go = _flowers[idx];

                bool isFlower = cell.PlantStage == SCoL.PlantStage.SmallPlant;
                if (!isFlower)
                {
                    if (go.activeSelf) go.SetActive(false);
                    continue;
                }

                if (voxelWorld != null)
                {
                    var pos = voxelWorld.ColumnTopWorld(x, y);
                    go.transform.position = pos + Vector3.up * 0.10f;
                }
                else
                {
                    // Fallback: place on 2D grid cell center
                    go.transform.position = runtime.Grid.CellCenterWorld(x, y) + Vector3.up * 0.25f;
                }

                if (!go.activeSelf) go.SetActive(true);
            }
        }
    }
}
