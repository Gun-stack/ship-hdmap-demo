package com.shiphdmap.api.dataset;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.TestcontainersConfiguration;
import com.shiphdmap.api.model.SeedData;
import com.shiphdmap.api.model.VehicleMap;
import java.nio.file.Files;
import java.nio.file.Path;
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
public class SeedImportTests {
	@Autowired MockMvc mvc;
	@Autowired JdbcClient db;
	@Autowired ObjectMapper json;
	@Autowired DatasetController datasets;
	@Autowired SeedImporter importer;

	public static SeedData fixtureAsSeed(ObjectMapper json) throws Exception {
		VehicleMap m = json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class);
		return new SeedData(m.decks(), m.facilities(), m.lashingPoints(), m.ramps(), m.lanes(), m.parkingSlots(), m.landmarks(), m.markings());
	}

	/** Lashing-point count of the fixture; tests compare against this instead of a literal so a regenerated fixture does not break them. */
	public static int fixtureLashingCount(ObjectMapper json) throws Exception {
		return json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class).lashingPoints().size();
	}

	@BeforeEach
	void clean() { db.sql("DELETE FROM dataset").update(); datasets.create(new DatasetController.NewDataset("roro-demo-01", "RORO demo", "Demo Ship", 12.3456, 45.6789, 87.5, 120.0)); }

	@Test
	void importsFixtureIntoFourTables() throws Exception {
		SeedData seed = fixtureAsSeed(json);
		int lp = seed.lashingPoints().size();
		Map<String, Object> r = importer.importSeed("roro-demo-01", seed);
		assertThat(r).containsEntry("decks", 3).containsEntry("parking_slots", 2);
		assertThat(db.sql("SELECT count(*) FROM deck WHERE dataset_id = 'roro-demo-01'").query(Integer.class).single()).isEqualTo(3);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND layer = 'LM'").query(Integer.class).single()).isEqualTo(19);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND layer = 'C' AND kind = 'pillar'").query(Integer.class).single()).isEqualTo(18);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND kind = 'ramp'").query(Integer.class).single()).isEqualTo(1);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND layer = 'A2'").query(Integer.class).single()).isEqualTo(3);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND layer = 'LP'").query(Integer.class).single()).isEqualTo(lp);
		assertThat(db.sql("SELECT version FROM dataset WHERE id = 'roro-demo-01'").query(Integer.class).single()).isEqualTo(2);
		// geometry really is Z and in Ship Frame: LM-0001 at (12, -6.2, 11.8)
		String gj = db.sql("SELECT ST_AsGeoJSON(geom)::text FROM feature WHERE dataset_id = 'roro-demo-01' AND id = 'LM-0001'").query(String.class).single();
		assertThat(gj).contains("[12,-6.2,11.8]");
		assertThat(db.sql("SELECT ST_SRID(geom) FROM feature WHERE dataset_id = 'roro-demo-01' AND id = 'LM-0001'").query(Integer.class).single()).isEqualTo(0);
		// slot row carries references
		assertThat(db.sql("SELECT access_lane_id FROM parking_slot WHERE dataset_id = 'roro-demo-01' AND feature_id = 'PS-D3-001'").query(String.class).single()).isEqualTo("A2-D3-0001");
		assertThat(db.sql("SELECT cardinality(lashing_ids) FROM parking_slot WHERE dataset_id = 'roro-demo-01' AND feature_id = 'PS-D3-001'").query(Integer.class).single()).isEqualTo(4);
	}

	@Test
	void reimportUpsertsWithoutDuplicates() throws Exception {
		SeedData seed = fixtureAsSeed(json);
		importer.importSeed("roro-demo-01", seed);
		importer.importSeed("roro-demo-01", seed);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01'").query(Integer.class).single()).isEqualTo(19 + 18 + 1 + 3 + seed.lashingPoints().size() + 2);
		assertThat(db.sql("SELECT version FROM dataset WHERE id = 'roro-demo-01'").query(Integer.class).single()).isEqualTo(3);
	}

	@Test
	void unknownDatasetIs404() {
		org.junit.jupiter.api.Assertions.assertThrows(com.shiphdmap.api.ApiErrors.NotFound.class, () -> importer.importSeed("nope", new SeedData(null, null, null, null, null, null, null, null)));
	}

	@Test
	void deletingDeckNullsFeatureDeckIdButKeepsDatasetId() throws Exception {
		importer.importSeed("roro-demo-01", fixtureAsSeed(json));
		db.sql("DELETE FROM deck WHERE dataset_id = 'roro-demo-01' AND id = 'D1'").update();
		Map<String, Object> row = db.sql("SELECT dataset_id, deck_id FROM feature WHERE dataset_id = 'roro-demo-01' AND id = 'A2-D1-0001'").query().listOfRows().get(0);
		assertThat(row.get("dataset_id")).isEqualTo("roro-demo-01");
		assertThat(row.get("deck_id")).isNull();
	}

	@Test
	void constraintViolationBodyHidesDriverText() throws Exception {
		// a slot whose access_lane_id points to a lane that does not exist violates the FK
		String body = """
			{"parking_slots":[{"id":"PS-X","deck_id":"D3","polygon":[[1,1,10.6],[2,1,10.6],[2,2,10.6],[1,1,10.6]],
			 "target_pose":{"x":1.5,"y":1.5,"heading_deg":0},"tolerance":{"lat_m":0.1,"lon_m":0.1,"heading_deg":1},
			 "vehicle_class":"passenger","access_lane_id":"A2-NOPE","lashing_points":[],"sequence_no":9,"status":"empty"}]}""";
		importer.importSeed("roro-demo-01", fixtureAsSeed(json));
		mvc.perform(post("/api/datasets/roro-demo-01/seed").contentType(MediaType.APPLICATION_JSON).content(body))
			.andExpect(status().isBadRequest())
			.andExpect(jsonPath("$.message").value(org.hamcrest.Matchers.startsWith("constraint violation")))
			.andExpect(jsonPath("$.message").value(org.hamcrest.Matchers.not(org.hamcrest.Matchers.containsString("ERROR"))));
	}

	@Test
	void landmarkWithoutMarkerIs400() {
		var lm = new VehicleMap.Landmark("LM-0001", null, new double[] { 0, 0, 0 }, null, 0.3, "D3", null);
		var ex = org.junit.jupiter.api.Assertions.assertThrows(com.shiphdmap.api.ApiErrors.BadRequest.class,
			() -> importer.importSeed("roro-demo-01", new SeedData(null, null, null, null, null, null, java.util.List.of(lm), null)));
		assertThat(ex.field).isEqualTo("marker");
	}
}
