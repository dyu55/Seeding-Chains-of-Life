using System;
using UnityEngine;

namespace SCoL.Visualization
{
    /// <summary>
    /// Chooses skybox materials based on (1) season and (2) day/night.
    ///
    /// You provide 8 materials total:
    /// - Spring Day / Spring Night
    /// - Summer Day / Summer Night
    /// - Autumn Day / Autumn Night
    /// - Winter Day / Winter Night
    ///
    /// Day/Night is driven by a DayNightLightingController if assigned.
    /// If not assigned, it uses system clock hour (simple heuristic).
    /// </summary>
    public class SeasonSkyboxController : MonoBehaviour
    {
        public enum Mode
        {
            SystemDate,
            Manual,
            CycleInEditor
        }

        public enum Season
        {
            Spring = 0,
            Summer = 1,
            Autumn = 2,
            Winter = 3,
        }

        public enum DayPhase
        {
            Day,
            Night
        }

        [Header("Skybox Materials (Day)")]
        public Material springDay;
        public Material summerDay;
        public Material autumnDay;
        public Material winterDay;

        [Header("Skybox Materials (Night)")]
        public Material springNight;
        public Material summerNight;
        public Material autumnNight;
        public Material winterNight;

        [Header("Mode")]
        public Mode mode = Mode.SystemDate;
        public Season manualSeason = Season.Spring;

        [Tooltip("Only used in CycleInEditor mode.")]
        [Min(1f)] public float cycleSeconds = 10f;

        [Header("Day/Night Source")]
        [Tooltip("Optional: reference to your DayNightLightingController. If set, we will pick day/night based on its timeOfDay01.")]
        public DayNightLightingController dayNightController;

        [Tooltip("If using DayNightLightingController, this range counts as DAY. Outside is NIGHT.\nExample: 0.23..0.77 roughly gives sunrise/sunset boundaries.")]
        public Vector2 dayRange01 = new Vector2(0.23f, 0.77f);

        [Tooltip("If no DayNightLightingController is assigned: consider it day between these hours (24h clock).")]
        [Range(0, 23)] public int systemDayStartHour = 7;
        [Range(0, 23)] public int systemNightStartHour = 19;

        [Header("Behavior")]
        [Tooltip("If enabled, also updates DynamicGI environment after setting skybox.")]
        public bool updateEnvironmentGI = true;

        [Tooltip("Apply once on Start.")]
        public bool applyOnStart = true;

        [Tooltip("How often to re-evaluate in SystemDate mode (and day/night).")]
        [Min(0f)] public float refreshIntervalSeconds = 1f;

        float _t;
        Season _lastSeason = (Season)(-1);
        DayPhase _lastPhase = (DayPhase)(-1);

        void Start()
        {
            if (applyOnStart)
                ApplyNow(force: true);
        }

        void Update()
        {
            _t += Time.unscaledDeltaTime;

            // Manual mode: apply immediately when dropdown changes.
            if (mode == Mode.Manual)
            {
                ApplyNow(force: false);
                return;
            }

            // Cycle preview mode
            if (mode == Mode.CycleInEditor)
            {
                if (_t >= cycleSeconds)
                {
                    _t = 0f;
                    manualSeason = (Season)(((int)manualSeason + 1) % 4);
                    ApplyNow(force: true);
                }
                return;
            }

            // SystemDate refresh
            if (mode == Mode.SystemDate)
            {
                if (refreshIntervalSeconds <= 0f)
                    return;

                if (_t >= refreshIntervalSeconds)
                {
                    _t = 0f;
                    ApplyNow(force: false);
                }
            }
        }

#if UNITY_EDITOR
        void OnValidate()
        {
            if (!isActiveAndEnabled) return;
            ApplyNow(force: true);
        }
#endif

        [ContextMenu("Apply Now")]
        public void ApplyNow() => ApplyNow(force: true);

