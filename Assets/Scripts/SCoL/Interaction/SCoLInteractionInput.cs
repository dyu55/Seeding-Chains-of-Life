using UnityEngine;
using SCoL.InputLayer;
using UnityEngine.InputSystem;

namespace SCoL.Interaction
{
    /// <summary>
    /// Unified interaction input entry point for gameplay scripts.
    /// Keeps callers agnostic to FPS vs VR control sources.
    /// </summary>
    public static class SCoLInteractionInput
    {
        public static Vector2 Move() => GameplayInputFacade.Player != null ? GameplayInputFacade.Player.Move : Vector2.zero;
        public static Vector2 LookDelta() => GameplayInputFacade.Player != null ? GameplayInputFacade.Player.Look : Vector2.zero;
        public static float TurnDegreesThisFrame() => GameplayInputFacade.Player != null ? GameplayInputFacade.Player.TurnDegreesThisFrame : 0f;
        public static bool JumpPressed()
        {
            return (GameplayInputFacade.Player != null && GameplayInputFacade.Player.JumpPressedThisFrame)
                   || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
        }
        public static bool SprintHeld() => GameplayInputFacade.Player != null && GameplayInputFacade.Player.SprintHeld;

        static bool KeyThisFrame(Key key)
        {
            var keyboard = Keyboard.current;
            return keyboard != null && keyboard[key].wasPressedThisFrame;
        }

        static bool MouseButtonThisFrame(int button)
        {
            var mouse = Mouse.current;
            if (mouse == null)
                return false;

            return button switch
            {
                0 => mouse.leftButton.wasPressedThisFrame,
                1 => mouse.rightButton.wasPressedThisFrame,
                2 => mouse.middleButton.wasPressedThisFrame,
                _ => false
            };
        }

        public static bool PrimaryPressed()
        {
            return (GameplayInputFacade.Player != null && GameplayInputFacade.Player.PrimaryPressedThisFrame)
                   || MouseButtonThisFrame(0);
        }

        public static bool SecondaryPressed()
        {
            return (GameplayInputFacade.Player != null && GameplayInputFacade.Player.SecondaryPressedThisFrame)
                   || MouseButtonThisFrame(1);
        }

        public static bool DropPressed()
        {
            return (GameplayInputFacade.Player != null && GameplayInputFacade.Player.DropPressedThisFrame)
                   || KeyThisFrame(Key.Q);
        }

        public static bool ToolNextPressed()
        {
            return GameplayInputFacade.Player != null && GameplayInputFacade.Player.ToolNextPressedThisFrame;
        }

        public static bool ToolPrevPressed()
        {
            return GameplayInputFacade.Player != null && GameplayInputFacade.Player.ToolPrevPressedThisFrame;
        }

        public static bool PausePressed()
        {
            return (GameplayInputFacade.Player != null && GameplayInputFacade.Player.PausePressedThisFrame)
                   || KeyThisFrame(Key.Escape);
        }

        public static bool RespawnPressed()
        {
            return (GameplayInputFacade.Player != null && GameplayInputFacade.Player.RespawnPressedThisFrame)
                   || KeyThisFrame(Key.Y);
        }

        public static bool ChestPressed()
        {
            return KeyThisFrame(Key.F)
                   || (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame);
        }

        public static bool ToolSlotPressed(int slot)
        {
            var p = GameplayInputFacade.Player;
            return slot switch
            {
                1 => (p != null && p.ToolSlot1PressedThisFrame) || KeyThisFrame(Key.Digit1),
                2 => (p != null && p.ToolSlot2PressedThisFrame) || KeyThisFrame(Key.Digit2),
                3 => (p != null && p.ToolSlot3PressedThisFrame) || KeyThisFrame(Key.Digit3),
                4 => (p != null && p.ToolSlot4PressedThisFrame) || KeyThisFrame(Key.Digit4),
                5 => (p != null && p.ToolSlot5PressedThisFrame) || KeyThisFrame(Key.Digit5),
                _ => false
            };
        }

        public static bool TryGetAimRay(Camera fallbackCamera, out Ray ray)
        {
            var aim = GameplayInputFacade.Aim;
            if (aim != null && aim.TryGetAimRay(fallbackCamera, out ray))
                return true;
            ray = default;
            return false;
        }
    }
}
