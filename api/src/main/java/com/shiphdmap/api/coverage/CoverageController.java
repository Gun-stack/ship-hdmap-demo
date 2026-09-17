package com.shiphdmap.api.coverage;

import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.JsonMaps;
import com.shiphdmap.api.dataset.Datasets;
import com.shiphdmap.api.geo.Rings;
import com.shiphdmap.api.geo.Wkt;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.web.bind.annotation.*;
import tools.jackson.databind.ObjectMapper;

/** Spec M5c §4: landmark coverage for one deck. Read-only - extra_landmarks and omit never touch the database. */
@RestController
@RequestMapping("/api/datasets/{ds}/decks/{deck}/coverage")
public class CoverageController {
	private final JdbcClient db;
	private final ObjectMapper json;
	public CoverageController(JdbcClient db, ObjectMapper json) { this.db = db; this.json = json; }

	public record ExtraLandmark(Double x, Double y, Double phiDeg) {}
	public record CoverageIn(String mode, Double gridM, Double fovDeg, Double maxDistM, Double maxViewAngleDeg,
		Double sigmaR, Double sigmaTheta, Double sigmaAlpha, List<ExtraLandmark> extraLandmarks, List<String> omit,
		Integer budget) {}

	@PostMapping
	public Map<String, Object> coverage(@PathVariable String ds, @PathVariable String deck, @RequestBody(required = false) CoverageIn in) {
		var ctx = load(ds, deck, in);
		var r = CoverageAnalyzer.analyze(ctx.outline, ctx.pillars, ctx.landmarks, ctx.scope, ctx.psi, ctx.gridM, ctx.sensor);
		double otherPsi = ctx.psi == CoverageAnalyzer.PSI_LOAD ? CoverageAnalyzer.PSI_UNLOAD : CoverageAnalyzer.PSI_LOAD;
		var other = CoverageAnalyzer.analyze(ctx.outline, ctx.pillars, ctx.landmarks, ctx.scope, otherPsi, ctx.gridM, ctx.sensor);

		var body = new LinkedHashMap<String, Object>();
		body.put("deck", deck);
		body.put("mode", ctx.mode);
		body.put("grid_m", ctx.gridM);
		body.put("bbox", Rings.bbox(ctx.outline));
		body.put("n_cells", r.nCells());
		body.put("n_drawn", r.nDrawn());
		body.put("blind_ratio", r.blindRatio());
		body.put("weak_ratio", r.weakRatio());
		if (r.worst() != null) body.put("worst", cell(r.worst()));
		body.put("other_mode", Map.of("mode", ctx.mode.equals("load") ? "unload" : "load",
			"blind_ratio", other.blindRatio(), "weak_ratio", other.weakRatio()));
		var cells = new ArrayList<Map<String, Object>>(r.cells().size());
		for (var c : r.cells()) cells.add(cell(c));
		body.put("cells", cells);
		return body;
	}

	@PostMapping("/suggest")
	public Map<String, Object> suggest(@PathVariable String ds, @PathVariable String deck, @RequestBody(required = false) CoverageIn in) {
		var ctx = load(ds, deck, in);
		int budget = in == null || in.budget() == null ? 3 : in.budget();
		double g = CoverageAnalyzer.SUGGEST_GRID_M;
		var cands = CoverageAnalyzer.candidates(ctx.outline, ctx.pillarIds, ctx.pillars, ctx.landmarks);
		var picks = CoverageAnalyzer.suggest(ctx.outline, ctx.pillars, ctx.landmarks, ctx.scope, cands, ctx.psi, ctx.sensor, budget);

		var before = CoverageAnalyzer.analyze(ctx.outline, ctx.pillars, ctx.landmarks, ctx.scope, ctx.psi, g, ctx.sensor);
		var withAll = new ArrayList<>(ctx.landmarks);
		for (var p : picks) withAll.add(new CoverageAnalyzer.Landmark("CAND", p.x(), p.y(), Math.toRadians(p.phiDeg())));
		var after = CoverageAnalyzer.analyze(ctx.outline, ctx.pillars, withAll, ctx.scope, ctx.psi, g, ctx.sensor);

		var out = new ArrayList<Map<String, Object>>();
		for (var p : picks) out.add(Map.of("rank", p.rank(), "x", p.x(), "y", p.y(), "phi_deg", p.phiDeg(),
			"mounted_on", p.mountedOn(), "blind_after", p.blindAfter(), "weak_after", p.weakAfter(), "gain", p.gain()));
		var body = new LinkedHashMap<String, Object>();
		body.put("deck", deck);
		body.put("mode", ctx.mode);
		body.put("budget", Math.min(budget, CoverageAnalyzer.MAX_BUDGET));
		body.put("grid_m", g);                                           // suggest pins its own grid: spec §4.2
		body.put("before", Map.of("blind_ratio", before.blindRatio(), "weak_ratio", before.weakRatio()));
		body.put("after", Map.of("blind_ratio", after.blindRatio(), "weak_ratio", after.weakRatio()));
		body.put("suggestions", out);
		return body;
	}

	/** in_scope is written only when false: it is the exception, and most cells would carry a redundant true. */
	static Map<String, Object> cell(CoverageAnalyzer.Cell c) {
		var m = new LinkedHashMap<String, Object>();
		m.put("x", c.x()); m.put("y", c.y()); m.put("n", c.n());
		if (c.sigmaXy() != null) { m.put("sigma_xy", c.sigmaXy()); m.put("sigma_psi", c.sigmaPsiDeg()); m.put("stability", c.stability()); }
		if (!c.inScope()) m.put("in_scope", false);
		return m;
	}

