package com.shiphdmap.api.dataset;

import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.geo.Wkt;
import com.shiphdmap.api.model.SeedData;
import com.shiphdmap.api.model.VehicleMap.*;
import java.util.HashMap;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/** Seed JSON (generator output or a full fixture) -> deck / feature / parking_slot rows. Upsert by (dataset_id, id). */
@Service
public class SeedImporter {
	private final JdbcClient db;
	private final ObjectMapper json;
	public SeedImporter(JdbcClient db, ObjectMapper json) { this.db = db; this.json = json; }

	@Transactional
	public Map<String, Object> importSeed(String datasetId, SeedData seed) {
		Datasets.require(db, datasetId);
		int decks = 0, features = 0, slots = 0;
		Map<Double, String> deckByZ = new HashMap<>();
		for (Deck d : nz(seed.decks())) {
			db.sql("""
				INSERT INTO deck (dataset_id, id, name, z_surface, z_clear, movable, outline)
				VALUES (:ds, :id, :name, :zs, :zc, :mov, ST_GeomFromText(:wkt, 0))
				ON CONFLICT (dataset_id, id) DO UPDATE SET name = EXCLUDED.name, z_surface = EXCLUDED.z_surface, z_clear = EXCLUDED.z_clear, movable = EXCLUDED.movable, outline = EXCLUDED.outline""")
				.param("ds", datasetId).param("id", d.id()).param("name", d.name()).param("zs", d.zSurface()).param("zc", d.zClear()).param("mov", d.movable())
				.param("wkt", Wkt.polygon(d.outline())).update();
			deckByZ.put(d.zSurface(), d.id()); decks++;
		}
		for (Facility f : nz(seed.facilities()))
			features += upsertFeature(datasetId, f.id(), f.deckId(), "C", f.kind(), Wkt.polygon(f.footprint()), Map.of("z_min", f.zMin(), "z_max", f.zMax()));
		for (LashingPoint lp : nz(seed.lashingPoints()))
			features += upsertFeature(datasetId, lp.id(), lp.deckId(), "LP", lp.kind() == null ? "cloverleaf" : lp.kind(), Wkt.point(lp.position()), Map.of());
		for (Ramp r : nz(seed.ramps())) {
			var props = new LinkedHashMap<String, Object>();
			props.put("type", r.type()); props.put("length_m", r.lengthM()); props.put("width_m", r.widthM()); props.put("angle_range_deg", r.angleRangeDeg());
			props.put("connects_lane", r.connectsLane()); props.put("transition_landmarks", r.transitionLandmarks());
			features += upsertFeature(datasetId, r.id(), deckByZ.get(r.hinge()[0][2]), "C", "ramp", Wkt.lineString(r.hinge()), props);
		}
		for (Lane l : nz(seed.lanes())) {
			var props = new LinkedHashMap<String, Object>();
			props.put("width_m", l.widthM()); props.put("direction", l.direction()); props.put("speed_limit_kmh", l.speedLimitKmh()); props.put("next", l.next());
			features += upsertFeature(datasetId, l.id(), l.deckId(), "A2", "centerline", Wkt.lineString(l.centerline()), props);
		}
		for (Landmark lm : nz(seed.landmarks())) {
			var props = new LinkedHashMap<String, Object>();
			props.put("family", lm.marker().family()); props.put("code", lm.marker().code()); props.put("normal", lm.normal()); props.put("size_m", lm.sizeM()); props.put("mounted_on", lm.mountedOn());
			features += upsertFeature(datasetId, lm.id(), lm.deckId(), "LM", "apriltag", Wkt.point(lm.position()), props);
		}
		for (ParkingSlot ps : nz(seed.parkingSlots())) {
			features += upsertFeature(datasetId, ps.id(), ps.deckId(), "B2", "parking_slot", Wkt.polygon(ps.polygon()), Map.of());
			db.sql("""
				INSERT INTO parking_slot (dataset_id, feature_id, target_x, target_y, target_heading_deg, tol_lat_m, tol_lon_m, tol_heading_deg, vehicle_class, sequence_no, status, access_lane_id, lashing_ids)
				VALUES (:ds, :id, :tx, :ty, :th, :tl, :tn, :tg, :vc, :seq, :st, :lane, :lash)
				ON CONFLICT (dataset_id, feature_id) DO UPDATE SET target_x = EXCLUDED.target_x, target_y = EXCLUDED.target_y, target_heading_deg = EXCLUDED.target_heading_deg,
				  tol_lat_m = EXCLUDED.tol_lat_m, tol_lon_m = EXCLUDED.tol_lon_m, tol_heading_deg = EXCLUDED.tol_heading_deg, vehicle_class = EXCLUDED.vehicle_class,
				  sequence_no = EXCLUDED.sequence_no, status = EXCLUDED.status, access_lane_id = EXCLUDED.access_lane_id, lashing_ids = EXCLUDED.lashing_ids""")
				.param("ds", datasetId).param("id", ps.id()).param("tx", ps.targetPose().x()).param("ty", ps.targetPose().y()).param("th", ps.targetPose().headingDeg())
				.param("tl", ps.tolerance().latM()).param("tn", ps.tolerance().lonM()).param("tg", ps.tolerance().headingDeg())
				.param("vc", ps.vehicleClass() == null ? "passenger" : ps.vehicleClass()).param("seq", ps.sequenceNo()).param("st", ps.status() == null ? "empty" : ps.status())
				.param("lane", ps.accessLaneId()).param("lash", nz(ps.lashingPoints()).toArray(new String[0])).update();
			slots++;
		}
		int version = Datasets.bumpVersion(db, datasetId);
		return Map.of("decks", decks, "features", features, "parking_slots", slots, "version", version);
	}

	int upsertFeature(String ds, String id, String deckId, String layer, String kind, String wkt, Map<String, ?> props) {
		db.sql("""
			INSERT INTO feature (dataset_id, id, deck_id, layer, kind, geom, props)
			VALUES (:ds, :id, :deck, :layer, :kind, ST_GeomFromText(:wkt, 0), CAST(:props AS jsonb))
			ON CONFLICT (dataset_id, id) DO UPDATE SET deck_id = EXCLUDED.deck_id, layer = EXCLUDED.layer, kind = EXCLUDED.kind, geom = EXCLUDED.geom, props = EXCLUDED.props, updated_at = now()""")
			.param("ds", ds).param("id", id).param("deck", deckId).param("layer", layer).param("kind", kind).param("wkt", wkt).param("props", json.writeValueAsString(props)).update();
		return 1;
	}

	static <T> List<T> nz(List<T> l) { return l == null ? List.of() : l; }
}
