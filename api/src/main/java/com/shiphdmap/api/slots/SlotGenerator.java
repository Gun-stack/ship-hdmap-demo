package com.shiphdmap.api.slots;

import com.shiphdmap.api.geo.Rings;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Map;

/**
 * Grid-fill parking-slot generator (spec §9.2). Pure: no DB, no Spring.
 * ponytail: greedy grid fill aligned to the lashing pitch, no optimisation; upgrade path is 2D bin packing.
 */
public final class SlotGenerator {
	private SlotGenerator() {}

	public record Params(String vehicleClass, double gapLatM, double gapLonM, double lashingPitchM) {}
	public record Lane(String id, double[][] centerline, double widthM) {}
	public record Lashing(String id, double x, double y) {}
	public record Slot(String id, String deckId, double[][] polygon, double targetX, double targetY, String vehicleClass, String accessLaneId, List<String> lashingIds, int sequenceNo) {}
	public record Result(List<Slot> slots, double utilization, double lashingCoverage) {}
	record Vehicle(double lengthM, double widthM) {}

	public static final Params DEFAULTS = new Params("passenger", 0.30, 0.40, 0.75);
	static final Map<String, Vehicle> VEHICLES = Map.of("passenger", new Vehicle(4.8, 1.85)); // spec §5.3: one class for now
	static final double LASHING_REACH_M = 1.0;   // a corner "has" the nearest lashing point within this distance
	/** Straight run before the slot in the scenario planner's approach. MUST match Unity ScenarioPlanner.FinalRunM. */
	static final double FINAL_RUN_M = 5.0;
	static final double SWEEP_STEP_M = 0.5;      // vehicle poses sampled along the approach when testing it for obstacles
	static final double GRID_ORIGIN_M = 1.0;     // the generator's lashing grid starts 1 m inside the outline (ShipSeedBuilder)

	public static Result generate(String deckId, double[][] outline, double z, List<double[][]> obstacles, List<Lane> lanes, List<Lashing> lashings, Params p) {
		Vehicle v = VEHICLES.get(p.vehicleClass());
		if (v == null) throw new IllegalArgumentException("unknown vehicle_class " + p.vehicleClass());
		double cellX = ceilTo(v.lengthM() + p.gapLonM(), p.lashingPitchM()), cellY = ceilTo(v.widthM() + p.gapLatM(), p.lashingPitchM());
		double[] bb = bbox(outline);
		var cells = new ArrayList<double[]>(); // {x0, y0}
		for (double y0 = bb[1] + GRID_ORIGIN_M; y0 + v.widthM() <= bb[3] - p.gapLatM() + 1e-9; y0 += cellY)
			for (double x0 = bb[0] + GRID_ORIGIN_M; x0 + v.lengthM() <= bb[2] - p.gapLonM() + 1e-9; x0 += cellX) {
				double x1 = x0 + v.lengthM(), y1 = y0 + v.widthM();
				if (!contains(outline, x0, y0) || !contains(outline, x1, y0) || !contains(outline, x1, y1) || !contains(outline, x0, y1)) continue;
				if (hitsObstacle(x0, y0, x1, y1, obstacles, p.gapLatM())) continue;
				if (inCorridor(x0, y0, x1, y1, lanes, p.gapLatM())) continue;
				if (approachBlocked(x0, y0, x1, y1, lanes, obstacles, v)) continue;
				cells.add(new double[] { x0, y0 });
			}
		// Bow (max x) first, then port→starboard ascending y. The fill order is load-bearing: every approach runs astern of
		// its target, so filling bow-first means a vehicle never crosses ground that already holds a parked car.
		cells.sort(Comparator.<double[]>comparingDouble(c -> -c[0]).thenComparingDouble(c -> c[1]));
		var slots = new ArrayList<Slot>(); int withFour = 0;
		for (int i = 0; i < cells.size(); i++) {
			double x0 = cells.get(i)[0], y0 = cells.get(i)[1], x1 = x0 + v.lengthM(), y1 = y0 + v.widthM();
			double[][] ring = { { x0, y0, z }, { x1, y0, z }, { x1, y1, z }, { x0, y1, z }, { x0, y0, z } };
			var lash = nearestLashings(ring, lashings); if (lash.size() == 4) withFour++;
			double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
			slots.add(new Slot(String.format("PS-%s-%03d", deckId, i + 1), deckId, ring, cx, cy, p.vehicleClass(), nearestLane(cx, cy, lanes), lash, i + 1));
		}
		double util = slots.isEmpty() ? 0 : slots.size() * v.lengthM() * v.widthM() / ringArea(outline);
		return new Result(slots, util, slots.isEmpty() ? 0 : (double) withFour / slots.size());
	}

