package com.shiphdmap.api;

import static org.assertj.core.api.Assertions.assertThat;

import java.util.List;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.context.annotation.Import;
import org.springframework.jdbc.core.simple.JdbcClient;

@Import(TestcontainersConfiguration.class)
@SpringBootTest
class ApiApplicationTests {
	@Autowired JdbcClient db;

	@Test
	void migrationCreatesFourTablesAndPostgis() {
		List<String> tables = db.sql("SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' ORDER BY 1").query(String.class).list();
		assertThat(tables).contains("dataset", "deck", "feature", "parking_slot");
		String v = db.sql("SELECT postgis_version()").query(String.class).single();
		assertThat(v).startsWith("3.5");
	}
}
