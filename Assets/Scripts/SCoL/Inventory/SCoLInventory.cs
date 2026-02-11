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
        public int water = 0;
        public int fire = 0;

        public int Get(SCoLItemType type)
        {
            return type switch
            {
                SCoLItemType.Seed => seeds,
                SCoLItemType.Water => water,
                SCoLItemType.Fire => fire,
                _ => 0
            };
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
            }
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
            }
            return false;
        }
    }
}
