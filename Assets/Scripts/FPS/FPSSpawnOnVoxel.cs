using System.Collections;
using UnityEngine;
using SCoL.Voxels;

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
    public int forceEnableRenderRadiusChunks = 1;
    public int forceEnableColliderRadiusChunks = 2;

    CharacterController _cc;
    VoxelWorld _voxelWorld;

    IEnumerator Start()
    {
        _cc = GetComponent<CharacterController>();
        _voxelWorld = FindFirstObjectByType<VoxelWorld>();

        // Wait one frame so runtime-generated voxel chunks/colliders can appear.
        yield return null;

        float deadline = Time.realtimeSinceStartup + Mathf.Max(0.1f, retrySeconds);
        while (Time.realtimeSinceStartup <= deadline)
        {
            if (TryFindVoxelSpawnPoint(out var point))
            {
                if (_voxelWorld != null)
                {
                    // Ensure chunks/colliders are active before teleporting the controller.
                    _voxelWorld.ForceEnableChunksAtWorld(
                        point,
                        renderRadiusChunks: Mathf.Max(0, forceEnableRenderRadiusChunks),
                        colliderRadiusChunks: Mathf.Max(0, forceEnableColliderRadiusChunks));
                }

                if (TryProjectToColliderSurface(point, out var groundedPoint))
                    point = groundedPoint;

                var snapped = point + Vector3.up * spawnOffsetY;
                Teleport(snapped);
                ForceEnableNearbyVoxelChunks(snapped);
                yield break;
            }
            yield return null;
        }

        // Last-resort fallback: snap to voxel center column so we never keep free-falling from authoring position.
        if (_voxelWorld != null && _voxelWorld.Config != null)
        {
            int x = Mathf.Clamp(_voxelWorld.Config.worldWidth / 2, 0, _voxelWorld.Config.worldWidth - 1);
            int z = Mathf.Clamp(_voxelWorld.Config.worldDepth / 2, 0, _voxelWorld.Config.worldDepth - 1);
            int y = _voxelWorld.GetSurfaceY(x, z);
            Vector3 fallback = _voxelWorld.OriginWorld + new Vector3(x + 0.5f, y + 1f, z + 0.5f);
            _voxelWorld.ForceEnableChunksAtWorld(fallback, renderRadiusChunks: 2, colliderRadiusChunks: 2);
            Teleport(fallback + Vector3.up * spawnOffsetY);
        }
    }

    bool TryFindVoxelSpawnPoint(out Vector3 point)
    {
        point = default;

        var voxelWorld = _voxelWorld != null ? _voxelWorld : FindFirstObjectByType<VoxelWorld>();
        _voxelWorld = voxelWorld;
        if (voxelWorld != null && voxelWorld.Config != null)
            return TryFindDryLandSpawnPoint(voxelWorld, out point);

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

    bool TryProjectToColliderSurface(Vector3 nearPoint, out Vector3 point)
    {
        point = nearPoint;
        Vector3 origin = nearPoint + Vector3.up * Mathf.Max(2f, castHeight * 0.05f);
        float dist = Mathf.Max(6f, castDistance * 0.25f);
        if (!Physics.Raycast(origin, Vector3.down, out var hit, dist, hitMask, QueryTriggerInteraction.Ignore))
            return false;
        if (!IsVoxelHit(hit.collider))
            return false;

        point = hit.point;
        return true;
    }

    bool TryFindDryLandSpawnPoint(VoxelWorld voxelWorld, out Vector3 point)
    {
        point = default;
        var cfg = voxelWorld.Config;
        int width = cfg.worldWidth;
        int depth = cfg.worldDepth;
        if (width <= 0 || depth <= 0) return false;

        int step = Mathf.Max(1, Mathf.RoundToInt(searchStep));
        // Hard rule: start searching from map center so spawn remains stable after world-size changes.
        int cx = width / 2;
        int cz = depth / 2;

        int maxRingByConfig = Mathf.CeilToInt(Mathf.Max(width, depth) / (2f * step));
        int maxRing = Mathf.Max(Mathf.Max(0, searchRings), maxRingByConfig);

        for (int ring = 0; ring <= maxRing; ring++)
        {
            int radius = ring * step;
            int samples = ring == 0 ? 1 : ring * 8;
            for (int i = 0; i < samples; i++)
            {
                Vector2 dir = ring == 0 ? Vector2.zero : DirectionOnCircle(i / (float)samples);
                int x = cx + Mathf.RoundToInt(dir.x * radius);
                int z = cz + Mathf.RoundToInt(dir.y * radius);
                if (!IsDryLandColumn(voxelWorld, x, z))
                    continue;

                int y = voxelWorld.GetSurfaceY(x, z);
                point = voxelWorld.OriginWorld + new Vector3(x + 0.5f, y + 1f, z + 0.5f);
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

    static bool IsDryLandColumn(VoxelWorld voxelWorld, int x, int z)
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

    void ForceEnableNearbyVoxelChunks(Vector3 worldPos)
    {
        var world = FindFirstObjectByType<VoxelWorld>();
        if (world == null)
            return;

        world.ForceEnableChunksAtWorld(
            worldPos,
            renderRadiusChunks: Mathf.Max(0, forceEnableRenderRadiusChunks),
            colliderRadiusChunks: Mathf.Max(0, forceEnableColliderRadiusChunks));
    }
}
