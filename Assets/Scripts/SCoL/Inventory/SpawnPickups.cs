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

        public Vector3 center = new Vector3(0f, 1.1f, 1.2f);
        public float radius = 0.8f;

        private bool _spawned;

        private void Start()
        {
            if (_spawned) return;
            _spawned = true;

            Spawn(SCoLItemType.Seed, seedCount, 0f);
            Spawn(SCoLItemType.Water, waterCount, 1.5f);
            Spawn(SCoLItemType.Fire, fireCount, 3.0f);
        }

        private void Spawn(SCoLItemType type, int count, float angleOffset)
        {
            if (count <= 0) return;
            for (int i = 0; i < count; i++)
            {
                float a = angleOffset + (i / (float)count) * Mathf.PI * 2f;
                Vector3 pos = center + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;

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
            }
        }
    }
}
