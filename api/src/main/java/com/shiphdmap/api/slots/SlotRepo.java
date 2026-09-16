package com.shiphdmap.api.slots;

import com.shiphdmap.api.geo.Wkt;
import com.shiphdmap.api.model.VehicleMap.ParkingSlot;
import java.util.List;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

/** parking_slot rows: a B2/parking_slot feature plus the slot columns. Used by the seed importer and the generator. */
@Repository
public class SlotRepo {
	private final JdbcClient db;
	public SlotRepo(JdbcClient db) { this.db = db; }

	public void upsert(String ds, ParkingSlot ps) {
		db.sql("""
			INSERT INTO feature (dataset_id, id, deck_id, layer, kind, geom, props) VALUES (:ds, :id, :deck, 'B2', 'parking_slot', ST_GeomFromText(:wkt, 0), '{}'::jsonb)
			ON CONFLICT (dataset_id, id) DO UPDATE SET deck_id = EXCLUDED.deck_id, layer = 'B2', kind = 'parking_slot', geom = EXCLUDED.geom, updated_at = now()""")
			.param("ds", ds).param("id", ps.id()).param("deck", ps.deckId()).param("wkt", Wkt.polygon(ps.polygon())).update();
		db.sql("""
			INSERT INTO parking_slot (dataset_id, feature_id, target_x, target_y, target_heading_deg, tol_lat_m, tol_lon_m, tol_heading_deg, vehicle_class, sequence_no, status, access_lane_id, lashing_ids)
			VALUES (:ds, :id, :tx, :ty, :th, :tl, :tn, :tg, :vc, :seq, :st, :lane, :lash)
			ON CONFLICT (dataset_id, feature_id) DO UPDATE SET target_x = EXCLUDED.target_x, target_y = EXCLUDED.target_y, target_heading_deg = EXCLUDED.target_heading_deg,
			  tol_lat_m = EXCLUDED.tol_lat_m, tol_lon_m = EXCLUDED.tol_lon_m, tol_heading_deg = EXCLUDED.tol_heading_deg, vehicle_class = EXCLUDED.vehicle_class,
			  sequence_no = EXCLUDED.sequence_no, status = EXCLUDED.status, access_lane_id = EXCLUDED.access_lane_id, lashing_ids = EXCLUDED.lashing_ids""")
			.param("ds", ds).param("id", ps.id()).param("tx", ps.targetPose().x()).param("ty", ps.targetPose().y()).param("th", ps.targetPose().headingDeg())
			.param("tl", ps.tolerance().latM()).param("tn", ps.tolerance().lonM()).param("tg", ps.tolerance().headingDeg())
			.param("vc", ps.vehicleClass() == null ? "passenger" : ps.vehicleClass()).param("seq", ps.sequenceNo()).param("st", ps.status() == null ? "empty" : ps.status())
			.param("lane", ps.accessLaneId()).param("lash", (ps.lashingPoints() == null ? List.<String>of() : ps.lashingPoints()).toArray(new String[0])).update();
	}

	/** Deleting the feature cascades to parking_slot. Returns the number of slots removed. */
	public int deleteDeckSlots(String ds, String deck) {
		return db.sql("DELETE FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'B2' AND kind = 'parking_slot'").param("ds", ds).param("deck", deck).update();
	}
}
