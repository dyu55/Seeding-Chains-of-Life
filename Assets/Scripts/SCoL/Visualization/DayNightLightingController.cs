using UnityEngine;
using SCoL.Weather;

namespace SCoL.Visualization
{
    /// <summary>
    /// Simple day/night cycle controller (Minecraft-style): drive directional light + ambient + fog
    /// based on a normalized time-of-day value.
    ///
    /// Attach to any GameObject in your scene.
    ///
    /// Key concepts:
    /// - timeOfDay01: 0..1 (0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset)
    /// - cycleDurationSeconds: if > 0 and advanceTime is true, timeOfDay01 advances automatically.
    ///
    /// Works with URP/Built-in. Optional: updates DynamicGI environment at a throttled rate.
    /// </summary>
    public class DayNightLightingController : MonoBehaviour
    {
        [Header("Time")]
        [Range(0f, 1f)] public float timeOfDay01 = 0.5f;

        [Tooltip("If true, timeOfDay01 advances automatically.")]
        public bool advanceTime = true;

        [Tooltip("Seconds for a full day (0..1). Example: 1200 = 20 minutes (Minecraft-ish).")]
        [Min(0f)] public float cycleDurationSeconds = 1200f;

        [Tooltip("Start the cycle at this time when entering Play Mode.")]
        public bool setTimeOnStart = false;

        [Range(0f, 1f)] public float startTime01 = 0.5f;

        [Header("Sun / Moon Light")]
        [Tooltip("Directional light to rotate and color (your Sun light).")]
        public Light directionalLight;

        [Tooltip("Rotation around X in degrees across the day. -90..270 yields a full arc.")]
        public Vector2 sunPitchRange = new Vector2(-90f, 270f);

        [Tooltip("Rotation around Y in degrees (sets the compass direction of sunrise/sunset).")]
        public float sunYaw = 0f;

        [Tooltip("Directional light intensity over time.")]
        public AnimationCurve sunIntensity = DefaultSunIntensity();

        [Tooltip("Directional light color over time.")]
        public Gradient sunColor = DefaultSunColor();

        [Header("Ambient / Reflections")]
        [Tooltip("Ambient intensity over time (RenderSettings.ambientIntensity).")]
        public AnimationCurve ambientIntensity = DefaultAmbientIntensity();

        [Tooltip("Reflection intensity over time (RenderSettings.reflectionIntensity).")]
        public AnimationCurve reflectionIntensity = DefaultReflectionIntensity();

        [Header("Fog (optional)")]
        public bool driveFog = true;

        [Header("Weather (optional)")]
        [Tooltip("Optional global WeatherSystem. If assigned, lighting/fog will respond to weather and thunderstorms can flash.")]
        public WeatherSystem weatherSystem;

        [Tooltip("Multiplier applied to fog density during rain.")]
        [Min(0f)] public float fogDensityRainMultiplier = 1.35f;

        [Tooltip("Multiplier applied to fog density during thunderstorms.")]
        [Min(0f)] public float fogDensityThunderMultiplier = 1.6f;

        [Tooltip("Multiplier applied to ambient intensity during rain/thunder.")]
        [Range(0f, 1f)] public float ambientDimmingInBadWeather = 0.85f;

        [Header("Weather Audio (optional)")]
        [Tooltip("Optional AudioSource used to play looping weather ambience.")]
        public AudioSource weatherAudioSource;

        [Tooltip("Loop played during Clear weather. Leave null for silence.")]
        public AudioClip clearLoop;

        [Tooltip("Loop played during Wind (autumn windy weather, etc.). Leave null for silence.")]
        public AudioClip windLoop;

        [Tooltip("Loop played during Snow. Leave null for silence.")]
        public AudioClip snowLoop;

        [Tooltip("Loop played during Rain.")]
        public AudioClip rainLoop;

        [Tooltip("Loop played during Thunderstorm.")]
        public AudioClip thunderLoop;

        [Range(0f, 1f)] public float weatherVolume = 0.9f;

