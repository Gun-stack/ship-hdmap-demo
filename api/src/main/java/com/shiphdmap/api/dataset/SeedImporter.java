package com.shiphdmap.api.dataset;

import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.geo.Wkt;
import com.shiphdmap.api.model.SeedData;
import com.shiphdmap.api.model.VehicleMap.*;
import com.shiphdmap.api.slots.SlotRepo;
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
	private final SlotRepo slots;
	public SeedImporter(JdbcClient db, ObjectMapper json, SlotRepo slots) { this.db = db; this.json = json; this.slots = slots; }

	@Transactional
	public Map<String, Object> importSeed(String datasetId, SeedData seed) {
		Datasets.require(db, datasetId);
		int decks = 0, features = 0, slotCount = 0;
		Map<Double, String> deckByZ = new HashMap<>();
		for (Deck d : nz(seed.decks())) {
			if (d.outline() == null) throw new ApiErrors.BadRequest("deck " + d.id() + " outline is required", "outline");
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
			features += upsertFeature(datasetId, r.id(), nearestDeck(deckByZ, r.hinge()[0][2]), "C", "ramp", Wkt.lineString(r.hinge()), props);
		}
		for (Lane l : nz(seed.lanes())) {
			if (l.centerline() == null) throw new ApiErrors.BadRequest("lane " + l.id() + " centerline is required", "centerline");
			var props = new LinkedHashMap<String, Object>();
			props.put("width_m", l.widthM()); props.put("direction", l.direction()); props.put("speed_limit_kmh", l.speedLimitKmh()); props.put("next", l.next());
			features += upsertFeature(datasetId, l.id(), l.deckId(), "A2", "centerline", Wkt.lineString(l.centerline()), props);
		}
		for (Landmark lm : nz(seed.landmarks())) {
			if (lm.marker() == null) throw new ApiErrors.BadRequest("landmark " + lm.id() + " marker is required", "marker");
			if (lm.position() == null) throw new ApiErrors.BadRequest("landmark " + lm.id() + " position is required", "position");
			var props = new LinkedHashMap<String, Object>();
			props.put("family", lm.marker().family()); props.put("code", lm.marker().code()); props.put("normal", lm.normal()); props.put("size_m", lm.sizeM()); props.put("mounted_on", lm.mountedOn());
			features += upsertFeature(datasetId, lm.id(), lm.deckId(), "LM", "apriltag", Wkt.point(lm.position()), props);
		}
		for (Marking m : nz(seed.markings()))
			features += upsertFeature(datasetId, m.id(), m.deckId(), "B2", m.kind(), Wkt.polygon(m.polygon()), Map.of());
		for (ParkingSlot ps : nz(seed.parkingSlots())) {
			if (ps.targetPose() == null) throw new ApiErrors.BadRequest("slot " + ps.id() + " target_pose is required", "target_pose");
			if (ps.tolerance() == null) throw new ApiErrors.BadRequest("slot " + ps.id() + " tolerance is required", "tolerance");
			if (ps.polygon() == null) throw new ApiErrors.BadRequest("slot " + ps.id() + " polygon is required", "polygon");
			slots.upsert(datasetId, ps); features++;
			slotCount++;
		}
		int version = Datasets.bumpVersion(db, datasetId);
		return Map.of("decks", decks, "features", features, "parking_slots", slotCount, "version", version);
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

	/** Ramp hinge z rarely lands exactly on a deck's z_surface (float generator output); match within 0.05 m. */
	static String nearestDeck(Map<Double, String> deckByZ, double z) {
		String best = null; double bestDiff = 0.05;
		for (var e : deckByZ.entrySet()) { double diff = Math.abs(e.getKey() - z); if (diff <= bestDiff) { bestDiff = diff; best = e.getValue(); } }
		return best;
	}
}
