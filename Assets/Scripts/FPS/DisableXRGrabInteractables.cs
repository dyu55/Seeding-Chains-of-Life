using UnityEngine;

#if UNITY_XR_INTERACTION_TOOLKIT
using UnityEngine.XR.Interaction.Toolkit.Interactables;
#endif

/// <summary>
/// FPS-only helper: strips XRGrabInteractable in loaded scenes so XR grab logic can't run.
/// (No scene wiring required.)
/// </summary>
public static class DisableXRGrabInteractables
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StripXRGrabInteractables()
    {
#if UNITY_XR_INTERACTION_TOOLKIT
        const bool destroyInsteadOfDisable = true;

        var grabs = Object.FindObjectsByType<XRGrabInteractable>(FindObjectsSortMode.None);
        if (grabs == null || grabs.Length == 0) return;

        foreach (var g in grabs)
        {
            if (g == null) continue;
            if (destroyInsteadOfDisable)
                Object.Destroy(g);
            else
                g.enabled = false;
        }
#endif
    }
}
