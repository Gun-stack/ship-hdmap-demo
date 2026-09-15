package com.shiphdmap.api.pose;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.within;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import org.hamcrest.Matchers;
import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.TestcontainersConfiguration;
import com.shiphdmap.api.dataset.DatasetController;
import com.shiphdmap.api.dataset.SeedImporter;
import com.shiphdmap.api.model.Pose;
import com.shiphdmap.api.model.SeedData;
import com.shiphdmap.api.model.VehicleMap;
import java.nio.file.Files;
import java.nio.file.Path;
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
class PoseTests {
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
		datasets.create(new DatasetController.NewDataset(DS, "RORO demo", "Demo Ship", 12.3456, 45.6789, 87.5, 120.0));
		VehicleMap m = json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class);
		importer.importSeed(DS, new SeedData(m.decks(), m.facilities(), m.lashingPoints(), m.ramps(), m.lanes(), null, null));
	}

	@Test
	void rampAngleFormula() {
		// hinge z 10.6, draft aft 8.6 -> hinge above water 2.0; quay 3.5 + tide 1.2 = 4.7; rise 2.7 over 30 m -> asin(0.09) = 5.16 deg (> max 4 -> blocked)
		Pose p = new Pose(8.1, 8.6, 0, 87.5, 1.2, 3.5, 12.3456, 45.6789, "2026-09-15T00:00:00Z");
		PoseStore.RampState s = PoseStore.rampState(10.6, 30, new double[] { -7, 4 }, p);
		assertThat(s.angleDeg()).isCloseTo(Math.toDegrees(Math.asin(2.7 / 30)), within(1e-9));
		assertThat(s.state()).isEqualTo("blocked");
		// tide 0 -> rise 1.5 -> 2.87 deg -> deployed
		s = PoseStore.rampState(10.6, 30, new double[] { -7, 4 }, new Pose(8.1, 8.6, 0, 87.5, 0, 3.5, 0, 0, null));
		assertThat(s.state()).isEqualTo("deployed");
		// impossible geometry (rise > length) clamps and blocks
		s = PoseStore.rampState(10.6, 3, new double[] { -7, 4 }, new Pose(8.1, 8.6, 0, 87.5, 5, 3.5, 0, 0, null));
		assertThat(s.angleDeg()).isEqualTo(90.0);
		assertThat(s.state()).isEqualTo("blocked");
	}

	@Test
	void poseDefaultsFromDatasetAndPutOverrides() throws Exception {
		mvc.perform(get("/api/datasets/" + DS + "/pose")).andExpect(status().isOk())
			.andExpect(jsonPath("$.ap_lat").value(12.3456)).andExpect(jsonPath("$.heading_deg").value(87.5)).andExpect(jsonPath("$.trim_deg").isNumber());
		String body = "{\"draft_fwd_m\":8.0,\"draft_aft_m\":9.2,\"heel_deg\":-0.4,\"heading_deg\":90,\"tide_m\":1.0,\"quay_z_m\":3.5,\"ap_lat\":12.35,\"ap_lon\":45.68,\"measured_at\":\"2026-09-15T09:30:00Z\"}";
		mvc.perform(put("/api/datasets/" + DS + "/pose").contentType(MediaType.APPLICATION_JSON).content(body)).andExpect(status().isOk())
			.andExpect(jsonPath("$.draft_aft_m").value(9.2))
			// exact double equality is unreachable here: 9.2 - 8.0 != the literal 1.2 at the bit level (IEEE-754), so compare with a tolerance like the formula test above does.
			.andExpect(jsonPath("$.trim_deg").value(Matchers.closeTo(Math.toDegrees(Math.atan(1.2 / 120.0)), 1e-9)));
		mvc.perform(get("/api/datasets/" + DS + "/pose")).andExpect(jsonPath("$.heading_deg").value(90.0));
		assertThat(poses.georef(DS).headingDeg()).isEqualTo(90.0);
		mvc.perform(get("/api/datasets/nope/pose")).andExpect(status().isNotFound());
	}

	@Test
	void rampEndpointUsesPose() throws Exception {
		mvc.perform(get("/api/datasets/" + DS + "/ramps/RAMP-STERN")).andExpect(status().isOk())
			.andExpect(jsonPath("$.id").value("RAMP-STERN")).andExpect(jsonPath("$.length_m").value(30.0)).andExpect(jsonPath("$.hinge[1][1]").value(6.0))
			.andExpect(jsonPath("$.state").value("deployed"));
		mvc.perform(put("/api/datasets/" + DS + "/pose").contentType(MediaType.APPLICATION_JSON)
			.content("{\"draft_fwd_m\":8.1,\"draft_aft_m\":8.6,\"heel_deg\":0,\"heading_deg\":87.5,\"tide_m\":1.2,\"quay_z_m\":3.5,\"ap_lat\":0,\"ap_lon\":0}"));
		mvc.perform(get("/api/datasets/" + DS + "/ramps/RAMP-STERN")).andExpect(jsonPath("$.state").value("blocked"));
		mvc.perform(get("/api/datasets/" + DS + "/ramps/NOPE")).andExpect(status().isNotFound());
	}
}
