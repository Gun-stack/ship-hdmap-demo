using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    public static class Localizer
    {
        public const int MaxIterations = 5;
        public const double StopDelta = 1e-4;

        /// Noise-free observation of lm from truth: r, theta = bearing - psi, alpha = phi - psi.
        public static Observation Observe(Pose2D truth, LandmarkRef lm)
        {
            double dx = lm.mx - truth.x, dy = lm.my - truth.y;
            return new Observation
            {
                id = lm.id,
                r = Math.Sqrt(dx * dx + dy * dy),
                thetaRad = ShipFrame.WrapRad(Math.Atan2(dy, dx) - truth.psiRad),
                alphaRad = ShipFrame.WrapRad(lm.phiRad - truth.psiRad),
            };
        }

        /// N = 1: psi = phi - alpha; x = mx - r cos(psi + theta); y = my - r sin(psi + theta).
        public static Pose2D ClosedForm(Observation o, LandmarkRef lm)
        {
            double psi = ShipFrame.WrapRad(lm.phiRad - o.alphaRad);
            return new Pose2D { x = lm.mx - o.r * Math.Cos(psi + o.thetaRad), y = lm.my - o.r * Math.Sin(psi + o.thetaRad), psiRad = psi };
        }

        public static LocalizerResult Solve(IList<Observation> obs, IDictionary<string, LandmarkRef> map,
            double sigmaR, double sigmaTheta, double sigmaAlpha, Pose2D? previous)
        {
            var used = new List<(Observation o, LandmarkRef lm)>();
            foreach (var o in obs) if (map.TryGetValue(o.id, out var lm)) used.Add((o, lm));

            if (used.Count == 0)
                return new LocalizerResult { ok = false, pose = previous ?? default, nObs = 0 };

            // Initial guess: closed form from the nearest marker (or the caller's previous estimate when given).
            Pose2D p;
            if (previous.HasValue) p = previous.Value;
            else { var nearest = used[0]; foreach (var u in used) if (u.o.r < nearest.o.r) nearest = u; p = ClosedForm(nearest.o, nearest.lm); }

            if (used.Count == 1 && !previous.HasValue)
                return new LocalizerResult { ok = true, pose = p, nObs = 1, residualRms = 0, iterations = 0 };

            double wr = Weight(sigmaR), wt = Weight(sigmaTheta), wa = Weight(sigmaAlpha);
            int iter = 0;
            for (iter = 1; iter <= MaxIterations; iter++)
            {
                // Normal equations: (J^T W J) delta = J^T W e, 3x3
                double[,] A = new double[3, 3]; double[] b = new double[3];
                foreach (var (o, lm) in used)
                {
                    var res = Residual(p, o, lm);
                    double[] jr = { -res.dx / res.rh, -res.dy / res.rh, 0 };
                    double[] jt = { res.dy / (res.rh * res.rh), -res.dx / (res.rh * res.rh), -1 };
                    double[] ja = { 0, 0, -1 };
                    Accumulate(A, b, jr, wr, res.er); Accumulate(A, b, jt, wt, res.et); Accumulate(A, b, ja, wa, res.ea);
                }
                double[] d = Solve3(A, b);
                if (d == null) return new LocalizerResult { ok = false, pose = previous ?? p, nObs = used.Count, residualRms = 0, iterations = iter };
                p.x += d[0]; p.y += d[1]; p.psiRad = ShipFrame.WrapRad(p.psiRad + d[2]);
                if (Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]) < StopDelta) break;
            }
            // Final residual after the last update
            double rms = ResidualRms(p, used);
            return new LocalizerResult { ok = true, pose = p, nObs = used.Count, residualRms = rms, iterations = Math.Min(iter, MaxIterations) };
        }

        /// Per-marker prediction error at pose p: dx/dy/rh (for the Jacobian) plus range/bearing/normal residuals.
        static (double dx, double dy, double rh, double er, double et, double ea) Residual(Pose2D p, Observation o, LandmarkRef lm)
        {
            double dx = lm.mx - p.x, dy = lm.my - p.y;
            double rh = Math.Sqrt(dx * dx + dy * dy); if (rh < 1e-9) rh = 1e-9;
            double er = o.r - rh;
            double et = ShipFrame.WrapRad(o.thetaRad - (Math.Atan2(dy, dx) - p.psiRad));
            double ea = ShipFrame.WrapRad(o.alphaRad - (lm.phiRad - p.psiRad));
            return (dx, dy, rh, er, et, ea);
        }

        /// RMS of range/bearing/normal residuals across all used observations at pose p.
        static double ResidualRms(Pose2D p, List<(Observation o, LandmarkRef lm)> used)
        {
            double sumSq = 0; int n = 0;
            foreach (var (o, lm) in used)
            {
                var res = Residual(p, o, lm);
                sumSq += res.er * res.er + res.et * res.et + res.ea * res.ea; n += 3;
            }
            return Math.Sqrt(sumSq / n);
        }

        /// Weight = 1/sigma^2, sigma clamped away from 0 so a zero sensor-noise setting can't divide by zero into NaN/Infinity.
        static double Weight(double s) { s = Math.Max(s, 1e-6); return 1 / (s * s); }

        static void Accumulate(double[,] A, double[] b, double[] j, double w, double e)
        {
            for (int i = 0; i < 3; i++) { b[i] += w * j[i] * e; for (int k = 0; k < 3; k++) A[i, k] += w * j[i] * j[k]; }
        }

        /// Cramer's rule for the 3x3 normal equations. ponytail: direct inverse, fine for 3 unknowns; use Cholesky if this ever grows.
        /// Returns null when the system is singular (caller keeps the previous pose instead of reporting a bogus zero delta).
        static double[] Solve3(double[,] A, double[] b)
        {
            double det = Det3(A[0, 0], A[0, 1], A[0, 2], A[1, 0], A[1, 1], A[1, 2], A[2, 0], A[2, 1], A[2, 2]);
            if (Math.Abs(det) < 1e-12) return null;
            double d0 = Det3(b[0], A[0, 1], A[0, 2], b[1], A[1, 1], A[1, 2], b[2], A[2, 1], A[2, 2]);
            double d1 = Det3(A[0, 0], b[0], A[0, 2], A[1, 0], b[1], A[1, 2], A[2, 0], b[2], A[2, 2]);
            double d2 = Det3(A[0, 0], A[0, 1], b[0], A[1, 0], A[1, 1], b[1], A[2, 0], A[2, 1], b[2]);
            return new[] { d0 / det, d1 / det, d2 / det };
        }

        static double Det3(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
            => m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20);
    }
}
