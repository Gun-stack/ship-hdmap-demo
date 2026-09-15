using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShipHdMap.Editor
{
    /// Builds Assets/Scenes/Demo.unity in batch mode (no GUI needed). Menu also works interactively.
    public static class SceneSetup
    {
        const string ScenePath = "Assets/Scenes/Demo.unity";

        [MenuItem("ShipHdMap/Create Demo Scene")]
        public static void CreateDemoScene()
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            var cam = Camera.main.gameObject;
            cam.transform.position = new Vector3(60, 25, -40);
            cam.transform.rotation = Quaternion.Euler(20, -20, 0);
            cam.GetComponent<Camera>().farClipPlane = 500;
            cam.tag = "MainCamera";

            var map = new GameObject("Map");
            var rt = map.AddComponent<MapRuntime>();
            rt.cam = Camera.main;

            if (!Directory.Exists("Assets/Scenes")) AssetDatabase.CreateFolder("Assets", "Scenes");
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), ScenePath);

            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
