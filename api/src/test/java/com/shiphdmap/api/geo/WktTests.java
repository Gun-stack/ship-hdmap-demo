package com.shiphdmap.api.geo;

import static org.assertj.core.api.Assertions.assertThat;

import org.junit.jupiter.api.Test;

class WktTests {
	@Test
	void buildsWktWithZ() {
		assertThat(Wkt.point(new double[] { 82.4, 3.1, 10.6 })).isEqualTo("POINT Z(82.4 3.1 10.6)");
		assertThat(Wkt.lineString(new double[][] { { 2, 0, 10.6 }, { 60, 0, 10.6 } })).isEqualTo("LINESTRING Z(2.0 0.0 10.6,60.0 0.0 10.6)");
		assertThat(Wkt.polygon(new double[][] { { 0, 0, 1 }, { 1, 0, 1 }, { 1, 1, 1 } })).isEqualTo("POLYGON Z((0.0 0.0 1.0,1.0 0.0 1.0,1.0 1.0 1.0,0.0 0.0 1.0))");
		assertThat(Wkt.polygon(new double[][] { { 0, 0, 1 }, { 1, 0, 1 }, { 1, 1, 1 }, { 0, 0, 1 } })).endsWith("0.0 0.0 1.0))");
	}

	@Test
	void parsesGeoJsonCoordinates() {
		assertThat(Wkt.type("{\"type\":\"Point\",\"coordinates\":[1,2,3]}")).isEqualTo("Point");
		assertThat(Wkt.coords("{\"type\":\"Point\",\"coordinates\":[1,2,3]}")).isEqualTo(new double[][] { { 1, 2, 3 } });
		assertThat(Wkt.coords("{\"type\":\"LineString\",\"coordinates\":[[1,2,3],[4,5,6]]}")).isEqualTo(new double[][] { { 1, 2, 3 }, { 4, 5, 6 } });
		assertThat(Wkt.coords("{\"type\":\"Polygon\",\"coordinates\":[[[0,0,1],[1,0,1],[1,1,1],[0,0,1]]]}")).hasNumberOfRows(4);
	}
}
