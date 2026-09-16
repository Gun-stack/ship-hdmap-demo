package com.shiphdmap.api.slots;

import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.dataset.Datasets;
import com.shiphdmap.api.geo.Wkt;
import com.shiphdmap.api.model.VehicleMap.ParkingSlot;
import com.shiphdmap.api.model.VehicleMap.TargetPose;
import com.shiphdmap.api.model.VehicleMap.Tolerance;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.bind.annotation.*;

/** Spec §6: POST /datasets/{ds}/decks/{deck}/slots/generate — regenerates every parking slot on one deck. */
@RestController
@RequestMapping("/api/datasets/{ds}/decks/{deck}/slots")
public class SlotController {
	private final JdbcClient db; private final SlotRepo slots;
	public SlotController(JdbcClient db, SlotRepo slots) { this.db = db; this.slots = slots; }

	public record GenerateIn(String vehicleClass, Double gapLatM, Double gapLonM, Double lashingPitchM) {}

	@PostMapping("/generate")
	@Transactional
	public Map<String, Object> generate(@PathVariable String ds, @PathVariable String deck, @RequestBody(required = false) GenerateIn in) {
		Datasets.require(db, ds);
		var d = db.sql("SELECT z_surface, ST_AsGeoJSON(outline)::text AS g FROM deck WHERE dataset_id = :ds AND id = :deck").param("ds", ds).param("deck", deck)
			.query().listOfRows().stream().findFirst().orElseThrow(() -> new ApiErrors.NotFound("deck " + deck));
		double z = ((Number) d.get("z_surface")).doubleValue(); double[][] outline = Wkt.coords((String) d.get("g"));
		var p = new SlotGenerator.Params(
			in == null || in.vehicleClass() == null ? SlotGenerator.DEFAULTS.vehicleClass() : in.vehicleClass(),
			in == null || in.gapLatM() == null ? SlotGenerator.DEFAULTS.gapLatM() : in.gapLatM(),
			in == null || in.gapLonM() == null ? SlotGenerator.DEFAULTS.gapLonM() : in.gapLonM(),
			in == null || in.lashingPitchM() == null ? SlotGenerator.DEFAULTS.lashingPitchM() : in.lashingPitchM());
		if (!SlotGenerator.VEHICLES.containsKey(p.vehicleClass())) throw new ApiErrors.BadRequest("unknown vehicle_class " + p.vehicleClass(), "vehicle_class");
		if (p.gapLatM() < 0 || p.gapLonM() < 0 || p.lashingPitchM() <= 0) throw new ApiErrors.BadRequest("gaps must be >= 0 and lashing_pitch_m > 0", "gap_lat_m");

		var obstacles = new ArrayList<double[][]>();
		for (var r : db.sql("SELECT ST_AsGeoJSON(geom)::text AS g FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'C' AND kind = 'pillar'").param("ds", ds).param("deck", deck).query().listOfRows())
			obstacles.add(Wkt.coords((String) r.get("g")));
		var lanes = new ArrayList<SlotGenerator.Lane>();
		for (var r : db.sql("SELECT id, ST_AsGeoJSON(geom)::text AS g, (props->>'width_m')::float8 AS w FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'A2'").param("ds", ds).param("deck", deck).query().listOfRows())
			lanes.add(new SlotGenerator.Lane((String) r.get("id"), Wkt.coords((String) r.get("g")), r.get("w") == null ? 3.2 : ((Number) r.get("w")).doubleValue()));
		var lashings = new ArrayList<SlotGenerator.Lashing>();
		for (var r : db.sql("SELECT id, ST_X(geom) AS x, ST_Y(geom) AS y FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'LP'").param("ds", ds).param("deck", deck).query().listOfRows())
			lashings.add(new SlotGenerator.Lashing((String) r.get("id"), ((Number) r.get("x")).doubleValue(), ((Number) r.get("y")).doubleValue()));

		var res = SlotGenerator.generate(deck, outline, z, obstacles, lanes, lashings, p);
		slots.deleteDeckSlots(ds, deck);
		var out = new ArrayList<Map<String, Object>>();
		for (var s : res.slots()) {
			var ps = new ParkingSlot(s.id(), deck, s.polygon(), new TargetPose(s.targetX(), s.targetY(), 0), new Tolerance(0.15, 0.30, 2), s.vehicleClass(), s.accessLaneId(), s.lashingIds(), s.sequenceNo(), "empty");
			slots.upsert(ds, ps);
			out.add(slotJson(ps));
		}
		int version = Datasets.bumpVersion(db, ds);
		var body = new LinkedHashMap<String, Object>();
		body.put("deck", deck); body.put("count", out.size()); body.put("utilization", res.utilization()); body.put("lashing_coverage", res.lashingCoverage());
		body.put("version", version); body.put("slots", out);
		return body;
	}

	static Map<String, Object> slotJson(ParkingSlot s) {
		var m = new LinkedHashMap<String, Object>();
		m.put("id", s.id()); m.put("deck_id", s.deckId()); m.put("polygon", s.polygon());
		m.put("target_pose", Map.of("x", s.targetPose().x(), "y", s.targetPose().y(), "heading_deg", s.targetPose().headingDeg()));
		m.put("tolerance", Map.of("lat_m", s.tolerance().latM(), "lon_m", s.tolerance().lonM(), "heading_deg", s.tolerance().headingDeg()));
		m.put("vehicle_class", s.vehicleClass()); m.put("access_lane_id", s.accessLaneId()); m.put("lashing_points", s.lashingPoints());
		m.put("sequence_no", s.sequenceNo()); m.put("status", s.status());
		return m;
	}
}
