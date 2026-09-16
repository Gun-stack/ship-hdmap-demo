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
    }
}
