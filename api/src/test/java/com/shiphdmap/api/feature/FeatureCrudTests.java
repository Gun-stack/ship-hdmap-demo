package com.shiphdmap.api.feature;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.TestcontainersConfiguration;
import com.shiphdmap.api.dataset.DatasetController;
import com.shiphdmap.api.dataset.SeedImporter;
import com.shiphdmap.api.model.SeedData;
import com.shiphdmap.api.model.VehicleMap;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Map;
import org.hamcrest.Matchers;
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
class FeatureCrudTests {
	@Autowired MockMvc mvc;
	@Autowired JdbcClient db;
	@Autowired ObjectMapper json;
	@Autowired DatasetController datasets;
	@Autowired SeedImporter importer;

	static final String DS = "roro-demo-01";

	@BeforeEach
	void seed() throws Exception {
		db.sql("DELETE FROM dataset").update();
		datasets.create(new DatasetController.NewDataset(DS, "RORO demo", "Demo Ship", 12.3456, 45.6789, 87.5, 120.0));
		VehicleMap m = json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class);
		importer.importSeed(DS, new SeedData(m.decks(), m.facilities(), m.lashingPoints(), m.ramps(), m.lanes(), m.parkingSlots(), m.landmarks(), m.markings()));
	}

	@Test
	void listFiltersByDeckAndLayer() throws Exception {
		mvc.perform(get("/api/datasets/" + DS + "/features").param("deck", "D3").param("layer", "LM"))
			.andExpect(status().isOk()).andExpect(jsonPath("$.length()").value(19)).andExpect(jsonPath("$[0].geometry.type").value("Point"))
			.andExpect(jsonPath("$[0].props.code").isNumber())
			.andExpect(jsonPath("$[0].created_at", Matchers.matchesPattern("^\\d{4}-\\d{2}-\\d{2}T.*Z$")));
		mvc.perform(get("/api/datasets/" + DS + "/features").param("layer", "A2")).andExpect(jsonPath("$.length()").value(3));
	}

	@Test
	void createWithoutIdGeneratesNextLandmarkIdAndBumpsVersion() throws Exception {
		int before = db.sql("SELECT version FROM dataset WHERE id = :id").param("id", DS).query(Integer.class).single();
		String body = """
			{"deck_id":"D3","layer":"LM","kind":"apriltag","geometry":{"type":"Point","coordinates":[84.0,-6.2,11.8]},
			 "props":{"family":"apriltag-36h11","code":7,"normal":[0,1,0],"size_m":0.3,"mounted_on":"C-PILLAR-D3-007"}}""";
		mvc.perform(post("/api/datasets/" + DS + "/features").contentType(MediaType.APPLICATION_JSON).content(body))
			.andExpect(status().isCreated()).andExpect(jsonPath("$.id").value("LM-0020")).andExpect(jsonPath("$.geometry.coordinates[0]").value(84.0));
		int after = db.sql("SELECT version FROM dataset WHERE id = :id").param("id", DS).query(Integer.class).single();
		assertThat(after).isEqualTo(before + 1);
	}

	@Test
	void validation() throws Exception {
		mvc.perform(post("/api/datasets/" + DS + "/features").contentType(MediaType.APPLICATION_JSON)
			.content("{\"layer\":\"ZZ\",\"kind\":\"x\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[0,0,0]}}"))
			.andExpect(status().isBadRequest()).andExpect(jsonPath("$.field").value("layer"));
		mvc.perform(post("/api/datasets/" + DS + "/features").contentType(MediaType.APPLICATION_JSON)
			.content("{\"layer\":\"LM\",\"kind\":\"x\",\"deck_id\":\"D9\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[0,0,0]}}"))
			.andExpect(status().isBadRequest()).andExpect(jsonPath("$.field").value("deck_id"));
		mvc.perform(post("/api/datasets/" + DS + "/features").contentType(MediaType.APPLICATION_JSON).content("{\"layer\":\"LM\",\"kind\":\"x\"}"))
			.andExpect(status().isBadRequest()).andExpect(jsonPath("$.field").value("geometry"));
		mvc.perform(get("/api/datasets/" + DS + "/features/NOPE")).andExpect(status().isNotFound());
		mvc.perform(get("/api/datasets/nope/features")).andExpect(status().isNotFound());
	}

	@Test
	void updateMovesGeometryAndDeleteRemoves() throws Exception {
		int beforePut = version();
		String body = """
			{"deck_id":"D3","layer":"LM","kind":"apriltag","geometry":{"type":"Point","coordinates":[13.0,-6.2,11.8]},"props":{"code":1}}""";
		mvc.perform(put("/api/datasets/" + DS + "/features/LM-0001").contentType(MediaType.APPLICATION_JSON).content(body))
			.andExpect(status().isOk()).andExpect(jsonPath("$.geometry.coordinates[0]").value(13.0)).andExpect(jsonPath("$.props.code").value(1));
		assertThat(version()).isEqualTo(beforePut + 1);
		mvc.perform(put("/api/datasets/" + DS + "/features/LM-0001").contentType(MediaType.APPLICATION_JSON)
			.content("{\"layer\":\"LP\",\"kind\":\"x\",\"geometry\":{\"type\":\"Point\",\"coordinates\":[0,0,0]}}"))
			.andExpect(status().isBadRequest()).andExpect(jsonPath("$.field").value("layer"));
		int beforeDelete = version();
		mvc.perform(delete("/api/datasets/" + DS + "/features/LM-0001")).andExpect(status().isNoContent());
		assertThat(version()).isEqualTo(beforeDelete + 1);
		mvc.perform(delete("/api/datasets/" + DS + "/features/LM-0001")).andExpect(status().isNotFound());
	}

	@Test
	void deletingLaneNullsSlotAccessLaneReference() throws Exception {
		mvc.perform(delete("/api/datasets/" + DS + "/features/A2-D3-0001")).andExpect(status().isNoContent());
		Map<String, Object> row = db.sql("SELECT access_lane_id FROM parking_slot WHERE dataset_id = :ds AND feature_id = 'PS-D3-001'").param("ds", DS).query().listOfRows().get(0);
		assertThat(row.get("access_lane_id")).isNull();
	}

	@Test
	void deletingLashingPointReferencedByASlotIsConflict() throws Exception {
		mvc.perform(delete("/api/datasets/" + DS + "/features/LP-D3-3962")).andExpect(status().isConflict());
	}

	int version() { return db.sql("SELECT version FROM dataset WHERE id = :id").param("id", DS).query(Integer.class).single(); }

	@Test
	void partialPutKeepsOmittedFields() throws Exception {
		mvc.perform(put("/api/datasets/" + DS + "/features/LM-0002").contentType(MediaType.APPLICATION_JSON)
			.content("{\"geometry\":{\"type\":\"Point\",\"coordinates\":[12.5,6.2,11.8]}}"))
			.andExpect(status().isOk())
			.andExpect(jsonPath("$.deck_id").value("D3"))
			.andExpect(jsonPath("$.kind").value("apriltag"))
			.andExpect(jsonPath("$.props.code").value(2))
			.andExpect(jsonPath("$.geometry.coordinates[0]").value(12.5));
	}

	@Test
	void slotStatus() throws Exception {
		int before = version();
		mvc.perform(put("/api/datasets/" + DS + "/slots/PS-D3-001/status").contentType(MediaType.APPLICATION_JSON).content("{\"status\":\"filled\"}"))
			.andExpect(status().isOk()).andExpect(jsonPath("$.status").value("filled"));
		assertThat(version()).isEqualTo(before + 1);
		mvc.perform(put("/api/datasets/" + DS + "/slots/PS-D3-001/status").contentType(MediaType.APPLICATION_JSON).content("{\"status\":\"bogus\"}"))
			.andExpect(status().isBadRequest());
		mvc.perform(put("/api/datasets/" + DS + "/slots/NOPE/status").contentType(MediaType.APPLICATION_JSON).content("{\"status\":\"empty\"}"))
			.andExpect(status().isNotFound());
	}
}
