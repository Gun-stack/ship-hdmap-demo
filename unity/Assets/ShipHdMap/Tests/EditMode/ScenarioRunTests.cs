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
        [TearDown] public void Cleanup() { Time.timeScale = 1f; if (go) Object.DestroyImmediate(go); var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship); }
        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));
        static string FilledFixture() => Fixture().Replace("\"status\": \"empty\"", "\"status\": \"filled\"");
        const string NoNoise = "{\"sigma_r\":0,\"sigma_theta\":0,\"sigma_alpha\":0,\"sigma_gps\":0}";

        MapRuntime NewRuntime(string fixture)
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Emit += (n, j) => emitted.Add((n, j));
            rt.Load(fixture); rt.SetNoise(NoNoise);
            return rt;
        }

        /// Steps until an event with this name arrives; fails after maxSteps.
        static string RunUntil(MapRuntime rt, List<(string name, string json)> log, string name, int maxSteps = 4000)
        {
            int start = log.Count;
            for (int i = 0; i < maxSteps; i++)
            {
                rt.Step(0.05f);
                var hit = log.Skip(start).FirstOrDefault(e => e.name == name);
                if (hit.name != null) return hit.json;
            }
            Assert.Fail($"no {name} within {maxSteps} steps (phase {rt.ScenarioPhase}, s {rt.Vehicle.s:F1})"); return null;
        }

        [Test]
        public void LoadScenarioParksFirstSlotEmitsAndSpawnsNextVehicle()
        {
            var rt = NewRuntime(Fixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"start\"") && e.json.Contains("\"load\"")));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"target\"") && e.json.Contains("PS-D3-001")));
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnLane));
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
        public void LoadFinishesWhenNoEmptySlotRemains()
        {
            var rt = NewRuntime(FilledFixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Idle));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"finished\"") && e.json.Contains("no_empty_slot")));
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
        public void UnresolvableLaneSkipsTheSlotInsteadOfEndingTheRun()
        {
            var f = Fixture(); int i = f.IndexOf("\"access_lane_id\": \"A2-D3-0001\"");
            f = f.Substring(0, i) + "\"access_lane_id\": \"A2-NOPE\"" + f.Substring(i + "\"access_lane_id\": \"A2-D3-0001\"".Length);
            var rt = NewRuntime(f);   // PS-D3-001's lane no longer resolves
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-002"));
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnLane));
        }
    }
}
