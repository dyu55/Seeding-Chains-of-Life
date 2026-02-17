using UnityEngine;
using UnityEngine.InputSystem;

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

        var mouse = Mouse.current;
        var kb = Keyboard.current;

        // Look (mouse delta)
        if (cameraPivot != null && mouse != null)
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

        // Escape to unlock cursor
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}
