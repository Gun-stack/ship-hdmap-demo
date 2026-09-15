using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class DemoSceneTests
    {
        // Opening a scene replaces the active EditMode test scene; leave a clean empty scene behind for tests that follow.
        [TearDown] public void Cleanup() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        [Test]
        public void DemoSceneHasMapRuntimeAndIsInBuildSettings()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Demo.unity", OpenSceneMode.Single);

            var map = GameObject.Find("Map");
            Assert.That(map, Is.Not.Null);
            var rt = map.GetComponent<MapRuntime>();
            Assert.That(rt, Is.Not.Null);
            Assert.That(rt.cam, Is.Not.Null);
            Assert.That(rt.cam.CompareTag("MainCamera"), Is.True);

            Assert.That(EditorBuildSettings.scenes.Length, Is.GreaterThan(0));
            Assert.That(EditorBuildSettings.scenes[0].path, Does.EndWith("Demo.unity"));
        }
    }
}
