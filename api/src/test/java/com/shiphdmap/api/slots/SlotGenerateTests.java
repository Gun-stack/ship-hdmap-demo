package com.shiphdmap.api.slots;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.TestcontainersConfiguration;
import com.shiphdmap.api.dataset.DatasetController;
import com.shiphdmap.api.dataset.SeedImporter;
import com.shiphdmap.api.dataset.SeedImportTests;
import java.util.Map;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.http.MediaType;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.web.servlet.MockMvc;

@Import(TestcontainersConfiguration.class)
@SpringBootTest
@AutoConfigureMockMvc
class SlotGenerateTests {
	static final String DS = "roro-demo-01";
	@Autowired MockMvc mvc; @Autowired JdbcClient db; @Autowired ObjectMapper json; @Autowired DatasetController datasets; @Autowired SeedImporter importer;
	@Autowired com.shiphdmap.api.layout.ShipLayout layout;

	@BeforeEach
	void seed() throws Exception {
		db.sql("DELETE FROM dataset").update();
		datasets.create(new DatasetController.NewDataset(DS, "RORO demo", "Demo Ship", 12.3456, 45.6789, 87.5, 120.0));
		importer.importSeed(DS, SeedImportTests.fixtureAsSeed(json));
	}

	@Test
	void generatesDeckThreeAndReplacesFixtureSlots() throws Exception {
		int v0 = db.sql("SELECT version FROM dataset WHERE id = :id").param("id", DS).query(Integer.class).single();
		String body = mvc.perform(post("/api/datasets/" + DS + "/decks/D3/slots/generate").contentType(MediaType.APPLICATION_JSON).content("{}"))
			.andExpect(status().isOk()).andExpect(jsonPath("$.deck").value("D3")).andExpect(jsonPath("$.count").value(org.hamcrest.Matchers.greaterThan(60)))
			.andExpect(jsonPath("$.utilization").value(org.hamcrest.Matchers.greaterThan(0.2), Double.class)).andExpect(jsonPath("$.version").value(v0 + 1))
			.andExpect(jsonPath("$.slots[0].id").value("PS-D3-001")).andExpect(jsonPath("$.slots[0].sequence_no").value(4001))   // D3 loads last of five: rank 4
			.andReturn().getResponse().getContentAsString();
		@SuppressWarnings("unchecked") Map<String, Object> r = json.readValue(body, Map.class);
		int count = (Integer) r.get("count");
		assertThat(db.sql("SELECT count(*) FROM parking_slot WHERE dataset_id = :ds").param("ds", DS).query(Integer.class).single()).isEqualTo(count);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = :ds AND id IN ('PS-D3-001','PS-D3-002') AND kind = 'parking_slot'").param("ds", DS).query(Integer.class).single()).isEqualTo(2); // ids reused, rows replaced
		// the fixture now ships the whole Deck 3 lashing grid, so slot generation maps every corner to a socket
		mvc.perform(get("/api/datasets/" + DS + "/vehicle-map")).andExpect(status().isOk()).andExpect(jsonPath("$.parking_slots.length()").value(count));
	}

	@Test
	void regenerateIsIdempotentAndBumpsVersion() throws Exception {
		mvc.perform(post("/api/datasets/" + DS + "/decks/D3/slots/generate").contentType(MediaType.APPLICATION_JSON).content("{}")).andExpect(status().isOk());
		int v1 = db.sql("SELECT version FROM dataset WHERE id = :id").param("id", DS).query(Integer.class).single();
		mvc.perform(post("/api/datasets/" + DS + "/decks/D3/slots/generate").contentType(MediaType.APPLICATION_JSON).content("{\"gap_lat_m\":1.0,\"gap_lon_m\":1.5}"))
			.andExpect(status().isOk()).andExpect(jsonPath("$.version").value(v1 + 1));
		int wide = db.sql("SELECT count(*) FROM parking_slot WHERE dataset_id = :ds").param("ds", DS).query(Integer.class).single();
		assertThat(wide).isGreaterThan(0).isLessThan(120);
	}

