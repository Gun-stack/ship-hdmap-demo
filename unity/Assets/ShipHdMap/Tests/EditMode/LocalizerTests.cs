using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class LocalizerTests
    {
        const double D = Math.PI / 180.0;

        static Dictionary<string, LandmarkRef> Map() => new Dictionary<string, LandmarkRef>
        {
            ["A"] = new LandmarkRef { id = "A", mx = 4.0, my = -6.5, phiRad = 90 * D },   // normal +y
            ["B"] = new LandmarkRef { id = "B", mx = 4.0, my = 6.5, phiRad = -90 * D },   // normal -y
            ["C"] = new LandmarkRef { id = "C", mx = 40.0, my = 11.9, phiRad = -90 * D },
        };

        [Test]
        public void ObserveThenClosedFormRecoversPose()
        {
            var truth = new Pose2D { x = 10, y = 1.5, psiRad = 20 * D };
            foreach (var lm in Map().Values)
            {
                var o = Localizer.Observe(truth, lm);
                var p = Localizer.ClosedForm(o, lm);
                Assert.That(p.x, Is.EqualTo(truth.x).Within(1e-6), lm.id);
                Assert.That(p.y, Is.EqualTo(truth.y).Within(1e-6), lm.id);
                Assert.That(ShipFrame.WrapRad(p.psiRad - truth.psiRad), Is.EqualTo(0).Within(1e-9), lm.id);
            }
        }

        [Test]
        public void ObserveGeometry()
        {
            // vehicle at origin heading +x; marker straight ahead at (5,0) facing -x: r=5, theta=0, alpha=180deg
            var o = Localizer.Observe(new Pose2D(), new LandmarkRef { id = "F", mx = 5, my = 0, phiRad = Math.PI });
            Assert.That(o.r, Is.EqualTo(5).Within(1e-9));
            Assert.That(o.thetaRad, Is.EqualTo(0).Within(1e-9));
            Assert.That(Math.Abs(o.alphaRad), Is.EqualTo(Math.PI).Within(1e-9));
            // marker to the left (+y) -> theta = +90deg
            var l = Localizer.Observe(new Pose2D(), new LandmarkRef { id = "L", mx = 0, my = 3, phiRad = -Math.PI / 2 });
            Assert.That(l.thetaRad, Is.EqualTo(Math.PI / 2).Within(1e-9));
        }

        [Test]
        public void GaussNewtonThreeMarkersNoNoise()
        {
            var truth = new Pose2D { x = 12, y = -2, psiRad = -15 * D };
            var obs = new List<Observation>();
            foreach (var lm in Map().Values) obs.Add(Localizer.Observe(truth, lm));
            var res = Localizer.Solve(obs, Map(), 0.2, 1 * D, 2 * D, null);
            Assert.That(res.ok);
            Assert.That(res.nObs, Is.EqualTo(3));
            Assert.That(res.pose.x, Is.EqualTo(truth.x).Within(0.05));
            Assert.That(res.pose.y, Is.EqualTo(truth.y).Within(0.05));
            Assert.That(ShipFrame.WrapRad(res.pose.psiRad - truth.psiRad), Is.EqualTo(0).Within(0.5 * D));
            Assert.That(res.residualRms, Is.LessThan(1e-6));
        }

        [Test]
        public void GaussNewtonConvergesFromBadInitial()
        {
            var truth = new Pose2D { x = 12, y = -2, psiRad = -15 * D };
            var obs = new List<Observation>();
            foreach (var lm in Map().Values) obs.Add(Localizer.Observe(truth, lm));
            var bad = new Pose2D { x = 14, y = 0, psiRad = 0 };
            var res = Localizer.Solve(obs, Map(), 0.2, 1 * D, 2 * D, bad);
            Assert.That(res.pose.x, Is.EqualTo(truth.x).Within(0.05));
            Assert.That(res.pose.y, Is.EqualTo(truth.y).Within(0.05));
        }

        [Test]
        public void NoiseAveragesOut()
        {
            var truth = new Pose2D { x = 12, y = -2, psiRad = 0 };
            var rng = new Random(7);
            double Gauss(double s) { double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble(); return s * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
            double errSingle = 0, errMulti = 0; int trials = 200;
            for (int t = 0; t < trials; t++)
            {
                var obs = new List<Observation>();
                foreach (var lm in Map().Values)
                {
                    var o = Localizer.Observe(truth, lm);
                    obs.Add(new Observation { id = o.id, r = o.r + Gauss(0.2), thetaRad = o.thetaRad + Gauss(1 * D), alphaRad = o.alphaRad + Gauss(2 * D) });
                }
                var single = Localizer.ClosedForm(obs[0], Map()["A"]);
                var multi = Localizer.Solve(obs, Map(), 0.2, 1 * D, 2 * D, null).pose;
                errSingle += Math.Sqrt(Math.Pow(single.x - truth.x, 2) + Math.Pow(single.y - truth.y, 2));
                errMulti += Math.Sqrt(Math.Pow(multi.x - truth.x, 2) + Math.Pow(multi.y - truth.y, 2));
            }
            Assert.That(errMulti / trials, Is.LessThan(errSingle / trials));
        }

        [Test]
        public void ZeroObservationsKeepsPrevious()
        {
            var prev = new Pose2D { x = 1, y = 2, psiRad = 0.3 };
            var res = Localizer.Solve(new List<Observation>(), Map(), 0.2, 1 * D, 2 * D, prev);
            Assert.That(res.ok, Is.False);
            Assert.That(res.pose.x, Is.EqualTo(1));
            Assert.That(res.nObs, Is.EqualTo(0));
        }

        [Test]
        public void UnknownMarkerIsIgnored()
        {
            var truth = new Pose2D { x = 12, y = -2, psiRad = 0 };
            var obs = new List<Observation> { Localizer.Observe(truth, Map()["A"]), new Observation { id = "ZZZ", r = 1, thetaRad = 0, alphaRad = 0 } };
            var res = Localizer.Solve(obs, Map(), 0.2, 1 * D, 2 * D, null);
            Assert.That(res.nObs, Is.EqualTo(1));
            Assert.That(res.pose.x, Is.EqualTo(truth.x).Within(1e-6));
        }
    }
}
