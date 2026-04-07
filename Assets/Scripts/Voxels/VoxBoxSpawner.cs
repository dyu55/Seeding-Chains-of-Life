using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drops VoxBox prefabs into the world at runtime.
///
/// Usage:
/// 1) Add this component to an empty GameObject in your scene (e.g., "VoxBoxSpawner").
/// 2) Drag prefabs from Assets/VoxBox/Prefabs/... into <see cref="spawnPrefabs"/>.
/// 3) Set groundMask to your ground layer (or leave as Everything if you don't care).
/// 4) Press Play.
///
/// Spawned objects are tagged "Harvestable" so FPSRaycastInteractor can find them.
/// </summary>
[DisallowMultipleComponent]
public class VoxBoxSpawner : MonoBehaviour
{
    [Header("What to spawn")]
    public GameObject[] spawnPrefabs;

    [Header("How many")]
    [Min(0)] public int count = 25;
    public bool spawnOnStart = true;

    [Header("Where")]
    [Min(0f)] public float radius = 25f;
    public Vector2 randomScaleRange = new Vector2(0.9f, 1.25f);

    [Header("Ground placement")]
    public LayerMask groundMask = ~0;
    [Tooltip("Cast from this height downward to find the ground.")]
    public float raycastHeight = 50f;
    public float groundOffset = 0.02f;

    [Header("Harvestable")]
    public string harvestableTag = "Harvestable";
    public bool addColliderIfMissing = true;

    [Header("Debug")]
    public bool logSpawns;
    [Tooltip("Log warnings when a prefab slot contains an invalid/non-GameObject reference.")]
    public bool warnOnInvalidPrefabRefs = false;

    readonly List<GameObject> _spawned = new();
    readonly HashSet<int> _invalidPrefabIds = new();

    void Start()
    {
        if (spawnOnStart)
            Respawn();
    }

    [ContextMenu("Respawn")]
    public void Respawn()
    {
        ClearSpawned();

        if (spawnPrefabs == null || spawnPrefabs.Length == 0)
        {
            Debug.LogWarning("[VoxBoxSpawner] No spawnPrefabs assigned. Drag VoxBox prefabs into the list.", this);
            return;
        }

        for (int i = 0; i < count; i++)
        {
            var prefab = PickPrefab();
            if (prefab == null) continue;

            if (!TryPickPointOnGround(out var pos))
                continue;

            var rot = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
            var go = TryInstantiatePrefab(prefab, pos, rot);
            if (go == null)
                continue;

            // tag for harvest logic (raycast interactor walks parents looking for this)
            TryTag(go);

            // random uniform scale (keeps voxels chunky but varied)
            float s = UnityEngine.Random.Range(randomScaleRange.x, randomScaleRange.y);
            go.transform.localScale *= s;

            if (addColliderIfMissing)
                EnsureAnyCollider(go);

            _spawned.Add(go);

            if (logSpawns)
                Debug.Log($"[VoxBoxSpawner] Spawned '{go.name}' at {pos}", go);
        }
    }

    void ClearSpawned()
    {
        for (int i = _spawned.Count - 1; i >= 0; i--)
        {
            if (_spawned[i] != null)
                Destroy(_spawned[i]);
        }
        _spawned.Clear();
    }

    GameObject PickPrefab()
    {
        if (spawnPrefabs == null || spawnPrefabs.Length == 0)
            return null;

        // Avoid nulls by trying a few times
        for (int tries = 0; tries < 8; tries++)
        {
            var p = spawnPrefabs[UnityEngine.Random.Range(0, spawnPrefabs.Length)];
            if (p == null) continue;
            if (_invalidPrefabIds.Contains(p.GetInstanceID())) continue;
            return p;
        }

        return null;
    }

    GameObject TryInstantiatePrefab(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null) return null;

        try
        {
            // Use non-generic Instantiate to avoid InvalidCastException from bad serialized refs.
            var obj = Instantiate((UnityEngine.Object)prefab, position, rotation);
            if (obj is GameObject go)
                return go;

            if (obj != null)
                Destroy(obj);

            _invalidPrefabIds.Add(prefab.GetInstanceID());
            if (warnOnInvalidPrefabRefs)
                Debug.LogWarning($"[VoxBoxSpawner] Skipped non-GameObject prefab reference: '{prefab.name}'.", this);
            return null;
        }
        catch (Exception ex)
        {
            _invalidPrefabIds.Add(prefab.GetInstanceID());
            if (warnOnInvalidPrefabRefs)
                Debug.LogWarning($"[VoxBoxSpawner] Failed to instantiate '{prefab.name}': {ex.GetType().Name} - {ex.Message}", this);
            return null;
        }
    }

    bool TryPickPointOnGround(out Vector3 pos)
    {
        // random point in XZ disc around this spawner
        Vector2 r = UnityEngine.Random.insideUnitCircle * radius;
        Vector3 origin = transform.position + new Vector3(r.x, raycastHeight, r.y);

        if (Physics.Raycast(origin, Vector3.down, out var hit, raycastHeight * 2f, groundMask, QueryTriggerInteraction.Ignore))
        {
            pos = hit.point + Vector3.up * groundOffset;
            return true;
        }

        // fallback: just place at spawner height if no ground found
        pos = transform.position + new Vector3(r.x, 0f, r.y);
        return true;
    }

    void TryTag(GameObject go)
    {
        if (go == null) return;

        // If the tag doesn't exist in Tag Manager, Unity will throw.
        try
        {
            go.tag = harvestableTag;
        }
        catch (Exception)
        {
            Debug.LogWarning($"[VoxBoxSpawner] Tag '{harvestableTag}' not defined. Add it in Project Settings -> Tags and Layers.", go);
        }
    }

    void EnsureAnyCollider(GameObject root)
    {
        if (root == null) return;

        // If any collider already exists, we're good.
        if (root.GetComponentInChildren<Collider>() != null)
            return;

        // Try to add a MeshCollider from the first MeshFilter we can find.
        var mf = root.GetComponentInChildren<MeshFilter>();
        if (mf != null && mf.sharedMesh != null)
        {
            var mc = mf.gameObject.AddComponent<MeshCollider>();
            mc.sharedMesh = mf.sharedMesh;
            mc.convex = true; // helps with triggers/physics; fine for small props
            return;
        }

        // As a last resort, add a simple box collider at root.
        var bc = root.AddComponent<BoxCollider>();
        bc.center = Vector3.zero;
        bc.size = Vector3.one;
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 0.3f, 0.25f);
        Gizmos.DrawSphere(transform.position, radius);
    }
}
