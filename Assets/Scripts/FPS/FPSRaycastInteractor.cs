using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// FPS mouse interaction: on LMB, raycast from screen center and detect objects tagged "Harvestable"
/// within a max distance.
/// 
/// No XR dependencies.
/// </summary>
public class FPSRaycastInteractor : MonoBehaviour
{
    public enum ApplyTool
    {
        Seed,
        Water,
        Fire
    }

    public Camera cameraSource;
    public float maxDistance = 3f;
    public LayerMask hitMask = ~0;

    [Header("Harvest")]
    public bool destroyOnHarvest = true;
    [Tooltip("Small delay so the voxelize material swap can be seen before the object disappears.")]
    public float destroyDelaySeconds = 0.06f;

    [Header("Debug")]
    public bool logHits = true;

    [Header("Apply Tool (RMB)")]
    public ApplyTool currentTool = ApplyTool.Seed;
    [Min(1)] public int spreadLayers = 3;
    [Min(0.1f)] public float spreadLayerIntervalSeconds = 1f;
    [Min(0f)] public float lingerAfterSpreadSeconds = 0.05f;
    [Min(0.1f)] public float spreadCellSize = 1f;
    [Min(0.1f)] public float blockScale = 1f;
    [Range(0.01f, 0.5f)] public float blockThickness = 0.06f;
    [Min(0.1f)] public float surfaceProbeHeight = 10f;
    [Min(0f)] public float surfaceOffset = 0.01f;
    public Color waterSpreadColor = new Color(0.18f, 0.45f, 0.95f, 0.95f);
    public Color fireSpreadColor = new Color(0.95f, 0.18f, 0.14f, 0.95f);

    [Header("Seed Growth Models (Optional)")]
    public bool useImportedPlantStageModels = true;
    public GameObject sproutStagePrefab;
    public GameObject smallStagePrefab;
    public GameObject mediumStagePrefab;
    public GameObject matureStagePrefab;

    [Header("Water/Fire vs Planted Models")]
    [Min(0f)] public float waterBoostSecondsPerTile = 3f;
    public bool fireCanDestroyPlants = false;
    [Range(0f, 1f)] public float fireDestroyChance = 0.5f;
    [Min(0f)] public float fireDestroyDelaySeconds = 0.25f;

    SCoL.Inventory.SCoLInventory _inventory;
    Material _waterSpreadMat;
    Material _fireSpreadMat;
    FPSSeeding.GrowthSetup _growthSetup;

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

        _growthSetup = new FPSSeeding.GrowthSetup();
        RefreshGrowthSetup();
    }

    void Update()
    {
        if (cameraSource == null) return;
        HandleToolSwitchInput();

        // Primary: harvest
        if (SCoL.Interaction.SCoLInteractionInput.PrimaryPressed())
        {
            if (!SCoL.Interaction.SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
                return;

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

        // Secondary: apply selected tool (RMB)
        if (SCoL.Interaction.SCoLInteractionInput.SecondaryPressed())
        {
            if (!SCoL.Interaction.SCoLInteractionInput.TryGetAimRay(cameraSource, out var ray))
                return;

            if (!Physics.Raycast(ray, out var hit, 50f, hitMask, QueryTriggerInteraction.Ignore))
                return;

            // treat upward-facing surfaces as ground
            if (hit.normal.y < 0.35f)
                return;

            if (_inventory == null)
                _inventory = FindFirstObjectByType<SCoL.Inventory.SCoLInventory>();
            if (_inventory == null)
                return;

            switch (currentTool)
            {
                case ApplyTool.Seed:
                {
                    if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Seed, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No seeds to plant");
                        return;
                    }

                    var spawnPos = hit.point + hit.normal * 0.02f;
                    var spawnRot = Quaternion.LookRotation(Vector3.ProjectOnPlane(cameraSource.transform.forward, Vector3.up).normalized, Vector3.up);
                    RefreshGrowthSetup();
                    var spawned = FPSSeeding.SpawnFromSeed(spawnPos, spawnRot, _growthSetup);

                    FPSGameFeel.VoxelBurst(hit.point, count: 14, spread: 1.0f, life: 0.8f, cubeSize: 0.055f);
                    FPSGameFeel.Shake(0.05f, 0.10f);

                    if (logHits && spawned != null)
                        Debug.Log($"[FPSRaycastInteractor] Planted: {spawned.name}");
                    break;
                }

                case ApplyTool.Water:
                {
                    if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Water, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No water to place");
                        return;
                    }

                    StartCoroutine(SpawnTransientSpread(hit.point, hit.normal, GetWaterSpreadMat(), false, true));
                    break;
                }

                case ApplyTool.Fire:
                {
                    if (!_inventory.TryConsume(SCoL.Inventory.SCoLItemType.Fire, 1))
                    {
                        if (logHits) Debug.Log("[FPSRaycastInteractor] No fire to place");
                        return;
                    }

                    TryBurnTintTarget(hit);
                    StartCoroutine(SpawnTransientSpread(hit.point, hit.normal, GetFireSpreadMat(), true, false));
                    break;
                }
            }
        }
    }

    void RefreshGrowthSetup()
    {
        if (_growthSetup == null)
            _growthSetup = new FPSSeeding.GrowthSetup();

        if (!useImportedPlantStageModels)
        {
            _growthSetup.sproutPrefab = null;
            _growthSetup.smallPrefab = null;
            _growthSetup.mediumPrefab = null;
            _growthSetup.maturePrefab = null;
            return;
        }

        _growthSetup.sproutPrefab = sproutStagePrefab;
        _growthSetup.smallPrefab = smallStagePrefab;
        _growthSetup.mediumPrefab = mediumStagePrefab;
        _growthSetup.maturePrefab = matureStagePrefab;
    }

    void HandleToolSwitchInput()
    {
#if ENABLE_INPUT_SYSTEM
        var kb = Keyboard.current;
        if (kb == null) return;
        if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) currentTool = ApplyTool.Seed;
        if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) currentTool = ApplyTool.Water;
        if (kb.digit3Key.wasPressedThisFrame || kb.numpad3Key.wasPressedThisFrame) currentTool = ApplyTool.Fire;
