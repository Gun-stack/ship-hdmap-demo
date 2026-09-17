# M5c 커버리지 분석·배치 추천 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 차가 실제로 지나가고 주차하는 곳마다 달성 가능한 추정 정밀도를 계산해 히트맵으로 보이고, 가상 마커로 실험하고, 다음에 놓을 마커 위치를 추천한다.

**Architecture:** 계산은 전부 API 다. `CoverageAnalyzer` 는 DB 도 Spring 도 모르는 순수 정적 클래스로 `SlotGenerator` 와 같은 자리에 선다. 컨트롤러가 DB 에서 갑판 윤곽·랜드마크·기둥·구획·차로를 모아 넘기고 결과를 JSON 으로 낸다. Unity 는 건드리지 않는다. 웹은 결과를 평면도 SVG 위에 히트맵으로 그리고 파라미터·후보·추천을 패널에서 다룬다.

**Tech Stack:** Java 25 / Spring Boot 4 / JdbcClient / PostGIS, JUnit 5 + AssertJ, React 19 + Zustand + Vite, Vitest

**Spec:** `docs/superpowers/specs/2026-09-17-m5c-coverage-design.md`

## Global Constraints

- 좌표는 Ship Frame `[x, y, z]` m. 각도는 **와이어에서 도(°), 내부 계산은 라디안**
- JSON 키는 snake_case. **null 인 필드는 응답에서 생략된다**
- API 패키지 규칙: 순수 계산기와 컨트롤러를 같은 패키지에 두고, 계산기는 Spring 을 import 하지 않는다 (`SlotGenerator` / `SlotController` 와 동일)
- 허용오차 기준값은 `ScenarioPlanner.Judge` 의 기본값과 같다: `lat_m = 0.15`, `heading_deg = 2.0`
- 센서 기본값은 `LandmarkSensor` 와 같다: `fov 90°`, `max_dist 25 m`, `max_view_angle 70°`, `σ_r 0.2`, `σ_θ 1°`, `σ_α 2°`
- **이미 있는 것을 다시 쓰지 않는다.** 링 기하는 `SlotGenerator` 에 있고(`contains`, `bbox`) `geo/Rings` 로 올려 공유한다. `props` 의 JSON 배열은 `VehicleMapAssembler.arr` 가 이미 꺼내며 `JsonMaps` 로 옮겨 공유한다. `props` 는 코드베이스 관례대로 `props::text` 로 읽는다
- 자바 들여쓰기는 탭, 웹은 스페이스 2 칸 (기존 파일 그대로)
- 커밋 메시지는 한 줄 영문 요약. **자명하지 않은 변경은 본문에 왜 그렇게 했는지 적는다** — 기존 이력이 그렇다
  (`test: pin the handover gap to the estimation error instead of a magic number` 참조). 본문 끝에
  `Co-Authored-By:` 와 `Claude-Session:` 두 줄을 붙인다. 아래 각 Task 의 커밋 명령은 제목만 적어 두었다

---

## File Structure

