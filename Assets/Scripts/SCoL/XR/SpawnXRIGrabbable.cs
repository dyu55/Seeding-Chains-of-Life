using UnityEngine;


namespace SCoL.XR
{
    /// <summary>
    /// Spawns a simple primitive (sphere by default) and attaches XRGrabInteractable.
    /// This avoids having to hand-edit YAML for XRGrabInteractable serialized fields.
    /// </summary>
    public class SpawnXRIGrabbable : MonoBehaviour
    {
        public enum Shape
        {
            Sphere,
            Cube,
            Capsule
        }

        public Shape shape = Shape.Sphere;
        public Vector3 position = new Vector3(0.5f, 1.2f, 1.0f);
        public float scale = 0.15f;
        public bool useGravity = true;
        public float mass = 0.2f;

        [Header("Debug")]
        public string objectName = "XRI_Grabbable";

        private bool _spawned;

        private void Start()
        {
            if (_spawned) return;
            _spawned = true;

            var primitive = GameObject.CreatePrimitive(shape switch
            {
                Shape.Cube => PrimitiveType.Cube,
                Shape.Capsule => PrimitiveType.Capsule,
                _ => PrimitiveType.Sphere
            });

            primitive.name = objectName;
            primitive.transform.position = position;
            primitive.transform.localScale = Vector3.one * scale;

            var rb = primitive.AddComponent<Rigidbody>();
            rb.useGravity = useGravity;
            rb.mass = mass;

            // XR Grab
            primitive.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();

            // Optional: make it a little bouncy/obvious
            var r = primitive.GetComponent<Renderer>();
            if (r != null)
                r.material.color = new Color(0.9f, 0.9f, 0.2f);
        }
    }
}
