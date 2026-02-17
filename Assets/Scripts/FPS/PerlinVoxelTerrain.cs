using UnityEngine;

/// <summary>
/// T10: Procedural Perlin voxel terrain for testing.
/// Generates a 50x50 grid of cube blocks around/under the player at scene start.
/// Runtime-only (no prefabs required).
/// </summary>
[DisallowMultipleComponent]
public class PerlinVoxelTerrain : MonoBehaviour
{
    [Header("Size")]
    public int sizeX = 50;
    public int sizeZ = 50;
    public float blockSize = 1f;

    [Header("Height")]
    public float noiseScale = 0.12f;
    public int minHeight = 0;
    public int maxHeight = 6;

    [Header("Placement")]
    public Transform center;
    public bool generateOnce = true;

    [Header("Material")]
    public Color groundColor = new Color(0.45f, 0.75f, 0.45f, 1f);

    GameObject _root;
    Material _mat;
    bool _generated;

    void Start()
    {
        if (center == null)
        {
            var fpc = FindFirstObjectByType<SimpleFirstPersonController>();
            if (fpc != null) center = fpc.transform;
        }

        if (center == null && Camera.main != null)
            center = Camera.main.transform;

        Generate();
    }

    public void Generate()
    {
        if (generateOnce && _generated) return;
        _generated = true;

        if (_root != null) Destroy(_root);
        _root = new GameObject("PerlinVoxelTerrain (Runtime)");

        _mat = BuildMat();

        Vector3 c = center != null ? center.position : Vector3.zero;
        float halfX = (sizeX - 1) * 0.5f;
        float halfZ = (sizeZ - 1) * 0.5f;

        // Seed the noise with world position so moving start position changes the pattern slightly.
        float seedX = c.x * 0.01f + Random.Range(-1000f, 1000f);
        float seedZ = c.z * 0.01f + Random.Range(-1000f, 1000f);

        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                float nx = (x + seedX) * noiseScale;
                float nz = (z + seedZ) * noiseScale;
                float n = Mathf.PerlinNoise(nx, nz);
                int h = Mathf.RoundToInt(Mathf.Lerp(minHeight, maxHeight, n));

                // Build a column up to h (inclusive), so terrain has volume.
                for (int y = 0; y <= h; y++)
                {
                    Vector3 pos = new Vector3((x - halfX) * blockSize, y * blockSize, (z - halfZ) * blockSize);
                    pos += new Vector3(Mathf.Floor(c.x / blockSize) * blockSize, 0f, Mathf.Floor(c.z / blockSize) * blockSize);
                    SpawnBlock(pos);
                }
            }
        }

        // Add a big ground collider (optional) - columns already have colliders.
    }

    void SpawnBlock(Vector3 worldPos)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.transform.SetParent(_root.transform, worldPositionStays: true);
        cube.transform.position = worldPos;
        cube.transform.localScale = Vector3.one * blockSize;

        if (cube.TryGetComponent<Renderer>(out var r))
            r.sharedMaterial = _mat;

        cube.isStatic = true;
    }

    Material BuildMat()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Diffuse");

        var m = new Material(shader);
        m.name = "PerlinVoxelGround (Runtime)";
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", groundColor);
        if (m.HasProperty("_Color")) m.SetColor("_Color", groundColor);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.05f);
        if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0.0f);
        return m;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void EnsureExists()
    {
        if (FindFirstObjectByType<PerlinVoxelTerrain>() != null) return;
        var go = new GameObject("PerlinVoxelTerrainGenerator (Runtime)");
        DontDestroyOnLoad(go);
        go.AddComponent<PerlinVoxelTerrain>();
    }
}
