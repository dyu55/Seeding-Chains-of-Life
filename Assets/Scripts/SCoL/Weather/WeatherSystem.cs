using System;
using UnityEngine;
using SCoL.Visualization;

namespace SCoL.Weather
{
    /// <summary>
    /// Global weather scheduler/state machine.
    ///
    /// Design goals:
    /// - Global (one weather at a time)
    /// - Season-aware: each season has its own frequency weights
    /// - Thunderstorm only occurs as escalation after 2 consecutive Rain segments (sessions)
    /// - Tunable, deterministic (optional seed)
    ///
    /// This class does NOT do visuals by itself; consumers (VFX/audio/lighting) should subscribe to events
    /// or poll CurrentPhase/Intensity/Wind.
    /// </summary>
    public sealed class WeatherSystem : MonoBehaviour
    {
        [Serializable]
        public struct SeasonWeights
        {
            [Range(0f, 1f)] public float clear;
            [Range(0f, 1f)] public float rain;
            [Range(0f, 1f)] public float wind;
            [Range(0f, 1f)] public float snow;

            public static SeasonWeights Normalize(SeasonWeights w)
            {
                // NOTE: thunder is not a base weight; it's escalation-only.
                float c = Mathf.Max(0f, w.clear);
                float r = Mathf.Max(0f, w.rain);
                float wi = Mathf.Max(0f, w.wind);
                float s = Mathf.Max(0f, w.snow);

                float sum = c + r + wi + s;
                if (sum <= 0f)
                {
                    w.clear = 1f;
                    w.rain = w.wind = w.snow = 0f;
                    return w;
                }

                w.clear = c / sum;
                w.rain = r / sum;
                w.wind = wi / sum;
                w.snow = s / sum;
                return w;
            }
        }

        [Header("Season Source")]
        [Tooltip("Optional: drives seasonal weather frequencies. If null, defaults to Spring config.")]
        public SeasonSkyboxController seasonSource;

        [Header("Seasonal Frequencies (weights)")]
        [Tooltip("Spring target: Clear ~70%, Rain ~30% (Thunder is escalation-only).")]
        public SeasonWeights spring = new SeasonWeights { clear = 0.70f, rain = 0.30f, wind = 0f, snow = 0f };

        [Tooltip("Summer target: Clear ~60%, Rain ~40% (Thunder is escalation-only).")]
        public SeasonWeights summer = new SeasonWeights { clear = 0.60f, rain = 0.40f, wind = 0f, snow = 0f };

        [Tooltip("Autumn target: Clear ~40%, Wind ~50%, remaining Rain. Thunder is escalation-only.")]
        public SeasonWeights autumn = new SeasonWeights { clear = 0.40f, rain = 0.10f, wind = 0.50f, snow = 0f };

        [Tooltip("Winter target: Snow ~80%, Clear ~20%.")]
        public SeasonWeights winter = new SeasonWeights { clear = 0.20f, rain = 0f, wind = 0f, snow = 0.80f };

        [Header("Randomness")]
        [Tooltip("If true, use a fixed seed for deterministic weather.")]
        public bool deterministic = false;

        public int randomSeed = 12345;

        [Header("Durations (seconds)")]
        public Vector2 clearDurationRange = new Vector2(10f, 20f);
        public Vector2 windDurationRange = new Vector2(8f, 14f);
        public Vector2 rainDurationRange = new Vector2(10f, 15f);

        [Tooltip("Thunderstorm duration (short burst).")]
        public Vector2 thunderDurationRange = new Vector2(4f, 6f);

        public Vector2 snowDurationRange = new Vector2(12f, 20f);

        [Header("Behavior")]
        [Tooltip("When currently in the same category, multiply its chance to continue by this factor.")]
        [Min(0f)] public float phaseStickiness = 1.25f;

        [Header("Thunderstorm (Escalation)")]
        [Tooltip("Number of consecutive Rain segments required before thunderstorms can occur.")]
        [Min(1)] public int thunderAfterConsecutiveRainSegments = 2;

        [Tooltip("If true, when the condition is met, the next eligible transition will be a thunderstorm.")]
        public bool thunderGuaranteedWhenQueued = true;

        [Range(0f, 1f)]
        [Tooltip("If not guaranteed, probability that the queued thunder actually triggers at the next eligible transition.")]
        public float thunderTriggerProbability = 0.8f;

        [Header("Wind (optional)")]
        public bool enableWind = true;

        [Tooltip("Base wind direction in world space (normalized at runtime).")]
        public Vector3 windDirection = new Vector3(1f, 0f, 0f);

        [Min(0f)] public float windSpeedClear = 0.4f;
        [Min(0f)] public float windSpeedWind = 1.0f;
        [Min(0f)] public float windSpeedRain = 0.8f;
        [Min(0f)] public float windSpeedThunder = 1.2f;
        [Min(0f)] public float windSpeedSnow = 0.6f;

        [Header("Runtime (read-only)")]
        [SerializeField] WeatherPhase currentPhase = WeatherPhase.Clear;
        [SerializeField, Min(0f)] float currentPhaseRemainingSeconds = 0f;

        public WeatherPhase CurrentPhase => currentPhase;

        /// <summary>0..1 (you can interpret as light/medium/heavy later). For now, derived from phase.</summary>
        public float Intensity01 { get; private set; } = 0f;

        public event Action<WeatherPhase> OnPhaseStarted;
        public event Action<WeatherPhase> OnPhaseEnded;

        System.Random _rng;

        int _consecutiveRainSegments;
        bool _thunderQueued;

        void Awake()
        {
            _rng = deterministic ? new System.Random(randomSeed) : new System.Random(Environment.TickCount);
        }

