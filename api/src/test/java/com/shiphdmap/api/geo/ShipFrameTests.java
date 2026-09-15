package com.shiphdmap.api.geo;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.within;

import tools.jackson.databind.JsonNode;
import tools.jackson.databind.json.JsonMapper;
import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;

class ShipFrameTests {
	static JsonNode vectors() throws Exception {
		// api/ is one level below the repository root
		return new JsonMapper().readTree(Files.readString(Path.of("..", "docs", "test-vectors", "ship-frame.json")));
	}

	@Test
	void pointsMapToUnityLikeCSharp() throws Exception {
		for (JsonNode p : vectors().get("points")) {
			double[] u = ShipFrame.toUnity(p.get("ship").get(0).asDouble(), p.get("ship").get(1).asDouble(), p.get("ship").get(2).asDouble());
			for (int i = 0; i < 3; i++) assertThat(u[i]).isCloseTo(p.get("unity").get(i).asDouble(), within(1e-9));
		}
	}

	@Test
	void wrapDeg() throws Exception {
		for (JsonNode w : vectors().get("wrap_deg"))
			assertThat(ShipFrame.wrapDeg(w.get("in").asDouble())).isCloseTo(w.get("out").asDouble(), within(1e-9));
	}

	@Test
	void wgs84PlanarApproximation() throws Exception {
		for (JsonNode c : vectors().get("wgs84")) {
			var g = new ShipFrame.Georef(c.get("georef").get("ap_lat").asDouble(), c.get("georef").get("ap_lon").asDouble(), c.get("georef").get("heading_deg").asDouble());
			double[] ll = ShipFrame.toWgs84(g, c.get("ship").get(0).asDouble(), c.get("ship").get(1).asDouble());
			assertThat(ll[0]).as("lat " + c).isCloseTo(c.get("expect").get("lat").asDouble(), within(1e-7));
			assertThat(ll[1]).as("lon " + c).isCloseTo(c.get("expect").get("lon").asDouble(), within(1e-7));
		}
	}
}
