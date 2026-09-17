package com.shiphdmap.api.coverage;

import com.shiphdmap.api.geo.ShipFrame;
import java.util.List;

/**
 * Landmark coverage: how well a vehicle could localise itself at each point of a deck (spec M5c §3).
 * Pure: no DB, no Spring, same shape as SlotGenerator.
 *
 * The estimate is the Cramer-Rao style lower bound of Localizer's Gauss-Newton: the same Jacobians and the same
 * 1/sigma^2 weights build A = J^T W J, and the diagonal of A^-1 is the achievable variance. No residuals are needed
 * because we are not estimating a pose, only asking how precisely one could be estimated from this spot.
 */
public final class CoverageAnalyzer {
	private CoverageAnalyzer() {}

	/** A map landmark: position and normal angle phi = atan2(ny, nx). */
	public record Landmark(String id, double x, double y, double phiRad) {}
	/** Vehicle sensor model. Angles in radians. */
	public record Sensor(double fovRad, double maxDistM, double maxViewAngleRad, double sigmaR, double sigmaTheta, double sigmaAlpha) {}
	/** Achievable precision at one spot. sigmaXy/sigmaPsiDeg are null when the spot is blind; n survives either way. */
	public record Estimate(int n, Double sigmaXy, Double sigmaPsiDeg) {}

	public static final Sensor DEFAULTS = new Sensor(Math.toRadians(90), 25, Math.toRadians(70),
		0.2, Math.toRadians(1), Math.toRadians(2));
	/** Judge's default tolerance: the bar a cell has to clear. */
	public static final double TOL_LAT_M = 0.15, TOL_HEADING_DEG = 2.0;
	static final double MIN_SIGMA = 1e-6;   // same clamp as Localizer.Weight, so sigma 0 cannot divide by zero

	/** Same three conditions as the Unity LandmarkSensor.IsVisibleGeometric. */
	static boolean visible(double x, double y, double psi, Landmark lm, Sensor s) {
		double dx = lm.x() - x, dy = lm.y() - y, r = Math.hypot(dx, dy);
		if (r > s.maxDistM() || r < 1e-6) return false;
		if (Math.abs(ShipFrame.wrapRad(Math.atan2(dy, dx) - psi)) > s.fovRad() / 2) return false;
		return Math.abs(ShipFrame.wrapRad(Math.atan2(-dy, -dx) - lm.phiRad())) <= s.maxViewAngleRad();
	}

	public static Estimate estimate(double x, double y, double psi, List<Landmark> lms, Sensor s) {
		double wr = weight(s.sigmaR()), wt = weight(s.sigmaTheta()), wa = weight(s.sigmaAlpha());
		double[][] A = new double[3][3];
		int n = 0;
		for (var lm : lms) {
			if (!visible(x, y, psi, lm, s)) continue;
			n++;
			double dx = lm.x() - x, dy = lm.y() - y, r = Math.hypot(dx, dy);
			accumulate(A, new double[] { -dx / r, -dy / r, 0 }, wr);
			accumulate(A, new double[] { dy / (r * r), -dx / (r * r), -1 }, wt);
			accumulate(A, new double[] { 0, 0, -1 }, wa);
		}
		if (n == 0) return new Estimate(0, null, null);
		double[][] c = invert3(A);
		if (c == null) return new Estimate(n, null, null);   // blind too: see stability() and analyze()
		double vx = Math.max(c[0][0], 0), vy = Math.max(c[1][1], 0), vp = Math.max(c[2][2], 0);
		return new Estimate(n, Math.sqrt(vx + vy), Math.toDegrees(Math.sqrt(vp)));
	}

	/** Margin against the parking tolerance; < 1 means this spot cannot meet it. null when blind. */
	public static Double stability(Estimate e) {
		if (e.sigmaXy() == null || e.sigmaPsiDeg() == null) return null;
		return Math.min(TOL_LAT_M / e.sigmaXy(), TOL_HEADING_DEG / e.sigmaPsiDeg());
	}

	static double weight(double sigma) { double v = Math.max(sigma, MIN_SIGMA); return 1 / (v * v); }

	static void accumulate(double[][] A, double[] j, double w) {
		for (int i = 0; i < 3; i++) for (int k = 0; k < 3; k++) A[i][k] += w * j[i] * j[k];
	}

	/**
	 * Cofactor inverse of a 3x3; null when singular. ponytail: direct inverse, fine for 3 unknowns.
	 * The singular branch is defensive and unreachable in practice - at the default sigmas w_theta is about 3283, so
	 * det A runs around 1e5 (measured minimum over the whole fixture deck: 1.095e+05).
	 */
	static double[][] invert3(double[][] a) {
		double det = a[0][0] * (a[1][1] * a[2][2] - a[1][2] * a[2][1])
			- a[0][1] * (a[1][0] * a[2][2] - a[1][2] * a[2][0])
			+ a[0][2] * (a[1][0] * a[2][1] - a[1][1] * a[2][0]);
		if (Math.abs(det) < 1e-12) return null;
		double[][] out = new double[3][3];
		for (int i = 0; i < 3; i++)
			for (int j = 0; j < 3; j++) {
				int r0 = i == 0 ? 1 : 0, r1 = i == 2 ? 1 : 2, c0 = j == 0 ? 1 : 0, c1 = j == 2 ? 1 : 2;
				double minor = a[r0][c0] * a[r1][c1] - a[r0][c1] * a[r1][c0];
				out[j][i] = ((i + j) % 2 == 0 ? minor : -minor) / det;   // transposed cofactor = adjugate
			}
		return out;
	}
}