| 파일 | 책임 |
| --- | --- |
| `api/…/geo/ShipFrame.java` | `wrapRad` 추가 (C# `ShipFrame.WrapRad` 와 대칭) |
| `api/…/geo/Rings.java` | **신규.** `contains`·`bbox` 를 `SlotGenerator` 에서 옮기고 `distToPolyline` 추가 |
| `api/…/slots/SlotGenerator.java` | `contains`·`bbox` 를 `Rings` 로 위임 |
| `api/…/JsonMaps.java` | `doubles(Object)` 추가 (`VehicleMapAssembler.arr` 를 옮긴 것) |
| `api/…/export/VehicleMapAssembler.java` | `arr` 를 `JsonMaps.doubles` 로 위임 |
| `api/…/coverage/CoverageAnalyzer.java` | 순수 계산: 가시성·차폐·정보행렬·격자·평가 대상·후보·그리디 |
| `api/…/coverage/CoverageController.java` | `POST …/coverage`, `POST …/coverage/suggest`. DB 조회와 JSON 조립 |
| `api/…/coverage/CoverageAnalyzerTests.java` | 수학·격자·차폐·평가 대상·추천 단위 테스트, 회귀 2 건 |
| `api/…/coverage/CoverageApiTests.java` | 엔드포인트 계약, 실제 픽스처 숫자, `extra_landmarks`/`omit` 이 DB 를 바꾸지 않음 |
| `web/src/api/types.ts` | `CoverageIn`, `CoverageOut`, `CoverageCell`, `SuggestOut` |
| `web/src/api/client.ts` | `coverage`, `suggestCoverage` |
| `web/src/store/editor.ts` | `coverage` 상태, `candidates`, `runCoverage`, `commitCandidates` |
| `web/src/geo/coverage.ts` | `cellColor`, `cellOpacity`, `fmtRatio` — 순수 함수, Vitest 대상 |
| `web/src/geo/coverage.test.ts` | 위 세 함수 |
| `web/src/components/MiniMap.tsx` | 히트맵 레이어 (표시 전용, 클릭 핸들러 없음) |
| `web/src/components/CoveragePanel.tsx` | 파라미터·요약·후보·추천·확정 |
| `web/src/App.tsx` | 편집 모드 오른쪽에 `CoveragePanel` |
| `docs/api-contract.md` | 엔드포인트 2 개 |

---

### Task 1: 공유 기하와 예상 오차 σ

수학 코어. 격자도 차폐도 없이 "한 지점에서 마커 목록이 주어지면 σ 가 얼마인가"만 만든다. 먼저 이미 있는 기하 헬퍼를 공유 위치로 올린다.

**Files:**
- Modify: `api/src/main/java/com/shiphdmap/api/geo/ShipFrame.java`
- Create: `api/src/main/java/com/shiphdmap/api/geo/Rings.java`
- Modify: `api/src/main/java/com/shiphdmap/api/slots/SlotGenerator.java`
- Create: `api/src/main/java/com/shiphdmap/api/coverage/CoverageAnalyzer.java`
- Test: `api/src/test/java/com/shiphdmap/api/coverage/CoverageAnalyzerTests.java`

**Interfaces:**
- Produces: `ShipFrame.wrapRad(double)`
- Produces: `Rings.contains(double[][], double, double)`, `Rings.bbox(double[][])`, `Rings.distToPolyline(double[][], double, double)`
- Produces: `CoverageAnalyzer.Landmark(String id, double x, double y, double phiRad)`
- Produces: `CoverageAnalyzer.Sensor(double fovRad, double maxDistM, double maxViewAngleRad, double sigmaR, double sigmaTheta, double sigmaAlpha)`
- Produces: `CoverageAnalyzer.Estimate(int n, Double sigmaXy, Double sigmaPsiDeg)` — 보이는 마커가 없거나 특이행렬이면 σ 둘 다 `null`. **`n` 은 그대로 남긴다**
- Produces: `CoverageAnalyzer.DEFAULTS`, `TOL_LAT_M`, `TOL_HEADING_DEG`
- Produces: `CoverageAnalyzer.visible(...)` (package-private), `estimate(...)`, `stability(Estimate)`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`api/src/test/java/com/shiphdmap/api/coverage/CoverageAnalyzerTests.java`:

```java
package com.shiphdmap.api.coverage;

import static org.assertj.core.api.Assertions.*;

import com.shiphdmap.api.geo.ShipFrame;
import java.util.List;
import org.junit.jupiter.api.Test;

class CoverageAnalyzerTests {
	static final CoverageAnalyzer.Sensor S = CoverageAnalyzer.DEFAULTS;

	/** A marker at range r on bearing beta from the origin, its normal pointing back at the origin. */
	static CoverageAnalyzer.Landmark at(String id, double r, double betaDeg) {
		double b = Math.toRadians(betaDeg);
		return new CoverageAnalyzer.Landmark(id, r * Math.cos(b), r * Math.sin(b), ShipFrame.wrapRad(b + Math.PI));
	}

	@Test
	void oneMarkerAlreadyDeterminesThePose() {
		var e = CoverageAnalyzer.estimate(0, 0, 0, List.of(at("A", 10, 0)), S);
		assertThat(e.n()).isEqualTo(1);
		assertThat(e.sigmaXy()).isNotNull().isFinite().isPositive();
		assertThat(e.sigmaPsiDeg()).isNotNull().isFinite().isPositive();
	}

	@Test
	void nothingVisibleIsBlind() {
		var behind = at("B", 10, 180);                       // outside the 90 deg FOV
		var e = CoverageAnalyzer.estimate(0, 0, 0, List.of(behind), S);
		assertThat(e.n()).isZero();
		assertThat(e.sigmaXy()).isNull();
		assertThat(CoverageAnalyzer.stability(e)).isNull();
	}

	@Test
	void tooFarAndTooObliqueAreNotVisible() {
		assertThat(CoverageAnalyzer.visible(0, 0, 0, at("far", 30, 0), S)).isFalse();
		// normal turned 80 deg away from the vehicle: beyond max_view_angle 70
		var oblique = new CoverageAnalyzer.Landmark("obl", 10, 0, Math.toRadians(180 - 80));
		assertThat(CoverageAnalyzer.visible(0, 0, 0, oblique, S)).isFalse();
	}

	/** The point of the whole feature: counting markers is naive, geometry decides precision. */
	@Test
	void spreadMarkersBeatClusteredOnesAtEqualCount() {
		var clustered = List.of(at("a", 10, -5), at("b", 10, 0), at("c", 10, 5));
		var spread = List.of(at("a", 10, -40), at("b", 10, 0), at("c", 10, 40));
		var ec = CoverageAnalyzer.estimate(0, 0, 0, clustered, S);
		var es = CoverageAnalyzer.estimate(0, 0, 0, spread, S);
		assertThat(ec.n()).isEqualTo(3);
		assertThat(es.n()).isEqualTo(3);
		assertThat(es.sigmaXy()).isLessThan(ec.sigmaXy());
	}

	@Test
	void worseSensorMeansWorseExpectedError() {
		var lms = List.of(at("a", 10, -30), at("b", 10, 30));
		var good = CoverageAnalyzer.estimate(0, 0, 0, lms, S);
		var bad = CoverageAnalyzer.estimate(0, 0, 0, lms,
			new CoverageAnalyzer.Sensor(S.fovRad(), S.maxDistM(), S.maxViewAngleRad(), S.sigmaR() * 4, S.sigmaTheta() * 4, S.sigmaAlpha() * 4));
		assertThat(bad.sigmaXy()).isGreaterThan(good.sigmaXy());
		assertThat(bad.sigmaPsiDeg()).isGreaterThan(good.sigmaPsiDeg());
	}

	@Test
	void zeroSensorSigmaDoesNotProduceNaN() {
		var e = CoverageAnalyzer.estimate(0, 0, 0, List.of(at("a", 10, 0)),
			new CoverageAnalyzer.Sensor(S.fovRad(), S.maxDistM(), S.maxViewAngleRad(), 0, 0, 0));
		assertThat(e.sigmaXy()).isNotNull().isFinite();
	}

	@Test
	void stabilityIsTheTighterOfTheTwoMargins() {
		var e = new CoverageAnalyzer.Estimate(2, 0.30, 1.0);   // 0.15/0.30 = 0.5 ; 2.0/1.0 = 2.0
		assertThat(CoverageAnalyzer.stability(e)).isCloseTo(0.5, within(1e-9));
	}
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd api && ./gradlew test --tests '*CoverageAnalyzerTests*'`
Expected: 컴파일 실패 — `CoverageAnalyzer` 와 `ShipFrame.wrapRad` 가 없다

- [ ] **Step 3: `ShipFrame.wrapRad` 를 더한다**

`api/src/main/java/com/shiphdmap/api/geo/ShipFrame.java` 의 `wrapDeg` 바로 아래에:

```java
	/** (-pi, pi]. Mirrors the C# ShipFrame.WrapRad. */
	public static double wrapRad(double a) {
		a %= 2 * Math.PI;
		if (a <= -Math.PI) a += 2 * Math.PI; else if (a > Math.PI) a -= 2 * Math.PI;
		return a;
	}
```

- [ ] **Step 4: 링 기하를 `geo/Rings` 로 올린다**

`SlotGenerator` 의 `contains` 와 `bbox` 를 그대로 옮긴다. 복사가 아니라 **이동**이다 — 두 벌이 생기면 갈라진다.

`api/src/main/java/com/shiphdmap/api/geo/Rings.java`:

```java
package com.shiphdmap.api.geo;

/** Closed-ring and polyline helpers on the x-y projection. Shared by SlotGenerator and CoverageAnalyzer. */
public final class Rings {
	private Rings() {}

	/** Even-odd ray casting on the x-y projection of a closed ring. */
	public static boolean contains(double[][] ring, double x, double y) {
		boolean in = false;
		for (int i = 0, j = ring.length - 1; i < ring.length; j = i++) {
			double xi = ring[i][0], yi = ring[i][1], xj = ring[j][0], yj = ring[j][1];
			if ((yi > y) != (yj > y) && x < (xj - xi) * (y - yi) / (yj - yi) + xi) in = !in;
		}
		return in;
	}

	public static double[] bbox(double[][] ring) {
		double minX = Double.MAX_VALUE, minY = Double.MAX_VALUE, maxX = -Double.MAX_VALUE, maxY = -Double.MAX_VALUE;
		for (var p : ring) { minX = Math.min(minX, p[0]); minY = Math.min(minY, p[1]); maxX = Math.max(maxX, p[0]); maxY = Math.max(maxY, p[1]); }
		return new double[] { minX, minY, maxX, maxY };
	}

	/** Distance from (x, y) to a polyline, x-y only. Lane corridors are "within width/2 of the centreline". */
	public static double distToPolyline(double[][] pts, double x, double y) {
		double best = Double.MAX_VALUE;
		for (int i = 0; i + 1 < pts.length; i++) {
			double ax = pts[i][0], ay = pts[i][1], bx = pts[i + 1][0], by = pts[i + 1][1];
			double vx = bx - ax, vy = by - ay, l2 = vx * vx + vy * vy;
			double t = l2 < 1e-12 ? 0 : Math.max(0, Math.min(1, ((x - ax) * vx + (y - ay) * vy) / l2));
			best = Math.min(best, Math.hypot(x - (ax + t * vx), y - (ay + t * vy)));
		}
		return best;
	}
}
```

`SlotGenerator` 에서 두 메서드의 **몸통을 지우고 위임**한다 (기존 호출자와 테스트가 그대로 통과하게):

```java
	static double[] bbox(double[][] ring) { return Rings.bbox(ring); }

	/** Even-odd ray casting on the x-y projection of a closed ring. */
	public static boolean contains(double[][] ring, double x, double y) { return Rings.contains(ring, x, y); }
```

`import com.shiphdmap.api.geo.Rings;` 를 더한다.

- [ ] **Step 5: `CoverageAnalyzer` 의 수학 부분을 만든다**

`api/src/main/java/com/shiphdmap/api/coverage/CoverageAnalyzer.java`:

```java
package com.shiphdmap.api.coverage;

import com.shiphdmap.api.geo.ShipFrame;
import java.util.List;

/**
 * Landmark coverage: how well a vehicle could localise itself at each point of a deck (spec M5c §3).
 * Pure: no DB, no Spring, same shape as SlotGenerator.
 *
 * The estimate is the Cramer-Rao style lower bound of Localizer's Gauss-Newton: the same Jacobians and the same
 * 1/sigma^2 weights build A = J^T W J, and the diagonal of A^-1 is the achievable variance. No residuals are needed
 * because we are not estimating a pose, only asking how precisely one could be estimated from this spot.
 */
public final class CoverageAnalyzer {
	private CoverageAnalyzer() {}

	/** A map landmark: position and normal angle phi = atan2(ny, nx). */
	public record Landmark(String id, double x, double y, double phiRad) {}
	/** Vehicle sensor model. Angles in radians. */
	public record Sensor(double fovRad, double maxDistM, double maxViewAngleRad, double sigmaR, double sigmaTheta, double sigmaAlpha) {}
	/** Achievable precision at one spot. sigmaXy/sigmaPsiDeg are null when the spot is blind; n survives either way. */
	public record Estimate(int n, Double sigmaXy, Double sigmaPsiDeg) {}

	public static final Sensor DEFAULTS = new Sensor(Math.toRadians(90), 25, Math.toRadians(70),
		0.2, Math.toRadians(1), Math.toRadians(2));
	/** Judge's default tolerance: the bar a cell has to clear. */
	public static final double TOL_LAT_M = 0.15, TOL_HEADING_DEG = 2.0;
	static final double MIN_SIGMA = 1e-6;   // same clamp as Localizer.Weight, so sigma 0 cannot divide by zero

	/** Same three conditions as the Unity LandmarkSensor.IsVisibleGeometric. */
	static boolean visible(double x, double y, double psi, Landmark lm, Sensor s) {
		double dx = lm.x() - x, dy = lm.y() - y, r = Math.hypot(dx, dy);
		if (r > s.maxDistM() || r < 1e-6) return false;
		if (Math.abs(ShipFrame.wrapRad(Math.atan2(dy, dx) - psi)) > s.fovRad() / 2) return false;
		return Math.abs(ShipFrame.wrapRad(Math.atan2(-dy, -dx) - lm.phiRad())) <= s.maxViewAngleRad();
	}

	public static Estimate estimate(double x, double y, double psi, List<Landmark> lms, Sensor s) {
		double wr = weight(s.sigmaR()), wt = weight(s.sigmaTheta()), wa = weight(s.sigmaAlpha());
		double[][] A = new double[3][3];
		int n = 0;
		for (var lm : lms) {
			if (!visible(x, y, psi, lm, s)) continue;
			n++;
			double dx = lm.x() - x, dy = lm.y() - y, r = Math.hypot(dx, dy);
			accumulate(A, new double[] { -dx / r, -dy / r, 0 }, wr);
			accumulate(A, new double[] { dy / (r * r), -dx / (r * r), -1 }, wt);
			accumulate(A, new double[] { 0, 0, -1 }, wa);
		}
		if (n == 0) return new Estimate(0, null, null);
		double[][] c = invert3(A);
		if (c == null) return new Estimate(n, null, null);   // blind too: see stability() and analyze()
		double vx = Math.max(c[0][0], 0), vy = Math.max(c[1][1], 0), vp = Math.max(c[2][2], 0);
		return new Estimate(n, Math.sqrt(vx + vy), Math.toDegrees(Math.sqrt(vp)));
	}

	/** Margin against the parking tolerance; < 1 means this spot cannot meet it. null when blind. */
	public static Double stability(Estimate e) {
		if (e.sigmaXy() == null || e.sigmaPsiDeg() == null) return null;
		return Math.min(TOL_LAT_M / e.sigmaXy(), TOL_HEADING_DEG / e.sigmaPsiDeg());
	}

	static double weight(double sigma) { double v = Math.max(sigma, MIN_SIGMA); return 1 / (v * v); }

	static void accumulate(double[][] A, double[] j, double w) {
		for (int i = 0; i < 3; i++) for (int k = 0; k < 3; k++) A[i][k] += w * j[i] * j[k];
	}

	/**
	 * Cofactor inverse of a 3x3; null when singular. ponytail: direct inverse, fine for 3 unknowns.
	 * The singular branch is defensive and unreachable in practice - at the default sigmas w_theta is about 3283, so
	 * det A runs around 1e5 (measured minimum over the whole fixture deck: 1.095e+05).
	 */
	static double[][] invert3(double[][] a) {
		double det = a[0][0] * (a[1][1] * a[2][2] - a[1][2] * a[2][1])
			- a[0][1] * (a[1][0] * a[2][2] - a[1][2] * a[2][0])
			+ a[0][2] * (a[1][0] * a[2][1] - a[1][1] * a[2][0]);
		if (Math.abs(det) < 1e-12) return null;
		double[][] out = new double[3][3];
		for (int i = 0; i < 3; i++)
			for (int j = 0; j < 3; j++) {
				int r0 = i == 0 ? 1 : 0, r1 = i == 2 ? 1 : 2, c0 = j == 0 ? 1 : 0, c1 = j == 2 ? 1 : 2;
				double minor = a[r0][c0] * a[r1][c1] - a[r0][c1] * a[r1][c0];
				out[j][i] = ((i + j) % 2 == 0 ? minor : -minor) / det;   // transposed cofactor = adjugate
			}
		return out;
	}
}
```

- [ ] **Step 6: 테스트 통과 확인**

Run: `cd api && ./gradlew test --tests '*CoverageAnalyzerTests*' --tests '*Slot*'`
Expected: `CoverageAnalyzerTests` 7 개 PASS, 기존 `Slot*` 테스트도 그대로 PASS (`Rings` 위임이 깨지지 않았다는 확인)

- [ ] **Step 7: 커밋**

```bash
git add api/src/main/java/com/shiphdmap/api/geo/ api/src/main/java/com/shiphdmap/api/slots/SlotGenerator.java api/src/main/java/com/shiphdmap/api/coverage/ api/src/test/java/com/shiphdmap/api/coverage/
git commit -m "feat: expected localisation error from the landmark information matrix"
```

---

### Task 2: 격자·차폐·평가 대상

한 점의 σ 를 갑판 전체로 넓히고, 기둥 차폐를 넣고, **지표의 분모를 차가 실제로 갈 수 있는 셀로 좁힌다.** 회귀 테스트 두 건이 여기서 나온다.

**Files:**
- Modify: `api/src/main/java/com/shiphdmap/api/coverage/CoverageAnalyzer.java`
- Test: `api/src/test/java/com/shiphdmap/api/coverage/CoverageAnalyzerTests.java`

**Interfaces:**
- Consumes: Task 1 의 `Landmark`, `Sensor`, `Estimate`, `estimate`, `stability`, `Rings`
- Produces: `CoverageAnalyzer.Lane(double[][] centerline, double widthM)`
- Produces: `CoverageAnalyzer.Scope(List<double[][]> slots, List<Lane> lanes)` — `Scope.ALL` 은 비어 있고 **비면 전 셀이 in-scope**
- Produces: `CoverageAnalyzer.Cell(double x, double y, int n, Double sigmaXy, Double sigmaPsiDeg, Double stability, boolean inScope)`
- Produces: `CoverageAnalyzer.Result(List<Cell> cells, int nCells, int nDrawn, double blindRatio, double weakRatio, Cell worst)` — `nCells` 는 **in-scope 셀 수이자 두 비율의 분모**, `nDrawn` 은 `cells.size()`
- Produces: `CoverageAnalyzer.analyze(double[][] outline, List<double[][]> pillars, List<Landmark> lms, Scope scope, double psi, double gridM, Sensor s)`
- Produces: `CoverageAnalyzer.PSI_LOAD = 0.0`, `PSI_UNLOAD = Math.PI`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`CoverageAnalyzerTests` 에 이어 붙인다. 픽스처 모사는 실제 Deck 3 을 따른다 — 측면 마커 `y = ±6.2` 12 m 간격, 선수·선미 끝단 마커 모두 `φ = 180°`, 구획 4 열, 차로 1 개.

```java
	static double[][] rect(double x0, double y0, double x1, double y1) {
		return new double[][] { { x0, y0, 0 }, { x1, y0, 0 }, { x1, y1, 0 }, { x0, y1, 0 }, { x0, y0, 0 } };
	}

	/** The fixture's Deck 3 markers: 18 side markers facing the lane, 4 end markers all facing astern, plus LM-0019. */
	static List<CoverageAnalyzer.Landmark> fixtureLandmarks() {
		var out = new java.util.ArrayList<CoverageAnalyzer.Landmark>();
		int n = 0;
		for (double x = 12; x <= 108 + 1e-9; x += 12) {
			out.add(new CoverageAnalyzer.Landmark(String.format("LM-%04d", ++n), x, -6.2, Math.toRadians(90)));
			out.add(new CoverageAnalyzer.Landmark(String.format("LM-%04d", ++n), x, 6.2, Math.toRadians(-90)));
		}
		out.add(new CoverageAnalyzer.Landmark("LM-0019", 40, 11.9, Math.toRadians(-90)));
		for (double[] p : new double[][] { { 119.7, -3 }, { 119.7, 3 }, { 0.3, -5.5 }, { 0.3, 5.5 } })
			out.add(new CoverageAnalyzer.Landmark(String.format("LM-%04d", ++n + 1), p[0], p[1], Math.PI));
		return out;
	}

	/**
	 * Where a vehicle can be on the fixture's Deck 3: one lane down the middle and four rows of slots either side.
	 * The real deck has 136 slot polygons; four strips per side reproduce the |y| bands the rows occupy, which is
	 * what the regressions below actually assert on.
	 */
	static CoverageAnalyzer.Scope fixtureScope() {
		var slots = new java.util.ArrayList<double[][]>();
		for (double[] band : new double[][] { { 2.4, 4.4 }, { 4.6, 6.6 }, { 6.9, 8.9 }, { 9.1, 11.1 } }) {
			slots.add(rect(0, band[0], 120, band[1]));
			slots.add(rect(0, -band[1], 120, -band[0]));
		}
		var lane = new CoverageAnalyzer.Lane(new double[][] { { 2, 0, 0 }, { 60, 0, 0 }, { 118, 0, 0 } }, 3.2);
		return new CoverageAnalyzer.Scope(slots, List.of(lane));
	}

	static double blindIn(CoverageAnalyzer.Result r, double loY, double hiY) {
		var band = r.cells().stream().filter(c -> c.inScope() && Math.abs(c.y()) >= loY && Math.abs(c.y()) < hiY).toList();
		assertThat(band).isNotEmpty();
		return (double) band.stream().filter(c -> c.sigmaXy() == null).count() / band.size();
	}

	@Test
	void gridCoversTheDeckOutlineOnly() {
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), List.of(), fixtureLandmarks(),
			CoverageAnalyzer.Scope.ALL, CoverageAnalyzer.PSI_LOAD, 2.0, S);
		assertThat(r.nDrawn()).isEqualTo(r.cells().size()).isGreaterThan(500);
		assertThat(r.cells()).allSatisfy(c -> {
			assertThat(c.x()).isBetween(0.0, 120.0);
			assertThat(c.y()).isBetween(-12.0, 12.0);
		});
		assertThat(r.blindRatio()).isBetween(0.0, 1.0);
	}

	@Test
	void cellsInsidePillarsAreNotEvaluated() {
		var pillars = List.of(rect(58, -1, 62, 1));
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), pillars, fixtureLandmarks(),
			CoverageAnalyzer.Scope.ALL, CoverageAnalyzer.PSI_LOAD, 1.0, S);
		assertThat(r.cells()).noneMatch(c -> c.x() > 58 && c.x() < 62 && c.y() > -1 && c.y() < 1);
	}

	@Test
	void aPillarOnTheSightLineHidesTheMarker() {
		var lm = List.of(at("A", 10, 0));
		var wall = List.of(rect(4, -1, 6, 1));                        // straddles the line from (0,0) to (10,0)
		var open = CoverageAnalyzer.analyze(rect(-1, -2, 1, 2), List.of(), lm, CoverageAnalyzer.Scope.ALL, 0, 1.0, S);
		var blocked = CoverageAnalyzer.analyze(rect(-1, -2, 1, 2), wall, lm, CoverageAnalyzer.Scope.ALL, 0, 1.0, S);
		assertThat(open.cells()).anyMatch(c -> c.n() == 1);
		assertThat(blocked.cells()).allMatch(c -> c.n() == 0);
	}

	@Test
	void aMarkerIsNotHiddenByThePillarItIsMountedOn() {
		var lm = List.of(at("A", 10, 0));                              // marker at (10, 0)
		var ownPillar = List.of(rect(10, -0.3, 10.6, 0.3));            // the face it sits on
		var r = CoverageAnalyzer.analyze(rect(-1, -2, 1, 2), ownPillar, lm, CoverageAnalyzer.Scope.ALL, 0, 1.0, S);
		assertThat(r.cells()).anyMatch(c -> c.n() == 1);
	}

	/** An empty scope means "no slots or lanes mapped yet": count every cell rather than divide by zero. */
	@Test
	void emptyScopeCountsEveryCell() {
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), List.of(), fixtureLandmarks(),
			CoverageAnalyzer.Scope.ALL, CoverageAnalyzer.PSI_LOAD, 2.0, S);
		assertThat(r.nCells()).isEqualTo(r.nDrawn());
		assertThat(r.cells()).allMatch(CoverageAnalyzer.Cell::inScope);
	}

	/** The whole point of §3.4: the deck edges are drawn but must not be in the denominator. */
	@Test
	void scopeNarrowsTheDenominatorWithoutShrinkingTheHeatmap() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var all = CoverageAnalyzer.analyze(deck, List.of(), lms, CoverageAnalyzer.Scope.ALL, CoverageAnalyzer.PSI_LOAD, 2.0, S);
		var scoped = CoverageAnalyzer.analyze(deck, List.of(), lms, fixtureScope(), CoverageAnalyzer.PSI_LOAD, 2.0, S);
		assertThat(scoped.nDrawn()).isEqualTo(all.nDrawn());              // the heatmap is unchanged
		assertThat(scoped.nCells()).isLessThan(all.nCells());             // the denominator is not
		assertThat(scoped.cells()).anyMatch(c -> !c.inScope());
		assertThat(scoped.blindRatio()).isLessThan(all.blindRatio());     // the edges were inflating it
	}

	@Test
	void worstIsTheWeakestNonBlindInScopeCell() {
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), List.of(), fixtureLandmarks(),
			fixtureScope(), CoverageAnalyzer.PSI_LOAD, 2.0, S);
		assertThat(r.worst()).isNotNull();
		assertThat(r.worst().inScope()).isTrue();
		assertThat(r.worst().stability()).isNotNull();
		double min = r.cells().stream().filter(c -> c.inScope() && c.stability() != null)
			.mapToDouble(CoverageAnalyzer.Cell::stability).min().orElseThrow();
		assertThat(r.worst().stability()).isCloseTo(min, within(1e-9));
	}

	/**
	 * Regression 1 (spec §7.2-1): the outer slot rows are blind even when loading, because every side marker sits on
	 * the single line y = +/-6.2 and the outer rows fall outside the 90 deg FOV. Flip this when markers are added.
	 */
	@Test
	void theOuterSlotRowIsBlindEvenWhenLoading() {
		var r = CoverageAnalyzer.analyze(rect(0, -12, 120, 12), List.of(), fixtureLandmarks(),
			fixtureScope(), CoverageAnalyzer.PSI_LOAD, 1.0, S);
		assertThat(blindIn(r, 9.0, 12.0)).as("outer row, loading").isGreaterThan(0.5);
		assertThat(blindIn(r, 0.0, 2.0)).as("the lane is fine").isLessThan(0.05);
	}

	/**
	 * Regression 2 (spec §7.2-2): driving astern there is nothing ahead near the stern - the two stern end markers
	 * face -x and are invisible from anywhere on the deck (spec §3.5).
	 */
	@Test
	void unloadIsBlindNearTheStern() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var load = CoverageAnalyzer.analyze(deck, List.of(), lms, fixtureScope(), CoverageAnalyzer.PSI_LOAD, 1.0, S);
		var unload = CoverageAnalyzer.analyze(deck, List.of(), lms, fixtureScope(), CoverageAnalyzer.PSI_UNLOAD, 1.0, S);
		java.util.function.ToLongFunction<CoverageAnalyzer.Result> sternBlind = r -> r.cells().stream()
			.filter(c -> c.inScope() && c.x() < 20 && c.sigmaXy() == null).count();
		assertThat(sternBlind.applyAsLong(unload)).as("unload is blind astern").isGreaterThan(sternBlind.applyAsLong(load));
		assertThat(unload.blindRatio()).isGreaterThan(load.blindRatio());
	}

	/** The stern end markers are dead weight in both modes: their normal faces off the deck. */
	@Test
	void theSternEndMarkersAreVisibleFromNowhere() {
		var stern = new CoverageAnalyzer.Landmark("LM-0022", 0.3, -5.5, Math.PI);
		var bow = new CoverageAnalyzer.Landmark("LM-0020", 119.7, -3, Math.PI);
		var deck = rect(0, -12, 120, 12);
		for (double psi : new double[] { CoverageAnalyzer.PSI_LOAD, CoverageAnalyzer.PSI_UNLOAD })
			assertThat(CoverageAnalyzer.analyze(deck, List.of(), List.of(stern), CoverageAnalyzer.Scope.ALL, psi, 1.0, S)
				.cells()).allMatch(c -> c.n() == 0);
		assertThat(CoverageAnalyzer.analyze(deck, List.of(), List.of(bow), CoverageAnalyzer.Scope.ALL,
			CoverageAnalyzer.PSI_LOAD, 1.0, S).cells()).anyMatch(c -> c.n() == 1);
	}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd api && ./gradlew test --tests '*CoverageAnalyzerTests*'`
Expected: 컴파일 실패 — `Lane`, `Scope`, `Cell`, `Result`, `analyze`, `PSI_LOAD`, `PSI_UNLOAD` 가 없다

- [ ] **Step 3: 격자·차폐·평가 대상을 더한다**

`CoverageAnalyzer` 에 이어 붙인다:

```java
	/** A drivable lane: centreline plus width. The corridor is everything within width/2 of it. */
	public record Lane(double[][] centerline, double widthM) {}

	/**
	 * Where a vehicle can actually be (spec §3.4): inside a slot polygon, or inside a lane corridor.
	 * An empty scope means "nothing mapped": every cell counts, so the unit tests can use a bare rectangle.
	 */
	public record Scope(List<double[][]> slots, List<Lane> lanes) {
		public static final Scope ALL = new Scope(List.of(), List.of());
		boolean isEmpty() { return slots.isEmpty() && lanes.isEmpty(); }
		boolean has(double x, double y) {
			if (isEmpty()) return true;
			for (var l : lanes) if (Rings.distToPolyline(l.centerline(), x, y) <= l.widthM() / 2) return true;
			for (var s : slots) if (Rings.contains(s, x, y)) return true;
			return false;
		}
	}

	/** One evaluated grid point. sigma/stability are null when blind. inScope false means "drawn but not counted". */
	public record Cell(double x, double y, int n, Double sigmaXy, Double sigmaPsiDeg, Double stability, boolean inScope) {}
	/** nCells is the in-scope count AND the denominator of both ratios; nDrawn is cells.size(). They differ. */
	public record Result(List<Cell> cells, int nCells, int nDrawn, double blindRatio, double weakRatio, Cell worst) {}

	public static final double PSI_LOAD = 0.0, PSI_UNLOAD = Math.PI;
	static final double MOUNT_CLEARANCE_M = 0.05;   // shorten the sight line at the marker end so its own pillar never blocks it

	public static Result analyze(double[][] outline, List<double[][]> pillars, List<Landmark> lms, Scope scope,
			double psi, double gridM, Sensor s) {
		double[] b = Rings.bbox(outline);
		var cells = new java.util.ArrayList<Cell>();
		int inScope = 0, blind = 0, weak = 0;
		Cell worst = null;
		for (double y = b[1] + gridM / 2; y <= b[3]; y += gridM)
			for (double x = b[0] + gridM / 2; x <= b[2]; x += gridM) {
				if (!Rings.contains(outline, x, y)) continue;
				if (insideAny(pillars, x, y)) continue;
				boolean in = scope.has(x, y);
				var e = estimate(x, y, psi, visibleFrom(x, y, psi, lms, pillars, s), s);
				Double st = stability(e);
				var c = new Cell(x, y, e.n(), e.sigmaXy(), e.sigmaPsiDeg(), st, in);
				cells.add(c);
				if (!in) continue;
				inScope++;
				if (st == null) { blind++; continue; }   // n = 0 or singular: both are "cannot localise here"
				if (st < 1.0) weak++;
				if (worst == null || st < worst.stability()) worst = c;
			}
		return new Result(cells, inScope, cells.size(),
			inScope == 0 ? 0 : (double) blind / inScope, inScope == 0 ? 0 : (double) weak / inScope, worst);
	}

	/** The landmarks actually usable from (x, y): visible by geometry and not hidden by a pillar. */
	static List<Landmark> visibleFrom(double x, double y, double psi, List<Landmark> lms, List<double[][]> pillars, Sensor s) {
		var out = new java.util.ArrayList<Landmark>();
		for (var lm : lms) if (visible(x, y, psi, lm, s) && !occluded(x, y, lm, pillars)) out.add(lm);
		return out;
	}

	/** 2D segment test: pillars run floor to ceiling, so the plan view is exact rather than an approximation. */
	static boolean occluded(double x, double y, Landmark lm, List<double[][]> pillars) {
		double dx = lm.x() - x, dy = lm.y() - y, len = Math.hypot(dx, dy);
		if (len < 1e-9) return false;
		double tx = lm.x() - dx / len * MOUNT_CLEARANCE_M, ty = lm.y() - dy / len * MOUNT_CLEARANCE_M;
		for (var p : pillars)
			for (int i = 0; i + 1 < p.length; i++)
				if (segmentsCross(x, y, tx, ty, p[i][0], p[i][1], p[i + 1][0], p[i + 1][1])) return true;
		return false;
	}

	static boolean segmentsCross(double ax, double ay, double bx, double by, double cx, double cy, double dx, double dy) {
		double d1 = cross(cx, cy, dx, dy, ax, ay), d2 = cross(cx, cy, dx, dy, bx, by);
		double d3 = cross(ax, ay, bx, by, cx, cy), d4 = cross(ax, ay, bx, by, dx, dy);
		return ((d1 > 0) != (d2 > 0)) && ((d3 > 0) != (d4 > 0));   // proper crossings only; touching a corner does not occlude
	}

	static double cross(double ax, double ay, double bx, double by, double px, double py) {
		return (bx - ax) * (py - ay) - (by - ay) * (px - ax);
	}

	static boolean insideAny(List<double[][]> rings, double x, double y) {
		for (var r : rings) if (Rings.contains(r, x, y)) return true;
		return false;
	}
```

`import com.shiphdmap.api.geo.Rings;` 를 더한다.

- [ ] **Step 4: 테스트 통과 확인**

Run: `cd api && ./gradlew test --tests '*CoverageAnalyzerTests*'`
Expected: PASS 16 개

`theOuterSlotRowIsBlindEvenWhenLoading` 또는 `unloadIsBlindNearTheStern` 이 실패하면 픽스처 모사가 실제와 다른 것이다. 실제 값과 대조한다 (DB 가 떠 있어야 한다):

```bash
docker exec db-postgis-1 psql -U shiphdmap -d shiphdmap -c \
  "SELECT round(abs(ST_Y(geom))::numeric,1) y, count(*) FROM feature WHERE deck_id='D3' AND layer='LM' GROUP BY 1 ORDER BY 1;
   SELECT round(min(abs(ST_Y((dp).geom)))::numeric,2), round(max(abs(ST_Y((dp).geom)))::numeric,2)
   FROM feature f, ST_DumpPoints(f.geom) dp WHERE f.deck_id='D3' AND f.layer='B2';"
```

기준값 (`grid_m` 1.0, 마커 23 · 기둥 18 · 구획 136 · 차로 1): 선적 바깥 열 blind 0.682, 차로 blind 0.017, 하역 선미 in-scope blind 셀 304 개.

- [ ] **Step 5: 커밋**

```bash
git add api/src/main/java/com/shiphdmap/api/coverage/CoverageAnalyzer.java api/src/test/java/com/shiphdmap/api/coverage/CoverageAnalyzerTests.java
git commit -m "feat: grid coverage scoped to slots and lanes, with pillar occlusion"
```

---

### Task 3: 후보 면과 그리디 추천

**Files:**
- Modify: `api/src/main/java/com/shiphdmap/api/coverage/CoverageAnalyzer.java`
- Test: `api/src/test/java/com/shiphdmap/api/coverage/CoverageAnalyzerTests.java`

**Interfaces:**
- Consumes: Task 2 의 `Scope`, `Cell`, `Result`, `analyze`
- Produces: `CoverageAnalyzer.Candidate(double x, double y, double phiRad, String mountedOn)`
- Produces: `CoverageAnalyzer.Suggestion(int rank, double x, double y, double phiDeg, String mountedOn, double blindAfter, double weakAfter, double gain)` — `gain` 은 그 순위에서 줄어든 `blindRatio`
- Produces: `CoverageAnalyzer.candidates(double[][] outline, List<String> pillarIds, List<double[][]> pillars, List<Landmark> existing)` — 긴 변은 `FACE_SPACING_M` 12 m 마다 분할, 배제는 **거리 AND 법선 방향**
- Produces: `CoverageAnalyzer.suggest(double[][] outline, List<double[][]> pillars, List<Landmark> lms, Scope scope, List<Candidate> cands, double psi, Sensor s, int budget)` — **`gridM` 을 받지 않는다.** `SUGGEST_GRID_M` 2.0 고정
- Produces: `CoverageAnalyzer.MAX_BUDGET = 10`, `SUGGEST_GRID_M = 2.0`, `FACE_SPACING_M = 12.0`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

```java
	@Test
	void candidatesSitOnFacesAndFaceTheVehicleSide() {
		var deck = rect(0, -12, 120, 12);
		var pillar = rect(59.7, -0.3, 60.3, 0.3);
		var cs = CoverageAnalyzer.candidates(deck, List.of("Pillar-1"), List.of(pillar), List.of());
		// 4 pillar faces (0.6 m, one point each) + the deck outline split every 12 m: 2 x 10 long, 2 x 2 short
		assertThat(cs).hasSize(4 + 24);
		var stern = cs.stream().filter(c -> c.mountedOn().equals("Pillar-1") && c.x() < 59.8).findFirst().orElseThrow();
		assertThat(Math.toDegrees(stern.phiRad())).isCloseTo(180, within(1e-6));   // the aft face looks aft, away from the pillar
		var bulkhead = cs.stream().filter(c -> c.mountedOn().equals("deck") && c.x() < 0.1).findFirst().orElseThrow();
		assertThat(Math.toDegrees(bulkhead.phiRad())).isCloseTo(0, within(1e-6));  // the stern bulkhead looks forward, into the deck
	}

	/** Only the face the marker already occupies is taken: same spot AND same direction. */
	@Test
	void candidatesSkipTheFaceAnExistingMarkerAlreadyCovers() {
		var deck = rect(0, -12, 120, 12);
		var pillar = rect(59.7, -0.3, 60.3, 0.3);
		var occupied = List.of(new CoverageAnalyzer.Landmark("LM-1", 59.7, 0, Math.PI));   // on the aft face, facing aft
		var cs = CoverageAnalyzer.candidates(deck, List.of("Pillar-1"), List.of(pillar), occupied);
		var onPillar = cs.stream().filter(c -> c.mountedOn().equals("Pillar-1")).toList();
		assertThat(onPillar).hasSize(3);                                                   // the aft face is gone
		assertThat(onPillar).noneMatch(c -> Math.abs(ShipFrame.wrapRad(c.phiRad() - Math.PI)) < 1e-6);
	}

	/**
	 * The defect that makes §1.4 reachable. Every fixture pillar already carries a marker 0.12-0.67 m away, so a
	 * distance-only rule would drop all 72 pillar faces and leave nothing that can light up the outer slot rows.
	 */
	@Test
	void aMarkerOnOneFaceDoesNotBlockTheOppositeFace() {
		var deck = rect(0, -12, 120, 12);
		var pillar = rect(11.7, -6.8, 12.3, -6.2);                          // the fixture's pillar P3, exactly
		// LM-0001 sits ON the inner face midpoint: distance 0, so only a direction test can tell the faces apart
		var lm = List.of(new CoverageAnalyzer.Landmark("LM-0001", 12.0, -6.2, Math.toRadians(90)));
		var onPillar = CoverageAnalyzer.candidates(deck, List.of("P"), List.of(pillar), lm).stream()
			.filter(c -> c.mountedOn().equals("P")).toList();
		assertThat(onPillar).hasSize(3);
		var outward = onPillar.stream().filter(c -> Math.abs(Math.toDegrees(c.phiRad()) + 90) < 1e-6).findFirst();
		assertThat(outward).as("the face pointing away from the lane survives").isPresent();
		// and it is what reaches the outer row: y = -10 is blind without it, lit with it
		var scope = new CoverageAnalyzer.Scope(List.of(rect(0, -11.1, 120, -9.1)), List.of());
		var before = CoverageAnalyzer.analyze(deck, List.of(pillar), lm, scope, CoverageAnalyzer.PSI_LOAD, 1.0, S);
		var after = CoverageAnalyzer.analyze(deck, List.of(pillar), 
			java.util.stream.Stream.concat(lm.stream(), java.util.stream.Stream.of(
				new CoverageAnalyzer.Landmark("NEW", outward.get().x(), outward.get().y(), outward.get().phiRad()))).toList(),
			scope, CoverageAnalyzer.PSI_LOAD, 1.0, S);
		assertThat(after.blindRatio()).isLessThan(before.blindRatio());
	}

	@Test
	void suggestFillsABlindSpotFirst() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var cands = CoverageAnalyzer.candidates(deck, List.of(), List.of(), lms);
		var out = CoverageAnalyzer.suggest(deck, List.of(), lms, fixtureScope(), cands, CoverageAnalyzer.PSI_UNLOAD, S, 2);
		assertThat(out).hasSize(2);
		assertThat(out.get(0).rank()).isEqualTo(1);
		assertThat(out.get(0).gain()).as("rank 1 removes blind cells").isPositive();
	}

	@Test
	void suggestionsNeverGetWorse() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var cands = CoverageAnalyzer.candidates(deck, List.of(), List.of(), lms);
		var out = CoverageAnalyzer.suggest(deck, List.of(), lms, fixtureScope(), cands, CoverageAnalyzer.PSI_UNLOAD, S, 3);
		for (int i = 1; i < out.size(); i++)
			assertThat(out.get(i).blindAfter()).isLessThanOrEqualTo(out.get(i - 1).blindAfter());
	}

	@Test
	void budgetIsClampedAndCandidatesAreNotReused() {
		var deck = rect(0, -12, 120, 12);
		var lms = fixtureLandmarks();
		var cands = CoverageAnalyzer.candidates(deck, List.of(), List.of(), lms);
		var out = CoverageAnalyzer.suggest(deck, List.of(), lms, fixtureScope(), cands, CoverageAnalyzer.PSI_UNLOAD, S, 99);
		assertThat(out.size()).isLessThanOrEqualTo(CoverageAnalyzer.MAX_BUDGET);
		assertThat(out.stream().map(s -> s.x() + "," + s.y()).distinct()).hasSize(out.size());
	}

	/**
	 * The reason §3.4 exists. A face that only lights up the empty deck edge must lose to one that lights up a slot,
	 * even though the edge has far more cells. Without the scope the greedy picks the edge.
	 */
	@Test
	void suggestIgnoresCellsNobodyDrivesThrough() {
		var deck = rect(0, -30, 40, 30);                                  // a wide deck: most of it is empty
		var lane = new CoverageAnalyzer.Lane(new double[][] { { 0, 0, 0 }, { 40, 0, 0 } }, 3.2);
		var scope = new CoverageAnalyzer.Scope(List.of(), List.of(lane));
		var edgeFace = new CoverageAnalyzer.Candidate(20, 30, Math.toRadians(-90), "edge");   // lights up the far edge
		var laneFace = new CoverageAnalyzer.Candidate(20, 2, Math.toRadians(-90), "lane");    // lights up the lane
		var out = CoverageAnalyzer.suggest(deck, List.of(), List.of(), scope,
			List.of(edgeFace, laneFace), CoverageAnalyzer.PSI_LOAD, S, 1);
		assertThat(out).hasSize(1);
		assertThat(out.get(0).mountedOn()).isEqualTo("lane");
	}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd api && ./gradlew test --tests '*CoverageAnalyzerTests*'`
Expected: 컴파일 실패 — `Candidate`, `Suggestion`, `candidates`, `suggest`, `MAX_BUDGET` 이 없다

- [ ] **Step 3: 후보와 그리디를 더한다**

```java
	/** A face a marker could be mounted on: midpoint plus the outward normal (the side the vehicle is on). */
	public record Candidate(double x, double y, double phiRad, String mountedOn) {}
	/** One greedy pick. Ratios are the state AFTER adopting this and every earlier suggestion; gain is the blind drop. */
	public record Suggestion(int rank, double x, double y, double phiDeg, String mountedOn,
		double blindAfter, double weakAfter, double gain) {}

	public static final int MAX_BUDGET = 10;
	/** Split long edges this often, so a 120 m bulkhead offers candidates at the same density as the pillars. */
	static final double FACE_SPACING_M = 12.0;
	/** Suggestions land on structure faces, which are metres apart; a finer grid buys nothing. Spec §4.2. */
	public static final double SUGGEST_GRID_M = 2.0;
	static final double CANDIDATE_CLEARANCE_M = 2.0;     // "the same spot" for the purpose of the rule below
	static final double FACE_ALIGN_RAD = Math.PI / 4;    // ...and "the same direction". Both must hold to exclude a face.
	static final double NORMAL_PROBE_M = 0.1;          // step off the face to decide which way is "outward"
	static final double BLIND_PENALTY = 10;            // one blind cell is worse than ten weak ones: different kinds of bad

	public static List<Candidate> candidates(double[][] outline, List<String> pillarIds, List<double[][]> pillars, List<Landmark> existing) {
		var out = new java.util.ArrayList<Candidate>();
		for (int p = 0; p < pillars.size(); p++) {
			String id = p < pillarIds.size() ? pillarIds.get(p) : "pillar-" + p;
			addFaces(out, pillars.get(p), id, true, existing);
		}
		addFaces(out, outline, "deck", false, existing);
		return out;
	}

	/** outward = true for pillars (normal points away from the ring), false for the deck outline (normal points inward). */
	static void addFaces(List<Candidate> out, double[][] ring, String mountedOn, boolean outward, List<Landmark> existing) {
		for (int i = 0; i + 1 < ring.length; i++) {
			double ax = ring[i][0], ay = ring[i][1];
			double ex = ring[i + 1][0] - ax, ey = ring[i + 1][1] - ay, len = Math.hypot(ex, ey);
			if (len < 1e-9) continue;
			double nx = -ey / len, ny = ex / len;                       // one of the two perpendiculars
			boolean probeInside = Rings.contains(ring, ax + ex / 2 + nx * NORMAL_PROBE_M, ay + ey / 2 + ny * NORMAL_PROBE_M);
			if (probeInside == outward) { nx = -nx; ny = -ny; }          // flip when it points the wrong way
			double phi = Math.atan2(ny, nx);
			int parts = Math.max(1, (int) Math.round(len / FACE_SPACING_M));   // a pillar face stays one point
			for (int k = 0; k < parts; k++) {
				double t = (k + 0.5) / parts, mx = ax + ex * t, my = ay + ey * t;
				if (alreadyCovered(mx, my, phi, existing)) continue;
				out.add(new Candidate(mx, my, phi, mountedOn));
			}
		}
	}

	/**
	 * A face is taken only when a marker is both close AND pointing the same way. Distance alone is wrong: the fixture
	 * pillars are 0.6 m across with a marker already on one face, so a 2 m radius would swallow all four faces of all
	 * 18 pillars and leave nothing aimed at the outer slot rows (spec §5.1.1).
	 */
	static boolean alreadyCovered(double x, double y, double phi, List<Landmark> existing) {
		for (var lm : existing)
			if (Math.hypot(lm.x() - x, lm.y() - y) < CANDIDATE_CLEARANCE_M
				&& Math.abs(ShipFrame.wrapRad(lm.phiRad() - phi)) < FACE_ALIGN_RAD) return true;
		return false;
	}

	/** Blind/weak counts over the in-scope cells only. The suggest path needs no heatmap, so out-of-scope cells are skipped entirely. */
	record Tally(int nCells, int blind, int weak) {
		double blindRatio() { return nCells == 0 ? 0 : (double) blind / nCells; }
		double weakRatio() { return nCells == 0 ? 0 : (double) weak / nCells; }
		double score() { return blind * BLIND_PENALTY + weak; }
	}

	static Tally tally(double[][] outline, List<double[][]> pillars, List<Landmark> lms, Scope scope,
			double psi, double gridM, Sensor s) {
		double[] b = Rings.bbox(outline);
		int n = 0, blind = 0, weak = 0;
		for (double y = b[1] + gridM / 2; y <= b[3]; y += gridM)
			for (double x = b[0] + gridM / 2; x <= b[2]; x += gridM) {
				if (!scope.has(x, y)) continue;                          // skipped before any geometry work
				if (!Rings.contains(outline, x, y) || insideAny(pillars, x, y)) continue;
				n++;
				Double st = stability(estimate(x, y, psi, visibleFrom(x, y, psi, lms, pillars, s), s));
				if (st == null) blind++;
				else if (st < 1.0) weak++;
			}
		return new Tally(n, blind, weak);
	}

	/**
	 * Greedy: adopt the candidate that lowers the score most, repeat. Blind cells outweigh weak ones.
	 * ponytail: every candidate re-walks the grid. Information only accumulates, so an ok cell can never turn bad and
	 * in principle only the currently blind/weak cells need revisiting; caching each cell's baseline A matrix would
	 * drop the marker factor too. The 2 m grid already makes this a few hundred ms, so neither is worth it yet.
	 */
	public static List<Suggestion> suggest(double[][] outline, List<double[][]> pillars, List<Landmark> lms,
			Scope scope, List<Candidate> cands, double psi, Sensor s, int budget) {
		int n = Math.min(Math.max(budget, 0), MAX_BUDGET);
		var chosen = new java.util.ArrayList<>(lms);
		var pool = new java.util.ArrayList<>(cands);
		var out = new java.util.ArrayList<Suggestion>();
		var cur = tally(outline, pillars, chosen, scope, psi, SUGGEST_GRID_M, s);
		for (int rank = 1; rank <= n && !pool.isEmpty(); rank++) {
			Candidate best = null;
			Tally bestT = null;
			for (var c : pool) {
				var trial = new java.util.ArrayList<>(chosen);
				trial.add(new Landmark("CAND", c.x(), c.y(), c.phiRad()));
				var t = tally(outline, pillars, trial, scope, psi, SUGGEST_GRID_M, s);
				if (t.score() < (bestT == null ? cur.score() : bestT.score())) { bestT = t; best = c; }
			}
			if (best == null) break;                                     // nothing improves anything
			out.add(new Suggestion(rank, best.x(), best.y(), Math.toDegrees(best.phiRad()), best.mountedOn(),
				bestT.blindRatio(), bestT.weakRatio(), cur.blindRatio() - bestT.blindRatio()));
			chosen.add(new Landmark("CAND-" + rank, best.x(), best.y(), best.phiRad()));
			pool.remove(best);
			cur = bestT;
		}
		return out;
	}
```

- [ ] **Step 4: 테스트 통과 확인**

Run: `cd api && ./gradlew test --tests '*CoverageAnalyzerTests*'`
Expected: PASS 24 개

- [ ] **Step 5: 커밋**

```bash
git add api/src/main/java/com/shiphdmap/api/coverage/CoverageAnalyzer.java api/src/test/java/com/shiphdmap/api/coverage/CoverageAnalyzerTests.java
git commit -m "feat: greedy marker-placement suggestions over structure faces"
```

---

### Task 4: 엔드포인트

**Files:**
- Modify: `api/src/main/java/com/shiphdmap/api/JsonMaps.java`
- Modify: `api/src/main/java/com/shiphdmap/api/export/VehicleMapAssembler.java`
- Create: `api/src/main/java/com/shiphdmap/api/coverage/CoverageController.java`
- Test: `api/src/test/java/com/shiphdmap/api/coverage/CoverageApiTests.java`
- Modify: `docs/api-contract.md`

**Interfaces:**
- Produces: `JsonMaps.doubles(Object)` — `VehicleMapAssembler.arr` 를 옮긴 것
- Produces: `POST /api/datasets/{ds}/decks/{deck}/coverage`, `POST /api/datasets/{ds}/decks/{deck}/coverage/suggest`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

기존 통합 테스트(`SlotGenerateTests`)와 같은 뼈대를 쓴다. 실측값을 기준으로 잡는다 — 이 테스트가 §1.4 완료 기준을 지킨다.

`api/src/test/java/com/shiphdmap/api/coverage/CoverageApiTests.java`:

```java
package com.shiphdmap.api.coverage;

import static org.assertj.core.api.Assertions.*;

import com.shiphdmap.api.TestcontainersConfiguration;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.boot.test.web.client.TestRestTemplate;
import org.springframework.context.annotation.Import;

@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT)
@Import(TestcontainersConfiguration.class)
@SuppressWarnings("unchecked")
class CoverageApiTests {
	@Autowired TestRestTemplate rest;

	Map<String, Object> post(String path, Object body) { return rest.postForObject(path, body, Map.class); }
	Map<String, Object> d3(Object body) { return post("/api/datasets/roro-demo-01/decks/D3/coverage", body); }
	static double num(Map<String, Object> m, String k) { return ((Number) m.get(k)).doubleValue(); }

	@Test
	void coverageReturnsCellsAndBothModeSummaries() {
		var out = d3(Map.of("mode", "load", "grid_m", 2.0));
		assertThat(out).containsKeys("deck", "mode", "grid_m", "n_cells", "n_drawn", "blind_ratio", "weak_ratio", "cells", "other_mode");
		assertThat(out.get("mode")).isEqualTo("load");
		assertThat((List<?>) out.get("cells")).isNotEmpty();
		var other = (Map<String, Object>) out.get("other_mode");
		assertThat(other.get("mode")).isEqualTo("unload");
		assertThat(other).doesNotContainKey("cells");
	}

	/**
	 * The denominator is the drivable/parkable area, not the deck: Deck 3 is about 1836 of 2880 cells at grid 1.0.
	 * Asserted as a relationship plus a loose band - the exact counts move if a pillar swallows a grid centre or the
	 * slot generator is re-parameterised, and the precise figures belong in theFixtureBlindSpotsShowUp.
	 */
	@Test
	void theDenominatorIsTheInScopeCellsOnly() {
		var out = d3(Map.of("mode", "load", "grid_m", 1.0));
		int inScope = (int) num(out, "n_cells"), drawn = (int) num(out, "n_drawn");
		assertThat(inScope).isLessThan(drawn).isCloseTo(1836, within(50));
		assertThat(drawn).isCloseTo(2880, within(50));
		var cells = (List<Map<String, Object>>) out.get("cells");
		assertThat(cells).hasSize(drawn);
		// in_scope is serialised only when false, so it appears on exactly the excluded cells and nowhere else
		assertThat(cells.stream().filter(c -> c.containsKey("in_scope")).count()).isEqualTo(drawn - inScope);
		assertThat(cells.stream().filter(c -> Boolean.FALSE.equals(c.get("in_scope"))).count()).isEqualTo(drawn - inScope);
	}

	@Test
	void blindCellsOmitSigma() {
		var out = d3(Map.of("mode", "unload", "grid_m", 2.0));
		var cells = (List<Map<String, Object>>) out.get("cells");
		var blind = cells.stream().filter(c -> ((Number) c.get("n")).intValue() == 0).findFirst();
		assertThat(blind).isPresent();
		assertThat(blind.get()).doesNotContainKeys("sigma_xy", "sigma_psi", "stability");
	}

	/** Spec §1.4: the tool has to surface the outer-row blind spot and the unload stern blind spot. */
	@Test
	void theFixtureBlindSpotsShowUp() {
		var load = d3(Map.of("mode", "load", "grid_m", 1.0));
		var unload = d3(Map.of("mode", "unload", "grid_m", 1.0));
		assertThat(num(load, "blind_ratio")).isCloseTo(0.203, within(0.01));
		assertThat(num(unload, "blind_ratio")).isCloseTo(0.352, within(0.01));

		var cells = (List<Map<String, Object>>) load.get("cells");
		java.util.function.BiFunction<Double, Double, Double> blindInBand = (lo, hi) -> {
			var band = cells.stream().filter(c -> !Boolean.FALSE.equals(c.get("in_scope")))
				.filter(c -> { double y = Math.abs(num(c, "y")); return y >= lo && y < hi; }).toList();
			return (double) band.stream().filter(c -> ((Number) c.get("n")).intValue() == 0).count() / band.size();
		};
		assertThat(blindInBand.apply(9.0, 12.0)).as("outer slot row, loading").isGreaterThan(0.5);
		assertThat(blindInBand.apply(0.0, 2.0)).as("the lane, loading").isLessThan(0.05);
	}

	@Test
	void extraLandmarksImproveCoverageWithoutTouchingTheDatabase() {
		var before = d3(Map.of("mode", "unload", "grid_m", 2.0));
		var with = d3(Map.of("mode", "unload", "grid_m", 2.0, "extra_landmarks",
			List.of(Map.of("x", 10.0, "y", -6.2, "phi_deg", 0.0), Map.of("x", 10.0, "y", 6.2, "phi_deg", 0.0))));
		assertThat(num(with, "blind_ratio")).isLessThan(num(before, "blind_ratio"));
		var again = d3(Map.of("mode", "unload", "grid_m", 2.0));
		assertThat(again.get("blind_ratio")).isEqualTo(before.get("blind_ratio"));   // nothing was persisted
	}

	/**
	 * Removing markers must be measured on blind_ratio, not weak_ratio: a weak cell that goes blind LEAVES the weak
	 * numerator, so weak_ratio falls. On this fixture omitting LM-0020/0021 takes load weak from 0.6875 to 0.5375.
	 */
	@Test
	void omitRaisesTheBlindRatio() {
		var full = d3(Map.of("mode", "load", "grid_m", 2.0));
		var less = d3(Map.of("mode", "load", "grid_m", 2.0, "omit", List.of("LM-0020", "LM-0021")));
		assertThat(num(less, "blind_ratio")).isGreaterThan(num(full, "blind_ratio"));
	}

	@Test
	void suggestReturnsRankedPicks() {
		var out = post("/api/datasets/roro-demo-01/decks/D3/coverage/suggest",
			Map.of("mode", "unload", "grid_m", 1.0, "budget", 2));
		assertThat(num(out, "grid_m")).as("suggest pins the grid at 2 m and says so").isEqualTo(2.0);
		var picks = (List<Map<String, Object>>) out.get("suggestions");
		assertThat(picks).hasSize(2);
		assertThat(picks.get(0)).containsKeys("rank", "x", "y", "phi_deg", "mounted_on", "blind_after", "weak_after", "gain");
		assertThat(num((Map<String, Object>) out.get("after"), "blind_ratio"))
			.isLessThanOrEqualTo(num((Map<String, Object>) out.get("before"), "blind_ratio"));
	}

	@Test
	void unknownDeckIs404AndBadGridIs400() {
		assertThat(rest.postForEntity("/api/datasets/roro-demo-01/decks/NOPE/coverage", Map.of(), Map.class).getStatusCode().value()).isEqualTo(404);
		assertThat(rest.postForEntity("/api/datasets/roro-demo-01/decks/D3/coverage", Map.of("grid_m", 0), Map.class).getStatusCode().value()).isEqualTo(400);
	}
}
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd api && ./gradlew test --tests '*CoverageApiTests*'`
Expected: FAIL — 엔드포인트가 없어 404

- [ ] **Step 3: `props` 배열 헬퍼를 공유 위치로 올린다**

`VehicleMapAssembler.arr` 를 `JsonMaps` 로 옮긴다. `api/src/main/java/com/shiphdmap/api/JsonMaps.java` 에:

```java
	/** A props JSON array such as "normal": [nx, ny, nz] as a double[]; null when the key is absent. */
	@SuppressWarnings("unchecked")
	public static double[] doubles(Object o) {
		if (o == null) return null;
		List<Number> l = (List<Number>) o;
		double[] a = new double[l.size()];
		for (int i = 0; i < a.length; i++) a[i] = l.get(i).doubleValue();
		return a;
	}
```

`VehicleMapAssembler` 의 `arr` 는 위임만 남긴다:

```java
	static double[] arr(Object o) { return JsonMaps.doubles(o); }
```

- [ ] **Step 4: 컨트롤러를 만든다**

`api/src/main/java/com/shiphdmap/api/coverage/CoverageController.java`:

```java
package com.shiphdmap.api.coverage;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.shiphdmap.api.ApiErrors;
import com.shiphdmap.api.JsonMaps;
import com.shiphdmap.api.dataset.Datasets;
import com.shiphdmap.api.geo.Rings;
import com.shiphdmap.api.geo.Wkt;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.springframework.jdbc.core.simple.JdbcClient;
import org.springframework.web.bind.annotation.*;

/** Spec M5c §4: landmark coverage for one deck. Read-only - extra_landmarks and omit never touch the database. */
@RestController
@RequestMapping("/api/datasets/{ds}/decks/{deck}/coverage")
public class CoverageController {
	private final JdbcClient db;
	private final ObjectMapper json;
	public CoverageController(JdbcClient db, ObjectMapper json) { this.db = db; this.json = json; }

	public record ExtraLandmark(Double x, Double y, Double phiDeg) {}
	public record CoverageIn(String mode, Double gridM, Double fovDeg, Double maxDistM, Double maxViewAngleDeg,
		Double sigmaR, Double sigmaTheta, Double sigmaAlpha, List<ExtraLandmark> extraLandmarks, List<String> omit,
		Integer budget) {}

	@PostMapping
	public Map<String, Object> coverage(@PathVariable String ds, @PathVariable String deck, @RequestBody(required = false) CoverageIn in) {
		var ctx = load(ds, deck, in);
		var r = CoverageAnalyzer.analyze(ctx.outline, ctx.pillars, ctx.landmarks, ctx.scope, ctx.psi, ctx.gridM, ctx.sensor);
		double otherPsi = ctx.psi == CoverageAnalyzer.PSI_LOAD ? CoverageAnalyzer.PSI_UNLOAD : CoverageAnalyzer.PSI_LOAD;
		var other = CoverageAnalyzer.analyze(ctx.outline, ctx.pillars, ctx.landmarks, ctx.scope, otherPsi, ctx.gridM, ctx.sensor);

		var body = new LinkedHashMap<String, Object>();
		body.put("deck", deck);
		body.put("mode", ctx.mode);
		body.put("grid_m", ctx.gridM);
		body.put("bbox", Rings.bbox(ctx.outline));
		body.put("n_cells", r.nCells());
		body.put("n_drawn", r.nDrawn());
		body.put("blind_ratio", r.blindRatio());
		body.put("weak_ratio", r.weakRatio());
		if (r.worst() != null) body.put("worst", cell(r.worst()));
		body.put("other_mode", Map.of("mode", ctx.mode.equals("load") ? "unload" : "load",
			"blind_ratio", other.blindRatio(), "weak_ratio", other.weakRatio()));
		var cells = new ArrayList<Map<String, Object>>(r.cells().size());
		for (var c : r.cells()) cells.add(cell(c));
		body.put("cells", cells);
		return body;
	}

	@PostMapping("/suggest")
	public Map<String, Object> suggest(@PathVariable String ds, @PathVariable String deck, @RequestBody(required = false) CoverageIn in) {
		var ctx = load(ds, deck, in);
		int budget = in == null || in.budget() == null ? 3 : in.budget();
		double g = CoverageAnalyzer.SUGGEST_GRID_M;
		var cands = CoverageAnalyzer.candidates(ctx.outline, ctx.pillarIds, ctx.pillars, ctx.landmarks);
		var picks = CoverageAnalyzer.suggest(ctx.outline, ctx.pillars, ctx.landmarks, ctx.scope, cands, ctx.psi, ctx.sensor, budget);

		var before = CoverageAnalyzer.analyze(ctx.outline, ctx.pillars, ctx.landmarks, ctx.scope, ctx.psi, g, ctx.sensor);
		var withAll = new ArrayList<>(ctx.landmarks);
		for (var p : picks) withAll.add(new CoverageAnalyzer.Landmark("CAND", p.x(), p.y(), Math.toRadians(p.phiDeg())));
		var after = CoverageAnalyzer.analyze(ctx.outline, ctx.pillars, withAll, ctx.scope, ctx.psi, g, ctx.sensor);

		var out = new ArrayList<Map<String, Object>>();
		for (var p : picks) out.add(Map.of("rank", p.rank(), "x", p.x(), "y", p.y(), "phi_deg", p.phiDeg(),
			"mounted_on", p.mountedOn(), "blind_after", p.blindAfter(), "weak_after", p.weakAfter(), "gain", p.gain()));
		var body = new LinkedHashMap<String, Object>();
		body.put("deck", deck);
		body.put("mode", ctx.mode);
		body.put("budget", Math.min(budget, CoverageAnalyzer.MAX_BUDGET));
		body.put("grid_m", g);                                           // suggest pins its own grid: spec §4.2
		body.put("before", Map.of("blind_ratio", before.blindRatio(), "weak_ratio", before.weakRatio()));
		body.put("after", Map.of("blind_ratio", after.blindRatio(), "weak_ratio", after.weakRatio()));
		body.put("suggestions", out);
		return body;
	}

	/** in_scope is written only when false: it is the exception, and most cells would carry a redundant true. */
	static Map<String, Object> cell(CoverageAnalyzer.Cell c) {
		var m = new LinkedHashMap<String, Object>();
		m.put("x", c.x()); m.put("y", c.y()); m.put("n", c.n());
		if (c.sigmaXy() != null) { m.put("sigma_xy", c.sigmaXy()); m.put("sigma_psi", c.sigmaPsiDeg()); m.put("stability", c.stability()); }
		if (!c.inScope()) m.put("in_scope", false);
		return m;
	}

	record Ctx(double[][] outline, List<double[][]> pillars, List<String> pillarIds, List<CoverageAnalyzer.Landmark> landmarks,
		CoverageAnalyzer.Scope scope, double psi, double gridM, String mode, CoverageAnalyzer.Sensor sensor) {}

	Ctx load(String ds, String deck, CoverageIn in) {
		Datasets.require(db, ds);
		var d = db.sql("SELECT ST_AsGeoJSON(outline)::text AS g FROM deck WHERE dataset_id = :ds AND id = :deck")
			.param("ds", ds).param("deck", deck).query().listOfRows().stream().findFirst()
			.orElseThrow(() -> new ApiErrors.NotFound("deck " + deck));
		double[][] outline = Wkt.coords((String) d.get("g"));

		String mode = in == null || in.mode() == null ? "load" : in.mode();
		if (!mode.equals("load") && !mode.equals("unload")) throw new ApiErrors.BadRequest("mode must be load or unload", "mode");
		double gridM = in == null || in.gridM() == null ? 1.0 : in.gridM();
		if (gridM <= 0 || gridM > 10) throw new ApiErrors.BadRequest("grid_m must be in (0, 10]", "grid_m");
		var def = CoverageAnalyzer.DEFAULTS;
		var sensor = new CoverageAnalyzer.Sensor(
			in == null || in.fovDeg() == null ? def.fovRad() : Math.toRadians(in.fovDeg()),
			in == null || in.maxDistM() == null ? def.maxDistM() : in.maxDistM(),
			in == null || in.maxViewAngleDeg() == null ? def.maxViewAngleRad() : Math.toRadians(in.maxViewAngleDeg()),
			in == null || in.sigmaR() == null ? def.sigmaR() : in.sigmaR(),
			in == null || in.sigmaTheta() == null ? def.sigmaTheta() : Math.toRadians(in.sigmaTheta()),
			in == null || in.sigmaAlpha() == null ? def.sigmaAlpha() : Math.toRadians(in.sigmaAlpha()));
		if (sensor.maxDistM() <= 0 || sensor.fovRad() <= 0) throw new ApiErrors.BadRequest("fov_deg and max_dist_m must be > 0", "fov_deg");

		var pillars = new ArrayList<double[][]>();
		var pillarIds = new ArrayList<String>();
		for (var r : rows(ds, deck, "SELECT id, ST_AsGeoJSON(geom)::text AS g FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'C' AND kind = 'pillar'")) {
			pillarIds.add((String) r.get("id"));
			pillars.add(Wkt.coords((String) r.get("g")));
		}

		// Spec §3.4: where a vehicle can actually be. Same queries SlotController already uses.
		var slots = new ArrayList<double[][]>();
		for (var r : rows(ds, deck, "SELECT ST_AsGeoJSON(geom)::text AS g FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'B2'"))
			slots.add(Wkt.coords((String) r.get("g")));
		var lanes = new ArrayList<CoverageAnalyzer.Lane>();
		for (var r : rows(ds, deck, "SELECT ST_AsGeoJSON(geom)::text AS g, (props->>'width_m')::float8 AS w FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'A2'"))
			lanes.add(new CoverageAnalyzer.Lane(Wkt.coords((String) r.get("g")),
				r.get("w") == null ? 3.2 : ((Number) r.get("w")).doubleValue()));

		var omit = in == null || in.omit() == null ? List.<String>of() : in.omit();
		var lms = new ArrayList<CoverageAnalyzer.Landmark>();
		for (var r : rows(ds, deck, "SELECT id, ST_X(geom) AS x, ST_Y(geom) AS y, props::text AS p FROM feature WHERE dataset_id = :ds AND deck_id = :deck AND layer = 'LM'")) {
			String id = (String) r.get("id");
			if (omit.contains(id)) continue;
			double[] n = JsonMaps.doubles(JsonMaps.toMap(json, (String) r.get("p")).get("normal"));
			double phi = n == null || n.length < 2 ? Math.PI : Math.atan2(n[1], n[0]);   // no normal: treat as facing astern
			lms.add(new CoverageAnalyzer.Landmark(id, ((Number) r.get("x")).doubleValue(), ((Number) r.get("y")).doubleValue(), phi));
		}
		if (in != null && in.extraLandmarks() != null) {
			int i = 0;
			for (var e : in.extraLandmarks()) {
				if (e.x() == null || e.y() == null || e.phiDeg() == null) throw new ApiErrors.BadRequest("extra_landmarks need x, y, phi_deg", "extra_landmarks");
				lms.add(new CoverageAnalyzer.Landmark("EXTRA-" + (++i), e.x(), e.y(), Math.toRadians(e.phiDeg())));
			}
		}
		double psi = mode.equals("unload") ? CoverageAnalyzer.PSI_UNLOAD : CoverageAnalyzer.PSI_LOAD;
		return new Ctx(outline, pillars, pillarIds, lms, new CoverageAnalyzer.Scope(slots, lanes), psi, gridM, mode, sensor);
	}

	List<Map<String, Object>> rows(String ds, String deck, String sql) {
		return db.sql(sql).param("ds", ds).param("deck", deck).query().listOfRows();
	}
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `cd api && ./gradlew test`
Expected: 전체 PASS. `CoverageApiTests` 8 개 포함

`theDenominatorIsTheInScopeCellsOnly` 의 1836/2880 은 현재 픽스처 기준이다. 구획 생성 파라미터가 바뀌면 이 숫자도 바뀌므로, 실패하면 먼저 실제 값을 확인하고 스펙 §1.4 표와 함께 갱신한다.

- [ ] **Step 6: `docs/api-contract.md` 에 계약을 더한다**

피처 CRUD 항목 아래에 두 줄:

```markdown
- `POST /api/datasets/{ds}/decks/{deck}/coverage` — 갑판 격자의 달성 가능 추정 정밀도. 본문 전체가 선택: `mode`(`load`|`unload`, 기본 `load`), `grid_m`(기본 1.0, (0,10]), `fov_deg`·`max_dist_m`·`max_view_angle_deg`·`sigma_r`·`sigma_theta`·`sigma_alpha`(기본은 Unity `LandmarkSensor` 와 동일, 각도는 도), `extra_landmarks[{x,y,phi_deg}]`, `omit[id]`. 응답 `cells[{x,y,n,sigma_xy,sigma_psi,stability,in_scope}]` — `n=0` 이면 σ 생략, `in_scope` 는 false 일 때만 실린다. `n_cells` 는 **구획 ∪ 차로 회랑** 셀 수이자 `blind_ratio`·`weak_ratio` 의 분모이고, `n_drawn` 은 `cells` 길이(갑판 전체)다. `worst`·`other_mode` 동봉. **읽기 전용 — DB 를 바꾸지 않는다**
- `POST /api/datasets/{ds}/decks/{deck}/coverage/suggest` — 다음에 놓을 마커 위치. 위 파라미터 + `budget`(기본 3, 최대 10). **`grid_m` 은 무시하고 2.0 으로 고정하며 응답에 실제 쓴 값을 싣는다.** 응답 `before`·`after`·`suggestions[{rank,x,y,phi_deg,mounted_on,blind_after,weak_after,gain}]`. 순위는 누적(2 순위는 1 순위를 놓은 상태의 계산)이고 `gain` 은 그 순위에서 줄어든 `blind_ratio` 다
```

- [ ] **Step 7: 커밋**

```bash
git add api/src/main/java/com/shiphdmap/api/ api/src/test/java/com/shiphdmap/api/coverage/CoverageApiTests.java docs/api-contract.md
git commit -m "feat: coverage and suggest endpoints"
```

---

### Task 5: 웹 배선과 순수 함수

계산 결과를 받고, 색과 불투명도를 정하는 순수 함수를 만든다. 아직 화면은 없다.

**Files:**
- Modify: `web/src/api/types.ts`, `web/src/api/client.ts`, `web/src/store/editor.ts`
- Create: `web/src/geo/coverage.ts`, `web/src/geo/coverage.test.ts`

**Interfaces:**
- Produces: `CoverageCell`, `CoverageOut`, `SuggestOut`, `CoverageIn`, `Candidate` (types.ts)
- Produces: `api.coverage(ds, deck, body)`, `api.suggestCoverage(ds, deck, body)`
- Produces: `cellColor(c: CoverageCell): string`, `cellOpacity(c: CoverageCell): number`, `fmtRatio(v: number): string`
- Produces: store 의 `coverage`, `coverageMode`, `coverageParams`, `candidates`, `suggestions`, `coverageBusy`, `runCoverage(deck)`, `addCandidate`, `removeCandidate`, `clearCandidates`, `runSuggest(deck, budget)`, `commitCandidates(deck)`

**갑판 선택은 스토어가 하지 않는다.** `MiniMap.pickDeck` 이 이미 규칙을 갖고 있는데 스토어가 따로 고르면 두 규칙이 갈라진다 (세 갑판의 면적이 같아 `deckFilter = "all"` 이면 실제로 다른 갑판이 나온다). 스토어를 `MiniMap` 에서 import 하면 순환이 되므로, **액션이 갑판 id 를 인자로 받고** 패널이 `pickDeck` 으로 구해 넘긴다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`web/src/geo/coverage.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { BLIND_COLOR, cellColor, cellOpacity, fmtRatio } from "./coverage";

describe("cellColor", () => {
  it("칠하지 못하는 셀은 빨강", () => {
    expect(cellColor({ x: 0, y: 0, n: 0 })).toBe(BLIND_COLOR);
  });
  it("허용오차를 못 채우면 주황 계열, 채우면 초록 계열", () => {
    const weak = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.3, sigma_psi: 1, stability: 0.5 });
    const ok = cellColor({ x: 0, y: 0, n: 3, sigma_xy: 0.1, sigma_psi: 0.5, stability: 1.5 });
    expect(weak).not.toBe(ok);
    expect(weak).toMatch(/^#/);
    expect(ok).toMatch(/^#/);
  });
  it("stability 가 오르면 초록에 가까워진다", () => {
    const green = (hex: string) => parseInt(hex.slice(3, 5), 16);
    const low = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.6, sigma_psi: 4, stability: 0.25 });
    const mid = cellColor({ x: 0, y: 0, n: 2, sigma_xy: 0.15, sigma_psi: 2, stability: 1.0 });
    const high = cellColor({ x: 0, y: 0, n: 3, sigma_xy: 0.07, sigma_psi: 1, stability: 2.0 });
    expect(green(low)).toBeLessThan(green(mid));
    expect(green(mid)).toBeLessThan(green(high));
    expect(low).not.toBe(BLIND_COLOR);
  });
});

