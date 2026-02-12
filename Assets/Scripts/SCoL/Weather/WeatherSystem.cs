using System;
using UnityEngine;

namespace SCoL.Weather
{
    /// <summary>
    /// Global weather scheduler/state machine.
    ///
    /// Design goals:
    /// - Global (one weather at a time)
    /// - No dependency on day/night system, but can be configured with a "day length" to track rainy-day streaks
    /// - Clear is most common; Rain is second; Thunderstorm only occurs as escalation after prolonged rain
    /// - Tunable, deterministic (optional seed)
    ///
    /// This class does NOT do visuals by itself; consumers (VFX/audio/lighting) should subscribe to events
    /// or poll CurrentPhase/Intensity/Wind.
    /// </summary>
    public sealed class WeatherSystem : MonoBehaviour
    {
        [Header("Time Model")]
        [Tooltip("Seconds per in-game day (used only for rainy-day streak tracking).")]
        [Min(0.1f)] public float dayLengthSeconds = 20f;

        [Tooltip("How many seconds of Rain/Thunder within a day counts as a 'rainy day'.")]
        [Min(0f)] public float rainyDayThresholdSeconds = 8f;

        [Header("Randomness")]
        [Tooltip("If true, use a fixed seed for deterministic weather.")]
        public bool deterministic = false;

        public int randomSeed = 12345;

        [Header("Durations (seconds)")]
        public Vector2 clearDurationRange = new Vector2(10f, 20f);
        public Vector2 rainDurationRange = new Vector2(10f, 15f);

        [Tooltip("Thunderstorm duration (short burst).")]
        public Vector2 thunderDurationRange = new Vector2(4f, 6f);

        [Header("Base Weights")]
        [Range(0f, 1f)] public float clearWeight = 0.65f;
        [Range(0f, 1f)] public float rainWeight = 0.35f;

        [Tooltip("When currently raining, multiply rain's chance to continue by this factor.")]
        [Min(0f)] public float rainStickiness = 1.6f;

        [Header("Thunderstorm (Escalation)")]
        [Tooltip("Number of consecutive rainy days required before thunderstorms can occur.")]
        [Min(1)] public int thunderAfterConsecutiveRainyDays = 2;

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
        [Min(0f)] public float windSpeedRain = 0.8f;
        [Min(0f)] public float windSpeedThunder = 1.2f;

        [Header("Runtime (read-only)")]
        [SerializeField] WeatherPhase currentPhase = WeatherPhase.Clear;
        [SerializeField, Min(0f)] float currentPhaseRemainingSeconds = 0f;

        public WeatherPhase CurrentPhase => currentPhase;

        /// <summary>0..1 (you can interpret as light/medium/heavy later). For now, derived from phase.</summary>
        public float Intensity01 { get; private set; } = 0f;

        public event Action<WeatherPhase> OnPhaseStarted;
        public event Action<WeatherPhase> OnPhaseEnded;

        System.Random _rng;

        float _dayT;
        float _rainedThisDaySeconds;
        int _rainStreakDays;
        bool _thunderQueued;

        void Awake()
        {
            _rng = deterministic ? new System.Random(randomSeed) : new System.Random(Environment.TickCount);
        }

        void Start()
        {
            // Kick off with a clear segment by default.
            ForcePhase(WeatherPhase.Clear, SampleRange(clearDurationRange));
        }

        void Update()
        {
            float dt = Time.deltaTime;

            // --- day accounting
            _dayT += dt;

            if (currentPhase == WeatherPhase.Rain || currentPhase == WeatherPhase.Thunderstorm)
                _rainedThisDaySeconds += dt;

            while (_dayT >= dayLengthSeconds)
            {
                _dayT -= dayLengthSeconds;
                bool rainyDay = _rainedThisDaySeconds >= rainyDayThresholdSeconds;
                _rainedThisDaySeconds = 0f;

                if (rainyDay) _rainStreakDays++;
                else _rainStreakDays = 0;

                if (_rainStreakDays >= thunderAfterConsecutiveRainyDays)
                    _thunderQueued = true;
            }

            // --- phase countdown
            currentPhaseRemainingSeconds -= dt;
            if (currentPhaseRemainingSeconds <= 0f)
            {
                ScheduleNextPhase();
            }

            // --- derived intensity
            Intensity01 = currentPhase switch
            {
                WeatherPhase.Clear => 0f,
                WeatherPhase.Rain => 0.6f,
                WeatherPhase.Thunderstorm => 1f,
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
                WeatherPhase.Rain => windSpeedRain,
                WeatherPhase.Thunderstorm => windSpeedThunder,
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

            OnPhaseEnded?.Invoke(currentPhase);
            currentPhase = phase;
            currentPhaseRemainingSeconds = durationSeconds;
            OnPhaseStarted?.Invoke(currentPhase);
        }

        void ScheduleNextPhase()
        {
            // Thunderstorm: only as escalation, and preferably from Rain.
            if (_thunderQueued && (currentPhase == WeatherPhase.Rain || currentPhase == WeatherPhase.Thunderstorm))
            {
                bool trigger = thunderGuaranteedWhenQueued || _rng.NextDouble() < thunderTriggerProbability;
                if (trigger)
                {
                    _thunderQueued = false; // consume queue
                    ForcePhase(WeatherPhase.Thunderstorm, SampleRange(thunderDurationRange));
                    return;
                }
            }

            // Otherwise pick Clear vs Rain.
            float wClear = Mathf.Max(0f, clearWeight);
            float wRain = Mathf.Max(0f, rainWeight);

            if (currentPhase == WeatherPhase.Rain || currentPhase == WeatherPhase.Thunderstorm)
                wRain *= Mathf.Max(0f, rainStickiness);

            // Normalize
            float sum = wClear + wRain;
            if (sum <= 0f)
            {
                wClear = 1f;
                wRain = 0f;
                sum = 1f;
            }

            double roll = _rng.NextDouble() * sum;
            WeatherPhase next = (roll < wClear) ? WeatherPhase.Clear : WeatherPhase.Rain;

            float dur = next == WeatherPhase.Clear ? SampleRange(clearDurationRange) : SampleRange(rainDurationRange);
            ForcePhase(next, dur);
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
