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
        [Tooltip("Optional scene PlantVoxelRenderer to reuse instead of creating a runtime fallback.")]
        public PlantVoxelRenderer plantVoxelRendererOverride;

        [Header("Initial Ecology")]
        [Tooltip("Seed an initial set of plants so cellular automata has a starting population.")]
        public bool seedInitialPlants = true;
        [Range(0f, 0.10f)] public float initialPlantDensity = 0.02f;

        [Header("Settlement Terrain")]
        [Tooltip("Flatten a large dry plain at the map center for the campsite.")]
        public bool flattenMapCenterForSettlement = true;
        [Min(4f)] public float centralSettlementPlainRadius = 26f;
        [Header("Initial Flower Clusters")]
        public bool seedInitialFlowerClusters = true;
        [Min(1)] public int initialFlowerClusterCount = 8;
        [Min(2)] public int initialFlowerClusterMinFlowers = 4;
        [Min(2)] public int initialFlowerClusterMaxFlowers = 7;
        [Min(0.5f)] public float initialFlowerClusterRadius = 2.4f;
        public bool initialFlowerClustersCountAsLineage = true;

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
        [Min(1f)] public float densePlayerFlowerRadius = 3.2f;
        [Min(0.5f)] public float denseFlowerDropCheckSeconds = 6f;
        [Range(0f, 1f)] public float denseFlowerDropChance = 1f;
        [Min(1)] public int denseFlowerMaxActiveSeedDrops = 48;
        [Min(0.25f)] public float denseFlowerSeedDropSpacing = 2.6f;
        [Min(0f)] public float denseFlowerSeedDropHeight = 0.24f;
        [Min(0f)] public float denseFlowerSeedDropVisibleLift = 0.18f;
        [Min(0.1f)] public float denseFlowerSeedDropScale = 0.28f;
        [Min(1f)] public float denseFlowerSeedDropLifetimeSeconds = 30f;
        [Min(1f)] public float denseFlowerSeedSelfPlantDelaySeconds = 12f;
        [Min(0.25f)] public float denseFlowerSeedSelfPlantRetrySeconds = 1f;
        [Min(0f)] public float denseFlowerSeedDropBurstHeight = 0.4f;
        [Range(0, 64)] public int denseFlowerSeedDropBurstCount = 14;
        [Min(2)] public int denseFlowerSeedDropMinCount = 4;
        [Min(2)] public int denseFlowerSeedDropMaxCount = 20;
        [Min(1)] public int calmPollinationMinRadiusCells = 3;
        [Min(1)] public int calmPollinationSearchRadiusCells = 12;
        [Min(1)] public int windPollinationMinRadiusCells = 10;
        [Min(1)] public int windPollinationSearchRadiusCells = 28;
        [Header("Wind Pollination Spread")]
        [Min(0f)] public float windPollinationDistanceMin = 3f;
        [Min(0f)] public float windPollinationDistanceMax = 7f;
        [Min(0f)] public float windPollinationLateralJitter = 1.1f;
        [Header("Dense Flower Seed Drop Glow")]
        public bool denseFlowerSeedDropsGlow = true;
        public Color denseFlowerSeedDropGlowColor = new Color(0.22f, 0.95f, 0.75f, 1f);
        [Min(0f)] public float denseFlowerSeedDropGlowRange = 1.8f;
        [Range(0f, 8f)] public float denseFlowerSeedDropGlowIntensity = 1.8f;
        [Min(0.01f)] public float denseFlowerSeedDropHaloSize = 0.45f;
        [Min(0f)] public float denseFlowerSeedDropHaloHeight = 0.18f;

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
        private SpawnPickups _spawnPickups;
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

            if (flattenMapCenterForSettlement)
            {
                _voxelWorld.enableFlatBuildPads = true;
                _voxelWorld.forceCentralSettlementPad = true;
                _voxelWorld.flatBuildPadCount = 1;
                _voxelWorld.flatBuildPadRadius = Mathf.Max(_voxelWorld.flatBuildPadRadius, centralSettlementPlainRadius);
                _voxelWorld.centralSettlementPadRadius = Mathf.Max(_voxelWorld.centralSettlementPadRadius, centralSettlementPlainRadius);
                _voxelWorld.centralSettlementPadHardRadius = Mathf.Max(_voxelWorld.centralSettlementPadHardRadius, centralSettlementPlainRadius * 0.72f);
                _voxelWorld.flatBuildPadBlend = 1f;
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
            if (seedInitialFlowerClusters)
                SeedInitialFlowerClusters();

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
                _plantRenderer = plantVoxelRendererOverride;
                if (_plantRenderer == null)
                {
                    var renderers = FindObjectsByType<PlantVoxelRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
                    if (renderers != null && renderers.Length > 0)
                        _plantRenderer = renderers[0];
                }
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

        public bool CanPlaceSeedAtWorld(Vector3 world)
        {
            if (IsWinterSeasonActive())
                return false;
            if (!TryWorldToCell(world, out int x, out int y))
                return false;
            if (!IsPlantableColumn(x, y))
                return false;

            var c = Grid.Get(x, y);
            return c != null && (c.PlantStage == PlantStage.Empty || c.PlantStage == PlantStage.Burnt);
        }

        public bool TryAutoPlantDroppedSeedAt(Vector3 world, int flowerVariantIndex)
        {
            if (!CanPlaceSeedAtWorld(world))
                return false;

            PlaceSeedAt(world, flowerVariantIndex);
            return true;
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
            if (CurrentSeason == Season.Winter)
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
            int activeDrops = CountActiveDenseFlowerSeedDrops();
            if (denseFlowerMaxActiveSeedDrops > 0 && activeDrops >= denseFlowerMaxActiveSeedDrops)
                return;

            float radius = Mathf.Max(1f, densePlayerFlowerRadius);
            int cellRadius = Mathf.Max(1, Mathf.CeilToInt(radius / Mathf.Max(0.0001f, Grid.CellSize)));
            var eligibleSources = new List<Vector2Int>(64);
            for (int y = 0; y < Grid.Height; y++)
            for (int x = 0; x < Grid.Width; x++)
            {
                var cell = Grid.Get(x, y);
                if (!IsDenseFlowerDropSourceCell(cell))
                    continue;

                int nearbyFlowers = CountDensePlayerFlowersAround(x, y, cellRadius, radius);
                if (nearbyFlowers < Mathf.Max(2, densePlayerFlowerThreshold))
                    continue;
                eligibleSources.Add(new Vector2Int(x, y));
            }

            if (eligibleSources.Count == 0)
                return;
            if (UnityEngine.Random.value > Mathf.Clamp01(denseFlowerDropChance))
                return;

            Vector2Int source = eligibleSources[UnityEngine.Random.Range(0, eligibleSources.Count)];
            var sourceCell = Grid.Get(source.x, source.y);
            int crowdedCount = CountDensePlayerFlowersAround(source.x, source.y, cellRadius, radius);
            int dropCount = GetDenseFlowerSeedDropCount(crowdedCount);
            if (denseFlowerMaxActiveSeedDrops > 0)
                dropCount = Mathf.Min(dropCount, Mathf.Max(0, denseFlowerMaxActiveSeedDrops - activeDrops));
            if (dropCount <= 0)
                return;

            var clusterPoints = new List<Vector3>(16);
            CollectDenseFlowerClusterWorldPoints(source.x, source.y, cellRadius, radius, clusterPoints);
            if (clusterPoints.Count == 0)
                clusterPoints.Add(GetDenseFlowerDropSourceWorld(source.x, source.y));

            var variantPool = new List<int>(8);
            CollectDenseFlowerClusterVariants(source.x, source.y, cellRadius, radius, variantPool);
            if (variantPool.Count == 0 && sourceCell != null && sourceCell.FlowerVariantIndex >= 0)
                variantPool.Add(sourceCell.FlowerVariantIndex);

            Shuffle(variantPool);
            var dropCells = new List<Vector2Int>(64);
            CollectPollinationDropCells(source.x, source.y, crowdedCount, dropCells);

            int spawned = 0;
            for (int i = 0; i < dropCells.Count && spawned < dropCount; i++)
            {
                Vector2Int cellPos = dropCells[i];
                Vector3 dropWorld = _voxelWorld.ColumnTopWorld(cellPos.x, cellPos.y) + Vector3.up * Mathf.Max(0f, denseFlowerSeedDropHeight);
                if (HasNearbyDenseFlowerSeedPickup(dropWorld, 0.18f))
                    continue;

                int flowerVariantIndex = variantPool.Count > 0
                    ? variantPool[spawned % variantPool.Count]
                    : (sourceCell != null ? sourceCell.FlowerVariantIndex : -1);
                SpawnDenseFlowerSeedPickup(dropWorld, flowerVariantIndex);
                spawned++;
            }
        }

        int GetDenseFlowerSeedDropCount(int nearbyFlowers)
        {
            int minCount = Mathf.Max(2, denseFlowerSeedDropMinCount);
            int maxCount = Mathf.Max(minCount, denseFlowerSeedDropMaxCount);
            float t = Mathf.InverseLerp(Mathf.Max(2, densePlayerFlowerThreshold), 14f, nearbyFlowers);
            return Mathf.RoundToInt(Mathf.Lerp(minCount, maxCount, t));
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
            float radius = Mathf.Max(1f, densePlayerFlowerRadius);
            var clusterPoints = new List<Vector3>(16);
            CollectDenseFlowerClusterWorldPoints(centerX, centerY, cellRadius, radius, clusterPoints);
            if (clusterPoints.Count == 0)
            {
                Vector3 fallback = GetDenseFlowerDropSourceWorld(centerX, centerY);
                if (!HasNearbyDenseFlowerSeedPickup(fallback, denseFlowerSeedDropSpacing))
                {
                    dropWorld = fallback;
                    return true;
                }
                return false;
            }

            for (int i = 0; i < clusterPoints.Count; i++)
            {
                Vector3 anchor = clusterPoints[UnityEngine.Random.Range(0, clusterPoints.Count)];
                if (HasNearbyDenseFlowerSeedPickup(anchor, denseFlowerSeedDropSpacing))
                    continue;

                dropWorld = anchor;
                return true;
            }

            for (int i = 0; i < clusterPoints.Count * 2; i++)
            {
                Vector3 anchor = clusterPoints[UnityEngine.Random.Range(0, clusterPoints.Count)];
                Vector3 candidate = anchor + new Vector3(
                    UnityEngine.Random.Range(-0.04f, 0.04f),
                    0f,
                    UnityEngine.Random.Range(-0.04f, 0.04f));
                if (HasNearbyDenseFlowerSeedPickup(candidate, denseFlowerSeedDropSpacing))
                    continue;

                dropWorld = candidate;
                return true;
            }

            return false;
        }

        void CollectDenseFlowerClusterWorldPoints(int centerX, int centerY, int cellRadius, float worldRadius, List<Vector3> sink)
        {
            sink.Clear();
            if (Grid == null)
                return;

            float rSqr = worldRadius * worldRadius;
            Vector3 center = Grid.CellCenterWorld(centerX, centerY);
            for (int y = centerY - cellRadius; y <= centerY + cellRadius; y++)
            for (int x = centerX - cellRadius; x <= centerX + cellRadius; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                var cell = Grid.Get(x, y);
                if (!IsDenseFlowerDropSourceCell(cell))
                    continue;

                Vector3 candidateCenter = Grid.CellCenterWorld(x, y);
                Vector2 d = new Vector2(candidateCenter.x - center.x, candidateCenter.z - center.z);
                if (d.sqrMagnitude > rSqr)
                    continue;

                sink.Add(GetDenseFlowerDropSourceWorld(x, y));
            }
        }

        void CollectDenseFlowerClusterVariants(int centerX, int centerY, int cellRadius, float worldRadius, List<int> sink)
        {
            sink.Clear();
            if (Grid == null)
                return;

            float rSqr = worldRadius * worldRadius;
            Vector3 center = Grid.CellCenterWorld(centerX, centerY);
            for (int y = centerY - cellRadius; y <= centerY + cellRadius; y++)
            for (int x = centerX - cellRadius; x <= centerX + cellRadius; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;

                var cell = Grid.Get(x, y);
                if (!IsDenseFlowerDropSourceCell(cell))
                    continue;

                Vector3 candidateCenter = Grid.CellCenterWorld(x, y);
                Vector2 d = new Vector2(candidateCenter.x - center.x, candidateCenter.z - center.z);
                if (d.sqrMagnitude > rSqr)
                    continue;

                if (cell.FlowerVariantIndex >= 0 && !sink.Contains(cell.FlowerVariantIndex))
                    sink.Add(cell.FlowerVariantIndex);
            }
        }

        static void Shuffle<T>(List<T> list)
        {
            if (list == null)
                return;

            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }

        Vector3 GetDenseFlowerDropSourceWorld(int x, int y)
        {
            if (_plantRenderer != null && _plantRenderer.TryGetActivePlantGameObject(x, y, out var go) && go != null)
                return go.transform.position + Vector3.up * Mathf.Max(0f, denseFlowerSeedDropHeight);

            return _voxelWorld.ColumnTopWorld(x, y) + Vector3.up * Mathf.Max(0f, denseFlowerSeedDropHeight);
        }

        bool TryResolvePollinationDropWorld(Vector3 flowerAnchorWorld, int crowdedCount, out Vector3 dropWorld)
        {
            if (TryResolveOpenPlantableDropWorld(ApplyWindToPollinationTarget(flowerAnchorWorld, crowdedCount), CurrentWeather == WeatherType.Wind ? 4 : 2, out dropWorld))
                return true;
            return TryResolveOpenPlantableDropWorld(flowerAnchorWorld, 2, out dropWorld);
        }

        void CollectPollinationDropCells(int centerX, int centerY, int crowdedCount, List<Vector2Int> sink)
        {
            sink.Clear();
            if (Grid == null || _voxelWorld == null)
                return;

            bool windy = CurrentWeather == WeatherType.Wind;
            int searchRadius = windy ? Mathf.Max(1, windPollinationSearchRadiusCells) : Mathf.Max(1, calmPollinationSearchRadiusCells);
            int minRadius = windy ? Mathf.Max(0, windPollinationMinRadiusCells) : Mathf.Max(0, calmPollinationMinRadiusCells);
            Vector2 wind = new Vector2(_windDirection.x, _windDirection.y);
            if (wind.sqrMagnitude > 0.001f)
                wind.Normalize();
            Vector2 lateral = new Vector2(-wind.y, wind.x);

            var weighted = new List<(Vector2Int cell, float weight)>(128);
            for (int y = centerY - searchRadius; y <= centerY + searchRadius; y++)
            for (int x = centerX - searchRadius; x <= centerX + searchRadius; x++)
            {
                if (!Grid.InBounds(x, y))
                    continue;
                if (!CanPlaceSeedAtCell(x, y))
                    continue;

                int dx = x - centerX;
                int dy = y - centerY;
                float dist = Mathf.Sqrt(dx * dx + dy * dy);
                if (dist > searchRadius)
                    continue;
                if (dist < minRadius)
                    continue;

                float weight;
                if (windy)
                {
                    Vector2 dir = dist > 0.001f ? new Vector2(dx, dy).normalized : wind;
                    float forward = wind.sqrMagnitude > 0.001f ? Mathf.Max(0f, Vector2.Dot(dir, wind)) : 0.5f;
                    float sidewaysPenalty = wind.sqrMagnitude > 0.001f ? Mathf.Abs(Vector2.Dot(dir, lateral)) : 0f;
                    float outward = Mathf.InverseLerp(minRadius, searchRadius, dist);
                    weight = forward * 5.5f + outward * 2.5f - sidewaysPenalty * 0.45f;
                }
                else
                {
                    float outward = Mathf.InverseLerp(minRadius, searchRadius, dist);
                    weight = outward * 3.5f;
                }

                weighted.Add((new Vector2Int(x, y), weight + UnityEngine.Random.Range(0f, 0.15f)));
            }

            weighted.Sort((a, b) => b.weight.CompareTo(a.weight));
            for (int i = 0; i < weighted.Count; i++)
            {
                var candidate = weighted[i].cell;
                if (IsTooCloseToChosenPollinationCell(candidate, sink))
                    continue;
                sink.Add(candidate);
            }
        }

        bool IsTooCloseToChosenPollinationCell(Vector2Int candidate, List<Vector2Int> chosen)
        {
            if (chosen == null)
                return false;

            float minSpacing = Mathf.Max(1f, denseFlowerSeedDropSpacing);
            float minSpacingSqr = minSpacing * minSpacing;
            for (int i = 0; i < chosen.Count; i++)
            {
                Vector2Int other = chosen[i];
                float dx = candidate.x - other.x;
                float dy = candidate.y - other.y;
                if (dx * dx + dy * dy < minSpacingSqr)
                    return true;
            }

            return false;
        }

        Vector3 ApplyWindToPollinationTarget(Vector3 flowerAnchorWorld, int crowdedCount)
        {
            if (CurrentWeather != WeatherType.Wind)
                return flowerAnchorWorld;

            Vector2 wind = new Vector2(_windDirection.x, _windDirection.y);
            if (wind.sqrMagnitude <= 0.001f)
                return flowerAnchorWorld;

            wind.Normalize();
            Vector2 lateral = new Vector2(-wind.y, wind.x);
            float t = Mathf.InverseLerp(Mathf.Max(2, densePlayerFlowerThreshold), 10f, crowdedCount);
            float forwardDistance = Mathf.Lerp(windPollinationDistanceMin, windPollinationDistanceMax, t) * Mathf.Max(0.5f, Grid.CellSize);
            float sideDistance = UnityEngine.Random.Range(-windPollinationLateralJitter, windPollinationLateralJitter) * Mathf.Max(0.5f, Grid.CellSize);

            return flowerAnchorWorld
                   + new Vector3(wind.x, 0f, wind.y) * forwardDistance
                   + new Vector3(lateral.x, 0f, lateral.y) * sideDistance;
        }

        bool TryResolveOpenPlantableDropWorld(Vector3 desiredWorld, int searchRadiusCells, out Vector3 dropWorld)
        {
            dropWorld = desiredWorld;
            if (!TryWorldToCell(desiredWorld, out int cx, out int cy))
                return false;

            int maxRadius = Mathf.Max(0, searchRadiusCells);
            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int y = cy - radius; y <= cy + radius; y++)
                for (int x = cx - radius; x <= cx + radius; x++)
                {
                    if (!Grid.InBounds(x, y))
                        continue;
                    if (Mathf.Abs(x - cx) != radius && Mathf.Abs(y - cy) != radius)
                        continue;
                    if (!CanPlaceSeedAtCell(x, y))
                        continue;

                    dropWorld = _voxelWorld.ColumnTopWorld(x, y) + Vector3.up * Mathf.Max(0f, denseFlowerSeedDropHeight);
                    return true;
                }
            }

            return false;
        }

        bool CanPlaceSeedAtCell(int x, int y)
        {
            if (Grid == null || !Grid.InBounds(x, y))
                return false;
            if (!IsPlantableColumn(x, y))
                return false;

            var c = Grid.Get(x, y);
            return c != null && (c.PlantStage == PlantStage.Empty || c.PlantStage == PlantStage.Burnt);
        }

        void SpawnDenseFlowerSeedPickup(Vector3 worldPos, int flowerVariantIndex)
        {
            if (_spawnPickups == null || !_spawnPickups.isActiveAndEnabled)
                _spawnPickups = FindFirstObjectByType<SpawnPickups>();

            GameObject prefab = null;
            if (_spawnPickups != null && _spawnPickups.seedPickupPrefabs != null && _spawnPickups.seedPickupPrefabs.Length > 0)
            {
                int idx = Mathf.Clamp(flowerVariantIndex, 0, _spawnPickups.seedPickupPrefabs.Length - 1);
                prefab = _spawnPickups.seedPickupPrefabs[idx];
            }

            var go = prefab != null
                ? Instantiate(prefab)
                : GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = $"PlayerFlowerSeedDrop_{flowerVariantIndex}_{Time.frameCount}";
            go.transform.position = worldPos + Vector3.up * Mathf.Max(0f, denseFlowerSeedDropVisibleLift);
            go.transform.localScale = Vector3.one * Mathf.Max(0.1f, denseFlowerSeedDropScale);
            go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), prefab != null ? 0f : 90f);

            var rb = go.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;

            var pickup = go.AddComponent<SCoLPickup>();
            pickup.type = SCoLItemType.Seed;
            pickup.amount = 1;
            pickup.seedVariantIndex = flowerVariantIndex;
            pickup.preserveExistingMaterials = prefab != null;
            pickup.autoPlantIfUncollected = true;
            pickup.autoPlantDelaySeconds = denseFlowerSeedSelfPlantDelaySeconds;
            pickup.autoPlantRetrySeconds = denseFlowerSeedSelfPlantRetrySeconds;
            pickup.ApplyVisual();
            EnsurePickupCollider(go);
            SnapPickupToGround(go, worldPos);
            AddDenseFlowerSeedDropGlow(go);
            if (denseFlowerSeedDropLifetimeSeconds > 0f)
                Destroy(go, denseFlowerSeedDropLifetimeSeconds);

            FPSGameFeel.VoxelBurst(
                go.transform.position + Vector3.up * Mathf.Max(0f, denseFlowerSeedDropBurstHeight),
                count: Mathf.Max(0, denseFlowerSeedDropBurstCount),
                spread: 0.65f,
                life: 0.8f,
                cubeSize: 0.05f);
        }

        static void EnsurePickupCollider(GameObject go)
        {
            if (go == null)
                return;

            var cols = go.GetComponentsInChildren<Collider>(includeInactive: true);
            if (cols != null && cols.Length > 0)
            {
                for (int i = 0; i < cols.Length; i++)
                {
                    var c = cols[i];
                    if (c == null)
                        continue;
                    c.enabled = true;
                    c.isTrigger = false;
                }
                return;
            }

            var added = go.AddComponent<BoxCollider>();
            added.enabled = true;
            added.isTrigger = false;
        }

        void SnapPickupToGround(GameObject go, Vector3 aroundPos)
        {
            if (go == null || !TryGetPickupBottomY(go, out float bottomY))
                return;

            float groundY = aroundPos.y;
            Vector3 terrainSample = aroundPos + Vector3.up * 8f;
            if (_voxelWorld != null &&
                _voxelWorld.TryGetTerrainSurfaceYAtWorld(terrainSample, out float terrainY, includeWaterSurface: false))
            {
                groundY = terrainY;
            }
            else
            {
                var hits = Physics.RaycastAll(
                    aroundPos + Vector3.up * 12f,
                    Vector3.down,
                    40f,
                    ~0,
                    QueryTriggerInteraction.Ignore);
                bool found = false;
                float lowestY = float.PositiveInfinity;
                for (int i = 0; i < hits.Length; i++)
                {
                    var hit = hits[i];
                    if (!IsValidPickupGroundHit(go, hit.collider))
                        continue;

                    if (hit.point.y < lowestY)
                    {
                        lowestY = hit.point.y;
                        found = true;
                    }
                }

                if (found)
                    groundY = lowestY;
            }

            float dy = (groundY + 0.01f) - bottomY;
            if (!Mathf.Approximately(dy, 0f))
                go.transform.position += Vector3.up * dy;
        }

        static bool TryGetPickupBottomY(GameObject go, out float bottomY)
        {
            bottomY = 0f;
            if (go == null)
                return false;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            bool has = false;
            Bounds bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                if (!has)
                {
                    bounds = renderer.bounds;
                    has = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }

            if (!has)
            {
                var colliders = go.GetComponentsInChildren<Collider>(includeInactive: true);
                for (int i = 0; i < colliders.Length; i++)
                {
                    var collider = colliders[i];
                    if (collider == null)
                        continue;

                    if (!has)
                    {
                        bounds = collider.bounds;
                        has = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }
            }

            if (!has)
                return false;

            bottomY = bounds.min.y;
            return true;
        }

        static bool IsValidPickupGroundHit(GameObject go, Collider collider)
        {
            if (go == null || collider == null || !collider.enabled || collider.isTrigger)
                return false;

            if (collider.transform.IsChildOf(go.transform))
                return false;

            if (collider.GetComponentInParent<SCoLPickup>() != null)
                return false;

            return true;
        }

        void AddDenseFlowerSeedDropGlow(GameObject go)
        {
            if (!denseFlowerSeedDropsGlow || go == null)
                return;

            var renderers = go.GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var renderer = renderers[i];
                if (renderer == null)
                    continue;

                var mats = renderer.materials;
                bool changed = false;
                for (int j = 0; j < mats.Length; j++)
                {
                    var mat = mats[j];
                    if (mat == null)
                        continue;

                    Color glowColor = denseFlowerSeedDropGlowColor;
                    if (mat.HasProperty("_BaseColor"))
                    {
                        Color baseColor = mat.GetColor("_BaseColor");
                        mat.SetColor("_BaseColor", Color.Lerp(baseColor, glowColor, 0.35f));
                        changed = true;
                    }
                    if (mat.HasProperty("_Color"))
                    {
                        Color baseColor = mat.GetColor("_Color");
                        mat.SetColor("_Color", Color.Lerp(baseColor, glowColor, 0.35f));
                        changed = true;
                    }
                    if (mat.HasProperty("_EmissionColor"))
                    {
                        mat.EnableKeyword("_EMISSION");
                        mat.SetColor("_EmissionColor", glowColor * Mathf.Max(0f, denseFlowerSeedDropGlowIntensity));
                        changed = true;
                    }
                }

                if (changed)
                    renderer.materials = mats;
            }

            var lightGo = new GameObject("PollinationGlow");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = Vector3.up * Mathf.Max(0f, denseFlowerSeedDropHaloHeight);

            var glowLight = lightGo.AddComponent<Light>();
            glowLight.type = LightType.Point;
            glowLight.color = denseFlowerSeedDropGlowColor;
            glowLight.range = Mathf.Max(0f, denseFlowerSeedDropGlowRange);
            glowLight.intensity = Mathf.Max(0f, denseFlowerSeedDropGlowIntensity);
            glowLight.shadows = LightShadows.None;
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

        private void SeedInitialFlowerClusters()
        {
            if (Grid == null || _voxelWorld == null)
                return;

            int clusterCount = Mathf.Max(1, initialFlowerClusterCount);
            int minFlowers = Mathf.Max(2, initialFlowerClusterMinFlowers);
            int maxFlowers = Mathf.Max(minFlowers, initialFlowerClusterMaxFlowers);
            int variantCount = 8;

            for (int cluster = 0; cluster < clusterCount; cluster++)
            {
                if (!TryFindInitialFlowerClusterCenter(out int centerX, out int centerY))
                    continue;

                int flowerVariant = _rng != null ? _rng.Next(0, variantCount) : UnityEngine.Random.Range(0, variantCount);
                int targetFlowers = _rng != null ? _rng.Next(minFlowers, maxFlowers + 1) : UnityEngine.Random.Range(minFlowers, maxFlowers + 1);
                int radiusCells = Mathf.Max(1, Mathf.CeilToInt(initialFlowerClusterRadius / Mathf.Max(0.01f, Grid.CellSize)));

                var candidates = new List<Vector2Int>(32);
                for (int y = centerY - radiusCells; y <= centerY + radiusCells; y++)
                for (int x = centerX - radiusCells; x <= centerX + radiusCells; x++)
                {
                    if (!Grid.InBounds(x, y))
                        continue;

                    Vector3 center = Grid.CellCenterWorld(centerX, centerY);
                    Vector3 candidate = Grid.CellCenterWorld(x, y);
                    Vector2 d = new Vector2(candidate.x - center.x, candidate.z - center.z);
                    if (d.sqrMagnitude > initialFlowerClusterRadius * initialFlowerClusterRadius)
                        continue;
                    if (!CanUseCellForInitialFlower(x, y))
                        continue;

                    candidates.Add(new Vector2Int(x, y));
                }

                Shuffle(candidates);
                int placed = 0;
                for (int i = 0; i < candidates.Count && placed < targetFlowers; i++)
                {
                    var cellPos = candidates[i];
                    var c = Grid.Get(cellPos.x, cellPos.y);
                    c.PlantStage = PlantStage.MediumTree;
                    c.PlantAgeSeconds = Mathf.Max(secondsPerTreeStage * 2f, 0.1f);
                    c.Durability = 1f;
                    c.Success = 0.95f;
                    c.Water = Mathf.Max(c.Water, 0.65f);
                    c.WaterVisual = 0f;
                    c.IsPlayerSeedLineage = initialFlowerClustersCountAsLineage;
                    c.FlowerVariantIndex = flowerVariant;
                    c.BurntAutoClearSeconds = 0f;
                    c.PlantHealth = 65f;
                    c.StompHits = 0;
                    c.ClearPlantPlacementOffset();
                    placed++;
                }
            }
        }

        bool TryFindInitialFlowerClusterCenter(out int centerX, out int centerY)
        {
            centerX = centerY = 0;
            if (Grid == null)
                return false;

            for (int attempt = 0; attempt < 64; attempt++)
            {
                int x = _rng != null ? _rng.Next(0, Grid.Width) : UnityEngine.Random.Range(0, Grid.Width);
                int y = _rng != null ? _rng.Next(0, Grid.Height) : UnityEngine.Random.Range(0, Grid.Height);
                if (!CanUseCellForInitialFlower(x, y))
                    continue;

                centerX = x;
                centerY = y;
                return true;
            }

            return false;
        }

        bool CanUseCellForInitialFlower(int x, int y)
        {
            if (Grid == null || !Grid.InBounds(x, y))
                return false;
            if (!IsPlantableColumn(x, y))
                return false;

            var c = Grid.Get(x, y);
            return c != null && (c.PlantStage == PlantStage.Empty || c.PlantStage == PlantStage.Burnt);
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
