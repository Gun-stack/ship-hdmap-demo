package com.shiphdmap.api;

import org.springframework.boot.test.context.TestConfiguration;
import org.springframework.boot.testcontainers.service.connection.ServiceConnection;
import org.springframework.context.annotation.Bean;
import org.testcontainers.postgresql.PostgreSQLContainer;
import org.testcontainers.utility.DockerImageName;

@TestConfiguration(proxyBeanMethods = false)
public class TestcontainersConfiguration {
	@Bean
	@ServiceConnection
	PostgreSQLContainer postgres() {
		// arm64 build of PostGIS 17; the official postgis/postgis image has no arm64 manifest
		return new PostgreSQLContainer(DockerImageName.parse("imresamu/postgis:17-3.5").asCompatibleSubstituteFor("postgres"));
	}
}
