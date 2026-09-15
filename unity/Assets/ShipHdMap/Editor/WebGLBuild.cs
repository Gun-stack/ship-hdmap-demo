using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShipHdMap.Editor
{
    public static class WebGLBuild
    {
        [MenuItem("ShipHdMap/Build WebGL")]
        public static void Build()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "web", "public", "unity"));
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; // dev server serves files as-is
            PlayerSettings.WebGL.decompressionFallback = false;
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Demo.unity" },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[WebGLBuild] {report.summary.result} -> {outDir} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime})");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
