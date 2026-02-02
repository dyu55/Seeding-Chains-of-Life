using UnityEngine;

namespace SCoL.Inventory
{
    /// <summary>
    /// A pickup item in the world. Can be collected via ray + trigger.
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLPickup : MonoBehaviour
    {
        public SCoLItemType type = SCoLItemType.Seed;
        public int amount = 1;

        public Color colorSeed = new Color(0.15f, 0.95f, 0.2f);
        public Color colorWater = new Color(0.2f, 0.55f, 1f);
        public Color colorFire = new Color(0.95f, 0.15f, 0.1f);

        private void Reset()
        {
            amount = 1;
        }

        private void Awake()
        {
            // Visual
            var r = GetComponent<Renderer>();
            if (r != null)
            {
                r.material.color = type switch
                {
                    SCoLItemType.Seed => colorSeed,
                    SCoLItemType.Water => colorWater,
                    SCoLItemType.Fire => colorFire,
                    _ => Color.white
                };
            }
        }
    }
}
