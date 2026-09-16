using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class ScenarioPlannerTests
    {
        static Lane D3Lane() => new Lane { id = "A2-D3-0001", deck_id = "D3", centerline = new[] { new double[] { 2, 0, 10.6 }, new double[] { 60, 0, 10.6 }, new double[] { 118, 0, 10.6 } }, speed_limit_kmh = 10 };
        static ParkingSlot Slot(string id, int seq, string status, double x = 100, double y = 2.925) =>
            new ParkingSlot { id = id, deck_id = "D3", sequence_no = seq, status = status, access_lane_id = "A2-D3-0001", target_pose = new TargetPose { x = x, y = y, heading_deg = 0 }, tolerance = new Tolerance { lat_m = 0.15, lon_m = 0.30, heading_deg = 2 } };

        [Test]
        public void NextSlotLoadTakesLowestEmptySequenceAndUnloadHighestFilled()
        {
            var slots = new List<ParkingSlot> { Slot("c", 3, "empty"), Slot("a", 1, "filled"), Slot("b", 2, null), Slot("d", 4, "needs_adjust") };
            Assert.That(ScenarioPlanner.NextSlot(slots, "load").id, Is.EqualTo("b"));
            Assert.That(ScenarioPlanner.NextSlot(slots, "unload").id, Is.EqualTo("d"));
            Assert.That(ScenarioPlanner.NextSlot(new List<ParkingSlot> { Slot("a", 1, "filled") }, "load"), Is.Null);
            Assert.That(ScenarioPlanner.NextSlot(null, "load"), Is.Null);
        }

        [Test]
        public void ExitPointIsFortyFiveDegreesBeforeTheSlotAndClampsAtLaneStart()
        {
            var t = new TargetPose { x = 102.4, y = 2.925, heading_deg = 0 };
            Assert.That(ScenarioPlanner.ExitS(D3Lane(), t), Is.EqualTo(102.4 - 2 - 2.925 - 2).Within(1e-9));   // x_exit 97.475, lane starts at x = 2
            var stern = new TargetPose { x = 3.6, y = -8, heading_deg = 0 };
            Assert.That(ScenarioPlanner.ExitS(D3Lane(), stern), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void ApproachPathEndsAtTargetWithTargetHeading()
        {
            var t = new TargetPose { x = 102.4, y = 2.925, heading_deg = 0 };
            var path = ScenarioPlanner.ApproachPath(new Pose2D { x = 97.5, y = 0.1, psiRad = 0 }, t, 10.6);
            Assert.That(path.Length, Is.EqualTo(3));
            Assert.That(path[0], Is.EqualTo(new[] { 97.5, 0.1, 10.6 }).Within(1e-9));
            Assert.That(path[1], Is.EqualTo(new[] { 100.4, 2.925, 10.6 }).Within(1e-9));
            Assert.That(path[2], Is.EqualTo(new[] { 102.4, 2.925, 10.6 }).Within(1e-9));
            var (end, heading, _) = LaneFollower.At(path, 999);
            Assert.That(end.x, Is.EqualTo(102.4f).Within(1e-4f)); Assert.That(heading, Is.EqualTo(0).Within(1e-9));

            var turned = ScenarioPlanner.ApproachPath(new Pose2D { x = 0, y = 0 }, new TargetPose { x = 10, y = 10, heading_deg = 90 }, 0);
            Assert.That(turned[1], Is.EqualTo(new[] { 10.0, 8.0, 0.0 }).Within(1e-9));   // last 2 m run along +y
        }

        [Test]
        public void DeparturePathBacksOutToTheLaneStart()
        {
            var t = new TargetPose { x = 102.4, y = 2.925, heading_deg = 0 };
            var path = ScenarioPlanner.DeparturePath(t, D3Lane(), 10.6);
            Assert.That(path.Length, Is.EqualTo(4));
            Assert.That(path[1], Is.EqualTo(new[] { 100.4, 2.925, 10.6 }).Within(1e-9));
            Assert.That(path[2], Is.EqualTo(new[] { 97.475, 0.0, 10.6 }).Within(1e-9));
            Assert.That(path[3], Is.EqualTo(new[] { 2.0, 0.0, 10.6 }).Within(1e-9));
            var (_, heading, _) = LaneFollower.At(path, 999);
            Assert.That(Math.Abs(heading), Is.EqualTo(Math.PI).Within(1e-9));   // ends pointing astern
        }

        [Test]
        public void ShiftTranslatesEveryPoint()
        {
            var shifted = ScenarioPlanner.Shift(new[] { new double[] { 1, 2, 3 }, new double[] { 4, 5, 3 } }, 0.5, -0.25);
            Assert.That(shifted[0], Is.EqualTo(new[] { 1.5, 1.75, 3.0 }).Within(1e-12));
            Assert.That(shifted[1], Is.EqualTo(new[] { 4.5, 4.75, 3.0 }).Within(1e-12));
        }

        [Test]
        public void JudgeUsesTargetFrameAndTolerance()
        {
            var t = new TargetPose { x = 100, y = 2, heading_deg = 0 };
            var tol = new Tolerance { lat_m = 0.15, lon_m = 0.30, heading_deg = 2 };
            var ok = ScenarioPlanner.Judge(new Pose2D { x = 100.2, y = 2.1, psiRad = 1 * Math.PI / 180 }, t, tol);
            Assert.That(ok.status, Is.EqualTo("filled"));
            Assert.That(ok.errLon, Is.EqualTo(0.2).Within(1e-9)); Assert.That(ok.errLat, Is.EqualTo(0.1).Within(1e-9)); Assert.That(ok.errHeadingDeg, Is.EqualTo(1).Within(1e-9));
            Assert.That(ScenarioPlanner.Judge(new Pose2D { x = 100, y = 2.2, psiRad = 0 }, t, tol).status, Is.EqualTo("needs_adjust"));     // lat 0.20 > 0.15
            Assert.That(ScenarioPlanner.Judge(new Pose2D { x = 100.35, y = 2, psiRad = 0 }, t, tol).status, Is.EqualTo("needs_adjust"));    // lon 0.35 > 0.30
            Assert.That(ScenarioPlanner.Judge(new Pose2D { x = 100, y = 2, psiRad = 2.5 * Math.PI / 180 }, t, tol).status, Is.EqualTo("needs_adjust"));
            // rotated target frame: heading 90 → "lon" is along +y
            var r = ScenarioPlanner.Judge(new Pose2D { x = 10, y = 10.2, psiRad = Math.PI / 2 }, new TargetPose { x = 10, y = 10, heading_deg = 90 }, null);
            Assert.That(r.errLon, Is.EqualTo(0.2).Within(1e-9)); Assert.That(r.errLat, Is.EqualTo(0).Within(1e-9)); Assert.That(r.status, Is.EqualTo("filled"));
        }
    }
}
