package com.shiphdmap.api.slots;

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
				cells.add(new double[] { x0, y0 });
			}
		cells.sort(Comparator.<double[]>comparingDouble(c -> -c[0]).thenComparingDouble(c -> c[1])); // bow (max x) first, then port→starboard ascending y
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

	static double[] bbox(double[][] ring) {
		double minX = Double.MAX_VALUE, minY = Double.MAX_VALUE, maxX = -Double.MAX_VALUE, maxY = -Double.MAX_VALUE;
		for (var pt : ring) { minX = Math.min(minX, pt[0]); minY = Math.min(minY, pt[1]); maxX = Math.max(maxX, pt[0]); maxY = Math.max(maxY, pt[1]); }
		return new double[] { minX, minY, maxX, maxY };
	}

	/** Even-odd ray casting on the x-y projection of a closed ring. */
	public static boolean contains(double[][] ring, double x, double y) {
		boolean in = false;
		for (int i = 0, j = ring.length - 1; i < ring.length; j = i++) {
			double xi = ring[i][0], yi = ring[i][1], xj = ring[j][0], yj = ring[j][1];
			if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) in = !in;
		}
		return in;
	}

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
	static boolean inCorridor(double x0, double y0, double x1, double y1, List<Lane> lanes, double gap) {
		double[][] samples = { { x0, y0 }, { x1, y0 }, { x1, y1 }, { x0, y1 }, { (x0 + x1) / 2, y0 }, { (x0 + x1) / 2, y1 }, { x0, (y0 + y1) / 2 }, { x1, (y0 + y1) / 2 }, { (x0 + x1) / 2, (y0 + y1) / 2 } };
		for (var l : lanes) {
			double r = l.widthM() / 2 + gap;
			for (var s : samples) if (distToPolyline(s[0], s[1], l.centerline()) < r) return true;
		}
		return false;
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
