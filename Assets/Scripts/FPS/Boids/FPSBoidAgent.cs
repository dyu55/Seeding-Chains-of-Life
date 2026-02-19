using UnityEngine;
using System.Collections.Generic;
using SCoL.Voxels;

/// <summary>
/// T11: Craig Reynolds boids core agent.
/// Separation + Alignment + Cohesion + optional bounds.
/// 
/// Attach to voxel animal root. Works in FPS (no XR dependencies).
/// </summary>
[DisallowMultipleComponent]
public class FPSBoidAgent : MonoBehaviour
{
    public enum BoidRole
    {
        Prey = 0,
        Predator = 1
    }

    public enum PlantEatAction
    {
        RemovePlant = 0,
        ResetToSprout = 1
    }

    [Header("Role")]
    public BoidRole role = BoidRole.Prey;

    [Header("Neighborhood")]
    public float neighborRadius = 3.5f;
    public float separationRadius = 1.0f;

    [Header("Forces")]
    public float separationWeight = 1.6f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 1.0f;
    public float wanderWeight = 1.2f;

    [Header("Motion")]
    public float maxSpeed = 2.8f;
    public float maxForce = 6.0f;
    public float drag = 1.0f;

    [Header("Wander")]
    public bool useWander = true;
    [Min(0.05f)] public float wanderRetargetSeconds = 1.2f;
    [Min(0f)] public float wanderJitter = 0.35f;

    [Header("Ground Constraint")]
    public bool constrainToGround = false;
    public LayerMask groundMask = ~0;
    [Min(0.1f)] public float groundRaycastHeight = 10f;
    [Min(0f)] public float groundOffset = 0.02f;
    [Min(1f)] public float groundSnapSpeed = 16f;

    [Header("Player Avoidance (Prey)")]
    public float playerFleeDistance = 5f;
    public float playerFleeWeight = 2.2f;

    [Header("Predator/Prey")]
    public float predatorChaseRadius = 10f;
    public float predatorChaseWeight = 1.8f;
    public float preyFleePredatorRadius = 7f;
    public float preyFleePredatorWeight = 2.4f;

    [Header("Bounds")]
    public bool useBounds = true;
    public Vector3 boundsCenter;
    public Vector3 boundsSize = new Vector3(30, 6, 30);
    public float boundsWeight = 0.9f;

    [Header("Debug")]
    public bool drawDebug = false;

    [Header("Water Avoidance (Land Animals)")]
    public VoxelWorld voxelWorld;
    public bool avoidWaterColumns = false;
    [Min(0f)] public float waterAvoidWeight = 3.2f;
    [Range(1, 12)] public int waterSearchRadius = 5;

    [Header("Plant Eating")]
    public bool canEatMaturePlants = false;
    [Min(0.1f)] public float eatPlantRange = 1.15f;
    [Min(0.1f)] public float eatCheckIntervalSeconds = 0.4f;
    [Min(0f)] public float eatCooldownSeconds = 2.2f;
    [Min(0.05f)] public float eatHeadTouchDistance = 0.25f;
    [Min(0.1f)] public float eatHoldSeconds = 2f;
    public Vector3 eatHeadLocalOffset = new Vector3(0f, 0.22f, 0.28f);
    [Min(0f)] public float eatApproachWeight = 3.2f;
    public PlantEatAction eatAction = PlantEatAction.ResetToSprout;

    [HideInInspector] public Vector3 velocity;

    static readonly List<FPSBoidAgent> ActiveAgents = new List<FPSBoidAgent>(128);
    static Transform _plantAttractor;
    static bool _plantAttractorEnabled;
    static float _plantAttractorRadius = 8f;
    static float _plantAttractorWeight = 3f;

    Camera _playerCam;
    float _jumpOffsetY;
    float _feedReactionTimer;
    float _feedReactionDuration = 1.2f;
    float _feedReactionJumpHeight = 0.35f;
    int _feedReactionJumpCount = 3;
    Vector3 _wanderDir;
    float _nextWanderRetargetAt;
    float _nextEatCheckAt;
    float _nextEatAllowedAt;
    FPSSeedGrowth _eatTarget;
    float _eatHoldTimer;

