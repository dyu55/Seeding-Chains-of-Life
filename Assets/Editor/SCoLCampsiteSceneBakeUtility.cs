using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using SCoL.Settlement;

public static class SCoLCampsiteSceneBakeUtility
{
    const string ScenePath = "Assets/Scenes/RecoveredScene.unity";

    public static void BakeRecoveredSceneCampsite()
    {
        var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        if (!scene.IsValid())
        {
            Debug.LogError("[SCoLCampsiteSceneBakeUtility] Failed to open RecoveredScene.");
            return;
        }

        var anchor = Object.FindFirstObjectByType<SCoLCampsiteSceneAnchor>();
        if (anchor == null)
        {
            Debug.LogError("[SCoLCampsiteSceneBakeUtility] No SCoLCampsiteSceneAnchor found in RecoveredScene.");
            return;
        }

        anchor.EnsureVisualNow();
        var visual = anchor.GetVisualRoot();
        if (visual == null)
        {
            Debug.LogError("[SCoLCampsiteSceneBakeUtility] Campsite visual was not created.");
            return;
        }

        EditorUtility.SetDirty(anchor);
        EditorUtility.SetDirty(visual);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Debug.Log($"[SCoLCampsiteSceneBakeUtility] Baked campsite visual '{visual.name}' into {ScenePath}.");
    }
}
