using UnityEngine;
using System.Collections.Generic;
using SCoL.Voxels;
using SCoL;
using SCoL.Combat;
using SCoL.Settlement;

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
    [Tooltip("If true, land animals immediately turn back when their forward lookahead hits water.")]
    public bool hardTurnAtWaterEdge = true;
    [Min(0.1f)] public float waterEdgeLookAheadDistance = 1.1f;
    [Min(0.1f)] public float waterEdgeTurnSpeedMultiplier = 1.2f;
    [Min(0f)] public float waterEdgeExtraAvoidWeight = 3.0f;
    [Tooltip("If true, land animals are forcefully pushed back to nearest dry column when they enter water.")]
    public bool hardEjectFromWater = true;
    [Tooltip("If true, snap directly to nearest dry column instead of slowly lerping while in water.")]
    public bool instantEjectFromWater = true;
    [Min(1f)] public float waterEjectLerpSpeed = 12f;
    [Min(0f)] public float waterEjectHeightOffset = 0.08f;

    [Header("Plant Eating")]
    public bool canEatMaturePlants = false;
    [Min(0.1f)] public float eatPlantRange = 1.15f;
    [Min(0.1f)] public float eatCheckIntervalSeconds = 0.4f;
    [Min(0f)] public float eatCooldownSeconds = 2.2f;
    [Min(0.05f)] public float eatHeadTouchDistance = 0.25f;
    [Min(0.1f)] public float eatHoldSeconds = 3f;
    public Vector3 eatHeadLocalOffset = new Vector3(0f, 0.22f, 0.28f);
    [Min(0f)] public float eatApproachWeight = 3.2f;
    public PlantEatAction eatAction = PlantEatAction.ResetToSprout;
    [Tooltip("If true, animals can target CA-rendered mature flowers (no collider required).")]
    public bool canEatCARuntimePlants = true;

    [Header("Ecology Response")]
    public bool seekPlantDensityZones = true;
    [Min(0.5f)] public float plantDensitySearchRadius = 14f;
    [Min(0.5f)] public float plantDensitySampleRadius = 4f;
    [Min(1)] public int plantDensityMinScore = 1;
    [Min(0f)] public float plantDensitySeekWeight = 4.5f;
    public bool seekPreyDensityZones = true;
    [Min(0.5f)] public float preyDensitySearchRadius = 22f;
    [Min(0.5f)] public float preyDensitySampleRadius = 6f;
    [Min(1)] public int preyDensityMinScore = 1;
    [Min(0f)] public float preyDensitySeekWeight = 5.2f;
    [Min(0f)] public float ecologyHotspotHoldSeconds = 6f;
    [Min(0.1f)] public float ecologyHotspotArrivalDistance = 2.2f;

    [Header("Combat")]
    public bool canAttackPlayer = false;
    public bool canAttackOtherAnimals = false;
    [Min(0.1f)] public float attackRange = 1.35f;
    [Min(0f)] public float attackDamage = 10f;
    [Min(0.1f)] public float attackCooldownSeconds = 1.1f;
    [Min(0f)] public float attackApproachWeight = 4.2f;

    [Header("Predator Audio")]
    public bool howlWhenPlayerDetected = true;
    [Min(0f)] public float wolfHowlMaxDistance = 12f;
    [Min(0f)] public float wolfHowlCooldownSeconds = 8f;

    [HideInInspector] public Vector3 velocity;

    static readonly List<FPSBoidAgent> ActiveAgents = new List<FPSBoidAgent>(128);
    public static IReadOnlyList<FPSBoidAgent> ActiveAgentsView => ActiveAgents;
    static Transform _plantAttractor;
    static bool _plantAttractorEnabled;
    static float _plantAttractorRadius = 8f;
    static float _plantAttractorWeight = 3f;
    static float _plantAttractorStopDistance = 1.1f;
    static float _plantAttractorFrontOffset = 1.4f;
    static Transform _fireRepellent;
    static bool _fireRepellentEnabled;
    static float _fireRepellentRadius = 8f;
    static float _fireRepellentWeight = 4f;
    static float _fireRepellentFrontOffset = 1.2f;

    Camera _playerCam;
    float _feedReactionTimer;
    float _feedReactionDuration = 1.2f;
    float _feedReactionJumpHeight = 0.35f;
    int _feedReactionJumpCount = 3;
    float _nextAttackAt;
    SCoLCombatHealth _combatHealth;
    Vector3 _wanderDir;
    float _nextWanderRetargetAt;
    float _nextEatCheckAt;
    float _nextEatAllowedAt;
    FPSSeedGrowth _eatTarget;
    bool _eatTargetIsCA;
    int _eatTargetCellX;
    int _eatTargetCellY;
    float _eatHoldTimer;
    SCoLRuntime _runtime;
    SCoLCombatHealth _lastCombatTarget;
    float _nextWolfHowlAt;
    bool _hasEcologyHotspot;
    Vector3 _ecologyHotspot;
    float _ecologyHotspotUntil;

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
        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();

        // predators are a bit faster by default
        if (role == BoidRole.Predator)
            maxSpeed *= 1.25f;

        _wanderDir = Random.insideUnitSphere;
        _wanderDir.y = 0f;
        if (_wanderDir.sqrMagnitude < 0.0001f)
            _wanderDir = Vector3.forward;
        _wanderDir.Normalize();
        _nextWanderRetargetAt = Time.time + Random.Range(0.05f, wanderRetargetSeconds);
        _combatHealth = GetComponent<SCoLCombatHealth>();
    }

    void Update()
    {
        var accel = ComputeAcceleration();

        velocity += accel * Time.deltaTime;

        // drag
        velocity = Vector3.Lerp(velocity, Vector3.zero, drag * Time.deltaTime);

        // clamp speed
        float sp = velocity.magnitude;
        if (sp > maxSpeed) velocity = velocity / sp * maxSpeed;

        ApplyHardWaterEdgeTurn();

        transform.position += velocity * Time.deltaTime;

        if (avoidWaterColumns && hardEjectFromWater)
            EjectFromWaterIfNeeded();

        if (constrainToGround)
            SnapToGround();

        if (avoidWaterColumns && hardEjectFromWater)
            EjectFromWaterIfNeeded();

        ApplyFeedReactionHop();

        if (canEatMaturePlants)
            UpdateEatProgress();

        TryAttackCombatTarget();

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
        bool hasFireRepellent = _fireRepellentEnabled && _fireRepellent != null;
        bool plantAttractorInRange = false;
        bool fireRepellentInRange = false;
        Vector3 toAttractor = Vector3.zero;
        Vector3 awayFromFire = Vector3.zero;
        float plantAttractorDistance = float.PositiveInfinity;
        float fireRepellentDistance = float.PositiveInfinity;
        if (hasPlantAttractor)
        {
            Vector3 targetPoint = _plantAttractor.position;
            Vector3 forward = _plantAttractor.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                targetPoint += forward.normalized * Mathf.Max(0f, _plantAttractorFrontOffset);

            toAttractor = targetPoint - transform.position;
            toAttractor.y = 0f;
            plantAttractorDistance = toAttractor.magnitude;
            plantAttractorInRange = plantAttractorDistance <= Mathf.Max(0.1f, _plantAttractorRadius);
        }
        if (hasFireRepellent)
        {
            Vector3 firePoint = _fireRepellent.position;
            Vector3 forward = _fireRepellent.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.0001f)
                firePoint += forward.normalized * Mathf.Max(0f, _fireRepellentFrontOffset);

            awayFromFire = transform.position - firePoint;
            awayFromFire.y = 0f;
            fireRepellentDistance = awayFromFire.magnitude;
            fireRepellentInRange = fireRepellentDistance <= Mathf.Max(0.1f, _fireRepellentRadius);
        }
        if (cam != null)
        {
            float dToPlayer = Vector3.Distance(transform.position, cam.transform.position);

            if (role == BoidRole.Prey)
            {
                if (!plantAttractorInRange && !fireRepellentInRange && dToPlayer < playerFleeDistance)
                {
                    var away = (transform.position - cam.transform.position);
                    away.y = 0f;
                    accel += SteerTowards(away) * playerFleeWeight;
                }
            }
        }

        if (fireRepellentInRange)
        {
            float t = 1f - Mathf.Clamp01(fireRepellentDistance / Mathf.Max(0.1f, _fireRepellentRadius));
            accel += SteerTowards(awayFromFire) * Mathf.Max(0f, _fireRepellentWeight) * Mathf.Lerp(0.5f, 1.5f, t);
            _wanderDir = awayFromFire.sqrMagnitude > 0.0001f ? awayFromFire.normalized : _wanderDir;
            _nextWanderRetargetAt = Time.time + Mathf.Max(0.15f, wanderRetargetSeconds * 0.5f);
        }
        // Plant lure: when player equips Plant tool, nearby animals follow.
        else if (plantAttractorInRange)
        {
            float stopDistance = Mathf.Max(0.1f, _plantAttractorStopDistance);
            if (plantAttractorDistance > stopDistance)
            {
                float slowRadius = Mathf.Max(stopDistance + 0.75f, stopDistance * 1.8f);
                float t = Mathf.InverseLerp(stopDistance, slowRadius, plantAttractorDistance);
                accel += SteerTowards(toAttractor) * Mathf.Max(0f, _plantAttractorWeight) * Mathf.Clamp01(t);
            }
            else
            {
                // Brake as the animal reaches the player's front-side rendezvous point.
                velocity = Vector3.Lerp(velocity, Vector3.zero, Mathf.Clamp01(6f * Time.deltaTime));
            }
        }
        else if (TryGetCombatTarget(out var combatTarget))
        {
            var toTarget = combatTarget.transform.position - transform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
                accel += SteerTowards(toTarget) * Mathf.Max(0f, attackApproachWeight);
        }
        else if (role == BoidRole.Predator && seekPreyDensityZones && TryFindRoleDensityHotspot(BoidRole.Prey, preyDensitySearchRadius, preyDensitySampleRadius, preyDensityMinScore, out var preyHotspot, out _))
        {
            RememberEcologyHotspot(preyHotspot);
            var toHotspot = preyHotspot - transform.position;
            toHotspot.y = 0f;
            float arrive = Mathf.Max(0.1f, ecologyHotspotArrivalDistance);
            if (toHotspot.sqrMagnitude > arrive * arrive)
                accel += SteerTowards(toHotspot) * Mathf.Max(0f, preyDensitySeekWeight);
            else
                velocity = Vector3.Lerp(velocity, Vector3.zero, Mathf.Clamp01(4f * Time.deltaTime));
        }
        else if (canEatMaturePlants && TryEnsureEatTarget())
        {
            var targetPos = GetCurrentEatTargetPoint();
            var headPos = GetHeadWorldPosition();
            var toTarget = targetPos - headPos;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
                accel += SteerTowards(toTarget) * Mathf.Max(0f, eatApproachWeight);
        }
        else if (role == BoidRole.Prey && seekPlantDensityZones && TryFindPlantDensityHotspot(out var plantHotspot, out _))
        {
            RememberEcologyHotspot(plantHotspot);
            var toPlants = plantHotspot - transform.position;
            toPlants.y = 0f;
            float arrive = Mathf.Max(0.1f, ecologyHotspotArrivalDistance);
            if (toPlants.sqrMagnitude > arrive * arrive)
                accel += SteerTowards(toPlants) * Mathf.Max(0f, plantDensitySeekWeight);
            else
                velocity = Vector3.Lerp(velocity, Vector3.zero, Mathf.Clamp01(4f * Time.deltaTime));
        }
        else if (_hasEcologyHotspot && Time.time < _ecologyHotspotUntil)
        {
            var toHotspot = _ecologyHotspot - transform.position;
            toHotspot.y = 0f;
            float arrive = Mathf.Max(0.1f, ecologyHotspotArrivalDistance);
            if (toHotspot.sqrMagnitude > arrive * arrive)
            {
                float w = role == BoidRole.Predator ? preyDensitySeekWeight * 0.8f : plantDensitySeekWeight * 0.8f;
                accel += SteerTowards(toHotspot) * Mathf.Max(0f, w);
            }
            else
            {
                velocity = Vector3.Lerp(velocity, Vector3.zero, Mathf.Clamp01(3f * Time.deltaTime));
            }
        }
        else if (useWander)
        {
            _hasEcologyHotspot = false;
            RetargetWanderIfNeeded();
            accel += SteerTowards(_wanderDir) * Mathf.Max(0f, wanderWeight);
        }

        var settlement = SCoLSettlementManager.Instance;
        if (settlement != null && settlement.IsActivated)
        {
            float avoidRadius = settlement.SafeZoneRadius + Mathf.Max(0.5f, settlement.SlowZonePadding);
            float distToSettlement = settlement.DistanceToCenterXZ(transform.position);
            if (distToSettlement < avoidRadius)
            {
                Vector3 away = settlement.GetSafeZoneRepelDirection(transform.position);
                bool insideSettlement = settlement.IsInsideSafeZone(transform.position);
                float t = insideSettlement
                    ? 1f
                    : 1f - Mathf.Clamp01((distToSettlement - settlement.SafeZoneRadius) / Mathf.Max(0.1f, avoidRadius - settlement.SafeZoneRadius));
                float avoidWeight = role == BoidRole.Predator
                    ? Mathf.Lerp(1.8f, 5.5f, Mathf.Clamp01(t))
                    : Mathf.Lerp(1.2f, 4.2f, Mathf.Clamp01(t));
                accel += SteerTowards(away) * avoidWeight;

                _wanderDir = away.sqrMagnitude > 0.0001f ? away.normalized : _wanderDir;
                _nextWanderRetargetAt = Time.time + Mathf.Max(0.2f, wanderRetargetSeconds * 0.5f);

                if (insideSettlement)
                {
                    Vector3 planarVelocity = new Vector3(velocity.x, 0f, velocity.z);
                    float speed = Mathf.Max(1.2f, planarVelocity.magnitude, maxSpeed * 0.55f);
                    Vector3 desired = away * speed;
                    Vector3 turned = planarVelocity.sqrMagnitude < 0.01f || Vector3.Dot(planarVelocity.normalized, away) < 0.25f
                        ? desired
                        : Vector3.Lerp(planarVelocity, desired, 0.65f);
                    velocity.x = turned.x;
                    velocity.z = turned.z;
                }
            }
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

    void ApplyHardWaterEdgeTurn()
    {
        if (!avoidWaterColumns || !hardTurnAtWaterEdge || voxelWorld == null || voxelWorld.Config == null)
            return;

        Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
        if (planar.sqrMagnitude < 0.0001f)
            return;

        float lookAhead = Mathf.Max(0.1f, waterEdgeLookAheadDistance);
        Vector3 ahead = transform.position + planar.normalized * lookAhead;
        if (!IsWaterColumnAtWorld(ahead))
            return;

        Vector3 away = -planar.normalized;
        if (TryFindNearestDryColumnWorld(transform.position, out var dryTarget))
        {
            Vector3 toDry = dryTarget - transform.position;
            toDry.y = 0f;
            if (toDry.sqrMagnitude > 0.0001f)
                away = (away + toDry.normalized * Mathf.Max(0f, waterEdgeExtraAvoidWeight * 0.5f)).normalized;
        }

        float speed = Mathf.Max(0.2f, planar.magnitude * Mathf.Max(0.1f, waterEdgeTurnSpeedMultiplier));
        Vector3 turned = away * speed;
        velocity.x = turned.x;
        velocity.z = turned.z;

        _wanderDir = away;
        _nextWanderRetargetAt = Time.time + Mathf.Max(0.2f, wanderRetargetSeconds * 0.6f);
    }

    public static void SetPlantAttractor(Transform target, bool enabled, float radius, float weight, float stopDistance = 1.1f, float frontOffset = 1.4f)
    {
        _plantAttractor = target;
        _plantAttractorEnabled = enabled && target != null;
        _plantAttractorRadius = Mathf.Max(0.1f, radius);
        _plantAttractorWeight = Mathf.Max(0f, weight);
        _plantAttractorStopDistance = Mathf.Max(0.1f, stopDistance);
        _plantAttractorFrontOffset = Mathf.Max(0f, frontOffset);
    }

    public static void SetFireRepellent(Transform target, bool enabled, float radius, float weight, float frontOffset = 1.2f)
    {
        _fireRepellent = target;
        _fireRepellentEnabled = enabled && target != null;
        _fireRepellentRadius = Mathf.Max(0.1f, radius);
        _fireRepellentWeight = Mathf.Max(0f, weight);
        _fireRepellentFrontOffset = Mathf.Max(0f, frontOffset);
    }

    public void FeedWithPlant(float jumpHeight, float reactionDurationSeconds, int jumps = 3)
    {
        _feedReactionJumpHeight = Mathf.Max(0.05f, jumpHeight);
        _feedReactionDuration = Mathf.Max(0.2f, reactionDurationSeconds);
        _feedReactionJumpCount = Mathf.Max(1, jumps);
        _feedReactionTimer = _feedReactionDuration;
        // Feed reaction should always be grounded for land animals.
        constrainToGround = true;
    }

    void TryAttackCombatTarget()
    {
        if (Time.time < _nextAttackAt)
            return;
        if (!TryGetCombatTarget(out var target) || target == null || target.IsDead)
            return;
        var settlement = SCoLSettlementManager.Instance;
        if (settlement != null && settlement.IsProtectedCombatTarget(target.transform))
            return;

        float range = Mathf.Max(0.1f, attackRange);
        Vector3 a = transform.position;
        Vector3 b = target.transform.position;
        a.y = 0f;
        b.y = 0f;
        if ((a - b).sqrMagnitude > range * range)
            return;

        if (target.ApplyDamage(Mathf.Max(0f, attackDamage)))
        {
            _nextAttackAt = Time.time + Mathf.Max(0.1f, attackCooldownSeconds);

            var visualSwap = GetComponent<AnimatedAnimalVisualSwap>();
            if (visualSwap != null)
                visualSwap.TriggerAttack();

            if (target.Faction == SCoLCombatFaction.Player)
            {
                SCoL.Visualization.DayNightLightingController.PlayInteractionSfx(SCoL.Visualization.DayNightLightingController.InteractionSfx.ManDamage);
                SCoL.Visualization.DayNightLightingController.PlayInteractionSfx(SCoL.Visualization.DayNightLightingController.InteractionSfx.ManScream);
            }

            if (target.TryGetComponent<FPSBoidAgent>(out var boid))
                boid.FeedWithPlant(0.18f, 0.45f, 2);
        }
    }

    bool TryGetCombatTarget(out SCoLCombatHealth target)
    {
        target = null;
        if (!canAttackPlayer && !canAttackOtherAnimals)
        {
            _lastCombatTarget = null;
            return false;
        }

        var activeHealths = SCoLCombatHealth.ActiveHealths;
        if (activeHealths == null)
            return false;

        float bestScore = float.PositiveInfinity;
        Vector3 myPos = transform.position;

        for (int i = 0; i < activeHealths.Count; i++)
        {
            var candidate = activeHealths[i];
            if (candidate == null || candidate == _combatHealth || candidate.IsDead)
                continue;

            bool validFaction =
                (canAttackPlayer && candidate.Faction == SCoLCombatFaction.Player) ||
                (canAttackOtherAnimals && candidate.Faction == SCoLCombatFaction.Animal);
            if (!validFaction)
                continue;
            var settlement = SCoLSettlementManager.Instance;
            if (settlement != null && settlement.IsProtectedCombatTarget(candidate.transform))
                continue;

            float dist = Vector3.Distance(candidate.transform.position, myPos);
            float score = dist;
            if (candidate.Faction == SCoLCombatFaction.Player)
                score -= 0.15f;

            if (score >= bestScore)
                continue;

            bestScore = score;
            target = candidate;
        }

        if (target != _lastCombatTarget)
        {
            if (target != null &&
                role == BoidRole.Predator &&
                howlWhenPlayerDetected &&
                target.Faction == SCoLCombatFaction.Player &&
                Time.time >= _nextWolfHowlAt)
            {
                float howlDistance = Mathf.Max(0f, wolfHowlMaxDistance);
                float playerDistance = Vector3.Distance(transform.position, target.transform.position);
                if (playerDistance <= howlDistance)
                {
                    SCoL.Visualization.DayNightLightingController.PlayInteractionSfx(SCoL.Visualization.DayNightLightingController.InteractionSfx.WolfHowl);
                    _nextWolfHowlAt = Time.time + Mathf.Max(0f, wolfHowlCooldownSeconds);
                }
            }
            _lastCombatTarget = target;
        }

        return target != null;
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
        if (voxelWorld == null)
            voxelWorld = FindFirstObjectByType<VoxelWorld>();

        if (avoidWaterColumns && voxelWorld != null && IsWaterColumnAtWorld(transform.position))
        {
            // Do not stick to seabed/surface while in water; first force return to dry land.
            EjectFromWaterIfNeeded();
            return;
        }

        if (voxelWorld != null && voxelWorld.TryGetTerrainSurfaceYAtWorld(transform.position, out float surfaceY, includeWaterSurface: false))
        {
            Vector3 p0 = transform.position;
            float targetY0 = surfaceY + groundOffset;
            p0.y = Mathf.Lerp(p0.y, targetY0, Mathf.Clamp01(groundSnapSpeed * Time.deltaTime));
            transform.position = p0;
            return;
        }

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

            float targetY = hit.point.y + groundOffset;
            p.y = Mathf.Lerp(p.y, targetY, Mathf.Clamp01(groundSnapSpeed * Time.deltaTime));
            transform.position = p;
            return;
        }
    }

    void ApplyFeedReactionHop()
    {
        if (_feedReactionTimer <= 0f)
            return;

        _feedReactionTimer -= Time.deltaTime;
        float elapsed = Mathf.Clamp(_feedReactionDuration - _feedReactionTimer, 0f, _feedReactionDuration);
        float t = _feedReactionDuration <= 0.0001f ? 1f : Mathf.Clamp01(elapsed / _feedReactionDuration);
        float wave = Mathf.Sin(t * Mathf.PI * 2f * Mathf.Max(1, _feedReactionJumpCount));
        if (wave < 0f) wave = 0f;
        float hopY = wave * _feedReactionJumpHeight;

        if (TryGetGroundY(out float groundY))
        {
            var p = transform.position;
            p.y = groundY + hopY;
            transform.position = p;
        }
    }

    bool TryGetGroundY(out float groundY)
    {
        groundY = transform.position.y;

        if (voxelWorld == null)
            voxelWorld = FindFirstObjectByType<VoxelWorld>();

        if (avoidWaterColumns && voxelWorld != null && IsWaterColumnAtWorld(transform.position))
            return false;

        if (voxelWorld != null && voxelWorld.TryGetTerrainSurfaceYAtWorld(transform.position, out float surfaceY, includeWaterSurface: false))
        {
            groundY = surfaceY + groundOffset;
            return true;
        }

        Vector3 p = transform.position;
        Vector3 origin = new Vector3(p.x, p.y + Mathf.Max(0.1f, groundRaycastHeight), p.z);
        float dist = Mathf.Max(0.2f, groundRaycastHeight * 2f);

        var hits = Physics.RaycastAll(origin, Vector3.down, dist, groundMask, QueryTriggerInteraction.Ignore);
        if (hits == null || hits.Length == 0)
            return false;

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

            groundY = hit.point.y + groundOffset;
            return true;
        }

        return false;
    }

    void EjectFromWaterIfNeeded()
    {
        if (!avoidWaterColumns || voxelWorld == null || voxelWorld.Config == null)
            return;
        if (!IsWaterColumnAtWorld(transform.position))
            return;
        if (!TryFindNearestDryColumnWorld(transform.position, out var dryWorld))
        {
            // Fallback: if we somehow cannot find dry land in search radius, force a turn-around.
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);
            if (planar.sqrMagnitude > 0.0001f)
            {
                Vector3 away = -planar.normalized * Mathf.Max(0.2f, maxSpeed * 0.8f);
                velocity.x = away.x;
                velocity.z = away.z;
            }
            return;
        }

        Vector3 current = transform.position;
        Vector3 p = current;
        Vector3 toDryPlanar = dryWorld - current;
        toDryPlanar.y = 0f;
        if (instantEjectFromWater)
        {
            p.x = dryWorld.x;
            p.z = dryWorld.z;
        }
        else
        {
            float t = Mathf.Clamp01(Time.deltaTime * Mathf.Max(1f, waterEjectLerpSpeed));
            p.x = Mathf.Lerp(p.x, dryWorld.x, t);
            p.z = Mathf.Lerp(p.z, dryWorld.z, t);
        }

        if (voxelWorld.TryGetTerrainSurfaceYAtWorld(dryWorld, out float drySurfaceY, includeWaterSurface: false))
            p.y = Mathf.Max(p.y, drySurfaceY + groundOffset + Mathf.Max(0f, waterEjectHeightOffset));
        else
            p.y = Mathf.Max(p.y, dryWorld.y + Mathf.Max(0f, waterEjectHeightOffset));

        transform.position = p;

        if (toDryPlanar.sqrMagnitude > 0.0001f)
        {
            Vector3 dir = toDryPlanar.normalized;
            float speed = Mathf.Max(0.2f, maxSpeed * 0.75f);
            velocity.x = dir.x * speed;
            velocity.z = dir.z * speed;
            if (velocity.y > 0f) velocity.y = 0f;
        }
    }

    bool TryEnsureEatTarget()
    {
        if (_eatTargetIsCA && IsValidCAEatTarget(_eatTargetCellX, _eatTargetCellY))
            return true;
        if (!_eatTargetIsCA && _eatTarget != null && IsValidEatTarget(_eatTarget))
            return true;

        _eatTarget = null;
        _eatTargetIsCA = false;
        _eatTargetCellX = _eatTargetCellY = -1;
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

        bool hasLegacy = best != null;
        int caX = -1;
        int caY = -1;
        float caD = float.PositiveInfinity;
        bool hasCA = canEatCARuntimePlants && TryFindBestCAEatTarget(range, out caX, out caY, out caD);

        if (!hasLegacy && !hasCA)
            return false;

        if (hasLegacy && (!hasCA || bestD <= caD))
        {
            _eatTarget = best;
            _eatTargetIsCA = false;
            _eatTargetCellX = _eatTargetCellY = -1;
            _eatHoldTimer = 0f;
            return true;
        }

        _eatTarget = null;
        _eatTargetIsCA = true;
        _eatTargetCellX = caX;
        _eatTargetCellY = caY;
        _eatHoldTimer = 0f;
        return true;
    }

    void UpdateEatProgress()
    {
        if (!TryEnsureEatTarget())
            return;

        if (_eatTargetIsCA)
        {
            if (!IsValidCAEatTarget(_eatTargetCellX, _eatTargetCellY))
            {
                _eatTargetIsCA = false;
                _eatTargetCellX = _eatTargetCellY = -1;
                _eatHoldTimer = 0f;
                return;
            }
        }
        else if (_eatTarget == null || !IsValidEatTarget(_eatTarget))
        {
            _eatTarget = null;
            _eatHoldTimer = 0f;
            return;
        }

        Vector3 headPos = GetHeadWorldPosition();
        Vector3 targetPos = GetCurrentEatTargetPoint();
        Vector2 headXZ = new Vector2(headPos.x, headPos.z);
        Vector2 targetXZ = new Vector2(targetPos.x, targetPos.z);
        float touchDist = Vector2.Distance(headXZ, targetXZ);
        float touchThreshold = Mathf.Max(0.05f, eatHeadTouchDistance);
        float settleThreshold = touchThreshold * 1.6f;

        if (touchDist <= settleThreshold)
        {
            // Arrival behavior: settle in place near flower head-point to avoid orbit/spin.
            velocity = Vector3.Lerp(velocity, Vector3.zero, 14f * Time.deltaTime);
            Vector3 look = targetPos - transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(look.normalized, Vector3.up), 12f * Time.deltaTime);
        }

        if (touchDist <= touchThreshold)
        {
            // Hold "eating" position for a short duration before applying result.
            _eatHoldTimer += Time.deltaTime;
            velocity = Vector3.Lerp(velocity, Vector3.zero, 10f * Time.deltaTime);

            if (_eatHoldTimer >= Mathf.Max(0.1f, eatHoldSeconds) && Time.time >= _nextEatAllowedAt)
            {
                if (_eatTargetIsCA)
                {
                    if (_runtime == null)
                        _runtime = FindFirstObjectByType<SCoLRuntime>();
                    if (_runtime != null)
                    {
                        if (eatAction == PlantEatAction.RemovePlant)
                            _runtime.TryDestroyPlantAtCell(_eatTargetCellX, _eatTargetCellY);
                        else
                            _runtime.TryResetPlantToSproutAtCell(_eatTargetCellX, _eatTargetCellY);
                    }
                }
                else
                {
                    if (eatAction == PlantEatAction.RemovePlant)
                        Destroy(_eatTarget.gameObject);
                    else
                        _eatTarget.ResetToSprout(clearBurn: true);
                }

                _nextEatAllowedAt = Time.time + Mathf.Max(0f, eatCooldownSeconds);
                _eatTarget = null;
                _eatTargetIsCA = false;
                _eatTargetCellX = _eatTargetCellY = -1;
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

    Vector3 GetCurrentEatTargetPoint()
    {
        if (_eatTargetIsCA)
            return GetCAEatTargetPoint(_eatTargetCellX, _eatTargetCellY);
        return GetEatTargetPoint(_eatTarget);
    }

    bool TryFindBestCAEatTarget(float range, out int bestX, out int bestY, out float bestDistSqr)
    {
        bestX = bestY = -1;
        bestDistSqr = float.PositiveInfinity;

        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null || _runtime.Grid == null)
            return false;

        if (!_runtime.TryWorldToCell(transform.position, out int cx, out int cy))
            return false;

        float cellSize = Mathf.Max(0.01f, _runtime.Grid.CellSize);
        int cellR = Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.1f, range) / cellSize));
        float rangeSqr = Mathf.Max(0.1f, range) * Mathf.Max(0.1f, range);

        Vector3 head = GetHeadWorldPosition();
        for (int y = cy - cellR; y <= cy + cellR; y++)
        for (int x = cx - cellR; x <= cx + cellR; x++)
        {
            if (!_runtime.Grid.InBounds(x, y))
                continue;

            var c = _runtime.Grid.Get(x, y);
            if (c == null || !c.HasPlant || c.PlantStage == PlantStage.Burnt || c.PlantStage < PlantStage.MediumTree)
                continue;

            Vector3 p = GetCAEatTargetPoint(x, y);
            float d = (p - head).sqrMagnitude;
            if (d > rangeSqr || d >= bestDistSqr)
                continue;

            bestDistSqr = d;
            bestX = x;
            bestY = y;
        }

        return bestX >= 0 && bestY >= 0;
    }

    bool IsValidCAEatTarget(int x, int y)
    {
        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null || _runtime.Grid == null || !_runtime.Grid.InBounds(x, y))
            return false;

        var c = _runtime.Grid.Get(x, y);
        if (c == null || !c.HasPlant || c.PlantStage == PlantStage.Burnt)
            return false;
        return c.PlantStage >= PlantStage.MediumTree;
    }

    Vector3 GetCAEatTargetPoint(int x, int y)
    {
        if (_runtime == null || _runtime.Grid == null || !_runtime.Grid.InBounds(x, y))
            return transform.position;

        Vector3 p = _runtime.Grid.CellCenterWorld(x, y);
        float yWorld = p.y + 0.45f;
        if (voxelWorld != null && voxelWorld.TryGetTerrainSurfaceYAtWorld(p, out float surfaceY, includeWaterSurface: false))
            yWorld = surfaceY + 0.55f;
        p.y = yWorld;
        return p;
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

        int surfaceY = voxelWorld.GetSurfaceY(x, z);
        if (surfaceY < voxelWorld.Config.seaLevel)
            return false;

        var surfaceType = voxelWorld.GetBlock(x, surfaceY, z);
        if (!IsLandSurfaceType(surfaceType))
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

        for (int r = 0; r <= maxR; r++)
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

    static bool IsLandSurfaceType(VoxelBlockType t)
    {
        return t == VoxelBlockType.Grass || t == VoxelBlockType.Dirt || t == VoxelBlockType.Stone;
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

    bool TryFindPlantDensityHotspot(out Vector3 hotspot, out int score)
    {
        hotspot = transform.position;
        score = 0;
        if (_runtime == null)
            _runtime = FindFirstObjectByType<SCoLRuntime>();
        if (_runtime == null)
            return false;

        bool found = _runtime.TryFindPlantDensityHotspot(
            transform.position,
            Mathf.Max(0.5f, plantDensitySearchRadius),
            Mathf.Max(0.5f, plantDensitySampleRadius),
            out hotspot,
            out score,
            lineageOnly: false);

        return found && score >= Mathf.Max(1, plantDensityMinScore);
    }

    bool TryFindRoleDensityHotspot(BoidRole wanted, float searchRadius, float sampleRadius, int minScore, out Vector3 hotspot, out int score)
    {
        hotspot = transform.position;
        score = 0;

        float searchRadiusSqr = Mathf.Max(0.5f, searchRadius);
        searchRadiusSqr *= searchRadiusSqr;
        float sampleRadiusSqr = Mathf.Max(0.5f, sampleRadius);
        sampleRadiusSqr *= sampleRadiusSqr;
        Vector3 myPos = transform.position;

        for (int i = 0; i < ActiveAgents.Count; i++)
        {
            var candidate = ActiveAgents[i];
            if (candidate == null || candidate == this || candidate.role != wanted)
                continue;

            Vector3 candidatePos = candidate.transform.position;
            Vector3 toCandidate = candidatePos - myPos;
            toCandidate.y = 0f;
            if (toCandidate.sqrMagnitude > searchRadiusSqr)
                continue;

            int candidateScore = 0;
            for (int j = 0; j < ActiveAgents.Count; j++)
            {
                var other = ActiveAgents[j];
                if (other == null || other.role != wanted)
                    continue;

                Vector3 toOther = other.transform.position - candidatePos;
                toOther.y = 0f;
                if (toOther.sqrMagnitude <= sampleRadiusSqr)
                    candidateScore++;
            }

            if (candidateScore <= score)
                continue;

            score = candidateScore;
            hotspot = candidatePos;
        }

        return score >= Mathf.Max(1, minScore);
    }

    void RememberEcologyHotspot(Vector3 hotspot)
    {
        _hasEcologyHotspot = true;
        _ecologyHotspot = hotspot;
        _ecologyHotspotUntil = Time.time + Mathf.Max(0f, ecologyHotspotHoldSeconds);
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
