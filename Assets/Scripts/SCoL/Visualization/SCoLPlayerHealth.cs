using UnityEngine;

namespace SCoL.Visualization
{
    /// <summary>
    /// Minimal player health component used by SCoLUIToolkitHUD.
    /// </summary>
    public sealed class SCoLPlayerHealth : MonoBehaviour
    {
        [Min(1f)] [SerializeField] float maxHealth = 100f;
        [Min(0f)] [SerializeField] float currentHealth = 100f;

        public float MaxHealth => Mathf.Max(1f, maxHealth);
        public float CurrentHealth => Mathf.Clamp(currentHealth, 0f, MaxHealth);

        public void SetMaxHealth(float value, bool fillToMax = false)
        {
            maxHealth = Mathf.Max(1f, value);
            if (fillToMax)
                currentHealth = maxHealth;
            else
                currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        }

        public void SetCurrentHealth(float value)
        {
            currentHealth = Mathf.Clamp(value, 0f, MaxHealth);
        }

        public void Damage(float amount)
        {
            if (amount <= 0f) return;
            currentHealth = Mathf.Clamp(currentHealth - amount, 0f, MaxHealth);
        }

        public void Heal(float amount)
        {
            if (amount <= 0f) return;
            currentHealth = Mathf.Clamp(currentHealth + amount, 0f, MaxHealth);
        }

        void Reset()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        }

        void OnValidate()
        {
            maxHealth = Mathf.Max(1f, maxHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
        }
    }
}
