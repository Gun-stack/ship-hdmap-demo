package com.shiphdmap.api.export;

import com.shiphdmap.api.dataset.Datasets;
import com.shiphdmap.api.feature.FeatureRepo;
import com.shiphdmap.api.geo.ShipFrame;
import com.shiphdmap.api.geo.Wkt;
import com.shiphdmap.api.pose.PoseStore;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.web.bind.annotation.*;

/** QGIS-facing export: WGS84 derived at request time from the current pose (spec §3.5). Nothing WGS84 is stored. */
@RestController
@RequestMapping("/api/datasets/{ds}")
public class GeoJsonController {
	private static final tools.jackson.databind.json.JsonMapper MAPPER = new tools.jackson.databind.json.JsonMapper();
	private final JdbcClient db;
	private final FeatureRepo features;
	private final PoseStore poses;
	public GeoJsonController(JdbcClient db, FeatureRepo features, PoseStore poses) { this.db = db; this.features = features; this.poses = poses; }

	@GetMapping(value = "/export.geojson", produces = "application/geo+json")
	@SuppressWarnings("unchecked")
	public ResponseEntity<Map<String, Object>> export(@PathVariable String ds) {
		Datasets.require(db, ds);
		ShipFrame.Georef g = poses.georef(ds);
		var out = new ArrayList<Map<String, Object>>();
		for (Map<String, Object> d : db.sql("SELECT id, name, z_surface, ST_AsGeoJSON(outline)::text g FROM deck WHERE dataset_id = :ds ORDER BY id").param("ds", ds).query().listOfRows()) {
			var props = new LinkedHashMap<String, Object>(); props.put("id", d.get("id")); props.put("layer", "DECK"); props.put("kind", "deck"); props.put("name", d.get("name")); props.put("z_surface", d.get("z_surface"));
			out.add(feature("Polygon", ring(Wkt.coords((String) d.get("g")), g), props));
		}
		for (Map<String, Object> f : features.list(ds, null, null)) {
			Map<String, Object> geom = (Map<String, Object>) f.get("geometry");
			String type = (String) geom.get("type");
			double[][] c = Wkt.coords(geoJson(geom));
			Object coords = switch (type) { case "Point" -> pt(c[0], g); case "LineString" -> line(c, g); default -> ring(c, g); };
			var props = new LinkedHashMap<String, Object>((Map<String, Object>) f.get("props"));
			props.put("id", f.get("id")); props.put("layer", f.get("layer")); props.put("kind", f.get("kind")); props.put("deck_id", f.get("deck_id"));
			out.add(feature(type, coords, props));
		}
		var fc = new LinkedHashMap<String, Object>(); fc.put("type", "FeatureCollection"); fc.put("features", out);
		return ResponseEntity.ok().contentType(MediaType.parseMediaType("application/geo+json")).body(fc);
	}

	static Map<String, Object> feature(String type, Object coords, Map<String, Object> props) {
		var geom = new LinkedHashMap<String, Object>(); geom.put("type", type); geom.put("coordinates", coords);
		var f = new LinkedHashMap<String, Object>(); f.put("type", "Feature"); f.put("geometry", geom); f.put("properties", props);
		return f;
	}
	static double r8(double v) { return Math.round(v * 1e8) / 1e8; }
	static double[] pt(double[] p, ShipFrame.Georef g) { double[] ll = ShipFrame.toWgs84(g, p[0], p[1]); return new double[] { r8(ll[1]), r8(ll[0]), p.length > 2 ? p[2] : 0 }; }
	static List<double[]> line(double[][] pts, ShipFrame.Georef g) { var l = new ArrayList<double[]>(); for (double[] p : pts) l.add(pt(p, g)); return l; }
	static List<List<double[]>> ring(double[][] pts, ShipFrame.Georef g) { return List.of(line(pts, g)); }
	static String geoJson(Map<String, Object> geom) { try { return MAPPER.writeValueAsString(geom); } catch (Exception e) { throw new IllegalStateException(e); } }
}
