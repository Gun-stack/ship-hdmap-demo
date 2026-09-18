using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    [Serializable]
    public class SensorNoise { public double sigmaR = 0.2; public double sigmaThetaRad = 1 * Math.PI / 180; public double sigmaAlphaRad = 2 * Math.PI / 180; public double sigmaGps = 0.5; public int seed = 1; }

    /// Why a mapped landmark is not an observation. One vocabulary for the drive, the driver's-eye overlay
    /// and the probe, so the three can never tell different stories about the same marker.
    public enum Miss { None, Range, Fov, Facing, Occluded, Blocked }

    /// Detection model (spec §9.4). This imitates the OUTPUT of a tag detector; nothing here decodes images.
    public class LandmarkSensor : MonoBehaviour
    {
        public float fovDeg = 90, maxDist = 25, maxViewAngleDeg = 70;
        public SensorNoise noise = new();
        public LayerMask occluders;
        public float eyeHeight = 1.2f;
        /// Markers the sensor must pretend not to see: damage, or a parked car in the way. The map still has them,
        /// which is the point -- the coverage prediction keeps promising them and the belief monitor notices.
        public HashSet<string> occluded = new();
        System.Random _rng;

        void Awake() { _rng = new System.Random(noise.seed); }
        public void Reseed(int seed) { noise.seed = seed; _rng = new System.Random(seed); }

        public static Miss GeometricMiss(Pose2D v, LandmarkRef lm, double fovRad, double maxDist, double maxViewAngleRad)
        {
            double dx = lm.mx - v.x, dy = lm.my - v.y, r = Math.Sqrt(dx * dx + dy * dy);
            if (r > maxDist || r < 1e-6) return Miss.Range;
            double theta = ShipFrame.WrapRad(Math.Atan2(dy, dx) - v.psiRad);
            if (Math.Abs(theta) > fovRad / 2) return Miss.Fov;
            // angle between marker normal and the direction marker -> vehicle
            double toV = Math.Atan2(-dy, -dx);
            return Math.Abs(ShipFrame.WrapRad(toV - lm.phiRad)) <= maxViewAngleRad ? Miss.None : Miss.Facing;
        }

        public static bool IsVisibleGeometric(Pose2D v, LandmarkRef lm, double fovRad, double maxDist, double maxViewAngleRad)
            => GeometricMiss(v, lm, fovRad, maxDist, maxViewAngleRad) == Miss.None;

        public static Observation AddNoise(Observation o, SensorNoise n, System.Random rng)
        {
            return new Observation { id = o.id, r = o.r + Gauss(rng, n.sigmaR), thetaRad = ShipFrame.WrapRad(o.thetaRad + Gauss(rng, n.sigmaThetaRad)), alphaRad = ShipFrame.WrapRad(o.alphaRad + Gauss(rng, n.sigmaAlphaRad)) };
        }

        static double Gauss(System.Random rng, double sigma)
        {
            if (sigma <= 0) return 0;
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// Why every mapped landmark is or is not observable from `pose`, with the eye at `eyeWorld`.
        /// The eye is a parameter, not this transform: the probe (spec §5.3) stands wherever the operator
        /// clicked, and a Pose2D alone cannot start an occlusion linecast.
        /// Returns a List, not a Dictionary: Sense draws noise in this order and the seeded runs depend on it.
        public List<(string id, Miss miss)> VisibleFrom(Pose2D pose, Vector3 eyeWorld, IDictionary<string, LandmarkRef> map, Func<string, Vector3> posOf)
        {
            var why = new List<(string, Miss)>(map.Count);
            double fov = fovDeg * Math.PI / 180, mva = maxViewAngleDeg * Math.PI / 180;
            // Keyed by the DICTIONARY KEY, not by lm.id: LandmarkRef is a struct copied into this dictionary, and
            // the key -- not the copy's own id field -- is the one thing every caller (Sense, Localizer.Solve,
            // the web bridge) actually holds to name an entry. MapRuntime.Confirm keeps the two in sync when it
            // re-keys a saved draft, but nothing at the type level enforces that; trusting the struct's own id
            // here would trust an invariant this dictionary does not guarantee.
            foreach (var kv in map)
            {
                string id = kv.Key; var lm = kv.Value;
                if (occluded.Contains(id)) { why.Add((id, Miss.Occluded)); continue; }
                var m = GeometricMiss(pose, lm, fov, maxDist, mva);
                if (m != Miss.None) { why.Add((id, m)); continue; }
                Vector3 target = posOf(id);
                bool blocked = occluders != 0 && Physics.Linecast(eyeWorld, target - (target - eyeWorld).normalized * 0.05f, occluders);
                why.Add((id, blocked ? Miss.Blocked : Miss.None));
            }
            return why;
        }

        /// Where this sensor's own eye is. Named so a caller that has to build the verdicts itself cannot
        /// drift from what Sense would have used.
        public Vector3 Eye => transform.position + Vector3.up * eyeHeight;

        /// truth: vehicle pose; map: landmark refs; unityPosOf: scene position of a marker (for occlusion linecast).
        public List<Observation> Sense(Pose2D truth, IDictionary<string, LandmarkRef> map, Func<string, Vector3> unityPosOf)
            => Sense(truth, map, VisibleFrom(truth, Eye, map, unityPosOf));

        /// For a caller that already has the verdicts -- the driver's eye draws them as well as senses through
        /// them, and every entry costs a Physics.Linecast, so running VisibleFrom twice a frame with identical
        /// arguments buys nothing. `why` is walked in its own order, which is VisibleFrom's, so the seeded noise
        /// draws land in exactly the sequence they did before.
        public List<Observation> Sense(Pose2D truth, IDictionary<string, LandmarkRef> map, List<(string id, Miss miss)> why)
        {
            var result = new List<Observation>();
            foreach (var (id, miss) in why)
            {
                if (miss != Miss.None) continue;
                result.Add(AddNoise(Localizer.Observe(truth, map[id]), noise, _rng ??= new System.Random(noise.seed)));
            }
            return result;
        }

        /// GPS fix in the Quay Frame: position plus Gaussian noise, heading taken as known (compass/IMU).
        /// The vehicle has nothing else until the entrance landmark pair comes into view.
        public Pose2D Gps(Pose2D truth)
        {
            var rng = _rng ??= new System.Random(noise.seed);
            return new Pose2D { x = truth.x + Gauss(rng, noise.sigmaGps), y = truth.y + Gauss(rng, noise.sigmaGps), psiRad = truth.psiRad };
        }
    }
}
