using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class VisibleFromTests
    {
        const double D = Math.PI / 180.0;
        GameObject go;
        [TearDown] public void Cleanup() { if (go) UnityEngine.Object.DestroyImmediate(go); }

        static Dictionary<string, LandmarkRef> Map() => new()
        {
            ["near"] = new LandmarkRef { id = "near", mx = 10, my = 0, phiRad = Math.PI },       // faces the vehicle
            // id deliberately differs from the dictionary key: MapRuntime.Confirm re-keys a saved draft without
            // rewriting the struct's own id, and why["far"] below would throw KeyNotFoundException instead of
            // reading Miss.Range if VisibleFrom ever went back to keying its results by lm.id.
            ["far"] = new LandmarkRef { id = "far-old", mx = 30, my = 0, phiRad = Math.PI },
            ["side"] = new LandmarkRef { id = "side", mx = 0.1, my = 10, phiRad = -Math.PI / 2 },// bearing ~89deg, outside the 45deg half-angle
            ["back"] = new LandmarkRef { id = "back", mx = 10, my = 0, phiRad = 0 },             // normal points away
        };

        /// Each of the three geometric conditions has its own answer, so the overlay can say WHY a marker
        /// went dark instead of just that it did (spec §5.2).
        [Test]
        public void GeometricMissNamesTheConditionThatFailed()
        {
            var v = new Pose2D { x = 0, y = 0, psiRad = 0 };
            var m = Map();
            Assert.That(LandmarkSensor.GeometricMiss(v, m["near"], 90 * D, 25, 70 * D), Is.EqualTo(Miss.None));
            Assert.That(LandmarkSensor.GeometricMiss(v, m["far"], 90 * D, 25, 70 * D), Is.EqualTo(Miss.Range));
            Assert.That(LandmarkSensor.GeometricMiss(v, m["side"], 90 * D, 25, 70 * D), Is.EqualTo(Miss.Fov));
            Assert.That(LandmarkSensor.GeometricMiss(v, m["back"], 90 * D, 25, 70 * D), Is.EqualTo(Miss.Facing));
        }

        // `IsVisibleGeometric` 이 `GeometricMiss(...) == Miss.None` 그 자체이므로 둘이 같은지 묻는 테스트는
        // 정의를 정의로 확인할 뿐 어떤 회귀에도 빨개지지 않는다. 그 술어는 기존
        // SensorAndVehicleTests.VisibilityRespectsFovDistanceAndViewAngle 이 실값 5 개로 이미 고정한다.

        /// Spec §7.2.3: the drive and the overlay must not be able to disagree.
        [Test]
        public void VisibleFromAgreesWithSenseWhenThereIsNoNoise()
        {
            go = new GameObject("Map");
            var sensor = go.AddComponent<LandmarkSensor>();
            sensor.noise = new SensorNoise { sigmaR = 0, sigmaThetaRad = 0, sigmaAlphaRad = 0 };
            sensor.occluders = 0;                                   // no colliders in this scene to hide behind
            var map = Map();
            Func<string, Vector3> posOf = id => ShipFrame.ToUnity(map[id].mx, map[id].my, 0);
            var truth = new Pose2D { x = 0, y = 0, psiRad = 0 };

            var sensed = new HashSet<string>();
            foreach (var o in sensor.Sense(truth, map, posOf)) sensed.Add(o.id);

            var seen = new HashSet<string>();
            foreach (var (id, miss) in sensor.VisibleFrom(truth, Vector3.up * sensor.eyeHeight, map, posOf))
                if (miss == Miss.None) seen.Add(id);

            Assert.That(seen, Is.EquivalentTo(sensed));
            Assert.That(seen, Is.EquivalentTo(new[] { "near" }));
        }

        /// Spec §7.2.4. A marker the sensor was told to ignore reads as Occluded, not as an ordinary miss:
        /// the map still promises it, which is exactly what M5d's belief monitor is there to notice.
        [Test]
        public void VisibleFromReportsTheOccludedSetSeparately()
        {
            go = new GameObject("Map");
            var sensor = go.AddComponent<LandmarkSensor>();
            sensor.occluders = 0;
            sensor.occluded.Add("near");
            var map = Map();
            Func<string, Vector3> posOf = id => ShipFrame.ToUnity(map[id].mx, map[id].my, 0);

            var why = new Dictionary<string, Miss>();
            foreach (var (id, miss) in sensor.VisibleFrom(new Pose2D { x = 0, y = 0, psiRad = 0 }, Vector3.up * 1.2f, map, posOf)) why[id] = miss;

            Assert.That(why["near"], Is.EqualTo(Miss.Occluded));
            Assert.That(why["far"], Is.EqualTo(Miss.Range));         // "far"'s own LandmarkRef.id is "far-old" -- this line only
                                                                      // reads by the dictionary key if VisibleFrom does too
            Assert.That(why.ContainsKey("far-old"), Is.False);       // and never under the struct's stale id
            Assert.That(why.Count, Is.EqualTo(4));   // every mapped marker gets an answer, not just the visible ones
        }

        /// The cone edges and the range arc are what turn "it went dark" into "it went dark because".
        [Test]
        public void ConeArcPointsSpanTheFieldOfViewAtTheRangeLimit()
        {
            var pts = SensorView.ConeArcPoints(new Pose2D { x = 0, y = 0, psiRad = 0 }, 90, 25, 10.6, 8);
            Assert.That(pts.Length, Is.EqualTo(11));                       // eye + 9 arc points + back to the eye
            // 2 cm above the deck floor, not on it: coplanar lines z-fight with the deck mesh
            foreach (var p in pts) Assert.That(p.y, Is.EqualTo(10.62f).Within(1e-3f));
            var (ax, ay, _) = ShipFrame.ToShip(pts[1]);
            Assert.That(Math.Sqrt(ax * ax + ay * ay), Is.EqualTo(25).Within(1e-3));
            Assert.That(Math.Atan2(ay, ax) * 180 / Math.PI, Is.EqualTo(45).Within(1e-3));
        }
    }
}
