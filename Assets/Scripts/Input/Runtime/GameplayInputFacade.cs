using UnityEngine;
using UnityEngine.InputSystem;

namespace SCoL.InputLayer
{
    /// <summary>
    /// Static access point so gameplay code stays input-backend agnostic.
    /// </summary>
    public static class GameplayInputFacade
    {
        private static IPlayerInput _player;
        private static IAimProvider _aim;
        private static bool _bootstrapped;

        public static IPlayerInput Player
        {
            get
            {
                EnsureBootstrapped();
                if (_player == null || !IsAlive(_player as Object))
                    _player = FindPlayer();
                return _player;
            }
        }

        public static IAimProvider Aim
        {
            get
            {
                EnsureBootstrapped();
                if (_aim == null || !IsAlive(_aim as Object))
                    _aim = FindAim();
                return _aim;
            }
        }

        public static void Bind(IPlayerInput player, IAimProvider aim)
        {
            _player = player;
            _aim = aim;
            _bootstrapped = true;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void RuntimeBootstrap()
        {
            EnsureBootstrapped();
        }

        private static void EnsureBootstrapped()
        {
            if (_bootstrapped)
                return;
            _bootstrapped = true;

            _player = FindPlayer();
            _aim = FindAim();
            if (_player != null && _aim != null)
                return;

            var go = new GameObject("GameplayInput (Runtime)");
            Object.DontDestroyOnLoad(go);

            var input = go.AddComponent<InputSystemPlayerInput>();
            if (input.playerInput == null)
                input.playerInput = Object.FindFirstObjectByType<PlayerInput>();

            var aim = go.AddComponent<InteractionAimProvider>();
            aim.fpsCamera = Camera.main;

            _player = input;
            _aim = aim;
        }

        private static IPlayerInput FindPlayer()
        {
            var found = Object.FindFirstObjectByType<InputSystemPlayerInput>();
            return found;
        }

        private static IAimProvider FindAim()
        {
            var found = Object.FindFirstObjectByType<InteractionAimProvider>();
            return found;
        }

        private static bool IsAlive(Object o) => o != null;
    }
}

