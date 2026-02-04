using System;
using UnityEngine;

namespace SCoL
{
    public class SCoLRuntime : MonoBehaviour
    {
        public SCoLConfig Config { get; private set; }
        public EcosystemGrid Grid { get; private set; }

        public Season CurrentSeason { get; private set; } = Season.Summer;
        public WeatherType CurrentWeather { get; private set; } = WeatherType.Clear;

        private float _tickTimer;
        private float _seasonTimer;

        private EcosystemRenderer _renderer;
        private Transform _renderRoot;

        public GridViewMode ViewMode
        {
            get => _renderer != null ? _renderer.ViewMode : GridViewMode.Stage;
            set
            {
                if (_renderer != null) _renderer.ViewMode = value;
            }
        }

        public bool OverlayFire
        {
            get => _renderer != null && _renderer.OverlayFire;
            set
            {
                if (_renderer != null) _renderer.OverlayFire = value;
            }
        }

        public void ForceRender()
        {
            _renderer?.Render(Grid);
        }

        private System.Random _rng;

        private void EnsureHUD()
        {
            // Prefer the uGUI HUD (works in desktop + VR). If missing, spawn it.
            if (FindFirstObjectByType<SCoL.Visualization.SCoLHUD>() == null)
            {
                var go = new GameObject("SCoL_HUD");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.AddComponent<SCoL.Visualization.SCoLHUD>();
                return;
            }

            // Legacy fallback: only spawn OnGUI HUD if uGUI HUD is not present.
            if (FindFirstObjectByType<SCoL.Visualization.SCoLOnGUIHUD>() != null)
                return;
        }

        public void Init(SCoLConfig config, Vector3 worldCenter)
        {
            Config = config;

            // Treat the provided origin as the *center* of the world/grid.
            // EcosystemGrid expects Origin to be the bottom-left corner in world space.
            Vector3 gridOrigin = worldCenter - new Vector3(config.width * config.cellSize * 0.5f, 0f, config.height * config.cellSize * 0.5f);

            Grid = new EcosystemGrid(config.width, config.height, config.cellSize, gridOrigin);
            _rng = new System.Random(Environment.TickCount);
            _tickTimer = 0f;
            _seasonTimer = 0f;

            _renderRoot = new GameObject("SCoL_Render").transform;
            _renderRoot.SetParent(transform, worldPositionStays: true);

            // Ground plane disabled for now; the tile grid itself is used as the visible ground.

            _renderer = new EcosystemRenderer(Grid, _renderRoot);
            _renderer.Render(Grid);

            EnsureHUD();
        }

        private void Update()
        {
            if (Grid == null || Config == null) return;

            // For now, keep simulation ticking, but tools give immediate visual feedback.
            _tickTimer += Time.deltaTime;
            _seasonTimer += Time.deltaTime;

            if (_seasonTimer >= Config.seasonSeconds)
            {
                _seasonTimer = 0f;
                AdvanceSeason();
            }

            // Background simulation tick (cellular-automata style).
            if (Config.enableSimulationTick && _tickTimer >= Config.tickSeconds)
            {
                _tickTimer = 0f;
                Tick();
                _renderer.Render(Grid);
            }
        }

        private void AdvanceSeason()
        {
            CurrentSeason = (Season)(((int)CurrentSeason + 1) % 4);

            // very lightweight season baseline shifts
            Grid.ForEach((x, y, c) =>
            {
                switch (CurrentSeason)
                {
                    case Season.Summer:
                        c.Sunlight = Mathf.Clamp01(c.Sunlight + 0.05f);
                        c.Heat = Mathf.Clamp01(c.Heat + 0.04f);
                        break;
                    case Season.Autumn:
                        c.Sunlight = Mathf.Clamp01(c.Sunlight - 0.05f);
                        c.Water = Mathf.Clamp01(c.Water + 0.03f);
                        break;
                    case Season.Winter:
                        c.Heat = Mathf.Clamp01(c.Heat - 0.08f);
                        break;
                    case Season.Spring:
                        c.Water = Mathf.Clamp01(c.Water + 0.06f);
                        break;
                }
            });
        }

        private void Tick()
        {
            ChooseWeather();

            // Clone snapshot for CA-like updates
            var next = new CellState[Grid.Width * Grid.Height];
            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    next[Grid.Index(x, y)] = Grid.CloneCell(x, y);
                }
            }

            // Diffusion + shading + weather effects (based on current grid)
            ApplyDiffusion(next);
            ApplyShading(next);
            ApplyWeather(next);

            // Growth + fire (based on current grid)
            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    var cur = Grid.Get(x, y);
                    var n = next[Grid.Index(x, y)];

                    StepFire(x, y, cur, n);
                    StepGrowth(x, y, cur, n);

