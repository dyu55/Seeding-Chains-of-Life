using UnityEngine;

namespace SCoL.InputLayer
{
    /// <summary>
    /// Optional explicit scene installer. Use when you want deterministic references
    /// instead of runtime auto-discovery.
    /// </summary>
    [DisallowMultipleComponent]
    public class GameplayInputInstaller : MonoBehaviour
    {
        public MonoBehaviour playerInputBehaviour;
        public MonoBehaviour aimProviderBehaviour;

        private void Awake()
        {
            var player = playerInputBehaviour as IPlayerInput;
            var aim = aimProviderBehaviour as IAimProvider;
            if (player != null && aim != null)
                GameplayInputFacade.Bind(player, aim);
        }
    }
}

