package com.shiphdmap.api.pose;

import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.dataset.Datasets;
import com.shiphdmap.api.feature.FeatureRepo;
import com.shiphdmap.api.model.Pose;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/api/datasets/{ds}")
public class PoseController {
	private final PoseStore poses;
	private final FeatureRepo features;
	private final JdbcClient db;
	public PoseController(PoseStore poses, FeatureRepo features, JdbcClient db) { this.poses = poses; this.features = features; this.db = db; }

	@GetMapping("/pose")
	public Map<String, Object> get(@PathVariable String ds) { return withTrim(ds, poses.get(ds)); }

	@PutMapping("/pose")
	public Map<String, Object> put(@PathVariable String ds, @RequestBody Pose pose) { return withTrim(ds, poses.put(ds, pose)); }

	/** Ramp geometry from the feature row plus the pose-derived angle/state. */
	@GetMapping("/ramps/{rid}")
	@SuppressWarnings("unchecked")
	public Map<String, Object> ramp(@PathVariable String ds, @PathVariable String rid) {
		Datasets.require(db, ds);
		Map<String, Object> f = features.get(ds, rid).filter(x -> "ramp".equals(x.get("kind"))).orElseThrow(() -> new ApiErrors.NotFound("ramp " + rid));
		Map<String, Object> props = (Map<String, Object>) f.get("props");
		List<List<Number>> hinge = (List<List<Number>>) ((Map<String, Object>) f.get("geometry")).get("coordinates");
		List<Number> range = (List<Number>) props.get("angle_range_deg");
		PoseStore.RampState st = PoseStore.rampState(hinge.get(0).get(2).doubleValue(), ((Number) props.get("length_m")).doubleValue(),
			new double[] { range.get(0).doubleValue(), range.get(1).doubleValue() }, poses.get(ds));
		var out = new LinkedHashMap<String, Object>(props);
		out.put("id", rid); out.put("deck_id", f.get("deck_id")); out.put("hinge", hinge); out.put("angle_deg", st.angleDeg()); out.put("state", st.state());
		return out;
	}

	Map<String, Object> withTrim(String ds, Pose p) {
		var out = new LinkedHashMap<String, Object>();
		out.put("draft_fwd_m", p.draftFwdM()); out.put("draft_aft_m", p.draftAftM()); out.put("heel_deg", p.heelDeg()); out.put("heading_deg", p.headingDeg());
		out.put("tide_m", p.tideM()); out.put("quay_z_m", p.quayZM()); out.put("ap_lat", p.apLat()); out.put("ap_lon", p.apLon()); out.put("measured_at", p.measuredAt());
		out.put("trim_deg", p.trimDeg(poses.lppM(ds)));
		return out;
	}
}
