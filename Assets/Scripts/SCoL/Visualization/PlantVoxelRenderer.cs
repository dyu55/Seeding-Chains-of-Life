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

        // Sparse pool: only create flower objects for active flower cells.
        private readonly System.Collections.Generic.Dictionary<int, GameObject> _active = new();
        private readonly System.Collections.Generic.Stack<GameObject> _pool = new();
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

        private GameObject Rent()
        {
            if (_pool.Count > 0)
            {
                var go = _pool.Pop();
                go.SetActive(true);
                return go;
            }

            var n = GameObject.CreatePrimitive(PrimitiveType.Cube);
            n.transform.SetParent(transform, worldPositionStays: true);
            n.transform.localScale = flowerScale;
            var r = n.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = _mat;
            // Don't collide; VR locomotion should collide with terrain, not with every flower.
            var col = n.GetComponent<Collider>();
            if (col != null) Destroy(col);
            return n;
        }

        private void Return(GameObject go)
        {
            if (go == null) return;
            go.SetActive(false);
            _pool.Push(go);
        }

        public void RenderNow()
        {
            if (runtime == null || runtime.Grid == null) return;

            int w = runtime.Grid.Width;
            int h = runtime.Grid.Height;

            // Mark all currently active as potentially removable.
            // We'll remove ones that are no longer flowers.
            var toRemove = new System.Collections.Generic.List<int>();
            foreach (var kv in _active)
                toRemove.Add(kv.Key);

            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int idx = y * w + x;
                var cell = runtime.Grid.Get(x, y);
                bool isFlower = cell.PlantStage == SCoL.PlantStage.SmallPlant;

                if (!isFlower)
                    continue;

                // It's a flower: ensure it exists.
                if (!_active.TryGetValue(idx, out var go) || go == null)
                {
                    go = Rent();
                    go.name = $"Flower_{x}_{y}";
                    _active[idx] = go;
                }

                // Remove from removal set
                toRemove.Remove(idx);

                if (voxelWorld != null)
                {
                    var pos = voxelWorld.ColumnTopWorld(x, y);
                    go.transform.position = pos + Vector3.up * 0.10f;
                }
                else
                {
                    go.transform.position = runtime.Grid.CellCenterWorld(x, y) + Vector3.up * 0.25f;
                }
            }

            // Return inactive flowers to pool.
            foreach (var idx in toRemove)
            {
                if (_active.TryGetValue(idx, out var go) && go != null)
                    Return(go);
                _active.Remove(idx);
            }
        }
    }
}
