using UnityEngine;

/// <summary>
/// Voxelize/assimilate an object: snap localScale to 0.5 increments and replace materials
/// with a simple voxel-looking material (runtime-created).
/// </summary>
public static class VoxelAssimilator
{
    const float Step = 0.5f;

    static Material _voxelMat;

    static Material GetVoxelMaterial()
    {
        if (_voxelMat != null) return _voxelMat;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");

        _voxelMat = new Material(shader);
        _voxelMat.name = "VoxelAssimilated (Runtime)";

        // Best-effort tint.
        if (_voxelMat.HasProperty("_BaseColor")) _voxelMat.SetColor("_BaseColor", new Color(0.65f, 0.85f, 0.65f, 1f));
        if (_voxelMat.HasProperty("_Color")) _voxelMat.SetColor("_Color", new Color(0.65f, 0.85f, 0.65f, 1f));
        if (_voxelMat.HasProperty("_Smoothness")) _voxelMat.SetFloat("_Smoothness", 0.05f);
        if (_voxelMat.HasProperty("_Metallic")) _voxelMat.SetFloat("_Metallic", 0.0f);

        return _voxelMat;
    }

    public static void Assimilate(GameObject target)
    {
        if (target == null) return;

        if (target.GetComponent<VoxelAssimilatedMarker>() != null)
            return;

        // 1) Snap localScale to 0.5 increments.
        var s = target.transform.localScale;
        s.x = Snap(s.x);
        s.y = Snap(s.y);
        s.z = Snap(s.z);
        target.transform.localScale = s;

        // 2) Replace materials on all renderers under this object.
        var mat = GetVoxelMaterial();
        var renderers = target.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var r in renderers)
        {
            if (r == null) continue;
            r.sharedMaterial = mat;
        }

        target.AddComponent<VoxelAssimilatedMarker>();
    }

    static float Snap(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) return Step;
        float snapped = Mathf.Round(v / Step) * Step;
        if (Mathf.Abs(snapped) < Step) snapped = Step * Mathf.Sign(v == 0 ? 1 : v);
        return snapped;
    }
}

/// <summary>Marker to prevent re-assimilating the same object repeatedly.</summary>
public sealed class VoxelAssimilatedMarker : MonoBehaviour { }
