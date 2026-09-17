using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class ScenarioRunTests
    {
        GameObject go; readonly List<(string name, string json)> emitted = new();
        [TearDown] public void Cleanup()
        {
            Time.timeScale = 1f;
            foreach (var rt in Object.FindObjectsByType<MapRuntime>(FindObjectsSortMode.None)) if (rt.Quay) Object.DestroyImmediate(rt.Quay);
            if (go) Object.DestroyImmediate(go);
            if (go2) Object.DestroyImmediate(go2);
            var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship);
        }
        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));
        static string FilledFixture() => Fixture().Replace("\"status\": \"empty\"", "\"status\": \"filled\"");
        const string NoNoise = "{\"sigma_r\":0,\"sigma_theta\":0,\"sigma_alpha\":0,\"sigma_gps\":0}";

        static string PoseJson(double heel) =>
            $"{{\"draft_fwd_m\":8.1,\"draft_aft_m\":8.6,\"heel_deg\":{heel},\"lpp_m\":120,\"tide_m\":0,\"quay_z_m\":3.5,\"ramp\":{{\"id\":\"RAMP-STERN\",\"angle_deg\":2.87,\"state\":\"deployed\"}}}}";

        MapRuntime NewRuntime(string fixture)
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Emit += (n, j) => emitted.Add((n, j));
            rt.Load(fixture); rt.SetNoise(NoNoise);
            return rt;
        }

        GameObject go2;
        /// A second runtime in one test (the first one's Quay/Map stay alive until TearDown).
        MapRuntime NewRuntimeSecond(string fixture)
        {
            go2 = new GameObject("Map2"); var rt = go2.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(fixture); rt.SetNoise(NoNoise);
            return rt;
        }

        /// Steps until an event with this name arrives; fails after maxSteps.
        static string RunUntil(MapRuntime rt, List<(string name, string json)> log, string name, int maxSteps = 4000, float step = 0.05f)
        {
            int start = log.Count;
            for (int i = 0; i < maxSteps; i++)
            {
                rt.Step(step);
                var hit = log.Skip(start).FirstOrDefault(e => e.name == name);
                if (hit.name != null) return hit.json;
            }
            Assert.Fail($"no {name} within {maxSteps} steps (phase {rt.ScenarioPhase}, s {rt.Vehicle.s:F1})"); return null;
        }

        [Test]
        public void LoadStartsOnTheQuayAndGpsErrorOffsetsTheRampEntry()
        {
            // The vehicle is told where the berth's ramp is; GPS is what makes it miss. sigma_gps 0 enters dead centre.
            var rt = NewRuntime(Fixture());
            rt.SetPose(PoseJson(0));
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnQuay));
            Assert.That(rt.Vehicle.transform.parent, Is.Null, "on the quay the vehicle drives in the Quay Frame");
            Assert.That(rt.Vehicle.Truth.x, Is.EqualTo(ScenarioPlanner.QuaySpawn[0]).Within(1e-6));
            double centred = rt.Vehicle.path[rt.Vehicle.path.Length - 1][1];

            var noisy = NewRuntimeSecond(Fixture());
            noisy.SetNoise("{\"sigma_r\":0,\"sigma_theta\":0,\"sigma_alpha\":0,\"sigma_gps\":2.0}");
            noisy.SetPose(PoseJson(0));
            noisy.StartScenario("{\"mode\":\"load\"}");
            double offset = noisy.Vehicle.path[noisy.Vehicle.path.Length - 1][1];
            Assert.That(System.Math.Abs(offset - centred), Is.GreaterThan(0.3), "a 2 m GPS sigma must show up as a lateral miss");
        }

        [Test]
        [Ignore("M5b Task 5 까지 보류")]
        public void LoadScenarioParksFirstSlotEmitsAndSpawnsNextVehicle()
        {
            var rt = NewRuntime(Fixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"start\"") && e.json.Contains("\"load\"")));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"target\"") && e.json.Contains("PS-D3-001")));
            Assert.That(emitted.First(e => e.name == "onScenario").json, Does.Contain("\"event\":"));   // pins the wire key ScenarioEvt.evt maps to

            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnQuay));
            Assert.That(rt.Vehicle.speedMps, Is.EqualTo(10 / 3.6).Within(1e-9));

            var json = RunUntil(rt, emitted, "onSlotFilled");
            var evt = MapJson.Parse<SlotFilledEvt>(json);
            Assert.That(evt.slot_id, Is.EqualTo("PS-D3-001"));
            Assert.That(evt.status, Is.EqualTo("filled"));                       // zero noise → the estimate equals the truth
            Assert.That(System.Math.Abs(evt.err_lat.Value), Is.LessThan(0.15)); Assert.That(System.Math.Abs(evt.err_lon.Value), Is.LessThan(0.30));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"leave_lane\"")));
            Assert.That(rt.CurrentMap.parking_slots.First(s => s.id == "PS-D3-001").status, Is.EqualTo("filled"));
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001"), Is.Not.Null);
            var fill = rt.transform.Find("Overlay/D3/PS-D3-001/Fill").GetComponent<MeshRenderer>().sharedMaterial.color;
            Assert.That(fill.b, Is.EqualTo(1f).Within(1e-3));                    // "filled" blue
            // next vehicle already on the lane, heading for PS-D3-002
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnLane));
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-002"));
            Assert.That(rt.Vehicle.s, Is.LessThan(1.0));
        }

        [Test]
        [Ignore("M5b Task 5 까지 보류")]
        public void HeadingEstimationErrorReachesTheParkingResult()
        {
            // LandmarkSensor noise is seeded (SensorNoise.seed), so this run is deterministic. The point of this test is
            // that a nonzero heading estimation error at the lane-exit moment must show up in err_heading -- before the
            // ToTruthFrame fix, StepScenario only translated the planned path, so the vehicle always finished on the
            // target heading and err_heading was structurally 0 no matter how noisy the estimate was.
            var rt = NewRuntime(Fixture());
            rt.SetNoise("{\"sigma_r\":0.5,\"sigma_theta\":3,\"sigma_alpha\":6,\"sigma_gps\":0}");
            rt.StartScenario("{\"mode\":\"load\"}");
            var json = RunUntil(rt, emitted, "onSlotFilled");
            var evt = MapJson.Parse<SlotFilledEvt>(json);
            Assert.That(System.Math.Abs(evt.err_heading.Value), Is.GreaterThan(0.05));
        }

        [Test]
        public void LoadFinishesWhenNoEmptySlotRemains()
        {
            var rt = NewRuntime(FilledFixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Idle));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"finished\"") && e.json.Contains("no_empty_slot")));
        }

        [Test]
        [Ignore("M5b Task 5 까지 보류")]
        public void FinishHidesTheVehicleAfterAllSlotsGetFilled()
        {
            var rt = NewRuntime(Fixture());   // both slots empty
            rt.StartScenario("{\"mode\":\"load\"}");
            RunUntil(rt, emitted, "onSlotFilled");   // PS-D3-001 filled, next vehicle spawned
            RunUntil(rt, emitted, "onSlotFilled");   // PS-D3-002 filled, no empty slot left -> Finish
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"finished\"") && e.json.Contains("no_empty_slot")));
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Idle));
            Assert.That(rt.Vehicle.gameObject.activeSelf, Is.False);

            rt.StartScenario("{\"mode\":\"unload\"}");   // starting a new scenario after a finish still works
            Assert.That(rt.Vehicle.gameObject.activeSelf, Is.True);
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Departing));
        }

        [Test]
        public void LoadRestoresParkedCarsAndUnloadEmptiesInReverse()
        {
            var rt = NewRuntime(FilledFixture());
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001"), Is.Not.Null);
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-002"), Is.Not.Null);
            rt.StartScenario("{\"mode\":\"unload\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Departing));
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-002"));                 // highest sequence first
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-002"), Is.Null);   // the car became the vehicle
            var json = RunUntil(rt, emitted, "onSlotFilled");
            var evt = MapJson.Parse<SlotFilledEvt>(json);
            Assert.That(evt.slot_id, Is.EqualTo("PS-D3-002")); Assert.That(evt.status, Is.EqualTo("empty")); Assert.That(evt.err_lat, Is.Null);
            Assert.That(json, Does.Not.Contain("err_lat"));
            Assert.That(rt.CurrentMap.parking_slots.First(s => s.id == "PS-D3-002").status, Is.EqualTo("empty"));
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-001"));
        }

        [Test]
        public void EditModeStopsTheScenarioAndResetsTimeScale()
        {
            var rt = NewRuntime(Fixture());
            rt.SetTimeScale("{\"scale\":5}");
            Assert.That(Time.timeScale, Is.EqualTo(5f).Within(1e-6f));
            rt.StartScenario("{\"mode\":\"load\"}");
            rt.Step(0.05f);
            rt.SetMode("edit");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Idle));
            Assert.That(rt.Vehicle.running, Is.False);
            Assert.That(rt.Vehicle.gameObject.activeSelf, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001"), Is.Null);   // nothing was parked yet
        }

        [Test]
        public void SetDeckHidesParkedCarsWithTheirDeck()
        {
            var rt = NewRuntime(FilledFixture());
            rt.SetDeck("D1");
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001").GetComponent<Renderer>().enabled, Is.False);
            rt.SetDeck("all");
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001").GetComponent<Renderer>().enabled, Is.True);
        }

        [Test]
        public void RestoredParkedBoxesInheritTheActiveDeckFilter()
        {
            var rt = NewRuntime(Fixture());
            rt.SetDeck("D1");
            rt.Load(FilledFixture());
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001").GetComponent<Renderer>().enabled, Is.False);
            rt.SetDeck("all");
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001").GetComponent<Renderer>().enabled, Is.True);
        }

        [Test]
        [Ignore("M5b Task 5 까지 보류")]
        public void CoarseStepsDoNotOvershootTheExitIntoTheParkingError()
        {
            // At a high time scale one Step covers several metres; without VehicleController.Rewind the plan's origin
            // lands metres past the lane exit and that overshoot shows up directly as err_lon (needs_adjust).
            var rt = NewRuntime(Fixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            var json = RunUntil(rt, emitted, "onSlotFilled", step: 2f);
            var evt = MapJson.Parse<SlotFilledEvt>(json);
            Assert.That(evt.status, Is.EqualTo("filled"));
            Assert.That(System.Math.Abs(evt.err_lon.Value), Is.LessThanOrEqualTo(0.30));
        }

        [Test]
        public void UnresolvableLaneSkipsTheSlotInsteadOfEndingTheRun()
        {
            var f = Fixture(); int i = f.IndexOf("\"access_lane_id\": \"A2-D3-0001\"");
            f = f.Substring(0, i) + "\"access_lane_id\": \"A2-NOPE\"" + f.Substring(i + "\"access_lane_id\": \"A2-D3-0001\"".Length);
            var rt = NewRuntime(f);   // PS-D3-001's lane no longer resolves
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-002"));
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnQuay));   // a ramp is in the map, so load starts on the quay (M5b)
        }
    }
}
