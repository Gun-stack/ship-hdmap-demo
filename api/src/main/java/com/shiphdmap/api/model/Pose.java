package com.shiphdmap.api.model;

/** Ship attitude at the berth (spec §4.1). trim_deg is derived, not an input. */
public record Pose(double draftFwdM, double draftAftM, double heelDeg, double headingDeg, double tideM, double quayZM, double apLat, double apLon, String measuredAt) {
	public double trimDeg(double lppM) { return Math.toDegrees(Math.atan((draftAftM - draftFwdM) / lppM)); }
}
