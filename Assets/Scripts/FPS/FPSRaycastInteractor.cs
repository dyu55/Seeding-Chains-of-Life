using UnityEngine;

/// <summary>
/// FPS mouse interaction: on LMB, raycast from screen center and detect objects tagged "Harvestable"
/// within a max distance.
/// 
/// No XR dependencies.
/// </summary>
public class FPSRaycastInteractor : MonoBehaviour
{
    public Camera cameraSource;
    public float maxDistance = 3f;
    public LayerMask hitMask = ~0;

    [Header("Harvest")]
    public bool destroyOnHarvest = true;
    [Tooltip("Small delay so the voxelize material swap can be seen before the object disappears.")]
    public float destroyDelaySeconds = 0.06f;

    [Header("Debug")]
    public bool logHits = true;

    SCoL.Inventory.SCoLInventory _inventory;

    void Awake()
    {
        if (cameraSource == null)
            cameraSource = Camera.main;

        _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
        if (_inventory == null)
        {
            // Create a minimal runtime inventory if the scene doesn't include one.
            var invGO = new GameObject("SCoLInventory (Runtime)");
            DontDestroyOnLoad(invGO);
            _inventory = invGO.AddComponent<SCoL.Inventory.SCoLInventory>();
        }
    }

    void Update()
    {
        if (cameraSource == null) return;

        // LMB: harvest
        if (Input.GetMouseButtonDown(0))
        {
            var ray = cameraSource.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            {
                var go = hit.collider != null ? hit.collider.gameObject : null;

                // Walk up parents to find a Harvestable root (colliders are often on child meshes).
                GameObject harvestable = null;
                for (var t = hit.collider != null ? hit.collider.transform : null; t != null; t = t.parent)
                {
                    if (t.gameObject.CompareTag("Harvestable")) { harvestable = t.gameObject; break; }
                }

                if (harvestable != null)
                {
                    if (logHits)
                        Debug.Log($"[FPSRaycastInteractor] Harvestable hit: {harvestable.name} (dist={hit.distance:0.00})", harvestable);

                    // T04: voxelize/assimilate effect
                    VoxelAssimilator.Assimilate(harvestable);

                    // T06: game feel (burst + shake)
                    FPSGameFeel.VoxelBurst(hit.point);
                    FPSGameFeel.Shake();

                    // T05: harvest -> add Voxel Seed to inventory and remove object
                    if (harvestable.GetComponent<FPSHarvestedMarker>() == null)
                    {
                        harvestable.AddComponent<FPSHarvestedMarker>();
                        _inventory.Add(SCoL.Inventory.SCoLItemType.Seed, 1);

                        if (destroyOnHarvest)
                        {
                            // Hide immediately, destroy shortly after.
                            SetRenderersEnabled(harvestable, false);
                            SetCollidersEnabled(harvestable, false);
                            StartCoroutine(DestroyLater(harvestable, destroyDelaySeconds));
                        }
                    }
                }
                else
                {
                    if (logHits)
                        Debug.Log($"[FPSRaycastInteractor] Hit non-harvestable: {(go != null ? go.name : "<null>")} (dist={hit.distance:0.00})");
                }
            }
            else
            {
                if (logHits)
                    Debug.Log("[FPSRaycastInteractor] No hit");
            }
        }

        // RMB: seed (spawn)
        if (Input.GetMouseButtonDown(1))
        {
            var ray = cameraSource.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (!Physics.Raycast(ray, out var hit, 50f, hitMask, QueryTriggerInteraction.Ignore))
                return;

            // treat upward-facing surfaces as ground
            if (hit.normal.y < 0.35f)
                return;

            if (_inventory == null)
                _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
            if (_inventory == null)
                return;

            if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Seed, 1))
            {
                if (logHits) Debug.Log("[FPSRaycastInteractor] No seeds to plant");
                return;
            }

            var spawnPos = hit.point + hit.normal * 0.02f;
            var spawnRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(cameraSource.transform.forward, Vector3.up).normalized, Vector3.up);
            var spawned = FPSSeeding.SpawnFromSeed(spawnPos, spawnRot);

            // feedback
            FPSGameFeel.VoxelBurst(hit.point, count: 14, spread: 1.0f, life: 0.8f, cubeSize: 0.055f);
            FPSGameFeel.Shake(0.05f, 0.10f);

            if (logHits && spawned != null)
                Debug.Log($"[FPSRaycastInteractor] Planted: {spawned.name}");
        }
    }

    System.Collections.IEnumerator DestroyLater(GameObject go, float delay)
    {
        if (delay > 0f)
            yield return new WaitForSeconds(delay);
        if (go != null)
            Destroy(go);
    }

    static void SetRenderersEnabled(GameObject go, bool enabled)
    {
        var rs = go.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var r in rs) if (r != null) r.enabled = enabled;
    }

    static void SetCollidersEnabled(GameObject go, bool enabled)
    {
        var cs = go.GetComponentsInChildren<Collider>(includeInactive: true);
        foreach (var c in cs) if (c != null) c.enabled = enabled;
    }

    sealed class FPSHarvestedMarker : MonoBehaviour { }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        // If the scene doesn't have one, create a lightweight runtime interactor.
        if (FindFirstObjectByType<FPSRaycastInteractor>() != null) return;

        var go = new GameObject("FPSRaycastInteractor (Runtime)");
        DontDestroyOnLoad(go);
        go.AddComponent<FPSRaycastInteractor>();
    }
}
