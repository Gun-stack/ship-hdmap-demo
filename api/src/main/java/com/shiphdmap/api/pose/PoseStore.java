package com.shiphdmap.api.pose;

import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.geo.ShipFrame;
import com.shiphdmap.api.model.Pose;
import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;

/** Ship attitude is state, not map data: kept in memory per dataset (spec §4.1). Defaults come from the dataset's berth georef. */
@Service
public class PoseStore {
	private final JdbcClient db;
	private final Map<String, Pose> poses = new ConcurrentHashMap<>();
	public PoseStore(JdbcClient db) { this.db = db; }

	public record RampState(double angleDeg, String state) {}

	public Pose get(String ds) {
		Pose p = poses.get(ds);
		if (p != null) return p;
		Map<String, Object> d = db.sql("SELECT ap_lat, ap_lon, heading_deg FROM dataset WHERE id = :id").param("id", ds).query().listOfRows().stream().findFirst().orElseThrow(() -> new ApiErrors.NotFound("dataset " + ds));
		return new Pose(8.1, 8.6, 0, ((Number) d.get("heading_deg")).doubleValue(), 0, 3.5, ((Number) d.get("ap_lat")).doubleValue(), ((Number) d.get("ap_lon")).doubleValue(), null);
	}

	public Pose put(String ds, Pose p) {
		get(ds); // 404 if the dataset does not exist
		if (p.draftFwdM() <= 0) throw new ApiErrors.BadRequest("draft_fwd_m must be positive", "draft_fwd_m");
		if (p.draftAftM() <= 0) throw new ApiErrors.BadRequest("draft_aft_m must be positive", "draft_aft_m");
		poses.put(ds, p);
		return p;
	}

	/** Test only: resets in-memory poses between tests. */
	public void clear() { poses.clear(); }

	public double lppM(String ds) { return db.sql("SELECT lpp_m FROM dataset WHERE id = :id").param("id", ds).query(Double.class).optional().orElseThrow(() -> new ApiErrors.NotFound("dataset " + ds)); }

	/** Pose wins over the dataset's stored berth values (spec §5.4). */
	public ShipFrame.Georef georef(String ds) { Pose p = get(ds); return new ShipFrame.Georef(p.apLat(), p.apLon(), p.headingDeg()); }

	/** Spec §4.2: angle = asin(((quay_z + tide) - (hinge_z - draft_aft)) / length). */
	public static RampState rampState(double hingeZ, double lengthM, double[] range, Pose pose) {
		double rise = (pose.quayZM() + pose.tideM()) - (hingeZ - pose.draftAftM());
		double ratio = rise / lengthM;
		double angle = Math.abs(ratio) > 1 ? Math.copySign(90, ratio) : Math.toDegrees(Math.asin(ratio));
		boolean ok = Math.abs(ratio) <= 1 && angle >= range[0] && angle <= range[1];
		return new RampState(angle, ok ? "deployed" : "blocked");
	}
}
