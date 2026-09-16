using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class PoseTests
    {
        GameObject go;
        [TearDown] public void Cleanup() { if (go) Object.DestroyImmediate(go); var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship); }
        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));
        const string TrimOnly = "{\"draft_fwd_m\":8.1,\"draft_aft_m\":10.1,\"heel_deg\":0,\"lpp_m\":120,\"ramp\":{\"id\":\"RAMP-STERN\",\"angle_deg\":4,\"state\":\"deployed\"}}";
        const string HeelOnly = "{\"draft_fwd_m\":8.6,\"draft_aft_m\":8.6,\"heel_deg\":3,\"lpp_m\":120}";

        MapRuntime NewRuntime() { go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); return rt; }

        [Test]
        public void PositiveTrimRaisesTheBow()
        {
            var rt = NewRuntime();
            rt.SetPose(TrimOnly);   // trim = atan(2/120) = 0.955 deg; bow point 100 m forward rises ~1.67 m
            var bow = rt.transform.TransformPoint(ShipFrame.ToUnity(100, 0, 10));
            Assert.That(bow.y, Is.GreaterThan(11.5f));
            var ap = rt.transform.TransformPoint(ShipFrame.ToUnity(0, 0, 10));
            // rotation is about the AP origin (x=0,y=0,z=0), not about this point itself (10 m up); a rigid
            // rotation still moves a point 10 m off its own axis by the second-order term height*(1-cos(trim))
            // ~= 10*(1-cos(0.955deg)) ~= 0.0014 m, so 1e-4f is unreachable -- 0.01f leaves an order of magnitude
            // of margin while still catching a gross axis mixup (which would show a first-order ~1.7 m shift).
            Assert.That(ap.y, Is.EqualTo(10f).Within(0.01f));
        }

        [Test]
        public void PositiveHeelLowersStarboard()
        {
            var rt = NewRuntime();
            rt.SetPose(HeelOnly);   // starboard point 10 m off the centreline drops 10*sin(3deg) = 0.52 m
            var stbd = rt.transform.TransformPoint(ShipFrame.ToUnity(50, -10, 10));
            Assert.That(stbd.y, Is.LessThan(9.6f));
            var port = rt.transform.TransformPoint(ShipFrame.ToUnity(50, 10, 10));
            Assert.That(port.y, Is.GreaterThan(10.4f));
        }

        [Test]
        public void PoseKeepsShipFrameCoordinatesAndTiltsRampAndShip()
        {
            var rt = NewRuntime();
            var p = new ShipParams(); ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);   // built at the scene root, like the Demo scene before Awake attaches it
            rt.Load(Fixture());
            var lm = rt.LandmarksRoot.Find("LM-0001").GetComponent<LandmarkMarker>();
            var before = lm.ToModel();
            var lane = rt.CurrentMap.lanes.Find(l => l.id == "A2-D3-0001");
            rt.Vehicle.StartLane(lane, 10.6);
            var vehicleBefore = ShipFrame.ToShip(rt.transform.InverseTransformPoint(rt.Vehicle.transform.position));

            rt.SetPose(TrimOnly);

            var after = lm.ToModel();
            Assert.That(after.position, Is.EqualTo(before.position).Within(1e-4));
            Assert.That(after.normal, Is.EqualTo(before.normal).Within(1e-4));
            Assert.That(lm.transform.position.y, Is.Not.EqualTo(before.position[2]).Within(0.05));   // but the world position did move
            var vehicleAfter = ShipFrame.ToShip(rt.transform.InverseTransformPoint(rt.Vehicle.transform.position));
            Assert.That(vehicleAfter.x, Is.EqualTo(vehicleBefore.x).Within(1e-4)); Assert.That(vehicleAfter.z, Is.EqualTo(vehicleBefore.z).Within(1e-4));
            Assert.That(rt.Vehicle.transform.position.y, Is.Not.EqualTo(11.1f).Within(0.02f));   // x = 2 rises 2*tan(0.955deg) ~= 0.033 m

            var ship = GameObject.Find("Ship");
            Assert.That(ship.transform.parent, Is.EqualTo(rt.transform));                                   // attached under the Map root
            var ramp = ship.transform.Find("Ramp");
            Assert.That(Mathf.DeltaAngle(ramp.localRotation.eulerAngles.z, -4f), Is.EqualTo(0f).Within(1e-3f)); // SetRampAngle(4) -> local z -4
            var floor = ship.transform.Find("D3/Floor");
            Assert.That(floor.position.y, Is.GreaterThan(10.5f + 60f * Mathf.Tan(0.955f * Mathf.Deg2Rad) - 0.6f)); // floor centre (x=60) rose with the root

            var overlayLine = rt.transform.Find("Overlay/D3/A2-D3-0001").GetComponent<LineRenderer>();
            Assert.That(overlayLine.useWorldSpace, Is.False);
        }

        [Test]
        public void PoseBeforeLoadIsAppliedAfterLoad()
        {
            var rt = NewRuntime();
            rt.SetPose(HeelOnly);
            var p = new ShipParams(); ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            rt.Load(Fixture());
            Assert.That(GameObject.Find("Ship").transform.parent, Is.EqualTo(rt.transform));
            var stbd = rt.transform.TransformPoint(ShipFrame.ToUnity(50, -10, 10));
            Assert.That(stbd.y, Is.LessThan(9.6f));
        }

        [Test]
        public void PoseRotationSigns()
        {
            var bow = MapRuntime.PoseRotation(10, 0) * Vector3.right;   Assert.That(bow.y, Is.GreaterThan(0.1f));
            var stbd = MapRuntime.PoseRotation(0, 10) * Vector3.forward; Assert.That(stbd.y, Is.LessThan(-0.1f));
        }
    }
}
