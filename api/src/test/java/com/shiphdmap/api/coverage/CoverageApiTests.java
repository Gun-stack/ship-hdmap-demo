package com.shiphdmap.api.coverage;

import static org.assertj.core.api.Assertions.*;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.*;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.*;

import com.shiphdmap.api.TestcontainersConfiguration;
import com.shiphdmap.api.dataset.DatasetController;
import com.shiphdmap.api.dataset.SeedImportTests;
import com.shiphdmap.api.dataset.SeedImporter;
import java.util.List;
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
import tools.jackson.databind.ObjectMapper;

@Import(TestcontainersConfiguration.class)
@SpringBootTest
@AutoConfigureMockMvc
@SuppressWarnings("unchecked")
class CoverageApiTests {
	static final String DS = "roro-demo-01";
	@Autowired MockMvc mvc; @Autowired JdbcClient db; @Autowired ObjectMapper json; @Autowired DatasetController datasets; @Autowired SeedImporter importer;

	@BeforeEach
	void seed() throws Exception {
		db.sql("DELETE FROM dataset").update();
		datasets.create(new DatasetController.NewDataset(DS, "RORO demo", "Demo Ship", 12.3456, 45.6789, 87.5, 120.0));
		importer.importSeed(DS, SeedImportTests.fixtureAsSeed(json));
		// Spec §1.4 measures the deck with its slots generated (136 on D3); the seed fixture ships only two sample
		// slots, and without the rest the in-scope population would be the lane alone.
		mvc.perform(post("/api/datasets/" + DS + "/decks/D3/slots/generate").contentType(MediaType.APPLICATION_JSON).content("{}"))
			.andExpect(status().isOk());
	}

	Map<String, Object> postJson(String path, Object body) throws Exception {
		String s = mvc.perform(post(path).contentType(MediaType.APPLICATION_JSON).content(json.writeValueAsString(body)))
			.andExpect(status().isOk()).andReturn().getResponse().getContentAsString();
		return json.readValue(s, Map.class);
	}

	Map<String, Object> d3(Object body) throws Exception { return postJson("/api/datasets/" + DS + "/decks/D3/coverage", body); }
	static double num(Map<String, Object> m, String k) { return ((Number) m.get(k)).doubleValue(); }

	@Test
	void coverageReturnsCellsAndBothModeSummaries() throws Exception {
		var out = d3(Map.of("mode", "load", "grid_m", 2.0));
		assertThat(out).containsKeys("deck", "mode", "grid_m", "n_cells", "n_drawn", "blind_ratio", "weak_ratio", "cells", "other_mode");
		assertThat(out.get("mode")).isEqualTo("load");
		assertThat((List<?>) out.get("cells")).isNotEmpty();
		var other = (Map<String, Object>) out.get("other_mode");
		assertThat(other.get("mode")).isEqualTo("unload");
		assertThat(other).doesNotContainKey("cells");
	}

	/**
	 * The denominator is the drivable/parkable area, not the deck: Deck 3 is about 1236 of 2880 cells at grid 1.0.
	 * Asserted as a relationship plus a loose band - the exact counts move if a pillar swallows a grid centre or the
	 * slot generator is re-parameterised, and the precise figures belong in theFixtureBlindSpotsShowUp.
	 */
	@Test
	void theDenominatorIsTheInScopeCellsOnly() throws Exception {
		var out = d3(Map.of("mode", "load", "grid_m", 1.0));
		int inScope = (int) num(out, "n_cells"), drawn = (int) num(out, "n_drawn");
		assertThat(inScope).isLessThan(drawn).isCloseTo(1236, within(50));
		assertThat(drawn).isCloseTo(2880, within(50));
		var cells = (List<Map<String, Object>>) out.get("cells");
		assertThat(cells).hasSize(drawn);
		// in_scope is serialised only when false, so it appears on exactly the excluded cells and nowhere else
		assertThat(cells.stream().filter(c -> c.containsKey("in_scope")).count()).isEqualTo(drawn - inScope);
		assertThat(cells.stream().filter(c -> Boolean.FALSE.equals(c.get("in_scope"))).count()).isEqualTo(drawn - inScope);
	}

