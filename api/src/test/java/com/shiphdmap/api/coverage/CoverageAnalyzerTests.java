package com.shiphdmap.api.coverage;

import static org.assertj.core.api.Assertions.*;

import com.shiphdmap.api.geo.ShipFrame;
import java.util.List;
import org.junit.jupiter.api.Test;

class CoverageAnalyzerTests {
	static final CoverageAnalyzer.Sensor S = CoverageAnalyzer.DEFAULTS;

	/** A marker at range r on bearing beta from the origin, its normal pointing back at the origin. */
	static CoverageAnalyzer.Landmark at(String id, double r, double betaDeg) {
		double b = Math.toRadians(betaDeg);
		return new CoverageAnalyzer.Landmark(id, r * Math.cos(b), r * Math.sin(b), ShipFrame.wrapRad(b + Math.PI));
	}

	@Test
	void oneMarkerAlreadyDeterminesThePose() {
		var e = CoverageAnalyzer.estimate(0, 0, 0, List.of(at("A", 10, 0)), S);
		assertThat(e.n()).isEqualTo(1);
		assertThat(e.sigmaXy()).isNotNull().isFinite().isPositive();
		assertThat(e.sigmaPsiDeg()).isNotNull().isFinite().isPositive();
	}

	@Test
	void nothingVisibleIsBlind() {
		var behind = at("B", 10, 180);                       // outside the 90 deg FOV
		var e = CoverageAnalyzer.estimate(0, 0, 0, List.of(behind), S);
		assertThat(e.n()).isZero();
		assertThat(e.sigmaXy()).isNull();
		assertThat(CoverageAnalyzer.stability(e)).isNull();
	}

	@Test
	void tooFarAndTooObliqueAreNotVisible() {
		assertThat(CoverageAnalyzer.visible(0, 0, 0, at("far", 30, 0), S)).isFalse();
		// normal turned 80 deg away from the vehicle: beyond max_view_angle 70
		var oblique = new CoverageAnalyzer.Landmark("obl", 10, 0, Math.toRadians(180 - 80));
		assertThat(CoverageAnalyzer.visible(0, 0, 0, oblique, S)).isFalse();
	}

	/** The point of the whole feature: counting markers is naive, geometry decides precision. */
	@Test
	void spreadMarkersBeatClusteredOnesAtEqualCount() {
		var clustered = List.of(at("a", 10, -5), at("b", 10, 0), at("c", 10, 5));
		var spread = List.of(at("a", 10, -40), at("b", 10, 0), at("c", 10, 40));
		var ec = CoverageAnalyzer.estimate(0, 0, 0, clustered, S);
		var es = CoverageAnalyzer.estimate(0, 0, 0, spread, S);
		assertThat(ec.n()).isEqualTo(3);
		assertThat(es.n()).isEqualTo(3);
		assertThat(es.sigmaXy()).isLessThan(ec.sigmaXy());
	}

	@Test
	void worseSensorMeansWorseExpectedError() {
		var lms = List.of(at("a", 10, -30), at("b", 10, 30));
		var good = CoverageAnalyzer.estimate(0, 0, 0, lms, S);
		var bad = CoverageAnalyzer.estimate(0, 0, 0, lms,
			new CoverageAnalyzer.Sensor(S.fovRad(), S.maxDistM(), S.maxViewAngleRad(), S.sigmaR() * 4, S.sigmaTheta() * 4, S.sigmaAlpha() * 4));
		assertThat(bad.sigmaXy()).isGreaterThan(good.sigmaXy());
		assertThat(bad.sigmaPsiDeg()).isGreaterThan(good.sigmaPsiDeg());
	}

	@Test
	void zeroSensorSigmaDoesNotProduceNaN() {
		var e = CoverageAnalyzer.estimate(0, 0, 0, List.of(at("a", 10, 0)),
			new CoverageAnalyzer.Sensor(S.fovRad(), S.maxDistM(), S.maxViewAngleRad(), 0, 0, 0));
		assertThat(e.sigmaXy()).isNotNull().isFinite();
	}

	@Test
	void stabilityIsTheTighterOfTheTwoMargins() {
		var e = new CoverageAnalyzer.Estimate(2, 0.30, 1.0);   // 0.15/0.30 = 0.5 ; 2.0/1.0 = 2.0
		assertThat(CoverageAnalyzer.stability(e)).isCloseTo(0.5, within(1e-9));
	}

