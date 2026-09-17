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
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(23));
            Assert.That(rt.MapRefs.ContainsKey("LM-0003"));
            Assert.That(rt.MapRefs["LM-0001"].phiRad, Is.EqualTo(System.Math.PI / 2).Within(1e-6)); // normal +y
            Assert.That(rt.CurrentMap.parking_slots.Count, Is.EqualTo(2));
        }

        [Test]
        public void LoadTwiceReplacesLandmarks()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture()); rt.Load(Fixture());
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(23));
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
        public void SelectFocusesOrbitOnlyFromTheWeb()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            var camGo = new GameObject("Cam"); var orbit = camGo.AddComponent<OrbitCamera>(); rt.Orbit = orbit;
            var startTarget = orbit.target;

            rt.Highlight("LM-0003");
            Assert.That(orbit.target, Is.EqualTo(startTarget)); // scene-side highlight must not move the camera

            rt.Select("LM-0003");
            var markerPos = rt.LandmarksRoot.Find("LM-0003").position;
            Assert.That(Vector3.Distance(orbit.target, markerPos), Is.LessThan(1e-3f));

            rt.SetMode("drive");
            var driveTarget = orbit.target;
            rt.Select("LM-0004");
            Assert.That(orbit.target, Is.EqualTo(driveTarget)); // drive mode never refocuses on a selection

            Object.DestroyImmediate(camGo);
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

        [Test]
        public void LoadBuildsOverlayLinesAndSetDeckFilters()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            var overlay = rt.transform.Find("Overlay"); Assert.That(overlay, Is.Not.Null);
            var lines = overlay.GetComponentsInChildren<LineRenderer>(true);
            Assert.That(lines.Length, Is.EqualTo(5)); // 3 lanes + 2 slots
            rt.SetDeck("D3");
            Assert.That(overlay.Find("D1/A2-D1-0001").GetComponent<LineRenderer>().enabled, Is.False);
            Assert.That(overlay.Find("D3/A2-D3-0001").GetComponent<LineRenderer>().enabled, Is.True);
            int fillMeshesBefore = CountFillMeshes();
            rt.Load(Fixture()); // rebuild keeps the filter and does not duplicate
            Assert.That(rt.transform.Find("Overlay").GetComponentsInChildren<LineRenderer>(true).Length, Is.EqualTo(5));
            Assert.That(rt.transform.Find("Overlay/D1/A2-D1-0001").GetComponent<LineRenderer>().enabled, Is.False);
            Assert.That(CountFillMeshes(), Is.EqualTo(fillMeshesBefore)); // the old overlay's fill meshes were destroyed, not leaked
        }

        static int CountFillMeshes()
        {
            int n = 0;
            foreach (var m in Resources.FindObjectsOfTypeAll<Mesh>()) if (m && m.name.EndsWith("~fill")) n++;
            return n;
        }

        [Test]
        public void SlotFillsFollowStatusAndSelection()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            var fill = rt.transform.Find("Overlay/D3/PS-D3-001/Fill");
            Assert.That(fill, Is.Not.Null);
            var mr = fill.GetComponent<MeshRenderer>();
            Assert.That(mr.sharedMaterial.color.a, Is.EqualTo(0.35f).Within(1e-3));
            rt.Select("PS-D3-001");
            Assert.That(mr.sharedMaterial.color.a, Is.EqualTo(0.75f).Within(1e-3));
            rt.Select("LM-0001");   // selecting a marker clears the slot highlight
            Assert.That(mr.sharedMaterial.color.a, Is.EqualTo(0.35f).Within(1e-3));
            Assert.That(rt.LandmarksRoot.Find("LM-0001/Halo").gameObject.activeSelf, Is.True);
            rt.Select("");          // empty id clears everything
            Assert.That(rt.LandmarksRoot.Find("LM-0001/Halo").gameObject.activeSelf, Is.False);
            rt.SetDeck("D1");
            Assert.That(mr.enabled, Is.False);
        }

        [Test]
        public void SetDeckHidesMarkersOfOtherDecks()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            rt.SetDeck("D1");
            Assert.That(rt.LandmarksRoot.Find("LM-0001").gameObject.activeSelf, Is.False); // fixture landmarks are all on D3
            rt.SetDeck("all");
            Assert.That(rt.LandmarksRoot.Find("LM-0001").gameObject.activeSelf, Is.True);
        }

        [Test]
        public void FeatureCreatedEventCarriesNormal()
        {
            var json = MapJson.Serialize(new FeatureCreatedEvt { tempId = "LM-0002", layer = "LM", x = 1, y = 2, z = 3, deck = "D3", mounted_on = "P", normal = new[] { 0.0, -1.0, 0.0 } });
            Assert.That(json, Does.Contain("normal"));
        }
    }
}
