package com.shiphdmap.api.dataset;

import com.shiphdmap.api.ApiErrors;
import org.springframework.jdbc.core.simple.JdbcClient;

public final class Datasets {
	private Datasets() {}
	public static void require(JdbcClient db, String id) {
		if (db.sql("SELECT count(*) FROM dataset WHERE id = :id").param("id", id).query(Integer.class).single() == 0) throw new ApiErrors.NotFound("dataset " + id);
	}
	/** Every write bumps the version inside the caller's transaction; the new value is the vehicle-map ETag. */
	public static int bumpVersion(JdbcClient db, String id) {
		return db.sql("UPDATE dataset SET version = version + 1 WHERE id = :id RETURNING version").param("id", id).query(Integer.class).single();
	}
}
