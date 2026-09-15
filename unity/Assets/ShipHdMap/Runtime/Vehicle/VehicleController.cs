using UnityEngine;

namespace ShipHdMap
{
    public class VehicleController : MonoBehaviour
    {
        public double speedMps = 2.0;
        public double[][] centerline; public double deckZ; public double s;
        public bool running;
        public Pose2D Truth;

        public void StartLane(Lane lane, double z) { centerline = lane.centerline; deckZ = z; s = 0; running = true; Apply(); }

        void Update()
        {
            if (!running || centerline == null) return;
            s += speedMps * Time.deltaTime;
            var (_, _, end) = LaneFollower.At(centerline, s);
            if (end) running = false;
            Apply();
        }

        void Apply()
        {
            var (p, h, _) = LaneFollower.At(centerline, s);
            Truth = new Pose2D { x = p.x, y = p.y, psiRad = h };
            transform.position = ShipFrame.ToUnity(p.x, p.y, deckZ + 0.5);
            transform.rotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(h * 180 / System.Math.PI), 0);
        }
    }
}