describe("cellOpacity", () => {
  it("차가 갈 수 있는 셀은 진하게", () => {
    expect(cellOpacity({ x: 0, y: 0, n: 2, stability: 1 })).toBeGreaterThan(0.4);
  });
  it("in_scope 가 false 면 흐리게 — 지표에서 빠졌다는 표시", () => {
    const out = cellOpacity({ x: 0, y: 0, n: 0, in_scope: false });
    expect(out).toBeLessThan(0.25);
    expect(out).toBeGreaterThan(0);
  });
});

describe("fmtRatio", () => {
  it("백분율 한 자리", () => {
    expect(fmtRatio(0.0213)).toBe("2.1 %");
    expect(fmtRatio(0)).toBe("0.0 %");
  });
});
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd web && pnpm vitest run coverage`
Expected: FAIL — `./coverage` 모듈이 없다

- [ ] **Step 3: 타입을 더한다**

`web/src/api/types.ts` 끝에:

```ts
/** in_scope is absent for in-scope cells: the API only writes it when false. */
export type CoverageCell = { x: number; y: number; n: number; sigma_xy?: number; sigma_psi?: number; stability?: number; in_scope?: boolean };
export type CoverageMode = "load" | "unload";
export type CoverageSensor = { fov_deg: number; max_dist_m: number; max_view_angle_deg: number; sigma_r: number; sigma_theta: number; sigma_alpha: number };
export type Candidate = { x: number; y: number; phi_deg: number; mounted_on?: string };
export type CoverageIn = Partial<CoverageSensor> & { mode?: CoverageMode; grid_m?: number; extra_landmarks?: Candidate[]; omit?: string[]; budget?: number };
export type CoverageOut = {
  deck: string; mode: CoverageMode; grid_m: number; bbox: number[];
  n_cells: number;        // in-scope cells; the denominator of both ratios
  n_drawn: number;        // cells.length, the whole deck
  blind_ratio: number; weak_ratio: number; worst?: CoverageCell;
  other_mode: { mode: CoverageMode; blind_ratio: number; weak_ratio: number };
  cells: CoverageCell[];
};
export type Suggestion = { rank: number; x: number; y: number; phi_deg: number; mounted_on: string; blind_after: number; weak_after: number; gain: number };
export type SuggestOut = {
  deck: string; mode: CoverageMode; budget: number; grid_m: number;
  before: { blind_ratio: number; weak_ratio: number }; after: { blind_ratio: number; weak_ratio: number };
  suggestions: Suggestion[];
};
```

- [ ] **Step 4: 클라이언트에 두 줄을 더한다**

`web/src/api/client.ts` 의 `api` 객체에, `generateSlots` 아래:

```ts
  coverage: (ds: string, deck: string, body: CoverageIn) => req<CoverageOut>("POST", `/datasets/${ds}/decks/${deck}/coverage`, body),
  suggestCoverage: (ds: string, deck: string, body: CoverageIn) => req<SuggestOut>("POST", `/datasets/${ds}/decks/${deck}/coverage/suggest`, body),
