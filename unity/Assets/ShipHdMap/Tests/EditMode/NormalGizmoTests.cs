using System;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class NormalGizmoTests
    {
        /// The handle reads an angle in Ship Frame, never in Unity's -- the marker's stored normal is Ship Frame
        /// and a sign slip here would mirror every rotation about the centreline.
        [Test]
        public void PhiAtReadsShipFrameAngles()
        {
            var c = ShipFrame.ToUnity(50, 0, 11);
            Assert.That(NormalGizmo.PhiAt(c, ShipFrame.ToUnity(55, 0, 11)), Is.EqualTo(0).Within(1e-6));
            Assert.That(NormalGizmo.PhiAt(c, ShipFrame.ToUnity(50, 5, 11)), Is.EqualTo(Math.PI / 2).Within(1e-6));
            Assert.That(NormalGizmo.PhiAt(c, ShipFrame.ToUnity(50, -5, 11)), Is.EqualTo(-Math.PI / 2).Within(1e-6));
        }

        /// M5c §5.1.1's trap: atan2(-0.0, -1) is -pi, and an unwrapped -pi is a different number from pi
        /// for every downstream comparison. WrapRad's half-open interval is the fix; this pins it.
        [Test]
        public void PhiAtWrapsToTheHalfOpenInterval()
        {
            var c = ShipFrame.ToUnity(50, 0, 11);
            double a = NormalGizmo.PhiAt(c, ShipFrame.ToUnity(45, -0.0, 11));
            Assert.That(a, Is.EqualTo(Math.PI).Within(1e-9));
            Assert.That(a, Is.GreaterThan(-Math.PI));
        }

        /// Height must not leak into the angle: the handle is a circle on the deck plane, and dragging the
        /// mouse a little high should not swing the normal.
        [Test]
        public void PhiAtIgnoresHeight()
        {
            var c = ShipFrame.ToUnity(50, 0, 11);
            Assert.That(NormalGizmo.PhiAt(c, ShipFrame.ToUnity(55, 0, 14)), Is.EqualTo(0).Within(1e-6));
        }
    }
}
