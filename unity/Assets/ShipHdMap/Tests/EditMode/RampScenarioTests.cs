using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    /// M8: runs that leave the stern-ramp deck over the hoistable internal ramps.
    public class RampScenarioTests
    {
        GameObject go; readonly List<(string name, string json)> emitted = new();
        [TearDown] public void Cleanup()
        {
            Time.timeScale = 1f;
            foreach (var rt in Object.FindObjectsByType<MapRuntime>(FindObjectsSortMode.None))
            {
                if (rt.Quay) Object.DestroyImmediate(rt.Quay);
                if (rt.Vehicle) Object.DestroyImmediate(rt.Vehicle.gameObject);
            }
            if (go) Object.DestroyImmediate(go);
            var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship);
            emitted.Clear();
        }

        static VehicleMap FixtureMap() => MapJson.Parse<VehicleMap>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json"))));
        const string NoNoise = "{\"sigma_r\":0,\"sigma_theta\":0,\"sigma_alpha\":0,\"sigma_gps\":0}";
        const string Pose = "{\"draft_fwd_m\":8.1,\"draft_aft_m\":8.6,\"heel_deg\":0,\"lpp_m\":120,\"tide_m\":0,\"quay_z_m\":3.5,\"ramp\":{\"id\":\"RAMP-STERN\",\"angle_deg\":2.87,\"state\":\"deployed\"}}";

        static ParkingSlot Slot(string id, string deck, double z, double x, double y, int seq, string status = "empty") => new ParkingSlot
        {
            id = id, deck_id = deck, access_lane_id = $"A2-{deck}-0001", sequence_no = seq, status = status, vehicle_class = "passenger",
            target_pose = new TargetPose { x = x, y = y, heading_deg = 0 }, tolerance = new Tolerance { lat_m = 0.15, lon_m = 0.30, heading_deg = 2 },
            lashing_points = new List<string>(),
            polygon = new[] { new[] { x - 2.4, y - 0.925, z }, new[] { x + 2.4, y - 0.925, z }, new[] { x + 2.4, y + 0.925, z }, new[] { x - 2.4, y + 0.925, z }, new[] { x - 2.4, y - 0.925, z } },
        };

        /// The fixture's two D3 slots moved to D3's place in the global order, plus one slot on D1, one on D5, and one on
        /// RAMP-D3-D2's ground on D3 -- floor only once that ramp is stowed.
        static VehicleMap Ship(string status = "empty")
        {
            var m = FixtureMap();
            foreach (var s in m.parking_slots) { s.sequence_no += 4000; s.status = status; }
            m.parking_slots.Add(Slot("PS-D1-001", "D1", 5.4, 100, 2.925, 1, status));
            m.parking_slots.Add(Slot("PS-D5-001", "D5", 15.8, 100, 2.925, 1001, status));
            m.parking_slots.Add(Slot("PS-D3-090", "D3", 10.6, 25, 9.8, 4090, status));
            return m;
        }

        MapRuntime Run(VehicleMap m)
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Emit += (n, j) => emitted.Add((n, j));
            rt.Load(MapJson.Serialize(m)); rt.SetNoise(NoNoise); rt.SetPose(Pose);
            return rt;
        }

        static string RunUntil(MapRuntime rt, List<(string name, string json)> log, string name, System.Func<string, bool> where = null, int maxSteps = 12000, float step = 0.1f)
        {
            int start = log.Count;
            for (int i = 0; i < maxSteps; i++)
            {
                rt.Step(step);
                var hit = log.Skip(start).FirstOrDefault(e => e.name == name && (where == null || where(e.json)));
                if (hit.name != null) return hit.json;
            }
            Assert.Fail($"no {name} within {maxSteps} steps (phase {rt.ScenarioPhase}, s {rt.Vehicle.s:F1})"); return null;
        }

        IEnumerable<string> RampEvents() => emitted.Where(e => e.name == "onScenario" && e.json.Contains("\"ramp\"")).Select(e => MapJson.Parse<ScenarioEvt>(e.json).detail);

        [Test]
        public void TheFirstCarGoesToTheFarthestDeckOverTwoRampsWithoutAJump()
        {
            var rt = Run(Ship());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D1-001"), "far decks first");
            Assert.That(rt.RampStates["RAMP-D3-D2"], Is.EqualTo(RampPlanner.Deployed));
            Assert.That(rt.RampStates["RAMP-D2-D1"], Is.EqualTo(RampPlanner.Deployed));

            var phases = new List<MapRuntime.Phase>(); Vector3? last = null; float worst = 0;
            for (int i = 0; i < 12000 && !emitted.Any(e => e.name == "onSlotFilled"); i++)
            {
                rt.Step(0.1f);
                if (phases.Count == 0 || phases[^1] != rt.ScenarioPhase) phases.Add(rt.ScenarioPhase);
                var p = rt.Vehicle.transform.position;
                // Ship-Frame legs only: the quay leg moves in another frame, and one step at lane speed is 0.28 m
                if (last.HasValue && rt.ScenarioPhase is MapRuntime.Phase.OnRoute or MapRuntime.Phase.OnLane or MapRuntime.Phase.Parking)
                    worst = Mathf.Max(worst, (p - last.Value).magnitude);
                last = p;
            }
            var evt = MapJson.Parse<SlotFilledEvt>(emitted.First(e => e.name == "onSlotFilled").json);
            Assert.That(evt.slot_id, Is.EqualTo("PS-D1-001"));
            Assert.That(evt.status, Is.EqualTo("filled"));
            Assert.That(phases, Is.SupersetOf(new[] { MapRuntime.Phase.OnQuay, MapRuntime.Phase.OnRamp, MapRuntime.Phase.OnRoute, MapRuntime.Phase.OnLane, MapRuntime.Phase.Parking }));
            Assert.That(phases.IndexOf(MapRuntime.Phase.OnRoute), Is.LessThan(phases.IndexOf(MapRuntime.Phase.OnLane)));
            Assert.That(worst, Is.LessThan(0.6f), "no teleport between the route, the lane and the approach");
            var events = emitted.Where(e => e.name == "onScenario").Select(e => MapJson.Parse<ScenarioEvt>(e.json)).ToList();
            int route = events.FindIndex(e => e.evt == "route" && e.detail == "ROUTE-D1"), lane = events.FindIndex(e => e.evt == "lane" && e.detail == "D1");
            Assert.That(route, Is.GreaterThanOrEqualTo(0)); Assert.That(lane, Is.GreaterThan(route), "on the route, then on D1's lane");
            Assert.That(rt.transform.Find("Overlay/D1/PARKED-PS-D1-001"), Is.Not.Null);
        }

        [Test]
        public void RampsGoUpBehindEachFinishedDeckAndTheRampGroundFillsLast()
        {
            var rt = Run(Ship());
            rt.StartScenario("{\"mode\":\"load\"}");
            RunUntil(rt, emitted, "onSlotFilled", j => j.Contains("PS-D1-001"));
            // D1 done: nothing left beyond either port ramp (D2 has no slots here), so both go up as the D5 car is sent
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D5-001"));
            Assert.That(RampEvents(), Is.SupersetOf(new[] { "RAMP-D3-D2 stowed", "RAMP-D2-D1 stowed" }));
            Assert.That(rt.RampStates["RAMP-D3-D4"], Is.EqualTo(RampPlanner.Deployed));
            RunUntil(rt, emitted, "onSlotFilled", j => j.Contains("PS-D5-001"));
            Assert.That(RampEvents(), Is.SupersetOf(new[] { "RAMP-D3-D4 stowed", "RAMP-D4-D5 stowed" }));
            Assert.That(rt.RampStates.Values, Is.All.EqualTo(RampPlanner.Stowed));
            // D3 last, the slot on RAMP-D3-D2's ground included: it is floor now
            RunUntil(rt, emitted, "onSlotFilled", j => j.Contains("PS-D3-001"));
            RunUntil(rt, emitted, "onSlotFilled", j => j.Contains("PS-D3-002"));
            var onRamp = MapJson.Parse<SlotFilledEvt>(RunUntil(rt, emitted, "onSlotFilled", j => j.Contains("PS-D3-090")));
            Assert.That(onRamp.status, Is.Not.EqualTo("unreachable"));
        }

        [Test]
        public void UnloadDrivesBackAlongTheRouteAndLowersTheRampItNeeds()
        {
            var m = Ship("filled");
            var rt = Run(m);
            // a car stands on RAMP-D3-D2's ground, so after the Load that ramp can only be up
            Assert.That(rt.RampStates["RAMP-D3-D2"], Is.EqualTo(RampPlanner.Stowed));
            Assert.That(RampEvents(), Is.EquivalentTo(new[] { "RAMP-D3-D2 stowed", "RAMP-D2-D1 deployed", "RAMP-D3-D4 deployed", "RAMP-D4-D5 deployed" }),
                "the Load announces every ramp, or the web keeps showing this one down under the parked car");
            rt.StartScenario("{\"mode\":\"unload\"}");
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-090"), "last in, first out");
            RunUntil(rt, emitted, "onScenario", j => j.Contains("\"target\"") && j.Contains("PS-D5-001"));
            Assert.That(rt.RampStates["RAMP-D3-D4"], Is.EqualTo(RampPlanner.Deployed));
            RunUntil(rt, emitted, "onSlotFilled", j => j.Contains("PS-D5-001"));
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.ReturnRoute));
            RunUntil(rt, emitted, "onScenario", j => j.Contains("\"target\"") && j.Contains("PS-D1-001"));
            Assert.That(RampEvents(), Does.Contain("RAMP-D3-D2 deployed"), "PS-D3-090 left, so the ramp under it can come down");
            var gone = MapJson.Parse<SlotFilledEvt>(RunUntil(rt, emitted, "onSlotFilled", j => j.Contains("PS-D1-001")));
            Assert.That(gone.status, Is.EqualTo("empty"));
            for (int i = 0; i < 12000 && rt.ScenarioPhase == MapRuntime.Phase.ReturnRoute; i++) rt.Step(0.1f);
            Assert.That(new[] { MapRuntime.Phase.RampDown, MapRuntime.Phase.QuayOut, MapRuntime.Phase.Idle }, Does.Contain(rt.ScenarioPhase), "back on the stern-ramp deck and out");
        }

        [Test]
        public void ARampWillNotComeDownOntoAParkedCarNorMoveDuringARun()
        {
            var rt = Run(Ship("filled"));
            rt.SetRampState("{\"id\":\"RAMP-D3-D2\",\"state\":\"deployed\"}");
            Assert.That(rt.RampStates["RAMP-D3-D2"], Is.EqualTo(RampPlanner.Stowed));
            Assert.That(RampEvents().Last(), Is.EqualTo("RAMP-D3-D2 stowed"), "the answer is the state the scene has, not the one asked for");
            rt.SetRampState("{\"id\":\"RAMP-D3-D4\",\"state\":\"stowed\"}");
            Assert.That(rt.RampStates["RAMP-D3-D4"], Is.EqualTo(RampPlanner.Stowed));
            var shipRamp = rt.transform.Find("Ship");   // EditMode builds no hull, so the mesh side is covered in ShipMeshBuilderTests
            Assert.That(shipRamp, Is.Null);
        }

        [Test]
        public void EachDeckHasItsOwnPromise()
        {
            var rt = Run(Ship());
            rt.SetPrediction("{\"deck_id\":\"D1\",\"grid_m\":1,\"bbox\":[0,-12,120,12],\"cells\":[{\"x\":100.5,\"y\":2.5,\"s\":0.11}]}");
            rt.SetPrediction("{\"deck_id\":\"D3\",\"grid_m\":1,\"bbox\":[0,-12,120,12],\"cells\":[{\"x\":100.5,\"y\":2.5,\"s\":0.33}]}");
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.Prediction.SigmaAt(100.5, 2.5), Is.EqualTo(0.11).Within(1e-9), "the D1 car is judged against D1's map");
        }
    }

    public class RampPlannerTests
    {
        static VehicleMap Map() => MapJson.Parse<VehicleMap>(File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json"))));
        static ParkingSlot S(string id, string deck, double x, double y, string status) => new ParkingSlot { id = id, deck_id = deck, status = status, target_pose = new TargetPose { x = x, y = y } };

        [Test]
        public void NearDecksAreOnTheSternRampSide()
        {
            var m = Map();
            Assert.That(RampPlanner.SternDeck(m), Is.EqualTo("D3"));
            Assert.That(RampPlanner.NearDeck(m, m.ramps.Single(r => r.id == "RAMP-D3-D2")), Is.EqualTo("D3"));
            Assert.That(RampPlanner.NearDeck(m, m.ramps.Single(r => r.id == "RAMP-D2-D1")), Is.EqualTo("D2"));
            Assert.That(RampPlanner.NearDeck(m, m.ramps.Single(r => r.id == "RAMP-D4-D5")), Is.EqualTo("D4"));
        }

        [Test]
        public void ASlotOnARampWaitsUntilEverythingBeyondItIsLoaded()
        {
            var m = Map(); var state = new Dictionary<string, string>();
            var onRamp = S("PS-D3-090", "D3", 25, 9.8, "empty");
            var beyond = S("PS-D1-001", "D1", 100, 3, "empty");
            Assert.That(RampPlanner.RampUnder(m, onRamp)?.id, Is.EqualTo("RAMP-D3-D2"));
            Assert.That(RampPlanner.Available(m, onRamp, "load", state, new[] { onRamp, beyond }), Is.False);
            beyond.status = "filled";
            Assert.That(RampPlanner.Available(m, onRamp, "load", state, new[] { onRamp, beyond }), Is.True);
            // and the plan for that slot puts the ramp (and its sibling with nothing beyond) up
            var plan = RampPlanner.Plan(m, onRamp, "load", state, new[] { onRamp, beyond });
            Assert.That(plan, Does.Contain(("RAMP-D3-D2", RampPlanner.Stowed)));
        }

        [Test]
        public void ARouteThroughAStowedRampWithACarOnItIsClosed()
        {
            var m = Map(); var state = new Dictionary<string, string> { ["RAMP-D3-D2"] = RampPlanner.Stowed };
            var onRamp = S("PS-D3-090", "D3", 25, 9.8, "filled");
            var d2 = S("PS-D2-001", "D2", 100, 3, "filled");
            Assert.That(RampPlanner.Available(m, d2, "unload", state, new[] { onRamp, d2 }), Is.False);
            onRamp.status = "empty";
            Assert.That(RampPlanner.Available(m, d2, "unload", state, new[] { onRamp, d2 }), Is.True);
            Assert.That(RampPlanner.Plan(m, d2, "unload", state, new[] { onRamp, d2 }), Does.Contain(("RAMP-D3-D2", RampPlanner.Deployed)));
        }

        [Test]
        public void AnUnreachableCarIsNotACarOnTheRamp()
        {
            var m = Map();
            var gaveUp = S("PS-D3-090", "D3", 25, 9.8, "unreachable");
            Assert.That(RampPlanner.CanDeploy(m, m.ramps.Single(r => r.id == "RAMP-D3-D2"), new[] { gaveUp }), Is.True);
        }
    }
}
