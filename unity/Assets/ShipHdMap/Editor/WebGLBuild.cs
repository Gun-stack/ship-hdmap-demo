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
            IncludeShader("Standard"); IncludeShader("Unlit/Texture"); IncludeShader("Unlit/Color"); // no scene material references these; halo/overlay use Shader.Find at runtime
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

        /// Adds shader to GraphicsSettings.m_AlwaysIncludedShaders (if not already present) so the WebGL
        /// build keeps it even though no scene material references it directly (e.g. Shader.Find at runtime).
        static void IncludeShader(string name)
        {
            var shader = Shader.Find(name);
            if (shader == null) { Debug.LogWarning($"[WebGLBuild] Shader.Find(\"{name}\") returned null; cannot include it."); return; }
            var so = new SerializedObject(Unsupported.GetSerializedAssetInterfaceSingleton("GraphicsSettings"));
            var list = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < list.arraySize; i++) if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader) { Debug.Log($"[WebGLBuild] shader already included: {shader.name}"); return; }
            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[WebGLBuild] added always-included shader: {shader.name}");
        }
    }
}
