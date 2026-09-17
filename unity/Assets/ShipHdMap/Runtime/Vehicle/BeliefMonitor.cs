using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    public enum BeliefState { Ok, Degraded, Lost, Backtracking, Stopped }

    /// Spec M5d §3.4-3.6. Wire values are already converted: everything here is metres and plain ratios.
    [Serializable]
    public class BeliefParams
    {
        public double k = 2.0;            // "worse than the map promised" multiplier
        public int frames = 5;            // consecutive frames before Degraded trips
        public double driftRate = 0.05;   // odometry sigma growth per metre travelled
        public double budgetM = 1.0;      // odometry sigma that ends the wait
        public double maxLostM = 5.0;     // distance that ends the wait, whichever comes first
        public double trailM = 20.0;      // how far back the escape route reaches
        public double trailStepM = 0.25;  // trail recording interval
    }

    /// Decides whether the vehicle still knows where it is, and how far to retreat when it does not.
    /// The trail is arc length on the vehicle's current path: VehicleController is parameterised by s and already
    /// has Rewind(s), so retracing is simply driving s down. Deliberately free of UnityEngine, so EditMode tests
    /// run without a scene or a frame.
    public class BeliefMonitor
    {
        readonly BeliefParams _p;
        readonly List<double> _trail = new();   // arc lengths, oldest first
        int _degraded;
        double _sinceRecord;
        bool _hasBacktrackedThisPath;   // a path gets one retreat; a second loss on it pushes through instead

        public BeliefMonitor(BeliefParams p) { _p = p ?? new BeliefParams(); }

        public BeliefState State { get; private set; } = BeliefState.Ok;
        /// Distance travelled since the belief went Lost. Reset on recovery.
        public double LostM { get; private set; }
        /// Odometry uncertainty accumulated over LostM.
        public double SigmaOdo => _p.driftRate * LostM;
        /// Length of escape route currently held.
        public double TrailM => _trail.Count < 2 ? 0 : _trail[_trail.Count - 1] - _trail[0];
        /// Arc length to drive back to while backtracking; null when the trail is spent.
        public double? BacktrackTargetS => _trail.Count > 0 ? _trail[_trail.Count - 1] : (double?)null;

        public void ReachedBacktrackTarget() { if (_trail.Count > 0) _trail.RemoveAt(_trail.Count - 1); }

        /// Call on a new vehicle or whenever the path changes -- arc lengths from the old path mean nothing on a new
        /// one, and a new path earns a fresh retreat too.
        public void Reset() { State = BeliefState.Ok; LostM = 0; _degraded = 0; _sinceRecord = 0; _trail.Clear(); _hasBacktrackedThisPath = false; }

        /// One tick. sigmaXy is null when the solve produced nothing; predictedSigmaXy is null where the coverage
        /// map says the cell is blind, in which case there is nothing to hold the reading against.
        public void Step(double? sigmaXy, double? predictedSigmaXy, double pathS, double movedM)
        {
            if (State == BeliefState.Stopped) return;

            if (sigmaXy.HasValue)
            {
                // Any state recovers the moment a solve comes back: that is the whole point of retreating.
                LostM = 0;
                Record(pathS, movedM);
                bool worseThanPromised = predictedSigmaXy.HasValue && sigmaXy.Value > _p.k * predictedSigmaXy.Value;
                _degraded = worseThanPromised ? _degraded + 1 : 0;
                State = _degraded >= _p.frames ? BeliefState.Degraded : BeliefState.Ok;
                return;
            }

            _degraded = 0;
            if (State == BeliefState.Ok || State == BeliefState.Degraded) { State = BeliefState.Lost; LostM = 0; }

            if (State == BeliefState.Lost)
            {
                LostM += movedM;
                // The two limits mean different things: a spent odometry budget means dead reckoning can no longer
                // be trusted even to retrace the trail, so it always stops -- it never earns a retreat. The distance
                // limit earns a retreat only once per path; a second loss on the same path would just retreat into
                // the same gap forever, so it pushes through blind instead (spec §6.6: hold briefly on odometry,
                // stop if that runs out -- retreating twice into one gap is not "holding briefly").
                if (SigmaOdo > _p.budgetM) { State = BeliefState.Stopped; return; }
                if (LostM > _p.maxLostM && !_hasBacktrackedThisPath) { State = BeliefState.Backtracking; _hasBacktrackedThisPath = true; }
                return;
            }

            // Backtracking: MapRuntime drives the trail down; we only notice when it runs out.
            if (_trail.Count == 0) State = BeliefState.Stopped;
        }

        void Record(double pathS, double movedM)
        {
            _sinceRecord += movedM;
            if (_trail.Count > 0 && _sinceRecord < _p.trailStepM) return;
            _sinceRecord = 0;
            _trail.Add(pathS);
            // Cap by distance, not entry count: entries land wherever a frame overshoots trailStepM, so a count
            // cap only equals trailM metres when spacing happens to be exact. It rarely is (§ review M5d-A).
            while (_trail.Count > 2 && TrailM > _p.trailM) _trail.RemoveAt(0);
        }
    }
}
