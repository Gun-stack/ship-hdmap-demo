using NUnit.Framework;
using ShipHdMap;

public class BeliefMonitorTests
{
    static BeliefParams P() => new BeliefParams();   // spec defaults: k 2, frames 5, drift .05, budget 1, maxLost 5, trail 20, step .25

    /// Drive forward with a healthy solve; arc length advances with the distance travelled.
    static BeliefMonitor Healthy(double metres, BeliefParams p = null)
    {
        var m = new BeliefMonitor(p ?? P());
        for (double s = 0; s < metres; s += 0.25) m.Step(0.30, 0.30, s, 0.25);
        return m;
    }

    /// Keep driving with nothing visible, starting from arc length s0. Returns where it ended up.
    static double GoBlind(BeliefMonitor m, double s0, int ticks)
    {
        double s = s0;
        for (int i = 0; i < ticks; i++) { m.Step(null, 0.30, s, 0.25); s += 0.25; }
        return s;
    }

    [Test]
    public void StartsOkAndStaysOkWhenTheSolveMatchesThePrediction()
    {
        var m = Healthy(10);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
        Assert.That(m.LostM, Is.Zero);
    }

    [Test]
    public void NoSigmaIsLostImmediately()
    {
        var m = Healthy(10);
        m.Step(null, 0.30, 10, 0.25);
        Assert.That(m.State, Is.EqualTo(BeliefState.Lost));
    }

    /// Worse than the map promised, but only after N frames in a row: one bad frame is noise.
    [Test]
    public void DegradedNeedsNConsecutiveFrames()
    {
        var m = Healthy(10);
        var p = P();
        for (int i = 0; i < p.frames - 1; i++) m.Step(0.90, 0.30, 10, 0);   // 3x the promise
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok), "N-1 frames must not trip it");
        m.Step(0.90, 0.30, 10, 0);
        Assert.That(m.State, Is.EqualTo(BeliefState.Degraded));
    }

    [Test]
    public void OneGoodFrameResetsTheDegradedCount()
    {
        var m = Healthy(10);
        var p = P();
        for (int i = 0; i < p.frames - 1; i++) m.Step(0.90, 0.30, 10, 0);
        m.Step(0.31, 0.30, 10, 0);                                          // within k
        for (int i = 0; i < p.frames - 1; i++) m.Step(0.90, 0.30, 10, 0);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
    }

    /// Nothing to compare against means nothing to be suspicious of; the blind path handles it.
    [Test]
    public void NoPredictionMeansNoDegradedJudgement()
    {
        var m = Healthy(10);
        for (int i = 0; i < 20; i++) m.Step(9.0, null, 10, 0);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
    }

    [Test]
    public void LostRecoversWhenMarkersComeBack()
    {
        var m = Healthy(10);
        double s = GoBlind(m, 10, 2);
        Assert.That(m.State, Is.EqualTo(BeliefState.Lost));
        m.Step(0.35, 0.30, s, 0.25);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
        Assert.That(m.LostM, Is.Zero, "the counter resets on recovery");
    }

    /// At the default 5 %/m the distance limit (5 m) binds before the sigma budget (1 m needs 20 m).
    [Test]
    public void DistanceLimitTripsBacktrackingAtDefaults()
    {
        var m = Healthy(10);
        GoBlind(m, 10, 21);                                                 // 5.25 m lost
        Assert.That(m.State, Is.EqualTo(BeliefState.Backtracking));
        Assert.That(m.LostM, Is.GreaterThan(5.0));
        Assert.That(m.SigmaOdo, Is.LessThan(P().budgetM), "the sigma budget did not fire");
    }

    /// Crank the drift and the sigma budget binds first instead -- both regimes must be reachable.
    [Test]
    public void SigmaBudgetTripsBacktrackingWhenDriftIsHigh()
    {
        var p = P(); p.driftRate = 0.5;                                     // 1.0 m of budget after 2 m
        var m = Healthy(10, p);
        GoBlind(m, 10, 9);                                                  // 2.25 m lost
        Assert.That(m.State, Is.EqualTo(BeliefState.Backtracking));
        Assert.That(m.LostM, Is.LessThan(p.maxLostM), "the distance limit did not fire");
        Assert.That(m.SigmaOdo, Is.GreaterThan(p.budgetM));
    }

    /// Backtracking hands back arc lengths, newest first, so the vehicle retraces the path it drove.
    [Test]
    public void BacktrackWalksTheTrailBackwards()
    {
        var m = Healthy(10);
        GoBlind(m, 10, 21);
        Assert.That(m.State, Is.EqualTo(BeliefState.Backtracking));
        double prev = double.MaxValue;
        for (int i = 0; i < 10; i++)
        {
            var t = m.BacktrackTargetS;
            Assert.That(t, Is.Not.Null);
            Assert.That(t.Value, Is.LessThan(prev), "targets must march backwards");
            prev = t.Value;
            m.ReachedBacktrackTarget();
        }
    }

    [Test]
    public void RunningOutOfTrailStops()
    {
        var p = P(); p.trailM = 1.0;                                        // a handful of entries
        var m = Healthy(10, p);
        double s = GoBlind(m, 10, 21);
        while (m.BacktrackTargetS != null) m.ReachedBacktrackTarget();
        m.Step(null, 0.30, s, 0.25);
        Assert.That(m.State, Is.EqualTo(BeliefState.Stopped));
    }

    [Test]
    public void StoppedIsTerminalUntilReset()
    {
        var p = P(); p.trailM = 1.0;
        var m = Healthy(10, p);
        double s = GoBlind(m, 10, 21);
        while (m.BacktrackTargetS != null) m.ReachedBacktrackTarget();
        m.Step(null, 0.30, s, 0.25);
        m.Step(0.30, 0.30, s, 0.25);                                        // even a good solve must not revive it
        Assert.That(m.State, Is.EqualTo(BeliefState.Stopped));
    }

    [Test]
    public void BacktrackingRecoversWhenMarkersReturn()
    {
        var m = Healthy(10);
        GoBlind(m, 10, 21);
        Assert.That(m.State, Is.EqualTo(BeliefState.Backtracking));
        m.Step(0.40, 0.30, 12, 0.25);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
    }

    /// The trail is why backtracking is safe: it is road already driven. Degraded stretches count too --
    /// the solve worked there, it was merely worse than promised, so backtracking into one recovers sooner.
    [Test]
    public void TheTrailIsCappedAndRecordsDegradedToo()
    {
        var p = P(); p.trailM = 2.0;
        var m = new BeliefMonitor(p);
        for (double s = 0; s < 20; s += 0.25) m.Step(0.90, 0.30, s, 0.25);   // all Degraded after N frames
        Assert.That(m.State, Is.EqualTo(BeliefState.Degraded));
        Assert.That(m.TrailM, Is.LessThanOrEqualTo(p.trailM + 1e-9));
        Assert.That(m.TrailM, Is.GreaterThan(1.0), "a degraded stretch still lays trail");
    }

    [Test]
    public void ResetClearsEverything()
    {
        var m = Healthy(10);
        m.Step(null, 0.30, 10, 0.25);
        m.Reset();
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
        Assert.That(m.TrailM, Is.Zero);
        Assert.That(m.BacktrackTargetS, Is.Null);
    }
}
