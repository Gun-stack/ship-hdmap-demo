package com.shiphdmap.api.model;

import com.fasterxml.jackson.annotation.JsonProperty;

/** Ship attitude at the berth (spec §4.1). trim_deg is derived, not an input.
 * Fields are boxed so PUT can be a partial update: a field omitted from the request body
 * deserializes to null and PoseStore.put merges it in from the current pose instead of zeroing it.
 * quayZM is annotated because Jackson's SNAKE_CASE strategy collapses consecutive capitals
 * ("ZM" -> "zm"), producing "quay_zm" instead of the spec's "quay_z_m". */
public record Pose(Double draftFwdM, Double draftAftM, Double heelDeg, Double headingDeg, Double tideM,
	@JsonProperty("quay_z_m") Double quayZM, Double apLat, Double apLon, String measuredAt) {
	public double trimDeg(double lppM) { return Math.toDegrees(Math.atan((draftAftM.doubleValue() - draftFwdM.doubleValue()) / lppM)); }
}
