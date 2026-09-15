package com.shiphdmap.api;

import tools.jackson.core.type.TypeReference;
import tools.jackson.databind.ObjectMapper;
import java.util.Map;

/** Shared JSON-string -> Map<String,Object> parsing (Jackson 3: no checked exceptions). */
public final class JsonMaps {
	private JsonMaps() {}
	public static Map<String, Object> toMap(ObjectMapper json, String s) {
		return json.readValue(s == null ? "{}" : s, new TypeReference<Map<String, Object>>() {});
	}
}
