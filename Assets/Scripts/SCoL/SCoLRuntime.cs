using System;
using System.Collections.Generic;
using UnityEngine;
using SCoL.Voxels;
using SCoL.Visualization;
using SCoL.Weather;
using SCoL.Inventory;
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
        [Tooltip("Deterministic age threshold (seconds) for each tree promotion stage.")]
        [Min(0.5f)] public float secondsPerTreeStage = 10f;
        [Tooltip("If true, watering can accelerate plant growth progression.")]
        public bool waterCanAccelerateGrowth = true;
        [Tooltip("When water is applied, add this many growth-age seconds to nearby plants.")]
        [Min(0f)] public float waterGrowthAgeBoostSeconds = 5f;
        [Tooltip("When water is applied, boost plant success (0..1) to accelerate growth checks.")]
        [Range(0f, 1f)] public float waterGrowthSuccessBoost = 0.35f;
        [Tooltip("If true, watering promotes planted flowers one stage at a time (Stage1->Stage2->Stage3).")]
        public bool waterPromotesFlowerStages = false;
        [Tooltip("If true, only player-seeded lineage plants can be promoted by watering.")]
        public bool waterPromotionOnlyForPlayerLineage = true;
        [Tooltip("If true, watering stops at Stage3 (MediumTree) and will not promote to LargeTree.")]
        public bool waterPromotionClampToStage3 = true;
        [Tooltip("If true, seed-grown plants stop at final flower stage (MediumTree) and never promote to LargeTree.")]
        public bool stopSeedGrowthAtFinalFlower = true;

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
        [Tooltip("If enabled, wind weather increases CA spread chance.")]
        public bool accelerateCASpreadInWindWeather = true;
        [Tooltip("Spread multiplier applied while weather is Wind.")]
        [Range(1f, 5f)] public float windWeatherSpreadMultiplier = 1.6f;
        [Tooltip("Wind direction index (0=E, 1=NE, 2=N, 3=NW, 4=W, 5=SW, 6=S, 7=SE).")]
        [Range(0, 7)] public int windDirectionIndex = 0;
        [Tooltip("If enabled, wind direction rotates over time.")]
        public bool randomizeWindDirectionOverTime = false;
        [Min(1f)] public float windDirectionChangeSeconds = 20f;

        [Header("Seasonal Growth")]
        [Tooltip("If true, cellular spread and plant growth are paused during Winter.")]
        public bool pausePlantGrowthInWinter = true;

        [Header("Player Flower Seed Drops")]
        public bool dropSeedPickupsNearDensePlayerFlowers = true;
        [Min(2)] public int densePlayerFlowerThreshold = 3;
        [Min(1f)] public float densePlayerFlowerRadius = 4.5f;
        [Min(0.5f)] public float denseFlowerDropCheckSeconds = 2f;
        [Range(0f, 1f)] public float denseFlowerDropChance = 1f;
        [Min(1)] public int denseFlowerMaxActiveSeedDrops = 12;
        [Min(0.25f)] public float denseFlowerSeedDropSpacing = 1.6f;
        [Min(0f)] public float denseFlowerSeedDropHeight = 0.24f;

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
        private WeatherSystem _weatherSystem;
        private SeasonSkyboxController _seasonSkybox;
        private float _nextSeasonProbeAt;
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
        float _nextDenseFlowerDropAt;

        private void EnsureHUD()
        {
            // Prefer the SimpleUIKit-based runtime HUD.
            if (FindFirstObjectByType<SCoL.Visualization.SCoLUIToolkitHUD>() == null)
            {
                var go = new GameObject("SCoL_SimpleUIKitHUD");
                go.transform.SetParent(transform, worldPositionStays: false);
                go.AddComponent<SCoL.Visualization.SCoLUIToolkitHUD>();
            }

            // Disable legacy HUD layers to avoid overlap.
            var legacyHud = FindFirstObjectByType<SCoL.Visualization.SCoLHUD>();
            if (legacyHud != null)
                legacyHud.enabled = false;

            var onGuiHud = FindFirstObjectByType<SCoL.Visualization.SCoLOnGUIHUD>();
            if (onGuiHud != null)
                onGuiHud.enabled = false;

            var fpsCrosshair = FindFirstObjectByType<FPSCrosshair>();
            if (fpsCrosshair != null)
                fpsCrosshair.enabled = false;

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
            UpdateWindDirectionState(Time.deltaTime);

            if (TryGetExternalSeason(out var externalSeason))
            {
                _seasonTimer = 0f;
                if (CurrentSeason != externalSeason)
                    SetSeason(externalSeason);
            }
            else
            {
                _seasonTimer += Time.deltaTime;
                if (_seasonTimer >= Config.seasonSeconds)
                {
                    _seasonTimer = 0f;
                    AdvanceSeason();
                }
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

            if (dropSeedPickupsNearDensePlayerFlowers && Time.time >= _nextDenseFlowerDropAt)
            {
                _nextDenseFlowerDropAt = Time.time + Mathf.Max(0.5f, denseFlowerDropCheckSeconds);
                TrySpawnDenseFlowerSeedPickup();
            }
        }

        private void AdvanceSeason()
        {
            SetSeason((Season)(((int)CurrentSeason + 1) % 4));
        }

        private void SetSeason(Season season)
        {
            CurrentSeason = season;

            // very lightweight season baseline shifts
            Grid.ForEach((x, y, c) =>
            {
                switch (season)
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
            WeatherType previousWeather = CurrentWeather;
            ChooseWeather();

            if (CurrentWeather == WeatherType.Rain && previousWeather != WeatherType.Rain)
                PromoteFlowersForRain();

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
            switch (GetEffectiveSeason())
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

        private Season GetEffectiveSeason()
        {
            return TryGetExternalSeason(out var externalSeason) ? externalSeason : CurrentSeason;
        }

        private bool TryGetExternalSeason(out Season season)
        {
            season = CurrentSeason;
            if (Time.time >= _nextSeasonProbeAt)
            {
                if (_weatherSystem == null || !_weatherSystem.isActiveAndEnabled)
                    _weatherSystem = FindFirstObjectByType<WeatherSystem>();

                if (_weatherSystem != null && _weatherSystem.seasonSource != null)
                    _seasonSkybox = _weatherSystem.seasonSource;
                else if (_seasonSkybox == null || !_seasonSkybox.isActiveAndEnabled)
                    _seasonSkybox = FindFirstObjectByType<SeasonSkyboxController>();

                _nextSeasonProbeAt = Time.time + 1f;
            }

            if (_seasonSkybox == null)
                return false;

            season = _seasonSkybox.GetCurrentSeason() switch
            {
                SeasonSkyboxController.Season.Spring => Season.Spring,
                SeasonSkyboxController.Season.Summer => Season.Summer,
                SeasonSkyboxController.Season.Autumn => Season.Autumn,
                SeasonSkyboxController.Season.Winter => Season.Winter,
                _ => CurrentSeason
            };
            return true;
        }

        private bool IsWinterSeasonActive()
        {
            return GetEffectiveSeason() == Season.Winter;
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
                            // Thunder plant scorching is handled by FPS interaction (ScorchRandomPlants).
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
                    n.BurntAutoClearSeconds = 0f;
                    n.PlantHealth = 0f;
                    n.StompHits = 0;
                    n.ClearPlantPlacementOffset();
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
            bool pauseForWinter = pausePlantGrowthInWinter && IsWinterSeasonActive();
            bool isFlowerLineage = ShouldClampFlowerAtFinalStage(cur);

            // if burnt, slowly recover success
            if (cur.PlantStage == PlantStage.Burnt)
            {
                n.PlantHealth = 0f;
                n.StompHits = 0;
                n.ClearPlantPlacementOffset();
                if (cur.BurntAutoClearSeconds > 0f)
                {
                    float remain = Mathf.Max(0f, cur.BurntAutoClearSeconds - Mathf.Max(0.01f, Config.tickSeconds));
                    n.BurntAutoClearSeconds = remain;
                    n.PlantAgeSeconds = 0f;
                    n.Success = cur.Success;
                    n.IsPlayerSeedLineage = cur.IsPlayerSeedLineage;
                    n.FlowerVariantIndex = cur.FlowerVariantIndex;
                    if (remain <= 0f)
                    {
                        n.PlantStage = PlantStage.Empty;
                        n.IsOnFire = false;
                        n.FireFuel = 0f;
                        n.IsPlayerSeedLineage = false;
                        n.FlowerVariantIndex = -1;
                        n.PlantHealth = 0f;
                        n.StompHits = 0;
                        n.ClearPlantPlacementOffset();
                        n.BurntAutoClearSeconds = 0f;
                    }
                    return;
                }
                if (pauseForWinter)
                {
                    n.PlantAgeSeconds = cur.PlantAgeSeconds;
                    n.Success = cur.Success;
                    n.IsPlayerSeedLineage = cur.IsPlayerSeedLineage;
                    n.FlowerVariantIndex = cur.FlowerVariantIndex;
                    n.BurntAutoClearSeconds = 0f;
                    return;
                }

                n.PlantAgeSeconds = 0f;
                n.Success = Mathf.Clamp01(cur.Success + 0.01f);
                n.IsPlayerSeedLineage = cur.IsPlayerSeedLineage;
                n.FlowerVariantIndex = cur.FlowerVariantIndex;
                n.BurntAutoClearSeconds = 0f;
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
                n.FlowerVariantIndex = -1;
                n.BurntAutoClearSeconds = 0f;
                n.PlantHealth = 0f;
                n.StompHits = 0;
                n.ClearPlantPlacementOffset();
                n.Success = Mathf.Clamp01(cur.Success - 0.05f);
                return;
            }

            if (pauseForWinter)
            {
                if (!cur.HasPlant)
                {
                    n.PlantAgeSeconds = 0f;
                    n.IsPlayerSeedLineage = false;
                    n.FlowerVariantIndex = -1;
                    n.BurntAutoClearSeconds = 0f;
                    n.PlantHealth = 0f;
                    n.StompHits = 0;
                    n.ClearPlantPlacementOffset();
                }
                else
                {
                    n.PlantAgeSeconds = cur.PlantAgeSeconds;
                    n.IsPlayerSeedLineage = cur.IsPlayerSeedLineage;
                    n.FlowerVariantIndex = cur.FlowerVariantIndex;
                    n.BurntAutoClearSeconds = cur.BurntAutoClearSeconds;
                    n.PlantHealth = cur.PlantHealth;
                    n.StompHits = cur.StompHits;
                    n.BurntAutoClearSeconds = cur.BurntAutoClearSeconds;
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
                n.FlowerVariantIndex = -1;
                n.PlantHealth = 0f;
                n.StompHits = 0;
                n.ClearPlantPlacementOffset();

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
                        float weatherSpreadMultiplier = GetWeatherSpreadMultiplier();
                        float chance = Config.stochasticSproutChance * env * (0.35f + 0.65f * neigh);
                        chance *= flowerSpreadMultiplier;
                        chance *= weatherSpreadMultiplier;
                        if (onlyPlayerSeededLineageCA && lineagePlants > 0)
                        {
                            chance *= lineageSpreadChanceMultiplier;
                            chance = Mathf.Max(chance, lineageMinSproutChance * weatherSpreadMultiplier);
                        }
                        chance = Mathf.Clamp(chance, 0f, 0.95f);

                        if (_rng.NextDouble() < chance)
                        {
                            n.PlantStage = PlantStage.SmallPlant;
                            n.PlantAgeSeconds = 0f;
                            n.Durability = 1.0f;
                            n.IsPlayerSeedLineage = onlyPlayerSeededLineageCA || lineagePlants > 0;
                            n.FlowerVariantIndex = ResolveSpreadFlowerVariantIndex(x, y);
                            n.PlantHealth = 50f;
                            n.StompHits = 0;
                            n.ClearPlantPlacementOffset();
                        }
                    }

                    return;
                }

                // Strict CA birth (classic Life-style)
                float strictSpreadChance = Mathf.Clamp01(flowerSpreadMultiplier * GetWeatherSpreadMultiplier());
                if (smallPlants == 3 &&
                    waterOk &&
                    sunOk &&
                    heatOk &&
                    IsPlantableColumn(x, y) &&
                    (!constrainCASpreadToWindDirection || windSourcePlants > 0) &&
                    _rng.NextDouble() < strictSpreadChance)
                {
                    n.PlantStage = PlantStage.SmallPlant;
                    n.PlantAgeSeconds = 0f;
                    n.Durability = 1.0f;
                    n.IsPlayerSeedLineage = onlyPlayerSeededLineageCA || lineagePlants > 0;
                    n.FlowerVariantIndex = ResolveSpreadFlowerVariantIndex(x, y);
                    n.PlantHealth = 50f;
                    n.StompHits = 0;
                    n.ClearPlantPlacementOffset();
                }
                return;
            }

            if (!cur.HasPlant)
            {
                n.PlantAgeSeconds = 0f;
                n.IsPlayerSeedLineage = false;
                n.FlowerVariantIndex = -1;
                n.BurntAutoClearSeconds = 0f;
                n.PlantHealth = 0f;
                n.StompHits = 0;
                n.ClearPlantPlacementOffset();
                return;
            }

            // Keep the planted flower variant stable after placement.
            n.FlowerVariantIndex = cur.FlowerVariantIndex;
            n.PlantHealth = cur.PlantHealth;
            n.StompHits = cur.StompHits;

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
                    n.FlowerVariantIndex = -1;
                    n.BurntAutoClearSeconds = 0f;
                    n.PlantHealth = 0f;
                    n.StompHits = 0;
                    n.ClearPlantPlacementOffset();
                    return;
                }

                n.PlantAgeSeconds = ageNext;
            }
            else
            {
                // Keep age advancing even when lifecycle timeout is disabled,
                // so deterministic stage progression still works.
                n.PlantAgeSeconds = cur.PlantAgeSeconds + Config.tickSeconds;
            }

            // For this prototype: once planted, a tile stays planted (no death-by-environment).
            // We still penalize success/durability so conditions matter, but we don't erase the plant.
            if (cur.Durability <= 0.15f || !waterOk || !sunOk || !heatOk)
            {
                n.Durability = Mathf.Clamp01(cur.Durability - 0.02f);
                n.Success = Mathf.Clamp01(cur.Success - 0.03f);
                if (cur.IsPlayerSeedLineage)
                    PromotePlantByAge(ref n);
                return;
            }

            // Growth progression: if neighborhood supports it and success is high
            // Success slowly increases when a plant survives ticks
            n.Success = Mathf.Clamp01(cur.Success + 0.01f);

            // Flower variants advance only through explicit watering interactions.
            if (isFlowerLineage)
                return;

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
                        if (!ShouldClampFlowerAtFinalStage(cur) && !stopSeedGrowthAtFinalFlower && anyPlants >= 3)
                            n.PlantStage = PlantStage.LargeTree;
                        break;
                }
            }

            // Deterministic promotion by age applies only to non-flower vegetation.
            PromotePlantByAge(ref n);
        }

        // ---------- Public interaction API (call from XR interactables / UI) ----------

        public bool TryWorldToCell(Vector3 world, out int x, out int y)
        {
            x = y = 0;
            return Grid != null && Grid.TryWorldToCell(world, out x, out y);
        }

        private void StorePlantPlacementOffset(CellState cell, int x, int y, Vector3 world)
        {
            if (cell == null || Grid == null)
                return;

            Vector3 center = Grid.CellCenterWorld(x, y);
            float maxOffset = Mathf.Max(0f, Grid.CellSize * 0.5f - 0.05f);
            cell.PlantOffsetX = Mathf.Clamp(world.x - center.x, -maxOffset, maxOffset);
            cell.PlantOffsetZ = Mathf.Clamp(world.z - center.z, -maxOffset, maxOffset);
        }

        public void PlaceSeedAt(Vector3 world, int flowerVariantIndex)
        {
            if (IsWinterSeasonActive()) return;
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
            c.FlowerVariantIndex = flowerVariantIndex >= 0 ? flowerVariantIndex : -1;
            c.BurntAutoClearSeconds = 0f;
            c.PlantHealth = 50f;
            c.StompHits = 0;
            StorePlantPlacementOffset(c, x, y, world);

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

        /// <summary>
        /// Backward-compatible overload for UnityEvents and standard usage.
        /// </summary>
        public void PlaceSeedAt(Vector3 world)
        {
            PlaceSeedAt(world, -1);
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
            c.FlowerVariantIndex = -1;
            c.BurntAutoClearSeconds = 0f;
            c.PlantHealth = 0f;
            c.StompHits = 0;
            c.ClearPlantPlacementOffset();
            c.Success = Mathf.Clamp01(c.Success - 0.02f);

            _renderer?.Render(Grid);
            _plantRenderer?.RenderNow();
            return true;
        }

        public bool TryResetPlantToSproutAtCell(int x, int y)
        {
            if (Grid == null || !Grid.InBounds(x, y))
                return false;

            var c = Grid.Get(x, y);
            if (c == null || !c.HasPlant || c.PlantStage == PlantStage.Burnt)
                return false;

            c.PlantStage = PlantStage.SmallPlant;
            c.PlantAgeSeconds = 0f;
            c.Durability = Mathf.Max(c.Durability, 0.85f);
            c.Success = Mathf.Max(c.Success, 0.65f);
            c.IsOnFire = false;
            c.FireFuel = 0f;
            c.BurntAutoClearSeconds = 0f;

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

        public int TryDestroyPlantAroundWorld(Vector3 world, float radius = 1.25f, int maxPlants = 3)
        {
            if (Grid == null)
                return 0;
            if (!TryWorldToCell(world, out int cx, out int cy))
                return 0;

            float r = Mathf.Max(0.1f, radius);
            float cell = Mathf.Max(0.0001f, Grid.CellSize);
            int cellR = Mathf.Max(1, Mathf.CeilToInt(r / cell));
            int removed = 0;

            var candidates = new List<(int x, int y, float dSqr)>(32);
            for (int y = cy - cellR; y <= cy + cellR; y++)
            for (int x = cx - cellR; x <= cx + cellR; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                var c = Grid.Get(x, y);
                if (!c.HasPlant)
                    continue;

                Vector3 center = Grid.CellCenterWorld(x, y);
                Vector2 d = new Vector2(center.x - world.x, center.z - world.z);
                float dSqr = d.sqrMagnitude;
                if (dSqr <= r * r)
                    candidates.Add((x, y, dSqr));
            }

            if (candidates.Count == 0)
                return 0;

            candidates.Sort((a, b) => a.dSqr.CompareTo(b.dSqr));
            int limit = Mathf.Max(1, maxPlants);
            for (int i = 0; i < candidates.Count && removed < limit; i++)
            {
                var c = candidates[i];
                if (TryDestroyPlantAtCell(c.x, c.y))
                    removed++;
            }

            return removed;
        }

        public int TryStompPlantAroundWorld(Vector3 world, float radius = 1.25f, int maxPlants = 3, int hitsRequired = 3)
        {
            if (Grid == null)
                return 0;
            if (!TryWorldToCell(world, out int cx, out int cy))
                return 0;

            float r = Mathf.Max(0.1f, radius);
            float cell = Mathf.Max(0.0001f, Grid.CellSize);
            int cellR = Mathf.Max(1, Mathf.CeilToInt(r / cell));
            int removed = 0;

            var candidates = new List<(int x, int y, float dSqr)>(32);
            for (int y = cy - cellR; y <= cy + cellR; y++)
            for (int x = cx - cellR; x <= cx + cellR; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                var c = Grid.Get(x, y);
                if (!c.HasPlant)
                    continue;

                Vector3 center = Grid.CellCenterWorld(x, y);
                Vector2 d = new Vector2(center.x - world.x, center.z - world.z);
                float dSqr = d.sqrMagnitude;
                if (dSqr <= r * r)
                    candidates.Add((x, y, dSqr));
            }

            if (candidates.Count == 0)
                return 0;

            candidates.Sort((a, b) => a.dSqr.CompareTo(b.dSqr));
            int limit = Mathf.Max(1, maxPlants);
            int threshold = Mathf.Max(1, hitsRequired);
            for (int i = 0; i < candidates.Count && removed < limit; i++)
            {
                var c = candidates[i];
                var cellState = Grid.Get(c.x, c.y);
                cellState.StompHits = Mathf.Min(threshold, cellState.StompHits + 1);
                if (cellState.StompHits < threshold)
                    continue;

                if (TryDestroyPlantAtCell(c.x, c.y))
                    removed++;
            }

            if (removed == 0)
                _plantRenderer?.RenderNow();

            return removed;
        }

        public void AddWaterAt(Vector3 world, float amount = 0.25f)
        {
            if (!TryWorldToCell(world, out int x, out int y)) return;
            AddWaterAtCell(x, y, amount);
        }

        public void AddWaterAtCell(int x, int y, float amount = 0.25f)
        {
            if (Grid == null || !Grid.InBounds(x, y))
                return;
            var cell = Grid.Get(x, y);

            // Keep sim var
            cell.Water = Mathf.Clamp01(cell.Water + amount);
            // Stronger, more readable visual
            cell.WaterVisual = Mathf.Clamp01(cell.WaterVisual + amount);
            TryPromotePlantStageByWater(ref cell);
            ApplyWaterGrowthBoost(ref cell);

            // Ensure readable view
            ViewMode = GridViewMode.Stage;
            OverlayFire = true;

            _renderer?.Render(Grid);
            _plantRenderer?.RenderNow();
        }

        public int AddWaterAroundWorld(Vector3 world, float radius = 1.5f, float amount = 0.25f)
        {
            if (Grid == null)
                return 0;
            if (!TryWorldToCell(world, out int cx, out int cy))
                return 0;

            float r = Mathf.Max(0.1f, radius);
            float cell = Mathf.Max(0.0001f, Grid.CellSize);
            int cellR = Mathf.Max(1, Mathf.CeilToInt(r / cell));
            int affected = 0;

            for (int y = cy - cellR; y <= cy + cellR; y++)
            for (int x = cx - cellR; x <= cx + cellR; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                Vector3 center = Grid.CellCenterWorld(x, y);
                Vector2 d = new Vector2(center.x - world.x, center.z - world.z);
                if (d.sqrMagnitude > r * r)
                    continue;

                var dst = Grid.Get(x, y);
                dst.Water = Mathf.Clamp01(dst.Water + amount);
                dst.WaterVisual = Mathf.Clamp01(dst.WaterVisual + amount);
                TryPromotePlantStageByWater(ref dst);
                ApplyWaterGrowthBoost(ref dst);
                affected++;
            }

            if (affected > 0)
            {
                ViewMode = GridViewMode.Stage;
                OverlayFire = true;
                _renderer?.Render(Grid);
                _plantRenderer?.RenderNow();
            }

            return affected;
        }

        public void IgniteAt(Vector3 world, float fuel = 0.8f)
        {
            if (!TryWorldToCell(world, out int x, out int y)) return;

            var c = Grid.Get(x, y);
            if (c != null && c.HasPlant)
            {
                ScorchCell(x, y);
                ViewMode = GridViewMode.Stage;
                OverlayFire = true;
                _renderer?.Render(Grid);
                _plantRenderer?.RenderNow();
                return;
            }

            // Ensure readable view
            ViewMode = GridViewMode.Stage;
            OverlayFire = true;

            // Stop any previous spread
            StopAllCoroutines();
            StartCoroutine(FireSpreadSimple.Spread(this, x, y, maxDistance: 3, secondsPerStep: 1f));

            _renderer?.Render(Grid);
            _plantRenderer?.RenderNow();
        }

        public int IgniteAroundWorld(Vector3 world, float radius = 1.25f, float fuel = 0.8f)
        {
            if (Grid == null)
                return 0;
            if (!TryWorldToCell(world, out int cx, out int cy))
                return 0;

            float r = Mathf.Max(0.1f, radius);
            float cell = Mathf.Max(0.0001f, Grid.CellSize);
            int cellR = Mathf.Max(1, Mathf.CeilToInt(r / cell));
            int ignited = 0;

            for (int y = cy - cellR; y <= cy + cellR; y++)
            for (int x = cx - cellR; x <= cx + cellR; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                Vector3 center = Grid.CellCenterWorld(x, y);
                Vector2 d = new Vector2(center.x - world.x, center.z - world.z);
                if (d.sqrMagnitude > r * r)
                    continue;

                var c = Grid.Get(x, y);
                if (!c.HasPlant)
                    continue;

                if (ScorchCell(x, y))
                    ignited++;
            }

            if (ignited > 0)
            {
                ViewMode = GridViewMode.Stage;
                OverlayFire = true;
                _renderer?.Render(Grid);
                _plantRenderer?.RenderNow();
            }

            return ignited;
        }

        public int ScorchAroundWorld(Vector3 world, float radius = 1.25f, int maxPlants = 3)
        {
            if (Grid == null)
                return 0;
            if (!TryWorldToCell(world, out int cx, out int cy))
                return 0;

            float r = Mathf.Max(0.1f, radius);
            float cell = Mathf.Max(0.0001f, Grid.CellSize);
            int cellR = Mathf.Max(1, Mathf.CeilToInt(r / cell));
            int scorched = 0;

            var candidates = new List<(int x, int y, float dSqr)>(32);
            for (int y = cy - cellR; y <= cy + cellR; y++)
            for (int x = cx - cellR; x <= cx + cellR; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                var c = Grid.Get(x, y);
                if (!c.HasPlant)
                    continue;

                Vector3 center = Grid.CellCenterWorld(x, y);
                Vector2 d = new Vector2(center.x - world.x, center.z - world.z);
                float dSqr = d.sqrMagnitude;
                if (dSqr <= r * r)
                    candidates.Add((x, y, dSqr));
            }

            if (candidates.Count == 0)
                return 0;

            candidates.Sort((a, b) => a.dSqr.CompareTo(b.dSqr));
            int limit = Mathf.Max(1, maxPlants);
            for (int i = 0; i < candidates.Count && scorched < limit; i++)
            {
                var c = candidates[i];
                if (ScorchCell(c.x, c.y))
                    scorched++;
            }

            if (scorched > 0)
            {
                ViewMode = GridViewMode.Stage;
                OverlayFire = true;
                _renderer?.Render(Grid);
                _plantRenderer?.RenderNow();
            }

            return scorched;
        }

        public int ScorchPatchWorld(Vector3 world, int halfExtent = 2)
        {
            if (Grid == null)
                return 0;
            if (!TryWorldToCell(world, out int cx, out int cy))
                return 0;

            int clampedHalfExtent = Mathf.Max(0, halfExtent);
            int scorched = 0;

            for (int y = cy - clampedHalfExtent; y <= cy + clampedHalfExtent; y++)
            for (int x = cx - clampedHalfExtent; x <= cx + clampedHalfExtent; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                if (ScorchCell(x, y))
                    scorched++;
            }

            if (scorched > 0)
            {
                ViewMode = GridViewMode.Stage;
                OverlayFire = true;
                _renderer?.Render(Grid);
                _plantRenderer?.RenderNow();
            }

            return scorched;
        }

        private void ApplyWaterGrowthBoost(ref CellState c)
        {
            if (!waterCanAccelerateGrowth)
                return;

            if (!c.HasPlant || c.PlantStage == PlantStage.Burnt)
                return;

            // Flower variants should still benefit from watering until they reach the final flower stage.
            if (ShouldClampFlowerAtFinalStage(c) && c.PlantStage >= PlantStage.MediumTree)
                return;

            c.Success = Mathf.Clamp01(c.Success + Mathf.Max(0f, waterGrowthSuccessBoost));
            c.PlantAgeSeconds += Mathf.Max(0f, waterGrowthAgeBoostSeconds);
            PromotePlantByAge(ref c);
        }

        private void TryPromotePlantStageByWater(ref CellState c, bool isRainWeather = false)
        {
            if (!waterPromotesFlowerStages)
                return;
            if (!c.HasPlant || c.PlantStage == PlantStage.Burnt)
                return;
            if (!isRainWeather && CurrentSeason == Season.Winter)
                return;
            if (waterPromotionOnlyForPlayerLineage && !c.IsPlayerSeedLineage)
                return;

            switch (c.PlantStage)
            {
                case PlantStage.SmallPlant:
                    c.PlantStage = PlantStage.SmallTree;   // Stage1 -> Stage2
                    c.Success = Mathf.Clamp01(Mathf.Max(c.Success, 0.78f));
                    c.PlantAgeSeconds = Mathf.Max(c.PlantAgeSeconds, Mathf.Max(0.5f, secondsPerTreeStage));
                    break;
                case PlantStage.SmallTree:
                    c.PlantStage = PlantStage.MediumTree;  // Stage2 -> Stage3
                    c.Success = Mathf.Clamp01(Mathf.Max(c.Success, 0.86f));
                    c.PlantAgeSeconds = Mathf.Max(c.PlantAgeSeconds, Mathf.Max(0.5f, secondsPerTreeStage) * 2f);
                    break;
                case PlantStage.MediumTree:
                    if (!waterPromotionClampToStage3 && !stopSeedGrowthAtFinalFlower)
                    {
                        c.PlantStage = PlantStage.LargeTree;
                        c.Success = Mathf.Clamp01(Mathf.Max(c.Success, 0.92f));
                        c.PlantAgeSeconds = Mathf.Max(c.PlantAgeSeconds, Mathf.Max(0.5f, secondsPerTreeStage) * 3f);
                    }
                    break;
            }
        }

        private void PromoteFlowersForRain()
        {
            if (Grid == null)
                return;

            Grid.ForEach((x, y, c) =>
            {
                TryPromotePlantStageByWater(ref c, isRainWeather: true);
            });
        }

        private void PromotePlantByAge(ref CellState c)
        {
            if (!c.HasPlant || c.PlantStage == PlantStage.Burnt)
                return;

            if (ShouldClampFlowerAtFinalStage(c))
            {
                if (c.PlantStage > PlantStage.MediumTree)
                    c.PlantStage = PlantStage.MediumTree;
            }

            float step = Mathf.Max(0.5f, secondsPerTreeStage);
            if (CurrentWeather == WeatherType.Rain)
                step = Mathf.Max(0.5f, step - 5f);
            float age = Mathf.Max(0f, c.PlantAgeSeconds);

            if (age >= step * 2f && c.PlantStage < PlantStage.MediumTree)
                c.PlantStage = PlantStage.MediumTree;
            else if (age >= step && c.PlantStage < PlantStage.SmallTree)
                c.PlantStage = PlantStage.SmallTree;

            if (!ShouldClampFlowerAtFinalStage(c) && !stopSeedGrowthAtFinalFlower && age >= step * 3f)
                c.PlantStage = PlantStage.LargeTree;
            else if ((stopSeedGrowthAtFinalFlower || ShouldClampFlowerAtFinalStage(c)) &&
                     c.IsPlayerSeedLineage && c.PlantStage == PlantStage.LargeTree)
                c.PlantStage = PlantStage.MediumTree;
        }

        private static bool ShouldClampFlowerAtFinalStage(CellState c)
        {
            return c.FlowerVariantIndex >= 0;
        }

        private bool ScorchCell(int x, int y, float autoClearSeconds = 0f)
        {
            if (Grid == null || !Grid.InBounds(x, y))
                return false;

            var c = Grid.Get(x, y);
            if (!c.HasPlant || c.PlantStage == PlantStage.Burnt)
                return false;
            c.PlantStage = PlantStage.Burnt;
            c.Durability = 0f;
            c.IsOnFire = false;
            c.FireFuel = 0f;
            c.FlowerVariantIndex = -1;
            c.BurntAutoClearSeconds = Mathf.Max(0f, autoClearSeconds);
            c.PlantAgeSeconds = 0f;
            c.PlantHealth = 0f;
            c.StompHits = 0;
            c.ClearPlantPlacementOffset();
            return true;
        }

        public int ScorchRandomPlants(int minCount = 1, int maxCount = 5, float autoClearSeconds = 5f)
        {
            if (Grid == null)
                return 0;

            int min = Mathf.Max(1, minCount);
            int max = Mathf.Max(min, maxCount);
            var candidates = new List<(int x, int y)>(128);

            for (int y = 0; y < Grid.Height; y++)
            for (int x = 0; x < Grid.Width; x++)
            {
                var c = Grid.Get(x, y);
                if (c == null || !c.HasPlant || c.PlantStage == PlantStage.Burnt)
                    continue;
                candidates.Add((x, y));
            }

            if (candidates.Count == 0)
                return 0;

            int target = Mathf.Min(candidates.Count, _rng.Next(min, max + 1));
            int scorched = 0;
            for (int i = 0; i < target; i++)
            {
                int pick = i + _rng.Next(0, candidates.Count - i);
                var cell = candidates[pick];
                candidates[pick] = candidates[i];
                candidates[i] = cell;
                if (ScorchCell(cell.x, cell.y, autoClearSeconds))
                    scorched++;
            }

            if (scorched > 0)
            {
                ViewMode = GridViewMode.Stage;
                OverlayFire = true;
                _renderer?.Render(Grid);
                _plantRenderer?.RenderNow();
            }

            return scorched;
        }

        public void StompAt(Vector3 world, float damage = -1f)
        {
            if (!TryWorldToCell(world, out int x, out int y)) return;
            if (damage < 0f) damage = Config.stompDamage;
            var c = Grid.Get(x, y);
            c.Durability = Mathf.Clamp01(c.Durability - damage);
            _plantRenderer?.RenderNow();
        }

        void TrySpawnDenseFlowerSeedPickup()
        {
            if (Grid == null || _voxelWorld == null || _voxelWorld.Config == null)
                return;
            if (denseFlowerMaxActiveSeedDrops > 0 && CountActiveDenseFlowerSeedDrops() >= denseFlowerMaxActiveSeedDrops)
                return;

            int attempts = 24;
            float radius = Mathf.Max(1f, densePlayerFlowerRadius);
            int cellRadius = Mathf.Max(1, Mathf.CeilToInt(radius / Mathf.Max(0.0001f, Grid.CellSize)));
            for (int i = 0; i < attempts; i++)
            {
                int x = UnityEngine.Random.Range(0, Grid.Width);
                int y = UnityEngine.Random.Range(0, Grid.Height);
                var cell = Grid.Get(x, y);
                if (!IsDenseFlowerDropSourceCell(cell))
                    continue;

                int nearbyFlowers = CountDensePlayerFlowersAround(x, y, cellRadius, radius);
                if (nearbyFlowers < Mathf.Max(2, densePlayerFlowerThreshold))
                    continue;
                if (UnityEngine.Random.value > Mathf.Clamp01(denseFlowerDropChance))
                    return;

                if (TryFindDenseFlowerDropPoint(x, y, cellRadius, out var dropWorld))
                {
                    SpawnDenseFlowerSeedPickup(dropWorld, cell.FlowerVariantIndex);
                    return;
                }
            }
        }

        int CountDensePlayerFlowersAround(int cx, int cy, int cellRadius, float worldRadius)
        {
            int count = 0;
            float rSqr = worldRadius * worldRadius;
            Vector3 center = Grid.CellCenterWorld(cx, cy);
            for (int y = cy - cellRadius; y <= cy + cellRadius; y++)
            for (int x = cx - cellRadius; x <= cx + cellRadius; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                var cell = Grid.Get(x, y);
                if (!IsDenseFlowerDropSourceCell(cell))
                    continue;

                Vector3 candidate = Grid.CellCenterWorld(x, y);
                Vector2 d = new Vector2(candidate.x - center.x, candidate.z - center.z);
                if (d.sqrMagnitude <= rSqr)
                    count++;
            }

            return count;
        }

        bool IsDenseFlowerDropSourceCell(CellState cell)
        {
            return cell != null &&
                   cell.HasPlant &&
                   cell.IsPlayerSeedLineage &&
                   cell.FlowerVariantIndex >= 0 &&
                   cell.PlantStage >= PlantStage.MediumTree &&
                   cell.PlantStage != PlantStage.Burnt;
        }

        bool TryFindDenseFlowerDropPoint(int centerX, int centerY, int cellRadius, out Vector3 dropWorld)
        {
            dropWorld = Grid.CellCenterWorld(centerX, centerY) + Vector3.up * denseFlowerSeedDropHeight;
            int attempts = Mathf.Max(8, cellRadius * 6);
            for (int i = 0; i < attempts; i++)
            {
                int x = Mathf.Clamp(centerX + UnityEngine.Random.Range(-cellRadius, cellRadius + 1), 0, Grid.Width - 1);
                int y = Mathf.Clamp(centerY + UnityEngine.Random.Range(-cellRadius, cellRadius + 1), 0, Grid.Height - 1);
                if (!IsPlantableColumn(x, y))
                    continue;

                Vector3 candidate = _voxelWorld.ColumnTopWorld(x, y) + Vector3.up * Mathf.Max(0f, denseFlowerSeedDropHeight);
                if (HasNearbyDenseFlowerSeedPickup(candidate, denseFlowerSeedDropSpacing))
                    continue;

                dropWorld = candidate;
                return true;
            }

            return false;
        }

        void SpawnDenseFlowerSeedPickup(Vector3 worldPos, int flowerVariantIndex)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"PlayerFlowerSeedDrop_{flowerVariantIndex}_{Time.frameCount}";
            go.transform.position = worldPos;
            go.transform.localScale = new Vector3(0.26f, 0.18f, 0.26f);
            go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 90f);

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            var pickup = go.AddComponent<SCoLPickup>();
            pickup.type = SCoLItemType.Seed;
            pickup.amount = 1;
            pickup.seedVariantIndex = flowerVariantIndex;
            pickup.preserveExistingMaterials = false;
            pickup.ApplyVisual();
        }

        bool HasNearbyDenseFlowerSeedPickup(Vector3 worldPos, float radius)
        {
            float rSqr = Mathf.Max(0.1f, radius) * Mathf.Max(0.1f, radius);
            var pickups = FindObjectsByType<SCoLPickup>(FindObjectsSortMode.None);
            for (int i = 0; i < pickups.Length; i++)
            {
                var pickup = pickups[i];
                if (pickup == null || pickup.type != SCoLItemType.Seed)
                    continue;
                if (pickup.name == null || !pickup.name.StartsWith("PlayerFlowerSeedDrop_"))
                    continue;

                Vector3 d = pickup.transform.position - worldPos;
                d.y = 0f;
                if (d.sqrMagnitude <= rSqr)
                    return true;
            }

            return false;
        }

        int CountActiveDenseFlowerSeedDrops()
        {
            int count = 0;
            var pickups = FindObjectsByType<SCoLPickup>(FindObjectsSortMode.None);
            for (int i = 0; i < pickups.Length; i++)
            {
                var pickup = pickups[i];
                if (pickup == null || pickup.type != SCoLItemType.Seed)
                    continue;
                if (pickup.name != null && pickup.name.StartsWith("PlayerFlowerSeedDrop_"))
                    count++;
            }

            return count;
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
            // For VR, we don't need the 1.75f height (which was for the FPS capsule center), the XR Floor is 0.
            // Voxel tops are at surfaceY + 1.0f. Spawn at 1.1f to prevent CharacterController from clipping through.
            Vector3 snapped = _voxelWorld.OriginWorld + new Vector3(x + 0.5f, surfaceY + 1.1f, z + 0.5f);
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

        private float GetWeatherSpreadMultiplier()
        {
            if (!accelerateCASpreadInWindWeather)
                return 1f;
            if (CurrentWeather != WeatherType.Wind)
                return 1f;
            return Mathf.Max(1f, windWeatherSpreadMultiplier);
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
                    c.FlowerVariantIndex = -1;
                    c.PlantHealth = 50f;
                    c.ClearPlantPlacementOffset();
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
                        c.FlowerVariantIndex = -1;
                        c.PlantHealth = 50f;
                        c.ClearPlantPlacementOffset();
                        return;
                    }
                }
            }
        }

        private int ResolveSpreadFlowerVariantIndex(int x, int y)
        {
            if (Grid == null)
                return -1;

            int bestVariant = -1;
            int bestCount = 0;

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dy == 0) continue;
                    int nx = x + dx;
                    int ny = y + dy;
                    if (!Grid.InBounds(nx, ny)) continue;

                    var nb = Grid.Get(nx, ny);
                    if (nb == null || !nb.HasPlant) continue;
                    if (nb.FlowerVariantIndex < 0) continue;
                    if (onlyPlayerSeededLineageCA && !nb.IsPlayerSeedLineage) continue;
                    if (constrainCASpreadToWindDirection && !IsWindSpreadDirection(nx, ny, x, y)) continue;

                    int variant = nb.FlowerVariantIndex;

                    int count = 0;
                    for (int sy = -1; sy <= 1; sy++)
                    {
                        for (int sx = -1; sx <= 1; sx++)
                        {
                            if (sx == 0 && sy == 0) continue;
                            int tx = x + sx;
                            int ty = y + sy;
                            if (!Grid.InBounds(tx, ty)) continue;

                            var s = Grid.Get(tx, ty);
                            if (s == null || !s.HasPlant) continue;
                            if (s.FlowerVariantIndex != variant) continue;
                            if (onlyPlayerSeededLineageCA && !s.IsPlayerSeedLineage) continue;
                            if (constrainCASpreadToWindDirection && !IsWindSpreadDirection(tx, ty, x, y)) continue;
                            count++;
                        }
                    }

                    if (count > bestCount || (count == bestCount && (bestVariant < 0 || variant < bestVariant)))
                    {
                        bestCount = count;
                        bestVariant = variant;
                    }
                }
            }

            return bestVariant;
        }
    }
}