        void Start()
        {
            if (seasonSource == null)
                seasonSource = FindFirstObjectByType<SeasonSkyboxController>();

            // Kick off with a clear segment by default.
            ForcePhase(WeatherPhase.Clear, SampleRange(clearDurationRange));
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // --- phase countdown
            currentPhaseRemainingSeconds -= dt;
            if (currentPhaseRemainingSeconds <= 0f)
                ScheduleNextPhase();

            // --- derived intensity
            Intensity01 = currentPhase switch
            {
                WeatherPhase.Clear => 0f,
                WeatherPhase.Wind => 0.35f,
                WeatherPhase.Rain => 0.6f,
                WeatherPhase.Thunderstorm => 1f,
                WeatherPhase.Snow => 0.55f,
                _ => 0f
            };
        }

        public Vector3 GetWindVelocity(Vector3 _worldPos)
        {
            if (!enableWind) return Vector3.zero;

            Vector3 dir = windDirection;
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.right;
            dir.Normalize();

            float speed = currentPhase switch
            {
                WeatherPhase.Clear => windSpeedClear,
                WeatherPhase.Wind => windSpeedWind,
                WeatherPhase.Rain => windSpeedRain,
                WeatherPhase.Thunderstorm => windSpeedThunder,
                WeatherPhase.Snow => windSpeedSnow,
                _ => windSpeedClear
            };

            return dir * speed;
        }

        public void ForcePhase(WeatherPhase phase, float durationSeconds)
        {
            if (durationSeconds <= 0f) durationSeconds = 0.01f;

            if (phase == currentPhase)
            {
                currentPhaseRemainingSeconds = durationSeconds;
                return;
            }

            // End previous
            OnPhaseEnded?.Invoke(currentPhase);

            // Start new
            currentPhase = phase;
            currentPhaseRemainingSeconds = durationSeconds;

            // Maintain "2 consecutive rain sessions" rule state.
            if (phase == WeatherPhase.Rain)
            {
                _consecutiveRainSegments++;
                if (_consecutiveRainSegments >= thunderAfterConsecutiveRainSegments)
                    _thunderQueued = true;
            }
            else
            {
                // Thunder counts as breaking the rain streak (otherwise you'd chain forever).
                _consecutiveRainSegments = 0;
            }

            OnPhaseStarted?.Invoke(currentPhase);
        }

        void ScheduleNextPhase()
        {
            // Thunderstorm: escalation-only and only from Rain.
            if (_thunderQueued && currentPhase == WeatherPhase.Rain)
            {
                bool trigger = thunderGuaranteedWhenQueued || _rng.NextDouble() < thunderTriggerProbability;
                if (trigger)
                {
                    _thunderQueued = false; // consume queue
                    ForcePhase(WeatherPhase.Thunderstorm, SampleRange(thunderDurationRange));
                    return;
                }
            }

            // Determine current season weights.
            SeasonWeights w = SeasonWeights.Normalize(GetWeightsForCurrentSeason());

            // Apply stickiness for current phase (reduces jitter).
            // Only affects base-roll phases (Clear/Wind/Rain/Snow).
            float stick = Mathf.Max(0f, phaseStickiness);
            if (stick > 0f)
            {
                switch (currentPhase)
                {
                    case WeatherPhase.Clear: w.clear *= stick; break;
                    case WeatherPhase.Wind: w.wind *= stick; break;
                    case WeatherPhase.Rain: w.rain *= stick; break;
                    case WeatherPhase.Snow: w.snow *= stick; break;
                }
            }

            // Roll
            float sum = Mathf.Max(0f, w.clear) + Mathf.Max(0f, w.wind) + Mathf.Max(0f, w.rain) + Mathf.Max(0f, w.snow);
            if (sum <= 0f)
            {
                ForcePhase(WeatherPhase.Clear, SampleRange(clearDurationRange));
                return;
            }

            double roll = _rng.NextDouble() * sum;
            WeatherPhase next;

            double c = Mathf.Max(0f, w.clear);
            double wi = Mathf.Max(0f, w.wind);
            double r = Mathf.Max(0f, w.rain);
            // snow implied

            if (roll < c) next = WeatherPhase.Clear;
            else if (roll < c + wi) next = WeatherPhase.Wind;
            else if (roll < c + wi + r) next = WeatherPhase.Rain;
            else next = WeatherPhase.Snow;

            float dur = next switch
            {
                WeatherPhase.Clear => SampleRange(clearDurationRange),
                WeatherPhase.Wind => SampleRange(windDurationRange),
                WeatherPhase.Rain => SampleRange(rainDurationRange),
                WeatherPhase.Snow => SampleRange(snowDurationRange),
                _ => SampleRange(clearDurationRange)
            };

            ForcePhase(next, dur);
        }

        SeasonWeights GetWeightsForCurrentSeason()
        {
            if (seasonSource == null)
                return spring;

            // SeasonSkyboxController defines its own Season enum. Map it to weights.
            return seasonSource.GetCurrentSeason() switch
            {
                SeasonSkyboxController.Season.Spring => spring,
                SeasonSkyboxController.Season.Summer => summer,
                SeasonSkyboxController.Season.Autumn => autumn,
                SeasonSkyboxController.Season.Winter => winter,
                _ => spring
            };
        }

        float SampleRange(Vector2 range)
        {
            float a = Mathf.Min(range.x, range.y);
            float b = Mathf.Max(range.x, range.y);
            if (Mathf.Approximately(a, b)) return a;

            // System.Random -> [0,1)
            float t = (float)_rng.NextDouble();
            return Mathf.Lerp(a, b, t);
        }
    }
}
