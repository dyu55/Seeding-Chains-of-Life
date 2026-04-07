using UnityEngine;
using SCoL.Voxels;
using SCoL.Visualization;
using SCoL.Weather;
using SCoL.Interaction;
using SCoL.Settlement;

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

    [Header("Season Movement")]
    [Range(0.2f, 1f)] public float winterMoveMultiplier = 0.65f;

    [Header("Plant Stomp")]
    public bool destroyPlantWhenSteppedOn = true;
    [Min(0.05f)] public float stompCheckIntervalSeconds = 0.1f;
    [Min(0.02f)] public float stompProbeRadius = 0.16f;
    [Min(0.05f)] public float stompProbeDistance = 0.55f;
    public LayerMask stompMask = ~0;

    [Header("Options")]
    public bool lockCursor = true;

    [Header("Underwater View")]
    public bool enableUnderwaterView = true;
    [Min(0f)] public float waterlinePadding = 0.05f;
    [Min(0f)] public float underwaterFogDensity = 0.095f;
    [Min(5f)] public float underwaterFarClip = 14f;
    public Color underwaterFogColor = new Color(0.18f, 0.46f, 0.62f, 1f);

    [Header("Fail-Safe Spawn Rescue")]
    [Min(0.5f)] public float rescueBelowWorldOffset = 4f;
    [Min(0f)] public float rescueHeightOffset = 1.25f;
    [Min(0.1f)] public float rescueCooldownSeconds = 1.0f;
    public int rescueColliderRadiusChunks = 2;

    [Header("Footstep Audio")]
    [Tooltip("AudioSource used to play one-off footstep sound.")]
        public AudioSource footstepAudioSource;

    [Tooltip("Default footstep clip")]
    public AudioClip footstepClip;

    [Tooltip("Snow footstep clip")]
    public AudioClip footstepClipSnow;

    [Tooltip("Underwater footstep clip")]
    public AudioClip footstepClipUnderwater;

    [Range(0f, 1f)] public float footstepVolume = 1f;
    [Min(0f)] public float underwaterFootstepVolumeMultiplier = 2f;

    public float stepRate = 0.25f;
	public float stepCoolDown;
    [Min(0f)] public float footstepMoveThreshold = 0.01f;

    CharacterController _cc;
    float _pitch;
    Vector3 _velocity;
    Camera _playerCamera;
    VoxelWorld _voxelWorld;
    SeasonSkyboxController _seasonSkybox;
    WeatherSystem _weatherSystem;
    SCoL.SCoLRuntime _runtime;
    SCoLSettlementManager _settlementManager;
    float _nextSeasonLookupAt;

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
    float _nextStompCheckAt;
    float _moveInputSqrMagnitude;
    Vector3 _lastFootstepPosition;
    bool _hasLastFootstepPosition;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (cameraPivot == null && Camera.main != null) cameraPivot = Camera.main.transform;
        _playerCamera = cameraPivot != null ? cameraPivot.GetComponent<Camera>() : Camera.main;
        if (footstepAudioSource == null)
            footstepAudioSource = GetComponentInChildren<AudioSource>(includeInactive: true);
        if (footstepAudioSource != null)
        {
            footstepAudioSource.playOnAwake = false;
            footstepAudioSource.loop = false;
            footstepAudioSource.spatialBlend = 0f;
        }
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
        if (_settlementManager == null)
            _settlementManager = FindFirstObjectByType<SCoLSettlementManager>();

        if (_settlementManager != null && _settlementManager.IsStorageUiOpen)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;

            if (SCoLInteractionInput.PausePressed())
                _settlementManager.CloseStorageUi(relockCursor: lockCursor);

            _velocity = Vector3.zero;
            return;
        }

        bool cursorLocked = Cursor.lockState == CursorLockMode.Locked;

        // Look / turn from unified input (FPS today, VR later).
        if (cameraPivot != null && cursorLocked)
        {
            var d = SCoLInteractionInput.LookDelta();
            float mxFromLook = d.x * mouseSensitivity * 0.02f;
            float my = d.y * mouseSensitivity * 0.02f;
            float yawDelta = mxFromLook + SCoLInteractionInput.TurnDegreesThisFrame();

            transform.Rotate(0f, yawDelta, 0f, Space.Self);

            _pitch -= my;
            _pitch = Mathf.Clamp(_pitch, -maxPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Move
        Vector2 moveInput = SCoLInteractionInput.Move();
        float x = moveInput.x;
        float z = moveInput.y;

        Vector3 move = (transform.right * x + transform.forward * z);
        if (move.sqrMagnitude > 1f) move.Normalize();
        _moveInputSqrMagnitude = new Vector2(x, z).sqrMagnitude;

        bool sprinting = SCoLInteractionInput.SprintHeld();
        float speed = sprinting ? sprintSpeed : walkSpeed;
        if (IsWinterActive())
            speed *= Mathf.Clamp(winterMoveMultiplier, 0.2f, 1f);
        _cc.Move(move * (speed * Time.deltaTime));
        TryStepOntoFrozenWater();

        // Ground / gravity
        bool grounded = _cc.isGrounded;
        if (grounded && _velocity.y < 0f)
            _velocity.y = -2f; // keep grounded

        // Jump
        if (grounded && SCoLInteractionInput.JumpPressed())
        {
            // v = sqrt(h * -2g)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        _velocity.y += gravity * Time.deltaTime;
        _cc.Move(_velocity * Time.deltaTime);
        TryStompPlantUnderfoot(grounded);

        TryRescueIfFallenBelowWorld();

        // Escape to unlock cursor
        if (SCoLInteractionInput.PausePressed())
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        // Re-lock on left click so players can quickly get back to controlling the camera.
        if (lockCursor && !cursorLocked && SCoLInteractionInput.PrimaryPressed())
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        applyFootstepAudio();
    }

    bool IsWinterActive()
    {
        if (Time.time >= _nextSeasonLookupAt)
        {
            if (_seasonSkybox == null || !_seasonSkybox.isActiveAndEnabled)
                _seasonSkybox = FindFirstObjectByType<SeasonSkyboxController>();
            if (_weatherSystem == null || !_weatherSystem.isActiveAndEnabled)
                _weatherSystem = FindFirstObjectByType<WeatherSystem>();
            if (_runtime == null || !_runtime.isActiveAndEnabled)
                _runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
            _nextSeasonLookupAt = Time.time + 1f;
        }

        if (_seasonSkybox != null && _seasonSkybox.GetCurrentSeason() == SeasonSkyboxController.Season.Winter)
            return true;
        if (_weatherSystem != null && _weatherSystem.CurrentPhase == WeatherPhase.Snow)
            return true;
        if (_runtime != null && _runtime.CurrentSeason == SCoL.Season.Winter)
            return true;

        return false;
    }

    void TryStompPlantUnderfoot(bool grounded)
    {
        if (!destroyPlantWhenSteppedOn || !grounded)
            return;
        if (Time.time < _nextStompCheckAt)
            return;
        _nextStompCheckAt = Time.time + Mathf.Max(0.05f, stompCheckIntervalSeconds);

        var runtime = FindFirstObjectByType<SCoL.SCoLRuntime>();
        if (runtime != null)
        {
            Vector3 stompPoint = transform.position + Vector3.down * 0.25f;
            int removed = runtime.TryStompPlantAroundWorld(stompPoint, radius: 1.0f, maxPlants: 2, hitsRequired: 3);
            if (removed > 0)
            {
                SCoL.Visualization.DayNightLightingController.PlayInteractionSfx(SCoL.Visualization.DayNightLightingController.InteractionSfx.DestroySeed);
                Debug.Log($"[SimpleFirstPersonController] Stomp removed runtime plants: {removed}");
                return;
            }
        }

        float radius = Mathf.Max(0.02f, stompProbeRadius);
        if (_cc != null)
            radius = Mathf.Max(radius, _cc.radius * 0.85f);
        radius = Mathf.Max(radius, 0.45f);

        var nearby = new System.Collections.Generic.List<FPSSeedGrowth>(8);
        FPSSeedGrowth.CollectNearby(transform.position, Mathf.Max(radius, stompProbeDistance + 0.6f), nearby);
        if (nearby.Count == 0)
            return;

        float footY = _cc != null ? _cc.bounds.min.y : transform.position.y;
        for (int i = 0; i < nearby.Count; i++)
        {
            var g = nearby[i];
            if (g == null) continue;
            if (!g.TryGetPlantBounds(out var b)) continue;

            Vector2 d = new Vector2(transform.position.x - b.center.x, transform.position.z - b.center.z);
            float horizontalLimit = Mathf.Max(radius, Mathf.Max(b.extents.x, b.extents.z) + 0.05f);
            if (d.sqrMagnitude > horizontalLimit * horizontalLimit)
                continue;

            float top = b.max.y;
            if (footY < top - 0.10f || footY > top + Mathf.Max(0.2f, stompProbeDistance))
                continue;

            SCoL.Visualization.DayNightLightingController.PlayInteractionSfx(SCoL.Visualization.DayNightLightingController.InteractionSfx.DestroySeed);
            Destroy(g.gameObject);
            Debug.Log("[SimpleFirstPersonController] Stomp removed FPSSeedGrowth plant.");
            break;
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

    void applyFootstepAudio()
    {
        if (footstepAudioSource == null)
            return;

        footstepAudioSource.spatialBlend = 0f; // 2D
        footstepAudioSource.volume = Mathf.Clamp01(footstepVolume) * 4f;

        Vector3 samplePos = GetFootstepSamplePosition();
        Vector3 planarSample = new Vector3(transform.position.x, 0f, transform.position.z);

        stepCoolDown -= Time.deltaTime;

        bool hasMoveInput = _moveInputSqrMagnitude > Mathf.Max(0f, footstepMoveThreshold);
        bool grounded = _cc != null && _cc.isGrounded;
        if (!hasMoveInput || !grounded)
        {
            _hasLastFootstepPosition = false;
            return;
        }

        if (!_hasLastFootstepPosition)
        {
            _lastFootstepPosition = planarSample;
            _hasLastFootstepPosition = true;
        }

        float movedSinceLastStep = Vector3.Distance(planarSample, _lastFootstepPosition);
        if (stepCoolDown >= 0f || movedSinceLastStep < 0.08f)
            return;

        AudioClip clip = footstepClip;
        float clipVolumeScale = footstepAudioSource.volume;
        if (IsFootInWater(samplePos))
        {
            clip = footstepClipUnderwater;
            clipVolumeScale *= Mathf.Max(0f, underwaterFootstepVolumeMultiplier);
        }
        else if (IsWinterActive())
            clip = footstepClipSnow;

        if (clip == null)
            return;

        footstepAudioSource.PlayOneShot(clip, clipVolumeScale);
        stepCoolDown = stepRate;
        _lastFootstepPosition = planarSample;
    }

    Vector3 GetFootstepSamplePosition()
    {
        if (_cc != null)
            return _cc.bounds.min + Vector3.up * 0.08f;
        return transform.position + Vector3.up * 0.08f;
    }

    bool IsFootInWater(Vector3 worldPos)
    {
        if (_voxelWorld == null)
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();
        if (_voxelWorld == null || _voxelWorld.Config == null)
            return false;
        if (!_voxelWorld.TryWorldToColumn(worldPos, out int x, out int z))
            return false;

        int localY = Mathf.Clamp(
            Mathf.FloorToInt(worldPos.y - _voxelWorld.OriginWorld.y),
            0,
            _voxelWorld.Config.worldHeight - 1);

        // Check the player's foot cell and one block above for shallow shoreline contact.
        if (_voxelWorld.GetBlock(x, localY, z) == VoxelBlockType.Water)
            return true;
        int aboveY = Mathf.Min(_voxelWorld.Config.worldHeight - 1, localY + 1);
        if (_voxelWorld.GetBlock(x, aboveY, z) == VoxelBlockType.Water)
            return true;

        if (_voxelWorld.TryGetVisibleWaterSurfaceYAtWorld(worldPos, out float waterSurfaceY))
            return worldPos.y <= waterSurfaceY + 0.05f;

        int sea = _voxelWorld.Config.seaLevel;
        return _voxelWorld.GetBlock(x, sea, z) == VoxelBlockType.Water && worldPos.y <= (_voxelWorld.OriginWorld.y + sea + 1f + 0.05f);
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
        if (_voxelWorld != null &&
            _voxelWorld.TryGetVisibleWaterSurfaceYAtWorld(worldPos, out float visibleWaterSurfaceY) &&
            worldPos.y >= visibleWaterSurfaceY - Mathf.Max(0f, waterlinePadding))
            return false;

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

        float waterSurfaceY = _voxelWorld.OriginWorld.y + sea + 1f - Mathf.Max(0f, waterlinePadding);
        if (_voxelWorld.TryGetVisibleWaterSurfaceYAtWorld(worldPos, out float visibleY))
            waterSurfaceY = visibleY - Mathf.Max(0f, waterlinePadding);

        return worldPos.y < waterSurfaceY;
    }

    void TryStepOntoFrozenWater()
    {
        if (_cc == null)
            return;
        if (_voxelWorld == null)
            _voxelWorld = FindFirstObjectByType<VoxelWorld>();
        if (_voxelWorld == null || !_voxelWorld.IsWinterSurfaceFrozen)
            return;

        Vector3 probe = transform.position + transform.forward * Mathf.Min(0.2f, _cc.radius * 0.65f);
        if (!_voxelWorld.TryGetFrozenWaterSurfaceYAtWorld(probe, out float frozenSurfaceY))
            return;

        float footY = _cc.bounds.min.y;
        float stepUp = frozenSurfaceY - footY;
        float maxStep = Mathf.Max(0.12f, _cc.stepOffset + 0.08f);
        if (stepUp <= 0.01f || stepUp > maxStep)
            return;

        _cc.Move(Vector3.up * stepUp);
        if (_velocity.y < 0f)
            _velocity.y = -0.5f;
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
