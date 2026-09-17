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

	/** A face a marker could be mounted on: midpoint plus the outward normal (the side the vehicle is on). */
	public record Candidate(double x, double y, double phiRad, String mountedOn) {}
	/** One greedy pick. Ratios are the state AFTER adopting this and every earlier suggestion; gain is the blind drop. */
	public record Suggestion(int rank, double x, double y, double phiDeg, String mountedOn,
		double blindAfter, double weakAfter, double gain) {}

	public static final int MAX_BUDGET = 10;
	/** Split long edges this often, so a 120 m bulkhead offers candidates at the same density as the pillars. */
	static final double FACE_SPACING_M = 12.0;
	/** Suggestions land on structure faces, which are metres apart; a finer grid buys nothing. Spec §4.2. */
	public static final double SUGGEST_GRID_M = 2.0;
	static final double CANDIDATE_CLEARANCE_M = 2.0;     // "the same spot" for the purpose of the rule below
	static final double FACE_ALIGN_RAD = Math.PI / 4;    // ...and "the same direction". Both must hold to exclude a face.
	static final double NORMAL_PROBE_M = 0.1;          // step off the face to decide which way is "outward"
	static final double BLIND_PENALTY = 10;            // one blind cell is worse than ten weak ones: different kinds of bad

	public static List<Candidate> candidates(double[][] outline, List<String> pillarIds, List<double[][]> pillars, List<Landmark> existing) {
		var out = new java.util.ArrayList<Candidate>();
		for (int p = 0; p < pillars.size(); p++) {
			String id = p < pillarIds.size() ? pillarIds.get(p) : "pillar-" + p;
			addFaces(out, pillars.get(p), id, true, existing);
		}
		addFaces(out, outline, "deck", false, existing);
		return out;
	}

	/** outward = true for pillars (normal points away from the ring), false for the deck outline (normal points inward). */
	static void addFaces(List<Candidate> out, double[][] ring, String mountedOn, boolean outward, List<Landmark> existing) {
		for (int i = 0; i + 1 < ring.length; i++) {
			double ax = ring[i][0], ay = ring[i][1];
			double ex = ring[i + 1][0] - ax, ey = ring[i + 1][1] - ay, len = Math.hypot(ex, ey);
			if (len < 1e-9) continue;
			double nx = -ey / len, ny = ex / len;                       // one of the two perpendiculars
			boolean probeInside = Rings.contains(ring, ax + ex / 2 + nx * NORMAL_PROBE_M, ay + ey / 2 + ny * NORMAL_PROBE_M);
			if (probeInside == outward) { nx = -nx; ny = -ny; }          // flip when it points the wrong way
			// wrapRad, not a bare atan2: flipping a zero component leaves -0.0, and atan2(-0.0, -1) is -pi where the
			// (-pi, pi] convention the rest of the code uses says pi. Same direction, but the value is what callers compare.
			double phi = ShipFrame.wrapRad(Math.atan2(ny, nx));
			int parts = Math.max(1, (int) Math.round(len / FACE_SPACING_M));   // a pillar face stays one point
			for (int k = 0; k < parts; k++) {
				double t = (k + 0.5) / parts, mx = ax + ex * t, my = ay + ey * t;
				if (alreadyCovered(mx, my, phi, existing)) continue;
				out.add(new Candidate(mx, my, phi, mountedOn));
			}
		}
	}

	/**
	 * A face is taken only when a marker is both close AND pointing the same way. Distance alone is wrong: the fixture
	 * pillars are 0.6 m across with a marker already on one face, so a 2 m radius would swallow all four faces of all
	 * 18 pillars and leave nothing aimed at the outer slot rows (spec §5.1.1).
	 */
	static boolean alreadyCovered(double x, double y, double phi, List<Landmark> existing) {
		for (var lm : existing)
			if (Math.hypot(lm.x() - x, lm.y() - y) < CANDIDATE_CLEARANCE_M
				&& Math.abs(ShipFrame.wrapRad(lm.phiRad() - phi)) < FACE_ALIGN_RAD) return true;
		return false;
	}

	/** Blind/weak counts over the in-scope cells only. The suggest path needs no heatmap, so out-of-scope cells are skipped entirely. */
	record Tally(int nCells, int blind, int weak) {
		double blindRatio() { return nCells == 0 ? 0 : (double) blind / nCells; }
		double weakRatio() { return nCells == 0 ? 0 : (double) weak / nCells; }
		double score() { return blind * BLIND_PENALTY + weak; }
	}

	static Tally tally(double[][] outline, List<double[][]> pillars, List<Landmark> lms, Scope scope,
			double psi, double gridM, Sensor s) {
		double[] b = Rings.bbox(outline);
		int n = 0, blind = 0, weak = 0;
		for (double y = b[1] + gridM / 2; y <= b[3]; y += gridM)
			for (double x = b[0] + gridM / 2; x <= b[2]; x += gridM) {
				if (!scope.has(x, y)) continue;                          // skipped before any geometry work
				if (!Rings.contains(outline, x, y) || insideAny(pillars, x, y)) continue;
				n++;
				Double st = stability(estimate(x, y, psi, visibleFrom(x, y, psi, lms, pillars, s), s));
				if (st == null) blind++;
				else if (st < 1.0) weak++;
			}
		return new Tally(n, blind, weak);
	}

	/**
	 * Greedy: adopt the candidate that lowers the score most, repeat. Blind cells outweigh weak ones.
	 * ponytail: every candidate re-walks the grid. Information only accumulates, so an ok cell can never turn bad and
	 * in principle only the currently blind/weak cells need revisiting; caching each cell's baseline A matrix would
	 * drop the marker factor too. The 2 m grid already makes this a few hundred ms, so neither is worth it yet.
	 */
	public static List<Suggestion> suggest(double[][] outline, List<double[][]> pillars, List<Landmark> lms,
			Scope scope, List<Candidate> cands, double psi, Sensor s, int budget) {
		int n = Math.min(Math.max(budget, 0), MAX_BUDGET);
		var chosen = new java.util.ArrayList<>(lms);
		var pool = new java.util.ArrayList<>(cands);
		var out = new java.util.ArrayList<Suggestion>();
		var cur = tally(outline, pillars, chosen, scope, psi, SUGGEST_GRID_M, s);
		for (int rank = 1; rank <= n && !pool.isEmpty(); rank++) {
			Candidate best = null;
			Tally bestT = null;
			for (var c : pool) {
				var trial = new java.util.ArrayList<>(chosen);
				trial.add(new Landmark("CAND", c.x(), c.y(), c.phiRad()));
				var t = tally(outline, pillars, trial, scope, psi, SUGGEST_GRID_M, s);
				if (t.score() < (bestT == null ? cur.score() : bestT.score())) { bestT = t; best = c; }
			}
			if (best == null) break;                                     // nothing improves anything
			// + 0.0 turns -0.0 into 0.0: a face whose normal is exactly +x comes out of atan2 as -0.0, and the panel
			// would render the perfectly ordinary "forward" as "-0.0 deg".
			out.add(new Suggestion(rank, best.x(), best.y(), Math.toDegrees(best.phiRad()) + 0.0, best.mountedOn(),
				bestT.blindRatio(), bestT.weakRatio(), cur.blindRatio() - bestT.blindRatio()));
			chosen.add(new Landmark("CAND-" + rank, best.x(), best.y(), best.phiRad()));
			pool.remove(best);
			cur = bestT;
		}
		return out;
	}
}
