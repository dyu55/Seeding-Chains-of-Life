using UnityEngine;
using UnityEngine.InputSystem;
using SCoL.Voxels;

/// <summary>
/// Minimal FPS controller for keyboard + mouse (no XR, no Input System dependency).
/// - WASD: move
/// - Mouse: look
/// - Space: jump
/// - Left Shift: sprint
/// </summary>
[DisallowMultipleComponent]
public class SimpleFirstPersonController : MonoBehaviour
{
    [Header("References")]
    public Transform cameraPivot; // usually the Main Camera transform

    [Header("Movement")]
    public float walkSpeed = 5f;
    public float sprintSpeed = 8f;
    public float jumpHeight = 1.2f;
    public float gravity = -18f;

    [Header("Look")]
    public float mouseSensitivity = 2.0f;
    public float maxPitch = 85f;

    [Header("Options")]
    public bool lockCursor = true;

    [Header("Underwater View")]
    public bool enableUnderwaterView = true;
    [Min(0f)] public float waterlinePadding = 0.05f;
    [Min(0f)] public float underwaterFogDensity = 0.055f;
    [Min(5f)] public float underwaterFarClip = 28f;
    public Color underwaterFogColor = new Color(0.18f, 0.46f, 0.62f, 1f);

    [Header("Fail-Safe Spawn Rescue")]
    [Min(0.5f)] public float rescueBelowWorldOffset = 4f;
    [Min(0f)] public float rescueHeightOffset = 1.25f;
    [Min(0.1f)] public float rescueCooldownSeconds = 1.0f;
    public int rescueColliderRadiusChunks = 2;

    CharacterController _cc;
    float _pitch;
    Vector3 _velocity;
    Camera _playerCamera;
    VoxelWorld _voxelWorld;

