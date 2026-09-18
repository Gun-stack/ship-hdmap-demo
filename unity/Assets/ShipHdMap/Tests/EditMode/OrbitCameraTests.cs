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

        /// A null driverTarget is the probe's contract, not a missing case: Driver mode with nobody to follow
        /// means "leave the camera where it was put". A helpful orbit fallback here would yank the virtual
        /// viewpoint away the frame after it was placed.
        [Test]
        public void DriverModeWithNoTargetLeavesTheCameraExactlyWhereItWasPut()
        {
            var go = new GameObject("cam"); go.AddComponent<Camera>();
            var orbit = go.AddComponent<OrbitCamera>();
            var placed = new Vector3(42, 12.4f, -7); var facing = Quaternion.Euler(0, 110, 0);
            go.transform.SetPositionAndRotation(placed, facing);
            orbit.mode = CamMode.Driver; orbit.driverTarget = null;
            orbit.ApplyDriver();
            Assert.That(Vector3.Distance(go.transform.position, placed), Is.LessThan(1e-4f));
            Assert.That(Quaternion.Angle(go.transform.rotation, facing), Is.LessThan(1e-3f));
            Object.DestroyImmediate(go);
        }

        /// Fly clamps pitch to +-89 and the orbit camera to -5..89, and Focus/AdoptCurrentPose import whatever
        /// fly left. While the clamp lived inside the right-drag guard the camera could sit below its own floor
        /// -- under the deck -- until the first right-drag snapped it up in one 84-degree jump.
        [Test]
        public void OrbitPitchIsClampedWhateverLeftItOutOfRange()
        {
            var go = new GameObject("cam"); go.AddComponent<Camera>();
            var orbit = go.AddComponent<OrbitCamera>();
            orbit.pitchDeg = -80f;                       // a fly-mode pitch, legal there and not here
            orbit.ClampOrbitPitch();
            Assert.That(orbit.pitchDeg, Is.EqualTo(-5f).Within(1e-4f));
            orbit.pitchDeg = 120f; orbit.ClampOrbitPitch();
            Assert.That(orbit.pitchDeg, Is.EqualTo(89f).Within(1e-4f));
            orbit.pitchDeg = 35f; orbit.ClampOrbitPitch();
            Assert.That(orbit.pitchDeg, Is.EqualTo(35f).Within(1e-4f), "an in-range pitch must be left alone");
            Object.DestroyImmediate(go);
        }

        /// Driver's eye is the sensor's eye: same height, same heading. That is the whole reason the overlay
        /// in that view is allowed to claim it shows what the sensor sees (spec §5.2).
        ///
        /// Asserted as "the eye looks down the NOSE", not as "the rotation was copied". The old form --
        /// Quaternion.Angle(camera.rotation, car.rotation) < 1e-3 -- asserted the implementation back to
        /// itself, so it stayed green while the view pointed 90 deg to starboard: the nose is the vehicle's
        /// local +X and a Unity camera looks along its own +Z.
        [Test]
        public void DriverModeLooksDownTheNoseAndNotOutTheStarboardSide()
        {
            // The full stack the scene composes: the Map root carries the hull's trim and HEEL, and the
            // vehicle's own rotation yaws onto the heading then pitches about its local Z for the ramp.
            // Heel is the part that makes this fixture falsify the `up` assertion below. Ramp pitch alone does
            // not: a rotation about the local Z leaves the body's forward horizontal, and LookRotation only
            // uses the component of `up` perpendicular to forward -- so Vector3.up and the vehicle's own up
            // project to the same thing and a Vector3.up reference stays green.
            var car = new GameObject("car");
            car.transform.SetPositionAndRotation(new Vector3(30, 10.6f, -4),
                MapRuntime.PoseRotation(1.5, 6) * Quaternion.Euler(0, 35, 0) * Quaternion.Euler(0, 0, 12));
            var go = new GameObject("cam"); go.AddComponent<Camera>();
            var orbit = go.AddComponent<OrbitCamera>();
            orbit.mode = CamMode.Driver; orbit.driverTarget = car.transform; orbit.driverEyeM = 1.2f;
            orbit.ApplyDriver();
            Assert.That(go.transform.position.y, Is.EqualTo(10.6f + 1.2f).Within(1e-3f));
            Assert.That(go.transform.position.x, Is.EqualTo(30f).Within(1e-3f));
            Assert.That(Vector3.Dot(go.transform.forward, car.transform.right), Is.EqualTo(1).Within(1e-3), "the eye looks where the nose points");
            Assert.That(Vector3.Dot(go.transform.forward, car.transform.forward), Is.EqualTo(0).Within(1e-3), "and not along the body's own +Z, which is starboard");
            // Two of them, because a cosine near 1 is a flat signal: a Vector3.up reference misses this fixture
            // by 4 deg, which is 0.9973 against the first (a whisker past tolerance) but 0.073 against the
            // second. Together with the nose assertion above they pin the rotation exactly -- an up that is
            // perpendicular to both the nose and the body's forward can only be +-the vehicle's own up, and the
            // first assertion picks the sign.
            Assert.That(Vector3.Dot(go.transform.up, car.transform.forward), Is.EqualTo(0).Within(1e-3), "the horizon carries no roll of its own");
            Assert.That(Vector3.Dot(go.transform.up, car.transform.up), Is.EqualTo(1).Within(1e-3), "upright in the vehicle's own frame, so trim, heel and the ramp tilt it together");
            Object.DestroyImmediate(car); Object.DestroyImmediate(go);
        }
    }
}
