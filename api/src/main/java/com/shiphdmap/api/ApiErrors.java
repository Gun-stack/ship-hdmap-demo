package com.shiphdmap.api;

import java.util.LinkedHashMap;
import java.util.Map;
import org.springframework.dao.DataIntegrityViolationException;
import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.ExceptionHandler;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestControllerAdvice;

/** One place for API errors: 400/404/409 exceptions and DB constraint violations -> 400 {status, error, message, field?}. */
@RestControllerAdvice
public class ApiErrors {
	public static class BadRequest extends RuntimeException {
		public final String field;
		public BadRequest(String m) { this(m, null); }
		public BadRequest(String m, String field) { super(m); this.field = field; }
	}
	public static class NotFound extends RuntimeException { public NotFound(String m) { super(m); } }
	public static class Conflict extends RuntimeException { public Conflict(String m) { super(m); } }

	@ExceptionHandler(BadRequest.class) @ResponseStatus(HttpStatus.BAD_REQUEST)
	Map<String, Object> badRequest(BadRequest e) { return body(400, "Bad Request", e.getMessage(), e.field); }

	@ExceptionHandler(NotFound.class) @ResponseStatus(HttpStatus.NOT_FOUND)
	Map<String, Object> notFound(NotFound e) { return body(404, "Not Found", e.getMessage(), null); }

	@ExceptionHandler(Conflict.class) @ResponseStatus(HttpStatus.CONFLICT)
	Map<String, Object> conflict(Conflict e) { return body(409, "Conflict", e.getMessage(), null); }

	@ExceptionHandler(DataIntegrityViolationException.class) @ResponseStatus(HttpStatus.BAD_REQUEST)
	Map<String, Object> constraint(DataIntegrityViolationException e) {
		String m = e.getMostSpecificCause().getMessage();
		return body(400, "Bad Request", m == null ? "constraint violation" : m.lines().findFirst().orElse(m), null);
	}

	static Map<String, Object> body(int status, String error, String message, String field) {
		var b = new LinkedHashMap<String, Object>();
		b.put("status", status); b.put("error", error); b.put("message", message);
		if (field != null) b.put("field", field);
		return b;
	}
}
