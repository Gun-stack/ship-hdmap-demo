package com.shiphdmap.api.geo;

import tools.jackson.databind.JsonNode;
import tools.jackson.databind.ObjectMapper;
import java.util.Arrays;
import java.util.Locale;
import java.util.stream.Collectors;

/** WKT (with Z) for ST_GeomFromText and coordinate extraction from ST_AsGeoJSON output. */
public final class Wkt {
	private Wkt() {}
	private static final ObjectMapper JSON = new tools.jackson.databind.json.JsonMapper();

	static String xyz(double[] p) { return String.format(Locale.ROOT, "%s %s %s", p[0], p[1], p.length > 2 ? p[2] : 0.0); }

	public static String point(double[] p) { return "POINT Z(" + xyz(p) + ")"; }

	public static String lineString(double[][] pts) {
		return "LINESTRING Z(" + Arrays.stream(pts).map(Wkt::xyz).collect(Collectors.joining(",")) + ")";
	}

	public static String polygon(double[][] ring) {
		double[][] closed = ring;
		double[] a = ring[0], z = ring[ring.length - 1];
		if (a[0] != z[0] || a[1] != z[1]) { closed = Arrays.copyOf(ring, ring.length + 1); closed[ring.length] = a; }
		return "POLYGON Z((" + Arrays.stream(closed).map(Wkt::xyz).collect(Collectors.joining(",")) + "))";
	}

	public static String type(String geoJson) {
		try { return JSON.readTree(geoJson).get("type").asText(); } catch (Exception e) { throw new IllegalArgumentException("bad GeoJSON", e); }
	}

	/** Point -> one row; LineString -> points; Polygon -> exterior ring. */
	public static double[][] coords(String geoJson) {
		try {
			JsonNode g = JSON.readTree(geoJson); JsonNode c = g.get("coordinates");
			return switch (g.get("type").asText()) {
				case "Point" -> new double[][] { toXyz(c) };
				case "LineString" -> rows(c);
				case "Polygon" -> rows(c.get(0));
				default -> throw new IllegalArgumentException("unsupported geometry " + g.get("type"));
			};
		} catch (IllegalArgumentException e) { throw e; } catch (Exception e) { throw new IllegalArgumentException("bad GeoJSON", e); }
	}

	static double[][] rows(JsonNode arr) { double[][] r = new double[arr.size()][]; for (int i = 0; i < arr.size(); i++) r[i] = toXyz(arr.get(i)); return r; }
	static double[] toXyz(JsonNode p) { return new double[] { p.get(0).asDouble(), p.get(1).asDouble(), p.size() > 2 ? p.get(2).asDouble() : 0.0 }; }
}
