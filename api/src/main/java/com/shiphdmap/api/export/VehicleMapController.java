package com.shiphdmap.api.export;

import com.shiphdmap.api.model.VehicleMap;
import org.springframework.http.CacheControl;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.*;

@RestController
@RequestMapping("/api/datasets/{ds}")
public class VehicleMapController {
	private final VehicleMapAssembler assembler;
	public VehicleMapController(VehicleMapAssembler assembler) { this.assembler = assembler; }

	/** ETag is the dataset version; clients poll with If-None-Match and get 304 until an edit bumps it.
	 * Checks the version first so a matching If-None-Match skips assembling the full map. */
	@GetMapping("/vehicle-map")
	public ResponseEntity<VehicleMap> vehicleMap(@PathVariable String ds, @RequestHeader(value = "If-None-Match", required = false) String ifNoneMatch) {
		String etag = "\"" + assembler.version(ds) + "\"";
		if (etag.equals(ifNoneMatch)) return ResponseEntity.status(304).eTag(etag).build();
		return ResponseEntity.ok().eTag(etag).cacheControl(CacheControl.noCache()).body(assembler.assemble(ds));
	}
}
