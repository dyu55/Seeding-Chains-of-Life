using UnityEngine;

/// <summary>
/// T07: Seeding loop for FPS.
/// On request (RMB), consume a Seed from SCoLInventory and spawn a simple voxel plant or animal
/// at the raycast hit point.
/// 
/// Runtime-only: spawns primitive cubes with a basic voxel material.
/// </summary>
public static class FPSSeeding
{
    static Material _voxelMat;

    static Material GetVoxelMat()
    {
        if (_voxelMat != null) return _voxelMat;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");

        _voxelMat = new Material(shader);
        _voxelMat.name = "VoxelSeedSpawn (Runtime)";
        if (_voxelMat.HasProperty("_BaseColor")) _voxelMat.SetColor("_BaseColor", new Color(0.55f, 0.9f, 0.55f, 1f));
        if (_voxelMat.HasProperty("_Color")) _voxelMat.SetColor("_Color", new Color(0.55f, 0.9f, 0.55f, 1f));
        if (_voxelMat.HasProperty("_Smoothness")) _voxelMat.SetFloat("_Smoothness", 0.02f);
        if (_voxelMat.HasProperty("_Metallic")) _voxelMat.SetFloat("_Metallic", 0.0f);
        return _voxelMat;
    }

    public static GameObject SpawnFromSeed(Vector3 position, Quaternion rotation)
    {
        // 50/50 plant vs animal
        if (Random.value < 0.5f) return SpawnPlant(position, rotation);
        return SpawnAnimal(position, rotation);
    }

    public static GameObject SpawnPlant(Vector3 position, Quaternion rotation)
    {
        var root = new GameObject("VoxelPlant (Seed)");
        root.transform.SetPositionAndRotation(position, rotation);

        var mat = GetVoxelMat();

        // Stem
        int stemH = Random.Range(2, 5);
        for (int i = 0; i < stemH; i++)
            SpawnCube(root.transform, new Vector3(0, 0.1f + i * 0.18f, 0), Vector3.one * 0.16f, mat);

        // Leaves
        int leaves = Random.Range(3, 6);
        for (int i = 0; i < leaves; i++)
        {
            var p = new Vector3(Random.Range(-0.22f, 0.22f), 0.1f + (stemH - 1) * 0.18f + Random.Range(-0.05f, 0.15f), Random.Range(-0.22f, 0.22f));
            var s = Vector3.one * Random.Range(0.14f, 0.18f);
            SpawnCube(root.transform, p, s, mat);
        }

        return root;
    }

    public static GameObject SpawnAnimal(Vector3 position, Quaternion rotation)
    {
        var root = new GameObject("VoxelAnimal (Seed)");
        root.transform.SetPositionAndRotation(position, rotation);

        var mat = GetVoxelMat();

        // Body
        SpawnCube(root.transform, new Vector3(0, 0.14f, 0), new Vector3(0.28f, 0.18f, 0.18f), mat);
        // Head
        SpawnCube(root.transform, new Vector3(0.22f, 0.18f, 0), new Vector3(0.16f, 0.14f, 0.14f), mat);
        // Legs
        SpawnCube(root.transform, new Vector3(-0.09f, 0.03f, -0.06f), new Vector3(0.06f, 0.06f, 0.06f), mat);
        SpawnCube(root.transform, new Vector3(-0.09f, 0.03f, 0.06f), new Vector3(0.06f, 0.06f, 0.06f), mat);
        SpawnCube(root.transform, new Vector3(0.09f, 0.03f, -0.06f), new Vector3(0.06f, 0.06f, 0.06f), mat);
        SpawnCube(root.transform, new Vector3(0.09f, 0.03f, 0.06f), new Vector3(0.06f, 0.06f, 0.06f), mat);

        // Boids agent (core flocking + predator/prey)
        var boid = root.AddComponent<FPSBoidAgent>();
        boid.role = (Random.value < 0.18f) ? FPSBoidAgent.BoidRole.Predator : FPSBoidAgent.BoidRole.Prey;

        // Tint predators slightly red for readability
        if (boid.role == FPSBoidAgent.BoidRole.Predator)
            TintAll(root, new Color(0.85f, 0.35f, 0.35f, 1f));

        return root;
    }

    static void SpawnCube(Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = localPos;
        cube.transform.localRotation = Quaternion.identity;
        cube.transform.localScale = localScale;

        if (cube.TryGetComponent<Renderer>(out var r))
            r.sharedMaterial = mat;

        // no collision for visuals; keep root collision decisions separate
        Object.Destroy(cube.GetComponent<Collider>());
    }

    static void TintAll(GameObject root, Color c)
    {
        var rs = root.GetComponentsInChildren<Renderer>(includeInactive: true);
        foreach (var r in rs)
        {
            if (r == null) continue;
            var m = r.sharedMaterial;
            if (m == null) continue;
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
        }
    }
}

public sealed class FPSSeedAnimalIdle : MonoBehaviour
{
    Vector3 _basePos;
    float _t;

    void Start() => _basePos = transform.position;

    void Update()
    {
        _t += Time.deltaTime;
        transform.position = _basePos + new Vector3(0, Mathf.Sin(_t * 3.5f) * 0.03f, 0);
        transform.Rotate(0f, Mathf.Sin(_t * 1.3f) * 20f * Time.deltaTime, 0f);
    }
}