	static double ceilTo(double v, double step) { return Math.ceil(v / step - 1e-9) * step; }

	static double[] bbox(double[][] ring) { return Rings.bbox(ring); }

	/** Even-odd ray casting on the x-y projection of a closed ring. */
	public static boolean contains(double[][] ring, double x, double y) { return Rings.contains(ring, x, y); }

	/** Shoelace area of the x-y projection, always positive. */
	public static double ringArea(double[][] ring) {
		double a = 0;
		for (int i = 0, j = ring.length - 1; i < ring.length; j = i++) a += ring[j][0] * ring[i][1] - ring[i][0] * ring[j][1];
		return Math.abs(a) / 2;
	}

	/** ponytail: obstacles are tested by their bounding box grown by the lateral gap — exact for the generator's rectangular pillars. */
	static boolean hitsObstacle(double x0, double y0, double x1, double y1, List<double[][]> obstacles, double margin) {
		for (var o : obstacles) {
			double[] b = bbox(o);
			if (x0 < b[2] + margin && x1 > b[0] - margin && y0 < b[3] + margin && y1 > b[1] - margin) return true;
		}
		return false;
	}

	/** A slot is in a lane corridor if any of nine sample points (corners, edge midpoints, centre) is within width/2 + gap of the centreline. */
	// ponytail: 9-point sampling; a lane narrower than ~2.1 m or a concave notch could slip through — fine for rectangular demo decks.
	static boolean inCorridor(double x0, double y0, double x1, double y1, List<Lane> lanes, double gap) {
		double[][] samples = { { x0, y0 }, { x1, y0 }, { x1, y1 }, { x0, y1 }, { (x0 + x1) / 2, y0 }, { (x0 + x1) / 2, y1 }, { x0, (y0 + y1) / 2 }, { x1, (y0 + y1) / 2 }, { (x0 + x1) / 2, (y0 + y1) / 2 } };
		for (var l : lanes) {
			double r = l.widthM() / 2 + gap;
			for (var s : samples) if (distToPolyline(s[0], s[1], l.centerline()) < r) return true;
		}
		return false;
	}

	/**
	 * A cell you cannot drive into is not a parking slot. The scenario planner (spec §3.5) leaves the lane
	 * FINAL_RUN_M + |lateral offset| astern of the target, drives a 45° diagonal to (target.x − FINAL_RUN_M, target.y),
	 * then runs straight into the slot; this rejects cells whose exit point falls off the end of the lane and cells whose
	 * swept vehicle body hits an obstacle. Parked cars need no test: the bow-first order keeps the approach ground empty.
	 */
	static boolean approachBlocked(double x0, double y0, double x1, double y1, List<Lane> lanes, List<double[][]> obstacles, Vehicle v) {
		double cx = (x0 + x1) / 2, cy = (y0 + y1) / 2;
		String id = nearestLane(cx, cy, lanes);
		if (id == null) return true;
		double[][] line = lanes.stream().filter(l -> l.id().equals(id)).findFirst().orElseThrow().centerline();
		double laneY = nearestPoint(cx, cy, line)[1];
		double exitX = cx - FINAL_RUN_M - Math.abs(cy - laneY);
		if (distToPolyline(exitX, laneY, line) > 1e-6) return true;   // the exit point lies beyond the end of the lane
		double[][] path = { { exitX, laneY }, { cx - FINAL_RUN_M, cy }, { cx, cy } };
		for (int i = 0; i + 1 < path.length; i++) {
			double dx = path[i + 1][0] - path[i][0], dy = path[i + 1][1] - path[i][1], len = Math.hypot(dx, dy), psi = Math.atan2(dy, dx);
			int n = Math.max(1, (int) Math.ceil(len / SWEEP_STEP_M));
			for (int k = 0; k <= n; k++) {
				double t = (double) k / n;
				double[][] car = box(path[i][0] + dx * t, path[i][1] + dy * t, psi, v.lengthM(), v.widthM());
				for (var o : obstacles) { double[] b = bbox(o); if (overlapsConvex(car, box((b[0] + b[2]) / 2, (b[1] + b[3]) / 2, 0, b[2] - b[0], b[3] - b[1]))) return true; }
			}
		}
		return false;
	}

