using UnityEngine;

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

    CharacterController _cc;
    float _pitch;
    Vector3 _velocity;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        if (cameraPivot == null && Camera.main != null) cameraPivot = Camera.main.transform;
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

        // Look
        if (cameraPivot != null)
        {
            float mx = Input.GetAxisRaw("Mouse X") * mouseSensitivity;
            float my = Input.GetAxisRaw("Mouse Y") * mouseSensitivity;

            transform.Rotate(0f, mx, 0f, Space.Self);

            _pitch -= my;
            _pitch = Mathf.Clamp(_pitch, -maxPitch, maxPitch);
            cameraPivot.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
        }

        // Move
        float x = Input.GetAxisRaw("Horizontal");
        float z = Input.GetAxisRaw("Vertical");
        Vector3 move = (transform.right * x + transform.forward * z);
        if (move.sqrMagnitude > 1f) move.Normalize();

        float speed = (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) ? sprintSpeed : walkSpeed;
        _cc.Move(move * (speed * Time.deltaTime));

        // Ground / gravity
        bool grounded = _cc.isGrounded;
        if (grounded && _velocity.y < 0f)
            _velocity.y = -2f; // keep grounded

        // Jump
        if (grounded && Input.GetKeyDown(KeyCode.Space))
        {
            // v = sqrt(h * -2g)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
        }

        _velocity.y += gravity * Time.deltaTime;
        _cc.Move(_velocity * Time.deltaTime);

        // Escape to unlock cursor
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
