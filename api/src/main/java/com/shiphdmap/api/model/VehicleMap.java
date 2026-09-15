package com.shiphdmap.api.model;

import java.util.List;
import java.util.Map;

/** vehicle-map v1 (spec §6). Jackson is configured with SNAKE_CASE, so mapId -> "map_id". */
public record VehicleMap(String schema, String mapId, int version, String generatedAt, FrameInfo frame,
		List<Deck> decks, List<Landmark> landmarks, List<Lane> lanes, List<ParkingSlot> parkingSlots,
		List<LashingPoint> lashingPoints, List<Marking> markings, List<Facility> facilities, List<Ramp> ramps) {

	public static final String SCHEMA = "ship-hdmap/vehicle-map/1.0";
	public static FrameInfo shipFrame() {
		return new FrameInfo("SHIP_AP", "AP x Baseline x Centerline", Map.of("x", "AP->bow (+)", "y", "port (+)", "z", "baseline->up (+)"), "m");
	}

	public record FrameInfo(String name, String origin, Map<String, String> axes, String unit) {}
	public record Marker(String family, int code) {}
	public record Deck(String id, String name, double zSurface, double zClear, boolean movable, double[][] outline) {}
	public record Landmark(String id, Marker marker, double[] position, double[] normal, double sizeM, String deckId, String mountedOn) {}
	public record Lane(String id, String deckId, double[][] centerline, double widthM, String direction, double speedLimitKmh, List<String> next) {}
	public record TargetPose(double x, double y, double headingDeg) {}
	public record Tolerance(double latM, double lonM, double headingDeg) {}
	public record ParkingSlot(String id, String deckId, double[][] polygon, TargetPose targetPose, Tolerance tolerance, String vehicleClass,
			String accessLaneId, List<String> lashingPoints, int sequenceNo, String status) {}
	public record LashingPoint(String id, String kind, double[] position, String deckId) {}
	public record Marking(String id, String kind, double[][] polygon, String deckId) {}
	public record Facility(String id, String kind, double[][] footprint, double zMin, double zMax, String deckId) {}
	public record Ramp(String id, String type, double[][] hinge, double lengthM, double widthM, double[] angleRangeDeg, String connectsLane, List<String> transitionLandmarks) {}
}