```

import 에 `CoverageIn`, `CoverageOut`, `SuggestOut` 를 더한다.

- [ ] **Step 5: 순수 함수를 만든다**

`web/src/geo/coverage.ts`:

```ts
import type { CoverageCell, CoverageSensor } from "../api/types";

export const BLIND_COLOR = "#d32f2f";
/** Must match CoverageAnalyzer.DEFAULTS. Seeding the sliders with these keeps the UI and the server in step. */
export const SENSOR_DEFAULTS: CoverageSensor = {
  fov_deg: 90, max_dist_m: 25, max_view_angle_deg: 70, sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2,
};

/** blind is its own colour; otherwise ramp orange -> green by stability, saturating at 2.0. */
export function cellColor(c: CoverageCell): string {
  if (c.n === 0 || c.stability == null) return BLIND_COLOR;
  const t = Math.min(c.stability, 2) / 2;                  // 0 .. 1
  const r = Math.round(240 - 180 * t), g = Math.round(120 + 80 * t), b = Math.round(40 + 40 * t);
  return `#${[r, g, b].map((v) => v.toString(16).padStart(2, "0")).join("")}`;
}

/** Cells nobody drives through are drawn faintly: visible, but plainly not part of the numbers. */
export function cellOpacity(c: CoverageCell): number { return c.in_scope === false ? 0.18 : 0.55; }

