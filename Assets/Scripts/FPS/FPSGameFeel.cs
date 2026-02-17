using UnityEngine;

/// <summary>
/// Lightweight FPS "game feel" helpers:
/// - Spawn a voxel-like burst (cube particles)
/// - Trigger small camera shake
/// 
/// Runtime-only: no scene setup required.
/// </summary>
public static class FPSGameFeel
{
    static FPSCameraShake _shake;

    public static void VoxelBurst(Vector3 position, int count = 18, float spread = 1.2f, float life = 0.9f, float cubeSize = 0.06f)
    {
        var root = new GameObject("VoxelBurst (Runtime)");
        root.transform.position = position;

        for (int i = 0; i < count; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Voxel";
            cube.transform.SetParent(root.transform, worldPositionStays: true);

            cube.transform.position = position + Random.insideUnitSphere * 0.08f;
            cube.transform.rotation = Random.rotation;
            cube.transform.localScale = Vector3.one * cubeSize * Random.Range(0.7f, 1.2f);

            // Physics
            var rb = cube.AddComponent<Rigidbody>();
            rb.mass = 0.02f;
            rb.linearDamping = 0.2f;
            rb.angularDamping = 0.05f;

            // Random impulse (biased forward/up)
            var dir = (Random.onUnitSphere + Vector3.up * 0.8f).normalized;
            rb.AddForce(dir * Random.Range(0.7f, 1.2f) * spread, ForceMode.Impulse);
            rb.AddTorque(Random.insideUnitSphere * 0.3f, ForceMode.Impulse);

            // Make it not block raycasts after burst
            Object.Destroy(cube.GetComponent<Collider>());

            Object.Destroy(cube, life);
        }

        Object.Destroy(root, life + 0.1f);
    }

    public static void Shake(float amplitude = 0.06f, float duration = 0.12f)
    {
        EnsureShakeExists();
        if (_shake != null)
            _shake.AddShake(amplitude, duration);
    }

    static void EnsureShakeExists()
    {
        if (_shake != null) return;

        var cam = Camera.main;
        if (cam == null) return;

        _shake = cam.GetComponent<FPSCameraShake>();
        if (_shake == null)
            _shake = cam.gameObject.AddComponent<FPSCameraShake>();
    }
}

/// <summary>
/// Simple camera shake by applying a per-frame local position offset.
/// </summary>
[DisallowMultipleComponent]
public class FPSCameraShake : MonoBehaviour
{
    float _timeLeft;
    float _amplitude;
    Vector3 _baseLocalPos;

    void Awake()
    {
        _baseLocalPos = transform.localPosition;
    }

    void OnEnable()
    {
        _baseLocalPos = transform.localPosition;
    }

    public void AddShake(float amplitude, float duration)
    {
        _amplitude = Mathf.Max(_amplitude, amplitude);
        _timeLeft = Mathf.Max(_timeLeft, duration);
    }

    void LateUpdate()
    {
        if (_timeLeft <= 0f)
        {
            transform.localPosition = _baseLocalPos;
            return;
        }

        _timeLeft -= Time.deltaTime;

        // small random jitter
        var offset = Random.insideUnitSphere * _amplitude;
        offset.z = 0f; // keep depth stable
        transform.localPosition = _baseLocalPos + offset;

        // decay
        _amplitude = Mathf.Lerp(_amplitude, 0f, 12f * Time.deltaTime);
    }
}
