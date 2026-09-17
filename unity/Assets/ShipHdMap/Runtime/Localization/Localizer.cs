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

            double wr = Weight(sigmaR), wt = Weight(sigmaTheta), wa = Weight(sigmaAlpha);   // moved up: every exit needs it

            // Initial guess: closed form from the nearest marker (or the caller's previous estimate when given).
            Pose2D p;
            if (previous.HasValue) p = previous.Value;
            else { var nearest = used[0]; foreach (var u in used) if (u.o.r < nearest.o.r) nearest = u; p = ClosedForm(nearest.o, nearest.lm); }

            if (used.Count == 1 && !previous.HasValue)
            {
                var (sxy0, sps0) = Sigma(p, used, wr, wt, wa);
                return new LocalizerResult { ok = true, pose = p, nObs = 1, residualRms = 0, iterations = 0, sigmaXy = sxy0, sigmaPsiDeg = sps0 };
            }

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
                if (d == null) return new LocalizerResult { ok = false, pose = previous ?? p, nObs = used.Count, residualRms = ResidualRms(previous ?? p, used), iterations = iter };
                p.x += d[0]; p.y += d[1]; p.psiRad = ShipFrame.WrapRad(p.psiRad + d[2]);
                if (Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]) < StopDelta) break;
            }
            // Final residual after the last update
            double rms = ResidualRms(p, used);
            var (sxy, sps) = Sigma(p, used, wr, wt, wa);
            return new LocalizerResult { ok = true, pose = p, nObs = used.Count, residualRms = rms,
                iterations = Math.Min(iter, MaxIterations), sigmaXy = sxy, sigmaPsiDeg = sps };
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

        /// A = J^T W J at pose p. Same Jacobians as the solve loop and as CoverageAnalyzer.estimate; no residuals,
        /// because precision does not depend on how wrong we currently are.
        static double[,] Information(Pose2D p, List<(Observation o, LandmarkRef lm)> used, double wr, double wt, double wa)
        {
            double[,] A = new double[3, 3];
            foreach (var (o, lm) in used)
            {
                var res = Residual(p, o, lm);
                double[] jr = { -res.dx / res.rh, -res.dy / res.rh, 0 };
                double[] jt = { res.dy / (res.rh * res.rh), -res.dx / (res.rh * res.rh), -1 };
                double[] ja = { 0, 0, -1 };
                foreach (var (j, w) in new[] { (jr, wr), (jt, wt), (ja, wa) })
                    for (int i = 0; i < 3; i++) for (int k = 0; k < 3; k++) A[i, k] += w * j[i] * j[k];
            }
            return A;
        }

        /// Cofactor inverse of a 3x3; null when singular. Mirrors CoverageAnalyzer.invert3.
        static double[,] Invert3(double[,] a)
        {
            double det = a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1])
                       - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0])
                       + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
            if (Math.Abs(det) < 1e-12) return null;
            double[,] outM = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    int r0 = i == 0 ? 1 : 0, r1 = i == 2 ? 1 : 2, c0 = j == 0 ? 1 : 0, c1 = j == 2 ? 1 : 2;
                    double minor = a[r0, c0] * a[r1, c1] - a[r0, c1] * a[r1, c0];
                    outM[j, i] = ((i + j) % 2 == 0 ? minor : -minor) / det;   // transposed cofactor = adjugate
                }
            return outM;
        }

        /// Achievable (sigma_xy, sigma_psi_deg) at p, or (null, null) when A cannot be inverted.
        static (double?, double?) Sigma(Pose2D p, List<(Observation o, LandmarkRef lm)> used, double wr, double wt, double wa)
        {
            var c = Invert3(Information(p, used, wr, wt, wa));
            if (c == null) return (null, null);
            double vx = Math.Max(c[0, 0], 0), vy = Math.Max(c[1, 1], 0), vp = Math.Max(c[2, 2], 0);
            return (Math.Sqrt(vx + vy), Math.Sqrt(vp) * 180 / Math.PI);
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