        [Header("Thunderstorm Lightning Flash (optional)")]
        public bool enableThunderFlash = true;

        [Tooltip("Seconds the flash stays bright.")]
        [Range(0.01f, 0.5f)] public float lightningFlashDuration = 0.08f;

        [Tooltip("Random time range between flashes while thunderstorm is active.")]
        public Vector2 lightningFlashIntervalRange = new Vector2(0.6f, 1.6f);

        [Tooltip("Intensity multiplier applied to the directional light during a flash.")]
        [Min(1f)] public float lightningIntensityMultiplier = 3.0f;

        [Tooltip("Color used during the flash.")]
        public Color lightningFlashColor = new Color(0.95f, 0.97f, 1.0f);

        [Tooltip("Fog color over time.")]
        public Gradient fogColor = DefaultFogColor();

        [Tooltip("Fog density over time (for exponential fog).")]
        public AnimationCurve fogDensity = DefaultFogDensity();

        [Header("Performance")]
        [Tooltip("If enabled, calls DynamicGI.UpdateEnvironment() at a throttled interval.")]
        public bool updateEnvironmentGI = true;

        [Min(0f)] public float giUpdateIntervalSeconds = 2f;

        float _giT;

        float _flashT;
        float _nextFlashIn;
        float _preFlashIntensity;
        Color _preFlashColor;
        bool _hasPreFlash;

        WeatherPhase _lastWeatherPhase;
        bool _hasLastWeatherPhase;

        void Reset()
        {
            // Auto-find a directional light
            if (directionalLight == null)
            {
                foreach (var l in FindObjectsByType<Light>(FindObjectsSortMode.None))
                {
                    if (l != null && l.type == LightType.Directional)
                    {
                        directionalLight = l;
                        break;
                    }
                }
            }
        }

        void OnEnable()
        {
            if (weatherSystem != null)
                weatherSystem.OnPhaseStarted += HandleWeatherPhaseStarted;
        }

        void OnDisable()
        {
            if (weatherSystem != null)
                weatherSystem.OnPhaseStarted -= HandleWeatherPhaseStarted;
        }

        void Start()
        {
            if (setTimeOnStart)
                timeOfDay01 = startTime01;

            Apply(forceGI: true);
            _nextFlashIn = SampleFlashInterval();

            // Initialize audio for the starting phase.
            if (Application.isPlaying && weatherSystem != null)
                ApplyWeatherAudio(weatherSystem.CurrentPhase);
        }

        void Update()
        {
            if (advanceTime && cycleDurationSeconds > 0f)
            {
                timeOfDay01 += Time.deltaTime / cycleDurationSeconds;
                if (timeOfDay01 > 1f) timeOfDay01 -= 1f;
            }

            // Fallback: if event subscription didn't happen (e.g., references assigned late),
            // poll the current phase and update audio when it changes.
            if (Application.isPlaying && weatherSystem != null)
            {
                var p = weatherSystem.CurrentPhase;
                if (!_hasLastWeatherPhase || p != _lastWeatherPhase)
                {
                    _lastWeatherPhase = p;
                    _hasLastWeatherPhase = true;
                    ApplyWeatherAudio(p);
                }
            }

            Apply(forceGI: false);
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            // Apply in edit mode too (nice for tuning curves/gradients)
            Apply(forceGI: true);
        }
#endif

