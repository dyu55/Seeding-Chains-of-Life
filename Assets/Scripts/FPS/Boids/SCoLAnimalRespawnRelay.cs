using UnityEngine;
using SCoL.Combat;

[DisallowMultipleComponent]
public sealed class SCoLAnimalRespawnRelay : MonoBehaviour
{
    VoxBoxAnimalSchoolSpawner _spawner;
    SCoLCombatHealth _health;
    bool _spawnWolf;
    int _slotIndex;
    bool _notified;

    public void Initialize(VoxBoxAnimalSchoolSpawner spawner, bool spawnWolf, int slotIndex)
    {
        if (_health != null)
            _health.Died -= OnDied;

        _spawner = spawner;
        _spawnWolf = spawnWolf;
        _slotIndex = Mathf.Max(0, slotIndex);
        _notified = false;
        _health = GetComponent<SCoLCombatHealth>();
        if (_health != null)
            _health.Died += OnDied;
    }

    void OnDisable()
    {
        if (_health != null)
            _health.Died -= OnDied;
    }

    void OnDied(SCoLCombatHealth _)
    {
        if (_notified || _spawner == null)
            return;

        _notified = true;
        _spawner.NotifyAnimalDeath(gameObject, _spawnWolf, _slotIndex);
    }
}
