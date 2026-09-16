using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    /// End-to-end EditMode check of the play loop: seed -> ship mesh -> fixture load -> scenario lane
    /// resolution -> vehicle placed at the lane start -> sensor sees at least one marker from there.
    public class ScenarioIntegrationTests
    {
        GameObject go;

        [TearDown]
        public void Cleanup()
        {
            if (go) Object.DestroyImmediate(go);
            var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship);
        }

        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));

        [Test]
        public void VehicleAtLaneStartSeesAtLeastOneLandmark()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            ShipMeshBuilder.Build(seed, new ShipParams());
            Physics.SyncTransforms();

            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            rt.Load(Fixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnLane));
            Assert.That(rt.Vehicle.Truth.x, Is.EqualTo(2).Within(1e-6));   // lane A2-D3-0001 starts at (2, 0)

            var obs = rt.Sensor.Sense(rt.Vehicle.Truth, rt.MapRefs, id => rt.Markers.First(m => m.id == id).transform.position);
            Assert.That(obs.Count, Is.GreaterThanOrEqualTo(1));
        }
    }
}
