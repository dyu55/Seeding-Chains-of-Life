using UnityEngine;

namespace SCoL
{
    public enum GridViewMode
    {
        Stage = 0,
        Water = 1,
        Heat = 2,
        Sunlight = 3,
        Durability = 4,
        Success = 5
    }

    public class EcosystemRenderer
    {
        private readonly GameObject[] _tiles;
        private readonly int _w;
        private readonly int _h;

        public GridViewMode ViewMode { get; set; } = GridViewMode.Stage;
        public bool OverlayFire { get; set; } = true;

        public EcosystemRenderer(EcosystemGrid grid, Transform root)
        {
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
                    go.transform.SetParent(root, worldPositionStays: false);
                    go.transform.position = grid.CellCenterWorld(x, y);

                    float size = grid.CellSize;
                    go.transform.localScale = new Vector3(size * 0.95f, 0.05f, size * 0.95f);

                    // Keep collider on tiles so the ground is clearly segmented and easy to click.
                    // (If this causes issues later, we can switch back to a separate ground collider.)

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

                float h = StageHeight(c);
                Color col = ColorForCell(c);

                if (OverlayFire && c.IsOnFire)
                {
                    col = Color.Lerp(col, new Color(0.95f, 0.25f, 0.05f), 0.85f);
                    h += 0.04f;
                }

                var t = go.transform;
                Vector3 s = t.localScale;
                s.y = Mathf.Max(0.02f, h);
                t.localScale = s;

                Vector3 p = t.position;
                p.y = s.y * 0.5f;
                t.position = p;

                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    r.sharedMaterial = null;
                    r.material.color = col;
                }
            });
        }

        private static float StageHeight(CellState c)
        {
            return c.PlantStage switch
            {
                PlantStage.Empty => 0.05f,
                PlantStage.SmallPlant => 0.10f,
                PlantStage.SmallTree => 0.16f,
                PlantStage.MediumTree => 0.22f,
                PlantStage.LargeTree => 0.28f,
                PlantStage.Burnt => 0.08f,
                _ => 0.05f
            };
        }

        private Color ColorForCell(CellState c)
        {
            switch (ViewMode)
            {
                case GridViewMode.Stage:
                    return StageColor(c);
                case GridViewMode.Water:
                    return Heatmap(c.Water);
                case GridViewMode.Heat:
                    return Heatmap(c.Heat);
                case GridViewMode.Sunlight:
                    return Heatmap(c.Sunlight);
                case GridViewMode.Durability:
                    return Heatmap(c.Durability);
                case GridViewMode.Success:
                    return Heatmap(c.Success);
                default:
                    return StageColor(c);
            }
        }

        private static Color StageColor(CellState c)
        {
            Color soil = new Color(0.25f, 0.2f, 0.15f);

            Color baseCol = c.PlantStage switch
            {
                PlantStage.Empty => soil,
                PlantStage.SmallPlant => new Color(0.25f, 0.85f, 0.25f),
                PlantStage.SmallTree => new Color(0.18f, 0.70f, 0.22f),
                PlantStage.MediumTree => new Color(0.12f, 0.55f, 0.20f),
                PlantStage.LargeTree => new Color(0.08f, 0.42f, 0.18f),
                PlantStage.Burnt => new Color(0.18f, 0.08f, 0.08f),
                _ => soil
            };

            // Simple prototype: watering darkens the current color.
            float w = Mathf.Clamp01(c.WaterVisual);
            baseCol = Color.Lerp(baseCol, baseCol * 0.55f, w);

            return baseCol;
        }

        /// <summary>
        /// 0..1 -> blue -> cyan -> green -> yellow -> red
        /// </summary>
        private static Color Heatmap(float v)
        {
            v = Mathf.Clamp01(v);
            if (v < 0.25f) return Color.Lerp(new Color(0.05f, 0.10f, 0.55f), new Color(0.10f, 0.80f, 0.85f), v / 0.25f);
            if (v < 0.50f) return Color.Lerp(new Color(0.10f, 0.80f, 0.85f), new Color(0.15f, 0.75f, 0.20f), (v - 0.25f) / 0.25f);
            if (v < 0.75f) return Color.Lerp(new Color(0.15f, 0.75f, 0.20f), new Color(0.95f, 0.85f, 0.10f), (v - 0.50f) / 0.25f);
            return Color.Lerp(new Color(0.95f, 0.85f, 0.10f), new Color(0.90f, 0.15f, 0.10f), (v - 0.75f) / 0.25f);
        }
    }
}