#else
        if (Input.GetKeyDown(KeyCode.Alpha1)) currentTool = ApplyTool.Seed;
        if (Input.GetKeyDown(KeyCode.Alpha2)) currentTool = ApplyTool.Water;
        if (Input.GetKeyDown(KeyCode.Alpha3)) currentTool = ApplyTool.Fire;
#endif
    }

    System.Collections.IEnumerator SpawnTransientSpread(Vector3 hitPoint, Vector3 hitNormal, Material mat, bool burnTargets = false, bool waterTargets = false)
    {
        var spawned = new System.Collections.Generic.List<GameObject>(64);

        Vector3 center = SnapToVoxelCenter(hitPoint + hitNormal * 0.55f);
        SpawnBlock(center, mat, spawned, burnTargets, waterTargets);

        int layers = Mathf.Max(1, spreadLayers);
        float dt = Mathf.Max(0.1f, spreadLayerIntervalSeconds);
        for (int layer = 1; layer <= layers; layer++)
        {
            yield return new WaitForSeconds(dt);
            SpawnRing(center, layer, mat, spawned, burnTargets, waterTargets);
        }

        if (lingerAfterSpreadSeconds > 0f)
            yield return new WaitForSeconds(lingerAfterSpreadSeconds);

        for (int i = 0; i < spawned.Count; i++)
        {
            if (spawned[i] != null)
                Destroy(spawned[i]);
        }
    }

    void SpawnRing(Vector3 center, int ring, Material mat, System.Collections.Generic.List<GameObject> sink, bool burnTargets, bool waterTargets)
    {
        for (int z = -ring; z <= ring; z++)
        {
            for (int x = -ring; x <= ring; x++)
            {
                if (Mathf.Abs(x) != ring && Mathf.Abs(z) != ring)
                    continue;

                SpawnBlock(center + new Vector3(x * spreadCellSize, 0f, z * spreadCellSize), mat, sink, burnTargets, waterTargets);
            }
        }
    }

    void SpawnBlock(Vector3 pos, Material mat, System.Collections.Generic.List<GameObject> sink, bool burnTargets, bool waterTargets)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = currentTool == ApplyTool.Water ? "WaterTempBlock" : "FireTempBlock";
        Vector3 surfacePos = ProjectToSurface(pos);
        go.transform.position = surfacePos;
        go.transform.localScale = new Vector3(blockScale, blockThickness, blockScale);

        var r = go.GetComponent<Renderer>();
        if (r != null && mat != null)
            r.sharedMaterial = mat;

        var c = go.GetComponent<Collider>();
        if (c != null)
            Destroy(c);

        if (burnTargets)
        {
            BurnTargetsAtTile(surfacePos, Mathf.Max(0.05f, blockScale * 0.55f));
            FireAffectSeedGrowthAtTile(surfacePos, Mathf.Max(0.05f, blockScale * 0.55f));
        }
        else if (waterTargets)
        {
            WaterAffectSeedGrowthAtTile(surfacePos, Mathf.Max(0.05f, blockScale * 0.55f));
        }

        sink.Add(go);
    }

    Vector3 ProjectToSurface(Vector3 p)
    {
        Vector3 origin = new Vector3(p.x, p.y + Mathf.Max(0.1f, surfaceProbeHeight), p.z);
        float dist = Mathf.Max(0.2f, surfaceProbeHeight * 2f);
        if (Physics.Raycast(origin, Vector3.down, out var hit, dist, hitMask, QueryTriggerInteraction.Ignore))
        {
            float y = hit.point.y + blockThickness * 0.5f + surfaceOffset;
            return new Vector3(p.x, y, p.z);
        }
        return p;
    }

    void TryBurnTintTarget(RaycastHit hit)
    {
        Transform target = null;
        for (Transform t = hit.collider != null ? hit.collider.transform : null; t != null; t = t.parent)
        {
            if (t.CompareTag("Harvestable"))
            {
                target = t;
                break;
            }
        }

        if (target == null && hit.collider != null)
        {
            var anyRenderer = hit.collider.GetComponentInParent<Renderer>();
            if (anyRenderer != null)
                target = anyRenderer.transform;
        }

        if (target == null)
            return;

        BurnTintRenderers(target.GetComponentsInChildren<Renderer>(includeInactive: true));
    }

    void BurnTargetsAtTile(Vector3 tileCenter, float tileRadius)
    {
        var hits = Physics.OverlapSphere(tileCenter, Mathf.Max(0.05f, tileRadius), hitMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return;

        var seen = new System.Collections.Generic.HashSet<Transform>();
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;

            Transform root = null;
            for (Transform t = col.transform; t != null; t = t.parent)
            {
                if (t.CompareTag("Harvestable"))
                {
                    root = t;
                    break;
                }
            }

            if (root == null)
            {
                var anyRenderer = col.GetComponentInParent<Renderer>();
                if (anyRenderer != null)
                    root = anyRenderer.transform;
            }

            if (root == null || !seen.Add(root))
                continue;

            Vector3 p = ClosestPointOnRoot(root, tileCenter);
            Vector2 d = new Vector2(p.x - tileCenter.x, p.z - tileCenter.z);
            if (d.magnitude > tileRadius)
                continue;

            BurnTintRenderers(root.GetComponentsInChildren<Renderer>(includeInactive: true));
        }
    }

    void WaterAffectSeedGrowthAtTile(Vector3 tileCenter, float tileRadius)
    {
        if (waterBoostSecondsPerTile <= 0f)
            return;

        var roots = CollectGrowthRootsAtTile(tileCenter, tileRadius);
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] != null)
                roots[i].ApplyWaterBoost(waterBoostSecondsPerTile);
        }
    }

    void FireAffectSeedGrowthAtTile(Vector3 tileCenter, float tileRadius)
    {
        var roots = CollectGrowthRootsAtTile(tileCenter, tileRadius);
        for (int i = 0; i < roots.Count; i++)
        {
            if (roots[i] != null)
                roots[i].ApplyFire(fireCanDestroyPlants, fireDestroyChance, fireDestroyDelaySeconds);
        }
    }

    System.Collections.Generic.List<FPSSeedGrowth> CollectGrowthRootsAtTile(Vector3 tileCenter, float tileRadius)
    {
        var outList = new System.Collections.Generic.List<FPSSeedGrowth>(8);
        var hits = Physics.OverlapSphere(tileCenter, Mathf.Max(0.05f, tileRadius), hitMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return outList;

        var seen = new System.Collections.Generic.HashSet<FPSSeedGrowth>();
        for (int i = 0; i < hits.Length; i++)
        {
            var col = hits[i];
            if (col == null) continue;
            var growth = col.GetComponentInParent<FPSSeedGrowth>();
            if (growth == null || !seen.Add(growth))
                continue;

            Vector3 p = ClosestPointOnRoot(growth.transform, tileCenter);
            Vector2 d = new Vector2(p.x - tileCenter.x, p.z - tileCenter.z);
            if (d.magnitude > tileRadius)
                continue;
            outList.Add(growth);
        }
        return outList;
    }

    static Vector3 ClosestPointOnRoot(Transform root, Vector3 from)
    {
        if (root == null)
            return Vector3.zero;

        var cols = root.GetComponentsInChildren<Collider>(includeInactive: true);
        if (cols != null && cols.Length > 0)
        {
            bool has = false;
            Vector3 best = root.position;
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null) continue;
                Vector3 p = c.ClosestPoint(from);
                float d = (p - from).sqrMagnitude;
                if (!has || d < bestSq)
                {
                    has = true;
                    bestSq = d;
                    best = p;
                }
            }
            if (has) return best;
        }

        return root.position;
    }

    static void BurnTintRenderers(Renderer[] renderers)
    {
        if (renderers == null || renderers.Length == 0)
            return;

        for (int i = 0; i < renderers.Length; i++)
        {
            var r = renderers[i];
            if (r == null) continue;
            var m = r.material; // per-instance runtime material
            if (m == null) continue;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", Color.black);
            if (m.HasProperty("_Color")) m.SetColor("_Color", Color.black);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
        }
    }

    Vector3 SnapToVoxelCenter(Vector3 p)
    {
        return new Vector3(
            Mathf.Floor(p.x) + 0.5f,
            Mathf.Floor(p.y) + 0.5f,
            Mathf.Floor(p.z) + 0.5f
        );
    }

    Material GetWaterSpreadMat()
    {
        if (_waterSpreadMat != null) return _waterSpreadMat;
        _waterSpreadMat = NewSpreadMat("TempWaterSpreadMat", waterSpreadColor);
        return _waterSpreadMat;
    }

    Material GetFireSpreadMat()
    {
        if (_fireSpreadMat != null) return _fireSpreadMat;
        _fireSpreadMat = NewSpreadMat("TempFireSpreadMat", fireSpreadColor);
        return _fireSpreadMat;
    }

    static Material NewSpreadMat(string name, Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        var mat = new Material(shader) { name = name };
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        return mat;
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
