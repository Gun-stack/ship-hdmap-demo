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
			.andExpect(status().isOk()).andExpect(jsonPath("$.deck").value("D3")).andExpect(jsonPath("$.count").value(org.hamcrest.Matchers.greaterThan(100)))
			.andExpect(jsonPath("$.utilization").value(org.hamcrest.Matchers.greaterThan(0.3), Double.class)).andExpect(jsonPath("$.version").value(v0 + 1))
			.andExpect(jsonPath("$.slots[0].id").value("PS-D3-001")).andExpect(jsonPath("$.slots[0].sequence_no").value(1))
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
}
