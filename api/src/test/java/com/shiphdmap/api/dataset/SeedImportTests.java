package com.shiphdmap.api.dataset;

import static org.assertj.core.api.Assertions.assertThat;

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
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;

@Import(TestcontainersConfiguration.class)
@SpringBootTest
class SeedImportTests {
	@Autowired JdbcClient db;
	@Autowired ObjectMapper json;
	@Autowired DatasetController datasets;
	@Autowired SeedImporter importer;

	static SeedData fixtureAsSeed(ObjectMapper json) throws Exception {
		VehicleMap m = json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class);
		return new SeedData(m.decks(), m.facilities(), m.lashingPoints(), m.ramps(), m.lanes(), m.parkingSlots(), m.landmarks(), m.markings());
	}

	@BeforeEach
	void clean() { db.sql("DELETE FROM dataset").update(); datasets.create(new DatasetController.NewDataset("roro-demo-01", "RORO demo", "Demo Ship", 12.3456, 45.6789, 87.5, 120.0)); }

	@Test
	void importsFixtureIntoFourTables() throws Exception {
		Map<String, Object> r = importer.importSeed("roro-demo-01", fixtureAsSeed(json));
		assertThat(r).containsEntry("decks", 3).containsEntry("parking_slots", 2);
		assertThat(db.sql("SELECT count(*) FROM deck WHERE dataset_id = 'roro-demo-01'").query(Integer.class).single()).isEqualTo(3);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND layer = 'LM'").query(Integer.class).single()).isEqualTo(19);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND layer = 'C' AND kind = 'pillar'").query(Integer.class).single()).isEqualTo(18);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND kind = 'ramp'").query(Integer.class).single()).isEqualTo(1);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND layer = 'A2'").query(Integer.class).single()).isEqualTo(3);
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01' AND layer = 'LP'").query(Integer.class).single()).isEqualTo(49);
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
		importer.importSeed("roro-demo-01", fixtureAsSeed(json));
		importer.importSeed("roro-demo-01", fixtureAsSeed(json));
		assertThat(db.sql("SELECT count(*) FROM feature WHERE dataset_id = 'roro-demo-01'").query(Integer.class).single()).isEqualTo(19 + 18 + 1 + 3 + 49 + 2);
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
	void landmarkWithoutMarkerIs400() {
		var lm = new VehicleMap.Landmark("LM-0001", null, new double[] { 0, 0, 0 }, null, 0.3, "D3", null);
		var ex = org.junit.jupiter.api.Assertions.assertThrows(com.shiphdmap.api.ApiErrors.BadRequest.class,
			() -> importer.importSeed("roro-demo-01", new SeedData(null, null, null, null, null, null, java.util.List.of(lm), null)));
		assertThat(ex.field).isEqualTo("marker");
	}
}
