package com.shiphdmap.api.feature;

import tools.jackson.core.type.TypeReference;
import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.geo.Wkt;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.regex.Matcher;
import java.util.regex.Pattern;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Repository;

@Repository
public class FeatureRepo {
	static final String SELECT = "SELECT dataset_id, id, deck_id, layer, kind, ST_AsGeoJSON(geom)::text AS geometry, props::text AS props, created_at, updated_at FROM feature";
	private final JdbcClient db;
	private final ObjectMapper json;
	public FeatureRepo(JdbcClient db, ObjectMapper json) { this.db = db; this.json = json; }

	public List<Map<String, Object>> list(String ds, String deck, String layer) {
		var sql = new StringBuilder(SELECT + " WHERE dataset_id = :ds");
		if (deck != null) sql.append(" AND deck_id = :deck");
		if (layer != null) sql.append(" AND layer = :layer");
		sql.append(" ORDER BY layer, id");
		var q = db.sql(sql.toString()).param("ds", ds);
		if (deck != null) q = q.param("deck", deck);
		if (layer != null) q = q.param("layer", layer);
		return q.query().listOfRows().stream().map(this::toJson).toList();
	}

	public Optional<Map<String, Object>> get(String ds, String id) {
		return db.sql(SELECT + " WHERE dataset_id = :ds AND id = :id").param("ds", ds).param("id", id).query().listOfRows().stream().findFirst().map(this::toJson);
	}

	public void insert(String ds, String id, String deck, String layer, String kind, String wkt, Map<String, Object> props) {
		db.sql("INSERT INTO feature (dataset_id, id, deck_id, layer, kind, geom, props) VALUES (:ds, :id, :deck, :layer, :kind, ST_GeomFromText(:wkt, 0), CAST(:props AS jsonb))")
			.param("ds", ds).param("id", id).param("deck", deck).param("layer", layer).param("kind", kind).param("wkt", wkt).param("props", write(props)).update();
	}

	public int update(String ds, String id, String deck, String kind, String wkt, Map<String, Object> props) {
		return db.sql("UPDATE feature SET deck_id = :deck, kind = :kind, geom = ST_GeomFromText(:wkt, 0), props = CAST(:props AS jsonb), updated_at = now() WHERE dataset_id = :ds AND id = :id")
			.param("ds", ds).param("id", id).param("deck", deck).param("kind", kind).param("wkt", wkt).param("props", write(props)).update();
	}

	public int delete(String ds, String id) { return db.sql("DELETE FROM feature WHERE dataset_id = :ds AND id = :id").param("ds", ds).param("id", id).update(); }

	static final Pattern SUFFIX = Pattern.compile("-(\\d+)$");
	/** LM-0001 style: layer prefix + 4-digit (max numeric suffix in that layer + 1). */
	public String nextId(String ds, String layer) {
		int max = 0;
		for (String id : db.sql("SELECT id FROM feature WHERE dataset_id = :ds AND layer = :layer").param("ds", ds).param("layer", layer).query(String.class).list()) {
			Matcher m = SUFFIX.matcher(id); if (m.find()) max = Math.max(max, Integer.parseInt(m.group(1)));
		}
		return String.format("%s-%04d", layer, max + 1);
	}

	Map<String, Object> toJson(Map<String, Object> row) {
		var out = new LinkedHashMap<String, Object>();
		out.put("id", row.get("id")); out.put("deck_id", row.get("deck_id")); out.put("layer", row.get("layer")); out.put("kind", row.get("kind"));
		out.put("geometry", read((String) row.get("geometry"))); out.put("props", read((String) row.get("props")));
		out.put("created_at", String.valueOf(row.get("created_at"))); out.put("updated_at", String.valueOf(row.get("updated_at")));
		return out;
	}

	String write(Map<String, Object> m) { return json.writeValueAsString(m == null ? Map.of() : m); }
	Map<String, Object> read(String s) { try { return json.readValue(s, new TypeReference<Map<String, Object>>() {}); } catch (Exception e) { throw new IllegalStateException(e); } }

	private static final tools.jackson.databind.json.JsonMapper MAPPER = new tools.jackson.databind.json.JsonMapper();

	/** GeoJSON fragment {type, coordinates} -> WKT Z. */
	public static String toWkt(Map<String, Object> geometry) {
		try {
			String g = MAPPER.writeValueAsString(geometry);
			return switch (Wkt.type(g)) {
				case "Point" -> Wkt.point(Wkt.coords(g)[0]);
				case "LineString" -> Wkt.lineString(Wkt.coords(g));
				case "Polygon" -> Wkt.polygon(Wkt.coords(g));
				default -> throw new IllegalArgumentException("unsupported geometry type");
			};
		} catch (tools.jackson.core.JacksonException e) { throw new IllegalArgumentException("bad geometry", e); }
	}
}
