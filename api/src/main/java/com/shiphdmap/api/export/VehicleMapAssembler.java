package com.shiphdmap.api.export;

import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.JsonMaps;
import com.shiphdmap.api.dataset.Datasets;
import com.shiphdmap.api.geo.Wkt;
import com.shiphdmap.api.model.VehicleMap;
import com.shiphdmap.api.model.VehicleMap.*;
import java.time.Instant;
import java.time.format.DateTimeFormatter;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;

/** DB rows -> vehicle-map v1 (spec §6). Ship Frame only; no WGS84 here. */
@Service
public class VehicleMapAssembler {
	private final JdbcClient db;
	private final ObjectMapper json;
	public VehicleMapAssembler(JdbcClient db, ObjectMapper json) { this.db = db; this.json = json; }

	/** Cheap ETag check: reads only dataset.version, no feature/deck/slot assembly. */
	public int version(String ds) {
		Datasets.require(db, ds);
		return db.sql("SELECT version FROM dataset WHERE id = :ds").param("ds", ds).query(Integer.class).single();
	}

	public VehicleMap assemble(String ds) {
		int version = version(ds);
		List<Deck> decks = db.sql("SELECT id, name, z_surface, z_clear, movable, ST_AsGeoJSON(outline)::text g FROM deck WHERE dataset_id = :ds ORDER BY z_surface").param("ds", ds)
			.query().listOfRows().stream().map(r -> new Deck((String) r.get("id"), (String) r.get("name"), d(r.get("z_surface")), d(r.get("z_clear")), (Boolean) r.get("movable"), Wkt.coords((String) r.get("g")))).toList();

		var landmarks = new ArrayList<Landmark>(); var lanes = new ArrayList<Lane>(); var lashing = new ArrayList<LashingPoint>();
		var markings = new ArrayList<Marking>(); var facilities = new ArrayList<Facility>(); var ramps = new ArrayList<Ramp>();
		for (Map<String, Object> r : db.sql("SELECT id, deck_id, layer, kind, ST_AsGeoJSON(geom)::text g, props::text p FROM feature WHERE dataset_id = :ds ORDER BY layer, id").param("ds", ds).query().listOfRows()) {
			String id = (String) r.get("id"), deck = (String) r.get("deck_id"), layer = (String) r.get("layer"), kind = (String) r.get("kind");
			double[][] c = Wkt.coords((String) r.get("g")); Map<String, Object> p = props((String) r.get("p"));
			switch (layer) {
				case "LM" -> landmarks.add(new Landmark(id, new Marker(str(p.get("family"), "apriltag-36h11"), ((Number) p.getOrDefault("code", 0)).intValue()), c[0], arr(p.get("normal")), d(p.getOrDefault("size_m", 0.3)), deck, (String) p.get("mounted_on")));
				case "A2" -> lanes.add(new Lane(id, deck, c, d(p.getOrDefault("width_m", 3.2)), str(p.get("direction"), "forward"), d(p.getOrDefault("speed_limit_kmh", 10)), strList(p.get("next"))));
				case "LP" -> lashing.add(new LashingPoint(id, kind, c[0], deck));
				case "B2" -> { if (!"parking_slot".equals(kind)) markings.add(new Marking(id, kind, c, deck)); }
				case "C" -> {
					if ("ramp".equals(kind)) ramps.add(new Ramp(id, str(p.get("type"), "stern_quarter"), c, d(p.get("length_m")), d(p.get("width_m")), arr(p.get("angle_range_deg")), (String) p.get("connects_lane"), strList(p.get("transition_landmarks"))));
					else facilities.add(new Facility(id, kind, c, d(p.getOrDefault("z_min", 0)), d(p.getOrDefault("z_max", 0)), deck));
				}
				default -> { /* A1, MEP: not part of the vehicle map in M2 */ }
			}
		}
		List<ParkingSlot> slots = db.sql("""
			SELECT f.id, f.deck_id, ST_AsGeoJSON(f.geom)::text g, s.target_x, s.target_y, s.target_heading_deg, s.tol_lat_m, s.tol_lon_m, s.tol_heading_deg,
			       s.vehicle_class, s.access_lane_id, s.lashing_ids, s.sequence_no, s.status
			FROM parking_slot s JOIN feature f ON f.dataset_id = s.dataset_id AND f.id = s.feature_id WHERE s.dataset_id = :ds ORDER BY s.sequence_no, f.id""").param("ds", ds)
			.query().listOfRows().stream().map(r -> new ParkingSlot((String) r.get("id"), (String) r.get("deck_id"), Wkt.coords((String) r.get("g")),
				new TargetPose(d(r.get("target_x")), d(r.get("target_y")), d(r.get("target_heading_deg"))),
				new Tolerance(d(r.get("tol_lat_m")), d(r.get("tol_lon_m")), d(r.get("tol_heading_deg"))),
				(String) r.get("vehicle_class"), (String) r.get("access_lane_id"), pgArray(r.get("lashing_ids")), ((Number) r.get("sequence_no")).intValue(), (String) r.get("status"))).toList();

		return new VehicleMap(VehicleMap.SCHEMA, ds, version, DateTimeFormatter.ISO_INSTANT.format(Instant.now().truncatedTo(ChronoUnit.SECONDS)), VehicleMap.shipFrame(),
			decks, landmarks, lanes, slots, lashing, markings, facilities, ramps);
	}

	Map<String, Object> props(String s) { return JsonMaps.toMap(json, s); }
	static double d(Object o) { return o == null ? 0 : ((Number) o).doubleValue(); }
	static String str(Object o, String def) { return o == null ? def : o.toString(); }
	@SuppressWarnings("unchecked") static double[] arr(Object o) { if (o == null) return null; List<Number> l = (List<Number>) o; double[] a = new double[l.size()]; for (int i = 0; i < a.length; i++) a[i] = l.get(i).doubleValue(); return a; }
	@SuppressWarnings("unchecked") static List<String> strList(Object o) { return o == null ? List.of() : (List<String>) o; }
	static List<String> pgArray(Object o) {
		try { if (o instanceof java.sql.Array a) return List.of((String[]) a.getArray()); } catch (java.sql.SQLException e) { throw new IllegalStateException(e); }
		if (o instanceof String[] s) return List.of(s);
		return List.of();
	}
}
