using UnityEngine;

namespace SCoL.Combat
{
    [DisallowMultipleComponent]
    public sealed class SCoLWorldHealthBar : MonoBehaviour
    {
        public Vector3 worldOffset = new Vector3(0f, 1.25f, 0f);
        public bool deriveOffsetFromRenderers = true;
        [Min(0.1f)] public float width = 0.8f;
        [Min(0.02f)] public float height = 0.08f;

        static Material s_FrameMaterial;
        static Material s_BackMaterial;
        static Material s_FillMaterial;

        Transform _root;
        Transform _fill;
        Camera _cam;
        float _normalized = 1f;

        void OnEnable()
        {
            EnsureBuilt();
            ApplyFill();
        }

        void LateUpdate()
        {
            if (_root == null)
                EnsureBuilt();
            if (_root == null)
                return;

            if (_cam == null)
                _cam = Camera.main;

            Vector3 offset = worldOffset;
            if (deriveOffsetFromRenderers && TryGetRenderableBounds(out Bounds bounds))
                offset.y = Mathf.Max(offset.y, bounds.extents.y * 2f + 0.18f);

            _root.position = transform.position + offset;
            if (_cam != null)
                _root.rotation = Quaternion.LookRotation(_cam.transform.forward, Vector3.up);
        }

        public void SetHealth(float current, float max)
        {
            float safeMax = Mathf.Max(0.0001f, max);
            _normalized = Mathf.Clamp01(current / safeMax);
            EnsureBuilt();
            ApplyFill();
        }

        public void SetVisible(bool isVisible)
        {
            if (_root == null)
                EnsureBuilt();
            if (_root != null)
                _root.gameObject.SetActive(isVisible);
        }

        void EnsureBuilt()
        {
            if (_root != null)
                return;

            EnsureSharedMaterials();

            var rootGO = new GameObject("WorldHealthBar");
            rootGO.transform.SetParent(transform, false);
            _root = rootGO.transform;

            var frame = CreateQuad("Frame", _root, width, height, s_FrameMaterial, Vector3.zero);
            CreateQuad("Background", frame, width * 0.92f, height * 0.62f, s_BackMaterial, new Vector3(0f, 0f, -0.001f));
            _fill = CreateQuad("Fill", frame, width * 0.92f, height * 0.62f, new Material(s_FillMaterial), new Vector3(0f, 0f, -0.002f));
        }

        void ApplyFill()
        {
            if (_fill == null)
                return;

            float fillWidth = Mathf.Max(0.0001f, width * 0.92f * _normalized);
            _fill.localScale = new Vector3(fillWidth, height * 0.62f, 1f);
            _fill.localPosition = new Vector3(-(width * 0.92f - fillWidth) * 0.5f, 0f, -0.002f);

            var renderer = _fill.GetComponent<Renderer>();
            if (renderer != null && renderer.sharedMaterial != null)
            {
                Color color = Color.Lerp(new Color(0.88f, 0.18f, 0.16f, 0.98f), new Color(0.24f, 0.82f, 0.40f, 0.98f), _normalized);
                renderer.sharedMaterial.color = color;
            }
        }

        static Transform CreateQuad(string name, Transform parent, float quadWidth, float quadHeight, Material mat, Vector3 localPos)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = new Vector3(quadWidth, quadHeight, 1f);

            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
                renderer.sharedMaterial = mat;

            return go.transform;
        }

        static void EnsureSharedMaterials()
        {
            if (s_FrameMaterial != null && s_BackMaterial != null && s_FillMaterial != null)
                return;

            Shader shader = Shader.Find("Unlit/Color");
            if (shader == null)
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (s_FrameMaterial == null)
                s_FrameMaterial = NewMaterial(shader, "WorldHealthBar_Frame", new Color(0.08f, 0.09f, 0.10f, 0.96f));
            if (s_BackMaterial == null)
                s_BackMaterial = NewMaterial(shader, "WorldHealthBar_Back", new Color(0.16f, 0.12f, 0.12f, 0.92f));
            if (s_FillMaterial == null)
                s_FillMaterial = NewMaterial(shader, "WorldHealthBar_Fill", new Color(0.24f, 0.82f, 0.40f, 0.98f));
        }

        static Material NewMaterial(Shader shader, string name, Color color)
        {
            var mat = new Material(shader) { name = name };
            if (mat.HasProperty("_BaseColor"))
                mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color"))
                mat.SetColor("_Color", color);
            return mat;
        }

        bool TryGetRenderableBounds(out Bounds bounds)
        {
            var renderers = GetComponentsInChildren<Renderer>(includeInactive: false);
            if (renderers == null || renderers.Length == 0)
            {
                bounds = default;
                return false;
            }

            bool found = false;
            bounds = default;
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || r.transform.IsChildOf(_root))
                    continue;

                if (!found)
                {
                    bounds = r.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(r.bounds);
                }
            }

            return found;
        }
    }
}
