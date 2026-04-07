using System.Collections.Generic;
using UnityEngine;
using SCoL.Visualization;

namespace SCoL.Combat
{
    [DisallowMultipleComponent]
    public sealed class SCoLCombatHealth : MonoBehaviour
    {
        static readonly List<SCoLCombatHealth> Active = new List<SCoLCombatHealth>(128);

        [SerializeField] SCoLCombatFaction faction = SCoLCombatFaction.Neutral;
        [SerializeField, Min(1f)] float maxHealth = 50f;
        [SerializeField, Min(0f)] float currentHealth = 50f;
        [SerializeField, Min(0f)] float damageInvulnerabilitySeconds = 0f;
        public bool destroyOnDeath = true;
        public bool showWorldHealthBar = true;

        SCoLPlayerHealth _playerHealth;
        SCoLWorldHealthBar _worldBar;
        bool _deathNotified;
        float _ignoreDamageUntil;

        public static IReadOnlyList<SCoLCombatHealth> ActiveHealths => Active;
        public event System.Action<SCoLCombatHealth> Died;
        public event System.Action<SCoLCombatHealth, float> Damaged;

        public SCoLCombatFaction Faction => faction;
        public float MaxHealth => _playerHealth != null ? _playerHealth.MaxHealth : Mathf.Max(1f, maxHealth);
        public float CurrentHealth => _playerHealth != null ? _playerHealth.CurrentHealth : Mathf.Clamp(currentHealth, 0f, MaxHealth);
        public bool IsDead => CurrentHealth <= 0.001f;
        public bool IsDamageInvulnerable => damageInvulnerabilitySeconds > 0f && Time.time < _ignoreDamageUntil;
        public float DamageInvulnerabilitySeconds
        {
            get => Mathf.Max(0f, damageInvulnerabilitySeconds);
            set => damageInvulnerabilitySeconds = Mathf.Max(0f, value);
        }

        void Awake()
        {
            _playerHealth = GetComponent<SCoLPlayerHealth>();
            if (_playerHealth != null)
            {
                _playerHealth.SetMaxHealth(maxHealth, fillToMax: false);
                _playerHealth.SetCurrentHealth(currentHealth);
            }
        }

        void OnEnable()
        {
            if (!Active.Contains(this))
                Active.Add(this);
            RefreshBar();
        }

        void OnDisable()
        {
            Active.Remove(this);
        }

        public void Configure(SCoLCombatFaction newFaction, float newMaxHealth, bool fillToMax = true, bool showBar = true, bool destroyWhenDead = true)
        {
            faction = newFaction;
            showWorldHealthBar = showBar;
            destroyOnDeath = destroyWhenDead;
            SetMaxHealth(newMaxHealth, fillToMax);
        }

        public void SetMaxHealth(float value, bool fillToMax)
        {
            maxHealth = Mathf.Max(1f, value);
            if (_playerHealth != null)
                _playerHealth.SetMaxHealth(maxHealth, fillToMax);

            if (fillToMax)
                currentHealth = maxHealth;
            else
                currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);

            if (_playerHealth != null && fillToMax)
                _playerHealth.SetCurrentHealth(maxHealth);

            RefreshBar();
        }

        public void SetCurrentHealth(float value)
        {
            currentHealth = Mathf.Clamp(value, 0f, MaxHealth);
            if (_playerHealth != null)
                _playerHealth.SetCurrentHealth(currentHealth);
            if (currentHealth > 0.001f)
                _deathNotified = false;
            RefreshBar();
        }

        public bool ApplyDamage(float amount)
        {
            if (amount <= 0f || IsDead || IsDamageInvulnerable)
                return false;

            float next = Mathf.Clamp(CurrentHealth - amount, 0f, MaxHealth);
            if (_playerHealth != null)
                _playerHealth.Damage(amount);
            currentHealth = next;
            if (damageInvulnerabilitySeconds > 0f)
                _ignoreDamageUntil = Time.time + damageInvulnerabilitySeconds;
            RefreshBar();
            Damaged?.Invoke(this, amount);

            if (next <= 0.001f)
                HandleDeath();

            return true;
        }

        public void Heal(float amount)
        {
            if (amount <= 0f || IsDead)
                return;

            float next = Mathf.Clamp(CurrentHealth + amount, 0f, MaxHealth);
            if (_playerHealth != null)
                _playerHealth.Heal(amount);
            currentHealth = next;
            if (currentHealth > 0.001f)
                _deathNotified = false;
            RefreshBar();
        }

        public void ClearDamageInvulnerability()
        {
            _ignoreDamageUntil = 0f;
        }

        public void RefreshBar()
        {
            if (!showWorldHealthBar)
            {
                if (_worldBar != null)
                    _worldBar.SetVisible(false);
                return;
            }

            if (_worldBar == null)
                _worldBar = GetComponent<SCoLWorldHealthBar>();
            if (_worldBar == null)
                _worldBar = gameObject.AddComponent<SCoLWorldHealthBar>();

            _worldBar.SetVisible(true);
            _worldBar.SetHealth(CurrentHealth, MaxHealth);
        }

        void HandleDeath()
        {
            if (!_deathNotified)
            {
                _deathNotified = true;
                Died?.Invoke(this);
            }

            RefreshBar();

            if (!destroyOnDeath)
                return;

            Destroy(gameObject);
        }
    }
}
