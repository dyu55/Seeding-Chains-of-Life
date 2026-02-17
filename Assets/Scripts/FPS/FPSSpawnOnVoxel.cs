using System.Collections;
using UnityEngine;

/// <summary>
/// Ensures FPS player starts on top of voxel terrain instead of fallback platforms.
/// </summary>
[DisallowMultipleComponent]
public class FPSSpawnOnVoxel : MonoBehaviour
{
    [Header("Search")]
    public float castHeight = 80f;
    public float castDistance = 200f;
    public float searchStep = 2f;
    public int searchRings = 6;
    public LayerMask hitMask = ~0;

    [Header("Placement")]
    public float spawnOffsetY = 0.15f;
    public float retrySeconds = 2f;

    CharacterController _cc;

    IEnumerator Start()
    {
        _cc = GetComponent<CharacterController>();

        // Wait one frame so runtime-generated voxel chunks/colliders can appear.
        yield return null;

        float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, retrySeconds);
        while (Time.realtimeSinceStartup <= deadline)
        {
            if (TryFindVoxelSpawnPoint(out var point))
            {
                Teleport(point + Vector3.up * spawnOffsetY);
                yield break;
            }
            yield return null;
        }
    }

    bool TryFindVoxelSpawnPoint(out Vector3 point)
    {
        point = default;

        Vector3 center = transform.position;
        var runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
        if (runtime != null)
            center = runtime.transform.position;

        // Search from center outward in rings so spawn is deterministic and close to gameplay area.
        for (int ring = 0; ring <= Mathf.Max(0, searchRings); ring++)
        {
            int samples = ring == 0 ? 1 : ring * 8;
            for (int i = 0; i < samples; i++)
            {
                Vector2 offset = ring == 0
                    ? Vector2.zero
                    : DirectionOnCircle(i / (float)samples) * (ring * searchStep);

                Vector3 origin = new Vector3(center.x + offset.x, castHeight, center.z + offset.y);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, castDistance, hitMask, QueryTriggerInteraction.Ignore))
                    continue;

                if (!IsVoxelHit(hit.collider))
                    continue;

                point = hit.point;
                return true;
            }
        }

        return false;
    }

    static Vector2 DirectionOnCircle(float t)
    {
        float a = t * Mathf.PI * 2f;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a));
    }

    static bool IsVoxelHit(Collider c)
    {
        if (c == null) return false;

        for (Transform t = c.transform; t != null; t = t.parent)
        {
            if (t.name.StartsWith("Chunk_"))
                return true;
            if (t.GetComponent<SCoL.Voxels.VoxelWorld>() != null)
                return true;
        }

        return false;
    }

    void Teleport(Vector3 worldPos)
    {
        if (_cc != null)
        {
            bool wasEnabled = _cc.enabled;
            _cc.enabled = false;
            transform.position = worldPos;
            _cc.enabled = wasEnabled;
            return;
        }

        transform.position = worldPos;
    }
}

