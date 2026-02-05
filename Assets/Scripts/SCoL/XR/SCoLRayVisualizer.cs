using UnityEngine;

namespace SCoL.XR
{
    /// <summary>
    /// Simple right-hand ray visualizer for VR aiming.
    /// Draws a LineRenderer along the same aim ray used by SCoLToolController.
    /// </summary>
    [DisallowMultipleComponent]
    public class SCoLRayVisualizer : MonoBehaviour
    {
        public SCoLToolController tool;

        [Header("Ray")]
        public float length = 6f;
        public Color color = new Color(0.2f, 0.9f, 1.0f, 0.9f);
        public float width = 0.01f;

        private LineRenderer _lr;

        private void Awake()
        {
            if (tool == null)
                tool = FindFirstObjectByType<SCoLToolController>();

            _lr = GetComponent<LineRenderer>();
            if (_lr == null)
                _lr = gameObject.AddComponent<LineRenderer>();

            _lr.useWorldSpace = true;
            _lr.positionCount = 2;
            _lr.startWidth = width;
            _lr.endWidth = width;
            _lr.material = new Material(Shader.Find("Unlit/Color"));
            _lr.material.color = color;
        }

        private void LateUpdate()
        {
            if (tool == null)
                tool = FindFirstObjectByType<SCoLToolController>();
            if (tool == null)
            {
                _lr.enabled = false;
                return;
            }

            if (!tool.TryGetToolAimRay(out var ray))
            {
                _lr.enabled = false;
                return;
            }

            _lr.enabled = true;
            _lr.SetPosition(0, ray.origin);
            _lr.SetPosition(1, ray.origin + ray.direction * length);
        }
    }
}
