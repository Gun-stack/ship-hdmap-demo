package com.shiphdmap.api.coverage;

import com.shiphdmap.api.geo.Rings;
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

	/** A drivable lane: centreline plus width. The corridor is everything within width/2 of it. */
	public record Lane(double[][] centerline, double widthM) {}

	/**
	 * Where a vehicle can actually be (spec §3.4): inside a slot polygon, or inside a lane corridor.
	 * An empty scope means "nothing mapped": every cell counts, so the unit tests can use a bare rectangle.
	 */
	public record Scope(List<double[][]> slots, List<Lane> lanes) {
		public static final Scope ALL = new Scope(List.of(), List.of());
		boolean isEmpty() { return slots.isEmpty() && lanes.isEmpty(); }
		boolean has(double x, double y) {
			if (isEmpty()) return true;
			for (var l : lanes) if (Rings.distToPolyline(l.centerline(), x, y) <= l.widthM() / 2) return true;
			for (var s : slots) if (Rings.contains(s, x, y)) return true;
			return false;
		}
	}

	/** One evaluated grid point. sigma/stability are null when blind. inScope false means "drawn but not counted". */
	public record Cell(double x, double y, int n, Double sigmaXy, Double sigmaPsiDeg, Double stability, boolean inScope) {}
	/** nCells is the in-scope count AND the denominator of both ratios; nDrawn is cells.size(). They differ. */
	public record Result(List<Cell> cells, int nCells, int nDrawn, double blindRatio, double weakRatio, Cell worst) {}

	public static final double PSI_LOAD = 0.0, PSI_UNLOAD = Math.PI;
	static final double MOUNT_CLEARANCE_M = 0.05;   // shorten the sight line at the marker end so its own pillar never blocks it

	public static Result analyze(double[][] outline, List<double[][]> pillars, List<Landmark> lms, Scope scope,
			double psi, double gridM, Sensor s) {
		double[] b = Rings.bbox(outline);
		var cells = new java.util.ArrayList<Cell>();
		int inScope = 0, blind = 0, weak = 0;
		Cell worst = null;
		for (double y = b[1] + gridM / 2; y <= b[3]; y += gridM)
			for (double x = b[0] + gridM / 2; x <= b[2]; x += gridM) {
				if (!Rings.contains(outline, x, y)) continue;
				if (insideAny(pillars, x, y)) continue;
				boolean in = scope.has(x, y);
				var e = estimate(x, y, psi, visibleFrom(x, y, psi, lms, pillars, s), s);
				Double st = stability(e);
				var c = new Cell(x, y, e.n(), e.sigmaXy(), e.sigmaPsiDeg(), st, in);
				cells.add(c);
				if (!in) continue;
				inScope++;
				if (st == null) { blind++; continue; }   // n = 0 or singular: both are "cannot localise here"
				if (st < 1.0) weak++;
				if (worst == null || st < worst.stability()) worst = c;
			}
		return new Result(cells, inScope, cells.size(),
			inScope == 0 ? 0 : (double) blind / inScope, inScope == 0 ? 0 : (double) weak / inScope, worst);
	}

	/** The landmarks actually usable from (x, y): visible by geometry and not hidden by a pillar. */
	static List<Landmark> visibleFrom(double x, double y, double psi, List<Landmark> lms, List<double[][]> pillars, Sensor s) {
		var out = new java.util.ArrayList<Landmark>();
		for (var lm : lms) if (visible(x, y, psi, lm, s) && !occluded(x, y, lm, pillars)) out.add(lm);
		return out;
	}

	/** 2D segment test: pillars run floor to ceiling, so the plan view is exact rather than an approximation. */
	static boolean occluded(double x, double y, Landmark lm, List<double[][]> pillars) {
		double dx = lm.x() - x, dy = lm.y() - y, len = Math.hypot(dx, dy);
		if (len < 1e-9) return false;
		double tx = lm.x() - dx / len * MOUNT_CLEARANCE_M, ty = lm.y() - dy / len * MOUNT_CLEARANCE_M;
		for (var p : pillars)
			for (int i = 0; i + 1 < p.length; i++)
				if (segmentsCross(x, y, tx, ty, p[i][0], p[i][1], p[i + 1][0], p[i + 1][1])) return true;
		return false;
	}

	static boolean segmentsCross(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy) {
		double d1 = cross(cx, cy, dx, dy, ax, ay), d2 = cross(cx, cy, dx, dy, bx, by);
		double d3 = cross(ax, ay, bx, by, cx, cy), d4 = cross(ax, ay, bx, by, dx, dy);
		return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));   // proper crossings only; touching a corner does not occlude
	}

	static double cross(double ax, double ay, double bx, double by, double px, double py) {
		return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
	}

	static boolean insideAny(List<double[][]> rings, double x, double y) {
		for (var r : rings) if (Rings.contains(r, x, y)) return true;
		return false;
	}
}
