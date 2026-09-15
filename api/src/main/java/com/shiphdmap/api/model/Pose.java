package com.shiphdmap.api.model;

import com.fasterxml.jackson.annotation.JsonProperty;

/** Ship attitude at the berth (spec §4.1). trim_deg is derived, not an input.
 * quayZM is annotated because Jackson's SNAKE_CASE strategy collapses consecutive capitals
 * ("ZM" -> "zm"), producing "quay_zm" instead of the spec's "quay_z_m". */
public record Pose(double draftFwdM, double draftAftM, double heelDeg, double headingDeg, double tideM,
	@JsonProperty("quay_z_m") double quayZM, double apLat, double apLon, String measuredAt) {
	public double trimDeg(double lppM) { return Math.toDegrees(Math.atan((draftAftM - draftFwdM) / lppM)); }
}
