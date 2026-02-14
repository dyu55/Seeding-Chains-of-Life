using UnityEngine;
using SCoL.Weather;

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

        [Tooltip("Vertical offset above camera for precipitation volumes.")]
        public float heightOffset = 2.0f;

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

        [Header("Wind (optional)")]
        [Tooltip("Optional WindZone to enable/adjust during Wind/Rain/Thunder.")]
        public WindZone windZone;

        [Min(0f)] public float windMainClear = 0.0f;
        [Min(0f)] public float windMainWindy = 0.75f;
        [Min(0f)] public float windMainRain = 0.35f;
        [Min(0f)] public float windMainThunder = 0.55f;
        [Min(0f)] public float windMainSnow = 0.25f;

        [Header("Performance")]
        [Tooltip("If true, disables VFX in Edit Mode (always) and only runs during Play Mode.")]
        public bool playModeOnly = true;

        WeatherPhase _lastPhase;
        bool _hasLast;

        void Reset()
        {
            // Best effort auto-wire
            weatherSystem = FindFirstObjectByType<WeatherSystem>();
        }

        void Start()
        {
            if (playModeOnly && !Application.isPlaying) return;

            if (weatherSystem == null)
                weatherSystem = FindFirstObjectByType<WeatherSystem>();

            EnsureRainSystem();
            EnsureSnowSystem();
            ApplyForPhase(weatherSystem != null ? weatherSystem.CurrentPhase : WeatherPhase.Clear, force: true);
        }

        void Update()
        {
            if (playModeOnly && !Application.isPlaying) return;

            if (weatherSystem == null)
                weatherSystem = FindFirstObjectByType<WeatherSystem>();

            // Follow camera/target
            if (followMainCamera)
            {
                Transform target = followTarget;
                if (target == null)
                {
                    var cam = Camera.main;
                    target = cam != null ? cam.transform : null;
                }

                if (target != null)
                {
                    Vector3 p = target.position;
                    p.y += heightOffset;

                    if (rainParticleSystem != null)
                        rainParticleSystem.transform.position = p;

                    if (snowParticleSystem != null)
                        snowParticleSystem.transform.position = p;
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

                if (rainParticleSystem != null)
                {
                    bool rainingNow = phase == WeatherPhase.Rain || (rainAlsoInThunderstorm && phase == WeatherPhase.Thunderstorm);
                    var em = rainParticleSystem.emission;
                    em.rateOverTime = rainingNow ? (rainEmissionRate * Mathf.Max(0.2f, intensity)) : 0f;
                }

                if (snowParticleSystem != null)
                {
                    bool snowingNow = phase == WeatherPhase.Snow;
                    var em = snowParticleSystem.emission;
                    em.rateOverTime = snowingNow ? (snowEmissionRate * Mathf.Max(0.2f, intensity)) : 0f;
                }
            }
        }

        void ApplyForPhase(WeatherPhase phase, bool force)
        {
            EnsureRainSystem();
            EnsureSnowSystem();

            bool shouldRain = phase == WeatherPhase.Rain || (rainAlsoInThunderstorm && phase == WeatherPhase.Thunderstorm);
            bool shouldSnow = phase == WeatherPhase.Snow;

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

            ApplyWindZone(phase);
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
