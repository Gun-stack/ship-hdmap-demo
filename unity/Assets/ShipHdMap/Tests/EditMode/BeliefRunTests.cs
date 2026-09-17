using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    /// Wiring-level checks: the monitor must actually see the solve, and backtracking must move the vehicle.
    public class BeliefRunTests
    {
        GameObject go;
        [TearDown] public void Cleanup()
        {
            foreach (var rt in Object.FindObjectsByType<MapRuntime>(FindObjectsSortMode.None))
            {
                if (rt.Quay) Object.DestroyImmediate(rt.Quay);
                if (rt.Vehicle) Object.DestroyImmediate(rt.Vehicle.gameObject);   // an OnQuay run unparents it, so destroying "go" alone misses it
            }
            if (go) Object.DestroyImmediate(go);
            var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship);
        }

        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));
        const string NoNoise = "{\"sigma_r\":0,\"sigma_theta\":0,\"sigma_alpha\":0,\"sigma_gps\":0}";

        MapRuntime LoadFixture()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture()); rt.SetNoise(NoNoise);
            return rt;
        }

        static string OccludeAllIds(MapRuntime rt)
        {
            var ids = new System.Collections.Generic.List<string>();
            foreach (var k in rt.MapRefs.Keys) ids.Add("\"" + k + "\"");
            return "{\"ids\":[" + string.Join(",", ids) + "]}";
        }

        /// This fixture's "load" run always starts on the quay (M5b) and only reaches the lane once it has SEEN
        /// the ramp's entrance pair; occluding every marker before that point blinds the entrance pair too and the
        /// run finishes with "no_frame_switch" instead of ever reaching OnLane. Drive there first, same as the
        /// scenario-run tests do, so "occlude everything" tests the belief on the deck, not the frame switch.
        static void DriveToLane(MapRuntime rt)
        {
            for (int i = 0; i < 4000 && rt.ScenarioPhase != MapRuntime.Phase.OnLane; i++) rt.Step(0.05f);
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnLane));
        }

        [Test]
        public void ABlindStretchTakesTheBeliefThroughLostToBacktracking()
        {
            var rt = LoadFixture();
            rt.SetBeliefParams("{\"max_lost_m\":2.0}");          // trip quickly
            rt.StartScenario("{\"mode\":\"load\"}");
            DriveToLane(rt);

            // occlude every marker: the deck goes blind wherever the vehicle is
            rt.SetOccluded(OccludeAllIds(rt));

            for (int i = 0; i < 400 && rt.Belief.State != BeliefState.Backtracking; i++) rt.Step(0.05f);
            Assert.That(rt.Belief.State, Is.EqualTo(BeliefState.Backtracking));
            Assert.That(rt.Belief.LostM, Is.GreaterThan(1.0));
        }

        [Test]
        public void BacktrackingDrivesTheArcLengthDown()
        {
            var rt = LoadFixture();
            rt.SetBeliefParams("{\"max_lost_m\":2.0}");
            rt.StartScenario("{\"mode\":\"load\"}");
            DriveToLane(rt);
            for (int i = 0; i < 100; i++) rt.Step(0.05f);        // build a trail while markers are visible
            double sBefore = rt.Vehicle.s;

            rt.SetOccluded(OccludeAllIds(rt));

            for (int i = 0; i < 400 && rt.Belief.State != BeliefState.Backtracking; i++) rt.Step(0.05f);
            for (int i = 0; i < 100; i++) rt.Step(0.05f);
            Assert.That(rt.Vehicle.s, Is.LessThan(sBefore), "the vehicle must retrace, not push on");
        }

        /// The payoff: uncovering the markers again puts the belief back together without human help.
        [Test]
        public void UncoveringTheMarkersRecoversTheBelief()
        {
            var rt = LoadFixture();
            rt.SetBeliefParams("{\"max_lost_m\":2.0}");
            rt.StartScenario("{\"mode\":\"load\"}");
            DriveToLane(rt);
            for (int i = 0; i < 100; i++) rt.Step(0.05f);

            rt.SetOccluded(OccludeAllIds(rt));
            for (int i = 0; i < 400 && rt.Belief.State != BeliefState.Backtracking; i++) rt.Step(0.05f);

            rt.SetOccluded("{\"ids\":[]}");
            for (int i = 0; i < 40; i++) rt.Step(0.05f);
            Assert.That(rt.Belief.State, Is.EqualTo(BeliefState.Ok));
        }

        [Test]
        public void ANormalLaneRunNeverLosesTheBelief()
        {
            var rt = LoadFixture();
            rt.StartScenario("{\"mode\":\"load\"}");
            for (int i = 0; i < 200; i++)
            {
                rt.Step(0.05f);
                if (rt.ScenarioPhase != MapRuntime.Phase.OnLane) continue;
                Assert.That(rt.Belief.State, Is.Not.EqualTo(BeliefState.Lost), "the lane is 1.7 % blind; a lane run must not trip");
            }
        }
    }
}
