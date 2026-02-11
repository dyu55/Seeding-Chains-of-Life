using UnityEngine;

namespace SCoL.Visualization
{
    /// <summary>
    /// Blends between a season Day and Night sky background using two large inverted spheres
    /// around the camera.
    ///
    /// Why this exists:
    /// - Unity skybox materials (Skybox/Panoramic) don't support cross-fading between two different
    ///   textures out of the box.
    /// - This dome method gives a smooth blend and looks natural for VR if textures are good.
    ///
    /// Setup:
    /// - Put this on a GameObject in the scene.
    /// - Assign seasonSkyboxController and dayNightController.
    /// - Ensure your season materials use Skybox/Panoramic and have a _MainTex texture.
    /// - Set your Camera Clear Flags/Background to Skybox or Solid Color; it won't matter much
    ///   because the dome will fill the view.
    ///
    /// Notes:
    /// - This uses URP Unlit shader if available, otherwise Unlit/Texture.
    /// - It does NOT currently apply rotation; if you need alignment, we can add yaw.
    /// </summary>
    public class SkyDomeDayNightBlender : MonoBehaviour
    {
        [Header("Sources")]
        public SeasonSkyboxController seasonSkyboxController;
        public DayNightLightingController dayNightController;

        [Header("Cycle")]
        [Tooltip("Seconds to crossfade at sunrise + sunset. Example: 10 means 10s fades.")]
        [Min(0.1f)] public float transitionSeconds = 10f;

        [Header("Dome")]
        public Transform followTarget;

        [Tooltip("Log warnings when textures/materials are missing.")]
        public bool logWarnings = true;

        [Tooltip("Big enough to cover the whole world. Keep far clip < dome radius.")]
        [Min(10f)] public float domeRadius = 200f;

        [Tooltip("Render queue for the dome materials. Lower renders earlier.")]
        public int renderQueue = 1000;

        [Tooltip("If true, disables RenderSettings.skybox so it doesn't fight the dome visually.")]
        public bool disableUnitySkybox = true;

        GameObject _dayDome;
        GameObject _nightDome;
        Material _dayMat;
        Material _nightMat;

        SeasonSkyboxController.Season _lastSeason = (SeasonSkyboxController.Season)(-1);

        void Awake()
        {
            if (seasonSkyboxController == null)
                seasonSkyboxController = FindFirstObjectByType<SeasonSkyboxController>();
            if (dayNightController == null)
                dayNightController = FindFirstObjectByType<DayNightLightingController>();

            if (followTarget == null && Camera.main != null)
                followTarget = Camera.main.transform;

            EnsureDomes();
            RefreshTextures(force: true);
        }

        void LateUpdate()
        {
            EnsureDomes();

            if (followTarget != null)
                transform.position = followTarget.position;

            if (disableUnitySkybox)
                RenderSettings.skybox = null;

            RefreshTextures(force: false);
            ApplyBlend();
        }

        void EnsureDomes()
        {
            if (_dayDome == null)
            {
                _dayDome = CreateDome("SkyDome_Day");
                _dayDome.transform.SetParent(transform, false);
                _dayMat = CreateUnlitTextureMaterial();
                _dayMat.renderQueue = renderQueue;
                _dayDome.GetComponent<MeshRenderer>().sharedMaterial = _dayMat;
            }

            if (_nightDome == null)
            {
                _nightDome = CreateDome("SkyDome_Night");
                _nightDome.transform.SetParent(transform, false);
                _nightMat = CreateUnlitTextureMaterial();
                _nightMat.renderQueue = renderQueue + 1; // render after day
                _nightDome.GetComponent<MeshRenderer>().sharedMaterial = _nightMat;
            }

            float d = domeRadius * 2f;
            // IMPORTANT: keep X negative so the sphere is inside-out (camera is inside).
            _dayDome.transform.localScale = new Vector3(-d, d, d);
            _nightDome.transform.localScale = new Vector3(-d, d, d);
        }

        static GameObject CreateDome(string name)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            // Remove collider
            var col = go.GetComponent<Collider>();
            if (col != null) Destroy(col);

            // Invert normals by flipping scale on X (cheap trick)
            go.transform.localScale = new Vector3(-1f, 1f, 1f);

            var mr = go.GetComponent<MeshRenderer>();
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            mr.receiveShadows = false;

            return go;
        }

        static Material CreateUnlitTextureMaterial()
        {
            // Prefer URP Unlit if available
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            bool isUrpUnlit = shader != null;
            if (shader == null)
                shader = Shader.Find("Unlit/Texture");

            var mat = new Material(shader);

            if (isUrpUnlit)
            {
                // Force transparency settings (URP Unlit)
                // These property names are used by URP shaders.
                if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f); // 1 = Transparent
                if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);     // 0 = Alpha
                if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
                if (mat.HasProperty("_AlphaClip")) mat.SetFloat("_AlphaClip", 0f);

                // Disable culling so inside of the dome always renders (more robust than relying on negative scale)
                if (mat.HasProperty("_Cull")) mat.SetFloat("_Cull", 0f); // 0 = Off

