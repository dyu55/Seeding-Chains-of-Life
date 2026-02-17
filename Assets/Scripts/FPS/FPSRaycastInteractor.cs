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

    [Header("Debug")]
    public bool logHits = true;

    void Awake()
    {
        if (cameraSource == null)
            cameraSource = Camera.main;
    }

    void Update()
    {
        if (cameraSource == null) return;

        if (Input.GetMouseButtonDown(0))
        {
            var ray = cameraSource.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            if (Physics.Raycast(ray, out var hit, maxDistance, hitMask, QueryTriggerInteraction.Ignore))
            {
                var go = hit.collider != null ? hit.collider.gameObject : null;
                if (go != null && go.CompareTag("Harvestable"))
                {
                    if (logHits)
                        Debug.Log($"[FPSRaycastInteractor] Harvestable hit: {go.name} (dist={hit.distance:0.00})", go);
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
    }

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