        void ApplyNow(bool force)
        {
            Season season = mode switch
            {
                Mode.Manual => manualSeason,
                Mode.SystemDate => GetSeasonFromMonth(DateTime.Now.Month),
                Mode.CycleInEditor => manualSeason,
                _ => manualSeason
            };

            DayPhase phase = GetDayPhase();

            if (!force && season == _lastSeason && phase == _lastPhase)
                return;

            var mat = GetMaterial(season, phase);
            if (mat == null)
            {
                Debug.LogWarning($"SeasonSkyboxController: Missing skybox material for {season} {phase}.", this);
                return;
            }

            RenderSettings.skybox = mat;

            if (updateEnvironmentGI)
                DynamicGI.UpdateEnvironment();

            _lastSeason = season;
            _lastPhase = phase;
        }

        DayPhase GetDayPhase()
        {
            if (dayNightController != null)
            {
                float t = Mathf.Repeat(dayNightController.timeOfDay01, 1f);
                bool isDay = IsWithinWrappedRange(t, dayRange01.x, dayRange01.y);
                return isDay ? DayPhase.Day : DayPhase.Night;
            }

            // Fallback: system clock
            int hour = DateTime.Now.Hour;
            bool sysDay;
            if (systemDayStartHour == systemNightStartHour)
            {
                sysDay = true;
            }
            else if (systemDayStartHour < systemNightStartHour)
            {
                // e.g., 7..19
                sysDay = hour >= systemDayStartHour && hour < systemNightStartHour;
            }
            else
            {
                // wrap-around (rare): e.g., 19..7
                sysDay = hour >= systemDayStartHour || hour < systemNightStartHour;
            }
            return sysDay ? DayPhase.Day : DayPhase.Night;
        }

        public static bool IsWithinWrappedRange(float t01, float start01, float end01)
        {
            t01 = Mathf.Repeat(t01, 1f);
            start01 = Mathf.Repeat(start01, 1f);
            end01 = Mathf.Repeat(end01, 1f);

            if (Mathf.Approximately(start01, end01))
                return true;

            if (start01 < end01)
                return t01 >= start01 && t01 <= end01;

            // wrapped, e.g. start=0.8 end=0.2
            return t01 >= start01 || t01 <= end01;
        }

        static Season GetSeasonFromMonth(int month)
        {
            // Northern hemisphere seasons by month:
            // Spring: Mar-May, Summer: Jun-Aug, Autumn: Sep-Nov, Winter: Dec-Feb
            return month switch
            {
                3 or 4 or 5 => Season.Spring,
                6 or 7 or 8 => Season.Summer,
                9 or 10 or 11 => Season.Autumn,
                _ => Season.Winter,
            };
        }

        public Material GetDayMaterialForSeason(Season season) => GetMaterial(season, DayPhase.Day);
        public Material GetNightMaterialForSeason(Season season) => GetMaterial(season, DayPhase.Night);

        public Season GetCurrentSeason()
        {
            return mode switch
            {
                Mode.Manual => manualSeason,
                Mode.SystemDate => GetSeasonFromMonth(DateTime.Now.Month),
                Mode.CycleInEditor => manualSeason,
                _ => manualSeason
            };
        }

        public DayPhase GetCurrentDayPhase() => GetDayPhase();

        Material GetMaterial(Season season, DayPhase phase)
        {
            return (season, phase) switch
            {
                (Season.Spring, DayPhase.Day) => springDay,
                (Season.Summer, DayPhase.Day) => summerDay,
                (Season.Autumn, DayPhase.Day) => autumnDay,
                (Season.Winter, DayPhase.Day) => winterDay,

                (Season.Spring, DayPhase.Night) => springNight,
                (Season.Summer, DayPhase.Night) => summerNight,
                (Season.Autumn, DayPhase.Night) => autumnNight,
                (Season.Winter, DayPhase.Night) => winterNight,

                _ => springDay
            };
        }
    }
}