export function fmtRatio(v: number): string { return `${(v * 100).toFixed(1)} %`; }
```

- [ ] **Step 6: 스토어에 상태와 액션을 더한다**

`web/src/store/editor.ts` 의 `EditorState` 에:

```ts
  coverage: CoverageOut | null;
  coverageMode: CoverageMode;
  coverageParams: CoverageSensor & { grid_m: number };
  candidates: Candidate[];
  suggestions: Suggestion[];
  coverageBusy: boolean;
  setCoverageMode: (m: CoverageMode, deck: string) => void;
  setCoverageParams: (p: Partial<CoverageSensor & { grid_m: number }>) => void;
  runCoverage: (deck: string) => Promise<void>;
  addCandidate: (c: Candidate, deck: string) => void;
  removeCandidate: (i: number, deck: string) => void;
  clearCandidates: (deck: string) => void;
  runSuggest: (deck: string, budget: number) => Promise<void>;
  commitCandidates: (deck: string) => Promise<number>;
```

구현부(초기값은 `create` 의 첫 객체에, 액션은 `generateSlots` 옆에):

```ts
  coverage: null, coverageMode: "load",
  // seeded with the API defaults: an empty object would leave the sliders at their minimum while the server
  // silently computed with something else
  coverageParams: { ...SENSOR_DEFAULTS, grid_m: 1.0 },
  candidates: [], suggestions: [], coverageBusy: false,

  setCoverageMode: (coverageMode, deck) => { set({ coverageMode }); void get().runCoverage(deck); },
  setCoverageParams: (p) => set((s) => ({ coverageParams: { ...s.coverageParams, ...p } })),
  async runCoverage(deck) {
    if (!deck) return;
    const s = get();
    set({ coverageBusy: true });
    try {
      const out = await api.coverage(s.datasetId, deck, { ...s.coverageParams, mode: s.coverageMode, extra_landmarks: s.candidates });
      set({ coverage: out, error: null });
    } catch (e) { set({ error: (e as Error).message }); }
    finally { set({ coverageBusy: false }); }
  },
  addCandidate: (c, deck) => { set((s) => ({ candidates: [...s.candidates, c] })); void get().runCoverage(deck); },
  removeCandidate: (i, deck) => { set((s) => ({ candidates: s.candidates.filter((_, k) => k !== i) })); void get().runCoverage(deck); },
  clearCandidates: (deck) => { set({ candidates: [], suggestions: [] }); void get().runCoverage(deck); },
  async runSuggest(deck, budget) {
    if (!deck) return;
    const s = get();
    set({ coverageBusy: true });
    try {
      const out = await api.suggestCoverage(s.datasetId, deck, { ...s.coverageParams, mode: s.coverageMode, extra_landmarks: s.candidates, budget });
      set({ suggestions: out.suggestions, error: null });
    } catch (e) { set({ error: (e as Error).message }); }
    finally { set({ coverageBusy: false }); }
  },
  /** Persist every candidate as a real landmark. Returns how many were written. */
  async commitCandidates(deck) {
    const s = get();
    if (!deck || s.candidates.length === 0) return 0;
    const z = (s.decks.find((d) => d.id === deck)?.z_surface ?? 0) + 1.2;
    let code = Math.max(0, ...Object.values(s.features).filter((f) => f.layer === "LM").map((f) => Number(f.props.code) || 0));
    for (const c of s.candidates) {
      const phi = (c.phi_deg * Math.PI) / 180;
      await api.createFeature(s.datasetId, {
        layer: "LM", deck_id: deck, kind: "apriltag",
        geometry: { type: "Point", coordinates: [c.x, c.y, z] },
        props: { family: "apriltag-36h11", code: ++code, normal: [Math.cos(phi), Math.sin(phi), 0], size_m: 0.3, mounted_on: c.mounted_on ?? "" },
      });
    }
    const n = s.candidates.length;
    set({ candidates: [], suggestions: [] });
    await get().load(s.datasetId);
    await get().runCoverage(deck);
    return n;
  },
