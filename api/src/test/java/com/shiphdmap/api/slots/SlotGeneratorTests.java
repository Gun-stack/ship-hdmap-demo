package com.shiphdmap.api.slots;

import static org.assertj.core.api.Assertions.*;

import java.util.ArrayList;
import java.util.List;
import org.junit.jupiter.api.Test;

class SlotGeneratorTests {
	static double[][] rect(double x0, double y0, double x1, double y1, double z) {
		return new double[][] { { x0, y0, z }, { x1, y0, z }, { x1, y1, z }, { x0, y1, z }, { x0, y0, z } };
	}
	static final double Z = 10.6;
	static final double[][] DECK = rect(0, -12, 120, 12, Z);
	static List<double[][]> pillars() {
		var out = new ArrayList<double[][]>();
		for (double y : new double[] { -6.5, 6.5 }) for (double x = 12; x < 120 - 1e-9; x += 12) out.add(rect(x - 0.3, y - 0.3, x + 0.3, y + 0.3, Z));
		return out;
	}
	static List<SlotGenerator.Lane> lanes() { return List.of(new SlotGenerator.Lane("A2-D3-0001", new double[][] { { 2, 0, Z }, { 60, 0, Z }, { 118, 0, Z } }, 3.2)); }
	static List<SlotGenerator.Lashing> grid() {
		var out = new ArrayList<SlotGenerator.Lashing>(); int n = 0;
		for (double x = 1; x <= 119 + 1e-9; x += 0.75) for (double y = -11; y <= 11 + 1e-9; y += 0.75) out.add(new SlotGenerator.Lashing(String.format("LP-D3-%04d", ++n), x, y));
		return out;
	}

	@Test
	void fillsDeckThreeWithGridAlignedSlots() {
		var r = SlotGenerator.generate("D3", DECK, Z, pillars(), lanes(), grid(), SlotGenerator.DEFAULTS);
		assertThat(r.slots()).hasSizeBetween(120, 176);
		assertThat(r.utilization()).isBetween(0.30, 0.60);
		assertThat(r.lashingCoverage()).isEqualTo(1.0);
		var first = r.slots().get(0);
		assertThat(first.sequenceNo()).isEqualTo(1);
		assertThat(first.id()).isEqualTo("PS-D3-001");
		assertThat(first.targetX()).isEqualTo(r.slots().stream().mapToDouble(SlotGenerator.Slot::targetX).max().orElseThrow()); // bow first
		assertThat(first.accessLaneId()).isEqualTo("A2-D3-0001");
		assertThat(first.lashingIds()).hasSize(4).doesNotHaveDuplicates();
		assertThat(first.polygon()).hasNumberOfRows(5);
		assertThat(first.polygon()[1][0] - first.polygon()[0][0]).isCloseTo(4.8, within(1e-9));
		assertThat(first.polygon()[2][1] - first.polygon()[1][1]).isCloseTo(1.85, within(1e-9));
		assertThat(first.polygon()[0][2]).isEqualTo(Z);
	}

	@Test
	void slotsAvoidLaneCorridorPillarsAndEachOther() {
		var r = SlotGenerator.generate("D3", DECK, Z, pillars(), lanes(), grid(), SlotGenerator.DEFAULTS);
		for (var s : r.slots()) {
			double y0 = s.polygon()[0][1], y1 = s.polygon()[2][1];
			assertThat(y1 < -1.9 || y0 > 1.9).as("outside lane corridor: " + s.id()).isTrue();
			for (var p : pillars()) assertThat(overlaps(s.polygon(), p, 0.30)).as(s.id() + " vs pillar").isFalse();
			for (int i = 0; i < 4; i++) assertThat(SlotGenerator.contains(DECK, s.polygon()[i][0], s.polygon()[i][1])).isTrue();
		}
		for (int i = 0; i < r.slots().size(); i++) for (int j = i + 1; j < r.slots().size(); j++)
			assertThat(overlaps(r.slots().get(i).polygon(), r.slots().get(j).polygon(), 0)).as("overlap " + i + "," + j).isFalse();
		assertThat(r.slots().stream().map(SlotGenerator.Slot::sequenceNo).toList()).isSorted().doesNotHaveDuplicates();
	}

	@Test
	void widerGapsYieldFewerSlotsAndNoLashingsWhenGridMissing() {
		var tight = SlotGenerator.generate("D3", DECK, Z, pillars(), lanes(), List.of(), SlotGenerator.DEFAULTS);
		var wide = SlotGenerator.generate("D3", DECK, Z, pillars(), lanes(), List.of(), new SlotGenerator.Params("passenger", 1.0, 1.5, 0.75));
		assertThat(wide.slots().size()).isLessThan(tight.slots().size());
		assertThat(tight.lashingCoverage()).isEqualTo(0.0);
		assertThat(tight.slots().get(0).lashingIds()).isEmpty();
	}

	@Test
	void geometryHelpers() {
		assertThat(SlotGenerator.ringArea(DECK)).isCloseTo(2880, within(1e-9));
		assertThat(SlotGenerator.contains(DECK, 60, 0)).isTrue();
		assertThat(SlotGenerator.contains(DECK, 121, 0)).isFalse();
		assertThatThrownBy(() -> SlotGenerator.generate("D3", DECK, Z, List.of(), lanes(), List.of(), new SlotGenerator.Params("truck", 0.3, 0.4, 0.75)))
			.isInstanceOf(IllegalArgumentException.class).hasMessageContaining("truck");
	}

	/** Axis-aligned overlap of two rectangular rings, b grown by margin. */
	static boolean overlaps(double[][] a, double[][] b, double margin) {
		return a[0][0] < b[2][0] + margin && a[2][0] > b[0][0] - margin && a[0][1] < b[2][1] + margin && a[2][1] > b[0][1] - margin;
	}
}
