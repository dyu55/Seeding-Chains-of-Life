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
        [Tooltip("For Seed pickups: which growth variant to plant. -1 keeps current selected variant.")]
        public int seedVariantIndex = -1;

        public Color colorSeed = new Color(0.15f, 0.95f, 0.2f);
        public Color colorWater = new Color(0.2f, 0.55f, 1f);
        public Color colorFire = new Color(0.95f, 0.15f, 0.1f);
        public Color colorPlant = new Color(0.45f, 1f, 0.35f);
        public Color colorStone = new Color(0.72f, 0.72f, 0.78f);
        [Header("Optional Textures")]
        public Texture2D seedTexture;
        public Texture2D waterTexture;
        public Texture2D fireTexture;
        public Texture2D plantTexture;
        public Texture2D stoneTexture;
        [Tooltip("When true and no explicit texture is assigned for this type, keeps prefab-authored materials unchanged.")]
        public bool preserveExistingMaterials = true;

        private void Reset()
        {
            amount = 1;
        }

        private void OnEnable()
        {
            ApplyVisual();
        }

        public void ApplyVisual()
        {
            Color tint = type switch
            {
                SCoLItemType.Seed => colorSeed,
                SCoLItemType.Water => colorWater,
                SCoLItemType.Fire => colorFire,
                SCoLItemType.Plant => colorPlant,
                SCoLItemType.Stone => colorStone,
                _ => Color.white
            };

            Texture2D tex = type switch
            {
                SCoLItemType.Seed => seedTexture,
                SCoLItemType.Water => waterTexture,
                SCoLItemType.Fire => fireTexture,
                SCoLItemType.Plant => plantTexture,
                SCoLItemType.Stone => stoneTexture,
                _ => null
            };

            if (preserveExistingMaterials && tex == null)
                return;

            var renderers = GetComponentsInChildren<Renderer>(includeInactive: true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                var m = r.material;
                if (m == null) continue;

                if (tex != null)
                {
                    if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.white);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                }
                else
                {
                    if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", tint);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", tint);
                }
            }
        }
    }
}
