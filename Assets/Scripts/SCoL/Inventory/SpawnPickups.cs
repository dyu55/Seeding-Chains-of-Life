using UnityEngine;

namespace SCoL.Inventory
{
    /// <summary>
    /// Spawns a few pickup spheres around the world for quick testing.
    /// </summary>
    public class SpawnPickups : MonoBehaviour
    {
        public int seedCount = 6;
        public int waterCount = 6;
        public int fireCount = 3;

        [Header("Placement")]
        public bool scatterAcrossGrid = true;

        [Tooltip("Used when scatterAcrossGrid=false.")]
        public Vector3 center = new Vector3(0f, 1.1f, 1.2f);

        [Tooltip("Used when scatterAcrossGrid=false.")]
        public float radius = 0.8f;

        [Tooltip("Vertical offset above ground/tile for spawned pickups.")]
        public float yOffset = 0.25f;

        private SCoL.SCoLRuntime _runtime;

        private bool _spawned;

        private void Start()
        {
            if (_spawned) return;
            _spawned = true;

            _runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();

            Spawn(SCoLItemType.Seed, seedCount, 0f);
            Spawn(SCoLItemType.Water, waterCount, 1.5f);
            Spawn(SCoLItemType.Fire, fireCount, 3.0f);
        }

        private void Spawn(SCoLItemType type, int count, float angleOffset)
        {
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                Vector3 pos;

                if (scatterAcrossGrid && _runtime != null && _runtime.Grid != null)
                {
                    int x = Random.Range(0, _runtime.Grid.Width);
                    int y = Random.Range(0, _runtime.Grid.Height);
                    pos = _runtime.Grid.CellCenterWorld(x, y);
                    pos.y += yOffset;
                }
                else
                {
                    float a = angleOffset + (i / (float)count) * Mathf.PI * 2f;
                    pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                }

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Pickup_{type}_{i}";
                go.transform.position = pos;
                go.transform.localScale = Vector3.one * 0.12f;

                var rb = go.AddComponent<Rigidbody>();
                rb.useGravity = true;
                rb.mass = 0.15f;

                var p = go.AddComponent<SCoLPickup>();
                p.type = type;
                p.amount = 1;
                p.ApplyVisual();

                // Make it interactable with the Starter Assets rig (grab to collect)
                go.AddComponent<SCoLXRICollectOnGrab>();
            }
        }
    }
}
