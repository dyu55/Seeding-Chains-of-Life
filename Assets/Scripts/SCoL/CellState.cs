using System;

namespace SCoL
{
    [Serializable]
    public class CellState
    {
        public PlantStage PlantStage = PlantStage.Empty;

        // Continuous, 0..1
        public float Water = 0.2f;
        public float Sunlight = 0.8f;
        public float Heat = 0.1f;
        public float Durability = 1.0f;
        public float Success = 0.5f;

        // Transient flags
        public bool IsOnFire;
        public float FireFuel = 0f; // 0..1

        public bool HasPlant => PlantStage != PlantStage.Empty && PlantStage != PlantStage.Burnt;
    }
}
