using UnityEngine;
using SCoL.Weather;
using SCoL.Voxels;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace SCoL.Visualization
{
    /// <summary>
    /// Simple visual controller driven by WeatherSystem.
    ///
    /// Option B implementation: this script can be dropped into an existing scene.
    /// It will (by default) create a lightweight camera-following rain ParticleSystem at runtime
    /// so the scene YAML doesn't need to encode complex ParticleSystem settings.
    ///
    /// Thunderstorm visuals are currently handled by DayNightLightingController (lightning flash + audio).
    /// This class handles particle-based rain + snow and optional Unity WindZone toggling.
    /// </summary>
    public sealed class WeatherVFXController : MonoBehaviour
    {
        [Header("References")]
        public WeatherSystem weatherSystem;

        [Tooltip("If true, the VFX follow a camera/target transform.")]
        public bool followMainCamera = true;

        [Tooltip("Optional explicit target to follow (recommended for XR). If null, uses Camera.main.")]
        public Transform followTarget;
        [Tooltip("If true, prefer Camera.main as follow target at runtime.")]
        public bool preferMainCameraAtRuntime = true;
        public VoxelWorld voxelWorld;

        [Tooltip("Vertical offset above camera for precipitation volumes.")]
        public float heightOffset = 2.0f;

        [Header("Auto Fit")]
        [Tooltip("Scale weather volumes based on voxel world size (reference world size = 100).")]
        public bool autoScaleToVoxelWorld = true;
        [Min(1f)] public float referenceWorldSize = 100f;
        [Min(1f)] public float maxAutoScale = 3f;
        [Tooltip("If true, increase rain/snow emission when auto-scaling up to keep density similar.")]
        public bool scaleEmissionWithAutoScale = true;

        [Header("Rain VFX")]
        [Tooltip("Optional ParticleSystem. If null, one will be created at runtime.")]
        public ParticleSystem rainParticleSystem;

        [Tooltip("Material used for the runtime-created rain ParticleSystem renderer.")]
        public Material rainMaterial;

        [Tooltip("Enable rain during Thunderstorm as well.")]
        public bool rainAlsoInThunderstorm = true;

        [Tooltip("Max particles to keep alive. Lower this on laptops/VR.")]
        [Range(100, 10000)] public int rainMaxParticles = 2500;

        [Tooltip("Emission rate (particles/sec) at full rain intensity.")]
        [Range(0f, 5000f)] public float rainEmissionRate = 900f;

        [Tooltip("Area size around camera where rain spawns.")]
        public Vector3 rainBoxSize = new Vector3(20f, 6f, 20f);

        [Tooltip("Fall speed range (m/s).")]
        public Vector2 rainFallSpeedRange = new Vector2(10f, 18f);

        [Tooltip("Particle lifetime (seconds).")]
        public Vector2 rainLifetimeRange = new Vector2(0.8f, 1.6f);

        [Tooltip("Particle size range.")]
        public Vector2 rainSizeRange = new Vector2(0.2f, 0.5f);

        [Header("Snow VFX")]
        [Tooltip("Optional ParticleSystem for snow. If null, one will be created at runtime.")]
        public ParticleSystem snowParticleSystem;

        [Tooltip("Material used for the runtime-created snow ParticleSystem renderer.")]
        public Material snowMaterial;

        [Tooltip("Max particles to keep alive. Lower this on laptops/VR.")]
        [Range(100, 20000)] public int snowMaxParticles = 4000;

        [Tooltip("Emission rate (particles/sec) at full snow intensity.")]
        [Range(0f, 5000f)] public float snowEmissionRate = 600f;

        [Tooltip("Area size around camera where snow spawns.")]
        public Vector3 snowBoxSize = new Vector3(25f, 8f, 25f);

        [Tooltip("Fall speed range (m/s).")]
        public Vector2 snowFallSpeedRange = new Vector2(1.5f, 3.5f);

        [Tooltip("Particle lifetime (seconds).")]
        public Vector2 snowLifetimeRange = new Vector2(2.5f, 4.5f);

        [Tooltip("Particle size range.")]
        public Vector2 snowSizeRange = new Vector2(0.08f, 0.18f);

        [Header("Firefly VFX (optional)")]
        [Tooltip("Optional ParticleSystem for fireflies. If null, one will be created at runtime.")]
        public ParticleSystem fireflyParticleSystem;

        [Tooltip("Material used for the runtime-created firefly ParticleSystem renderer.")]
        public Material fireflyMaterial;

        [Tooltip("Optional: DayNightLightingController used to decide whether it's night.")]
        public DayNightLightingController dayNightController;

        [Tooltip("Consider it NIGHT when timeOfDay01 is outside this DAY range.")]
        public Vector2 dayRange01 = new Vector2(0.23f, 0.77f);

        [Tooltip("Only show fireflies at night.")]
        public bool firefliesOnlyAtNight = true;

        [Tooltip("If true, fireflies are disabled in bad weather (Rain/Thunderstorm/Snow).")]
        public bool disableFirefliesInBadWeather = true;

        [Tooltip("If enabled, fireflies gather around dense healthy flower clusters instead of just following the camera.")]
        public bool clusterFirefliesAroundHealthyFlowers = true;
        [Min(1f)] public float fireflyClusterSearchRadius = 5.5f;
        [Min(2)] public int fireflyClusterMinFlowers = 3;
        [Min(0.1f)] public float fireflyClusterFollowLerp = 2.5f;
        [Range(0.5f, 4f)] public float fireflyClusterEmissionMultiplier = 1.8f;
        [Range(0.5f, 3f)] public float fireflyClusterBoxScaleMultiplier = 1.35f;
        public Vector3 fireflyClusterOffset = new Vector3(0f, 1.2f, 0f);

        [Range(0, 1000)] public int fireflyMaxParticles = 150;
        [Range(0f, 200f)] public float fireflyEmissionRate = 12f;
        public Vector3 fireflyBoxSize = new Vector3(18f, 6f, 18f);
        public Vector2 fireflyLifetimeRange = new Vector2(6f, 14f);
        public Vector2 fireflySpeedRange = new Vector2(0.15f, 0.6f);
        public Vector2 fireflySizeRange = new Vector2(0.03f, 0.10f);

        [Header("Wind (optional)")]
        [Tooltip("Optional WindZone to enable/adjust during Wind/Rain/Thunder.")]
        public WindZone windZone;

        [Min(0f)] public float windMainClear = 0.0f;
        [Min(0f)] public float windMainWindy = 0.75f;
        [Min(0f)] public float windMainRain = 0.35f;
        [Min(0f)] public float windMainThunder = 0.55f;
        [Min(0f)] public float windMainSnow = 0.25f;

        [Header("Sky Birds")]
        [Tooltip("Optional bird prefab. If null, the VoxBox bird prefab is auto-loaded in the editor.")]
        public GameObject skyBirdPrefab;
        public bool enableSkyBirds = true;
        [Range(1, 64)] public int skyBirdCount = 10;
        [Min(5f)] public float skyBirdHorizontalRadius = 42f;
        [Min(1f)] public float skyBirdMinHeight = 10f;
        [Min(1f)] public float skyBirdMaxHeight = 24f;
        [Min(0.1f)] public float skyBirdMinSpeed = 2.5f;
        [Min(0.1f)] public float skyBirdMaxSpeed = 5.5f;
        [Min(0.1f)] public float skyBirdTurnIntervalMin = 1.5f;
        [Min(0.1f)] public float skyBirdTurnIntervalMax = 4.5f;
        [Min(0f)] public float skyBirdReturnWeight = 1.4f;
        [Min(0f)] public float skyBirdVerticalJitter = 0.55f;
        [Range(0f, 20f)] public float skyBirdRotationLerp = 7f;

        [Header("Performance")]
        [Tooltip("If true, disables VFX in Edit Mode (always) and only runs during Play Mode.")]
        public bool playModeOnly = true;

        WeatherPhase _lastPhase;
        bool _hasLast;
        SCoLRuntime _runtime;
        bool _capturedBaseVolumes;
        Vector3 _rainBoxSizeBase;
        Vector3 _snowBoxSizeBase;
        Vector3 _fireflyBoxSizeBase;
        float _autoScale = 1f;
        bool _hasFireflyClusterTarget;
        Vector3 _fireflyClusterTarget;
        float _fireflyClusterStrength = 1f;
        readonly System.Collections.Generic.List<SkyBirdAgent> _skyBirds = new System.Collections.Generic.List<SkyBirdAgent>(16);

        struct SkyBirdAgent
        {
            public Transform transform;
            public Vector3 velocity;
            public float speed;
            public float nextTurnAt;
        }

        void Reset()
        {
            // Best effort auto-wire
            weatherSystem = FindFirstObjectByType<WeatherSystem>();
            AutoAssignBirdPrefab();
        }

        void Start()
        {
            if (playModeOnly && !Application.isPlaying) return;

            if (weatherSystem == null)
                weatherSystem = FindFirstObjectByType<WeatherSystem>();
            if (voxelWorld == null)
                voxelWorld = FindFirstObjectByType<VoxelWorld>();
            if (_runtime == null)
                _runtime = FindFirstObjectByType<SCoLRuntime>();

            CaptureBaseVolumeSettingsIfNeeded();
            RefreshAutoScaleFromWorld();

            EnsureRainSystem();
            EnsureSnowSystem();
            EnsureFireflySystem();
            EnsureSkyBirds();
            ApplyScaledVolumeSettings();
            ApplyForPhase(weatherSystem != null ? weatherSystem.CurrentPhase : WeatherPhase.Clear, force: true);
        }

        void OnDisable()
        {
            ClearSkyBirds();
        }

        void Update()
        {
            if (playModeOnly && !Application.isPlaying) return;

            if (weatherSystem == null)
                weatherSystem = FindFirstObjectByType<WeatherSystem>();
            if (voxelWorld == null)
                voxelWorld = FindFirstObjectByType<VoxelWorld>();
            if (_runtime == null)
                _runtime = FindFirstObjectByType<SCoLRuntime>();

            CaptureBaseVolumeSettingsIfNeeded();
            RefreshAutoScaleFromWorld();
            ApplyScaledVolumeSettings();
            UpdateFireflyClusterTarget(weatherSystem != null ? weatherSystem.CurrentPhase : WeatherPhase.Clear);

            Transform target = ResolveFollowTarget();
            UpdateSkyBirds(target);

            // Follow camera/target
            if (followMainCamera)
            {
                if (target != null)
                {
                    Vector3 p = target.position;
                    p.y += heightOffset;

                    if (rainParticleSystem != null)
                        rainParticleSystem.transform.position = p;

                    if (snowParticleSystem != null)
                        snowParticleSystem.transform.position = p;

                    if (fireflyParticleSystem != null && (!_hasFireflyClusterTarget || !clusterFirefliesAroundHealthyFlowers))
                        fireflyParticleSystem.transform.position = p;
                }
            }

            if (weatherSystem == null) return;

            var phase = weatherSystem.CurrentPhase;
            if (!_hasLast || phase != _lastPhase)
            {
                _lastPhase = phase;
                _hasLast = true;
                ApplyForPhase(phase, force: false);
            }

            // Optional: scale emission by intensity
            if (weatherSystem != null)
            {
                float intensity = Mathf.Clamp01(weatherSystem.Intensity01);
                float emissionScale = scaleEmissionWithAutoScale ? _autoScale : 1f;

                if (rainParticleSystem != null)
                {
                    bool rainingNow = phase == WeatherPhase.Rain || (rainAlsoInThunderstorm && phase == WeatherPhase.Thunderstorm);
                    var em = rainParticleSystem.emission;
                    em.rateOverTime = rainingNow ? (rainEmissionRate * emissionScale * Mathf.Max(0.2f, intensity)) : 0f;
                }

                if (snowParticleSystem != null)
                {
                    bool snowingNow = phase == WeatherPhase.Snow;
                    var em = snowParticleSystem.emission;
                    em.rateOverTime = snowingNow ? (snowEmissionRate * emissionScale * Mathf.Max(0.2f, intensity)) : 0f;
                }

                if (fireflyParticleSystem != null)
                {
                    bool active = ShouldShowFireflies(phase);
                    var em = fireflyParticleSystem.emission;
                    float rate = fireflyEmissionRate;
                    if (_hasFireflyClusterTarget && clusterFirefliesAroundHealthyFlowers)
                        rate *= Mathf.Max(1f, fireflyClusterEmissionMultiplier * Mathf.Max(0.5f, _fireflyClusterStrength));
                    em.rateOverTime = active ? rate : 0f;
                }
            }
        }

        void CaptureBaseVolumeSettingsIfNeeded()
        {
            if (_capturedBaseVolumes) return;
            _capturedBaseVolumes = true;
            _rainBoxSizeBase = rainBoxSize;
            _snowBoxSizeBase = snowBoxSize;
            _fireflyBoxSizeBase = fireflyBoxSize;
        }

        void RefreshAutoScaleFromWorld()
        {
            _autoScale = 1f;
            if (!autoScaleToVoxelWorld) return;
            if (voxelWorld == null || voxelWorld.Config == null) return;

            float span = Mathf.Max(voxelWorld.Config.worldWidth, voxelWorld.Config.worldDepth);
            float refSize = Mathf.Max(1f, referenceWorldSize);
            float s = span / refSize;
            _autoScale = Mathf.Clamp(s, 1f, Mathf.Max(1f, maxAutoScale));
        }

        void ApplyScaledVolumeSettings()
        {
            float s = Mathf.Max(0.1f, _autoScale);
            Vector3 rainScaled = _rainBoxSizeBase * s;
            Vector3 snowScaled = _snowBoxSizeBase * s;
            Vector3 fireflyScaled = _fireflyBoxSizeBase * s;

            if (rainParticleSystem != null)
            {
                var shape = rainParticleSystem.shape;
                shape.scale = rainScaled;
            }
            if (snowParticleSystem != null)
            {
                var shape = snowParticleSystem.shape;
                shape.scale = snowScaled;
            }
            if (fireflyParticleSystem != null)
            {
                var shape = fireflyParticleSystem.shape;
                if (_hasFireflyClusterTarget && clusterFirefliesAroundHealthyFlowers)
                    shape.scale = fireflyScaled * Mathf.Max(0.5f, fireflyClusterBoxScaleMultiplier * Mathf.Max(0.5f, _fireflyClusterStrength));
                else
                    shape.scale = fireflyScaled;
            }
        }

        void ApplyForPhase(WeatherPhase phase, bool force)
        {
            EnsureRainSystem();
            EnsureSnowSystem();
            EnsureFireflySystem();

            bool shouldRain = phase == WeatherPhase.Rain || (rainAlsoInThunderstorm && phase == WeatherPhase.Thunderstorm);
            bool shouldSnow = phase == WeatherPhase.Snow;
            bool shouldFireflies = ShouldShowFireflies(phase);

            if (rainParticleSystem != null)
            {
                if (shouldRain)
                {
                    if (force || !rainParticleSystem.isPlaying)
                        rainParticleSystem.Play();
                }
                else
                {
                    if (force || rainParticleSystem.isPlaying)
                        rainParticleSystem.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (snowParticleSystem != null)
            {
                if (shouldSnow)
                {
                    if (force || !snowParticleSystem.isPlaying)
                        snowParticleSystem.Play();
                }
                else
                {
                    if (force || snowParticleSystem.isPlaying)
                        snowParticleSystem.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
                }
            }

            if (fireflyParticleSystem != null)
            {
                if (shouldFireflies)
                {
                    if (force || !fireflyParticleSystem.isPlaying)
                        fireflyParticleSystem.Play();
                }
                else
                {
                    if (force || fireflyParticleSystem.isPlaying)
                        fireflyParticleSystem.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
                }
            }

            ApplyWindZone(phase);
        }

        Transform ResolveFollowTarget()
        {
            Transform target = followTarget;
            if (preferMainCameraAtRuntime && Camera.main != null)
                target = Camera.main.transform;
            else if (target == null)
            {
                var cam = Camera.main;
                target = cam != null ? cam.transform : null;
            }
            return target;
        }

        void EnsureSkyBirds()
        {
            if (!enableSkyBirds)
            {
                ClearSkyBirds();
                return;
            }

            AutoAssignBirdPrefab();
            if (skyBirdPrefab == null)
                return;

            int desired = Mathf.Max(1, skyBirdCount);
            while (_skyBirds.Count < desired)
                SpawnSkyBird();
            while (_skyBirds.Count > desired)
                RemoveSkyBird(_skyBirds.Count - 1);
        }

        void UpdateSkyBirds(Transform target)
        {
            if (!Application.isPlaying)
                return;

            EnsureSkyBirds();
            if (!enableSkyBirds || _skyBirds.Count == 0 || target == null)
                return;

            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            float minHeight = Mathf.Min(skyBirdMinHeight, skyBirdMaxHeight);
            float maxHeight = Mathf.Max(skyBirdMinHeight, skyBirdMaxHeight);
            Vector3 center = target.position + Vector3.up * Mathf.Lerp(minHeight, maxHeight, 0.5f);
            float radius = Mathf.Max(5f, skyBirdHorizontalRadius);

            for (int i = 0; i < _skyBirds.Count; i++)
            {
                var bird = _skyBirds[i];
                if (bird.transform == null)
                    continue;

                Vector3 pos = bird.transform.position;
                Vector3 planarDelta = new Vector3(pos.x - center.x, 0f, pos.z - center.z);
                float dist01 = Mathf.Clamp01(planarDelta.magnitude / radius);
                Vector3 returnDir = planarDelta.sqrMagnitude > 0.001f ? (-planarDelta.normalized) : Random.onUnitSphere;
                float verticalTarget = Mathf.Clamp(center.y + Random.Range(-skyBirdVerticalJitter, skyBirdVerticalJitter), target.position.y + minHeight, target.position.y + maxHeight);
                Vector3 desired = bird.velocity.normalized;

                if (Time.time >= bird.nextTurnAt)
                {
                    Vector3 randomDir = Random.onUnitSphere;
                    randomDir.y = Mathf.Clamp(randomDir.y, -0.55f, 0.55f);
                    desired = (randomDir + returnDir * (dist01 * skyBirdReturnWeight)).normalized;
                    desired.y += Mathf.Clamp((verticalTarget - pos.y) / Mathf.Max(1f, maxHeight), -0.35f, 0.35f);
                    desired.Normalize();
                    bird.nextTurnAt = Time.time + Random.Range(
                        Mathf.Min(skyBirdTurnIntervalMin, skyBirdTurnIntervalMax),
                        Mathf.Max(skyBirdTurnIntervalMin, skyBirdTurnIntervalMax));
                }
                else
                {
                    desired = (desired + returnDir * (dist01 * 0.35f)).normalized;
                    desired.y += Mathf.Clamp((verticalTarget - pos.y) * 0.05f, -0.2f, 0.2f);
                    desired.Normalize();
                }

                if (pos.y < target.position.y + minHeight)
                    desired.y = Mathf.Abs(desired.y) + 0.2f;
                else if (pos.y > target.position.y + maxHeight)
                    desired.y = -Mathf.Abs(desired.y) - 0.2f;

                bird.velocity = Vector3.Lerp(bird.velocity, desired * bird.speed, dt * 2.25f);
                pos += bird.velocity * dt;
                bird.transform.position = pos;

                Vector3 look = bird.velocity.sqrMagnitude > 0.0001f ? bird.velocity.normalized : bird.transform.forward;
                Quaternion targetRot = Quaternion.LookRotation(look, Vector3.up);
                bird.transform.rotation = Quaternion.Slerp(
                    bird.transform.rotation,
                    targetRot,
                    dt * Mathf.Max(0f, skyBirdRotationLerp));

                _skyBirds[i] = bird;
            }
        }

        void SpawnSkyBird()
        {
            if (skyBirdPrefab == null)
                return;

            var go = Instantiate(skyBirdPrefab, transform);
            go.name = $"SkyBird_{_skyBirds.Count}";
            SetLayerRecursive(go.transform, 0);

            Transform target = ResolveFollowTarget();
            Vector3 anchor = target != null ? target.position : transform.position;
            float minHeight = Mathf.Min(skyBirdMinHeight, skyBirdMaxHeight);
            float maxHeight = Mathf.Max(skyBirdMinHeight, skyBirdMaxHeight);
            Vector2 ring = Random.insideUnitCircle * Mathf.Max(5f, skyBirdHorizontalRadius);
            Vector3 pos = anchor + new Vector3(ring.x, Random.Range(minHeight, maxHeight), ring.y);
            go.transform.position = pos;

            float speed = Random.Range(Mathf.Min(skyBirdMinSpeed, skyBirdMaxSpeed), Mathf.Max(skyBirdMinSpeed, skyBirdMaxSpeed));
            Vector3 dir = Random.onUnitSphere;
            dir.y = Mathf.Clamp(dir.y, -0.4f, 0.4f);
            dir.Normalize();

            _skyBirds.Add(new SkyBirdAgent
            {
                transform = go.transform,
                velocity = dir * speed,
                speed = speed,
                nextTurnAt = Time.time + Random.Range(
                    Mathf.Min(skyBirdTurnIntervalMin, skyBirdTurnIntervalMax),
                    Mathf.Max(skyBirdTurnIntervalMin, skyBirdTurnIntervalMax))
            });
        }

        void RemoveSkyBird(int index)
        {
            if (index < 0 || index >= _skyBirds.Count)
                return;
            var bird = _skyBirds[index];
            if (bird.transform != null)
                Destroy(bird.transform.gameObject);
            _skyBirds.RemoveAt(index);
        }

        void ClearSkyBirds()
        {
            for (int i = _skyBirds.Count - 1; i >= 0; i--)
                RemoveSkyBird(i);
        }

        void AutoAssignBirdPrefab()
        {
#if UNITY_EDITOR
            if (skyBirdPrefab == null)
                skyBirdPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Modeling/_Incoming/bird.obj/bird.obj");
#endif
        }

        static void SetLayerRecursive(Transform root, int layer)
        {
            if (root == null)
                return;
            root.gameObject.layer = layer;
            for (int i = 0; i < root.childCount; i++)
                SetLayerRecursive(root.GetChild(i), layer);
        }

        void EnsureRainSystem()
        {
            if (rainParticleSystem != null) return;

            // Create a simple rain PS on demand.
            var go = new GameObject("RainVFX (Runtime)");
            go.transform.SetParent(transform, worldPositionStays: false);
            rainParticleSystem = go.AddComponent<ParticleSystem>();

            var main = rainParticleSystem.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = rainMaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(rainLifetimeRange.x, rainLifetimeRange.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(rainFallSpeedRange.x, rainFallSpeedRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(rainSizeRange.x, rainSizeRange.y);
            main.startColor = new Color(0.85f, 0.9f, 1f, 1f);

            var emission = rainParticleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = rainParticleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = rainBoxSize;

            var velocity = rainParticleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            // IMPORTANT: Unity requires x/y/z velocity curves to use the same mode.
            // Use a single constant downward velocity (rainParticleSystem.main.startSpeed already provides variation).
            float avgFall = -Mathf.Lerp(rainFallSpeedRange.x, rainFallSpeedRange.y, 0.7f);
            velocity.x = new ParticleSystem.MinMaxCurve(0f);
            velocity.y = new ParticleSystem.MinMaxCurve(avgFall);
            velocity.z = new ParticleSystem.MinMaxCurve(0f);

            var renderer = rainParticleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (rainMaterial != null)
                renderer.sharedMaterial = rainMaterial;

            // Start off (will be toggled by ApplyForPhase)
            rainParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void EnsureSnowSystem()
        {
            if (snowParticleSystem != null) return;

            var go = new GameObject("SnowVFX (Runtime)");
            go.transform.SetParent(transform, worldPositionStays: false);
            snowParticleSystem = go.AddComponent<ParticleSystem>();

            var main = snowParticleSystem.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = snowMaxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(snowLifetimeRange.x, snowLifetimeRange.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(snowFallSpeedRange.x, snowFallSpeedRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(snowSizeRange.x, snowSizeRange.y);
            main.startColor = new Color(1f, 1f, 1f, 0.95f);

            var emission = snowParticleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = snowParticleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = snowBoxSize;

            // Give snow a gentle drift.
            var velocity = snowParticleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            float avgFall = -Mathf.Lerp(snowFallSpeedRange.x, snowFallSpeedRange.y, 0.5f);
            velocity.x = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);
            velocity.y = new ParticleSystem.MinMaxCurve(avgFall);
            velocity.z = new ParticleSystem.MinMaxCurve(-0.3f, 0.3f);

            var noise = snowParticleSystem.noise;
            noise.enabled = true;
            noise.strength = 0.35f;
            noise.frequency = 0.25f;
            noise.scrollSpeed = 0.12f;

            var renderer = snowParticleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (snowMaterial != null)
                renderer.sharedMaterial = snowMaterial;

            snowParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        void EnsureFireflySystem()
        {
            if (fireflyParticleSystem != null) return;

            var go = new GameObject("FireflyVFX (Runtime)");
            go.transform.SetParent(transform, worldPositionStays: false);
            fireflyParticleSystem = go.AddComponent<ParticleSystem>();

            var main = fireflyParticleSystem.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(0, fireflyMaxParticles);
            main.startLifetime = new ParticleSystem.MinMaxCurve(fireflyLifetimeRange.x, fireflyLifetimeRange.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(fireflySpeedRange.x, fireflySpeedRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(fireflySizeRange.x, fireflySizeRange.y);
            main.startColor = new Color(1f, 0.98f, 0.65f, 1f);

            var emission = fireflyParticleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f; // driven in Update based on active state

            var shape = fireflyParticleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = fireflyBoxSize;

            // Gentle wander using noise.
            var noise = fireflyParticleSystem.noise;
            noise.enabled = true;
            noise.strength = 0.65f;
            noise.frequency = 0.12f;
            noise.scrollSpeed = 0.12f;
            noise.damping = true;

            // Blinking: alpha pulse over lifetime.
            var col = fireflyParticleSystem.colorOverLifetime;
            col.enabled = true;
            col.color = new ParticleSystem.MinMaxGradient(CreateFireflyBlinkGradient());

            var renderer = fireflyParticleSystem.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            if (fireflyMaterial != null)
                renderer.sharedMaterial = fireflyMaterial;

            fireflyParticleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        bool ShouldShowFireflies(WeatherPhase phase)
        {
            if (weatherSystem != null && weatherSystem.seasonSource != null)
            {
                var season = weatherSystem.seasonSource.GetCurrentSeason();
                if (season != SeasonSkyboxController.Season.Spring &&
                    season != SeasonSkyboxController.Season.Summer)
                    return false;
            }

            if (disableFirefliesInBadWeather)
            {
                if (phase == WeatherPhase.Thunderstorm || phase == WeatherPhase.Snow)
                    return false;
            }

            if (!firefliesOnlyAtNight)
                return true;

            if (dayNightController == null)
                dayNightController = FindFirstObjectByType<DayNightLightingController>();

            if (dayNightController == null)
                return true; // fallback: allow

            float t = Mathf.Repeat(dayNightController.timeOfDay01, 1f);
            bool isDay = SeasonSkyboxController.IsWithinWrappedRange(t, dayRange01.x, dayRange01.y);
            return !isDay;
        }

        void UpdateFireflyClusterTarget(WeatherPhase phase)
        {
            _hasFireflyClusterTarget = false;
            _fireflyClusterStrength = 1f;

            if (!clusterFirefliesAroundHealthyFlowers || fireflyParticleSystem == null || !ShouldShowFireflies(phase))
                return;
            if (_runtime == null || _runtime.Grid == null)
                return;

            int bestCount = 0;
            Vector3 bestCenter = fireflyParticleSystem.transform.position;
            float radius = Mathf.Max(1f, fireflyClusterSearchRadius);
            float radiusSqr = radius * radius;
            int cellRadius = Mathf.Max(1, Mathf.CeilToInt(radius / Mathf.Max(0.001f, _runtime.Grid.CellSize)));

            for (int y = 0; y < _runtime.Grid.Height; y++)
            for (int x = 0; x < _runtime.Grid.Width; x++)
            {
                var cell = _runtime.Grid.Get(x, y);
                if (!IsHealthyMatureFlower(cell))
                    continue;

                Vector3 center = _runtime.Grid.CellCenterWorld(x, y);
                int count = 0;
                Vector3 accum = Vector3.zero;

                for (int ny = y - cellRadius; ny <= y + cellRadius; ny++)
                for (int nx = x - cellRadius; nx <= x + cellRadius; nx++)
                {
                    if (!_runtime.Grid.InBounds(nx, ny))
                        continue;

                    var candidate = _runtime.Grid.Get(nx, ny);
                    if (!IsHealthyMatureFlower(candidate))
                        continue;

                    Vector3 p = _runtime.Grid.CellCenterWorld(nx, ny);
                    Vector3 d = p - center;
                    d.y = 0f;
                    if (d.sqrMagnitude > radiusSqr)
                        continue;

                    count++;
                    accum += p;
                }

                if (count >= Mathf.Max(2, fireflyClusterMinFlowers) && count > bestCount)
                {
                    bestCount = count;
                    bestCenter = accum / Mathf.Max(1, count);
                }
            }

            if (bestCount < Mathf.Max(2, fireflyClusterMinFlowers))
                return;

            _hasFireflyClusterTarget = true;
            _fireflyClusterStrength = Mathf.Clamp(bestCount / (float)Mathf.Max(1, fireflyClusterMinFlowers), 1f, 3f);
            _fireflyClusterTarget = bestCenter + fireflyClusterOffset;

            Vector3 current = fireflyParticleSystem.transform.position;
            float t = Mathf.Clamp01(Time.deltaTime * Mathf.Max(0.1f, fireflyClusterFollowLerp));
            fireflyParticleSystem.transform.position = Vector3.Lerp(current, _fireflyClusterTarget, t);
        }

        static bool IsHealthyMatureFlower(SCoL.CellState cell)
        {
            return cell != null &&
                   cell.HasPlant &&
                   cell.IsPlayerSeedLineage &&
                   cell.FlowerVariantIndex >= 0 &&
                   cell.PlantStage >= PlantStage.MediumTree &&
                   cell.PlantStage != PlantStage.Burnt &&
                   cell.PlantHealth > 0.35f;
        }

        static Gradient CreateFireflyBlinkGradient()
        {
            // Double-blink style by default.
            // Color is constant; alpha pulses.
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(1f, 0.98f, 0.65f), 0.00f),
                    new GradientColorKey(new Color(1f, 0.98f, 0.65f), 1.00f),
                },
                new[]
                {
                    new GradientAlphaKey(0f, 0.00f),
                    new GradientAlphaKey(1f, 0.22f),
                    new GradientAlphaKey(0f, 0.34f),
                    new GradientAlphaKey(1f, 0.48f),
                    new GradientAlphaKey(0f, 0.62f),
                    new GradientAlphaKey(0f, 1.00f),
                }
            );
            return g;
        }

        void ApplyWindZone(WeatherPhase phase)
        {
            if (windZone == null) return;

            // If the user didn't configure it, try to leave it enabled but with phase-tuned strength.
            windZone.gameObject.SetActive(true);

            windZone.mode = WindZoneMode.Directional;
            windZone.windMain = phase switch
            {
                WeatherPhase.Wind => windMainWindy,
                WeatherPhase.Rain => windMainRain,
                WeatherPhase.Thunderstorm => windMainThunder,
                WeatherPhase.Snow => windMainSnow,
                _ => windMainClear
            };
        }
    }
}
