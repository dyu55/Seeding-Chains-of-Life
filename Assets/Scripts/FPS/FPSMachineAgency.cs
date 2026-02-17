using UnityEngine;

/// <summary>
/// T08: "Machine agency" for the voxel claw/tool view.
/// After player is idle for a while, the tool will:
/// - do a small look-around animation, and/or
/// - attempt to assimilate a nearby Harvestable (no inventory gain; just voxelize + feedback)
/// 
/// Runs in FPS only; no XR.
/// </summary>
[DisallowMultipleComponent]
public class FPSMachineAgency : MonoBehaviour
{
    [Header("Idle Detection")]
    public float idleSecondsToActivate = 6f;

    [Header("Actions")]
    public Vector2 actionIntervalRange = new Vector2(2.0f, 4.5f);
    [Range(0f, 1f)] public float chanceAssimilate = 0.55f;

    [Header("Assimilate")]
    public float assimilateRadius = 3f;
    public LayerMask assimilateMask = ~0;

    [Header("Animation")]
    public float bobAmount = 0.03f;
    public float bobSpeed = 3.5f;
    public float lookYawDegrees = 12f;
    public float lookPitchDegrees = 6f;

    float _idleTimer;
    float _nextActionAt;

    Vector3 _baseLocalPos;
    Quaternion _baseLocalRot;

    Camera _cam;

    void Awake()
    {
        _baseLocalPos = transform.localPosition;
        _baseLocalRot = transform.localRotation;
        _cam = Camera.main;

        ScheduleNextAction();
    }

    void Update()
    {
        if (DetectPlayerActivity())
        {
            _idleTimer = 0f;
            return;
        }

        _idleTimer += Time.deltaTime;

        if (_idleTimer < idleSecondsToActivate)
            return;

        // idle animation (subtle)
        float t = Time.time;
        float bob = Mathf.Sin(t * bobSpeed) * bobAmount;
        float yaw = Mathf.Sin(t * 0.9f) * lookYawDegrees;
        float pitch = Mathf.Sin(t * 1.2f) * lookPitchDegrees;

        transform.localPosition = _baseLocalPos + new Vector3(0f, bob, 0f);
        transform.localRotation = _baseLocalRot * Quaternion.Euler(pitch, yaw, 0f);

        if (Time.time >= _nextActionAt)
        {
            DoIdleAction();
            ScheduleNextAction();
        }
    }

    bool DetectPlayerActivity()
    {
        // Mouse look or movement input cancels idle.
        if (Mathf.Abs(Input.GetAxisRaw("Mouse X")) > 0.01f) return true;
        if (Mathf.Abs(Input.GetAxisRaw("Mouse Y")) > 0.01f) return true;
        if (Mathf.Abs(Input.GetAxisRaw("Horizontal")) > 0.01f) return true;
        if (Mathf.Abs(Input.GetAxisRaw("Vertical")) > 0.01f) return true;
        if (Input.GetMouseButtonDown(0) || Input.GetMouseButtonDown(1)) return true;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift)) return true;
        return false;
    }

    void ScheduleNextAction()
    {
        _nextActionAt = Time.time + Random.Range(actionIntervalRange.x, actionIntervalRange.y);
    }

    void DoIdleAction()
    {
        if (Random.value < chanceAssimilate)
        {
            TryAssimilateNearby();
        }
        else
        {
            // more noticeable "look" twitch
            transform.localRotation = _baseLocalRot * Quaternion.Euler(Random.Range(-lookPitchDegrees, lookPitchDegrees), Random.Range(-lookYawDegrees, lookYawDegrees), 0f);
        }
    }

    void TryAssimilateNearby()
    {
        var cam = _cam != null ? _cam : Camera.main;
        if (cam == null) return;

        // Look for Harvestable colliders near the camera.
        var hits = Physics.OverlapSphere(cam.transform.position, assimilateRadius, assimilateMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0) return;

        Transform best = null;
        float bestScore = float.PositiveInfinity;

        foreach (var c in hits)
        {
            if (c == null) continue;

            // Walk up to find a Harvestable root
            Transform h = null;
            for (var t = c.transform; t != null; t = t.parent)
                if (t.CompareTag("Harvestable")) { h = t; break; }

            if (h == null) continue;

            float d = Vector3.Distance(cam.transform.position, h.position);
            if (d < bestScore)
            {
                bestScore = d;
                best = h;
            }
        }

        if (best == null) return;

        // Assimilate and feedback
        VoxelAssimilator.Assimilate(best.gameObject);
        FPSGameFeel.VoxelBurst(best.position);
        FPSGameFeel.Shake(0.04f, 0.09f);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureAttachedToToolView()
    {
        // Best-effort: attach to the voxel tool view if present.
        var tool = GameObject.Find("VoxelClawView");
        if (tool != null && tool.GetComponent<FPSMachineAgency>() == null)
            tool.AddComponent<FPSMachineAgency>();
    }
}
