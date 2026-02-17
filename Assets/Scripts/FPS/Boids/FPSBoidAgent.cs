using UnityEngine;

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

    [Header("Role")]
    public BoidRole role = BoidRole.Prey;

    [Header("Neighborhood")]
    public float neighborRadius = 3.5f;
    public float separationRadius = 1.0f;

    [Header("Forces")]
    public float separationWeight = 1.6f;
    public float alignmentWeight = 1.0f;
    public float cohesionWeight = 1.0f;

    [Header("Motion")]
    public float maxSpeed = 2.8f;
    public float maxForce = 6.0f;
    public float drag = 1.0f;

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

    [HideInInspector] public Vector3 velocity;

    Camera _playerCam;

    void Start()
    {
        // random initial velocity
        if (velocity.sqrMagnitude < 0.001f)
            velocity = Random.onUnitSphere * (maxSpeed * 0.5f);
        velocity.y = 0f;

        if (useBounds && boundsCenter == default)
            boundsCenter = transform.position;

        _playerCam = Camera.main;

        // predators are a bit faster by default
        if (role == BoidRole.Predator)
            maxSpeed *= 1.25f;
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

        transform.position += velocity * Time.deltaTime;

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
        var neighbors = Physics.OverlapSphere(transform.position, neighborRadius, ~0, QueryTriggerInteraction.Ignore);

        Vector3 separation = Vector3.zero;
        Vector3 alignment = Vector3.zero;
        Vector3 cohesionCenter = Vector3.zero;
        int count = 0;
        int sepCount = 0;

        foreach (var c in neighbors)
        {
            if (c == null) continue;
            var other = c.GetComponentInParent<FPSBoidAgent>();
            if (other == null || other == this) continue;

            float d = Vector3.Distance(transform.position, other.transform.position);
            if (d <= 0.0001f) continue;

            // Alignment + cohesion
            alignment += other.velocity;
            cohesionCenter += other.transform.position;
            count++;

            // Separation (stronger at close distances)
            if (d < separationRadius)
            {
                separation += (transform.position - other.transform.position) / (d * d);
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
        if (cam != null)
        {
            float dToPlayer = Vector3.Distance(transform.position, cam.transform.position);

            if (role == BoidRole.Prey)
            {
                if (dToPlayer < playerFleeDistance)
                {
                    var away = (transform.position - cam.transform.position);
                    away.y = 0f;
                    accel += SteerTowards(away) * playerFleeWeight;
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

        // clamp
        if (accel.magnitude > maxForce)
            accel = accel.normalized * maxForce;

        return accel;
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
        var cols = Physics.OverlapSphere(transform.position, radius, ~0, QueryTriggerInteraction.Ignore);
        Transform best = null;
        float bestD = float.PositiveInfinity;

        foreach (var c in cols)
        {
            if (c == null) continue;
            var other = c.GetComponentInParent<FPSBoidAgent>();
            if (other == null || other == this) continue;
            if (other.role != wanted) continue;

            float d = Vector3.Distance(transform.position, other.transform.position);
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
