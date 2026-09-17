using UnityEngine;

namespace ShipHdMap
{
    /// Kinematic vehicle: walks a [x,y,z] polyline (a lane centreline or a planned approach/departure path) at a constant speed.
    /// No Update of its own — MapRuntime.Step advances it so sensing and motion happen in a fixed order.
    public class VehicleController : MonoBehaviour
    {
        public double speedMps = 2.0;
        public double[][] path; public double deckZ; public double s;
        public bool running;
        public bool AtEnd { get; private set; }
        public Pose2D Truth;

        public void StartLane(Lane lane, double z) => StartPath(lane.centerline, z, lane.speed_limit_kmh > 0 ? lane.speed_limit_kmh / 3.6 : ScenarioPlanner.ParkSpeedMps);

        public void StartPath(double[][] line, double z, double speed) { path = line; deckZ = z; s = 0; speedMps = speed; running = true; AtEnd = false; Apply(); }

        public void Advance(double dt)
        {
            if (!running || path == null) return;
            s += speedMps * dt;
            var (_, _, end) = LaneFollower.At(path, s);
            if (end) { running = false; AtEnd = true; }
            Apply();
        }

        /// Puts the vehicle back on the path at exactly this arc length (used to land on the lane exit point instead of one frame past it).
        public void Rewind(double arcLength) { s = arcLength; running = true; AtEnd = false; Apply(); }

        void Apply()
        {
            var (p, h, _) = LaneFollower.At(path, s);
            Truth = new Pose2D { x = p.x, y = p.y, psiRad = h };
            transform.localPosition = ShipFrame.ToUnity(p.x, p.y, deckZ + 0.5);
            transform.localRotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(h * 180 / System.Math.PI), 0);
        }
    }
}