	@Test
	void blindCellsOmitSigma() throws Exception {
		var out = d3(Map.of("mode", "unload", "grid_m", 2.0));
		var cells = (List<Map<String, Object>>) out.get("cells");
		var blind = cells.stream().filter(c -> ((Number) c.get("n")).intValue() == 0).findFirst();
		assertThat(blind).isPresent();
		assertThat(blind.get()).doesNotContainKeys("sigma_xy", "sigma_psi", "stability");
	}

	/** Spec §1.4: the tool has to surface the outer-row blind spot and the unload stern blind spot. */
	@Test
	void theFixtureBlindSpotsShowUp() throws Exception {
		var load = d3(Map.of("mode", "load", "grid_m", 1.0));
		var unload = d3(Map.of("mode", "unload", "grid_m", 1.0));
		assertThat(num(load, "blind_ratio")).isCloseTo(0.168, within(0.01));
		assertThat(num(unload, "blind_ratio")).isCloseTo(0.256, within(0.01));

		var cells = (List<Map<String, Object>>) load.get("cells");
		java.util.function.BiFunction<Double, Double, Double> blindInBand = (lo, hi) -> {
			var band = cells.stream().filter(c -> !Boolean.FALSE.equals(c.get("in_scope")))
				.filter(c -> { double y = Math.abs(num(c, "y")); return y >= lo && y < hi; }).toList();
			return (double) band.stream().filter(c -> ((Number) c.get("n")).intValue() == 0).count() / band.size();
		};
		assertThat(blindInBand.apply(9.0, 12.0)).as("outer slot row, loading").isGreaterThan(0.5);
		assertThat(blindInBand.apply(0.0, 2.0)).as("the lane, loading").isLessThan(0.05);
	}

	@Test
	void extraLandmarksImproveCoverageWithoutTouchingTheDatabase() throws Exception {
		var before = d3(Map.of("mode", "unload", "grid_m", 2.0));
		var with = d3(Map.of("mode", "unload", "grid_m", 2.0, "extra_landmarks",
			List.of(Map.of("x", 10.0, "y", -6.2, "phi_deg", 0.0), Map.of("x", 10.0, "y", 6.2, "phi_deg", 0.0))));
		assertThat(num(with, "blind_ratio")).isLessThan(num(before, "blind_ratio"));
		var again = d3(Map.of("mode", "unload", "grid_m", 2.0));
		assertThat(again.get("blind_ratio")).isEqualTo(before.get("blind_ratio"));   // nothing was persisted
	}

	/**
	 * Removing markers must be measured on blind_ratio, not weak_ratio: a weak cell that goes blind LEAVES the weak
	 * numerator, so weak_ratio falls. On this fixture omitting LM-0020/0021 takes load weak from 0.807 to 0.690.
	 */
	@Test
	void omitRaisesTheBlindRatio() throws Exception {
		var full = d3(Map.of("mode", "load", "grid_m", 2.0));
		var less = d3(Map.of("mode", "load", "grid_m", 2.0, "omit", List.of("LM-0020", "LM-0021")));
		assertThat(num(less, "blind_ratio")).isGreaterThan(num(full, "blind_ratio"));
	}

	@Test
	void suggestReturnsRankedPicks() throws Exception {
		var out = postJson("/api/datasets/" + DS + "/decks/D3/coverage/suggest",
			Map.of("mode", "unload", "grid_m", 1.0, "budget", 2));
		assertThat(num(out, "grid_m")).as("suggest pins the grid at 2 m and says so").isEqualTo(2.0);
		var picks = (List<Map<String, Object>>) out.get("suggestions");
		assertThat(picks).hasSize(2);
		assertThat(picks.get(0)).containsKeys("rank", "x", "y", "phi_deg", "mounted_on", "blind_after", "weak_after", "gain");
		assertThat(num((Map<String, Object>) out.get("after"), "blind_ratio"))
			.isLessThanOrEqualTo(num((Map<String, Object>) out.get("before"), "blind_ratio"));
	}

	@Test
	void unknownDeckIs404AndBadGridIs400() throws Exception {
		mvc.perform(post("/api/datasets/" + DS + "/decks/NOPE/coverage").contentType(MediaType.APPLICATION_JSON).content("{}"))
			.andExpect(status().isNotFound());
		mvc.perform(post("/api/datasets/" + DS + "/decks/D3/coverage").contentType(MediaType.APPLICATION_JSON).content("{\"grid_m\":0}"))
			.andExpect(status().isBadRequest()).andExpect(jsonPath("$.field").value("grid_m"));
	}
}
