using System;

namespace SCoL
{
    [Serializable]
    public class CellState
    {
        public PlantStage PlantStage = PlantStage.Empty;

        // Lifetime tracking (seconds). Used when plant lifecycle is enabled.
        public float PlantAgeSeconds = 0f;

        // Continuous, 0..1
        public float Water = 0.2f;
        public float Sunlight = 0.8f;
        public float Heat = 0.1f;
        public float Durability = 1.0f;
        public float Success = 0.5f;

        // Transient flags
        public bool IsOnFire;
        public float FireFuel = 0f; // 0..1

        // Simple prototype: allows water to darken the cell color without relying on view modes
        public float WaterVisual = 0f; // 0..1

        public bool HasPlant => PlantStage != PlantStage.Empty && PlantStage != PlantStage.Burnt;
    }
}