    void OnEnable()
    {
        if (!ActiveAgents.Contains(this))
            ActiveAgents.Add(this);
    }

    void OnDisable()
    {
        ActiveAgents.Remove(this);
    }

    void Start()
    {
        // random initial velocity
        if (velocity.sqrMagnitude < 0.001f)
            velocity = Random.onUnitSphere * (maxSpeed * 0.5f);
        velocity.y = 0f;

        if (useBounds && boundsCenter == default)
            boundsCenter = transform.position;

        _playerCam = Camera.main;
        if (voxelWorld == null)
            voxelWorld = FindFirstObjectByType<VoxelWorld>();

        // predators are a bit faster by default
        if (role == BoidRole.Predator)
            maxSpeed *= 1.25f;

        _wanderDir = Random.insideUnitSphere;
        _wanderDir.y = 0f;
        if (_wanderDir.sqrMagnitude < 0.0001f)
            _wanderDir = Vector3.forward;
        _wanderDir.Normalize();
        _nextWanderRetargetAt = Time.time + Random.Range(0.05f, wanderRetargetSeconds);
    }

    void Update()
    {
        // Remove previous frame jump offset before boid integration.
        if (_jumpOffsetY != 0f)
        {
            var p0 = transform.position;
            p0.y -= _jumpOffsetY;
            transform.position = p0;
            _jumpOffsetY = 0f;
        }

        var accel = ComputeAcceleration();

        velocity += accel * Time.deltaTime;

        // drag
        velocity = Vector3.Lerp(velocity, Vector3.zero, drag * Time.deltaTime);

        // clamp speed
        float sp = velocity.magnitude;
        if (sp > maxSpeed) velocity = velocity / sp * maxSpeed;

        transform.position += velocity * Time.deltaTime;

        // Feed reaction: 3 jump pulses.
        if (_feedReactionTimer > 0f)
        {
            _feedReactionTimer -= Time.deltaTime;
            float elapsed = Mathf.Clamp(_feedReactionDuration - _feedReactionTimer, 0f, _feedReactionDuration);
            float t = _feedReactionDuration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / _feedReactionDuration);
            float wave = Mathf.Sin(t * Mathf.PI * 2f * Mathf.Max(1, _feedReactionJumpCount));
            if (wave < 0f) wave = 0f;
            _jumpOffsetY = wave * _feedReactionJumpHeight;

            var p1 = transform.position;
            p1.y += _jumpOffsetY;
            transform.position = p1;
        }

        if (constrainToGround)
            SnapToGround();

        if (canEatMaturePlants)
            UpdateEatProgress();

        // face direction
        if (velocity.sqrMagnitude > 0.01f)
        {
            var fwd = new Vector3(velocity.x, 0f, velocity.z);
            if (fwd.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(fwd.normalized, Vector3.up), 10f * Time.deltaTime);
        }

