using UnityEngine;

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

        [Tooltip("Fog color over time.")]
        public Gradient fogColor = DefaultFogColor();

        [Tooltip("Fog density over time (for exponential fog).")]
        public AnimationCurve fogDensity = DefaultFogDensity();

        [Header("Performance")]
        [Tooltip("If enabled, calls DynamicGI.UpdateEnvironment() at a throttled interval.")]
        public bool updateEnvironmentGI = true;

        [Min(0f)] public float giUpdateIntervalSeconds = 2f;

        float _giT;

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

        void Start()
        {
            if (setTimeOnStart)
                timeOfDay01 = startTime01;

            Apply(forceGI: true);
        }

        void Update()
        {
            if (advanceTime && cycleDurationSeconds > 0f)
            {
                timeOfDay01 += Time.deltaTime / cycleDurationSeconds;
                if (timeOfDay01 > 1f) timeOfDay01 -= 1f;
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
            }

            // --- Ambient + reflections
            RenderSettings.ambientIntensity = Mathf.Max(0f, ambientIntensity.Evaluate(t));
            RenderSettings.reflectionIntensity = Mathf.Max(0f, reflectionIntensity.Evaluate(t));

            // --- Fog
            if (driveFog)
            {
                RenderSettings.fog = true;
                RenderSettings.fogColor = fogColor.Evaluate(t);
                RenderSettings.fogDensity = Mathf.Max(0f, fogDensity.Evaluate(t));
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
