using System;
using UnityEngine;
using SCoL.Voxels;
using SCoL.Visualization;
using Unity.XR.CoreUtils;

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

        private VoxelWorld _voxelWorld;
        private PlantVoxelRenderer _plantRenderer;

        [Header("Rendering (Optional)")]
        [Tooltip("Render CA plant states using PlantVoxelRenderer (VoxBox prefabs / fallbacks).")]
        public bool enablePlantVoxelRenderer = true;

        [Header("Initial Ecology")]
        [Tooltip("Seed an initial set of plants so cellular automata has a starting population.")]
        public bool seedInitialPlants = true;
        [Range(0f, 0.10f)] public float initialPlantDensity = 0.02f;

        [Header("Tree Growth")]
        [Tooltip("Multiplier for promotions into tree stages. 0.33 means about 2/3 fewer new trees.")]
        [Range(0f, 1f)] public float treePromotionMultiplier = 0.33f;
        [Tooltip("Multiplier for flower/plant spread birth rate. 0.5 means half spread speed.")]
        [Range(0f, 1f)] public float flowerSpreadMultiplier = 0.5f;

        [Header("Lineage CA")]
        [Tooltip("If true, only plants seeded by player (and descendants) can spread via CA.")]
        public bool onlyPlayerSeededLineageCA = true;
        [Tooltip("Disable automatic initial plant seeding when lineage-only CA is enabled.")]
        public bool disableInitialPlantsWhenLineageOnly = true;
        [Tooltip("Extra birth-rate boost applied only when lineage-only CA is enabled.")]
        [Range(0f, 4f)] public float lineageSpreadChanceMultiplier = 0.1333f;
        [Tooltip("Minimum per-tick sprout chance for cells adjacent to lineage plants.")]
        [Range(0f, 1f)] public float lineageMinSproutChance = 0.008f;
        [Tooltip("How far (in blocks) lineage flowers can climb uphill from neighboring sources.")]
        [Range(1, 16)] public int lineageMaxClimbBlocks = 8;
        [Tooltip("Hydration bonus applied to a 3x3 neighborhood when placing a seed.")]
        [Range(0f, 0.5f)] public float lineageSeedNeighborWaterBoost = 0.12f;

        [Header("Wind CA")]
        [Tooltip("If enabled, CA spread can only move from source to target along current wind direction.")]
        public bool constrainCASpreadToWindDirection = true;
        [Tooltip("Wind direction index (0=E, 1=NE, 2=N, 3=NW, 4=W, 5=SW, 6=S, 7=SE).")]
        [Range(0, 7)] public int windDirectionIndex = 0;
        [Tooltip("If enabled, wind direction rotates over time.")]
        public bool randomizeWindDirectionOverTime = false;
        [Min(1f)] public float windDirectionChangeSeconds = 20f;

        [Header("Seasonal Growth")]
        [Tooltip("If true, cellular spread and plant growth are paused during Winter.")]
        public bool pausePlantGrowthInWinter = true;

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
            _plantRenderer?.RenderNow();
        }

        private System.Random _rng;
        private float _windDirectionTimer;
        private Vector2Int _windDirection = new Vector2Int(1, 0);
        private static readonly Vector2Int[] WindDirections8 =
        {
            new Vector2Int(1, 0),   // E
            new Vector2Int(1, 1),   // NE
            new Vector2Int(0, 1),   // N
            new Vector2Int(-1, 1),  // NW
            new Vector2Int(-1, 0),  // W
            new Vector2Int(-1, -1), // SW
            new Vector2Int(0, -1),  // S
            new Vector2Int(1, -1),  // SE
        };

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

        private void EnsureUnderwaterEffect()
        {
            if (_voxelWorld == null)
                return;

            var cams = FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < cams.Length; i++)
            {
                var cam = cams[i];
                if (cam == null) continue;
                if (!cam.CompareTag("MainCamera")) continue;

                var fx = cam.GetComponent<SCoLUnderwaterEffect>();
                if (fx == null)
                    fx = cam.gameObject.AddComponent<SCoLUnderwaterEffect>();

                fx.voxelWorld = _voxelWorld;
                fx.targetCamera = cam;
            }
        }

        public void Init(SCoLConfig config, Vector3 worldCenter)
        {
            Config = config;

            // Fallback grid setup when voxel world is unavailable.
            // EcosystemGrid expects bottom-left world origin.
            Vector3 fallbackGridOrigin = worldCenter - new Vector3(config.width * config.cellSize * 0.5f, 0f, config.height * config.cellSize * 0.5f);
            fallbackGridOrigin.y = 0f;

            int seed = config.useFixedSeed ? config.seed : Environment.TickCount;
            _rng = new System.Random(seed);
            _tickTimer = 0f;
            _seasonTimer = 0f;
            _windDirectionTimer = 0f;

            if (randomizeWindDirectionOverTime)
                SetWindDirectionIndex(_rng.Next(0, WindDirections8.Length));
            else
                SetWindDirectionIndex(windDirectionIndex);

            _renderRoot = new GameObject("SCoL_Render").transform;
            _renderRoot.SetParent(transform, worldPositionStays: true);

            // --- Voxel world (3D terrain) ---
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();
            if (_voxelWorld == null)
            {
                var wgo = new GameObject("VoxelWorld");
                wgo.transform.position = fallbackGridOrigin;
                _voxelWorld = wgo.AddComponent<VoxelWorld>();
                // user can assign a VoxelWorldConfig in-scene later; defaults are fine for prototype.
            }
            _voxelWorld.useTransformAsOrigin = true;
            _voxelWorld.InitIfNeeded();

            // Bind cellular automata to voxel columns so plants can populate the whole map.
            int gridWidth = config.width;
            int gridHeight = config.height;
            float gridCellSize = config.cellSize;
            Vector3 gridOrigin = fallbackGridOrigin;

            if (_voxelWorld != null && _voxelWorld.Config != null)
            {
                gridWidth = _voxelWorld.Config.worldWidth;
                gridHeight = _voxelWorld.Config.worldDepth;
                gridCellSize = 1f;
                gridOrigin = _voxelWorld.OriginWorld;
                gridOrigin.y = 0f;
            }

            Grid = new EcosystemGrid(gridWidth, gridHeight, gridCellSize, gridOrigin);

            bool allowInitialPlants = seedInitialPlants && !(onlyPlayerSeededLineageCA && disableInitialPlantsWhenLineageOnly);
            if (allowInitialPlants)
                SeedInitialPlants();

            // Snap XR rig to ground after voxel world exists.
            StartCoroutine(SnapRigToGroundNextFrame());

            // --- Legacy tile renderer (disable for voxel mode) ---
            if (config.enableLegacyTileRenderer)
            {
                _renderer = new EcosystemRenderer(Grid, _renderRoot);
                _renderer.Render(Grid);
            }

            // --- Plant renderer (flowers as cubes, placed on voxel surface) ---
            // Default OFF: keep the scene clean so you can populate it with imported models.
            if (enablePlantVoxelRenderer)
            {
                _plantRenderer = FindFirstObjectByType<PlantVoxelRenderer>();
                if (_plantRenderer == null)
                {
                    var pgo = new GameObject("PlantVoxelRenderer");
                    pgo.transform.SetParent(transform, worldPositionStays: true);
                    _plantRenderer = pgo.AddComponent<PlantVoxelRenderer>();
                }
                _plantRenderer.runtime = this;
                _plantRenderer.voxelWorld = _voxelWorld;
                _plantRenderer.RenderNow();
            }

            EnsureHUD();
            EnsureUnderwaterEffect();
        }

        private void Update()
        {
            if (Grid == null || Config == null) return;

            // For now, keep simulation ticking, but tools give immediate visual feedback.
            _tickTimer += Time.deltaTime;
            _seasonTimer += Time.deltaTime;
            UpdateWindDirectionState(Time.deltaTime);

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
                _renderer?.Render(Grid);
                if (enablePlantVoxelRenderer)
                    _plantRenderer?.RenderNow();
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
            bool pauseForWinter = pausePlantGrowthInWinter && CurrentSeason == Season.Winter;

            // if burnt, slowly recover success
            if (cur.PlantStage == PlantStage.Burnt)
            {
                if (pauseForWinter)
                {
                    n.PlantAgeSeconds = cur.PlantAgeSeconds;
                    n.Success = cur.Success;
                    n.IsPlayerSeedLineage = cur.IsPlayerSeedLineage;
                    return;
                }

                n.PlantAgeSeconds = 0f;
                n.Success = Mathf.Clamp01(cur.Success + 0.01f);
                n.IsPlayerSeedLineage = cur.IsPlayerSeedLineage;
                return;
            }

            // Terrain hard rule: plants cannot exist in submerged columns.
            if (cur.HasPlant && !IsPlantableColumn(x, y))
            {
                n.PlantStage = PlantStage.Empty;
                n.PlantAgeSeconds = 0f;
                n.IsOnFire = false;
                n.FireFuel = 0f;
                n.IsPlayerSeedLineage = false;
                n.Success = Mathf.Clamp01(cur.Success - 0.05f);
                return;
            }

            if (pauseForWinter)
            {
                if (!cur.HasPlant)
                {
                    n.PlantAgeSeconds = 0f;
                    n.IsPlayerSeedLineage = false;
                }
                else
                {
                    n.PlantAgeSeconds = cur.PlantAgeSeconds;
                    n.IsPlayerSeedLineage = cur.IsPlayerSeedLineage;
                }
                return;
            }

            // Basic water/sun ranges for plants
            bool waterOk = cur.Water >= 0.25f && cur.Water <= 0.85f;
            bool sunOk = cur.Sunlight >= 0.45f && cur.Sunlight <= 0.95f;
            bool heatOk = cur.Heat <= 0.85f; // too hot is bad (fire)

            int smallPlants = Grid.CountNeighbors(x, y, c => c.PlantStage == PlantStage.SmallPlant && (!onlyPlayerSeededLineageCA || c.IsPlayerSeedLineage));
            int anyPlants = Grid.CountNeighbors(x, y, c => c.HasPlant && (!onlyPlayerSeededLineageCA || c.IsPlayerSeedLineage));
            int lineagePlants = Grid.CountNeighbors(x, y, c => c.HasPlant && c.IsPlayerSeedLineage);
            int windSourcePlants = CountWindSourceNeighbors(x, y);

            if (cur.PlantStage == PlantStage.Empty)
            {
                if (!IsPlantableColumn(x, y))
                    return;
                if (onlyPlayerSeededLineageCA && lineagePlants <= 0)
                    return;
                if (constrainCASpreadToWindDirection && windSourcePlants <= 0)
                    return;

                // Birth: stochastic sprouting (less "grid-perfect" than strict Life rules).
                // We bias toward sprouting near existing plants and under decent conditions.
                if (Config.useStochasticSprouting)
                {
                    float env = 0f;
                    if (waterOk) env += 0.45f;
                    if (sunOk) env += 0.45f;
                    if (heatOk) env += 0.25f;
                    env = Mathf.Clamp01(env);
                    if (onlyPlayerSeededLineageCA && lineagePlants > 0)
                        env = Mathf.Max(env, 0.55f);
                    float minEnv = (onlyPlayerSeededLineageCA && lineagePlants > 0) ? 0.15f : 0.35f;

                    // Require at least some nearby vegetation so it doesn't random-fill the whole map.
                    if (anyPlants > 0 && env > minEnv)
                    {
                        // Voxel constraints for flowers:
                        // - only grow on grass surface
                        // - can climb up to +5 blocks relative to a neighboring flower source
                        if (_voxelWorld != null)
                        {
                            int targetH = _voxelWorld.GetSurfaceY(x, y);
                            bool hasReachableSource = false;

                            for (int dy = -1; dy <= 1 && !hasReachableSource; dy++)
                            for (int dx = -1; dx <= 1 && !hasReachableSource; dx++)
                            {
                                if (dx == 0 && dy == 0) continue;
                                int nx = x + dx;
                                int ny = y + dy;
                                if (!Grid.InBounds(nx, ny)) continue;

                                var nb = Grid.Get(nx, ny);
                                if (!nb.HasPlant) continue;
                                if (onlyPlayerSeededLineageCA && !nb.IsPlayerSeedLineage) continue;
                                if (!IsPlantableColumn(nx, ny)) continue;
                                if (!IsWindSpreadDirection(nx, ny, x, y)) continue;

                                int sourceH = _voxelWorld.GetSurfaceY(nx, ny);
                                int maxClimb = onlyPlayerSeededLineageCA ? lineageMaxClimbBlocks : 5;
                                if (targetH <= sourceH + maxClimb)
                                    hasReachableSource = true;
                            }

                            if (!hasReachableSource)
                                return;
                        }

                        // Neighborhood factor: more neighbors => higher chance, but diminishing returns.
                        float neighCount = constrainCASpreadToWindDirection ? windSourcePlants : anyPlants;
                        float neigh = Mathf.Clamp01(neighCount / 6f);
                        float chance = Config.stochasticSproutChance * env * (0.35f + 0.65f * neigh);
                        chance *= flowerSpreadMultiplier;
                        if (onlyPlayerSeededLineageCA && lineagePlants > 0)
                        {
                            chance *= lineageSpreadChanceMultiplier;
                            chance = Mathf.Max(chance, lineageMinSproutChance);
                        }
                        chance = Mathf.Clamp(chance, 0f, 0.95f);

                        if (_rng.NextDouble() < chance)
                        {
                            n.PlantStage = PlantStage.SmallPlant;
                            n.PlantAgeSeconds = 0f;
                            n.Durability = 1.0f;
                            n.IsPlayerSeedLineage = onlyPlayerSeededLineageCA || lineagePlants > 0;
                        }
                    }

                    return;
                }

                // Strict CA birth (classic Life-style)
                if (smallPlants == 3 &&
                    waterOk &&
                    sunOk &&
                    heatOk &&
                    IsPlantableColumn(x, y) &&
                    (!constrainCASpreadToWindDirection || windSourcePlants > 0) &&
                    _rng.NextDouble() < flowerSpreadMultiplier)
                {
                    n.PlantStage = PlantStage.SmallPlant;
                    n.PlantAgeSeconds = 0f;
                    n.Durability = 1.0f;
                    n.IsPlayerSeedLineage = onlyPlayerSeededLineageCA || lineagePlants > 0;
                }
                return;
            }

            if (!cur.HasPlant)
            {
                n.PlantAgeSeconds = 0f;
                n.IsPlayerSeedLineage = false;
                return;
            }

            // Lifecycle: plant disappears after a fixed lifetime (in seconds)
            if (Config.enablePlantLifecycle)
            {
                float ageNext = cur.PlantAgeSeconds + Config.tickSeconds;
                if (ageNext >= Config.plantLifetimeSeconds)
                {
                    n.PlantStage = PlantStage.Empty;
                    n.PlantAgeSeconds = 0f;
                    n.IsOnFire = false;
                    n.FireFuel = 0f;
                    n.IsPlayerSeedLineage = false;
                    return;
                }

                n.PlantAgeSeconds = ageNext;
            }
            else
            {
                n.PlantAgeSeconds = cur.PlantAgeSeconds;
            }

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
            growChance *= treePromotionMultiplier;

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

            // Terrain hard rule: only dry grass columns are valid.
            if (!IsPlantableColumn(x, y))
                return;

            // Allow planting on empty OR burnt/scorched tiles.
            if (c.PlantStage != PlantStage.Empty && c.PlantStage != PlantStage.Burnt) return;

            // Simple: seed always succeeds (visual green immediately)
            c.PlantStage = PlantStage.SmallPlant;
            c.PlantAgeSeconds = 0f;
            c.Durability = 1.0f;
            c.Water = Mathf.Max(c.Water, 0.55f);
            c.Success = Mathf.Max(c.Success, 0.75f);
            c.WaterVisual = 0f;
            c.IsPlayerSeedLineage = true;

            // Give nearby dry cells a small hydration nudge so lineage expansion is visible after seeding.
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx;
                    int ny = y + dy;
                    if (!Grid.InBounds(nx, ny))
                        continue;

                    var n = Grid.Get(nx, ny);
                    n.Water = Mathf.Clamp01(n.Water + lineageSeedNeighborWaterBoost);
                    if (n.PlantStage == PlantStage.Empty || n.PlantStage == PlantStage.Burnt)
                        n.Success = Mathf.Clamp01(Mathf.Max(n.Success, 0.58f));
                }
            }

            // Ensure readable view
            ViewMode = GridViewMode.Stage;
            OverlayFire = true;

            _renderer?.Render(Grid);
            _plantRenderer?.RenderNow();
        }

        public bool TryDestroyPlantAtCell(int x, int y)
        {
            if (Grid == null || !Grid.InBounds(x, y))
                return false;

            var c = Grid.Get(x, y);
            if (!c.HasPlant)
                return false;

            c.PlantStage = PlantStage.Empty;
            c.PlantAgeSeconds = 0f;
            c.IsOnFire = false;
            c.FireFuel = 0f;
            c.IsPlayerSeedLineage = false;
            c.Success = Mathf.Clamp01(c.Success - 0.02f);

            _renderer?.Render(Grid);
            _plantRenderer?.RenderNow();
            return true;
        }

        public bool TryDestroyPlantAtWorld(Vector3 world)
        {
            if (!TryWorldToCell(world, out int x, out int y))
                return false;
            return TryDestroyPlantAtCell(x, y);
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
            _plantRenderer?.RenderNow();
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
            _plantRenderer?.RenderNow();
        }

        public void StompAt(Vector3 world, float damage = -1f)
        {
            if (!TryWorldToCell(world, out int x, out int y)) return;
            if (damage < 0f) damage = Config.stompDamage;
            var c = Grid.Get(x, y);
            c.Durability = Mathf.Clamp01(c.Durability - damage);
            _plantRenderer?.RenderNow();
        }

        private System.Collections.IEnumerator SnapRigToGroundNextFrame()
        {
            // Wait a frame so EnsureStarterXRRig has a chance to instantiate the rig.
            yield return null;

            if (_voxelWorld == null || _voxelWorld.Config == null)
                yield break;

            var xrOrigin = FindFirstObjectByType<XROrigin>();
            if (xrOrigin == null)
            {
                var go = GameObject.Find("XR Origin (XR Rig)");
                if (go != null) xrOrigin = go.GetComponent<XROrigin>();
            }
            if (xrOrigin == null)
                yield break;

            int x = _voxelWorld.Config.worldWidth / 2;
            int z = _voxelWorld.Config.worldDepth / 2;

            // Prefer center-land spawn; fall back to center column if none is found.
            if (!TryFindNearestPlantableColumnAround(x, z, out x, out z))
            {
                x = Mathf.Clamp(x, 0, _voxelWorld.Config.worldWidth - 1);
                z = Mathf.Clamp(z, 0, _voxelWorld.Config.worldDepth - 1);
            }

            int surfaceY = _voxelWorld.GetSurfaceY(x, z);

            // XR Origin position is typically the tracking-space "floor"; camera height comes from a child offset.
            // Put the floor slightly above the surface to avoid starting inside the collider.
            // Spawn a bit higher so the CharacterController/Rigidbody has time to settle onto the collider.
            Vector3 snapped = _voxelWorld.OriginWorld + new Vector3(x + 0.5f, surfaceY + 1.75f, z + 0.5f);
            xrOrigin.transform.position = snapped;

            // Ensure the chunk under the player is active and collidable even with streaming.
            _voxelWorld.ForceEnableChunksAtWorld(snapped, renderRadiusChunks: 1, colliderRadiusChunks: 1);
        }

        private bool TryFindNearestPlantableColumnAround(int centerX, int centerZ, out int bestX, out int bestZ)
        {
            bestX = centerX;
            bestZ = centerZ;

            if (_voxelWorld == null || _voxelWorld.Config == null)
                return false;

            int w = _voxelWorld.Config.worldWidth;
            int d = _voxelWorld.Config.worldDepth;
            int maxR = Mathf.Max(w, d);
            centerX = Mathf.Clamp(centerX, 0, w - 1);
            centerZ = Mathf.Clamp(centerZ, 0, d - 1);

            if (IsPlantableColumn(centerX, centerZ))
            {
                bestX = centerX;
                bestZ = centerZ;
                return true;
            }

            for (int r = 1; r < maxR; r++)
            {
                bool found = false;
                float bestSq = float.PositiveInfinity;

                for (int dz = -r; dz <= r; dz++)
                for (int dx = -r; dx <= r; dx++)
                {
                    if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r)
                        continue;

                    int x = centerX + dx;
                    int z = centerZ + dz;
                    if (x < 0 || z < 0 || x >= w || z >= d)
                        continue;
                    if (!IsPlantableColumn(x, z))
                        continue;

                    float sq = dx * dx + dz * dz;
                    if (sq < bestSq)
                    {
                        bestSq = sq;
                        bestX = x;
                        bestZ = z;
                        found = true;
                    }
                }

                if (found)
                    return true;
            }

            return false;
        }

        private void IgniteCell(int x, int y, float fuel)
        {
            var c = Grid.Get(x, y);
            c.IsOnFire = true;
            c.FireFuel = Mathf.Clamp01(Mathf.Max(c.FireFuel, fuel));
        }

        private bool IsPlantableColumn(int x, int z)
        {
            if (_voxelWorld == null || _voxelWorld.Config == null)
                return true;

            if (!_voxelWorld.IsGrassSurface(x, z))
                return false;

            int surfaceY = _voxelWorld.GetSurfaceY(x, z);
            if (surfaceY < _voxelWorld.Config.seaLevel)
                return false;

            int aboveY = surfaceY + 1;
            if (aboveY < _voxelWorld.Config.worldHeight && _voxelWorld.GetBlock(x, aboveY, z) == VoxelBlockType.Water)
                return false;

            return true;
        }

        private void UpdateWindDirectionState(float dt)
        {
            if (!randomizeWindDirectionOverTime)
            {
                SetWindDirectionIndex(windDirectionIndex);
                return;
            }

            _windDirectionTimer += Mathf.Max(0f, dt);
            if (_windDirectionTimer < Mathf.Max(1f, windDirectionChangeSeconds))
                return;

            _windDirectionTimer = 0f;
            int next = windDirectionIndex;
            if (_rng != null)
            {
                for (int i = 0; i < 8; i++)
                {
                    int pick = _rng.Next(0, WindDirections8.Length);
                    if (pick != windDirectionIndex)
                    {
                        next = pick;
                        break;
                    }
                }
            }

            SetWindDirectionIndex(next);
        }

        private void SetWindDirectionIndex(int index)
        {
            if (WindDirections8 == null || WindDirections8.Length == 0)
                return;

            int len = WindDirections8.Length;
            index %= len;
            if (index < 0) index += len;
            windDirectionIndex = index;
            _windDirection = WindDirections8[index];
        }

        private bool IsWindSpreadDirection(int sourceX, int sourceY, int targetX, int targetY)
        {
            if (!constrainCASpreadToWindDirection)
                return true;

            int dx = targetX - sourceX;
            int dy = targetY - sourceY;
            dx = Mathf.Clamp(dx, -1, 1);
            dy = Mathf.Clamp(dy, -1, 1);
            return dx == _windDirection.x && dy == _windDirection.y;
        }

        private int CountWindSourceNeighbors(int targetX, int targetY)
        {
            int count = 0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int sx = targetX + dx;
                    int sy = targetY + dy;
                    if (!Grid.InBounds(sx, sy)) continue;

                    var src = Grid.Get(sx, sy);
                    if (!src.HasPlant) continue;
                    if (onlyPlayerSeededLineageCA && !src.IsPlayerSeedLineage) continue;
                    if (!IsWindSpreadDirection(sx, sy, targetX, targetY)) continue;
                    count++;
                }
            }

            return count;
        }

        private void SeedInitialPlants()
        {
            if (Grid == null || Config == null) return;

            bool seededAny = false;
            float density = Mathf.Clamp01(initialPlantDensity);

            for (int y = 0; y < Grid.Height; y++)
            {
                for (int x = 0; x < Grid.Width; x++)
                {
                    if (_rng.NextDouble() > density)
                        continue;

                    if (!IsPlantableColumn(x, y))
                        continue;

                    var c = Grid.Get(x, y);
                    c.PlantStage = PlantStage.SmallPlant;
                    c.PlantAgeSeconds = 0f;
                    c.Durability = 1f;
                    c.Success = Mathf.Clamp01(0.45f + (float)_rng.NextDouble() * 0.40f);
                    c.Water = Mathf.Clamp01(c.Water + 0.10f);
                    c.IsPlayerSeedLineage = false;
                    seededAny = true;
                }
            }

            if (seededAny) return;

            // Ensure at least one seed exists so stochastic CA can spread.
            int cx = Grid.Width / 2;
            int cy = Grid.Height / 2;
            int maxR = Mathf.Max(Grid.Width, Grid.Height);

            for (int r = 0; r < maxR; r++)
            {
                for (int oy = -r; oy <= r; oy++)
                {
                    for (int ox = -r; ox <= r; ox++)
                    {
                        int x = cx + ox;
                        int y = cy + oy;
                        if (!Grid.InBounds(x, y)) continue;
                        if (!IsPlantableColumn(x, y)) continue;

                        var c = Grid.Get(x, y);
                        c.PlantStage = PlantStage.SmallPlant;
                        c.PlantAgeSeconds = 0f;
                        c.Durability = 1f;
                        c.Success = 0.65f;
                        c.Water = Mathf.Clamp01(c.Water + 0.15f);
                        c.IsPlayerSeedLineage = false;
                        return;
                    }
                }
            }
        }
    }
}
