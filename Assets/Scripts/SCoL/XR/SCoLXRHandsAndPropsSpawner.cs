using UnityEngine;
using UnityEngine.XR;

namespace SCoL.XR
{
    /// <summary>
    /// Spawns simple hand visuals + grabbers, and a few grabbable spheres.
    /// Attach this to XR Origin (VR).
    /// </summary>
    public class SCoLXRHandsAndPropsSpawner : MonoBehaviour
    {
        public Transform trackingOrigin;

        [Header("Hands")]
        public bool spawnHands = true;
        public float handVisualScale = 0.08f;

        [Header("Props")]
        public bool spawnProps = true;
        public int propCount = 6;
        public Vector3 propsCenter = new Vector3(0.5f, 1.1f, 1.0f);
        public float propsRadius = 0.35f;

        private bool _spawned;

        private void Awake()
        {
            if (trackingOrigin == null)
                trackingOrigin = transform;
        }

        private void Start()
        {
            if (_spawned) return;
            _spawned = true;

            if (spawnHands)
            {
                SpawnHand("LeftHand", XRNode.LeftHand, Color.cyan);
                SpawnHand("RightHand", XRNode.RightHand, Color.magenta);
            }

            if (spawnProps)
            {
                SpawnProps();
            }
        }

        private void SpawnHand(string name, XRNode node, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(null);
            go.transform.localScale = Vector3.one * handVisualScale;

            var r = go.GetComponent<Renderer>();
            if (r != null) r.material.color = color;

            // collider for overlap
            var col = go.GetComponent<SphereCollider>();
            col.isTrigger = true;

            var grabber = go.AddComponent<SCoLHandGrabber>();
            grabber.hand = node;
            grabber.trackingOrigin = trackingOrigin;
        }

        private void SpawnProps()
        {
            for (int i = 0; i < propCount; i++)
            {
                float a = (propCount <= 1) ? 0f : (i / (float)propCount) * Mathf.PI * 2f;
                Vector3 p = propsCenter + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * propsRadius;

                var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                go.name = $"Grabbable_{i}";
                go.transform.position = p;
                go.transform.localScale = Vector3.one * 0.10f;

                var rb = go.AddComponent<Rigidbody>();
                rb.mass = 0.2f;
                rb.linearDamping = 0.1f;
                rb.angularDamping = 0.05f;

                var g = go.AddComponent<SCoLGrabbable>();

                // color variety
                var r = go.GetComponent<Renderer>();
                if (r != null)
                {
                    float t = propCount <= 1 ? 0f : i / (float)(propCount - 1);
                    r.material.color = Color.Lerp(Color.yellow, Color.green, t);
                }
            }
        }
    }
}
