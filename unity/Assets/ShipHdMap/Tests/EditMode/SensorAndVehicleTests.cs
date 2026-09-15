using System;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class SensorAndVehicleTests
    {
        const double D = Math.PI / 180.0;

        [Test]
        public void VisibilityRespectsFovDistanceAndViewAngle()
        {
            var v = new Pose2D { x = 0, y = 0, psiRad = 0 };
            var ahead = new LandmarkRef { id = "a", mx = 10, my = 0, phiRad = Math.PI };      // faces the vehicle
            var behind = new LandmarkRef { id = "b", mx = -10, my = 0, phiRad = 0 };
            var far = new LandmarkRef { id = "f", mx = 30, my = 0, phiRad = Math.PI };
            var backFacing = new LandmarkRef { id = "k", mx = 10, my = 0, phiRad = 0 };       // normal points away
            var side = new LandmarkRef { id = "s", mx = 0.1, my = 10, phiRad = -Math.PI / 2 }; // bearing ~89.4deg, outside 90deg fov half-angle 45
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, ahead, 90 * D, 25, 70 * D), Is.True);
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, behind, 90 * D, 25, 70 * D), Is.False);
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, far, 90 * D, 25, 70 * D), Is.False);
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, backFacing, 90 * D, 25, 70 * D), Is.False);
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, side, 90 * D, 25, 70 * D), Is.False);
        }

        [Test]
        public void NoiseIsDeterministicPerSeedAndZeroWhenSigmaZero()
        {
            var o = new Observation { id = "a", r = 10, thetaRad = 0.1, alphaRad = 0.2 };
            var n = new SensorNoise { sigmaR = 0.2, sigmaThetaRad = 1 * D, sigmaAlphaRad = 2 * D };
            var a = LandmarkSensor.AddNoise(o, n, new System.Random(5));
            var b = LandmarkSensor.AddNoise(o, n, new System.Random(5));
            Assert.That(a.r, Is.EqualTo(b.r));
            Assert.That(a.r, Is.Not.EqualTo(10));
            var z = LandmarkSensor.AddNoise(o, new SensorNoise { sigmaR = 0, sigmaThetaRad = 0, sigmaAlphaRad = 0 }, new System.Random(5));
            Assert.That(z.r, Is.EqualTo(10)); Assert.That(z.thetaRad, Is.EqualTo(0.1)); Assert.That(z.alphaRad, Is.EqualTo(0.2));
        }

        [Test]
        public void LaneFollowerWalksPolyline()
        {
            var line = new[] { new double[] { 0, 0, 10 }, new double[] { 10, 0, 10 }, new double[] { 10, 5, 10 } };
            var (p0, h0, e0) = LaneFollower.At(line, 0);
            Assert.That(p0, Is.EqualTo(new Vector2(0, 0))); Assert.That(h0, Is.EqualTo(0).Within(1e-9)); Assert.That(e0, Is.False);
            var (p1, h1, _) = LaneFollower.At(line, 12);
            Assert.That(p1.x, Is.EqualTo(10).Within(1e-6)); Assert.That(p1.y, Is.EqualTo(2).Within(1e-6));
            Assert.That(h1, Is.EqualTo(Math.PI / 2).Within(1e-9));   // heading +y after the corner
            var (p2, _, e2) = LaneFollower.At(line, 99);
            Assert.That(p2.y, Is.EqualTo(5).Within(1e-6)); Assert.That(e2, Is.True);
        }
    }
}
