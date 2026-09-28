package com.shiphdmap.api.layout;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import org.junit.jupiter.api.Test;

class ShipLayoutTests {
	static final List<String> DECKS = List.of("D1", "D2", "D3", "D4", "D5");

	@Test
	void farDecksLoadFirstLowerBeforeUpperAtTheSameDistance() {
		assertThat(DECKS.stream().sorted(java.util.Comparator.comparingInt(d -> ShipLayout.rank(DECKS, "D3", d))).toList())
			.containsExactly("D1", "D5", "D2", "D4", "D3");
		// the stern ramp on the bottom deck: plain bottom-up would be wrong -- the top is farthest
		assertThat(ShipLayout.rank(DECKS, "D1", "D5")).isZero();
		// no stern ramp known: z order
		assertThat(ShipLayout.rank(DECKS, null, "D1")).isZero();
		assertThat(ShipLayout.rank(DECKS, null, "D5")).isEqualTo(4);
	}

	@Test
	void routeRunsAreTheStretchesLyingOnTheDeck() {
		double[][] path = { { 2, 0, 10.6 }, { 40, 0, 10.6 }, { 34, 9.8, 10.6 }, { 14, 9.8, 8.0 }, { 8, 9.8, 8.0 }, { 8, 0, 8.0 } };
		var d3 = ShipLayout.runsAt(List.<double[][]>of(path), 10.6);
		assertThat(d3).hasSize(1);
		assertThat(d3.get(0)).hasNumberOfRows(3);        // down to the ramp's top: the slope itself is on neither deck
		var d2 = ShipLayout.runsAt(List.<double[][]>of(path), 8.0);
		assertThat(d2).hasSize(1);
		assertThat(d2.get(0)[0]).containsExactly(14.0, 9.8, 8.0);
		assertThat(ShipLayout.runsAt(List.<double[][]>of(path), 5.4)).isEmpty();
	}

	@Test
	void theFarSideOfARampIsTheDeckFartherFromTheSternRampDeck() {
		var down = new ShipLayout.InnerRamp("RAMP-D3-D2", "D2", "D3", new double[0][]);
		var up = new ShipLayout.InnerRamp("RAMP-D3-D4", "D3", "D4", new double[0][]);
		var v = new ShipLayout.View(DECKS, java.util.Map.of(), "D3", List.of(), List.of(down, up));
		assertThat(v.farDeck(down)).isEqualTo("D2");   // going down, the landing
		assertThat(v.farDeck(up)).isEqualTo("D4");     // going up, the opening the ramp swings into
	}
}
