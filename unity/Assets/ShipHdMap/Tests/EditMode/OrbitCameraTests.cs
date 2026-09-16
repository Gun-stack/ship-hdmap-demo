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
    }
}
