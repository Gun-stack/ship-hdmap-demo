package com.shiphdmap.api.geo;

/** Closed-ring and polyline helpers on the x-y projection. Shared by SlotGenerator and CoverageAnalyzer. */
public final class Rings {
	private Rings() {}

	/** Even-odd ray casting on the x-y projection of a closed ring. */
	public static boolean contains(double[][] ring, double x, double y) {
		boolean in = false;
		for (int i = 0, j = ring.length - 1; i < ring.length; j = i++) {
			double xi = ring[i][0], yi = ring[i][1], xj = ring[j][0], yj = ring[j][1];
			if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) in = !in;
		}
		return in;
	}

	public static double[] bbox(double[][] ring) {
		double minX = Double.MAX_VALUE, minY = Double.MAX_VALUE, maxX = -Double.MAX_VALUE, maxY = -Double.MAX_VALUE;
		for (var p : ring) { minX = Math.min(minX, p[0]); minY = Math.min(minY, p[1]); maxX = Math.max(maxX, p[0]); maxY = Math.max(maxY, p[1]); }
		return new double[] { minX, minY, maxX, maxY };
	}

	/** Distance from (x, y) to a polyline, x-y only. Lane corridors are "within width/2 of the centreline". */
	public static double distToPolyline(double[][] pts, double x, double y) {
		double best = Double.MAX_VALUE;
		for (int i = 0; i + 1 < pts.length; i++) {
			double ax = pts[i][0], ay = pts[i][1], bx = pts[i + 1][0], by = pts[i + 1][1];
			double vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
			double t = l2 < 1e-12 ? 0 : Math.max(0, Math.min(1, ((x - ax) * vx + (y - ay) * vy) / l2));
			best = Math.min(best, Math.hypot(x - (ax + t * vx), y - (ay + t * vy)));
		}
		return best;
	}
}
