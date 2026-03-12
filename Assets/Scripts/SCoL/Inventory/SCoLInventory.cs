using UnityEngine;

namespace SCoL.Inventory
{
    /// <summary>
    /// Very small inventory for prototype.
    /// Counts are integers.
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLInventory : MonoBehaviour
    {
        public int seeds = 0;
        [Header("Seed Types")]
        public int seedV1 = 0;
        public int seedV2 = 0;
        public int seedV3 = 0;
        public int seedType1 = 0; // "seed1" special lineage
        public int water = 0;
        public int fire = 0;
        public int plants = 0;

        [Header("Discovery (session only)")]
        public bool discoveredSeed = false;
        public bool discoveredWater = false;
        public bool discoveredFire = false;
        public bool discoveredPlant = false;

        private void Awake()
        {
            SyncDiscoveryFromCounts();
        }

        private void OnValidate()
        {
            SyncDiscoveryFromCounts();
        }

        public int Get(SCoLItemType type)
        {
            return type switch
            {
                SCoLItemType.Seed => seeds,
                SCoLItemType.Water => water,
                SCoLItemType.Fire => fire,
                SCoLItemType.Plant => plants,
                _ => 0
            };
        }

        public int GetSeedTypeCount(int variantIndex)
        {
            return variantIndex switch
            {
                0 => seedV1,
                1 => seedV2,
                2 => seedV3,
                3 => seedType1,
                _ => 0
            };
        }

        public void AddSeedType(int variantIndex, int amount = 1)
        {
            if (amount <= 0) return;

            switch (variantIndex)
            {
                case 0: seedV1 += amount; break;
                case 1: seedV2 += amount; break;
                case 2: seedV3 += amount; break;
                case 3: seedType1 += amount; break;
                default:
                    Add(SCoLItemType.Seed, amount);
                    return;
            }

            seeds += amount;
            Discover(SCoLItemType.Seed);
        }

        public bool TryConsumeSeedType(int variantIndex, int amount = 1)
        {
            if (amount <= 0) return true;

            switch (variantIndex)
            {
                case 0:
                    if (seedV1 < amount) return false;
                    seedV1 -= amount;
                    break;
                case 1:
                    if (seedV2 < amount) return false;
                    seedV2 -= amount;
                    break;
                case 2:
                    if (seedV3 < amount) return false;
                    seedV3 -= amount;
                    break;
                case 3:
                    if (seedType1 < amount) return false;
                    seedType1 -= amount;
                    break;
                default:
                    return TryConsume(SCoLItemType.Seed, amount);
            }

            seeds = Mathf.Max(0, seeds - amount);
            return true;
        }

        public string GetSeedTypeDisplayName(int variantIndex)
        {
            return variantIndex switch
            {
                0 => "SeedV1",
                1 => "SeedV2",
                2 => "SeedV3",
                3 => "seed1",
                _ => "Seed"
            };
        }

        public string GetSeedTypeSummary()
        {
            return $"SeedV1:{seedV1} SeedV2:{seedV2} SeedV3:{seedV3} seed1:{seedType1}";
        }

        public void Add(SCoLItemType type, int amount = 1)
        {
            if (amount <= 0) return;
            switch (type)
            {
                case SCoLItemType.Seed:
                    seeds += amount;
                    break;
                case SCoLItemType.Water:
                    water += amount;
                    break;
                case SCoLItemType.Fire:
                    fire += amount;
                    break;
                case SCoLItemType.Plant:
                    plants += amount;
                    break;
            }

            Discover(type);
        }

        public bool TryConsume(SCoLItemType type, int amount = 1)
        {
            if (amount <= 0) return true;
            switch (type)
            {
                case SCoLItemType.Seed:
                    if (seeds < amount) return false;
                    seeds -= amount;
                    return true;
                case SCoLItemType.Water:
                    if (water < amount) return false;
                    water -= amount;
                    return true;
                case SCoLItemType.Fire:
                    if (fire < amount) return false;
                    fire -= amount;
                    return true;
                case SCoLItemType.Plant:
                    if (plants < amount) return false;
                    plants -= amount;
                    return true;
            }
            return false;
        }

        public bool IsDiscovered(SCoLItemType type)
        {
            return type switch
            {
                SCoLItemType.Seed => discoveredSeed,
                SCoLItemType.Water => discoveredWater,
                SCoLItemType.Fire => discoveredFire,
                SCoLItemType.Plant => discoveredPlant,
                _ => false
            };
        }

        public void Discover(SCoLItemType type)
        {
            switch (type)
            {
                case SCoLItemType.Seed:
                    discoveredSeed = true;
                    break;
                case SCoLItemType.Water:
                    discoveredWater = true;
                    break;
                case SCoLItemType.Fire:
                    discoveredFire = true;
                    break;
                case SCoLItemType.Plant:
                    discoveredPlant = true;
                    break;
            }
        }

        public string GetItemDisplayName(SCoLItemType type, bool unknownIfUndiscovered = true)
        {
            if (unknownIfUndiscovered && !IsDiscovered(type))
                return "Unknown item";

            return type switch
            {
                SCoLItemType.Seed => "Seed",
                SCoLItemType.Water => "Water",
                SCoLItemType.Fire => "Fire",
                SCoLItemType.Plant => "Plant",
                _ => "Unknown item"
            };
        }

        public string GetItemDescription(SCoLItemType type, bool unknownIfUndiscovered = true)
        {
            if (unknownIfUndiscovered && !IsDiscovered(type))
                return "You have not discovered this item yet.";

            return type switch
            {
                SCoLItemType.Seed => "Used to plant and start ecosystem lineage.",
                SCoLItemType.Water => "Hydrates plants and can extinguish fire.",
                SCoLItemType.Fire => "Ignites and burns targets.",
                SCoLItemType.Plant => "Used to feed animals.",
                _ => "You have not discovered this item yet."
            };
        }

        private void SyncDiscoveryFromCounts()
        {
            seeds = Mathf.Max(seeds, seedV1 + seedV2 + seedV3 + seedType1);
            if (seeds > 0) discoveredSeed = true;
            if (water > 0) discoveredWater = true;
            if (fire > 0) discoveredFire = true;
            if (plants > 0) discoveredPlant = true;
        }
    }
}
