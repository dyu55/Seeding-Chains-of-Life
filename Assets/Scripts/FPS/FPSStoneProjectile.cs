using UnityEngine;
using SCoL.Combat;

[DisallowMultipleComponent]
public sealed class FPSStoneProjectile : MonoBehaviour
{
    [Min(0.1f)] public float lifetimeSeconds = 6f;
    [Min(0f)] public float damage = 10f;
    [Min(0.05f)] public float hitReactionJumpHeight = 0.22f;
    [Min(0.1f)] public float hitReactionDuration = 0.55f;
    [Min(1)] public int hitReactionJumps = 2;
    [Min(0.01f)] public float sweepPadding = 0.06f;

    Rigidbody _rb;
    Collider[] _colliders;
    readonly RaycastHit[] _sweepHits = new RaycastHit[12];
    readonly Collider[] _overlapHits = new Collider[12];
    Vector3 _lastSweepPosition;
    float _sweepRadius;
    bool _consumed;

    void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>(includeInactive: true);
        _sweepRadius = ResolveSweepRadius();
    }

    void OnEnable()
    {
        _consumed = false;
        _lastSweepPosition = transform.position;
        Destroy(gameObject, Mathf.Max(0.5f, lifetimeSeconds));
    }

    void FixedUpdate()
    {
        if (_consumed)
            return;

        Vector3 currentPosition = transform.position;
        Vector3 delta = currentPosition - _lastSweepPosition;
        float distance = delta.magnitude;
        if (distance > 0.0001f)
        {
            var ray = new Ray(_lastSweepPosition, delta / distance);
            int hitCount = Physics.SphereCastNonAlloc(
                ray,
                _sweepRadius,
                _sweepHits,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            int bestIndex = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                var hit = _sweepHits[i];
                if (!IsValidImpactCollider(hit.collider))
                    continue;
                if (hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                var hit = _sweepHits[bestIndex];
                HandleImpact(hit.collider, hit.point, hit.normal);
                return;
            }
        }

        int overlapCount = Physics.OverlapSphereNonAlloc(
            currentPosition,
            _sweepRadius,
            _overlapHits,
            ~0,
            QueryTriggerInteraction.Ignore);

        for (int i = 0; i < overlapCount; i++)
        {
            var collider = _overlapHits[i];
            if (!IsValidImpactCollider(collider))
                continue;

            Vector3 impactPoint = collider.ClosestPoint(currentPosition);
            Vector3 impactNormal = currentPosition - impactPoint;
            if (impactNormal.sqrMagnitude < 0.0001f)
                impactNormal = _rb != null && _rb.linearVelocity.sqrMagnitude > 0.0001f
                    ? -_rb.linearVelocity.normalized
                    : -transform.forward;
            else
                impactNormal.Normalize();

            HandleImpact(collider, impactPoint, impactNormal);
            return;
        }

        _lastSweepPosition = currentPosition;
    }

    void OnCollisionEnter(Collision collision)
    {
        Vector3 impactPoint = collision.contactCount > 0 ? collision.GetContact(0).point : transform.position;
        Vector3 impactNormal = collision.contactCount > 0 ? collision.GetContact(0).normal : -transform.forward;
        HandleImpact(collision.collider, impactPoint, impactNormal);
    }

    float ResolveSweepRadius()
    {
        float radius = 0.12f;
        if (_colliders == null || _colliders.Length == 0)
            return radius;

        for (int i = 0; i < _colliders.Length; i++)
        {
            var collider = _colliders[i];
            if (collider == null)
                continue;

            if (collider is SphereCollider sphere)
            {
                float maxScale = Mathf.Max(
                    Mathf.Abs(collider.transform.lossyScale.x),
                    Mathf.Abs(collider.transform.lossyScale.y),
                    Mathf.Abs(collider.transform.lossyScale.z));
                radius = Mathf.Max(radius, sphere.radius * maxScale);
                continue;
            }

            var extents = collider.bounds.extents;
            radius = Mathf.Max(radius, Mathf.Max(extents.x, extents.y, extents.z));
        }

        return radius + Mathf.Max(0.01f, sweepPadding);
    }

    bool IsValidImpactCollider(Collider collider)
    {
        if (collider == null || !collider.enabled || collider.isTrigger)
            return false;
        if (_colliders == null)
            return true;

        for (int i = 0; i < _colliders.Length; i++)
        {
            if (_colliders[i] == collider)
                return false;
        }

        return true;
    }

    void HandleImpact(Collider collider, Vector3 impactPoint, Vector3 impactNormal)
    {
        if (_consumed || !IsValidImpactCollider(collider))
            return;

        _consumed = true;
        var root = collider != null ? collider.transform : null;
        var health = root != null ? root.GetComponentInParent<SCoLCombatHealth>() : null;
        if (health != null)
            health.ApplyDamage(damage);

        var boid = root != null ? root.GetComponentInParent<FPSBoidAgent>() : null;
        if (boid != null)
        {
            boid.FeedWithPlant(hitReactionJumpHeight, hitReactionDuration, hitReactionJumps);

            var swap = boid.GetComponent<AnimatedAnimalVisualSwap>();
            if (swap != null)
                swap.TriggerHit();
        }

        FPSGameFeel.VoxelBurst(impactPoint, count: 8, spread: 0.45f, life: 0.35f, cubeSize: 0.04f);
        FPSGameFeel.Shake(0.03f, 0.06f);
        Destroy(gameObject);
    }
}
