using UnityEngine;
using SCoL.Voxels;

namespace SCoL.Visualization
{
    /// <summary>
    /// Lightweight underwater visual feedback for voxel oceans.
    /// Applies stronger blue fog when the active camera is below sea level.
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLUnderwaterEffect : MonoBehaviour
    {
        [Header("Refs")]
        public VoxelWorld voxelWorld;
        public Camera targetCamera;

        [Header("Level")]
        [Tooltip("World-space offset above sea level considered as the water surface.")]
        public float waterSurfaceOffset = 0.0f;
        [Tooltip("Small hysteresis to avoid flickering when camera hovers at the waterline.")]
        [Range(0f, 1f)] public float waterlineHysteresis = 0.00f;

        [Header("Fog Override")]
        public Color underwaterFogColor = new Color(0.12f, 0.28f, 0.40f, 1f);
        public FogMode underwaterFogMode = FogMode.ExponentialSquared;
        [Range(0f, 0.2f)] public float underwaterFogDensity = 0.032f;

        [Header("Camera Override")]
        [Tooltip("Use solid-color background underwater to hide skybox seams at the waterline.")]
        public bool overrideCameraClear = true;
        [Tooltip("Reduces distant terrain leaks while submerged.")]
        public bool overrideFarClip = true;
        [Min(5f)] public float underwaterFarClip = 42f;
        public Color underwaterBackgroundColor = new Color(0.10f, 0.22f, 0.30f, 1f);

        [Header("Water Visibility")]
        [Tooltip("When underwater, hide voxel water surface mesh (Minecraft-like underwater view).")]
        public bool hideWaterSurfaceWhenUnderwater = true;

        private bool _active;
        private bool _savedFogEnabled;
        private FogMode _savedFogMode;
        private Color _savedFogColor;
        private float _savedFogDensity;
        private CameraClearFlags _savedClearFlags;
        private Color _savedBackgroundColor;
        private float _savedFarClip;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();
            if (targetCamera == null)
                targetCamera = Camera.main;
            if (voxelWorld == null)
                voxelWorld = FindFirstObjectByType<VoxelWorld>();
        }

        private void LateUpdate()
        {
            if (voxelWorld == null || voxelWorld.Config == null)
                return;
            if (targetCamera == null || !targetCamera.isActiveAndEnabled)
                targetCamera = Camera.main;
            if (targetCamera == null)
                return;

            Vector3 camPos = targetCamera.transform.position;

            float waterY = voxelWorld.OriginWorld.y + voxelWorld.Config.seaLevel + waterSurfaceOffset;
            if (voxelWorld.TryGetVisibleWaterSurfaceYAtWorld(camPos, out float visibleWaterSurfaceY))
                waterY = visibleWaterSurfaceY + waterSurfaceOffset;

            if (voxelWorld.TryGetVisibleWaterSurfaceYAtWorld(camPos, out float currentVisibleSurfaceY) &&
                camPos.y >= currentVisibleSurfaceY - Mathf.Max(0f, waterlineHysteresis))
            {
                if (_active)
                    ExitUnderwater();
                return;
            }

            // Primary: exact voxel occupancy around camera (more reliable at the waterline than a pure Y threshold).
            bool inWaterVoxel = IsCameraInsideWaterVoxel(camPos);

            // Fallback: sea-level threshold with hysteresis.
            float y = camPos.y;
            bool byHeight;
            if (_active)
                byHeight = y < (waterY + waterlineHysteresis);
            else
                byHeight = y < (waterY - waterlineHysteresis);

            bool inWaterColumn = voxelWorld.IsWaterColumnAtWorld(camPos);
            bool shouldBeActive = inWaterVoxel || (inWaterColumn && byHeight);

            if (shouldBeActive == _active)
            {
                if (_active)
                    ApplyUnderwaterState();
                return;
            }

            if (shouldBeActive)
                EnterUnderwater();
            else
                ExitUnderwater();
        }

        private void OnDisable()
        {
            ExitUnderwater();
        }

        private void EnterUnderwater()
        {
            _savedFogEnabled = RenderSettings.fog;
            _savedFogMode = RenderSettings.fogMode;
            _savedFogColor = RenderSettings.fogColor;
            _savedFogDensity = RenderSettings.fogDensity;
            _savedClearFlags = targetCamera.clearFlags;
            _savedBackgroundColor = targetCamera.backgroundColor;
            _savedFarClip = targetCamera.farClipPlane;
            _active = true;
            if (hideWaterSurfaceWhenUnderwater && voxelWorld != null)
                voxelWorld.SetWaterSurfaceVisible(false);
            ApplyUnderwaterState();
        }

        private void ExitUnderwater()
        {
            if (!_active) return;
            _active = false;
            if (hideWaterSurfaceWhenUnderwater && voxelWorld != null)
                voxelWorld.SetWaterSurfaceVisible(true);
            RenderSettings.fog = _savedFogEnabled;
            RenderSettings.fogMode = _savedFogMode;
            RenderSettings.fogColor = _savedFogColor;
            RenderSettings.fogDensity = _savedFogDensity;
            targetCamera.clearFlags = _savedClearFlags;
            targetCamera.backgroundColor = _savedBackgroundColor;
            targetCamera.farClipPlane = _savedFarClip;
        }

        private void ApplyUnderwaterState()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = underwaterFogMode;
            RenderSettings.fogColor = underwaterFogColor;
            RenderSettings.fogDensity = underwaterFogDensity;

            if (overrideCameraClear)
            {
                targetCamera.clearFlags = CameraClearFlags.SolidColor;
                targetCamera.backgroundColor = underwaterBackgroundColor;
            }

            if (overrideFarClip)
                targetCamera.farClipPlane = Mathf.Min(_savedFarClip, underwaterFarClip);
        }

        private bool IsCameraInsideWaterVoxel(Vector3 worldPos)
        {
            if (!voxelWorld.TryWorldToColumn(worldPos, out int x, out int z))
                return false;

            Vector3 local = worldPos - voxelWorld.OriginWorld;
            int y = Mathf.Clamp(Mathf.FloorToInt(local.y), 0, voxelWorld.Config.worldHeight - 1);

            // Check immediate neighborhood around camera height to avoid "just-below-surface" misses.
            int y0 = Mathf.Max(0, y - 1);
            int y1 = Mathf.Min(voxelWorld.Config.worldHeight - 1, y + 1);

            for (int yy = y0; yy <= y1; yy++)
            {
                if (voxelWorld.GetBlock(x, yy, z) == VoxelBlockType.Water)
                    return true;
            }

            return false;
        }
    }
}