	/** Corners of a length × width rectangle centred on (cx, cy) and rotated by psi. */
	static double[][] box(double cx, double cy, double psi, double length, double width) {
		double c = Math.cos(psi), s = Math.sin(psi), hl = length / 2, hw = width / 2;
		double[][] out = new double[4][];
		double[][] local = { { hl, hw }, { hl, -hw }, { -hl, -hw }, { -hl, hw } };
		for (int i = 0; i < 4; i++) out[i] = new double[] { cx + local[i][0] * c - local[i][1] * s, cy + local[i][0] * s + local[i][1] * c };
		return out;
	}

	/** Separating-axis test for two convex polygons. */
	static boolean overlapsConvex(double[][] a, double[][] b) {
		for (double[][][] pair : new double[][][][] { { a, b }, { b, a } })
			for (int i = 0, j = pair[0].length - 1; i < pair[0].length; j = i++) {
				double nx = pair[0][j][1] - pair[0][i][1], ny = pair[0][i][0] - pair[0][j][0];
				double amin = Double.MAX_VALUE, amax = -Double.MAX_VALUE, bmin = Double.MAX_VALUE, bmax = -Double.MAX_VALUE;
				for (var q : pair[0]) { double d = q[0] * nx + q[1] * ny; amin = Math.min(amin, d); amax = Math.max(amax, d); }
				for (var q : pair[1]) { double d = q[0] * nx + q[1] * ny; bmin = Math.min(bmin, d); bmax = Math.max(bmax, d); }
				if (amax < bmin - 1e-9 || bmax < amin - 1e-9) return false;
			}
		return true;
	}

	/** Point on the polyline nearest to (x, y). */
	static double[] nearestPoint(double x, double y, double[][] line) {
		double best = Double.MAX_VALUE; double[] out = { line[0][0], line[0][1] };
		for (int i = 0; i + 1 < line.length; i++) {
			double ax = line[i][0], ay = line[i][1], dx = line[i + 1][0] - ax, dy = line[i + 1][1] - ay, len2 = dx * dx + dy * dy;
			double t = len2 < 1e-12 ? 0 : Math.max(0, Math.min(1, ((x - ax) * dx + (y - ay) * dy) / len2));
			double px = ax + t * dx, py = ay + t * dy, d = Math.hypot(x - px, y - py);
			if (d < best) { best = d; out = new double[] { px, py }; }
		}
		return out;
	}

	static double distToPolyline(double x, double y, double[][] line) {
		double best = Double.MAX_VALUE;
		for (int i = 0; i + 1 < line.length; i++) {
			double ax = line[i][0], ay = line[i][1], bx = line[i + 1][0], by = line[i + 1][1];
			double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
			double t = len2 < 1e-12 ? 0 : Math.max(0, Math.min(1, ((x - ax) * dx + (y - ay) * dy) / len2));
			double px = ax + t * dx, py = ay + t * dy;
			best = Math.min(best, Math.hypot(x - px, y - py));
		}
		return best;
	}

	static String nearestLane(double x, double y, List<Lane> lanes) {
		String best = null; double bestD = Double.MAX_VALUE;
		for (var l : lanes) { double d = distToPolyline(x, y, l.centerline()); if (d < bestD) { bestD = d; best = l.id(); } }
		return best;
	}

	/** Nearest lashing point to each of the four corners within LASHING_REACH_M, without repeats. */
	static List<String> nearestLashings(double[][] ring, List<Lashing> lashings) {
		var out = new LinkedHashSet<String>();
		for (int i = 0; i < 4; i++) {
			String best = null; double bestD = LASHING_REACH_M;
			for (var l : lashings) { double d = Math.hypot(l.x() - ring[i][0], l.y() - ring[i][1]); if (d < bestD && !out.contains(l.id())) { bestD = d; best = l.id(); } }
			if (best != null) out.add(best);
		}
		return List.copyOf(out);
	}
}