	static double[][] rect(double x0, double y0, double x1, double y1) {
		return new double[][] { { x0, y0, 0 }, { x1, y0, 0 }, { x1, y1, 0 }, { x0, y1, 0 }, { x0, y0, 0 } };
	}

	/** The fixture's Deck 3 markers: 18 side markers facing the lane, 4 end markers all facing astern, plus LM-0019. */
	static List<CoverageAnalyzer.Landmark> fixtureLandmarks() {
		var out = new java.util.ArrayList<CoverageAnalyzer.Landmark>();
		int n = 0;
		for (double x = 12; x <= 108 + 1e-9; x += 12) {
			out.add(new CoverageAnalyzer.Landmark(String.format("LM-%04d", ++n), x, -6.2, Math.toRadians(90)));
			out.add(new CoverageAnalyzer.Landmark(String.format("LM-%04d", ++n), x, 6.2, Math.toRadians(-90)));
		}
		out.add(new CoverageAnalyzer.Landmark("LM-0019", 40, 11.9, Math.toRadians(-90)));
		for (double[] p : new double[][] { { 119.7, -3 }, { 119.7, 3 }, { 0.3, -5.5 }, { 0.3, 5.5 } })
			out.add(new CoverageAnalyzer.Landmark(String.format("LM-%04d", ++n + 1), p[0], p[1], Math.PI));
		return out;
	}

	/**
	 * Where a vehicle can be on the fixture's Deck 3: one lane down the middle and four rows of slots either side.
	 * The real deck has 136 slot polygons; four strips per side reproduce the |y| bands the rows occupy, which is
	 * what the regressions below actually assert on.
	 */
	static CoverageAnalyzer.Scope fixtureScope() {
		var slots = new java.util.ArrayList<double[][]>();
		for (double[] band : new double[][] { { 2.4, 4.4 }, { 4.6, 6.6 }, { 6.9, 8.9 }, { 9.1, 11.1 } }) {
			slots.add(rect(0, band[0], 120, band[1]));
			slots.add(rect(0, -band[1], 120, -band[0]));
		}
		var lane = new CoverageAnalyzer.Lane(new double[][] { { 2, 0, 0 }, { 60, 0, 0 }, { 118, 0, 0 } }, 3.2);
		return new CoverageAnalyzer.Scope(slots, List.of(lane));
	}

	static double blindIn(CoverageAnalyzer.Result r, double loY, double hiY) {
		var band = r.cells().stream().filter(c -> c.inScope() && Math.abs(c.y()) >= loY && Math.abs(c.y()) < hiY).toList();
		assertThat(band).isNotEmpty();
		return (double) band.stream().filter(c -> c.sigmaXy() == null).count() / band.size();
	}

