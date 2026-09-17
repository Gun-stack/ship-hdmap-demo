using System;
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class PoseTests
    {
        GameObject go;
        [TearDown] public void Cleanup()
        {
            if (go) UnityEngine.Object.DestroyImmediate(go);
            var ship = GameObject.Find("Ship"); if (ship) UnityEngine.Object.DestroyImmediate(ship);
            var quay = GameObject.Find("Quay"); if (quay) UnityEngine.Object.DestroyImmediate(quay);   // InitForTest builds one per NewRuntime() call
        }
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
            Assert.That(bow.y, Is.GreaterThan(11.5f - 10.1f));   // root also sinks by draft_aft_m (M5b)
            var ap = rt.transform.TransformPoint(ShipFrame.ToUnity(0, 0, 10));
            // rotation is about the AP origin (x=0,y=0,z=0), not about this point itself (10 m up); a rigid
            // rotation still moves a point 10 m off its own axis by the second-order term height*(1-cos(trim))
            // ~= 10*(1-cos(0.955deg)) ~= 0.0014 m, so 1e-4f is unreachable -- 0.01f leaves an order of magnitude
            // of margin while still catching a gross axis mixup (which would show a first-order ~1.7 m shift).
            Assert.That(ap.y, Is.EqualTo(10f - 10.1f).Within(0.01f));   // and by that same sink
        }

        [Test]
        public void PositiveHeelLowersStarboard()
        {
            var rt = NewRuntime();
            rt.SetPose(HeelOnly);   // starboard point 10 m off the centreline drops 10*sin(3deg) = 0.52 m
            var stbd = rt.transform.TransformPoint(ShipFrame.ToUnity(50, -10, 10));
            Assert.That(stbd.y, Is.LessThan(9.6f - 8.6f));   // root also sinks by draft_aft_m (M5b)
            var port = rt.transform.TransformPoint(ShipFrame.ToUnity(50, 10, 10));
            Assert.That(port.y, Is.GreaterThan(10.4f - 8.6f));
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
            // angle_deg (4) is measured from the horizon; SetRampAngle adds the hull's own trim (~0.955 deg here)
            // on top so the ship-local rotation still lands the ramp at that horizon angle.
            double trimDeg = Math.Atan2(10.1 - 8.1, 120) * 180 / Math.PI;
            Assert.That(Mathf.DeltaAngle(ramp.localRotation.eulerAngles.z, (float)-(4 + trimDeg)), Is.EqualTo(0f).Within(1e-3f));
            var floor = ship.transform.Find("D3/Floor");
            Assert.That(floor.position.y, Is.GreaterThan(10.5f + 60f * Mathf.Tan(0.955f * Mathf.Deg2Rad) - 0.6f - 10.1f)); // floor centre (x=60) rose with the root, then the root sank by draft_aft_m (M5b)

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

        [Test]
        public void PlacementUnderTiltLandsOnTheDeckInShipFrame()
        {
            var rt = NewRuntime();
            var p = new ShipParams(); ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            rt.Load(Fixture());
            rt.SetPose(TrimOnly);   // attaches the ship and tilts the root; Physics.SyncTransforms runs inside
            // ray straight down onto Deck 3 at ship (50, 0), expressed in world space through the tilted root
            var origin = rt.transform.TransformPoint(ShipFrame.ToUnity(50, 0, 12.5));
            var down = rt.transform.TransformDirection(Vector3.down);
            Assert.That(Physics.Raycast(origin, down, out var hit, 10f, LandmarkPlacer.StructureMask()), Is.True);
            var lm = rt.Placer.PlaceAt(hit);
            var m = lm.ToModel();
            Assert.That(m.position[0], Is.EqualTo(50).Within(0.05));
            Assert.That(m.position[1], Is.EqualTo(0).Within(0.05));
            Assert.That(m.position[2], Is.EqualTo(10.6).Within(0.05));   // Deck 3 surface, not the world height
            Assert.That(m.deck_id, Is.EqualTo("D3"));
        }

        [Test]
        public void PoseSinksTheRootByAftDraftAndPutsTheQuayAtQuayZPlusTide()
        {
            var go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.SetPose("{\"draft_fwd_m\":8.1,\"draft_aft_m\":8.6,\"heel_deg\":0,\"lpp_m\":120,\"tide_m\":0.4,\"quay_z_m\":3.5}");
            Assert.That(rt.transform.localPosition.y, Is.EqualTo(-8.6f).Within(1e-4f));     // waterline is world y = 0
            Assert.That(QuayBuilder.SurfaceZ(rt.Quay), Is.EqualTo(3.9).Within(1e-4));       // quay_z + tide
            Assert.That(rt.Quay.transform.parent, Is.Null, "the quay must not ride on the Map root");
            Assert.That(rt.Quay.GetComponent<Collider>(), Is.Null, "the quay must not catch placement raycasts");
            UnityEngine.Object.DestroyImmediate(rt.Quay); UnityEngine.Object.DestroyImmediate(go);
        }

        [Test]
        public void RampFreeEndLandsOnTheQuaySurface()
        {
            // The identity behind the whole layout: angle = asin(((quay_z + tide) - (hinge_z - draft_aft)) / length)
            // puts the ramp's free end exactly on the quay surface. That angle is measured from the horizon (it
            // comes from two heights above the waterline), but SetRampAngle's rotation is ship-LOCAL -- so it only
            // lands there because SetRampAngle adds the hull's own trim back on top. Covers both a flat hull and
            // a trimmed one (drafts 8.1/10.1, trim ~0.955 deg), which is the regression case for that compensation:
            // without it the free end misses the quay by length*sin(trim), about half a metre here.
            double quayZ = 3.5, tide = 0.4;
            foreach (var (draftFwd, draftAft) in new (double, double)[] { (8.1, 8.6), (8.1, 10.1) })
            {
                var go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
                var seed = ShipSeedBuilder.Build(new ShipParams());
                var ship = ShipMeshBuilder.Build(seed, new ShipParams(), rt.transform);
                var r = seed.ramps[0];
                double hingeZ = r.hinge[0][2];
                double angle = Math.Asin(((quayZ + tide) - (hingeZ - draftAft)) / r.length_m) * 180 / Math.PI;
                rt.SetPose($"{{\"draft_fwd_m\":{draftFwd},\"draft_aft_m\":{draftAft},\"heel_deg\":0,\"lpp_m\":120,\"tide_m\":{tide},\"quay_z_m\":{quayZ},\"ramp\":{{\"id\":\"RAMP-STERN\",\"angle_deg\":{angle},\"state\":\"deployed\"}}}}");

                var ramp = ship.transform.Find("Ramp");
                float freeEndY = ramp.TransformPoint(new Vector3(-(float)r.length_m, 0, 0)).y;   // plate runs from the hinge toward -x
                Assert.That(freeEndY, Is.EqualTo((float)QuayBuilder.SurfaceZ(rt.Quay)).Within(0.01f), $"drafts {draftFwd}/{draftAft}");
                UnityEngine.Object.DestroyImmediate(rt.Quay); UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
