using UnityEngine;
using SCoL.InputLayer;

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
        public static bool JumpPressed() => GameplayInputFacade.Player != null && GameplayInputFacade.Player.JumpPressedThisFrame;
        public static bool SprintHeld() => GameplayInputFacade.Player != null && GameplayInputFacade.Player.SprintHeld;

        public static bool PrimaryPressed()
        {
            return GameplayInputFacade.Player != null && GameplayInputFacade.Player.PrimaryPressedThisFrame;
        }

        public static bool SecondaryPressed()
        {
            return GameplayInputFacade.Player != null && GameplayInputFacade.Player.SecondaryPressedThisFrame;
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
            return GameplayInputFacade.Player != null && GameplayInputFacade.Player.PausePressedThisFrame;
        }

        public static bool RespawnPressed()
        {
            return GameplayInputFacade.Player != null && GameplayInputFacade.Player.RespawnPressedThisFrame;
        }

        public static bool ToolSlotPressed(int slot)
        {
            var p = GameplayInputFacade.Player;
            if (p == null) return false;
            return slot switch
            {
                1 => p.ToolSlot1PressedThisFrame,
                2 => p.ToolSlot2PressedThisFrame,
                3 => p.ToolSlot3PressedThisFrame,
                4 => p.ToolSlot4PressedThisFrame,
                5 => p.ToolSlot5PressedThisFrame,
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