    bool _underwaterActive;
    bool _savedRenderState;
    bool _savedFog;
    FogMode _savedFogMode;
    Color _savedFogColor;
    float _savedFogDensity;
    float _savedFogStartDistance;
    float _savedFogEndDistance;
    float _savedFarClip;
    float _lastRescueTime = -999f;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (cameraPivot == null && Camera.main != null) cameraPivot = Camera.main.transform;
        _playerCamera = cameraPivot != null ? cameraPivot.GetComponent<Camera>() : Camera.main;
    }

    void OnEnable()
    {
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void Update()
    {
        if (_cc == null) return;

        var mouse = Mouse.current;
        var kb = Keyboard.current;
        bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;

        // Look (mouse delta)
        if (cameraPivot != null && mouse != null && cursorLocked)
        {
            var d = mouse.delta.ReadValue();
            float mx = d.x * mouseSensitivity * 0.02f; // scale down a bit vs legacy axes
            float my = d.y * mouseSensitivity * 0.02f;

            transform.Rotate(0f, mx, 0f, Space.Self);

            _pitch -= my;
            _pitch = Mathf.Clamp(_pitch, -maxPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Move (WASD / arrows)
        float x = 0f;
        float z = 0f;
        if (kb != null)
        {
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) x -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) x += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) z -= 1f;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) z += 1f;
        }

        Vector3 move = (transform.right * x + transform.forward * z);
        if (move.sqrMagnitude > 1f) move.Normalize();

        bool sprinting = kb != null && (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed);
        float speed = sprinting ? sprintSpeed : walkSpeed;
        _cc.Move(move * (speed * Time.deltaTime));

        // Ground / gravity
        bool grounded = _cc.isGrounded;
        if (grounded && _velocity.y < 0f)
            _velocity.y = -2f; // keep grounded

        // Jump
        if (grounded && kb != null && kb.spaceKey.wasPressedThisFrame)
        {
            // v = sqrt(h * -2g)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        _velocity.y += gravity * Time.deltaTime;
        _cc.Move(_velocity * Time.deltaTime);

        TryRescueIfFallenBelowWorld();

        // Escape to unlock cursor
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Re-lock on left click so players can quickly get back to controlling the camera.
        if (lockCursor && !cursorLocked && mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    void TryRescueIfFallenBelowWorld()
    {
        if (_voxelWorld == null)
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();
        if (_voxelWorld == null || _voxelWorld.Config == null)
            return;

        float worldMinY = _voxelWorld.OriginWorld.y - Mathf.Max(0.5f, rescueBelowWorldOffset);
        if (transform.position.y >= worldMinY)
            return;
        if (Time.unscaledTime < _lastRescueTime + Mathf.Max(0.1f, rescueCooldownSeconds))
            return;

        if (!TryFindRescuePoint(out var rescuePos))
            return;

        bool wasEnabled = _cc != null && _cc.enabled;
        if (_cc != null) _cc.enabled = false;
        transform.position = rescuePos;
        if (_cc != null) _cc.enabled = wasEnabled;
        _velocity = Vector3.zero;
        _lastRescueTime = Time.unscaledTime;

        _voxelWorld.ForceEnableChunksAtWorld(
            rescuePos,
            renderRadiusChunks: 1,
            colliderRadiusChunks: Mathf.Max(0, rescueColliderRadiusChunks));
    }

    bool TryFindRescuePoint(out Vector3 worldPos)
    {
        worldPos = transform.position;

        var cfg = _voxelWorld.Config;
        int width = cfg.worldWidth;
        int depth = cfg.worldDepth;
        if (width <= 0 || depth <= 0)
            return false;

        int cx = width / 2;
        int cz = depth / 2;
        int maxR = Mathf.Max(width, depth);

        for (int r = 0; r < maxR; r++)
        {
            int samples = r == 0 ? 1 : r * 8;
            for (int i = 0; i < samples; i++)
            {
                Vector2 dir = r == 0 ? Vector2.zero : DirectionOnCircle(i / (float)samples);
                int x = cx + Mathf.RoundToInt(dir.x * r);
                int z = cz + Mathf.RoundToInt(dir.y * r);
                if (x < 0 || z < 0 || x >= width || z >= depth)
                    continue;
                if (!_voxelWorld.IsGrassSurface(x, z))
                    continue;

                int y = _voxelWorld.GetSurfaceY(x, z);
                if (y < cfg.seaLevel)
                    continue;

                worldPos = _voxelWorld.OriginWorld + new Vector3(x + 0.5f, y + Mathf.Max(0f, rescueHeightOffset), z + 0.5f);
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

    void LateUpdate()
    {
        UpdateUnderwaterView();
    }

    void OnDisable()
    {
        RestoreRenderSettings();
    }

    void UpdateUnderwaterView()
    {
        if (!enableUnderwaterView)
        {
            RestoreRenderSettings();
            return;
        }

        if (_voxelWorld == null)
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();

        if (_voxelWorld == null || _voxelWorld.Config == null)
        {
            RestoreRenderSettings();
            return;
        }

        if (_playerCamera == null)
            _playerCamera = cameraPivot != null ? cameraPivot.GetComponent<Camera>() : Camera.main;

        Vector3 samplePos = cameraPivot != null ? cameraPivot.position : transform.position + Vector3.up * 1.6f;
        bool isUnderwater = IsUnderwater(samplePos);

        if (isUnderwater)
            ApplyUnderwaterState();
        else
            RestoreRenderSettings();
    }

    bool IsUnderwater(Vector3 worldPos)
    {
        if (!_voxelWorld.TryWorldToColumn(worldPos, out int x, out int z))
            return false;

        int localY = Mathf.Clamp(
            Mathf.FloorToInt(worldPos.y - _voxelWorld.OriginWorld.y),
            0,
            _voxelWorld.Config.worldHeight - 1);

        if (_voxelWorld.GetBlock(x, localY, z) == VoxelBlockType.Water)
            return true;

        int sea = _voxelWorld.Config.seaLevel;
        if (_voxelWorld.GetBlock(x, sea, z) != VoxelBlockType.Water)
            return false;

        float seaSurfaceY = _voxelWorld.OriginWorld.y + sea + 1f - Mathf.Max(0f, waterlinePadding);
        return worldPos.y < seaSurfaceY;
    }

    void ApplyUnderwaterState()
    {
        if (!_savedRenderState)
        {
            _savedFog = RenderSettings.fog;
            _savedFogMode = RenderSettings.fogMode;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;
            _savedFogStartDistance = RenderSettings.fogStartDistance;
            _savedFogEndDistance = RenderSettings.fogEndDistance;
            _savedFarClip = _playerCamera != null ? _playerCamera.farClipPlane : 1000f;
            _savedRenderState = true;
        }

        _underwaterActive = true;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogColor = underwaterFogColor;
        RenderSettings.fogDensity = Mathf.Max(0f, underwaterFogDensity);

        if (_playerCamera != null)
            _playerCamera.farClipPlane = Mathf.Min(_savedFarClip, Mathf.Max(5f, underwaterFarClip));
    }

    void RestoreRenderSettings()
    {
        if (!_underwaterActive || !_savedRenderState)
            return;

        _underwaterActive = false;

        RenderSettings.fog = _savedFog;
        RenderSettings.fogMode = _savedFogMode;
        RenderSettings.fogColor = _savedFogColor;
        RenderSettings.fogDensity = _savedFogDensity;
        RenderSettings.fogStartDistance = _savedFogStartDistance;
        RenderSettings.fogEndDistance = _savedFogEndDistance;

        if (_playerCamera != null)
            _playerCamera.farClipPlane = _savedFarClip;

        _savedRenderState = false;
    }
}
