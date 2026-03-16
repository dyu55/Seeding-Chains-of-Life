using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace SCoL.XR
{
    /// <summary>
    /// Adds a ContinuousTurnProvider at runtime so the right joystick rotates the player.
    /// Attach to the same GameObject as the XR Origin (VR).
    /// </summary>
    [DefaultExecutionOrder(-50)] // Run before other locomotion scripts
    public class SCoLXRTurnSetup : MonoBehaviour
    {
        [Header("Turn Settings")]
        [Tooltip("Degrees per second for continuous turning.")]
        public float turnSpeed = 90f;

        private void Start()
        {
            // Only add if there's no turn provider already
            var existing = GetComponent<ContinuousTurnProvider>();
            if (existing != null)
            {
                Debug.Log("[SCoLXRTurnSetup] ContinuousTurnProvider already exists, skipping.");
                return;
            }

            var mediator = GetComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionMediator>();
            
            var turnProvider = gameObject.AddComponent<ContinuousTurnProvider>();
            turnProvider.turnSpeed = turnSpeed;
            
            // Wire up the mediator
            if (mediator != null)
            {
                // Use reflection or the serialized field to set the mediator
                var mediatorField = typeof(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider)
                    .GetField("m_Mediator", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                if (mediatorField != null)
                    mediatorField.SetValue(turnProvider, mediator);
            }

            Debug.Log($"[SCoLXRTurnSetup] Added ContinuousTurnProvider with turnSpeed={turnSpeed}");
        }
    }
}
