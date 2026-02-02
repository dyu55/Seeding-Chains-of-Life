using UnityEngine;

namespace SCoL
{
    public class EcosystemRenderer
    {
        private readonly Transform _root;
        private readonly GameObject[] _tiles;
        private readonly int _w;
        private readonly int _h;

        public EcosystemRenderer(EcosystemGrid grid, Transform root)
        {
            _root = root;
            _w = grid.Width;
            _h = grid.Height;
            _tiles = new GameObject[_w * _h];

            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    int idx = y * _w + x;
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = $"Cell_{x}_{y}";
                    go.transform.SetParent(_root, worldPositionStays: false);
                    go.transform.position = grid.CellCenterWorld(x, y);

                    float size = grid.CellSize;
                    go.transform.localScale = new Vector3(size * 0.95f, 0.05f, size * 0.95f);

                    // Remove collider (we’ll raycast against a separate ground if needed)
                    Object.Destroy(go.GetComponent<Collider>());

                    _tiles[idx] = go;
                }
            }
        }

        public void Render(EcosystemGrid grid)
        {
            grid.ForEach((x, y, c) =>
            {
                int idx = y * _w + x;
                var go = _tiles[idx];

                // Height + color as immediate feedback
                float h = 0.05f;
                Color col = new Color(0.25f, 0.2f, 0.15f); // soil

                switch (c.PlantStage)
                {
                    case PlantStage.Empty:
                        col = Color.Lerp(col, new Color(0.2f, 0.25f, 0.2f), 0.10f);
                        break;
                    case PlantStage.SmallPlant:
                        h = 0.10f;
                        col = new Color(0.25f, 0.65f, 0.25f);
                        break;
                    case PlantStage.SmallTree:
                        h = 0.16f;
                        col = new Color(0.20f, 0.55f, 0.22f);
                        break;
                    case PlantStage.MediumTree:
                        h = 0.22f;
                        col = new Color(0.15f, 0.45f, 0.20f);
                        break;
                    case PlantStage.LargeTree:
                        h = 0.28f;
                        col = new Color(0.10f, 0.35f, 0.18f);
                        break;
                    case PlantStage.Burnt:
                        h = 0.08f;
                        col = new Color(0.1f, 0.1f, 0.1f);
                        break;
                }

                if (c.IsOnFire)
                {
                    col = Color.Lerp(col, new Color(0.9f, 0.35f, 0.05f), 0.75f);
                    h += 0.04f;
                }

                // Water tint
                col = Color.Lerp(col, new Color(0.05f, 0.25f, 0.6f), Mathf.Clamp01(c.Water) * 0.25f);

                var t = go.transform;
                Vector3 s = t.localScale;
                s.y = h;
                t.localScale = s;

                // keep bottom on ground
                Vector3 p = t.position;
                p.y = h * 0.5f;
                t.position = p;

                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    r.sharedMaterial = null; // use default
                    r.material.color = col;
                }
            });
        }
    }
}