                    // clamp continuous vars
                    n.Water = Mathf.Clamp01(n.Water);
                    n.Sunlight = Mathf.Clamp01(n.Sunlight);
                    n.Heat = Mathf.Clamp01(n.Heat);
                    n.Durability = Mathf.Clamp01(n.Durability);
                    n.Success = Mathf.Clamp01(n.Success);
                }
            }

            // Write back
            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    Grid.CopyFrom(x, y, next[Grid.Index(x, y)]);
                }
            }
        }

        private void ChooseWeather()
        {
            // Weighted random by season (simple version)
            float r = (float)_rng.NextDouble();
            switch (CurrentSeason)
            {
                case Season.Summer:
                    CurrentWeather = r < 0.75f ? WeatherType.Clear : WeatherType.Cloudy;
                    break;
                case Season.Autumn:
                    if (r < 0.55f) CurrentWeather = WeatherType.Rain;
                    else if (r < 0.70f) CurrentWeather = WeatherType.Wind;
                    else if (r < 0.78f) CurrentWeather = WeatherType.Lightning;
                    else CurrentWeather = WeatherType.Cloudy;
                    break;
                case Season.Winter:
                    CurrentWeather = r < 0.65f ? WeatherType.Snow : WeatherType.Wind;
                    break;
                case Season.Spring:
                    CurrentWeather = r < 0.55f ? WeatherType.Rain : WeatherType.Cloudy;
                    break;
            }
        }

        private void ApplyDiffusion(CellState[] next)
        {
            int w = Grid.Width;
            int h = Grid.Height;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    var cur = Grid.Get(x, y);

                    float waterAvg = cur.Water;
                    float heatAvg = cur.Heat;
                    int count = 1;

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            if (dx == 0 && dy == 0) continue;
                            int nx = x + dx;
                            int ny = y + dy;
                            if (!Grid.InBounds(nx, ny)) continue;
                            var n = Grid.Get(nx, ny);
                            waterAvg += n.Water;
                            heatAvg += n.Heat;
                            count++;
                        }
                    }

                    waterAvg /= count;
                    heatAvg /= count;

                    var dst = next[Grid.Index(x, y)];
                    dst.Water = Mathf.Lerp(cur.Water, waterAvg, Config.waterDiffuse);
                    dst.Heat = Mathf.Lerp(cur.Heat, heatAvg, Config.heatDiffuse);
                }
            }
        }

        private void ApplyShading(CellState[] next)
        {
            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    int largeTrees = Grid.CountNeighbors(x, y, c => c.PlantStage == PlantStage.LargeTree);
                    if (largeTrees <= 0) continue;

                    float shade = Mathf.Clamp01(largeTrees * Config.shadeFromLargeTree);
                    var dst = next[Grid.Index(x, y)];
                    dst.Sunlight = Mathf.Clamp01(dst.Sunlight - shade);
                }
            }
        }

        private void ApplyWeather(CellState[] next)
        {
            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    var dst = next[Grid.Index(x, y)];

                    switch (CurrentWeather)
                    {
                        case WeatherType.Rain:
                            dst.Water += Config.rainWaterPerTick;
                            break;
                        case WeatherType.Snow:
                            dst.Heat -= Config.snowColdPerTick;
                            break;
                        case WeatherType.Cloudy:
                            dst.Sunlight -= Config.cloudySunPenalty;
                            break;
                        case WeatherType.Lightning:
                            // rare ignition
                            if (_rng.NextDouble() < 0.002)
                            {
                                dst.IsOnFire = true;
                                dst.FireFuel = Mathf.Max(dst.FireFuel, 0.8f);
                            }
                            break;
                        case WeatherType.Wind:
                            // wind: minor drying
                            dst.Water -= 0.03f;
                            break;
                    }
                }
            }
        }

        private void StepFire(int x, int y, CellState cur, CellState n)
        {
            if (!cur.IsOnFire) return;

            n.Heat += Config.fireHeatPerTick;
            n.FireFuel = Mathf.Clamp01(cur.FireFuel - Config.fireFuelBurnPerTick);

            if (n.FireFuel <= 0.001f)
            {
                n.IsOnFire = false;
                if (cur.HasPlant)
                {
                    n.PlantStage = PlantStage.Burnt;
                    n.Durability = 0.0f;
                }
                return;
            }

            // spread to neighbors
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    int ny = y + dy;
                    if (!Grid.InBounds(nx, ny)) continue;

                    var neighborCur = Grid.Get(nx, ny);
                    if (!neighborCur.HasPlant || neighborCur.IsOnFire) continue;

                    if (_rng.NextDouble() < Config.fireSpreadChance)
                    {
                        var neighborNext = n; // (we only have current cell's next; neighbor updated elsewhere)
                        // We cannot directly write to neighbor here without indexing; handled by IgniteAt in practice.
                        IgniteCell(nx, ny, fuel: 0.6f);
                    }
                }
            }
        }

        private void StepGrowth(int x, int y, CellState cur, CellState n)
        {
            // if burnt, slowly recover success
            if (cur.PlantStage == PlantStage.Burnt)
            {
                n.Success = Mathf.Clamp01(cur.Success + 0.01f);
                return;
            }

            // Basic water/sun ranges for plants
            bool waterOk = cur.Water >= 0.25f && cur.Water <= 0.85f;
            bool sunOk = cur.Sunlight >= 0.45f && cur.Sunlight <= 0.95f;
            bool heatOk = cur.Heat <= 0.85f; // too hot is bad (fire)

            int smallPlants = Grid.CountNeighbors(x, y, c => c.PlantStage == PlantStage.SmallPlant);
            int anyPlants = Grid.CountNeighbors(x, y, c => c.HasPlant);

            if (cur.PlantStage == PlantStage.Empty)
            {
                // Birth (CA): loosened from classic Life (==3) to a broader "viable neighborhood" range.
                // This makes emergence visible in sparse, player-driven planting.
                bool neighborOk = smallPlants >= 2 && smallPlants <= 4;
                bool envOk = (waterOk && sunOk) || (waterOk && heatOk) || (sunOk && heatOk);

                if (neighborOk && envOk)
                {
                    n.PlantStage = PlantStage.SmallPlant;
                    n.Durability = 1.0f;
                    n.Success = Mathf.Clamp01(cur.Success + 0.05f);
                }

                return;
            }

            if (!cur.HasPlant) return;

            // For this prototype: once planted, a tile stays planted (no death-by-environment).
            // We still penalize success/durability so conditions matter, but we don't erase the plant.
            if (cur.Durability <= 0.15f || !waterOk || !sunOk || !heatOk)
            {
                n.Durability = Mathf.Clamp01(cur.Durability - 0.02f);
                n.Success = Mathf.Clamp01(cur.Success - 0.03f);
                return;
            }

            // Growth progression: if neighborhood supports it and success is high
            float growChance = Mathf.Lerp(0.02f, 0.15f, cur.Success);

            // over-crowding penalty
            if (anyPlants >= 6) growChance *= 0.35f;

            if (_rng.NextDouble() < growChance)
            {
                switch (cur.PlantStage)
                {
                    case PlantStage.SmallPlant:
                        if (smallPlants >= 2) n.PlantStage = PlantStage.SmallTree;
                        break;
                    case PlantStage.SmallTree:
                        if (smallPlants >= 3) n.PlantStage = PlantStage.MediumTree;
                        break;
                    case PlantStage.MediumTree:
                        if (anyPlants >= 3) n.PlantStage = PlantStage.LargeTree;
                        break;
                }
            }

            // Success slowly increases when a plant survives ticks
            n.Success = Mathf.Clamp01(cur.Success + 0.01f);
        }

        // ---------- Public interaction API (call from XR interactables / UI) ----------

        public bool TryWorldToCell(Vector3 world, out int x, out int y)
        {
            x = y = 0;
            return Grid != null && Grid.TryWorldToCell(world, out x, out y);
        }

        public void PlaceSeedAt(Vector3 world)
        {
            if (!TryWorldToCell(world, out int x, out int y)) return;

            var c = Grid.Get(x, y);

            // Allow planting on empty OR burnt/scorched tiles.
            if (c.PlantStage != PlantStage.Empty && c.PlantStage != PlantStage.Burnt) return;

            // Simple: seed always succeeds (visual green immediately)
            c.PlantStage = PlantStage.SmallPlant;
            c.Durability = 1.0f;
            c.WaterVisual = 0f;

            // Ensure readable view
            ViewMode = GridViewMode.Stage;
            OverlayFire = true;

            _renderer?.Render(Grid);
        }

        public void AddWaterAt(Vector3 world, float amount = 0.25f)
        {
            if (!TryWorldToCell(world, out int x, out int y)) return;
            var cell = Grid.Get(x, y);

            // Keep sim var
            cell.Water = Mathf.Clamp01(cell.Water + amount);
            // Stronger, more readable visual
            cell.WaterVisual = Mathf.Clamp01(cell.WaterVisual + amount);

            // Ensure readable view
            ViewMode = GridViewMode.Stage;
            OverlayFire = true;

            _renderer?.Render(Grid);
        }

        public void IgniteAt(Vector3 world, float fuel = 0.8f)
        {
            if (!TryWorldToCell(world, out int x, out int y)) return;

            // Ensure readable view
            ViewMode = GridViewMode.Stage;
            OverlayFire = true;

            // Stop any previous spread
            StopAllCoroutines();
            StartCoroutine(FireSpreadSimple.Spread(this, x, y, maxDistance: 3, secondsPerStep: 1f));

            _renderer?.Render(Grid);
        }

        public void StompAt(Vector3 world, float damage = -1f)
        {
            if (!TryWorldToCell(world, out int x, out int y)) return;
            if (damage < 0f) damage = Config.stompDamage;
            var c = Grid.Get(x, y);
            c.Durability = Mathf.Clamp01(c.Durability - damage);
        }

        private void IgniteCell(int x, int y, float fuel)
        {
            var c = Grid.Get(x, y);
            c.IsOnFire = true;
            c.FireFuel = Mathf.Clamp01(Mathf.Max(c.FireFuel, fuel));
        }
    }
}
