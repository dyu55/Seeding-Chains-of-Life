using UnityEngine;

namespace SCoL
{
    [CreateAssetMenu(menuName = "SCoL/Config", fileName = "SCoLConfig")]
    public class SCoLConfig : ScriptableObject
    {
        [Header("Grid")]
        [Min(1)] public int width = 32;
        [Min(1)] public int height = 32;
        [Min(0.1f)] public float cellSize = 0.5f;

        [Header("Timing")]
        [Min(0.05f)] public float tickSeconds = .25f;
        [Tooltip("Seconds per season (2.5min default).")]
        [Min(5f)] public float seasonSeconds = 150f;

        [Header("Diffusion")]
        [Range(0f, 1f)] public float waterDiffuse = 0.12f;
        [Range(0f, 1f)] public float heatDiffuse = 0.10f;

        [Header("Growth")]
        [Range(0f, 1f)] public float seedSuccessBase = 0.75f;
        [Range(0f, 1f)] public float stompDamage = 0.10f;

        [Tooltip("Base chance per tick for an empty tile to sprout when near plants (stochastic CA birth).")]
        [Range(0f, 1f)] public float stochasticSproutChance = 0.50f;

        [Tooltip("If true, use stochastic sprouting instead of the strict Life-style (==3) birth rule.")]
        public bool useStochasticSprouting = true;

        [Header("Lifecycle")]
        [Tooltip("If true, plants disappear after 'plantLifetimeSeconds'.")]
        public bool enablePlantLifecycle = true;

        [Min(1f)]
        public float plantLifetimeSeconds = 20f;

        [Header("Fire")]
        [Range(0f, 1f)] public float fireHeatPerTick = 0.25f;
        [Range(0f, 1f)] public float fireFuelBurnPerTick = 0.12f;
        [Range(0f, 1f)] public float fireSpreadChance = 0.20f;

        [Header("Shading")]
        [Range(0f, 1f)] public float shadeFromLargeTree = 0.35f;

        [Header("Weather")]
        [Range(0f, 1f)] public float rainWaterPerTick = 0.10f;
        [Range(0f, 1f)] public float snowColdPerTick = 0.08f;
        [Range(0f, 1f)] public float cloudySunPenalty = 0.15f;

        [Header("Debug")]
        public bool generateGroundPlane = true;
        public bool spawnCameraRigHint = false;

        [Header("Simulation")]
        [Tooltip("If true, the cellular-automata style simulation tick runs every tickSeconds.")]
        public bool enableSimulationTick = true;
    }
}
