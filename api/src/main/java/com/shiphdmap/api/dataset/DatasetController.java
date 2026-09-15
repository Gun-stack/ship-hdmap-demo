package com.shiphdmap.api.dataset;

import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.model.SeedData;
import java.util.Map;
import org.springframework.dao.DuplicateKeyException;
import org.springframework.http.HttpStatus;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/api/datasets")
public class DatasetController {
	private final JdbcClient db;
	private final SeedImporter importer;
	public DatasetController(JdbcClient db, SeedImporter importer) { this.db = db; this.importer = importer; }

	public record NewDataset(String id, String name, String shipName, Double apLat, Double apLon, Double headingDeg, Double lppM) {}

	@PostMapping
	@ResponseStatus(HttpStatus.CREATED)
	public Map<String, Object> create(@RequestBody NewDataset in) {
		if (in.id() == null || in.id().isBlank()) throw new ApiErrors.BadRequest("id is required", "id");
		if (in.name() == null || in.name().isBlank()) throw new ApiErrors.BadRequest("name is required", "name");
		try {
			db.sql("INSERT INTO dataset (id, name, ship_name, ap_lat, ap_lon, heading_deg, lpp_m) VALUES (:id, :name, :ship, :lat, :lon, :hdg, :lpp)")
				.param("id", in.id()).param("name", in.name()).param("ship", in.shipName() == null ? "" : in.shipName())
				.param("lat", in.apLat() == null ? 0.0 : in.apLat()).param("lon", in.apLon() == null ? 0.0 : in.apLon())
				.param("hdg", in.headingDeg() == null ? 0.0 : in.headingDeg()).param("lpp", in.lppM() == null ? 120.0 : in.lppM()).update();
		} catch (DuplicateKeyException e) { throw new ApiErrors.Conflict("dataset " + in.id() + " exists"); }
		return Map.of("id", in.id(), "version", 1);
	}

	@GetMapping("/{id}")
	public Map<String, Object> get(@PathVariable String id) {
		var rows = db.sql("SELECT id, name, ship_name, version, ap_lat, ap_lon, heading_deg, lpp_m, created_at FROM dataset WHERE id = :id")
			.param("id", id).query().listOfRows();
		if (rows.isEmpty()) throw new ApiErrors.NotFound("dataset " + id);
		return rows.get(0);
	}

	@PostMapping("/{id}/seed")
	public Map<String, Object> seed(@PathVariable String id, @RequestBody SeedData seed) { return importer.importSeed(id, seed); }
}
