package com.shiphdmap.api.export;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.within;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import tools.jackson.databind.JsonNode;
import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.TestcontainersConfiguration;
import com.shiphdmap.api.dataset.DatasetController;
import com.shiphdmap.api.dataset.SeedImportTests;
import com.shiphdmap.api.dataset.SeedImporter;
import com.shiphdmap.api.geo.ShipFrame;
import com.shiphdmap.api.model.SeedData;
import com.shiphdmap.api.model.VehicleMap;
import com.shiphdmap.api.pose.PoseStore;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.webmvc.test.autoconfigure.AutoConfigureMockMvc;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.test.web.servlet.MockMvc;

@Import(TestcontainersConfiguration.class)
@SpringBootTest
@AutoConfigureMockMvc
class GeoJsonExportTests {
	@Autowired MockMvc mvc;
	@Autowired JdbcClient db;
	@Autowired ObjectMapper json;
	@Autowired DatasetController datasets;
	@Autowired SeedImporter importer;
	@Autowired PoseStore poses;
	static final String DS = "roro-demo-01";

	@BeforeEach
	void seed() throws Exception {
		db.sql("DELETE FROM dataset").update(); poses.clear();
		datasets.create(new DatasetController.NewDataset(DS, "RORO demo", "Demo Ship", 12.3456, 45.6789, 90.0, 120.0));
		VehicleMap m = json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class);
		importer.importSeed(DS, new SeedData(m.decks(), m.facilities(), m.lashingPoints(), m.ramps(), m.lanes(), m.parkingSlots(), m.landmarks(), m.markings()));
	}

	@Test
	void featureCollectionInWgs84UsingDatasetGeoref() throws Exception {
		String body = mvc.perform(get("/api/datasets/" + DS + "/export.geojson")).andExpect(status().isOk())
			.andExpect(content().contentTypeCompatibleWith("application/geo+json")).andExpect(jsonPath("$.type").value("FeatureCollection"))
			.andReturn().getResponse().getContentAsString();
		JsonNode fc = json.readTree(body);
		JsonNode lm1 = null, deck3 = null;
		for (JsonNode f : fc.get("features")) {
			if ("LM-0001".equals(f.get("properties").get("id").asText())) lm1 = f;
			if ("D3".equals(f.get("properties").get("id").asText()) && "DECK".equals(f.get("properties").get("layer").asText())) deck3 = f;
		}
		assertThat(lm1).isNotNull(); assertThat(deck3).isNotNull();
		// heading 90 -> bow points east: ship (12, -6.2) -> east 12 m, south 6.2 m of the AP
		double[] expect = ShipFrame.toWgs84(new ShipFrame.Georef(12.3456, 45.6789, 90.0), 12.0, -6.2);
		assertThat(lm1.get("geometry").get("type").asText()).isEqualTo("Point");
		assertThat(lm1.get("geometry").get("coordinates").get(0).asDouble()).isCloseTo(expect[1], within(1e-7)); // lon first
		assertThat(lm1.get("geometry").get("coordinates").get(1).asDouble()).isCloseTo(expect[0], within(1e-7));
		assertThat(lm1.get("geometry").get("coordinates").get(2).asDouble()).isEqualTo(11.8);
		assertThat(lm1.get("properties").get("code").asInt()).isEqualTo(1);
		assertThat(deck3.get("geometry").get("type").asText()).isEqualTo("Polygon");
		assertThat(deck3.get("geometry").get("coordinates").get(0)).hasSize(5);
		assertThat(fc.get("features").size()).isEqualTo(3 + 19 + 18 + 1 + 3 + SeedImportTests.fixtureLashingCount(json) + 2);
	}

	@Test
	void poseOverridesGeoref() throws Exception {
		mvc.perform(org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put("/api/datasets/" + DS + "/pose").contentType("application/json")
			.content("{\"draft_fwd_m\":8.1,\"draft_aft_m\":8.6,\"heel_deg\":0,\"heading_deg\":0,\"tide_m\":0,\"quay_z_m\":3.5,\"ap_lat\":10,\"ap_lon\":20}"));
		JsonNode fc = json.readTree(mvc.perform(get("/api/datasets/" + DS + "/export.geojson")).andReturn().getResponse().getContentAsString());
		JsonNode lm1 = null; for (JsonNode f : fc.get("features")) if ("LM-0001".equals(f.get("properties").get("id").asText())) lm1 = f;
		double[] expect = ShipFrame.toWgs84(new ShipFrame.Georef(10, 20, 0), 12.0, -6.2);
		assertThat(lm1.get("geometry").get("coordinates").get(1).asDouble()).isCloseTo(expect[0], within(1e-7));
		mvc.perform(get("/api/datasets/nope/export.geojson")).andExpect(status().isNotFound());
	}

	@Test
	void fixedKeysWinOverFeatureProps() throws Exception {
		db.sql("INSERT INTO feature (dataset_id, id, deck_id, layer, kind, geom, props) VALUES ('roro-demo-01','LM-9999','D3','LM','apriltag',ST_GeomFromText('POINT Z(1 1 11.8)',0),'{\"id\":\"evil\",\"layer\":\"ZZ\",\"code\":9}'::jsonb)").update();
		JsonNode fc = json.readTree(mvc.perform(get("/api/datasets/" + DS + "/export.geojson")).andReturn().getResponse().getContentAsString());
		JsonNode evil = null;
		for (JsonNode f : fc.get("features")) if (f.get("properties").has("code") && f.get("properties").get("code").asInt() == 9) evil = f;
		assertThat(evil).isNotNull();
		assertThat(evil.get("properties").get("id").asText()).isEqualTo("LM-9999");
		assertThat(evil.get("properties").get("layer").asText()).isEqualTo("LM");
	}
}
