using UnityEngine;

namespace SCoL
{
    public class EcosystemGrid
    {
        public readonly int Width;
        public readonly int Height;
        public readonly float CellSize;
        public readonly Vector3 Origin;

        private readonly CellState[] _cells;

        public EcosystemGrid(int width, int height, float cellSize, Vector3 origin)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            Origin = origin;
            _cells = new CellState[width * height];
            for (int i = 0; i < _cells.Length; i++)
            {
                _cells[i] = new CellState();
            }
        }

        public bool InBounds(int x, int y) => x >= 0 && y >= 0 && x < Width && y < Height;

        public int Index(int x, int y) => y * Width + x;

        public CellState Get(int x, int y) => _cells[Index(x, y)];

        public Vector3 CellCenterWorld(int x, int y)
        {
            return Origin + new Vector3((x + 0.5f) * CellSize, 0f, (y + 0.5f) * CellSize);
        }

        public bool TryWorldToCell(Vector3 world, out int x, out int y)
        {
            Vector3 local = world - Origin;
            x = Mathf.FloorToInt(local.x / CellSize);
            y = Mathf.FloorToInt(local.z / CellSize);
            return InBounds(x, y);
        }

        public int CountNeighbors(int cx, int cy, System.Func<CellState, bool> predicate)
        {
            int count = 0;
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = cx + dx;
                    int ny = cy + dy;
                    if (!InBounds(nx, ny)) continue;
                    if (predicate(Get(nx, ny))) count++;
                }
            }
            return count;
        }

        public void ForEach(System.Action<int, int, CellState> action)
        {
            for (int y = 0; y < Height; y++)
            {
                for (int x = 0; x < Width; x++)
                {
                    action(x, y, Get(x, y));
                }
            }
        }

        public CellState CloneCell(int x, int y)
        {
            var c = Get(x, y);
            return new CellState
            {
                PlantStage = c.PlantStage,
                Water = c.Water,
                Sunlight = c.Sunlight,
                Heat = c.Heat,
                Durability = c.Durability,
                Success = c.Success,
                IsOnFire = c.IsOnFire,
                FireFuel = c.FireFuel
            };
        }

        public void CopyFrom(int x, int y, CellState src)
        {
            var dst = Get(x, y);
            dst.PlantStage = src.PlantStage;
            dst.Water = src.Water;
            dst.Sunlight = src.Sunlight;
            dst.Heat = src.Heat;
            dst.Durability = src.Durability;
            dst.Success = src.Success;
            dst.IsOnFire = src.IsOnFire;
            dst.FireFuel = src.FireFuel;
        }
    }
}
