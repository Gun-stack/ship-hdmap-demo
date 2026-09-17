package com.shiphdmap.api.geo;

/** Ship Frame: x forward from AP, y port (+), z up, metres. Heading psi CCW from +x (degrees).
 * Georef.headingDeg is a different quantity: the bow bearing over ground, clockwise from true north (degrees), used only by toWgs84. */
public final class ShipFrame {
	private ShipFrame() {}

	public static final double METRES_PER_DEG_LAT = 111_320.0;

	/** Berth georeference: AP position and the bow bearing (true north, clockwise, degrees). */
	public record Georef(double apLat, double apLon, double headingDeg) {}

	/** (-180, 180] */
	public static double wrapDeg(double a) {
		a %= 360.0;
		if (a <= -180.0) a += 360.0; else if (a > 180.0) a -= 360.0;
		return a;
	}

	/** (-pi, pi]. Mirrors the C# ShipFrame.WrapRad. */
	public static double wrapRad(double a) {
		a %= 2 * Math.PI;
		if (a <= -Math.PI) a += 2 * Math.PI; else if (a > Math.PI) a -= 2 * Math.PI;
		return a;
	}

	/** Same mapping as the C# ShipFrame.ToUnity: (x, y, z) -> (x, z, -y). Kept for the shared vector file. */
	public static double[] toUnity(double x, double y, double z) { return new double[] { x, z, -y }; }

	/** Planar approximation (spec §3.5). Bow unit vector (e, n) = (sin h, cos h); port = left of bow = (-cos h, sin h). */
	public static double[] toWgs84(Georef g, double x, double y) {
		double h = Math.toRadians(g.headingDeg());
		double e = x * Math.sin(h) - y * Math.cos(h);
		double n = x * Math.cos(h) + y * Math.sin(h);
		double lat = g.apLat() + n / METRES_PER_DEG_LAT;
		double lon = g.apLon() + e / (METRES_PER_DEG_LAT * Math.cos(Math.toRadians(g.apLat())));
		return new double[] { lat, lon };
	}
}
