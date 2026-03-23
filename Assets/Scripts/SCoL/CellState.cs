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
        public float BurntAutoClearSeconds = 0f;
        public float SpreadBlockSeconds = 0f;

        // Simple prototype: allows water to darken the cell color without relying on view modes
        public float WaterVisual = 0f; // 0..1

        // True only for plants that come from player-placed seeds (and their descendants).
        public bool IsPlayerSeedLineage = false;

        // Locked flower variant index chosen at seeding time.
        // -1 means "no locked variant" (renderer may pick by default/random policy).
        public int FlowerVariantIndex = -1;
        public float PlantHealth = 0f;
        public int StompHits = 0;

        // Fine placement offset inside the owning cell so manual planting can follow the cursor,
        // while the simulation itself still stays cell-based.
        public float PlantOffsetX = 0f;
        public float PlantOffsetZ = 0f;

        public bool HasPlant => PlantStage != PlantStage.Empty && PlantStage != PlantStage.Burnt;

        public void ClearPlantPlacementOffset()
        {
            PlantOffsetX = 0f;
            PlantOffsetZ = 0f;
        }
    }
}