        if (drawDebug)
            DrawBounds();
    }

    Vector3 ComputeAcceleration()
    {
        Vector3 separation = Vector3.zero;
        Vector3 alignment = Vector3.zero;
        Vector3 cohesionCenter = Vector3.zero;
        int count = 0;
        int sepCount = 0;

        Vector3 myPos = transform.position;
        float neighborRadiusSqr = neighborRadius * neighborRadius;
        float separationRadiusSqr = separationRadius * separationRadius;

        // Iterate active boids directly instead of doing Physics.OverlapSphere each frame.
        // This is faster and doesn't depend on spawned animals having colliders.
        for (int i = 0; i < ActiveAgents.Count; i++)
        {
            var other = ActiveAgents[i];
            if (other == null || other == this) continue;

            Vector3 toOther = other.transform.position - myPos;
            toOther.y = 0f;
            float dSqr = toOther.sqrMagnitude;
            if (dSqr <= 0.000001f || dSqr > neighborRadiusSqr) continue;

            // Alignment + cohesion
            alignment += other.velocity;
            cohesionCenter += other.transform.position;
            count++;

            // Separation (stronger at close distances)
            if (dSqr < separationRadiusSqr)
            {
                separation += (-toOther) / dSqr;
                sepCount++;
            }
        }

        Vector3 accel = Vector3.zero;

        if (sepCount > 0)
        {
            separation /= sepCount;
            separation.y = 0f;
            accel += SteerTowards(separation) * separationWeight;
        }

        if (count > 0)
        {
            // Alignment
            alignment /= count;
            alignment.y = 0f;
            accel += SteerTowards(alignment) * alignmentWeight;

            // Cohesion
            cohesionCenter /= count;
            var toCenter = (cohesionCenter - transform.position);
            toCenter.y = 0f;
            accel += SteerTowards(toCenter) * cohesionWeight;
        }

        // Player avoidance / chase
        var cam = _playerCam != null ? _playerCam : Camera.main;
        bool hasPlantAttractor = _plantAttractorEnabled && _plantAttractor != null;
        if (cam != null)
        {
            float dToPlayer = Vector3.Distance(transform.position, cam.transform.position);

            if (role == BoidRole.Prey)
            {
                if (!hasPlantAttractor && dToPlayer < playerFleeDistance)
                {
                    var away = (transform.position - cam.transform.position);
                    away.y = 0f;
                    accel += SteerTowards(away) * playerFleeWeight;
                }
            }
        }

        // Plant lure: when player equips Plant tool, nearby animals follow.
        if (hasPlantAttractor)
        {
            var toAttractor = (_plantAttractor.position - transform.position);
            toAttractor.y = 0f;
            float d = toAttractor.magnitude;
            if (d <= Mathf.Max(0.1f, _plantAttractorRadius))
                accel += SteerTowards(toAttractor) * Mathf.Max(0f, _plantAttractorWeight);
        }
        else if (canEatMaturePlants && TryEnsureEatTarget())
        {
            var targetPos = GetEatTargetPoint(_eatTarget);
            var headPos = GetHeadWorldPosition();
            var toTarget = targetPos - headPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
                accel += SteerTowards(toTarget) * Mathf.Max(0f, eatApproachWeight);
        }
        else if (useWander)
        {
            RetargetWanderIfNeeded();
            accel += SteerTowards(_wanderDir) * Mathf.Max(0f, wanderWeight);
        }

        // Predator/prey dynamics (simple)
        if (role == BoidRole.Predator)
        {
            var target = FindNearestRole(BoidRole.Prey, predatorChaseRadius);
            if (target != null)
            {
                var to = (target.position - transform.position);
                to.y = 0f;
                accel += SteerTowards(to) * predatorChaseWeight;
            }
        }
        else // Prey
        {
            var threat = FindNearestRole(BoidRole.Predator, preyFleePredatorRadius);
            if (threat != null)
            {
                var away = (transform.position - threat.position);
                away.y = 0f;
                accel += SteerTowards(away) * preyFleePredatorWeight;
            }
        }

        if (useBounds)
        {
            var b = new Bounds(boundsCenter, boundsSize);
            if (!b.Contains(transform.position))
            {
                var to = (boundsCenter - transform.position);
                to.y = 0f;
                accel += SteerTowards(to) * boundsWeight;
            }
        }

        if (avoidWaterColumns && voxelWorld != null && voxelWorld.Config != null)
        {
            bool currentlyInWater = IsWaterColumnAtWorld(transform.position);
            Vector3 planarVel = new Vector3(velocity.x, 0f, velocity.z);
            bool headingIntoWater = false;

            if (planarVel.sqrMagnitude > 0.04f)
            {
                Vector3 ahead = transform.position + planarVel.normalized * Mathf.Max(0.5f, neighborRadius * 0.65f);
                headingIntoWater = IsWaterColumnAtWorld(ahead);
            }

            if (currentlyInWater || headingIntoWater)
            {
                if (TryFindNearestDryColumnWorld(transform.position, out var dryTarget))
                {
                    var toDry = dryTarget - transform.position;
                    toDry.y = 0f;
                    float w = currentlyInWater ? waterAvoidWeight : waterAvoidWeight * 0.75f;
                    accel += SteerTowards(toDry) * Mathf.Max(0f, w);
                }
            }
        }

        // clamp
        if (accel.magnitude > maxForce)
            accel = accel.normalized * maxForce;

        return accel;
    }

    public static void SetPlantAttractor(Transform target, bool enabled, float radius, float weight)
    {
        _plantAttractor = target;
        _plantAttractorEnabled = enabled && target != null;
        _plantAttractorRadius = Mathf.Max(0.1f, radius);
        _plantAttractorWeight = Mathf.Max(0f, weight);
    }

    public void FeedWithPlant(float jumpHeight, float reactionDurationSeconds, int jumps = 3)
    {
        _feedReactionJumpHeight = Mathf.Max(0.05f, jumpHeight);
        _feedReactionDuration = Mathf.Max(0.2f, reactionDurationSeconds);
        _feedReactionJumpCount = Mathf.Max(1, jumps);
        _feedReactionTimer = _feedReactionDuration;
    }

    void RetargetWanderIfNeeded()
    {
        if (Time.time < _nextWanderRetargetAt)
            return;

        _nextWanderRetargetAt = Time.time + Mathf.Max(0.05f, wanderRetargetSeconds);
        Vector3 j = new Vector3(
            Random.Range(-wanderJitter, wanderJitter),
            0f,
            Random.Range(-wanderJitter, wanderJitter)
        );
        _wanderDir += j;
        _wanderDir.y = 0f;
        if (_wanderDir.sqrMagnitude < 0.0001f)
            _wanderDir = Random.insideUnitSphere;
        _wanderDir.y = 0f;
        _wanderDir.Normalize();
    }

    void SnapToGround()
    {
        Vector3 p = transform.position;
        Vector3 origin = new Vector3(p.x, p.y + Mathf.Max(0.1f, groundRaycastHeight), p.z);
        float dist = Mathf.Max(0.2f, groundRaycastHeight * 2f);

        var hits = Physics.RaycastAll(origin, Vector3.down, dist, groundMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return;

        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        for (int i = 0; i < hits.Length; i++)
        {
            var hit = hits[i];
            if (hit.collider == null)
                continue;

            var t = hit.collider.transform;
            if (t == transform || t.IsChildOf(transform))
                continue;
            var otherBoid = hit.collider.GetComponentInParent<FPSBoidAgent>();
            if (otherBoid != null)
                continue;

            float targetY = hit.point.y + groundOffset + _jumpOffsetY;
            p.y = Mathf.Lerp(p.y, targetY, Mathf.Clamp01(groundSnapSpeed * Time.deltaTime));
            transform.position = p;
            return;
        }
    }

    bool TryEnsureEatTarget()
    {
        if (_eatTarget != null && IsValidEatTarget(_eatTarget))
            return true;

        _eatTarget = null;
        _eatHoldTimer = 0f;

        if (Time.time < _nextEatCheckAt)
            return false;
        _nextEatCheckAt = Time.time + Mathf.Max(0.1f, eatCheckIntervalSeconds);

        if (Time.time < _nextEatAllowedAt || !canEatMaturePlants)
            return false;

        float range = Mathf.Max(0.1f, eatPlantRange);
        var hits = Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Collide);
        if (hits == null || hits.Length == 0)
            return false;

        FPSSeedGrowth best = null;
        float bestD = float.PositiveInfinity;
        var seen = new HashSet<FPSSeedGrowth>();

        for (int i = 0; i < hits.Length; i++)
        {
            var c = hits[i];
            if (c == null) continue;

            var g = c.GetComponentInParent<FPSSeedGrowth>();
            if (g == null || !seen.Add(g))
                continue;
            if (!g.IsMature || g.IsBurned)
                continue;

            float d = (g.transform.position - transform.position).sqrMagnitude;
            if (d < bestD)
            {
                bestD = d;
                best = g;
            }
        }

        if (best == null)
            return false;

        _eatTarget = best;
        _eatHoldTimer = 0f;
        return true;
    }

    void UpdateEatProgress()
    {
        if (!TryEnsureEatTarget())
            return;

        if (_eatTarget == null || !IsValidEatTarget(_eatTarget))
        {
            _eatTarget = null;
            _eatHoldTimer = 0f;
            return;
        }

        Vector3 headPos = GetHeadWorldPosition();
        Vector3 targetPos = GetEatTargetPoint(_eatTarget);
        float touchDist = Vector3.Distance(headPos, targetPos);
        if (touchDist <= Mathf.Max(0.05f, eatHeadTouchDistance))
        {
            // Hold "eating" position for a short duration before applying result.
            _eatHoldTimer += Time.deltaTime;
            velocity = Vector3.Lerp(velocity, Vector3.zero, 10f * Time.deltaTime);

            if (_eatHoldTimer >= Mathf.Max(0.1f, eatHoldSeconds) && Time.time >= _nextEatAllowedAt)
            {
                if (eatAction == PlantEatAction.RemovePlant)
                    Destroy(_eatTarget.gameObject);
                else
                    _eatTarget.ResetToSprout(clearBurn: true);

                _nextEatAllowedAt = Time.time + Mathf.Max(0f, eatCooldownSeconds);
                _eatTarget = null;
                _eatHoldTimer = 0f;
            }
        }
        else
        {
            // Lost contact: reset hold timer and keep approaching.
            _eatHoldTimer = 0f;
        }
    }

    bool IsValidEatTarget(FPSSeedGrowth g)
    {
        return g != null && g.isActiveAndEnabled && g.IsMature && !g.IsBurned;
    }

    Vector3 GetHeadWorldPosition()
    {
        return transform.TransformPoint(eatHeadLocalOffset);
    }

    Vector3 GetEatTargetPoint(FPSSeedGrowth g)
    {
        if (g == null) return transform.position;
        Vector3 from = GetHeadWorldPosition();
        var cols = g.GetComponentsInChildren<Collider>(includeInactive: true);
        if (cols != null && cols.Length > 0)
        {
            bool has = false;
            Vector3 best = g.transform.position;
            float bestSq = float.PositiveInfinity;
            for (int i = 0; i < cols.Length; i++)
            {
                var c = cols[i];
                if (c == null) continue;

                if (c is MeshCollider mc && !mc.convex)
                    continue;
                if (!(c is BoxCollider) &&
                    !(c is SphereCollider) &&
                    !(c is CapsuleCollider) &&
                    !(c is MeshCollider))
                    continue;

                Vector3 p;
                try
                {
                    p = c.ClosestPoint(from);
                }
                catch (System.Exception)
                {
                    continue;
                }

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
        return g.transform.position;
    }

    bool IsWaterColumnAtWorld(Vector3 worldPos)
    {
        if (voxelWorld == null || voxelWorld.Config == null)
            return false;

        if (!voxelWorld.TryWorldToColumn(worldPos, out int x, out int z))
            return true;

        int sea = voxelWorld.Config.seaLevel;
        if (voxelWorld.GetBlock(x, sea, z) == VoxelBlockType.Water)
            return true;

        int surfaceY = voxelWorld.GetSurfaceY(x, z);
        if (surfaceY < sea)
            return true;

        int aboveY = surfaceY + 1;
        if (aboveY < voxelWorld.Config.worldHeight &&
            voxelWorld.GetBlock(x, aboveY, z) == VoxelBlockType.Water)
            return true;

        return false;
    }

    bool IsDryLandColumn(int x, int z)
    {
        if (voxelWorld == null || voxelWorld.Config == null)
            return false;
        if (x < 0 || z < 0 || x >= voxelWorld.Config.worldWidth || z >= voxelWorld.Config.worldDepth)
            return false;
        if (!voxelWorld.IsGrassSurface(x, z))
            return false;

        int surfaceY = voxelWorld.GetSurfaceY(x, z);
        if (surfaceY < voxelWorld.Config.seaLevel)
            return false;

        int aboveY = surfaceY + 1;
        if (aboveY < voxelWorld.Config.worldHeight &&
            voxelWorld.GetBlock(x, aboveY, z) == VoxelBlockType.Water)
            return false;

        return true;
    }

    bool TryFindNearestDryColumnWorld(Vector3 fromWorld, out Vector3 dryWorld)
    {
        dryWorld = fromWorld;
        if (voxelWorld == null || voxelWorld.Config == null)
            return false;
        if (!voxelWorld.TryWorldToColumn(fromWorld, out int cx, out int cz))
            return false;

        int bestX = -1;
        int bestZ = -1;
        float bestSq = float.PositiveInfinity;
        int maxR = Mathf.Max(1, waterSearchRadius);

        for (int r = 1; r <= maxR; r++)
        {
            bool foundThisRing = false;
            for (int dz = -r; dz <= r; dz++)
            for (int dx = -r; dx <= r; dx++)
            {
                if (Mathf.Abs(dx) != r && Mathf.Abs(dz) != r)
                    continue;

                int x = cx + dx;
                int z = cz + dz;
                if (!IsDryLandColumn(x, z))
                    continue;

                float sq = dx * dx + dz * dz;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    bestX = x;
                    bestZ = z;
                    foundThisRing = true;
                }
            }

            if (foundThisRing)
                break;
        }

        if (bestX < 0 || bestZ < 0)
            return false;

        int y = voxelWorld.GetSurfaceY(bestX, bestZ);
        dryWorld = voxelWorld.OriginWorld + new Vector3(bestX + 0.5f, y + 1f, bestZ + 0.5f);
        return true;
    }

    Vector3 SteerTowards(Vector3 desired)
    {
        if (desired.sqrMagnitude < 0.0001f) return Vector3.zero;

        var desiredVel = desired.normalized * maxSpeed;
        var steer = desiredVel - velocity;
        steer.y = 0f;
        if (steer.magnitude > maxForce) steer = steer.normalized * maxForce;
        return steer;
    }

    Transform FindNearestRole(BoidRole wanted, float radius)
    {
        Transform best = null;
        float bestD = float.PositiveInfinity;
        float radiusSqr = radius * radius;
        Vector3 myPos = transform.position;

        for (int i = 0; i < ActiveAgents.Count; i++)
        {
            var other = ActiveAgents[i];
            if (other == null || other == this) continue;
            if (other.role != wanted) continue;

            float dSqr = (other.transform.position - myPos).sqrMagnitude;
            if (dSqr > radiusSqr) continue;

            float d = Mathf.Sqrt(dSqr);
            if (d < bestD)
            {
                bestD = d;
                best = other.transform;
            }
        }

        return best;
    }

    void DrawBounds()
    {
        var b = new Bounds(boundsCenter, boundsSize);
        var c = b.center;
        var e = b.extents;

        Vector3 p000 = c + new Vector3(-e.x, -e.y, -e.z);
        Vector3 p001 = c + new Vector3(-e.x, -e.y, e.z);
        Vector3 p010 = c + new Vector3(-e.x, e.y, -e.z);
        Vector3 p011 = c + new Vector3(-e.x, e.y, e.z);
        Vector3 p100 = c + new Vector3(e.x, -e.y, -e.z);
        Vector3 p101 = c + new Vector3(e.x, -e.y, e.z);
        Vector3 p110 = c + new Vector3(e.x, e.y, -e.z);
        Vector3 p111 = c + new Vector3(e.x, e.y, e.z);

        Debug.DrawLine(p000, p001, Color.cyan);
        Debug.DrawLine(p000, p010, Color.cyan);
        Debug.DrawLine(p001, p011, Color.cyan);
        Debug.DrawLine(p010, p011, Color.cyan);

        Debug.DrawLine(p100, p101, Color.cyan);
        Debug.DrawLine(p100, p110, Color.cyan);
        Debug.DrawLine(p101, p111, Color.cyan);
        Debug.DrawLine(p110, p111, Color.cyan);

        Debug.DrawLine(p000, p100, Color.cyan);
        Debug.DrawLine(p001, p101, Color.cyan);
        Debug.DrawLine(p010, p110, Color.cyan);
        Debug.DrawLine(p011, p111, Color.cyan);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoAttachToSeedAnimals()
    {
        // Best-effort: attach to runtime-seeded animals.
        var all = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        foreach (var t in all)
        {
            if (t == null) continue;
            if (t.name.StartsWith("VoxelAnimal (Seed)") && t.GetComponent<FPSBoidAgent>() == null)
            {
                t.gameObject.AddComponent<FPSBoidAgent>();
            }
        }
    }
}
