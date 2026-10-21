using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Makes the web (WebGL) build that's published to GitHub Pages.
// From the editor: Tools > Build Web Version. From the command line:
//   Unity.exe -batchmode -quit -projectPath <project> -buildTarget WebGL -executeMethod WebBuild.Build -webBuildPath <folder>
// GitHub Pages can't serve Unity's compressed (Brotli/Gzip) files properly, so compression is switched off.
public static class WebBuild
{
    private const string DefaultPath = "../2D-Game-WebBuild";

    [MenuItem("Tools/Build Web Version")]
    public static void Build()
    {
        string path = DefaultPath;
        string[] args = System.Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "-webBuildPath")
            {
                path = args[i + 1];
            }
        }

        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;

        string[] scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray();
        BuildPlayerOptions options = new BuildPlayerOptions
        {
            scenes = scenes,
            locationPathName = path,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        };
        BuildReport report = BuildPipeline.BuildPlayer(options);
        Debug.Log($"WebBuild: {report.summary.result}, {report.summary.totalSize / (1024f * 1024f):0.0} MB, scenes: {string.Join(", ", scenes)}, output: {path}");
        if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
        {
            EditorApplication.Exit(1);
        }
    }
}
