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
            PlayerSettings.stripEngineCode = false; // scene creates colliders (MeshCollider/CapsuleCollider) only at runtime, so engine stripping would drop them
            PlayerSettings.runInBackground = true; // embedded in the editor page; must keep simulating while the user works in side panels
            Debug.Log($"[WebGLBuild] stripEngineCode={PlayerSettings.stripEngineCode} runInBackground={PlayerSettings.runInBackground}");
            // Nothing is force-included any more: under URP every runtime material is a clone of a template in
            // Resources/Materials (RenderSetup), and Resources keeps both the shader and the variants the templates use.
            // Force-including URP/Lit would drag in every one of its thousands of variants.
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

        /// Removes a shader from GraphicsSettings.m_AlwaysIncludedShaders. Before URP this file force-included Standard
        /// and two Unlit shaders for its runtime materials; RenderSetup takes them back out.
        public static void ExcludeShader(string name)
        {
            var shader = Shader.Find(name); if (shader == null) return;
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("GraphicsSettings"));
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = list.arraySize - 1; i >= 0; i--)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { list.GetArrayElementAtIndex(i).objectReferenceValue = null; list.DeleteArrayElementAtIndex(i); }
            so.ApplyModifiedProperties();
        }
    }
}