	@Test
	void gridCoversTheDeckOutlineOnly() {
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), List.of(), fixtureLandmarks(),
			CoverageAnalyzer.Scope.ALL, CoverageAnalyzer.PSI_LOAD, 2.0, S);
		assertThat(r.nDrawn()).isEqualTo(r.cells().size()).isGreaterThan(500);
		assertThat(r.cells()).allSatisfy(c -> {
			assertThat(c.x()).isBetween(0.0, 120.0);
			assertThat(c.y()).isBetween(-12.0, 12.0);
		});
		assertThat(r.blindRatio()).isBetween(0.0, 1.0);
	}

	@Test
	void cellsInsidePillarsAreNotEvaluated() {
		var pillars = List.<double[][]>of(rect(58, -1, 62, 1));
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), pillars, fixtureLandmarks(),
			CoverageAnalyzer.Scope.ALL, CoverageAnalyzer.PSI_LOAD, 1.0, S);
		assertThat(r.cells()).noneMatch(c -> c.x() > 58 && c.x() < 62 && c.y() > -1 && c.y() < 1);
	}

	@Test
	void aPillarOnTheSightLineHidesTheMarker() {
		var lm = List.of(at("A", 10, 0));
		var wall = List.<double[][]>of(rect(4, -1, 6, 1));                        // straddles the line from (0,0) to (10,0)
		var open = CoverageAnalyzer.analyze(rect(-1, -2, 1, 2), List.of(), lm, CoverageAnalyzer.Scope.ALL, 0, 1.0, S);
		var blocked = CoverageAnalyzer.analyze(rect(-1, -2, 1, 2), wall, lm, CoverageAnalyzer.Scope.ALL, 0, 1.0, S);
		assertThat(open.cells()).anyMatch(c -> c.n() == 1);
		assertThat(blocked.cells()).allMatch(c -> c.n() == 0);
	}

	@Test
	void aMarkerIsNotHiddenByThePillarItIsMountedOn() {
		var lm = List.of(at("A", 10, 0));                              // marker at (10, 0)
		var ownPillar = List.<double[][]>of(rect(10, -0.3, 10.6, 0.3));            // the face it sits on
		var r = CoverageAnalyzer.analyze(rect(-1, -2, 1, 2), ownPillar, lm, CoverageAnalyzer.Scope.ALL, 0, 1.0, S);
		assertThat(r.cells()).anyMatch(c -> c.n() == 1);
	}

	/** An empty scope means "no slots or lanes mapped yet": count every cell rather than divide by zero. */
	@Test
	void emptyScopeCountsEveryCell() {
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), List.of(), fixtureLandmarks(),
			CoverageAnalyzer.Scope.ALL, CoverageAnalyzer.PSI_LOAD, 2.0, S);
		assertThat(r.nCells()).isEqualTo(r.nDrawn());
		assertThat(r.cells()).allMatch(CoverageAnalyzer.Cell::inScope);
	}

	/** The whole point of §3.4: the deck edges are drawn but must not be in the denominator. */
	@Test
	void scopeNarrowsTheDenominatorWithoutShrinkingTheHeatmap() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var all = CoverageAnalyzer.analyze(deck, List.of(), lms, CoverageAnalyzer.Scope.ALL, CoverageAnalyzer.PSI_LOAD, 2.0, S);
		var scoped = CoverageAnalyzer.analyze(deck, List.of(), lms, fixtureScope(), CoverageAnalyzer.PSI_LOAD, 2.0, S);
		assertThat(scoped.nDrawn()).isEqualTo(all.nDrawn());              // the heatmap is unchanged
		assertThat(scoped.nCells()).isLessThan(all.nCells());             // the denominator is not
		assertThat(scoped.cells()).anyMatch(c -> !c.inScope());
		assertThat(scoped.blindRatio()).isLessThan(all.blindRatio());     // the edges were inflating it
	}

	@Test
	void worstIsTheWeakestNonBlindInScopeCell() {
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), List.of(), fixtureLandmarks(),
			fixtureScope(), CoverageAnalyzer.PSI_LOAD, 2.0, S);
		assertThat(r.worst()).isNotNull();
		assertThat(r.worst().inScope()).isTrue();
		assertThat(r.worst().stability()).isNotNull();
		double min = r.cells().stream().filter(c -> c.inScope() && c.stability() != null)
			.mapToDouble(CoverageAnalyzer.Cell::stability).min().orElseThrow();
		assertThat(r.worst().stability()).isCloseTo(min, within(1e-9));
	}

	/**
	 * Regression 1 (spec §7.2-1): the outer slot rows are blind even when loading, because every side marker sits on
	 * the single line y = +/-6.2 and the outer rows fall outside the 90 deg FOV. Flip this when markers are added.
	 */
	@Test
	void theOuterSlotRowIsBlindEvenWhenLoading() {
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), List.of(), fixtureLandmarks(),
			fixtureScope(), CoverageAnalyzer.PSI_LOAD, 1.0, S);
		assertThat(blindIn(r, 9.0, 12.0)).as("outer row, loading").isGreaterThan(0.5);
		assertThat(blindIn(r, 0.0, 2.0)).as("the lane is fine").isLessThan(0.05);
	}

	/**
	 * Regression 2 (spec §7.2-2): driving astern there is nothing ahead near the stern - the two stern end markers
	 * face -x and are invisible from anywhere on the deck (spec §3.5).
	 */
	@Test
	void unloadIsBlindNearTheStern() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var load = CoverageAnalyzer.analyze(deck, List.of(), lms, fixtureScope(), CoverageAnalyzer.PSI_LOAD, 1.0, S);
		var unload = CoverageAnalyzer.analyze(deck, List.of(), lms, fixtureScope(), CoverageAnalyzer.PSI_UNLOAD, 1.0, S);
		java.util.function.ToLongFunction<CoverageAnalyzer.Result> sternBlind = r -> r.cells().stream()
			.filter(c -> c.inScope() && c.x() < 20 && c.sigmaXy() == null).count();
		assertThat(sternBlind.applyAsLong(unload)).as("unload is blind astern").isGreaterThan(sternBlind.applyAsLong(load));
		assertThat(unload.blindRatio()).isGreaterThan(load.blindRatio());
	}

	/** The stern end markers are dead weight in both modes: their normal faces off the deck. */
	@Test
	void theSternEndMarkersAreVisibleFromNowhere() {
		var stern = new CoverageAnalyzer.Landmark("LM-0022", 0.3, -5.5, Math.PI);
		var bow = new CoverageAnalyzer.Landmark("LM-0020", 119.7, -3, Math.PI);
		var deck = rect(0, -12, 120, 12);
		for (double psi : new double[] { CoverageAnalyzer.PSI_LOAD, CoverageAnalyzer.PSI_UNLOAD })
			assertThat(CoverageAnalyzer.analyze(deck, List.of(), List.of(stern), CoverageAnalyzer.Scope.ALL, psi, 1.0, S)
				.cells()).allMatch(c -> c.n() == 0);
		assertThat(CoverageAnalyzer.analyze(deck, List.of(), List.of(bow), CoverageAnalyzer.Scope.ALL,
			CoverageAnalyzer.PSI_LOAD, 1.0, S).cells()).anyMatch(c -> c.n() == 1);
	}

	@Test
	void candidatesSitOnFacesAndFaceTheVehicleSide() {
		var deck = rect(0, -12, 120, 12);
		var pillar = rect(59.7, -0.3, 60.3, 0.3);
		var cs = CoverageAnalyzer.candidates(deck, List.of("Pillar-1"), List.<double[][]>of(pillar), List.of());
		// 4 pillar faces (0.6 m, one point each) + the deck outline split every 12 m: 2 x 10 long, 2 x 2 short
		assertThat(cs).hasSize(4 + 24);
		var stern = cs.stream().filter(c -> c.mountedOn().equals("Pillar-1") && c.x() < 59.8).findFirst().orElseThrow();
		assertThat(Math.toDegrees(stern.phiRad())).isCloseTo(180, within(1e-6));   // the aft face looks aft, away from the pillar
		var bulkhead = cs.stream().filter(c -> c.mountedOn().equals("deck") && c.x() < 0.1).findFirst().orElseThrow();
		assertThat(Math.toDegrees(bulkhead.phiRad())).isCloseTo(0, within(1e-6));  // the stern bulkhead looks forward, into the deck
	}

	/** Only the face the marker already occupies is taken: same spot AND same direction. */
	@Test
	void candidatesSkipTheFaceAnExistingMarkerAlreadyCovers() {
		var deck = rect(0, -12, 120, 12);
		var pillar = rect(59.7, -0.3, 60.3, 0.3);
		var occupied = List.of(new CoverageAnalyzer.Landmark("LM-1", 59.7, 0, Math.PI));   // on the aft face, facing aft
		var cs = CoverageAnalyzer.candidates(deck, List.of("Pillar-1"), List.<double[][]>of(pillar), occupied);
		var onPillar = cs.stream().filter(c -> c.mountedOn().equals("Pillar-1")).toList();
		assertThat(onPillar).hasSize(3);                                                   // the aft face is gone
		assertThat(onPillar).noneMatch(c -> Math.abs(ShipFrame.wrapRad(c.phiRad() - Math.PI)) < 1e-6);
	}

	/**
	 * The defect that makes §1.4 reachable. Every fixture pillar already carries a marker 0.12-0.67 m away, so a
	 * distance-only rule would drop all 72 pillar faces and leave nothing that can light up the outer slot rows.
	 */
	@Test
	void aMarkerOnOneFaceDoesNotBlockTheOppositeFace() {
		var deck = rect(0, -12, 120, 12);
		var pillar = rect(11.6, -6.86, 12.2, -6.26);                        // the fixture's shape and place
		var lm = List.of(new CoverageAnalyzer.Landmark("LM-0001", 12.0, -6.2, Math.toRadians(90)));
		var onPillar = CoverageAnalyzer.candidates(deck, List.of("P"), List.<double[][]>of(pillar), lm).stream()
			.filter(c -> c.mountedOn().equals("P")).toList();
		assertThat(onPillar).hasSize(3);
		var outward = onPillar.stream().filter(c -> Math.abs(Math.toDegrees(c.phiRad()) + 90) < 1e-6).findFirst();
		assertThat(outward).as("the face pointing away from the lane survives").isPresent();
		// and it is what reaches the outer row: y = -10 is blind without it, lit with it
		var scope = new CoverageAnalyzer.Scope(List.<double[][]>of(rect(0, -11.1, 120, -9.1)), List.of());
		var before = CoverageAnalyzer.analyze(deck, List.<double[][]>of(pillar), lm, scope, CoverageAnalyzer.PSI_LOAD, 1.0, S);
		var after = CoverageAnalyzer.analyze(deck, List.<double[][]>of(pillar),
			java.util.stream.Stream.concat(lm.stream(), java.util.stream.Stream.of(
				new CoverageAnalyzer.Landmark("NEW", outward.get().x(), outward.get().y(), outward.get().phiRad()))).toList(),
			scope, CoverageAnalyzer.PSI_LOAD, 1.0, S);
		assertThat(after.blindRatio()).isLessThan(before.blindRatio());
	}

	@Test
	void suggestFillsABlindSpotFirst() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var cands = CoverageAnalyzer.candidates(deck, List.of(), List.of(), lms);
		var out = CoverageAnalyzer.suggest(deck, List.of(), lms, fixtureScope(), cands, CoverageAnalyzer.PSI_UNLOAD, S, 2);
		assertThat(out).hasSize(2);
		assertThat(out.get(0).rank()).isEqualTo(1);
		assertThat(out.get(0).gain()).as("rank 1 removes blind cells").isPositive();
	}

	@Test
	void suggestionsNeverGetWorse() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var cands = CoverageAnalyzer.candidates(deck, List.of(), List.of(), lms);
		var out = CoverageAnalyzer.suggest(deck, List.of(), lms, fixtureScope(), cands, CoverageAnalyzer.PSI_UNLOAD, S, 3);
		for (int i = 1; i < out.size(); i++)
			assertThat(out.get(i).blindAfter()).isLessThanOrEqualTo(out.get(i - 1).blindAfter());
	}

	@Test
	void budgetIsClampedAndCandidatesAreNotReused() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var cands = CoverageAnalyzer.candidates(deck, List.of(), List.of(), lms);
		var out = CoverageAnalyzer.suggest(deck, List.of(), lms, fixtureScope(), cands, CoverageAnalyzer.PSI_UNLOAD, S, 99);
		assertThat(out.size()).isLessThanOrEqualTo(CoverageAnalyzer.MAX_BUDGET);
		assertThat(out.stream().map(s -> s.x() + "," + s.y()).distinct()).hasSize(out.size());
	}

	/**
	 * The reason §3.4 exists. A face that only lights up the empty deck edge must lose to one that lights up a slot,
	 * even though the edge has far more cells. Without the scope the greedy picks the edge.
	 */
	@Test
	void suggestIgnoresCellsNobodyDrivesThrough() {
		var deck = rect(0, -30, 40, 30);                                  // a wide deck: most of it is empty
		var lane = new CoverageAnalyzer.Lane(new double[][] { { 0, 0, 0 }, { 40, 0, 0 } }, 3.2);
		var scope = new CoverageAnalyzer.Scope(List.of(), List.of(lane));
		var edgeFace = new CoverageAnalyzer.Candidate(20, 30, Math.toRadians(-90), "edge");   // lights up the far edge
		var laneFace = new CoverageAnalyzer.Candidate(20, 2, Math.toRadians(-90), "lane");    // lights up the lane
		var out = CoverageAnalyzer.suggest(deck, List.of(), List.of(), scope,
			List.of(edgeFace, laneFace), CoverageAnalyzer.PSI_LOAD, S, 1);
		assertThat(out).hasSize(1);
		assertThat(out.get(0).mountedOn()).isEqualTo("lane");
	}
}
