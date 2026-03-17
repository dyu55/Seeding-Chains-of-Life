using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class CodexBuild
{
    private const string OutputDir = "Builds/macOS";
    private const string AppName = "ChainsOfLife.app";

    [MenuItem("Codex/Build/macOS Presentation Build")]
    public static void BuildMacPresentation()
    {
        Directory.CreateDirectory(OutputDir);

        var options = new BuildPlayerOptions
        {
            scenes = new[]
            {
                "Assets/Scenes/RecoveredScene.unity"
            },
            locationPathName = Path.Combine(OutputDir, AppName),
            target = BuildTarget.StandaloneOSX,
            options = BuildOptions.None
        };

        BuildReport report = BuildPipeline.BuildPlayer(options);
        BuildSummary summary = report.summary;

        if (summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"macOS build failed: {summary.result}");
            EditorApplication.Exit(1);
            return;
        }

        Debug.Log($"macOS build succeeded: {summary.outputPath}");
        EditorApplication.Exit(0);
    }
}
