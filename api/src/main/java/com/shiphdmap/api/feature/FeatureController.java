package com.shiphdmap.api.feature;

import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.dataset.Datasets;
import java.util.List;
import java.util.Map;
import java.util.Set;
import org.springframework.http.HttpStatus;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/api/datasets/{ds}")
public class FeatureController {
	static final Set<String> LAYERS = Set.of("A1", "A2", "B2", "C", "LM", "LP", "MEP");
	static final Set<String> STATUSES = Set.of("empty", "filled", "needs_adjust");
	private final JdbcClient db;
	private final FeatureRepo repo;
	public FeatureController(JdbcClient db, FeatureRepo repo) { this.db = db; this.repo = repo; }

	public record FeatureIn(String id, String deckId, String layer, String kind, Map<String, Object> geometry, Map<String, Object> props) {}

	@GetMapping("/features")
	public List<Map<String, Object>> list(@PathVariable String ds, @RequestParam(required = false) String deck, @RequestParam(required = false) String layer) {
		Datasets.require(db, ds);
		return repo.list(ds, deck, layer);
	}

	@GetMapping("/features/{fid}")
	public Map<String, Object> get(@PathVariable String ds, @PathVariable String fid) {
		Datasets.require(db, ds);
		return repo.get(ds, fid).orElseThrow(() -> new ApiErrors.NotFound("feature " + fid));
	}

	@PostMapping("/features")
	@ResponseStatus(HttpStatus.CREATED)
	@Transactional
	public Map<String, Object> create(@PathVariable String ds, @RequestBody FeatureIn in) {
		Datasets.require(db, ds);
		validate(ds, in, true);
		if (in.kind() == null || in.kind().isBlank()) throw new ApiErrors.BadRequest("kind is required", "kind");
		String id = in.id() == null || in.id().isBlank() ? repo.nextId(ds, in.layer()) : in.id();
		if (repo.get(ds, id).isPresent()) throw new ApiErrors.Conflict("feature " + id + " exists");
		repo.insert(ds, id, in.deckId(), in.layer(), in.kind(), FeatureRepo.toWkt(in.geometry()), in.props());
		Datasets.bumpVersion(db, ds);
		return repo.get(ds, id).orElseThrow();
	}

	@PutMapping("/features/{fid}")
	@Transactional
	public Map<String, Object> update(@PathVariable String ds, @PathVariable String fid, @RequestBody FeatureIn in) {
		Datasets.require(db, ds);
		Map<String, Object> cur = repo.get(ds, fid).orElseThrow(() -> new ApiErrors.NotFound("feature " + fid));
		if (in.layer() != null && !in.layer().equals(cur.get("layer"))) throw new ApiErrors.BadRequest("layer cannot change", "layer");
		validate(ds, new FeatureIn(fid, in.deckId(), (String) cur.get("layer"), in.kind(), in.geometry(), in.props()), false);
		String deckId = in.deckId() == null ? (String) cur.get("deck_id") : in.deckId();
		String kind = in.kind() == null ? (String) cur.get("kind") : in.kind();
		@SuppressWarnings("unchecked")
		Map<String, Object> props = in.props() == null ? (Map<String, Object>) cur.get("props") : in.props();
		@SuppressWarnings("unchecked")
		String wkt = in.geometry() == null ? FeatureRepo.toWkt((Map<String, Object>) cur.get("geometry")) : FeatureRepo.toWkt(in.geometry());
		repo.update(ds, fid, deckId, kind, wkt, props);
		Datasets.bumpVersion(db, ds);
		return repo.get(ds, fid).orElseThrow();
	}

	@DeleteMapping("/features/{fid}")
	@ResponseStatus(HttpStatus.NO_CONTENT)
	@Transactional
	public void delete(@PathVariable String ds, @PathVariable String fid) {
		Datasets.require(db, ds);
		Map<String, Object> cur = repo.get(ds, fid).orElseThrow(() -> new ApiErrors.NotFound("feature " + fid));
		if ("LP".equals(cur.get("layer"))
				&& db.sql("SELECT count(*) FROM parking_slot WHERE dataset_id = :ds AND :id = ANY(lashing_ids)").param("ds", ds).param("id", fid).query(Integer.class).single() > 0)
			throw new ApiErrors.Conflict("lashing point " + fid + " is referenced by a parking slot");
		repo.delete(ds, fid);
		Datasets.bumpVersion(db, ds);
	}

	public record StatusIn(String status) {}

	@PutMapping("/slots/{sid}/status")
	@Transactional
	public Map<String, Object> slotStatus(@PathVariable String ds, @PathVariable String sid, @RequestBody StatusIn in) {
		Datasets.require(db, ds);
		if (in.status() == null || !STATUSES.contains(in.status())) throw new ApiErrors.BadRequest("status must be one of " + STATUSES, "status");
		int n = db.sql("UPDATE parking_slot SET status = :st WHERE dataset_id = :ds AND feature_id = :id").param("st", in.status()).param("ds", ds).param("id", sid).update();
		if (n == 0) throw new ApiErrors.NotFound("slot " + sid);
		Datasets.bumpVersion(db, ds);
		return Map.of("id", sid, "status", in.status());
	}

	void validate(String ds, FeatureIn in, boolean requireGeometry) {
		if (in.layer() == null || !LAYERS.contains(in.layer())) throw new ApiErrors.BadRequest("layer must be one of " + LAYERS, "layer");
		if (in.geometry() == null) {
			if (requireGeometry) throw new ApiErrors.BadRequest("geometry {type, coordinates} is required", "geometry");
		} else if (in.geometry().get("type") == null || in.geometry().get("coordinates") == null) {
			throw new ApiErrors.BadRequest("geometry {type, coordinates} is required", "geometry");
		}
		if (in.deckId() != null && db.sql("SELECT count(*) FROM deck WHERE dataset_id = :ds AND id = :d").param("ds", ds).param("d", in.deckId()).query(Integer.class).single() == 0)
			throw new ApiErrors.BadRequest("unknown deck " + in.deckId(), "deck_id");
		if (in.geometry() != null) {
			try { FeatureRepo.toWkt(in.geometry()); } catch (IllegalArgumentException e) { throw new ApiErrors.BadRequest(e.getMessage(), "geometry"); }
		}
	}
}
