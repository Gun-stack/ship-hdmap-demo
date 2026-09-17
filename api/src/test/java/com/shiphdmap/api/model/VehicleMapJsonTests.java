package com.shiphdmap.api.model;

import static org.assertj.core.api.Assertions.assertThat;

import tools.jackson.databind.PropertyNamingStrategies;
import tools.jackson.databind.json.JsonMapper;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

class VehicleMapJsonTests {
	static final JsonMapper M = JsonMapper.builder().propertyNamingStrategy(PropertyNamingStrategies.SNAKE_CASE).build();
	static String fixture() throws Exception { return Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")); }

	@Test
	void fixtureParsesIntoRecords() throws Exception {
		VehicleMap m = M.readValue(fixture(), VehicleMap.class);
		assertThat(m.schema()).isEqualTo("ship-hdmap/vehicle-map/1.0");
		assertThat(m.decks()).hasSize(3);
		assertThat(m.decks().get(2).zSurface()).isEqualTo(10.6);
		assertThat(m.landmarks()).hasSize(21);
		assertThat(m.landmarks().get(0).marker().code()).isEqualTo(1);
		assertThat(m.parkingSlots().get(0).lashingPoints()).hasSize(4);
		assertThat(m.ramps().get(0).transitionLandmarks()).containsExactly("LM-0001", "LM-0002");
	}

	@Test
	void roundTripKeepsSnakeCaseKeys() throws Exception {
		VehicleMap m = M.readValue(fixture(), VehicleMap.class);
		String json = M.writeValueAsString(m);
		assertThat(json).contains("\"map_id\"", "\"z_surface\"", "\"target_pose\"", "\"lashing_points\"", "\"access_lane_id\"", "\"transition_landmarks\"");
		assertThat(json).doesNotContain("\"mapId\"", "\"zSurface\"");
		assertThat(M.writeValueAsString(M.readValue(json, VehicleMap.class))).isEqualTo(json);
	}
}
