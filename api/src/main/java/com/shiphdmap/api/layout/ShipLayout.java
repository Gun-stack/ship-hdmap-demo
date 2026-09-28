package com.shiphdmap.api.layout;

import tools.jackson.databind.ObjectMapper;
import com.shiphdmap.api.JsonMaps;
import com.shiphdmap.api.geo.Wkt;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.stereotype.Service;

/**
 * The parts of the ship that span decks (M8): which deck the stern ramp lands on, the hoistable internal ramps between
 * decks, and the routes from the stern-ramp deck to every other deck. Slot generation and coverage both read the ship
 * through this one place, so "which ground on this deck is a driveway" and "which decks are loaded first" have one answer.
 */
@Service
public class ShipLayout {
	private final JdbcClient db;
	private final ObjectMapper json;
	public ShipLayout(JdbcClient db, ObjectMapper json) { this.db = db; this.json = json; }

	/** z tolerance for "this point lies on that deck": generator output is rounded to 1e-6, a deck pitch is metres. */
	static final double ON_DECK_M = 0.05;

	public record InnerRamp(String id, String lowerDeck, String upperDeck, double[][] footprint) {}

	public record View(List<String> decksByZ, Map<String, Double> zByDeck, String sternDeck, List<double[][]> routes, List<InnerRamp> innerRamps) {
		/** Each route's runs of consecutive vertices lying on this deck: the ground on it that cars drive over to reach some deck. */
		public List<double[][]> routeRunsOn(String deck) {
			Double z = zByDeck.get(deck);
			return z == null ? List.of() : runsAt(routes, z);
		}

		/**
		 * Ramp ground on this deck that can never hold a car: the ramp's FAR side (the deck farther from the stern-ramp deck).
		 * Going down it is the landing, going up the opening the ramp swings into -- either way, once the ramp is stowed the
		 * far deck is closed off, and while it is deployed the ground is ramp. The NEAR side is left to the slot generator:
		 * a stowed ramp frees it, and the far-decks-first loading order stows the ramp before that deck is loaded.
		 */
		public List<double[][]> farFootprintsOn(String deck) {
			var out = new ArrayList<double[][]>();
			for (var r : innerRamps) if (deck.equals(farDeck(r))) out.add(r.footprint());
			return out;
		}

		public String farDeck(InnerRamp r) {
			int s = decksByZ.indexOf(sternDeck), lo = decksByZ.indexOf(r.lowerDeck()), up = decksByZ.indexOf(r.upperDeck());
			if (s < 0 || lo < 0 || up < 0) return null;
			int dl = Math.abs(lo - s), du = Math.abs(up - s);
			return dl == du ? null : dl > du ? r.lowerDeck() : r.upperDeck();
		}

		/** Loading order of a deck: 0 is loaded first. See ShipLayout.rank. */
		public int rank(String deck) { return ShipLayout.rank(decksByZ, sternDeck, deck); }
	}

	public View load(String ds) {
		var decks = new ArrayList<String>(); var zs = new LinkedHashMap<String, Double>();
		for (var r : db.sql("SELECT id, z_surface FROM deck WHERE dataset_id = :ds ORDER BY z_surface").param("ds", ds).query().listOfRows()) {
			decks.add((String) r.get("id")); zs.put((String) r.get("id"), ((Number) r.get("z_surface")).doubleValue());
		}
		String stern = null; var inner = new ArrayList<InnerRamp>();
		for (var r : db.sql("SELECT id, deck_id, ST_AsGeoJSON(geom)::text AS g, props::text AS p FROM feature WHERE dataset_id = :ds AND layer = 'C' AND kind = 'ramp' ORDER BY id").param("ds", ds).query().listOfRows()) {
			Map<String, Object> p = JsonMaps.toMap(json, (String) r.get("p"));
			String type = p.get("type") == null ? "stern_quarter" : p.get("type").toString();
			if ("stern_quarter".equals(type)) { if (stern == null) stern = (String) r.get("deck_id"); continue; }
			double[][] hinge = Wkt.coords((String) r.get("g")), toe = JsonMaps.doubleRows(p.get("toe"));
			if (hinge.length < 2 || toe == null || toe.length < 2) continue;
			double[][] ring = { hinge[0], hinge[1], toe[1], toe[0], hinge[0] };
			inner.add(new InnerRamp((String) r.get("id"), (String) p.get("lower_deck"), (String) p.get("upper_deck"), ring));
		}
		var routes = new ArrayList<double[][]>();
		for (var r : db.sql("SELECT ST_AsGeoJSON(geom)::text AS g FROM feature WHERE dataset_id = :ds AND layer = 'A2' AND kind = 'route' ORDER BY id").param("ds", ds).query().listOfRows())
			routes.add(Wkt.coords((String) r.get("g")));
		return new View(decks, zs, stern, routes, inner);
	}

	/** Split polylines into their maximal runs of vertices within ON_DECK_M of z; runs shorter than one segment are dropped. */
	public static List<double[][]> runsAt(List<double[][]> paths, double z) {
		var out = new ArrayList<double[][]>();
		for (var path : paths) {
			var run = new ArrayList<double[]>();
			for (var pt : path) {
				if (pt.length > 2 && Math.abs(pt[2] - z) <= ON_DECK_M) { run.add(pt); continue; }
				if (run.size() >= 2) out.add(run.toArray(double[][]::new));
				run.clear();
			}
			if (run.size() >= 2) out.add(run.toArray(double[][]::new));
		}
		return out;
	}

	/**
	 * Far decks first: decks farther (in deck count) from the stern-ramp deck load earlier, and between two at the same
	 * distance the lower one first. With D3 as the stern-ramp deck that is D1, D5, D2, D4, D3 -- every internal ramp can be
	 * stowed once the decks beyond it are done, before the deck it opens onto is loaded. No stern ramp: plain z order.
	 */
	public static int rank(List<String> decksByZ, String sternDeck, String deck) {
		int s = sternDeck == null ? -1 : decksByZ.indexOf(sternDeck);
		var order = new ArrayList<>(decksByZ);
		order.sort(Comparator.<String>comparingInt(d -> s < 0 ? 0 : -Math.abs(decksByZ.indexOf(d) - s)).thenComparingInt(decksByZ::indexOf));
		return order.indexOf(deck);
	}
}