	@Test
	void rejectsUnknownDeckAndVehicleClass() throws Exception {
		mvc.perform(post("/api/datasets/" + DS + "/decks/D9/slots/generate").contentType(MediaType.APPLICATION_JSON).content("{}")).andExpect(status().isNotFound());
		mvc.perform(post("/api/datasets/" + DS + "/decks/D3/slots/generate").contentType(MediaType.APPLICATION_JSON).content("{\"vehicle_class\":\"truck\"}"))
			.andExpect(status().isBadRequest()).andExpect(jsonPath("$.field").value("vehicle_class"));
	}

	@Test
	void unreachableIsAStorableStatus() throws Exception {
		mvc.perform(post("/api/datasets/" + DS + "/decks/D3/slots/generate").contentType(MediaType.APPLICATION_JSON).content("{}")).andExpect(status().isOk());
		mvc.perform(put("/api/datasets/" + DS + "/slots/PS-D3-001/status").contentType(MediaType.APPLICATION_JSON).content("{\"status\":\"unreachable\"}"))
			.andExpect(status().isOk()).andExpect(jsonPath("$.status").value("unreachable"));
		assertThat(db.sql("SELECT status FROM parking_slot WHERE dataset_id = :ds AND feature_id = 'PS-D3-001'").param("ds", DS).query(String.class).single())
			.isEqualTo("unreachable");
	}

	/**
	 * M8: every deck gets slots, numbered so one global sequence loads far decks first, and none of them sits on ground a
	 * car drives over to reach another deck (a route run) or on the far side of an internal ramp.
	 */
	@Test
	@SuppressWarnings("unchecked")
	void everyDeckLoadsInShipOrderAndKeepsRoutesAndFarRampGroundClear() throws Exception {
		var ship = layout.load(DS);
		Map<String, Integer> firstSeq = new java.util.LinkedHashMap<>();
		for (String deck : java.util.List.of("D1", "D2", "D3", "D4", "D5")) {
			String body = mvc.perform(post("/api/datasets/" + DS + "/decks/" + deck + "/slots/generate").contentType(MediaType.APPLICATION_JSON).content("{}"))
				.andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
			Map<String, Object> r = json.readValue(body, Map.class);
			var slots = (java.util.List<Map<String, Object>>) r.get("slots");
			assertThat(slots).as(deck + " gets slots").hasSizeGreaterThan(20);
			firstSeq.put(deck, ((Number) slots.get(0).get("sequence_no")).intValue());
			var runs = ship.routeRunsOn(deck); var far = ship.farFootprintsOn(deck);
			for (var sl : slots) {
				double[][] poly = json.convertValue(sl.get("polygon"), double[][].class);
				double cx = (poly[0][0] + poly[2][0]) / 2, cy = (poly[0][1] + poly[2][1]) / 2;
				for (double[] pt : new double[][] { poly[0], poly[1], poly[2], poly[3], { cx, cy } }) {
					for (var run : runs)
						assertThat(com.shiphdmap.api.geo.Rings.distToPolyline(run, pt[0], pt[1])).as(sl.get("id") + " on a route run").isGreaterThanOrEqualTo(1.6);
					for (var f : far)
						assertThat(com.shiphdmap.api.geo.Rings.contains(f, pt[0], pt[1])).as(sl.get("id") + " on far-side ramp ground").isFalse();
				}
			}
		}
		// far decks first: D1, D5, D2, D4, D3 (the stern ramp lands on D3)
		assertThat(firstSeq).containsEntry("D1", 1).containsEntry("D5", 1001).containsEntry("D2", 2001).containsEntry("D4", 3001).containsEntry("D3", 4001);
		mvc.perform(get("/api/datasets/" + DS + "/vehicle-map")).andExpect(jsonPath("$.parking_slots[0].deck_id").value("D1"));
		// the D2 landing of RAMP-D3-D2 is far-side ground; the same strip on D3 (its hole, near side) may hold stowed-ramp slots
		assertThat(ship.farFootprintsOn("D2")).hasSize(1);
		assertThat(ship.farFootprintsOn("D3")).isEmpty();
	}
}
