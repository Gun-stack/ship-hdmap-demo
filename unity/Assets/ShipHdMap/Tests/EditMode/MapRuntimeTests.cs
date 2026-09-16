using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class MapRuntimeTests
    {
        GameObject go;
        [TearDown] public void Cleanup() { if (go) Object.DestroyImmediate(go); var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship); }

        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));

        [Test]
        public void LoadSpawnsLandmarksAndBuildsMap()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            rt.Load(Fixture());
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(19));
            Assert.That(rt.MapRefs.ContainsKey("LM-0003"));
            Assert.That(rt.MapRefs["LM-0001"].phiRad, Is.EqualTo(System.Math.PI / 2).Within(1e-6)); // normal +y
            Assert.That(rt.CurrentMap.parking_slots.Count, Is.EqualTo(2));
        }

        [Test]
        public void LoadTwiceReplacesLandmarks()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture()); rt.Load(Fixture());
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(19));
        }

        [Test]
        public void SetNoiseParsesAndApplies()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.SetNoise("{\"sigma_r\":0.5,\"sigma_theta\":2,\"sigma_alpha\":3,\"sigma_gps\":0.5}");
            Assert.That(rt.Sensor.noise.sigmaR, Is.EqualTo(0.5));
            Assert.That(rt.Sensor.noise.sigmaThetaRad, Is.EqualTo(2 * System.Math.PI / 180).Within(1e-9));
        }

        [Test]
        public void DeleteRemovesMarkerAndMapRef()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture());
            int before = rt.LandmarksRoot.childCount;
            rt.Delete("LM-0003");
            Assert.That(rt.MapRefs.ContainsKey("LM-0003"), Is.False);
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(before - 1));
            rt.Delete("LM-NOPE"); // no throw
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(before - 1));
        }

        [Test]
        public void SelectHighlightsWithoutEmitting()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            int emitted = 0; rt.Emit += (_, _) => emitted++;
            rt.Select("LM-0003");
            var halo3 = rt.LandmarksRoot.Find("LM-0003/Halo");
            Assert.That(halo3, Is.Not.Null); Assert.That(halo3.gameObject.activeSelf, Is.True);
            rt.Select("LM-0004");
            Assert.That(halo3.gameObject.activeSelf, Is.False);
            Assert.That(rt.LandmarksRoot.Find("LM-0004/Halo").gameObject.activeSelf, Is.True);
            rt.Select("LM-NOPE");
            Assert.That(rt.LandmarksRoot.Find("LM-0004/Halo").gameObject.activeSelf, Is.False);
            Assert.That(emitted, Is.EqualTo(0));
        }

        [Test]
        public void FeatureCreatedEventCarriesMountedOn()
        {
            var json = MapJson.Serialize(new FeatureCreatedEvt { tempId = "LM-0002", layer = "LM", x = 1, y = 2, z = 3, deck = "D3", mounted_on = "C-PILLAR-D3-001" });
            Assert.That(json, Does.Contain("mounted_on")); Assert.That(json, Does.Contain("C-PILLAR-D3-001"));
        }

        [Test]
        public void MarkerMovedUpdatesMapRefAndEmits()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            string name = null, json = null; rt.Emit += (n, j) => { name = n; json = j; };
            var lm = rt.LandmarksRoot.Find("LM-0001").GetComponent<LandmarkMarker>();
            lm.MoveTo(ShipFrame.ToUnity(30, 6, 11.8), ShipFrame.ToUnity(0, -1, 0), "D3", "C-PILLAR-D3-009");
            rt.OnMarkerMoved(lm);
            Assert.That(rt.MapRefs["LM-0001"].mx, Is.EqualTo(30).Within(1e-3));
            Assert.That(rt.MapRefs["LM-0001"].phiRad, Is.EqualTo(-System.Math.PI / 2).Within(1e-3));
            Assert.That(name, Is.EqualTo("onFeatureMoved"));
            Assert.That(json, Does.Contain("C-PILLAR-D3-009")); Assert.That(json, Does.Contain("normal"));
        }
    }
}
