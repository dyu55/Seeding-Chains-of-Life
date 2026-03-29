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
    [Min(0.01f)] public float targetAssistRadius = 0.42f;

    Rigidbody _rb;
    Collider[] _colliders;
    readonly RaycastHit[] _sweepHits = new RaycastHit[12];
    readonly Collider[] _overlapHits = new Collider[12];
    readonly Collider[] _assistHits = new Collider[24];
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

            if (TryFindDamageableAssistHit(_lastSweepPosition, currentPosition, out var assistCollider, out var assistPoint, out var assistNormal))
            {
                HandleImpact(assistCollider, assistPoint, assistNormal);
                return;
            }

            if (TryFindBoidAssistHit(_lastSweepPosition, currentPosition, out var boidCollider, out var boidImpactPoint, out var boidImpactNormal))
            {
                HandleImpact(boidCollider, boidImpactPoint, boidImpactNormal);
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

    bool TryFindBoidAssistHit(Vector3 from, Vector3 to, out Collider hitCollider, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitCollider = null;
        hitPoint = to;
        hitNormal = -transform.forward;

        var activeBoids = FPSBoidAgent.ActiveAgentsView;
        if (activeBoids == null || activeBoids.Count == 0)
            return false;

        Vector3 segment = to - from;
        float segmentLengthSq = segment.sqrMagnitude;
        float bestScore = float.MaxValue;

        for (int i = 0; i < activeBoids.Count; i++)
        {
            var boid = activeBoids[i];
            if (boid == null || !boid.isActiveAndEnabled)
                continue;

            var health = boid.GetComponent<SCoLCombatHealth>();
            if (health == null || health.IsDead)
                continue;

            if (!TryGetBoidBounds(boid, out var bounds))
                continue;

            Vector3 samplePoint;
            if (segmentLengthSq <= 0.0001f)
            {
                samplePoint = to;
            }
            else
            {
                float t = Mathf.Clamp01(Vector3.Dot(bounds.center - from, segment) / segmentLengthSq);
                samplePoint = from + segment * t;
            }

            Vector3 closest = bounds.ClosestPoint(samplePoint);
            float bodyRadius = Mathf.Max(bounds.extents.x, bounds.extents.z);
            float allowedDistance = Mathf.Max(targetAssistRadius, 0.28f) + Mathf.Clamp(bodyRadius, 0.22f, 0.95f);
            float score = (closest - samplePoint).sqrMagnitude;
            if (score > allowedDistance * allowedDistance || score >= bestScore)
                continue;

            var collider = boid.GetComponent<Collider>();
            if (collider == null)
                collider = boid.GetComponentInChildren<Collider>();
            if (!IsValidImpactCollider(collider))
                continue;

            bestScore = score;
            hitCollider = collider;
            hitPoint = closest;
        }

        if (hitCollider == null)
            return false;

        hitNormal = to - hitPoint;
        if (hitNormal.sqrMagnitude < 0.0001f)
        {
            hitNormal = _rb != null && _rb.linearVelocity.sqrMagnitude > 0.0001f
                ? -_rb.linearVelocity.normalized
                : -transform.forward;
        }
        else
        {
            hitNormal.Normalize();
        }

        return true;
    }

    static bool TryGetBoidBounds(FPSBoidAgent boid, out Bounds bounds)
    {
        bounds = default;
        if (boid == null)
            return false;

        var colliders = boid.GetComponentsInChildren<Collider>(includeInactive: false);
        bool found = false;
        for (int i = 0; i < colliders.Length; i++)
        {
            var collider = colliders[i];
            if (collider == null || !collider.enabled || collider.isTrigger)
                continue;

            if (!found)
            {
                bounds = collider.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (found)
            return true;

        var renderers = boid.GetComponentsInChildren<Renderer>(includeInactive: false);
        for (int i = 0; i < renderers.Length; i++)
        {
            var renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
                continue;

            if (!found)
            {
                bounds = renderer.bounds;
                found = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return found;
    }

    bool HasDamageableTarget(Collider collider)
    {
        if (!IsValidImpactCollider(collider))
            return false;

        var root = collider.transform;
        if (root == null)
            return false;

        var health = root.GetComponentInParent<SCoLCombatHealth>();
        return health != null && !health.IsDead;
    }

    bool TryFindDamageableAssistHit(Vector3 from, Vector3 to, out Collider hitCollider, out Vector3 hitPoint, out Vector3 hitNormal)
    {
        hitCollider = null;
        hitPoint = to;
        hitNormal = -transform.forward;

        float assistRadius = Mathf.Max(_sweepRadius, targetAssistRadius);
        int hitCount = Physics.OverlapCapsuleNonAlloc(
            from,
            to,
            assistRadius,
            _assistHits,
            ~0,
            QueryTriggerInteraction.Ignore);

        if (hitCount <= 0)
            return false;

        float bestScore = float.MaxValue;
        Vector3 segment = to - from;
        float segmentLengthSq = segment.sqrMagnitude;
        for (int i = 0; i < hitCount; i++)
        {
            var collider = _assistHits[i];
            if (!HasDamageableTarget(collider))
                continue;

            Vector3 samplePoint;
            if (segmentLengthSq <= 0.0001f)
            {
                samplePoint = to;
            }
            else
            {
                float t = Mathf.Clamp01(Vector3.Dot(collider.bounds.center - from, segment) / segmentLengthSq);
                samplePoint = from + segment * t;
            }

            Vector3 closest = collider.ClosestPoint(samplePoint);
            float score = (closest - samplePoint).sqrMagnitude;
            if (score >= bestScore)
                continue;

            bestScore = score;
            hitCollider = collider;
            hitPoint = closest;
        }

        if (hitCollider == null)
            return false;

        hitNormal = to - hitPoint;
        if (hitNormal.sqrMagnitude < 0.0001f)
        {
            hitNormal = _rb != null && _rb.linearVelocity.sqrMagnitude > 0.0001f
                ? -_rb.linearVelocity.normalized
                : -transform.forward;
        }
        else
        {
            hitNormal.Normalize();
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
        {
            bool wasAlive = !health.IsDead;
            health.ApplyDamage(damage);
            if (wasAlive && health.IsDead && (health.Faction == SCoLCombatFaction.Animal || health.Faction == SCoLCombatFaction.Wolf))
                SCoL.Visualization.DayNightLightingController.PlayAnimalDamageAt(health.transform.position, 1f);
        }

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
