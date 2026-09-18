using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class OrbitCameraTests
    {
        [Test]
        public void PosePutsCameraBehindTargetLookingAtIt()
        {
            var (p, r) = OrbitCamera.Pose(new Vector3(60, 10, 0), 0, 0, 10);
            Assert.That(p.x, Is.EqualTo(60).Within(1e-4)); Assert.That(p.y, Is.EqualTo(10).Within(1e-4)); Assert.That(p.z, Is.EqualTo(-10).Within(1e-4));
            Assert.That((r * Vector3.forward).z, Is.EqualTo(1).Within(1e-4));          // looks +z toward the target
            var (top, _) = OrbitCamera.Pose(Vector3.zero, 0, 90, 5);
            Assert.That(top.y, Is.EqualTo(5).Within(1e-4));                                // pitch 90 = straight above
            var (side, _) = OrbitCamera.Pose(Vector3.zero, 90, 0, 5);
            Assert.That(side.x, Is.EqualTo(-5).Within(1e-4));                              // yaw 90 = camera on -x looking +x
        }

        [Test]
        public void AdoptCurrentPoseReproducesTheCameraTransform()
        {
            var go = new GameObject("cam"); var cam = go.AddComponent<Camera>();
            cam.transform.SetPositionAndRotation(new Vector3(60, 25, -40), Quaternion.Euler(20, -20, 0));
            var orbit = go.AddComponent<OrbitCamera>(); orbit.distance = 70f;
            orbit.AdoptCurrentPose();
            var (p, r) = OrbitCamera.Pose(orbit.target, orbit.yawDeg, orbit.pitchDeg, orbit.distance);
            Assert.That(Vector3.Distance(p, cam.transform.position), Is.LessThan(1e-3f));
            Assert.That(Quaternion.Angle(r, cam.transform.rotation), Is.LessThan(1e-3f));
            Object.DestroyImmediate(go);
        }

        /// Free flight moves along the camera's own axes: forward is where you are looking, up is world up
        /// (Q/E), so "fly up" never means "fly into the deck" just because the camera was pitched down.
        [Test]
        public void FlyStepMovesAlongTheCameraAxesAndWorldUp()
        {
            var rot = Quaternion.Euler(0, 90, 0);                  // yawed a quarter turn, so "forward" is not world +z
            var (p, r) = OrbitCamera.FlyStep(Vector3.zero, rot, new Vector3(0, 0, 1), 1f, 10f);
            Assert.That(Vector3.Distance(p, rot * Vector3.forward * 10f), Is.LessThan(1e-3f));
            Assert.That(r, Is.EqualTo(rot));                       // FlyStep translates; looking around is the mouse's job

            var (up, _) = OrbitCamera.FlyStep(Vector3.zero, Quaternion.Euler(80, 0, 0), new Vector3(0, 1, 0), 1f, 10f);
            Assert.That(up.y, Is.EqualTo(10f).Within(1e-3f));      // straight up regardless of pitch
            Assert.That(up.x, Is.EqualTo(0f).Within(1e-3f));
            Assert.That(up.z, Is.EqualTo(0f).Within(1e-3f));
        }

        [Test]
        public void FlyStepScalesWithTimeAndSpeed()
        {
            var (a, _) = OrbitCamera.FlyStep(Vector3.zero, Quaternion.identity, new Vector3(1, 0, 0), 0.5f, 20f);
            Assert.That(a.x, Is.EqualTo(10f).Within(1e-3f));
            var (z, _) = OrbitCamera.FlyStep(Vector3.one, Quaternion.identity, Vector3.zero, 1f, 20f);
            Assert.That(Vector3.Distance(z, Vector3.one), Is.LessThan(1e-6f));
        }

        /// Driver's eye is the sensor's eye: same height, same heading. That is the whole reason the overlay
        /// in that view is allowed to claim it shows what the sensor sees (spec §5.2).
        [Test]
        public void DriverModeSitsAtTheVehicleEyeAndCopiesItsHeading()
        {
            var car = new GameObject("car"); car.transform.SetPositionAndRotation(new Vector3(30, 10.6f, -4), Quaternion.Euler(0, 35, 0));
            var go = new GameObject("cam"); go.AddComponent<Camera>();
            var orbit = go.AddComponent<OrbitCamera>();
            orbit.mode = CamMode.Driver; orbit.driverTarget = car.transform; orbit.driverEyeM = 1.2f;
            orbit.ApplyDriver();
            Assert.That(go.transform.position.y, Is.EqualTo(10.6f + 1.2f).Within(1e-3f));
            Assert.That(go.transform.position.x, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(Quaternion.Angle(go.transform.rotation, car.transform.rotation), Is.LessThan(1e-3f));
            Object.DestroyImmediate(car); Object.DestroyImmediate(go);
        }
    }
}
