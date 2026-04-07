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
        public int beanSeed = 0;
        public int brownSeed = 0;
        public int lightBrownSeed = 0;
        public int longSeed = 0;
        public int seedType1 = 0; // "seed1" special lineage
        public int seedV1 = 0;
        public int seedV2 = 0;
        public int seedV3 = 0;
        public int water = 0;
        public int fire = 0;
        public int plants = 0;
        public int stones = 0;

        [Header("Starter Inventory")]
        public bool applyMinimumStarterInventoryOnAwake = true;
        [Min(0)] public int starterSeeds = 300;
        [Min(0)] public int starterRoseglowSeeds = 100;
        [Min(0)] public int starterAmberbloomSeeds = 100;
        [Min(0)] public int starterMoonpetalSeeds = 100;
        [Min(0)] public int starterWater = 10;
        [Min(0)] public int starterFire = 10;
        [Min(0)] public int starterPlants = 10;
        [Min(0)] public int starterStones = 50;

        [Header("Discovery (session only)")]
        public bool discoveredSeed = false;
        public bool discoveredWater = false;
        public bool discoveredFire = false;
        public bool discoveredPlant = false;
        public bool discoveredStone = false;

        private void Awake()
        {
            EnsureMinimumStarterInventory();
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
                SCoLItemType.Stone => stones,
                _ => 0
            };
        }

        public int GetSeedTypeCount(int variantIndex)
        {
            return variantIndex switch
            {
                0 => beanSeed,
                1 => brownSeed,
                2 => lightBrownSeed,
                3 => longSeed,
                4 => seedType1,
                5 => seedV1,
                6 => seedV2,
                7 => seedV3,
                _ => 0
            };
        }

        public int GetSeedTypeVariantCount()
        {
            return 8;
        }

        public void AddSeedType(int variantIndex, int amount = 1)
        {
            if (amount <= 0) return;

            switch (variantIndex)
            {
                case 0: beanSeed += amount; break;
                case 1: brownSeed += amount; break;
                case 2: lightBrownSeed += amount; break;
                case 3: longSeed += amount; break;
                case 4: seedType1 += amount; break;
                case 5: seedV1 += amount; break;
                case 6: seedV2 += amount; break;
                case 7: seedV3 += amount; break;
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
                {
                    if (beanSeed < amount) return false;
                    beanSeed -= amount;
                    break;
                }
                case 1:
                    if (brownSeed < amount) return false;
                    brownSeed -= amount;
                    break;
                case 2:
                    if (lightBrownSeed < amount) return false;
                    lightBrownSeed -= amount;
                    break;
                case 3:
                    if (longSeed < amount) return false;
                    longSeed -= amount;
                    break;
                case 4:
                    if (seedType1 < amount) return false;
                    seedType1 -= amount;
                    break;
                case 5:
                    if (seedV1 < amount) return false;
                    seedV1 -= amount;
                    break;
                case 6:
                    if (seedV2 < amount) return false;
                    seedV2 -= amount;
                    break;
                case 7:
                    if (seedV3 < amount) return false;
                    seedV3 -= amount;
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
                0 => "Bean",
                1 => "Ember Seed",
                2 => "Moon Seed",
                3 => "Long Seed",
                4 => "Wild Seed",
                5 => "Roseglow",
                6 => "Amberbloom",
                7 => "Moonpetal",
                _ => "Seed"
            };
        }

        public string GetSeedTypeSummary()
        {
            return $"Bean:{GetSeedTypeCount(0)} Ember:{GetSeedTypeCount(1)} Moon:{GetSeedTypeCount(2)} Long:{GetSeedTypeCount(3)} Wild:{GetSeedTypeCount(4)} Rose:{GetSeedTypeCount(5)} Amber:{GetSeedTypeCount(6)} Moonpetal:{GetSeedTypeCount(7)}";
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
                case SCoLItemType.Stone:
                    stones += amount;
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
                case SCoLItemType.Stone:
                    if (stones < amount) return false;
                    stones -= amount;
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
                SCoLItemType.Stone => discoveredStone,
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
                case SCoLItemType.Stone:
                    discoveredStone = true;
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
                SCoLItemType.Stone => "Stone",
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
                SCoLItemType.Stone => "Thrown weapon. Effective against wolves and other animals.",
                _ => "You have not discovered this item yet."
            };
        }

        private void SyncDiscoveryFromCounts()
        {
            seeds = Mathf.Max(seeds, beanSeed + brownSeed + lightBrownSeed + longSeed + seedType1 + seedV1 + seedV2 + seedV3);
            if (seeds > 0) discoveredSeed = true;
            if (water > 0) discoveredWater = true;
            if (fire > 0) discoveredFire = true;
            if (plants > 0) discoveredPlant = true;
            if (stones > 0) discoveredStone = true;
        }

        private void EnsureMinimumStarterInventory()
        {
            if (!applyMinimumStarterInventoryOnAwake)
                return;

            water = Mathf.Max(water, starterWater);
            fire = Mathf.Max(fire, starterFire);
            plants = Mathf.Max(plants, starterPlants);
            stones = Mathf.Max(stones, starterStones);

            beanSeed = Mathf.Max(beanSeed, 25);
            brownSeed = Mathf.Max(brownSeed, 25);
            lightBrownSeed = Mathf.Max(lightBrownSeed, 25);
            longSeed = Mathf.Max(longSeed, 25);
            seedType1 = Mathf.Max(seedType1, 25);
            seedV1 = Mathf.Max(seedV1, starterRoseglowSeeds);
            seedV2 = Mathf.Max(seedV2, starterAmberbloomSeeds);
            seedV3 = Mathf.Max(seedV3, starterMoonpetalSeeds);

            int totalSeedVariants = beanSeed + brownSeed + lightBrownSeed + longSeed + seedType1 + seedV1 + seedV2 + seedV3;
            int seedDeficit = Mathf.Max(0, starterSeeds - totalSeedVariants);
            for (int i = 0; i < seedDeficit; i++)
            {
                switch (i % 8)
                {
                    case 0: beanSeed++; break;
                    case 1: brownSeed++; break;
                    case 2: lightBrownSeed++; break;
                    case 3: longSeed++; break;
                    case 4: seedType1++; break;
                    case 5: seedV1++; break;
                    case 6: seedV2++; break;
                    default: seedV3++; break;
                }
            }

            seeds = Mathf.Max(seeds, beanSeed + brownSeed + lightBrownSeed + longSeed + seedType1 + seedV1 + seedV2 + seedV3, starterSeeds);
        }
    }
}
