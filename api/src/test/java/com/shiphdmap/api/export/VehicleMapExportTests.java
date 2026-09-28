package com.shiphdmap.api.export;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.TestcontainersConfiguration;
import com.shiphdmap.api.dataset.DatasetController;
import com.shiphdmap.api.dataset.SeedImporter;
import com.shiphdmap.api.model.SeedData;
import com.shiphdmap.api.model.VehicleMap;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.List;
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
class VehicleMapExportTests {
	@Autowired MockMvc mvc;
	@Autowired JdbcClient db;
	@Autowired ObjectMapper json;
	@Autowired DatasetController datasets;
	@Autowired SeedImporter importer;
	@Autowired VehicleMapAssembler assembler;
	static final String DS = "roro-demo-01";
	VehicleMap fixture;

	@BeforeEach
	void seed() throws Exception {
		db.sql("DELETE FROM dataset").update();
		datasets.create(new DatasetController.NewDataset(DS, "RORO demo", "Demo Ship", 12.3456, 45.6789, 87.5, 120.0));
		fixture = json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class);
		importer.importSeed(DS, new SeedData(fixture.decks(), fixture.facilities(), fixture.lashingPoints(), fixture.ramps(), fixture.lanes(), fixture.parkingSlots(), fixture.landmarks(), fixture.markings(), fixture.routes()));
	}

	@Test
	void assembledMapMatchesFixtureContent() {
		VehicleMap m = assembler.assemble(DS);
		assertThat(m.schema()).isEqualTo(VehicleMap.SCHEMA);
		assertThat(m.mapId()).isEqualTo(DS);
		assertThat(m.version()).isEqualTo(2);
		assertThat(m.decks()).extracting(VehicleMap.Deck::id).containsExactly("D1", "D2", "D3", "D4", "D5");
		assertThat(m.decks().get(2).outline()).hasNumberOfRows(5);
		assertThat(m.landmarks()).hasSize(107);
		VehicleMap.Landmark lm1 = m.landmarks().stream().filter(l -> l.id().equals("LM-0001")).findFirst().orElseThrow();
		assertThat(lm1.position()).containsExactly(12.0, -6.2, 11.8);
		assertThat(lm1.normal()).containsExactly(0.0, 1.0, 0.0);
		assertThat(lm1.marker().code()).isEqualTo(1);
		assertThat(lm1.mountedOn()).isEqualTo("C-PILLAR-D3-001");
		assertThat(m.lanes()).hasSize(5);   // routes are not lanes: they come out under routes
		assertThat(m.lanes().stream().filter(l -> l.id().equals("A2-D3-0001")).findFirst().orElseThrow().centerline()).hasNumberOfRows(3);
		assertThat(m.parkingSlots()).hasSize(2);
		VehicleMap.ParkingSlot ps = m.parkingSlots().get(0);
		assertThat(ps.id()).isEqualTo("PS-D3-001");
		assertThat(ps.targetPose().x()).isEqualTo(102.4);
		assertThat(ps.lashingPoints()).hasSize(4);
		assertThat(ps.accessLaneId()).isEqualTo("A2-D3-0001");
		assertThat(m.lashingPoints()).hasSize(fixture.lashingPoints().size());
		assertThat(m.facilities()).hasSize(90);   // every deck: the scene rebuilds its hull from this map
		assertThat(m.ramps()).hasSize(5);
		assertThat(m.routes()).extracting(VehicleMap.Route::deckId).containsExactlyInAnyOrder("D1", "D2", "D4", "D5");
		VehicleMap.Route toD1 = m.routes().stream().filter(r -> r.deckId().equals("D1")).findFirst().orElseThrow();
		assertThat(toD1.ramps()).containsExactly("RAMP-D3-D2", "RAMP-D2-D1");
		assertThat(toD1.path()[0]).containsExactly(2.0, 0.0, 10.6);
		assertThat(toD1.path()[toD1.path().length - 1]).containsExactly(8.0, 0.0, 5.4);
		VehicleMap.Ramp inner = m.ramps().stream().filter(r -> r.id().equals("RAMP-D3-D2")).findFirst().orElseThrow();
		assertThat(inner.type()).isEqualTo("internal_hoistable");
		assertThat(inner.lowerDeck()).isEqualTo("D2"); assertThat(inner.upperDeck()).isEqualTo("D3");
		assertThat(inner.toe()).hasNumberOfRows(2);
		// ramps come out in id order, so the stern ramp is NOT ramps[0] (RAMP-D2-D1 sorts first): find it by type, as the scene must
		VehicleMap.Ramp stern = m.ramps().stream().filter(r -> "stern_quarter".equals(r.type())).findFirst().orElseThrow();
		assertThat(stern.transitionLandmarks()).containsExactly("LM-0022", "LM-0023");   // the ramp's own entrance pair, not the pillar tags
		assertThat(stern.hinge()[1]).containsExactly(0.0, 6.0, 10.6);
	}

	@Test
	void markingExportsWithoutDuplicatingSlots() {
		double[][] polygon = { { 50, -1, 10.6 }, { 52, -1, 10.6 }, { 52, 1, 10.6 }, { 50, -1, 10.6 } };
		importer.importSeed(DS, new SeedData(null, null, null, null, null, null, null, List.of(new VehicleMap.Marking("B2-0101", "arrow", polygon, "D3")), null));
		VehicleMap m = assembler.assemble(DS);
		assertThat(m.markings()).hasSize(1);
		VehicleMap.Marking marking = m.markings().get(0);
		assertThat(marking.id()).isEqualTo("B2-0101");
		assertThat(marking.kind()).isEqualTo("arrow");
		assertThat(marking.deckId()).isEqualTo("D3");
		assertThat(marking.polygon()).hasNumberOfRows(4);
		assertThat(m.parkingSlots()).hasSize(2);
	}

	@Test
	void laneNextRoundTrips() {
		VehicleMap.Lane original = fixture.lanes().stream().filter(l -> l.id().equals("A2-D3-0001")).findFirst().orElseThrow();
		VehicleMap.Lane withNext = new VehicleMap.Lane(original.id(), original.deckId(), original.centerline(), original.widthM(), original.direction(), original.speedLimitKmh(), List.of("A2-D2-0001"));
		importer.importSeed(DS, new SeedData(null, null, null, null, List.of(withNext), null, null, null, null));
		VehicleMap.Lane lane = assembler.assemble(DS).lanes().stream().filter(l -> l.id().equals("A2-D3-0001")).findFirst().orElseThrow();
		assertThat(lane.next()).containsExactly("A2-D2-0001");
	}

	@Test
	void etagAndNotModified() throws Exception {
		String etag = mvc.perform(get("/api/datasets/" + DS + "/vehicle-map")).andExpect(status().isOk())
			.andExpect(header().string("ETag", "\"2\"")).andExpect(jsonPath("$.map_id").value(DS)).andExpect(jsonPath("$.landmarks.length()").value(107))
			.andReturn().getResponse().getHeader("ETag");
		mvc.perform(get("/api/datasets/" + DS + "/vehicle-map").header("If-None-Match", etag)).andExpect(status().isNotModified());
		db.sql("UPDATE dataset SET version = version + 1 WHERE id = :id").param("id", DS).update();
		mvc.perform(get("/api/datasets/" + DS + "/vehicle-map").header("If-None-Match", etag)).andExpect(status().isOk()).andExpect(header().string("ETag", "\"3\""));
		mvc.perform(get("/api/datasets/nope/vehicle-map")).andExpect(status().isNotFound());
	}
}
