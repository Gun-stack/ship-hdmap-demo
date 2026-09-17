using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipHdMap
{
    /// Pure scenario geometry (spec §3): which slot next, where to leave the lane, the approach/departure polylines, and the
    /// parking judgement. No scene access, so it is unit-tested directly; MapRuntime owns the state machine.
    public static class ScenarioPlanner
    {
        public const double FinalRunM = 2.0, ParkSpeedMps = 2.0;
        const double D = Math.PI / 180.0;

        public static bool IsFilled(string status) => status == "filled" || status == "needs_adjust";

        public static ParkingSlot NextSlot(IEnumerable<ParkingSlot> slots, string mode)
        {
            if (slots == null) return null;
            return mode == "unload"
                ? slots.Where(s => IsFilled(s.status) && s.target_pose != null).OrderByDescending(s => s.sequence_no).FirstOrDefault()
                : slots.Where(s => (s.status ?? "empty") == "empty" && s.target_pose != null).OrderBy(s => s.sequence_no).FirstOrDefault();
        }

        /// Arc length and coordinates of the polyline point nearest to (x, y).
        public static (double s, double x, double y) NearestOnLine(double[][] line, double x, double y)
        {
            double best = double.MaxValue, bestS = 0, bx = line[0][0], by = line[0][1], acc = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                double ax = line[i][0], ay = line[i][1], dx = line[i + 1][0] - ax, dy = line[i + 1][1] - ay, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-12 ? 0 : Math.Min(1, Math.Max(0, ((x - ax) * dx + (y - ay) * dy) / len2));
                double px = ax + dx * t, py = ay + dy * t, d2 = (px - x) * (px - x) + (py - y) * (py - y);
                if (d2 < best) { best = d2; bestS = acc + Math.Sqrt(len2) * t; bx = px; by = py; }
                acc += Math.Sqrt(len2);
            }
            return (bestS, bx, by);
        }

        /// Leave the lane FinalRunM + |lateral offset| before the target so the approach is a 45° diagonal plus a straight 2 m run.
        public static double ExitS(Lane lane, TargetPose t)
        {
            var (_, _, laneY) = NearestOnLine(lane.centerline, t.x, t.y);
            double xExit = t.x - FinalRunM - Math.Abs(t.y - laneY);
            return NearestOnLine(lane.centerline, xExit, laneY).s;
        }

        /// Planned in the vehicle's belief frame: from its estimated pose to the target, the last FinalRunM along the target heading.
        public static double[][] ApproachPath(Pose2D est, TargetPose t, double z)
        {
            double h = t.heading_deg * D;
            return new[] { new[] { est.x, est.y, z }, new[] { t.x - FinalRunM * Math.Cos(h), t.y - FinalRunM * Math.Sin(h), z }, new[] { t.x, t.y, z } };
        }

        /// Unload: back out of the slot to the lane and drive to the lane start (astern), where the vehicle disappears.
        public static double[][] DeparturePath(TargetPose t, Lane lane, double z)
        {
            var (_, _, laneY) = NearestOnLine(lane.centerline, t.x, t.y);
            return new[] { new[] { t.x, t.y, z }, new[] { t.x - FinalRunM, t.y, z }, new[] { t.x - FinalRunM - Math.Abs(t.y - laneY), laneY, z }, new[] { lane.centerline[0][0], lane.centerline[0][1], z } };
        }

        /// Rigid transform from the vehicle's belief frame to the true frame, centred on the point where it left the lane
        /// (est in the belief frame, truth in the true frame): rotate each point's offset from est by
        /// truth.psi - est.psi, then place it relative to truth. A translation alone would leave the path's own
        /// heading unchanged, so the vehicle would always finish on the target heading and err_heading would be
        /// structurally 0 regardless of the estimate (2026-09-17: found by the user driving the demo with heavy noise).
        public static double[][] ToTruthFrame(double[][] path, Pose2D est, Pose2D truth)
        {
            double dPsi = ShipFrame.WrapRad(truth.psiRad - est.psiRad);
            double c = Math.Cos(dPsi), s = Math.Sin(dPsi);
            return path.Select(p =>
            {
                double ox = p[0] - est.x, oy = p[1] - est.y;
                return new[] { truth.x + ox * c - oy * s, truth.y + ox * s + oy * c, p[2] };
            }).ToArray();
        }

        /// Errors of the true pose in the target's frame: lon along the target heading, lat to its left (+y side), heading wrapped.
        public static (string status, double errLat, double errLon, double errHeadingDeg) Judge(Pose2D truth, TargetPose t, Tolerance tol)
        {
            tol ??= new Tolerance { lat_m = 0.15, lon_m = 0.30, heading_deg = 2 };
            double h = t.heading_deg * D, dx = truth.x - t.x, dy = truth.y - t.y;
            double lon = dx * Math.Cos(h) + dy * Math.Sin(h), lat = -dx * Math.Sin(h) + dy * Math.Cos(h);
            double hdg = ShipFrame.WrapDeg(truth.psiRad / D - t.heading_deg);
            bool ok = Math.Abs(lat) <= tol.lat_m && Math.Abs(lon) <= tol.lon_m && Math.Abs(hdg) <= tol.heading_deg;
            return (ok ? "filled" : "needs_adjust", lat, lon, hdg);
        }
    }
}