```

import 에 `SENSOR_DEFAULTS` 와 새 타입들을 더한다.

- [ ] **Step 7: 테스트 통과 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit`
Expected: PASS (새 6 개 포함), 타입 오류 없음

- [ ] **Step 8: 커밋**

```bash
git add web/src/api/types.ts web/src/api/client.ts web/src/store/editor.ts web/src/geo/coverage.ts web/src/geo/coverage.test.ts
git commit -m "feat: coverage client, store state and colour helpers"
```

---

### Task 6: 평면도 히트맵

**Files:**
- Modify: `web/src/components/MiniMap.tsx`

**Interfaces:**
- Consumes: Task 5 의 `cellColor`, `cellOpacity`, store 의 `coverage`, `candidates`
- Produces: 없음. **표시 전용이다 — `<svg>` 에 클릭 핸들러를 붙이지 않는다** (스펙 §6.3: 기존 피처에 이미 `onClick` 선택 핸들러가 있어 버블링으로 겹친다)

- [ ] **Step 1: 히트맵 레이어를 그린다**

`MiniMap.tsx` 의 `<svg>` 안, 갑판 윤곽 `<path>` **앞**에 (가장 아래 레이어):

```tsx
        {s.coverage?.cells.map((c, i) => (
          <rect key={i} x={c.x - s.coverage!.grid_m / 2} y={-c.y - s.coverage!.grid_m / 2}
            width={s.coverage!.grid_m} height={s.coverage!.grid_m}
            fill={cellColor(c)} fillOpacity={cellOpacity(c)} stroke="none" pointerEvents="none" />
        ))}
```

