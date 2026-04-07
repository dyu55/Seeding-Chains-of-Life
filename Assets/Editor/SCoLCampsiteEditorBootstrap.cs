using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SCoL.Settlement;

[InitializeOnLoad]
public static class SCoLCampsiteEditorBootstrap
{
    static SCoLCampsiteEditorBootstrap()
    {
        EditorApplication.delayCall += EnsureCampsiteVisualInOpenScene;
    }

    static void EnsureCampsiteVisualInOpenScene()
    {
        if (Application.isPlaying)
            return;

        var scene = EditorSceneManager.GetActiveScene();
        if (!scene.IsValid() || scene.path != "Assets/Scenes/RecoveredScene.unity")
            return;

        var anchor = Object.FindFirstObjectByType<SCoLCampsiteSceneAnchor>();
        if (anchor == null)
            return;

        anchor.EnsureVisualNow();
        if (anchor.GetVisualRoot() == null)
            return;

        EditorUtility.SetDirty(anchor.gameObject);
        EditorSceneManager.MarkSceneDirty(scene);
    }
}