	record Ctx(double[][] outline, List<double[][]> pillars, List<String> pillarIds, List<CoverageAnalyzer.Landmark> landmarks,
		CoverageAnalyzer.Scope scope, double psi, double gridM, String mode, CoverageAnalyzer.Sensor sensor) {}

	Ctx load(String ds, String deck, CoverageIn in) {
		Datasets.require(db, ds);
		var d = db.sql("SELECT ST_AsGeoJSON(outline)::text AS g FROM deck WHERE dataset_id = :ds AND id = :deck")
			.param("ds", ds).param("deck", deck).query().listOfRows().stream().findFirst()
			.orElseThrow(() -> new ApiErrors.NotFound("deck " + deck));
		double[][] outline = Wkt.coords((String) d.get("g"));

		String mode = in == null || in.mode() == null ? "load" : in.mode();
		if (!mode.equals("load") && !mode.equals("unload")) throw new ApiErrors.BadRequest("mode must be load or unload", "mode");
		double gridM = in == null || in.gridM() == null ? 1.0 : in.gridM();
		if (gridM <= 0 || gridM > 10) throw new ApiErrors.BadRequest("grid_m must be in (0, 10]", "grid_m");
		var def = CoverageAnalyzer.DEFAULTS;
		var sensor = new CoverageAnalyzer.Sensor(
			in == null || in.fovDeg() == null ? def.fovRad() : Math.toRadians(in.fovDeg()),
			in == null || in.maxDistM() == null ? def.maxDistM() : in.maxDistM(),
			in == null || in.maxViewAngleDeg() == null ? def.maxViewAngleRad() : Math.toRadians(in.maxViewAngleDeg()),
			in == null || in.sigmaR() == null ? def.sigmaR() : in.sigmaR(),
			in == null || in.sigmaTheta() == null ? def.sigmaTheta() : Math.toRadians(in.sigmaTheta()),
			in == null || in.sigmaAlpha() == null ? def.sigmaAlpha() : Math.toRadians(in.sigmaAlpha()));
		if (sensor.maxDistM() <= 0 || sensor.fovRad() <= 0) throw new ApiErrors.BadRequest("fov_deg and max_dist_m must be > 0", "fov_deg");

		// ORDER BY id on every one of these: candidate faces are generated in pillar order, and suggest() breaks a
		// tie by taking the first candidate, so an unordered scan would recommend a different face from run to run.
		var pillars = new ArrayList<double[][]>();
		var pillarIds = new ArrayList<String>();
		for (var r : rows(ds, deck, "SELECT id, ST_AsGeoJSON(geom)::text AS g FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'C' AND kind = 'pillar' ORDER BY id")) {
			pillarIds.add((String) r.get("id"));
			pillars.add(Wkt.coords((String) r.get("g")));
		}

		// Spec §3.4: where a vehicle can actually be. Same queries SlotController already uses.
		var slots = new ArrayList<double[][]>();
		for (var r : rows(ds, deck, "SELECT ST_AsGeoJSON(geom)::text AS g FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'B2' ORDER BY id"))
			slots.add(Wkt.coords((String) r.get("g")));
		var lanes = new ArrayList<CoverageAnalyzer.Lane>();
		for (var r : rows(ds, deck, "SELECT ST_AsGeoJSON(geom)::text AS g, (props->>'width_m')::float8 AS w FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'A2' ORDER BY id"))
			lanes.add(new CoverageAnalyzer.Lane(Wkt.coords((String) r.get("g")),
				r.get("w") == null ? 3.2 : ((Number) r.get("w")).doubleValue()));

		var omit = in == null || in.omit() == null ? List.<String>of() : in.omit();
		var lms = new ArrayList<CoverageAnalyzer.Landmark>();
		for (var r : rows(ds, deck, "SELECT id, ST_X(geom) AS x, ST_Y(geom) AS y, props::text AS p FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'LM'")) {
			String id = (String) r.get("id");
			if (omit.contains(id)) continue;
			double[] n = JsonMaps.doubles(JsonMaps.toMap(json, (String) r.get("p")).get("normal"));
			double phi = n == null || n.length < 2 ? Math.PI : Math.atan2(n[1], n[0]);   // no normal: treat as facing astern
			lms.add(new CoverageAnalyzer.Landmark(id, ((Number) r.get("x")).doubleValue(), ((Number) r.get("y")).doubleValue(), phi));
		}
		if (in != null && in.extraLandmarks() != null) {
			int i = 0;
			for (var e : in.extraLandmarks()) {
				if (e.x() == null || e.y() == null || e.phiDeg() == null) throw new ApiErrors.BadRequest("extra_landmarks need x, y, phi_deg", "extra_landmarks");
				lms.add(new CoverageAnalyzer.Landmark("EXTRA-" + (++i), e.x(), e.y(), Math.toRadians(e.phiDeg())));
			}
		}
		double psi = mode.equals("unload") ? CoverageAnalyzer.PSI_UNLOAD : CoverageAnalyzer.PSI_LOAD;
		return new Ctx(outline, pillars, pillarIds, lms, new CoverageAnalyzer.Scope(slots, lanes), psi, gridM, mode, sensor);
	}

	List<Map<String, Object>> rows(String ds, String deck, String sql) {
		return db.sql(sql).param("ds", ds).param("deck", deck).query().listOfRows();
	}
}