`pointerEvents="none"` 로 히트맵이 아래의 피처 선택을 가로채지 않게 한다.

import 에 `import { cellColor, cellOpacity } from "../geo/coverage";` 를 더한다.

- [ ] **Step 2: 후보 마커를 그린다**

기존 `drafts` 원 아래에:

```tsx
        {s.candidates.map((c, i) => (
          <g key={`cand-${i}`} pointerEvents="none">
            <circle cx={c.x} cy={-c.y} r={0.8} fill="none" stroke="#7b1fa2" strokeWidth={0.35} />
            <line x1={c.x} y1={-c.y} x2={c.x + Math.cos((c.phi_deg * Math.PI) / 180) * 2}
              y2={-c.y - Math.sin((c.phi_deg * Math.PI) / 180) * 2} stroke="#7b1fa2" strokeWidth={0.25} />
          </g>
        ))}
```

법선 화살표까지 그리는 이유: 법선이 위치보다 방향 추정에 치명적인데 지금은 `props` JSON 안에 숨어 있어 눈으로 확인할 길이 없다.

- [ ] **Step 3: 빌드와 기존 테스트 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit`
Expected: PASS. `MiniMap.test.ts` 의 기존 케이스가 그대로 통과한다

- [ ] **Step 4: 커밋**

```bash
git add web/src/components/MiniMap.tsx
git commit -m "feat: coverage heatmap and candidate normals on the plan view"
```

---

### Task 7: 커버리지 패널

**Files:**
- Create: `web/src/components/CoveragePanel.tsx`
- Modify: `web/src/App.tsx`

**Interfaces:**
- Consumes: Task 5 의 store 액션 전부, `MiniMap.pickDeck`
- Produces: `CoveragePanel` 컴포넌트

- [ ] **Step 1: 패널을 만든다**

`web/src/components/CoveragePanel.tsx`:

```tsx
import { useEffect, useState } from "react";
import { useEditorStore } from "../store/editor";
import { fmtRatio } from "../geo/coverage";
import { pickDeck } from "./MiniMap";

export function CoveragePanel() {
  const s = useEditorStore();
  const [budget, setBudget] = useState(3);
  const [msg, setMsg] = useState<string | null>(null);
  // the same rule MiniMap frames on, so the heatmap can never belong to a different deck than the drawing
  const deck = pickDeck(s.decks, s.deckFilter)?.id;
  useEffect(() => { if (deck) void s.runCoverage(deck); }, [deck]); // eslint-disable-line react-hooks/exhaustive-deps

  const p = s.coverageParams;
  const num = (k: keyof typeof p, label: string, step: number, min: number, max: number) => (
    <div className="row" key={k}>
      <label>{label}</label>
      <input type="range" step={step} min={min} max={max} value={p[k]}
        onChange={(e) => s.setCoverageParams({ [k]: Number(e.target.value) })}
        onMouseUp={() => deck && void s.runCoverage(deck)} onTouchEnd={() => deck && void s.runCoverage(deck)} />
      <span style={{ width: 44, textAlign: "right" }}>{p[k]}</span>
    </div>
  );

  if (!deck) return null;
  return (
    <div className="panel">
      <h4>커버리지{s.coverageBusy ? " …" : ""}</h4>
      <div className="row">
        <button className={`btn${s.coverageMode === "load" ? " primary" : ""}`} onClick={() => s.setCoverageMode("load", deck)}>선적</button>
        <button className={`btn${s.coverageMode === "unload" ? " primary" : ""}`} onClick={() => s.setCoverageMode("unload", deck)}>하역</button>
      </div>
      {s.coverage && (
        <>
          <div className="row"><label>사각지대</label><span><b>{fmtRatio(s.coverage.blind_ratio)}</b> · 반대 모드 {fmtRatio(s.coverage.other_mode.blind_ratio)}</span></div>
          <div className="row"><label>허용오차 미달</label><span>{fmtRatio(s.coverage.weak_ratio)}</span></div>
          {/* the denominator is not the deck: say so, or the number reads as twice the problem it is */}
          <div className="row"><label>대상</label><span>구획·차로 {s.coverage.n_cells.toLocaleString()} 셀 · 갑판 전체 {s.coverage.n_drawn.toLocaleString()} 셀 · {s.coverage.grid_m} m 격자</span></div>
          <div className="row"><label>최악 지점</label><span>{s.coverage.worst ? `x ${s.coverage.worst.x.toFixed(1)} y ${s.coverage.worst.y.toFixed(1)} · σxy ${s.coverage.worst.sigma_xy?.toFixed(2)} m` : "—"}</span></div>
        </>
      )}
      {num("grid_m", "격자 m", 0.5, 0.5, 4)}
      {num("max_dist_m", "인식거리 m", 1, 5, 40)}
      {num("fov_deg", "시야각 °", 5, 30, 180)}
      {num("max_view_angle_deg", "시야한계 °", 5, 20, 89)}
      {num("sigma_r", "σr m", 0.05, 0, 1)}
      {num("sigma_theta", "σθ °", 0.5, 0, 10)}
      {num("sigma_alpha", "σα °", 0.5, 0, 10)}

      <div className="row">
        <label>추천</label>
        <input type="number" min={1} max={10} value={budget} onChange={(e) => setBudget(Number(e.target.value))} style={{ width: 48 }} />
        <button className="btn" onClick={() => void s.runSuggest(deck, budget)} disabled={s.coverageBusy}>실행</button>
      </div>
      {s.suggestions.map((g) => (
        <div className="row" key={g.rank}>
          <span>{g.rank}. x {g.x.toFixed(1)} y {g.y.toFixed(1)} φ {g.phi_deg.toFixed(0)}° · 사각 −{fmtRatio(g.gain)}</span>
          <button className="btn" onClick={() => s.addCandidate({ x: g.x, y: g.y, phi_deg: g.phi_deg, mounted_on: g.mounted_on }, deck)}>담기</button>
        </div>
      ))}

      <div className="row">
        <label>후보 {s.candidates.length} 개</label>
        <button className="btn" onClick={() => s.clearCandidates(deck)} disabled={!s.candidates.length}>비우기</button>
        <button className="btn primary" disabled={!s.candidates.length}
          onClick={() => void s.commitCandidates(deck).then((n) => setMsg(`${n} 개 저장됨`))}>확정 저장</button>
      </div>
      {s.candidates.map((c, i) => (
        <div className="row" key={`c${i}`}>
          <span>x {c.x.toFixed(1)} y {c.y.toFixed(1)} φ {c.phi_deg.toFixed(0)}°</span>
          <button className="btn" onClick={() => s.removeCandidate(i, deck)}>삭제</button>
        </div>
      ))}
      {msg && <div className="row"><span>{msg}</span></div>}
    </div>
  );
}
```

- [ ] **Step 2: 앱에 끼운다**

`web/src/App.tsx` 의 편집 모드 가지에 `CoveragePanel` 을 더한다:

```tsx
        {mode === "edit" ? (<><LoadPanel reloadScene={reloadScene} /><CoveragePanel /><PropertyForm send={send} /></>) : <DrivePanel send={send} />}
