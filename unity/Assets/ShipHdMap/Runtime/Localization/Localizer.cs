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

            double wr = 1 / (sigmaR * sigmaR), wt = 1 / (sigmaTheta * sigmaTheta), wa = 1 / (sigmaAlpha * sigmaAlpha);
            int iter = 0; double rms = 0;
            for (iter = 1; iter <= MaxIterations; iter++)
            {
                // Normal equations: (J^T W J) delta = J^T W e, 3x3
                double[,] A = new double[3, 3]; double[] b = new double[3]; double sumSq = 0; int n = 0;
                foreach (var (o, lm) in used)
                {
                    double dx = lm.mx - p.x, dy = lm.my - p.y;
                    double rh = Math.Sqrt(dx * dx + dy * dy); if (rh < 1e-9) rh = 1e-9;
                    double er = o.r - rh;
                    double et = ShipFrame.WrapRad(o.thetaRad - (Math.Atan2(dy, dx) - p.psiRad));
                    double ea = ShipFrame.WrapRad(o.alphaRad - (lm.phiRad - p.psiRad));
                    double[] jr = { -dx / rh, -dy / rh, 0 };
                    double[] jt = { dy / (rh * rh), -dx / (rh * rh), -1 };
                    double[] ja = { 0, 0, -1 };
                    Accumulate(A, b, jr, wr, er); Accumulate(A, b, jt, wt, et); Accumulate(A, b, ja, wa, ea);
                    sumSq += er * er + et * et + ea * ea; n += 3;
                }
                rms = Math.Sqrt(sumSq / n);
                double[] d = Solve3(A, b);
                p.x += d[0]; p.y += d[1]; p.psiRad = ShipFrame.WrapRad(p.psiRad + d[2]);
                if (Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]) < StopDelta) break;
            }
            // Final residual after the last update
            { double sumSq = 0; int n = 0;
              foreach (var (o, lm) in used)
              { double dx = lm.mx - p.x, dy = lm.my - p.y, rh = Math.Sqrt(dx * dx + dy * dy);
                double er = o.r - rh, et = ShipFrame.WrapRad(o.thetaRad - (Math.Atan2(dy, dx) - p.psiRad)), ea = ShipFrame.WrapRad(o.alphaRad - (lm.phiRad - p.psiRad));
                sumSq += er * er + et * et + ea * ea; n += 3; }
              rms = Math.Sqrt(sumSq / n); }
            return new LocalizerResult { ok = true, pose = p, nObs = used.Count, residualRms = rms, iterations = Math.Min(iter, MaxIterations) };
        }

        static void Accumulate(double[,] A, double[] b, double[] j, double w, double e)
        {
            for (int i = 0; i < 3; i++) { b[i] += w * j[i] * e; for (int k = 0; k < 3; k++) A[i, k] += w * j[i] * j[k]; }
        }

        /// Cramer's rule for the 3x3 normal equations. ponytail: direct inverse, fine for 3 unknowns; use Cholesky if this ever grows.
        static double[] Solve3(double[,] A, double[] b)
        {
            double det = Det3(A[0, 0], A[0, 1], A[0, 2], A[1, 0], A[1, 1], A[1, 2], A[2, 0], A[2, 1], A[2, 2]);
            if (Math.Abs(det) < 1e-12) return new double[3];
            double d0 = Det3(b[0], A[0, 1], A[0, 2], b[1], A[1, 1], A[1, 2], b[2], A[2, 1], A[2, 2]);
            double d1 = Det3(A[0, 0], b[0], A[0, 2], A[1, 0], b[1], A[1, 2], A[2, 0], b[2], A[2, 2]);
            double d2 = Det3(A[0, 0], A[0, 1], b[0], A[1, 0], A[1, 1], b[1], A[2, 0], A[2, 1], b[2]);
            return new[] { d0 / det, d1 / det, d2 / det };
        }

        static double Det3(double a, double b, double c, double d, double e, double f, double g, double h, double i)
            => a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }
}
