using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShipHdMap;

public class LocalizerSigmaTests
{
    const double SR = 0.2, ST = Math.PI / 180, SA = 2 * Math.PI / 180;

    /// A marker at range r on bearing beta from the origin, its normal pointing back at the origin.
    static LandmarkRef At(string id, double r, double betaDeg)
    {
        double b = betaDeg * Math.PI / 180;
        return new LandmarkRef { id = id, mx = r * Math.Cos(b), my = r * Math.Sin(b), phiRad = ShipFrame.WrapRad(b + Math.PI) };
    }

    static (List<Observation>, Dictionary<string, LandmarkRef>) Scene(params LandmarkRef[] lms)
    {
        var map = new Dictionary<string, LandmarkRef>();
        var obs = new List<Observation>();
        var truth = new Pose2D { x = 0, y = 0, psiRad = 0 };
        foreach (var lm in lms) { map[lm.id] = lm; obs.Add(Localizer.Observe(truth, lm)); }
        return (obs, map);
    }

    /// Reference values computed from the same information matrix the API's CoverageAnalyzer builds.
    /// If these drift, the two implementations have diverged and the coverage heatmap is lying.
    [Test]
    public void SigmaMatchesTheCoverageAnalyzerReference()
    {
        var (obs, map) = Scene(At("a", 10, -30), At("b", 10, 30));
        var r = Localizer.Solve(obs, map, SR, ST, SA, new Pose2D());
        Assert.That(r.sigmaXy, Is.Not.Null);
        Assert.That(r.sigmaXy.Value, Is.EqualTo(0.251583).Within(1e-4), "sigma_xy for two markers at r=10, beta=+/-30");
        Assert.That(r.sigmaPsiDeg.Value, Is.EqualTo(1.051229).Within(1e-4));
    }

    [Test]
    public void OneMarkerStillYieldsSigma()
    {
        var (obs, map) = Scene(At("a", 10, 0));
        var r = Localizer.Solve(obs, map, SR, ST, SA, new Pose2D());
        Assert.That(r.sigmaXy.Value, Is.EqualTo(0.438530).Within(1e-4));
        // one marker leaves only ja = {0,0,-1}, so heading precision is exactly the sensor's alpha noise
        Assert.That(r.sigmaPsiDeg.Value, Is.EqualTo(2.0).Within(1e-6));
    }

    [Test]
    public void SurroundingMarkersBeatClusteredOnes()
    {
        var (o3, m3) = Scene(At("a", 10, -40), At("b", 10, 0), At("c", 10, 40));
        var spread = Localizer.Solve(o3, m3, SR, ST, SA, new Pose2D());
        Assert.That(spread.sigmaXy.Value, Is.EqualTo(0.200347).Within(1e-4));
        var (o2, m2) = Scene(At("a", 10, -30), At("b", 10, 30));
        var fewer = Localizer.Solve(o2, m2, SR, ST, SA, new Pose2D());
        Assert.That(spread.sigmaXy.Value, Is.LessThan(fewer.sigmaXy.Value));
    }

    /// The closed-form shortcut (one marker, no previous pose) skips the Gauss-Newton loop entirely.
    /// It must still report sigma, which is why Information() is rebuilt at the end rather than captured inside.
    [Test]
    public void TheClosedFormPathAlsoReportsSigma()
    {
        var (obs, map) = Scene(At("a", 10, 0));
        var r = Localizer.Solve(obs, map, SR, ST, SA, null);
        Assert.That(r.iterations, Is.Zero, "this is the closed-form path");
        Assert.That(r.sigmaXy, Is.Not.Null);
        Assert.That(r.sigmaXy.Value, Is.EqualTo(0.438530).Within(1e-4));
    }

    [Test]
    public void NoObservationsMeansNoSigma()
    {
        var r = Localizer.Solve(new List<Observation>(), new Dictionary<string, LandmarkRef>(), SR, ST, SA, new Pose2D());
        Assert.That(r.ok, Is.False);
        Assert.That(r.nObs, Is.Zero);
        Assert.That(r.sigmaXy, Is.Null);
        Assert.That(r.sigmaPsiDeg, Is.Null);
    }

    [Test]
    public void WorseSensorMeansWorseSigma()
    {
        var (obs, map) = Scene(At("a", 10, -30), At("b", 10, 30));
        var good = Localizer.Solve(obs, map, SR, ST, SA, new Pose2D());
        var bad = Localizer.Solve(obs, map, SR * 4, ST * 4, SA * 4, new Pose2D());
        Assert.That(bad.sigmaXy.Value, Is.GreaterThan(good.sigmaXy.Value));
        Assert.That(bad.sigmaPsiDeg.Value, Is.GreaterThan(good.sigmaPsiDeg.Value));
    }

    [Test]
    public void OccludedMarkersAreNotSeen()
    {
        var lm = At("a", 10, 0);
        var sensor = new UnityEngine.GameObject("s").AddComponent<LandmarkSensor>();
        var map = new Dictionary<string, LandmarkRef> { [lm.id] = lm };
        var truth = new Pose2D { x = 0, y = 0, psiRad = 0 };
        UnityEngine.Vector3 PosOf(string id) => new UnityEngine.Vector3((float)lm.mx, 1.2f, -(float)lm.my);

        Assert.That(sensor.Sense(truth, map, PosOf), Has.Count.EqualTo(1));
        sensor.occluded.Add("a");
        Assert.That(sensor.Sense(truth, map, PosOf), Is.Empty, "a damaged marker is simply not detected");
        UnityEngine.Object.DestroyImmediate(sensor.gameObject);
    }
}
