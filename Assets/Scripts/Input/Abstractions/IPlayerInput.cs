using UnityEngine;

namespace SCoL.InputLayer
{
    /// <summary>
    /// Unified gameplay input contract used by FPS today and VR later.
    /// </summary>
    public interface IPlayerInput
    {
        Vector2 Move { get; }
        Vector2 Look { get; }
        float TurnDegreesThisFrame { get; }

        bool JumpPressedThisFrame { get; }
        bool SprintHeld { get; }
        bool PrimaryPressedThisFrame { get; }
        bool SecondaryPressedThisFrame { get; }
        bool DropPressedThisFrame { get; }
        bool ToolNextPressedThisFrame { get; }
        bool ToolPrevPressedThisFrame { get; }
        bool PausePressedThisFrame { get; }

        bool ToolSlot1PressedThisFrame { get; }
        bool ToolSlot2PressedThisFrame { get; }
        bool ToolSlot3PressedThisFrame { get; }
        bool ToolSlot4PressedThisFrame { get; }
        bool ToolSlot5PressedThisFrame { get; }
        bool RespawnPressedThisFrame { get; }
        bool UseGamepadPrompts { get; }
    }
}
