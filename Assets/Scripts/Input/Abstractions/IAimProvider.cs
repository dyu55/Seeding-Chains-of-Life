using UnityEngine;

namespace SCoL.InputLayer
{
    /// <summary>
    /// Centralized aim-ray provider for gameplay raycasts.
    /// </summary>
    public interface IAimProvider
    {
        bool TryGetAimRay(Camera fallbackCamera, out Ray ray);
        Transform CurrentAimOrigin { get; }
    }
}

