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
    /// Thunderstorm visuals are currently handled by DayNightLightingController (flash).
    /// This class focuses on rain.
    /// </summary>
    public sealed class WeatherVFXController : MonoBehaviour
    {
        [Header("References")]
        public WeatherSystem weatherSystem;

        [Tooltip("If true, the rain effect follows a camera/target transform.")]
        public bool followMainCamera = true;

        [Tooltip("Optional explicit target to follow (recommended for XR). If null, uses Camera.main.")]
        public Transform followTarget;

        [Header("Rain VFX")]
        [Tooltip("Optional ParticleSystem. If null, one will be created at runtime.")]
        public ParticleSystem rainParticleSystem;

        [Tooltip("Material used for the runtime-created rain ParticleSystem renderer.")]
        public Material rainMaterial;

        [Tooltip("Enable rain during Thunderstorm as well.")]
        public bool rainAlsoInThunderstorm = true;

        [Tooltip("Max particles to keep alive. Lower this on laptops/VR.")]
        [Range(100, 10000)] public int maxParticles = 2500;

        [Tooltip("Emission rate (particles/sec) at full rain intensity.")]
        [Range(0f, 5000f)] public float emissionRate = 900f;

        [Tooltip("Area size around camera where rain spawns.")]
        public Vector3 boxSize = new Vector3(20f, 6f, 20f);

        [Tooltip("Fall speed range (m/s).")]
        public Vector2 fallSpeedRange = new Vector2(10f, 18f);

        [Tooltip("Particle lifetime (seconds).")]
        public Vector2 lifetimeRange = new Vector2(0.8f, 1.6f);

        [Tooltip("Particle size range.")]
        public Vector2 sizeRange = new Vector2(0.2f, 0.5f);

        [Tooltip("Vertical offset above camera.")]
        public float heightOffset = 2.0f;

        [Header("Performance")]
        [Tooltip("If true, disables rain in Edit Mode (always) and only runs during Play Mode.")]
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
                    var tr = rainParticleSystem != null ? rainParticleSystem.transform : null;
                    if (tr != null)
                    {
                        Vector3 p = target.position;
                        p.y += heightOffset;
                        tr.position = p;
                    }
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
            if (rainParticleSystem != null)
            {
                bool rainingNow = phase == WeatherPhase.Rain || (rainAlsoInThunderstorm && phase == WeatherPhase.Thunderstorm);
                if (rainingNow)
                {
                    float intensity = Mathf.Clamp01(weatherSystem.Intensity01);
                    var em = rainParticleSystem.emission;
                    em.rateOverTime = emissionRate * Mathf.Max(0.2f, intensity);
                }
                else
                {
                    // Ensure emission stays off in non-rain phases (Wind/Snow/Clear).
                    var em = rainParticleSystem.emission;
                    em.rateOverTime = 0f;
                }
            }
        }

        void ApplyForPhase(WeatherPhase phase, bool force)
        {
            EnsureRainSystem();

            bool shouldRain = phase == WeatherPhase.Rain || (rainAlsoInThunderstorm && phase == WeatherPhase.Thunderstorm);

            if (rainParticleSystem == null) return;

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
            main.maxParticles = maxParticles;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifetimeRange.x, lifetimeRange.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(fallSpeedRange.x, fallSpeedRange.y);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeRange.x, sizeRange.y);
            main.startColor = new Color(0.85f, 0.9f, 1f, 1f);

            var emission = rainParticleSystem.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;

            var shape = rainParticleSystem.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;

            var velocity = rainParticleSystem.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;

            // IMPORTANT: Unity requires x/y/z velocity curves to use the same mode.
            // Use a single constant downward velocity (rainParticleSystem.main.startSpeed already provides variation).
            float avgFall = -Mathf.Lerp(fallSpeedRange.x, fallSpeedRange.y, 0.7f);
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
    }
}
