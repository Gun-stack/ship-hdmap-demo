using UnityEngine;

namespace ShipHdMap
{
    /// Kinematic vehicle: walks a [x,y,z] polyline (a lane centreline or a planned approach/departure path) at a constant speed.
    /// No Update of its own — MapRuntime.Step advances it so sensing and motion happen in a fixed order.
    public class VehicleController : MonoBehaviour
    {
        public double speedMps = 2.0;
        public double[][] path; public double s;
        public bool running;
        public bool AtEnd { get; private set; }
        public double Z { get; private set; }          // current path height, Ship Frame or Quay Frame depending on the parent
        public Pose2D Truth;

        public void StartLane(Lane lane) => StartPath(lane.centerline, lane.speed_limit_kmh > 0 ? lane.speed_limit_kmh / 3.6 : ScenarioPlanner.ParkSpeedMps);

        public void StartPath(double[][] line, double speed) { path = line; s = 0; speedMps = speed; running = true; AtEnd = false; Apply(); }

        public void Advance(double dt)
        {
            if (!running || path == null) return;
            s += speedMps * dt;
            if (LaneFollower.At(path, s).end) { running = false; AtEnd = true; }
            Apply();
        }

        /// Puts the vehicle back on the path at exactly this arc length (used to land on the lane exit point instead of one frame past it).
        public void Rewind(double arcLength) { s = arcLength; running = true; AtEnd = false; Apply(); }

        void Apply()
        {
            var p = LaneFollower.At(path, s);
            Truth = new Pose2D { x = p.x, y = p.y, psiRad = p.headingRad };
            Z = p.z;
            transform.localPosition = ShipFrame.ToUnity(p.x, p.y, p.z + 0.5);
            // Yaw first, then pitch about the yawed local Z: a positive Z rotation takes local +X (the nose) toward +Y.
            transform.localRotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(p.headingRad * 180 / System.Math.PI), 0)
                                    * Quaternion.Euler(0, 0, (float)(p.pitchRad * 180 / System.Math.PI));
        }
    }
}