        void Apply(bool forceGI)
        {
            float t = Mathf.Repeat(timeOfDay01, 1f);

            // --- Directional light (sun)
            if (directionalLight != null)
            {
                float pitch = Mathf.Lerp(sunPitchRange.x, sunPitchRange.y, t);
                directionalLight.transform.rotation = Quaternion.Euler(pitch, sunYaw, 0f);

                directionalLight.intensity = Mathf.Max(0f, sunIntensity.Evaluate(t));
                directionalLight.color = sunColor.Evaluate(t);

                // Optional thunderstorm lightning flash (visual only).
                if (enableThunderFlash && weatherSystem != null && Application.isPlaying)
                {
                    bool inThunder = weatherSystem.CurrentPhase == WeatherPhase.Thunderstorm;

                    // Countdown to next flash while in thunder.
                    if (inThunder)
                    {
                        _nextFlashIn -= Time.deltaTime;
                        if (_nextFlashIn <= 0f)
                        {
                            _flashT = lightningFlashDuration;
                            _nextFlashIn = SampleFlashInterval();

                            if (!_hasPreFlash)
                            {
                                _preFlashIntensity = directionalLight.intensity;
                                _preFlashColor = directionalLight.color;
                                _hasPreFlash = true;
                            }
                        }
                    }
                    else
                    {
                        _flashT = 0f;
                        _hasPreFlash = false;
                        _nextFlashIn = SampleFlashInterval();
                    }

                    // Apply flash if active.
                    if (_flashT > 0f)
                    {
                        _flashT -= Time.deltaTime;
                        float baseIntensity = _hasPreFlash ? _preFlashIntensity : directionalLight.intensity;
                        Color baseColor = _hasPreFlash ? _preFlashColor : directionalLight.color;

                        directionalLight.intensity = baseIntensity * Mathf.Max(1f, lightningIntensityMultiplier);
                        directionalLight.color = lightningFlashColor;

                        if (_flashT <= 0f)
                        {
                            directionalLight.intensity = baseIntensity;
                            directionalLight.color = baseColor;
                            _hasPreFlash = false;
                        }
                    }
                }
            }

            // --- Ambient + reflections
            float ambient = Mathf.Max(0f, ambientIntensity.Evaluate(t));
            float reflections = Mathf.Max(0f, reflectionIntensity.Evaluate(t));

            if (weatherSystem != null)
            {
                if (weatherSystem.CurrentPhase == WeatherPhase.Rain || weatherSystem.CurrentPhase == WeatherPhase.Thunderstorm)
                {
                    ambient *= Mathf.Clamp01(ambientDimmingInBadWeather);
                    reflections *= Mathf.Clamp01(ambientDimmingInBadWeather);
                }
            }

            RenderSettings.ambientIntensity = ambient;
            RenderSettings.reflectionIntensity = reflections;

            // --- Fog
            if (driveFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogColor = fogColor.Evaluate(t);

                float d = Mathf.Max(0f, fogDensity.Evaluate(t));
                if (weatherSystem != null)
                {
                    if (weatherSystem.CurrentPhase == WeatherPhase.Rain)
                        d *= Mathf.Max(0f, fogDensityRainMultiplier);
                    else if (weatherSystem.CurrentPhase == WeatherPhase.Thunderstorm)
                        d *= Mathf.Max(0f, fogDensityThunderMultiplier);
                }

                RenderSettings.fogDensity = d;
            }

            // --- GI update (throttled)
            if (!updateEnvironmentGI) return;

            if (forceGI || giUpdateIntervalSeconds <= 0f)
            {
                DynamicGI.UpdateEnvironment();
                _giT = 0f;
                return;
            }

            _giT += Time.unscaledDeltaTime;
            if (_giT >= giUpdateIntervalSeconds)
            {
                _giT = 0f;
                DynamicGI.UpdateEnvironment();
            }
        }

        void HandleWeatherPhaseStarted(WeatherPhase phase)
        {
            ApplyWeatherAudio(phase);
        }

        void ApplyWeatherAudio(WeatherPhase phase)
        {
            if (weatherAudioSource == null) return;

            weatherAudioSource.spatialBlend = 0f; // 2D
            weatherAudioSource.volume = Mathf.Clamp01(weatherVolume);

            AudioClip target = phase switch
            {
                WeatherPhase.Clear => clearLoop,
                WeatherPhase.Wind => windLoop,
                WeatherPhase.Snow => snowLoop,
                WeatherPhase.Rain => rainLoop,
                WeatherPhase.Thunderstorm => thunderLoop,
                _ => null
            };

            if (target == null)
            {
                if (weatherAudioSource.isPlaying)
                    weatherAudioSource.Stop();
                weatherAudioSource.clip = null;
                return;
            }

            // Swap loop if needed.
            if (weatherAudioSource.clip != target)
            {
                weatherAudioSource.clip = target;
                weatherAudioSource.loop = true;
                weatherAudioSource.Play();
            }
            else if (!weatherAudioSource.isPlaying)
            {
                weatherAudioSource.loop = true;
                weatherAudioSource.Play();
            }
        }