                // Blend factors
                if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                // Default color white with alpha 1
                if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            }
            else
            {
                // Built-in Unlit/Texture: use _Color alpha for blending; set to Transparent queue
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            }

            return mat;
        }

        void RefreshTextures(bool force)
        {
            if (seasonSkyboxController == null)
                return;

            var season = seasonSkyboxController.GetCurrentSeason();
            if (!force && season == _lastSeason)
                return;

            var day = seasonSkyboxController.GetDayMaterialForSeason(season);
            var night = seasonSkyboxController.GetNightMaterialForSeason(season);

            var dayTex = ExtractPanoramaTexture(day);
            var nightTex = ExtractPanoramaTexture(night);

            if (logWarnings)
            {
                if (day == null) Debug.LogWarning($"SkyDomeDayNightBlender: Day material missing for season {season}.", this);
                if (night == null) Debug.LogWarning($"SkyDomeDayNightBlender: Night material missing for season {season}.", this);
                if (day != null && dayTex == null) Debug.LogWarning($"SkyDomeDayNightBlender: Could not extract texture from DAY skybox material '{day.name}'. Expected _MainTex.", this);
                if (night != null && nightTex == null) Debug.LogWarning($"SkyDomeDayNightBlender: Could not extract texture from NIGHT skybox material '{night.name}'. Expected _MainTex.", this);
            }

            if (_dayMat != null) SetMainTexture(_dayMat, dayTex);
            if (_nightMat != null) SetMainTexture(_nightMat, nightTex);

            _lastSeason = season;
        }

        static Texture ExtractPanoramaTexture(Material skyboxMat)
        {
            if (skyboxMat == null) return null;

            // Skybox/Panoramic uses _MainTex.
            if (skyboxMat.HasProperty("_MainTex"))
                return skyboxMat.GetTexture("_MainTex");

            // Fallbacks for other skybox shaders
            if (skyboxMat.HasProperty("_Tex"))
                return skyboxMat.GetTexture("_Tex");

            return null;
        }

        static void SetMainTexture(Material mat, Texture tex)
        {
            if (mat == null) return;
            if (tex == null) return;

            if (mat.HasProperty("_BaseMap")) mat.SetTexture("_BaseMap", tex); // URP Unlit
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", tex); // Built-in Unlit
        }

        void ApplyBlend()
        {
            if (_dayMat == null || _nightMat == null)
                return;

            float t01 = dayNightController != null
                ? Mathf.Repeat(dayNightController.timeOfDay01, 1f)
                : Mathf.Repeat(Time.time * 0.05f, 1f);

            // Use the same day/night boundaries as SeasonSkyboxController (so it stays in sync).
            Vector2 dayRange01 = seasonSkyboxController != null ? seasonSkyboxController.dayRange01 : new Vector2(0.23f, 0.77f);

            // Compute transition width in normalized time.
            float cycle = (dayNightController != null && dayNightController.cycleDurationSeconds > 0f)
                ? dayNightController.cycleDurationSeconds
                : 20f;
            float w01 = Mathf.Clamp01(transitionSeconds / cycle) * 0.5f; // half-width on each side

            float blend = ComputeDayNightBlend(t01, dayRange01.x, dayRange01.y, w01);

            // Day alpha goes down as night rises.
            SetAlpha(_dayMat, 1f - blend);
            SetAlpha(_nightMat, blend);
        }

        static float ComputeDayNightBlend(float t01, float dayStart01, float dayEnd01, float halfWidth01)
        {
            // blend = 0 => fully DAY texture
            // blend = 1 => fully NIGHT texture
            t01 = Mathf.Repeat(t01, 1f);
            dayStart01 = Mathf.Repeat(dayStart01, 1f);
            dayEnd01 = Mathf.Repeat(dayEnd01, 1f);
            halfWidth01 = Mathf.Clamp(halfWidth01, 0f, 0.25f);

            bool isDay = SeasonSkyboxController.IsWithinWrappedRange(t01, dayStart01, dayEnd01);
            float baseBlend = isDay ? 0f : 1f;

            if (halfWidth01 <= 0f)
                return baseBlend;

            // Use angle math to handle wrap-around cleanly.
            float tDeg = t01 * 360f;
            float sunriseDeg = dayStart01 * 360f;
            float sunsetDeg = dayEnd01 * 360f;
            float wDeg = halfWidth01 * 360f;

            float dSunrise = Mathf.DeltaAngle(sunriseDeg, tDeg); // negative before sunrise, positive after
            float dSunset = Mathf.DeltaAngle(sunsetDeg, tDeg);   // negative before sunset, positive after

            // Sunrise transition: NIGHT -> DAY
            if (Mathf.Abs(dSunrise) <= wDeg)
            {
                float u = Mathf.InverseLerp(-wDeg, wDeg, dSunrise);
                float s = u * u * (3f - 2f * u); // smoothstep
                return 1f - s;
            }

            // Sunset transition: DAY -> NIGHT
            if (Mathf.Abs(dSunset) <= wDeg)
            {
                float u = Mathf.InverseLerp(-wDeg, wDeg, dSunset);
                float s = u * u * (3f - 2f * u);
                return s;
            }

            return baseBlend;
        }

        static void SetAlpha(Material mat, float a)
        {
            a = Mathf.Clamp01(a);

            // URP Unlit uses _BaseColor, built-in Unlit uses _Color
            if (mat.HasProperty("_BaseColor"))
            {
                var c = mat.GetColor("_BaseColor");
                c.a = a;
                mat.SetColor("_BaseColor", c);
                return;
            }

            if (mat.HasProperty("_Color"))
            {
                var c = mat.GetColor("_Color");
                c.a = a;
                mat.SetColor("_Color", c);
            }
        }
    }
}