```

import 를 더한다: `import { CoveragePanel } from "./components/CoveragePanel";`

- [ ] **Step 3: 빌드 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit && pnpm build`
Expected: 전부 성공

- [ ] **Step 4: 커밋**

```bash
git add web/src/components/CoveragePanel.tsx web/src/App.tsx
git commit -m "feat: coverage panel with parameters, summary and suggestions"
```

---

### Task 8: 문서와 브라우저 검증

**Files:**
- Modify: `README.md`, `docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md`

- [ ] **Step 1: 전체 테스트를 돌린다**

```bash
cd api && ./gradlew test
cd ../web && pnpm vitest run && pnpm tsc --noEmit
```
Expected: 전부 PASS. 새 테스트 수 (Java `CoverageAnalyzerTests` 24 + `CoverageApiTests` 8, Vitest 6)

- [ ] **Step 2: API 와 웹을 띄워 수동 검증한다**

```bash
cd db && docker compose up -d
cd ../api && ./gradlew bootRun &
cd ../web && pnpm dev
```

브라우저에서 순서대로 확인한다:

1. 편집 모드에서 Deck 3 을 고르면 평면도에 히트맵이 뜨고, **바깥 열 구획이 빨갛다.** 갑판 가장자리도 빨갛지만 흐려서 구분된다
2. `사각지대` 가 **16.8 %** 이고, 옆에 `구획·차로 1,236 셀 · 갑판 전체 2,880 셀` 이 같이 보인다 (구획이 76 개일 때. 136 개면 `slots/generate` 를 먼저 돌린다)
3. **하역**으로 토글하면 선미 구간이 추가로 빨갛게 바뀌고 `사각지대` 가 **25.6 %** 로 오른다
4. 추천 3 개를 실행하면 바깥 열이나 선미를 메우는 위치가 나오고 `사각 −N %` 가 양수다
5. `담기` 를 누르면 후보가 보라색 원+법선 화살표로 나타나고 `사각지대` 가 떨어진다
6. `확정 저장` 을 누르면 랜드마크가 저장되고 3D 에 나타나며 상태바의 version 이 오른다
7. `인식거리 m` 을 15 로 내리면 `허용오차 미달` 이 오른다
8. `PosePanel` 의 흘수·횡경사를 움직여도 커버리지 숫자가 **변하지 않는다** (Ship Frame 불변)
9. 갑판 필터를 `전체` 로 두어도 평면도가 그리는 갑판과 히트맵의 갑판이 같다
10. 히트맵 위에서 마커를 클릭하면 **선택만** 되고 다른 일은 일어나지 않는다

- [ ] **Step 3: 기본 스펙의 구현 순서 표에 M5c 행을 더한다**

`docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md` §13 표의 M5b 행 아래:

```markdown
| M5c 커버리지 분석 | 구획·차로 격자의 추정 정밀도(정보행렬 기반 σ) 히트맵, 선적·하역 모드, 가상 마커 what-if, 그리디 배치 추천 (`2026-09-17-m5c-coverage-design.md`) | 바깥 열 구획의 사각지대와 하역 선미 사각지대가 숫자로 드러나고, 추천이 그것을 메우는 위치를 내며, 확정 저장이 DB 에 반영된다 |
```

- [ ] **Step 4: README 에 한 절을 더한다**

적재 계획 절 아래:

```markdown
### 랜드마크 커버리지

편집 모드의 `커버리지` 패널이 갑판을 격자로 훑어 **각 지점에서 달성 가능한 추정 정밀도**를 계산한다. 보이는 마커 수가 아니라 정보행렬에서 얻는 σ 이므로, 마커가 한쪽에 몰려 있으면 개수가 많아도 나쁘게 나온다.

- **지표의 분모는 구획과 차로 회랑이다.** 갑판 가장자리는 히트맵에 흐리게 그리기만 하고 숫자에는 넣지 않는다 — 차가 갈 일이 없는 곳이 통계를 지배하면 추천이 거기를 메우러 간다
- 선적(ψ=0) / 하역(ψ=180) 을 따로 계산한다. 시야각이 90° 라 주행 방향이 바뀌면 보이는 마커가 달라진다
- `추천` 은 기둥·격벽 면에 가상 마커를 하나씩 놓아보고 사각지대를 가장 많이 줄이는 자리를 제안한다
- 후보는 DB 를 건드리지 않는다. `확정 저장` 을 눌러야 실제 랜드마크가 된다

현재 픽스처의 Deck 3 에서는 마커가 `y = ±6.2` 한 줄에만 있어, 선적 모드에서도 **바깥 열 구획의 68 %** 에서 자세를 잡을 수 없다. 도구가 답해야 하는 질문이 바로 이것이다.
```

- [ ] **Step 5: 커밋**

```bash
git add README.md docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md
git commit -m "docs: M5c coverage analysis in the README and the milestone table"
```

---

## Self-Review

**스펙 커버리지**

| 스펙 절 | 담당 Task |
| --- | --- |
| §3.1 가시성 3 조건 | 1 (`visible`) |
| §3.1 차폐, 자기 기둥 예외 | 2 (`occluded`, `MOUNT_CLEARANCE_M`) |
| §3.2 정보행렬 → σ, blind 통합 | 1 (`estimate`, `invert3`) |
| §3.3 안정성 마진과 셀 분류 | 1 (`stability`), 2 (`analyze` 의 blind/weak 집계) |
| §3.4 격자, 윤곽 안·기둥 밖 | 2 (`analyze`) |
| §3.4 `in_scope` (구획 ∪ 차로), 빈 scope = 전부 | 2 (`Scope`), 4 (B2·A2 쿼리) |
| §3.5 모드 | 2 (`PSI_LOAD`/`PSI_UNLOAD`), 4 (`mode` 파라미터) |
| §3.5 선미 끝단 마커가 죽었다 | 2 (`theSternEndMarkersAreVisibleFromNowhere`) |
| §4.1 coverage 엔드포인트, `n_cells`/`n_drawn`, `worst`, `other_mode` | 4 |
| §4.2 suggest 엔드포인트, 격자 2.0 고정, `gain` = blind 감소량 | 3, 4 |
| §5.1 후보 면, 법선 방향, 긴 변 12 m 분할 | 3 (`candidates`, `addFaces`) |
| §5.1.1 거리 + 법선 배제 | 3 (`alreadyCovered`, `aMarkerOnOneFaceDoesNotBlockTheOppositeFace`) |
| §5.2 그리디, in-scope 목적함수, out-of-scope 건너뛰기 | 3 (`tally`, `suggest`) |
| §5.3 성능 주석 | 3 (`suggest` 의 `ponytail:`) |
| §6.1 히트맵 레이어, out-of-scope 흐리게, 갑판 일치 | 6, 7 (`pickDeck`) |
| §6.2 패널 구역, 파라미터 초기값, 분모 표시 | 5 (`SENSOR_DEFAULTS`), 7 |
| §6.3 클릭 배치 없음, 확정 저장 | 5 (`commitCandidates`), 6 (`pointerEvents="none"`) |
| §7.1 단위 10 항목 | 1, 2, 3 |
| §7.2 회귀 2 건 | 2 (`theOuterSlotRowIsBlindEvenWhenLoading`, `unloadIsBlindNearTheStern`), 4 (`theFixtureBlindSpotsShowUp`) |
| §7.3 Vitest 3 항목 | 5 |
| §7.4 브라우저 10 항목 | 8 |

**앞선 초안에서 고친 것**

1. **평가 대상** — 지표와 목적함수의 분모를 갑판 전체에서 구획 ∪ 차로로 좁혔다. 전 갑판으로 재면 선적 `blind_ratio` 29.7 % 의 대부분이 아무도 가지 않는 가장자리이고, 그리디가 거기를 메우러 간다. `scope` 모드 대신 셀당 `in_scope` 불리언이라 히트맵은 그대로 전 갑판이다
2. **`omit` 테스트** — `weak_ratio` 로 재면 깨진다. weak 셀이 blind 로 넘어가면서 분자가 줄기 때문(픽스처에서 0.6875 → 0.5375). `blind_ratio` 로 바꿨다
3. **`contains`/`bbox` 중복** — `SlotGenerator` 에서 `geo/Rings` 로 옮기고 양쪽이 공유한다. `props` 배열 파싱도 `VehicleMapAssembler.arr` 를 `JsonMaps.doubles` 로 옮겨 공유한다
4. **갑판 선택** — 스토어가 `decks[length-1]` 을 고르고 `MiniMap` 이 `pickDeck` 을 쓰면 갑판 면적이 모두 2,880 이라 실제로 D1 위에 D3 히트맵이 깔린다. 액션이 갑판 id 를 인자로 받고 패널이 `pickDeck` 으로 구한다 (스토어가 `MiniMap` 을 import 하면 순환)
5. **슬라이더 초기값** — `coverageParams` 를 `SENSOR_DEFAULTS` 로 채운다. 비워 두면 슬라이더는 최솟값에 서 있는데 서버는 기본값으로 계산한다
6. **평면도 클릭 배치 삭제** — 기존 피처에 `onClick` 이 있어 버블링으로 겹친다. `snapToFace` 와 그 테스트도 없앴다
7. **특이행렬 셀** — `blind` 로 합쳤다. 따로 두면 어느 비율에도 안 세지는데 화면에는 빨갛게 칠해진다
8. **`gain`** — `blind_ratio` 감소량으로 정의를 통일했다 (계획의 옛 식은 점수 차 / 셀 수라 스펙 예시와 단위가 달랐다)
9. **`suggest` 격자 2.0 고정** — 추천이 고르는 것은 구조물 면이라 1 m 해상도가 의미 없다. 요청의 `grid_m` 과 `budget` 이 곱해져 Tomcat 스레드를 오래 잡는 것도 같이 막힌다
10. **`fixtureLandmarks()`** — LM-0019 가 빠져 있었다 (실제 23, 헬퍼 22)
11. **후보 배제 규칙** — 거리 2 m 만 보면 Deck 3 의 기둥 면 72 개가 전부 걸러진다 (기둥 18 개가 전부 `|y| ≈ 6.5` 의 마커 줄 위에 있고 마커가 0.12~0.67 m 거리에 붙어 있다). 후보가 갑판 윤곽 4 개만 남아 §1.4 의 바깥 열 사각지대를 **메울 수단이 없어진다.** 거리 AND 법선 방향으로 바꾸고, 긴 변을 12 m 마다 나눈다

**남은 차이 — 의도한 축소**

- 스펙 §6.2 는 기존 마커 제외(`omit`)를 `LayerTree` 체크박스로 했다. 계획에는 API 까지만 있고 UI 는 없다. `omit` 은 엔드포인트와 테스트로 동작이 보장되며, 체크박스는 `LayerTree` 구조를 건드려야 해서 분리했다. 필요하면 Task 9 로 더한다
- §7.2 회귀는 단위 테스트에서 구획 136 개 대신 `|y|` 대역 8 줄로 모사한다. 단언이 대역별 blind 비율이라 같은 것을 잰다. 실제 136 개에 대한 검증은 Task 4 의 `theFixtureBlindSpotsShowUp` 이 DB 를 써서 한다

**타입 일관성** — `Landmark`·`Sensor`·`Estimate`·`Lane`·`Scope`·`Cell`·`Result`·`Tally`·`Candidate`·`Suggestion` 의 필드명이 Task 1→3 정의와 Task 4 컨트롤러 사용처에서 일치한다. `analyze` 는 `(outline, pillars, lms, scope, psi, gridM, sensor)`, `suggest` 는 `(outline, pillars, lms, scope, cands, psi, sensor, budget)` 으로 **`suggest` 만 `gridM` 이 없다**. 웹의 `CoverageCell.in_scope` 는 Java `Cell.inScope()` 와, `phi_deg` 는 `Suggestion.phiDeg()` 와 대응한다.
