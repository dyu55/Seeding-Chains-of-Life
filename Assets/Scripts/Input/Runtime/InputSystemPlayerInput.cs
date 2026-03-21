using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SCoL.InputLayer
{
    /// <summary>
    /// Input System-backed implementation with PlayerInput/InputActionAsset first,
    /// and safe runtime fallback actions for keyboard, mouse, and gamepad.
    /// </summary>
    [DisallowMultipleComponent]
    public class InputSystemPlayerInput : MonoBehaviour, IPlayerInput
    {
        [Header("Preferred Input Source")]
        [Tooltip("If assigned, actions are resolved from this PlayerInput first.")]
        public PlayerInput playerInput;
        [Tooltip("Optional direct actions asset fallback when PlayerInput is not present.")]
        public InputActionAsset actionsAsset;
        [Tooltip("Preferred action map name. Falls back to Player if Gameplay doesn't exist.")]
        public string preferredActionMap = "Gameplay";
        [Tooltip("Fallback action map name used by the current project asset.")]
        public string fallbackActionMap = "Player";

        [Header("Look Processing")]
        [Range(0.01f, 5f)] public float lookSensitivityX = 1f;
        [Range(0.01f, 5f)] public float lookSensitivityY = 1f;
        public bool enableLookSmoothing = false;
        [Range(0f, 30f)] public float lookSmoothing = 14f;

        [Header("Turn")]
        public bool useSnapTurn = false;
        [Min(0f)] public float smoothTurnDegreesPerSecond = 180f;
        [Range(0f, 1f)] public float turnAxisDeadzone = 0.2f;
        [Range(15f, 90f)] public float snapTurnDegrees = 45f;
        [Range(0.1f, 1f)] public float snapTurnDeadzone = 0.75f;

        [Header("Cursor")]
        public bool lockCursorOnStart = true;
        public bool hideCursorWhenLocked = true;
        public bool relockOnPrimaryClick = true;

        private InputAction _move;
        private InputAction _look;
        private InputAction _turn;
        private InputAction _jump;
        private InputAction _sprint;
        private InputAction _primary;
        private InputAction _secondary;
        private InputAction _drop;
        private InputAction _toolNext;
        private InputAction _toolPrev;
        private InputAction _pause;
        private InputAction _tool1;
        private InputAction _tool2;
        private InputAction _tool3;
        private InputAction _tool4;
        private InputAction _tool5;
        private InputAction _respawn;
        private InputAction _jumpSupplemental;
        private InputAction _primarySupplemental;
        private InputAction _secondarySupplemental;
        private InputAction _dropSupplemental;
        private InputAction _toolNextSupplemental;
        private InputAction _toolPrevSupplemental;
        private InputAction _pauseSupplemental;
        private InputAction _tool1Supplemental;
        private InputAction _tool2Supplemental;
        private InputAction _tool3Supplemental;
        private InputAction _tool4Supplemental;
        private InputAction _tool5Supplemental;
        private InputAction _respawnSupplemental;

        private InputActionMap _runtimeFallbackMap;
        private InputActionMap _resolvedMap;
        private bool _ownsResolvedMapEnable;
        private readonly List<InputAction> _supplementalActions = new List<InputAction>(16);
        private bool _snapReady = true;
        private Vector2 _smoothedLook;
        private bool _useGamepadPrompts;

        public Vector2 Move => ReadVector2(_move);
        public Vector2 Look => _smoothedLook;
        public float TurnDegreesThisFrame { get; private set; }

        public bool JumpPressedThisFrame => WasPressedAny(_jump, _jumpSupplemental);
        public bool SprintHeld => IsPressed(_sprint);
        public bool PrimaryPressedThisFrame => WasPressedAny(_primary, _primarySupplemental);
        public bool SecondaryPressedThisFrame => WasPressedAny(_secondary, _secondarySupplemental);
        public bool DropPressedThisFrame => WasPressedAny(_drop, _dropSupplemental);
        public bool ToolNextPressedThisFrame => WasPressedAny(_toolNext, _toolNextSupplemental);
        public bool ToolPrevPressedThisFrame => WasPressedAny(_toolPrev, _toolPrevSupplemental);
        public bool PausePressedThisFrame => WasPressedAny(_pause, _pauseSupplemental);
        public bool ToolSlot1PressedThisFrame => WasPressedAny(_tool1, _tool1Supplemental);
        public bool ToolSlot2PressedThisFrame => WasPressedAny(_tool2, _tool2Supplemental);
        public bool ToolSlot3PressedThisFrame => WasPressedAny(_tool3, _tool3Supplemental);
        public bool ToolSlot4PressedThisFrame => WasPressedAny(_tool4, _tool4Supplemental);
        public bool ToolSlot5PressedThisFrame => WasPressedAny(_tool5, _tool5Supplemental);
        public bool RespawnPressedThisFrame => WasPressedAny(_respawn, _respawnSupplemental);
        public bool UseGamepadPrompts => _useGamepadPrompts;

        private void Awake()
        {
            if (playerInput == null)
                playerInput = FindFirstObjectByType<PlayerInput>();

            ResolveActions();
            EnableResolvedMapIfOwned();
            EnableSupplementalActions();

            if (lockCursorOnStart)
                SetCursorLocked(true);
        }

        private void OnEnable()
        {
            EnableResolvedMapIfOwned();
            EnableFallbackActionsIfAny();
            EnableSupplementalActions();
        }

        private void OnDisable()
        {
            DisableResolvedMapIfOwned();
            DisableFallbackActionsIfAny();
            DisableSupplementalActions();
        }

        private void OnDestroy()
        {
            DisableSupplementalActions();
            for (int i = 0; i < _supplementalActions.Count; i++)
                _supplementalActions[i]?.Dispose();
            _supplementalActions.Clear();
        }

        private void Update()
        {
            Vector2 moveInput = ReadVector2(_move);
            Vector2 rawLook = ReadVector2(_look);
            rawLook = new Vector2(rawLook.x * lookSensitivityX, rawLook.y * lookSensitivityY);
            float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
            if (!enableLookSmoothing || lookSmoothing <= 0f)
            {
                _smoothedLook = rawLook;
            }
            else
            {
                float t = 1f - Mathf.Exp(-lookSmoothing * dt);
                _smoothedLook = Vector2.Lerp(_smoothedLook, rawLook, t);
            }

            float turnAxis = ReadFloat(_turn);
            if (Mathf.Abs(turnAxis) < Mathf.Clamp01(turnAxisDeadzone))
                turnAxis = 0f;
            TurnDegreesThisFrame = ComputeTurnDelta(turnAxis, dt);

            UpdatePromptDevice(moveInput, rawLook, turnAxis);

            if (lockCursorOnStart && PausePressedThisFrame)
                SetCursorLocked(false);
            if (lockCursorOnStart && relockOnPrimaryClick && Cursor.lockState != CursorLockMode.Locked && PrimaryPressedThisFrame)
                SetCursorLocked(true);
        }

        private void ResolveActions()
        {
            bool fromPlayerInput = false;
            InputActionMap map = ResolveActionMap(ref fromPlayerInput);
            if (map == null)
            {
                BuildRuntimeFallbackMap();
                map = _runtimeFallbackMap;
                fromPlayerInput = true;
            }
            _resolvedMap = map;
            _ownsResolvedMapEnable = map != null && !fromPlayerInput;

            _move = FindAction(map, "Move");
            _look = FindAction(map, "Look");
            _turn = FindAction(map, "Turn");
            _jump = FindAction(map, "Jump");
            _sprint = FindAction(map, "Sprint");
            _primary = FindAction(map, "Primary", "Attack", "Fire");
            _secondary = FindAction(map, "Secondary", "Interact", "AltFire");
            _drop = FindAction(map, "Drop", "Discard");
            _toolNext = FindAction(map, "ToolNext", "Next");
            _toolPrev = FindAction(map, "ToolPrev", "Previous", "Prev");
            _pause = FindAction(map, "Pause", "Menu", "Cancel");
            _tool1 = FindAction(map, "Tool1");
            _tool2 = FindAction(map, "Tool2");
            _tool3 = FindAction(map, "Tool3");
            _tool4 = FindAction(map, "Tool4");
            _tool5 = FindAction(map, "Tool5");
            _respawn = FindAction(map, "Respawn");

            BuildSupplementalActions();
        }

        private InputActionMap ResolveActionMap(ref bool fromPlayerInput)
        {
            if (playerInput != null && playerInput.actions != null)
            {
                var map = playerInput.actions.FindActionMap(preferredActionMap, throwIfNotFound: false);
                if (map != null)
                {
                    fromPlayerInput = true;
                    return map;
                }
                map = playerInput.actions.FindActionMap(fallbackActionMap, throwIfNotFound: false);
                if (map != null)
                {
                    fromPlayerInput = true;
                    return map;
                }
            }

            if (actionsAsset != null)
            {
                var map = actionsAsset.FindActionMap(preferredActionMap, throwIfNotFound: false);
                if (map != null) return map;
                map = actionsAsset.FindActionMap(fallbackActionMap, throwIfNotFound: false);
                if (map != null) return map;
            }

            return null;
        }

        private static InputAction FindAction(InputActionMap map, params string[] names)
        {
            if (map == null || names == null) return null;
            for (int i = 0; i < names.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(names[i])) continue;
                var a = map.FindAction(names[i], throwIfNotFound: false);
                if (a != null) return a;
            }
            return null;
        }

        private void BuildRuntimeFallbackMap()
        {
            _runtimeFallbackMap = new InputActionMap("Gameplay");

            _move = _runtimeFallbackMap.AddAction("Move", InputActionType.Value, null, null, null, null, "Vector2");
            _move.AddBinding("<Gamepad>/leftStick");
            _move.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");

            _look = _runtimeFallbackMap.AddAction("Look", InputActionType.Value, null, null, null, null, "Vector2");
            _look.AddBinding("<Mouse>/delta");
            _look.AddBinding("<Gamepad>/rightStick");

            _jump = _runtimeFallbackMap.AddAction("Jump", InputActionType.Button);
            _jump.AddBinding("<Keyboard>/space");
            _jump.AddBinding("<Gamepad>/buttonSouth");

            _sprint = _runtimeFallbackMap.AddAction("Sprint", InputActionType.Button);
            _sprint.AddBinding("<Keyboard>/leftShift");
            _sprint.AddBinding("<Gamepad>/leftStickPress");

            _primary = _runtimeFallbackMap.AddAction("Primary", InputActionType.Button);
            _primary.AddBinding("<Mouse>/leftButton");
            _primary.AddBinding("<Gamepad>/rightTrigger");

            _secondary = _runtimeFallbackMap.AddAction("Secondary", InputActionType.Button);
            _secondary.AddBinding("<Mouse>/rightButton");
            _secondary.AddBinding("<Gamepad>/leftTrigger");

            _drop = _runtimeFallbackMap.AddAction("Drop", InputActionType.Button);
            _drop.AddBinding("<Keyboard>/q");
            _drop.AddBinding("<Gamepad>/buttonWest");

            _toolNext = _runtimeFallbackMap.AddAction("ToolNext", InputActionType.Button);
            _toolNext.AddBinding("<Mouse>/scroll/up");
            _toolNext.AddBinding("<Gamepad>/rightShoulder");
            _toolNext.AddBinding("<Gamepad>/dpad/right");
            _toolNext.AddBinding("<Gamepad>/dpad/up");

            _toolPrev = _runtimeFallbackMap.AddAction("ToolPrev", InputActionType.Button);
            _toolPrev.AddBinding("<Mouse>/scroll/down");
            _toolPrev.AddBinding("<Gamepad>/leftShoulder");
            _toolPrev.AddBinding("<Gamepad>/dpad/left");
            _toolPrev.AddBinding("<Gamepad>/dpad/down");

            _pause = _runtimeFallbackMap.AddAction("Pause", InputActionType.Button);
            _pause.AddBinding("<Keyboard>/escape");
            _pause.AddBinding("<Gamepad>/startButton");
            _tool1 = _runtimeFallbackMap.AddAction("Tool1", InputActionType.Button, "<Keyboard>/1");
            _tool2 = _runtimeFallbackMap.AddAction("Tool2", InputActionType.Button, "<Keyboard>/2");
            _tool3 = _runtimeFallbackMap.AddAction("Tool3", InputActionType.Button, "<Keyboard>/3");
            _tool4 = _runtimeFallbackMap.AddAction("Tool4", InputActionType.Button, "<Keyboard>/4");
            _tool5 = _runtimeFallbackMap.AddAction("Tool5", InputActionType.Button, "<Keyboard>/5");
            _respawn = _runtimeFallbackMap.AddAction("Respawn", InputActionType.Button);
            _respawn.AddBinding("<Keyboard>/y");
            _respawn.AddBinding("<Gamepad>/buttonNorth");
        }

        private void BuildSupplementalActions()
        {
            ClearSupplementalActions();

            _primarySupplemental = CreateSupplementalAction("PrimarySupplemental", "<Mouse>/leftButton", "<Gamepad>/rightTrigger");
            _secondarySupplemental = CreateSupplementalAction("SecondarySupplemental", "<Mouse>/rightButton", "<Gamepad>/leftTrigger");
            _dropSupplemental = CreateSupplementalAction("DropSupplemental", "<Keyboard>/q", "<Gamepad>/buttonWest");
            _jumpSupplemental = CreateSupplementalAction("JumpSupplemental", "<Keyboard>/space", "<Gamepad>/buttonSouth");
            _toolNextSupplemental = CreateSupplementalAction("ToolNextSupplemental", "<Mouse>/scroll/up", "<Gamepad>/rightShoulder", "<Gamepad>/dpad/right", "<Gamepad>/dpad/up");
            _toolPrevSupplemental = CreateSupplementalAction("ToolPrevSupplemental", "<Mouse>/scroll/down", "<Gamepad>/leftShoulder", "<Gamepad>/dpad/left", "<Gamepad>/dpad/down");
            _pauseSupplemental = CreateSupplementalAction("PauseSupplemental", "<Keyboard>/escape", "<Gamepad>/startButton");
            if (_tool1 == null)
                _tool1Supplemental = CreateSupplementalAction("Tool1", "<Keyboard>/1");
            if (_tool2 == null)
                _tool2Supplemental = CreateSupplementalAction("Tool2", "<Keyboard>/2");
            if (_tool3 == null)
                _tool3Supplemental = CreateSupplementalAction("Tool3", "<Keyboard>/3");
            if (_tool4 == null)
                _tool4Supplemental = CreateSupplementalAction("Tool4", "<Keyboard>/4");
            if (_tool5 == null)
                _tool5Supplemental = CreateSupplementalAction("Tool5", "<Keyboard>/5");
            _respawnSupplemental = CreateSupplementalAction("RespawnSupplemental", "<Keyboard>/y", "<Gamepad>/buttonNorth");
        }

        private InputAction CreateSupplementalAction(string name, params string[] paths)
        {
            var action = new InputAction(name, InputActionType.Button);
            if (paths != null)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(paths[i]))
                        continue;
                    action.AddBinding(paths[i]);
                }
            }
            _supplementalActions.Add(action);
            return action;
        }

        private void ClearSupplementalActions()
        {
            _jumpSupplemental = null;
            _primarySupplemental = null;
            _secondarySupplemental = null;
            _dropSupplemental = null;
            _toolNextSupplemental = null;
            _toolPrevSupplemental = null;
            _pauseSupplemental = null;
            _tool1Supplemental = null;
            _tool2Supplemental = null;
            _tool3Supplemental = null;
            _tool4Supplemental = null;
            _tool5Supplemental = null;
            _respawnSupplemental = null;
        }

        private void EnableSupplementalActions()
        {
            for (int i = 0; i < _supplementalActions.Count; i++)
            {
                if (_supplementalActions[i] != null && !_supplementalActions[i].enabled)
                    _supplementalActions[i].Enable();
            }
        }

        private void DisableSupplementalActions()
        {
            for (int i = 0; i < _supplementalActions.Count; i++)
            {
                if (_supplementalActions[i] != null && _supplementalActions[i].enabled)
                    _supplementalActions[i].Disable();
            }
        }

        private void EnableResolvedMapIfOwned()
        {
            if (_ownsResolvedMapEnable && _resolvedMap != null && !_resolvedMap.enabled)
                _resolvedMap.Enable();
        }

        private void DisableResolvedMapIfOwned()
        {
            if (_ownsResolvedMapEnable && _resolvedMap != null && _resolvedMap.enabled)
                _resolvedMap.Disable();
        }

        private void EnableFallbackActionsIfAny()
        {
            if (_runtimeFallbackMap != null && !_runtimeFallbackMap.enabled)
                _runtimeFallbackMap.Enable();
        }

        private void DisableFallbackActionsIfAny()
        {
            if (_runtimeFallbackMap != null && _runtimeFallbackMap.enabled)
                _runtimeFallbackMap.Disable();
        }

        private float ComputeTurnDelta(float axis, float dt)
        {
            if (Mathf.Abs(axis) < 0.0001f)
            {
                _snapReady = true;
                return 0f;
            }

            if (!useSnapTurn)
                return axis * Mathf.Max(0f, smoothTurnDegreesPerSecond) * dt;

            if (Mathf.Abs(axis) < snapTurnDeadzone || !_snapReady)
                return 0f;

            _snapReady = false;
            return Mathf.Sign(axis) * snapTurnDegrees;
        }

        private void UpdatePromptDevice(Vector2 moveInput, Vector2 rawLook, float turnAxis)
        {
            InputDevice device;
            if (TryGetTriggeredDevice(_primary, out device) ||
                TryGetTriggeredDevice(_secondary, out device) ||
                TryGetTriggeredDevice(_drop, out device) ||
                TryGetTriggeredDevice(_jump, out device) ||
                TryGetTriggeredDevice(_sprint, out device) ||
                TryGetTriggeredDevice(_toolNext, out device) ||
                TryGetTriggeredDevice(_toolPrev, out device) ||
                TryGetTriggeredDevice(_pause, out device) ||
                TryGetTriggeredDevice(_tool1, out device) ||
                TryGetTriggeredDevice(_tool2, out device) ||
                TryGetTriggeredDevice(_tool3, out device) ||
                TryGetTriggeredDevice(_tool4, out device) ||
                TryGetTriggeredDevice(_tool5, out device) ||
                TryGetTriggeredDevice(_respawn, out device) ||
                TryGetTriggeredDevice(_primarySupplemental, out device) ||
                TryGetTriggeredDevice(_secondarySupplemental, out device) ||
                TryGetTriggeredDevice(_dropSupplemental, out device) ||
                TryGetTriggeredDevice(_jumpSupplemental, out device) ||
                TryGetTriggeredDevice(_toolNextSupplemental, out device) ||
                TryGetTriggeredDevice(_toolPrevSupplemental, out device) ||
                TryGetTriggeredDevice(_pauseSupplemental, out device) ||
                TryGetTriggeredDevice(_tool1Supplemental, out device) ||
                TryGetTriggeredDevice(_tool2Supplemental, out device) ||
                TryGetTriggeredDevice(_tool3Supplemental, out device) ||
                TryGetTriggeredDevice(_tool4Supplemental, out device) ||
                TryGetTriggeredDevice(_tool5Supplemental, out device) ||
                TryGetTriggeredDevice(_respawnSupplemental, out device) ||
                TryGetValueDevice(_look, rawLook.sqrMagnitude > 0.0004f, out device) ||
                TryGetValueDevice(_move, moveInput.sqrMagnitude > 0.0004f, out device) ||
                TryGetValueDevice(_turn, Mathf.Abs(turnAxis) > 0.0004f, out device))
            {
                _useGamepadPrompts = device is Gamepad || device is Joystick;
            }
        }

        private static bool WasPressed(InputAction a) => a != null && a.WasPressedThisFrame();
        private static bool WasPressedAny(InputAction a, InputAction b) => WasPressed(a) || WasPressed(b);
        private static bool IsPressed(InputAction a) => a != null && a.IsPressed();
        private static Vector2 ReadVector2(InputAction a) => a != null ? a.ReadValue<Vector2>() : Vector2.zero;
        private static float ReadFloat(InputAction a) => a != null ? a.ReadValue<float>() : 0f;

        private static bool TryGetTriggeredDevice(InputAction action, out InputDevice device)
        {
            device = action != null ? action.activeControl?.device : null;
            return action != null && action.triggered && device != null;
        }

        private static bool TryGetValueDevice(InputAction action, bool hasValue, out InputDevice device)
        {
            device = action != null ? action.activeControl?.device : null;
            return action != null && hasValue && device != null;
        }

        private static bool HasBindingPath(InputAction a, string pathFragment)
        {
            if (a == null || string.IsNullOrEmpty(pathFragment))
                return false;
            string needle = pathFragment.ToLowerInvariant();
            var bindings = a.bindings;
            for (int i = 0; i < bindings.Count; i++)
            {
                string p = bindings[i].effectivePath;
                if (string.IsNullOrEmpty(p))
                    p = bindings[i].path;
                if (string.IsNullOrEmpty(p))
                    continue;
                if (p.ToLowerInvariant().Contains(needle))
                    return true;
            }
            return false;
        }

        private void SetCursorLocked(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked || !hideCursorWhenLocked;
        }
    }
}