        float SampleFlashInterval()
        {
            float a = Mathf.Min(lightningFlashIntervalRange.x, lightningFlashIntervalRange.y);
            float b = Mathf.Max(lightningFlashIntervalRange.x, lightningFlashIntervalRange.y);
            if (Mathf.Approximately(a, b)) return a;
            return UnityEngine.Random.Range(a, b);
        }

        // -------- defaults --------

        static AnimationCurve DefaultSunIntensity()
        {
            // Midnight(0): 0
            // Sunrise(0.23): 0.2
            // Noon(0.5): 1
            // Sunset(0.77): 0.2
            // Midnight(1): 0
            return new AnimationCurve(
                new Keyframe(0.00f, 0.00f),
                new Keyframe(0.20f, 0.00f),
                new Keyframe(0.25f, 0.35f),
                new Keyframe(0.50f, 1.10f),
                new Keyframe(0.75f, 0.35f),
                new Keyframe(0.80f, 0.00f),
                new Keyframe(1.00f, 0.00f)
            );
        }

        static Gradient DefaultSunColor()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.12f, 0.16f, 0.30f), 0.00f), // night blue
                    new GradientColorKey(new Color(1.00f, 0.58f, 0.32f), 0.25f), // warm sunrise
                    new GradientColorKey(new Color(1.00f, 0.98f, 0.92f), 0.50f), // near-white noon
                    new GradientColorKey(new Color(1.00f, 0.50f, 0.30f), 0.75f), // warm sunset
                    new GradientColorKey(new Color(0.12f, 0.16f, 0.30f), 1.00f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                }
            );
            return g;
        }

        static AnimationCurve DefaultAmbientIntensity()
        {
            return new AnimationCurve(
                new Keyframe(0.00f, 0.20f),
                new Keyframe(0.25f, 0.70f),
                new Keyframe(0.50f, 1.00f),
                new Keyframe(0.75f, 0.70f),
                new Keyframe(1.00f, 0.20f)
            );
        }

        static AnimationCurve DefaultReflectionIntensity()
        {
            return new AnimationCurve(
                new Keyframe(0.00f, 0.20f),
                new Keyframe(0.25f, 0.60f),
                new Keyframe(0.50f, 1.00f),
                new Keyframe(0.75f, 0.60f),
                new Keyframe(1.00f, 0.20f)
            );
        }

        static Gradient DefaultFogColor()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.03f, 0.05f, 0.10f), 0.00f),
                    new GradientColorKey(new Color(0.85f, 0.55f, 0.45f), 0.25f),
                    new GradientColorKey(new Color(0.75f, 0.85f, 0.95f), 0.50f),
                    new GradientColorKey(new Color(0.85f, 0.50f, 0.40f), 0.75f),
                    new GradientColorKey(new Color(0.03f, 0.05f, 0.10f), 1.00f),
                },
                new[]
                {
                    new GradientAlphaKey(1f, 0f),
                    new GradientAlphaKey(1f, 1f),
                }
            );
            return g;
        }

        static AnimationCurve DefaultFogDensity()
        {
            // Very subtle fog changes (tune to taste; VR often prefers less fog).
            return new AnimationCurve(
                new Keyframe(0.00f, 0.010f),
                new Keyframe(0.25f, 0.006f),
                new Keyframe(0.50f, 0.004f),
                new Keyframe(0.75f, 0.006f),
                new Keyframe(1.00f, 0.010f)
            );
        }
    }
}
