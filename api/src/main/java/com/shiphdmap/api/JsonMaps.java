package com.shiphdmap.api;

import tools.jackson.core.type.TypeReference;
import tools.jackson.databind.ObjectMapper;
import java.util.List;
import java.util.Map;

/** Shared JSON-string -> Map<String,Object> parsing (Jackson 3: no checked exceptions). */
public final class JsonMaps {
	private JsonMaps() {}
	public static Map<String, Object> toMap(ObjectMapper json, String s) {
		return json.readValue(s == null ? "{}" : s, new TypeReference<Map<String, Object>>() {});
	}

	/** A props JSON array such as "normal": [nx, ny, nz] as a double[]; null when the key is absent. */
	@SuppressWarnings("unchecked")
	public static double[] doubles(Object o) {
		if (o == null) return null;
		List<Number> l = (List<Number>) o;
		double[] a = new double[l.size()];
		for (int i = 0; i < a.length; i++) a[i] = l.get(i).doubleValue();
		return a;
	}
}
