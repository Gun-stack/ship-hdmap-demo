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
}
