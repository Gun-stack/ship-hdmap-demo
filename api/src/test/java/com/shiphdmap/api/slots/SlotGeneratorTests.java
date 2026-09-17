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
		assertThat(r.slots()).hasSize(76);                                  // 136 grid cells, 60 of them unreachable (pillar on the approach, or exit off the lane)
		assertThat(r.utilization()).isCloseTo(0.234, within(0.001));
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
	void everySlotCanBeDrivenIntoWithoutCrossingAPillar() {
		var r = SlotGenerator.generate("D3", DECK, Z, pillars(), lanes(), grid(), SlotGenerator.DEFAULTS);
		for (var s : r.slots()) {
			double cx = s.targetX(), cy = s.targetY();
			assertThat(cx - SlotGenerator.FINAL_RUN_M - Math.abs(cy)).as("exit point on the lane: " + s.id()).isGreaterThanOrEqualTo(2.0);
			for (var pose : sweep(cx, cy))
				for (var p : pillars())
					assertThat(SlotGenerator.overlapsConvex(pose, ring(p))).as(s.id() + " sweeps pillar at " + p[0][0] + "," + p[0][1]).isFalse();
		}
	}

	@Test
	void aPillarOnTheApproachDiagonalRemovesTheSlot() {
		var lane = List.of(new SlotGenerator.Lane("A2-D3-0001", new double[][] { { 2, 0, Z }, { 58, 0, Z } }, 3.2));
		double[][] deck = rect(0, -12, 60, 12, Z);
		var free = SlotGenerator.generate("D3", deck, Z, List.of(), lane, List.of(), SlotGenerator.DEFAULTS);
		var target = free.slots().stream().filter(s -> s.targetY() < -6).findFirst().orElseThrow();
		double exitX = target.targetX() - SlotGenerator.FINAL_RUN_M - Math.abs(target.targetY());
		double px = exitX + Math.abs(target.targetY()) / 2, py = target.targetY() / 2;   // midpoint of the 45° diagonal
		var blocked = SlotGenerator.generate("D3", deck, Z, List.<double[][]>of(rect(px - 0.3, py - 0.3, px + 0.3, py + 0.3, Z)), lane, List.of(), SlotGenerator.DEFAULTS);
		assertThat(targets(free)).contains(List.of(target.targetX(), target.targetY()));
		assertThat(targets(blocked)).as("a pillar on the approach diagonal removes the slot").doesNotContain(List.of(target.targetX(), target.targetY()));
	}

	@Test
	void sternCellsWhoseExitFallsOffTheLaneAreNotSlots() {
		var r = SlotGenerator.generate("D3", DECK, Z, pillars(), lanes(), grid(), SlotGenerator.DEFAULTS);
		for (var s : r.slots())
			assertThat(s.targetX() - SlotGenerator.FINAL_RUN_M - Math.abs(s.targetY())).as("exit of " + s.id()).isGreaterThanOrEqualTo(2.0);
		assertThat(r.slots().stream().mapToDouble(SlotGenerator.Slot::targetX).min().orElseThrow()).isGreaterThan(8.0);
	}

	static List<List<Double>> targets(SlotGenerator.Result r) {
		return r.slots().stream().map(s -> List.of(s.targetX(), s.targetY())).toList();
	}

	@Test
	void theApproachNeverSweepsAnAlreadyParkedCar() {
		// This is what FINAL_RUN_M buys: at the 45° pivot the 4.8 x 1.85 m body spans ±2.35 m laterally while the slot rows
		// are 2.25 m apart, so a pivot inside the slot's own x range clips both neighbouring rows. Drop FINAL_RUN_M below
		// 4.75 m and this fails.
		var slots = SlotGenerator.generate("D3", DECK, Z, pillars(), lanes(), grid(), SlotGenerator.DEFAULTS).slots();
		for (int i = 0; i < slots.size(); i++) {
			var s = slots.get(i);
			for (var pose : sweep(s.targetX(), s.targetY()))
				for (int j = 0; j < i; j++) {   // slots generated earlier are already occupied when this one is filled
					var parked = slots.get(j);
					assertThat(SlotGenerator.overlapsConvex(pose, SlotGenerator.box(parked.targetX(), parked.targetY(), 0, 4.8, 1.85)))
						.as(s.id() + " sweeps " + parked.id()).isFalse();
				}
		}
	}

	/** Vehicle poses along the planner's approach to (cx, cy), sampled as the generator samples them. */
	static List<double[][]> sweep(double cx, double cy) {
		double f = SlotGenerator.FINAL_RUN_M;
		double[][] path = { { cx - f - Math.abs(cy), 0 }, { cx - f, cy }, { cx, cy } };
		var out = new ArrayList<double[][]>();
		for (int i = 0; i + 1 < path.length; i++) {
			double dx = path[i + 1][0] - path[i][0], dy = path[i + 1][1] - path[i][1], len = Math.hypot(dx, dy), psi = Math.atan2(dy, dx);
			int n = Math.max(1, (int) Math.ceil(len / 0.25));
			for (int k = 0; k <= n; k++) { double t = (double) k / n; out.add(SlotGenerator.box(path[i][0] + dx * t, path[i][1] + dy * t, psi, 4.8, 1.85)); }
		}
		return out;
	}

	static double[][] ring(double[][] r) { return new double[][] { { r[0][0], r[0][1] }, { r[1][0], r[1][1] }, { r[2][0], r[2][1] }, { r[3][0], r[3][1] } }; }

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
