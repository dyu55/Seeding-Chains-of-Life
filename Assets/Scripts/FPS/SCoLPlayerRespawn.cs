using UnityEngine;
using SCoL.Combat;
using SCoL.Visualization;

[DisallowMultipleComponent]
public sealed class SCoLPlayerRespawn : MonoBehaviour
{
    [Min(1f)] public float respawnHealth = 100f;
    [Min(4f)] public float minRespawnDistanceFromDeath = 28f;
    [Min(4)] public int randomSpawnAttempts = 48;
    public bool unlockCursorWhenDead = true;
    public bool relockCursorOnRespawn = true;

    SCoLPlayerHealth _playerHealth;
    SCoLCombatHealth _combatHealth;
    FPSSpawnOnVoxel _spawnOnVoxel;
    SimpleFirstPersonController _controller;
    FPSRaycastInteractor _interactor;
    FPSAimAuraHighlighter _aimAura;
    CharacterController _characterController;

    bool _deathStateApplied;
    Vector3 _deathWorldPos;

    public bool IsDead => _playerHealth != null && _playerHealth.CurrentHealth <= 0.001f;

    void Awake()
    {
        CacheReferences();
    }

    void Update()
    {
        CacheReferences();

        if (IsDead)
        {
            if (!_deathStateApplied)
                ApplyDeathState();
            if (Input.GetKeyDown(KeyCode.Y))
                RespawnAtRandomLocation();
            return;
        }

        if (_deathStateApplied)
            ClearDeathState();
    }

    public bool RespawnAtRandomLocation()
    {
        CacheReferences();
        if (_playerHealth == null)
            return false;

        Vector3 avoidWorldPos = _deathStateApplied ? _deathWorldPos : transform.position;
        if (_spawnOnVoxel == null)
            _spawnOnVoxel = GetComponent<FPSSpawnOnVoxel>();

        Vector3 respawnPoint = transform.position;
        bool found = _spawnOnVoxel != null &&
                     _spawnOnVoxel.TryFindRandomDryLandSpawnPoint(
                         avoidWorldPos,
                         Mathf.Max(4f, minRespawnDistanceFromDeath),
                         Mathf.Max(4, randomSpawnAttempts),
                         out respawnPoint);

        if (_spawnOnVoxel != null)
            _spawnOnVoxel.TeleportToSpawnPoint(found ? respawnPoint : transform.position, stabilize: true);

        _playerHealth.SetMaxHealth(respawnHealth, fillToMax: true);
        _playerHealth.SetCurrentHealth(respawnHealth);

        if (_combatHealth != null)
        {
            _combatHealth.SetMaxHealth(respawnHealth, fillToMax: true);
            _combatHealth.SetCurrentHealth(respawnHealth);
            _combatHealth.ClearDamageInvulnerability();
            _combatHealth.RefreshBar();
        }

        if (_characterController != null)
            _characterController.enabled = true;
        if (_controller != null)
            _controller.enabled = true;
        if (_interactor != null)
            _interactor.enabled = true;
        if (_aimAura != null)
            _aimAura.enabled = true;

        _deathStateApplied = false;

        if (relockCursorOnRespawn)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        return true;
    }

    void CacheReferences()
    {
        if (_playerHealth == null)
            _playerHealth = GetComponent<SCoLPlayerHealth>();
        if (_combatHealth == null)
            _combatHealth = GetComponent<SCoLCombatHealth>();
        if (_spawnOnVoxel == null)
            _spawnOnVoxel = GetComponent<FPSSpawnOnVoxel>();
        if (_controller == null)
            _controller = GetComponent<SimpleFirstPersonController>();
        if (_interactor == null)
            _interactor = GetComponent<FPSRaycastInteractor>();
        if (_aimAura == null)
            _aimAura = GetComponent<FPSAimAuraHighlighter>();
        if (_characterController == null)
            _characterController = GetComponent<CharacterController>();
    }

    void ApplyDeathState()
    {
        _deathStateApplied = true;
        _deathWorldPos = transform.position;

        if (_controller != null)
            _controller.enabled = false;
        if (_interactor != null)
            _interactor.enabled = false;
        if (_aimAura != null)
            _aimAura.enabled = false;

        if (unlockCursorWhenDead)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }

    void ClearDeathState()
    {
        _deathStateApplied = false;
    }
}
