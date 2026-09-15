# M3a 웹 편집기 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 브라우저에서 React 셸이 Unity WebGL 빌드를 품고, 갑판 트리·미니맵·속성 폼으로 랜드마크를 배치·수정·삭제하면 M2 API 를 통해 DB 에 반영되며, pose 슬라이더와 최소 주행 탭이 동작한다.

**Architecture:** `web/`(pnpm, Vite 8, React 19, TypeScript, Zustand 5, react-unity-webgl 10)이 `/api` 를 8081 로 프록시하고 `public/unity/`(gitignore) 의 WebGL 빌드를 `useUnityContext` 로 올린다. Unity → 웹은 `.jslib` 의 `dispatchReactUnityEvent`, 웹 → Unity 는 `sendMessage("Map", …)`. 상태는 Zustand 스토어 하나. 저장 단위는 피처 1건(스펙 9.1). Unity 쪽 변경은 `.jslib`·`Delete` 메시지·빌드 메서드뿐이며 씬 UX 는 M3b.

**Tech Stack:** Node 22 + pnpm, Vite 8.3, React 19.3, TypeScript 5, Zustand 5.0, react-unity-webgl 10.2, Vitest; Unity 6000.3.24f1 WebGL(압축 Disabled); Spring Boot 4 API(M2)

**Spec:** `docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md` — §2 구조, §8 화면 A안, §9.1 편집·저장 흐름, §10 브리지 계약, §13 M3a 행

## Global Constraints

- 브리지 계약(스펙 §10): R→U `Load`, `SetMode`, `SetDeck`, `Select`, `Confirm`, `SetPose`(M5), `SetNoise`, `StartScenario`, **`Delete`(M3a 추가)**; U→R `onSeedReady`, `onFeatureCreated`, `onFeatureMoved`(M3b), `onSelected`, `onSlotFilled`(M5), `onLocalization`. 페이로드는 JSON 문자열, 좌표는 Ship Frame, 각도는 도
- GameObject 이름 `Map`, 메서드명은 위 그대로 (`sendMessage("Map", "Load", json)`)
- 응답 규약: API 응답에서 null 필드는 생략된다(absent = null). `heading_deg`(pose·dataset)는 진북 기준 선수방위. 둘 다 `docs/api-contract.md` 에 적는다
- 좌표: Ship Frame x 선수(+), y 좌현(+), z 상방(+). TS `shipFrame.ts` 는 `docs/test-vectors/ship-frame.json` 의 `points`·`headings`·`wrap_deg`·`wgs84` 전부를 통과해야 한다
- 미니맵은 SVG, x 는 오른쪽(선수), **y 는 위쪽(좌현)** 이 되도록 화면 y 를 반전한다
- 포트: API 8081, PostGIS 5433, Vite 5173(기본). 5432/8080/8081 이외는 건드리지 않는다
- WebGL 빌드 산출물 `web/public/unity/` 는 gitignore(루트 `.gitignore` 에 이미 있음). 빌드는 압축 Disabled
- 저장소는 public. 기관·회사·과제명·이력 표현, 사용자 계정명이 든 절대경로, 실제 항구 좌표 금지. 사설 단어목록은 `SENSITIVE_TERMS_FILE` 환경변수로만 참조
- 커밋은 각 Task 끝에 한다. 푸시는 사용자 요청 시에만. Unity 배치 명령(빌드·테스트)은 에디터 GUI 를 닫은 상태에서만 돈다

---

## 파일 구조

```
api/src/main/java/com/shiphdmap/api/pose/PoseStore.java      (수정) measured_at 교체
api/src/main/java/com/shiphdmap/api/ApiErrors.java            (수정) 제약 위반 메시지 정리
docs/api-contract.md                                           (신규) 응답 규약·엔드포인트 요약
unity/Assets/Plugins/WebGL/ShipHdMapBridge.jslib               (신규) EmitToWeb → dispatchReactUnityEvent
unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs            (수정) WebGL 에서 Emit 을 jslib 로, Delete(id)
unity/Assets/ShipHdMap/Editor/WebGLBuild.cs                    (신규) 메뉴/배치 빌드
unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs       (수정) Delete 테스트
web/
  package.json, pnpm-lock.yaml, vite.config.ts, tsconfig.json, index.html
  src/main.tsx, src/App.tsx, src/App.css
  src/geo/shipFrame.ts                 wrap, toUnity, toWgs84
  src/api/types.ts                     Feature, Deck, Pose, VehicleMap 일부, RampState
  src/api/client.ts                    fetch 래퍼
  src/store/editor.ts                  Zustand 스토어(상태 + 액션)
  src/bridge/useShipUnity.ts           useUnityContext 래핑, send(), 이벤트 → 스토어
  src/components/TopBar.tsx, DeckTabs.tsx, LayerTree.tsx, MiniMap.tsx, PropertyForm.tsx,
                 PosePanel.tsx, DrivePanel.tsx, StatusBar.tsx
  src/geo/shipFrame.test.ts, src/store/editor.test.ts, src/api/client.test.ts
scripts/m3-dev.sh                       compose + bootRun + pnpm dev 한 번에
README.md                               (수정) web 절·상태
```

---

### Task 1: M2 파킹 정리와 API 계약 문서

**Files:**
- Modify: `api/src/main/java/com/shiphdmap/api/pose/PoseStore.java` (`put` 의 `measuredAt` 병합 → 교체)
- Modify: `api/src/main/java/com/shiphdmap/api/ApiErrors.java` (`constraint` 메시지)
- Modify: `api/src/test/java/com/shiphdmap/api/pose/PoseTests.java`, `api/src/test/java/com/shiphdmap/api/dataset/SeedImportTests.java` (테스트 추가)
- Create: `docs/api-contract.md`

**Interfaces:**
- `PUT /pose` 는 다른 필드를 병합하되 `measured_at` 은 **요청값으로 교체**한다(요청에 없으면 null 로 지워진다 → 응답에서 생략)
- 제약 위반 400 본문: `{"status":400,"error":"Bad Request","message":"constraint violation: <constraint name>"}`. PG 원문은 넣지 않는다. 제약 이름은 원문의 `constraint "<name>"` 에서 추출, 없으면 `constraint violation`

- [ ] **Step 1: 실패하는 테스트**

`PoseTests.java` 에 추가:

```java
	@Test
	void measuredAtIsReplacedNotMerged() throws Exception {
		mvc.perform(put("/api/datasets/" + DS + "/pose").contentType(MediaType.APPLICATION_JSON)
			.content("{\"tide_m\":0.3,\"measured_at\":\"2026-09-15T09:00:00Z\"}")).andExpect(status().isOk())
			.andExpect(jsonPath("$.measured_at").value("2026-09-15T09:00:00Z"));
		// a new measurement without a timestamp clears the old one (absent == null)
		mvc.perform(put("/api/datasets/" + DS + "/pose").contentType(MediaType.APPLICATION_JSON).content("{\"tide_m\":0.4}"))
			.andExpect(status().isOk()).andExpect(jsonPath("$.measured_at").doesNotExist()).andExpect(jsonPath("$.tide_m").value(0.4));
	}
```

`SeedImportTests.java` 에 추가 (MockMvc 가 없으면 `@AutoConfigureMockMvc` 와 `MockMvc` 필드를 클래스에 추가):

```java
	@Test
	void constraintViolationBodyHidesDriverText() throws Exception {
		// a slot whose access_lane_id points to a lane that does not exist violates the FK
		String body = """
			{"parking_slots":[{"id":"PS-X","deck_id":"D3","polygon":[[1,1,10.6],[2,1,10.6],[2,2,10.6],[1,1,10.6]],
			 "target_pose":{"x":1.5,"y":1.5,"heading_deg":0},"tolerance":{"lat_m":0.1,"lon_m":0.1,"heading_deg":1},
			 "vehicle_class":"passenger","access_lane_id":"A2-NOPE","lashing_points":[],"sequence_no":9,"status":"empty"}]}""";
		importer.importSeed("roro-demo-01", json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class).decks() == null ? null : fixtureAsSeed(json));
		mvc.perform(post("/api/datasets/roro-demo-01/seed").contentType(MediaType.APPLICATION_JSON).content(body))
			.andExpect(status().isBadRequest())
			.andExpect(jsonPath("$.message").value(org.hamcrest.Matchers.startsWith("constraint violation")))
			.andExpect(jsonPath("$.message").value(org.hamcrest.Matchers.not(org.hamcrest.Matchers.containsString("ERROR"))));
	}
```

위 테스트의 첫 `importer.importSeed(...)` 줄은 갑판 D3 가 있어야 하므로 픽스처를 먼저 적재하는 용도다. 간단히 `importer.importSeed("roro-demo-01", fixtureAsSeed(json));` 로 쓴다(삼항식은 쓰지 말 것).

- [ ] **Step 2: 실패 확인**

```bash
cd api && export JAVA_HOME=/opt/homebrew/opt/openjdk && ./gradlew test --tests 'com.shiphdmap.api.pose.*' --tests 'com.shiphdmap.api.dataset.*' --console=plain 2>&1 | grep -E "FAILED|tests completed|BUILD" | head
```

Expected: 두 테스트 FAILED (measured_at 이 유지됨; 메시지에 PG 원문).

- [ ] **Step 3: 구현**

`PoseStore.put` 의 병합 줄에서 `measuredAt` 만 바꾼다:

```java
		Pose merged = new Pose(
			p.draftFwdM() != null ? p.draftFwdM() : cur.draftFwdM(),
			p.draftAftM() != null ? p.draftAftM() : cur.draftAftM(),
			p.heelDeg() != null ? p.heelDeg() : cur.heelDeg(),
			p.headingDeg() != null ? p.headingDeg() : cur.headingDeg(),
			p.tideM() != null ? p.tideM() : cur.tideM(),
			p.quayZM() != null ? p.quayZM() : cur.quayZM(),
			p.apLat() != null ? p.apLat() : cur.apLat(),
			p.apLon() != null ? p.apLon() : cur.apLon(),
			p.measuredAt());   // replaced, not merged: a new measurement owns its timestamp
```

(기존 코드의 필드 순서·이름에 맞춰 위치만 바꾼다. 다른 필드의 병합은 그대로.)

`ApiErrors.constraint`:

```java
	static final java.util.regex.Pattern CONSTRAINT = java.util.regex.Pattern.compile("constraint \"([^\"]+)\"");

	@ExceptionHandler(DataIntegrityViolationException.class) @ResponseStatus(HttpStatus.BAD_REQUEST)
	Map<String, Object> constraint(DataIntegrityViolationException e) {
		String m = e.getMostSpecificCause().getMessage();
		var mt = m == null ? null : CONSTRAINT.matcher(m);
		String name = mt != null && mt.find() ? mt.group(1) : null;
		return body(400, "Bad Request", name == null ? "constraint violation" : "constraint violation: " + name, null);
	}
```

- [ ] **Step 4: 통과 확인** — 같은 명령. Expected: 전부 통과(스위트 35).

- [ ] **Step 5: 계약 문서**

`docs/api-contract.md` (문어체 불릿):

```markdown
# API 계약 (M2 기준, M3 웹이 의존)

- 베이스 경로 `/api`. 개발 시 Vite 가 `/api` 를 `http://localhost:8081` 로 프록시
- JSON 키는 snake_case. **null 인 필드는 응답에서 생략된다** — 클라이언트는 "없음"과 null 을 같게 다룬다
- 좌표는 Ship Frame `[x, y, z]` m. `heading_deg`(pose·dataset)는 진북 기준 시계방향 선수방위. 피처·차량의 헤딩 ψ(+x 기준 반시계)와 다른 양
- 오류: 4xx `{status, error, message, field?}`. 제약 위반은 `message: "constraint violation: <constraint>"`. 5xx 본문 없음
- 쓰기(피처 생성·수정·삭제, 구획 상태, 시드)마다 `dataset.version` +1. `GET vehicle-map` 의 `ETag` 가 이 값이며 `If-None-Match` 일치 시 304

## 엔드포인트

- `POST /datasets` `{id, name, ship_name?, ap_lat?, ap_lon?, heading_deg?, lpp_m?}` → 201 `{id, version}` · 409 중복
- `GET /datasets/{id}` → `{id, name, ship_name, version, ap_lat, ap_lon, heading_deg, lpp_m, created_at}`
- `POST /datasets/{id}/seed` 시드 JSON(vehicle-map 전체 가능) → `{decks, features, parking_slots, version}`
- `GET /datasets/{id}/features?deck=D3&layer=LM` → 피처 배열. 피처: `{id, deck_id?, layer, kind, geometry{type, coordinates}, props, created_at, updated_at}`
- `POST /datasets/{id}/features` `{id?, deck_id?, layer, kind, geometry, props?}` → 201 피처(id 생략 시 `{layer}-{n:0000}`)
- `PUT /datasets/{id}/features/{fid}` 부분 갱신(보낸 필드만; `layer` 변경 불가) → 200 피처
- `DELETE /datasets/{id}/features/{fid}` → 204 · 404 · 409(구획이 참조하는 래싱)
- `PUT /datasets/{id}/slots/{sid}/status` `{status: empty|filled|needs_adjust}` → `{id, status}`
- `GET /datasets/{id}/vehicle-map` → 스펙 §6 JSON, `ETag`
- `GET /datasets/{id}/export.geojson` → WGS84 FeatureCollection (`application/geo+json`)
- `GET|PUT /datasets/{id}/pose` → pose + `trim_deg`. PUT 은 부분 갱신, `measured_at` 은 요청값으로 교체
- `GET /datasets/{id}/ramps/{rid}` → 램프 props + `hinge`, `angle_deg`, `state(deployed|blocked)`

## 브리지 (React ↔ Unity, 스펙 §10)

- R→U `sendMessage("Map", name, json)`: `Load`, `SetMode("edit"|"drive")`, `SetDeck("D3"|"all")`, `Select(id)`, `Confirm({tempId,id})`, `Delete(id)`, `SetNoise({sigma_r,sigma_theta,sigma_alpha,sigma_gps})`, `StartScenario({mode})`
- U→R 이벤트(`addEventListener(name, (json) => …)`): `onSeedReady`, `onFeatureCreated{tempId,layer,x,y,z,deck}`, `onSelected{id}`, `onLocalization{est_x,est_y,est_psi,true_x,true_y,true_psi,residual_rms,n_obs,frame}`
```

- [ ] **Step 6: Commit**

```bash
git add api/src docs/api-contract.md
git commit -m "api: pose measured_at replaced on PUT, sanitized constraint errors; API contract doc"
```

---

### Task 2: Unity — jslib 브리지, `Delete` 메시지, WebGL 빌드

**Files:**
- Create: `unity/Assets/Plugins/WebGL/ShipHdMapBridge.jslib`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Create: `unity/Assets/ShipHdMap/Editor/WebGLBuild.cs`
- Modify: `unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs`

**Interfaces:**
- jslib: `EmitToWeb(name, json)` → `window.dispatchReactUnityEvent(UTF8ToString(name), UTF8ToString(json))`. 전역 함수가 없으면(스탠드얼론 페이지) 무시
- `MapRuntime.Send(name, json)`: 기존 `Emit?.Invoke` 유지 + `#if UNITY_WEBGL && !UNITY_EDITOR` 에서 `EmitToWeb` 호출
- `MapRuntime.Delete(string id)`: 마커가 있으면 `_markers`·`MapRefs`·`Placer.All` 에서 제거하고 GameObject 파괴(플레이 중이면 `Destroy`, 아니면 `DestroyImmediate`), 없으면 무시. 이벤트는 내지 않는다(웹이 먼저 DB 에서 지우고 Unity 에 알리는 흐름)
- `WebGLBuild.Build()` (메뉴 `ShipHdMap/Build WebGL`, 배치 `-executeMethod ShipHdMap.Editor.WebGLBuild.Build`): `PlayerSettings.WebGL.compressionFormat = Disabled`, `decompressionFallback = false`, 씬 `Assets/Scenes/Demo.unity`, 출력 `<repo>/web/public/unity` (경로는 `Application.dataPath` 기준 상대 계산), `BuildTarget.WebGL`. 결과 파일: `web/public/unity/Build/unity.loader.js`, `unity.data`, `unity.framework.js`, `unity.wasm`
- 산출물 이름은 출력 폴더의 마지막 세그먼트(`unity`)를 따른다 — React 쪽 URL 이 여기에 맞춘다

- [ ] **Step 1: 실패하는 테스트**

`MapRuntimeTests.cs` 에 추가:

```csharp
        [Test]
        public void DeleteRemovesMarkerAndMapRef()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture());
            int before = rt.LandmarksRoot.childCount;
            rt.Delete("LM-0003");
            Assert.That(rt.MapRefs.ContainsKey("LM-0003"), Is.False);
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(before - 1));
            rt.Delete("LM-NOPE"); // no throw
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(before - 1));
        }
```

- [ ] **Step 2: 실패 확인** (에디터 GUI 닫고)

```bash
cd unity && U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
"$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"; echo EXIT=$?
grep "error CS" Logs/editmode.log | head -3
```

Expected: `error CS1061 ... 'Delete'`.

- [ ] **Step 3: 구현**

`ShipHdMapBridge.jslib`:

```javascript
mergeInto(LibraryManager.library, {
  EmitToWeb: function (name, json) {
    if (typeof window !== "undefined" && typeof window.dispatchReactUnityEvent === "function") {
      window.dispatchReactUnityEvent(UTF8ToString(name), UTF8ToString(json));
    }
  },
});
```

`MapRuntime.cs` 변경 (기존 코드에 추가):

```csharp
using System.Runtime.InteropServices;
// ...
#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")] static extern void EmitToWeb(string name, string json);
#endif

        void Send(string name, string json)
        {
            Emit?.Invoke(name, json);
#if UNITY_WEBGL && !UNITY_EDITOR
            EmitToWeb(name, json);
#endif
        }

        /// Web deleted the feature in the DB first; remove the scene marker without emitting.
        public void Delete(string id)
        {
            if (!_markers.TryGetValue(id, out var m)) return;
            _markers.Remove(id); MapRefs.Remove(id); Placer.All.Remove(m);
            if (m) { if (Application.isPlaying) Destroy(m.gameObject); else DestroyImmediate(m.gameObject); }
        }
```

`WebGLBuild.cs`:

```csharp
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShipHdMap.Editor
{
    public static class WebGLBuild
    {
        [MenuItem("ShipHdMap/Build WebGL")]
        public static void Build()
        {
            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "web", "public", "unity"));
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled; // dev server serves files as-is
            PlayerSettings.WebGL.decompressionFallback = false;
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/Demo.unity" },
                locationPathName = outDir,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            Debug.Log($"[WebGLBuild] {report.summary.result} -> {outDir} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalTime})");
            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded && Application.isBatchMode) EditorApplication.Exit(1);
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Step 2 명령. Expected: `total="40" passed="40" failed="0"` (39 + 1). 스크립트 컴파일에 `Plugins/WebGL/*.jslib` 는 관여하지 않는다.

- [ ] **Step 5: WebGL 빌드 (에디터 GUI 닫고, 10분 안팎)**

```bash
cd unity && "$U" -batchmode -nographics -projectPath "$PWD" -executeMethod ShipHdMap.Editor.WebGLBuild.Build -quit -logFile "$PWD/Logs/webgl-build.log"; echo EXIT=$?
grep -E "\[WebGLBuild\]|error CS|Build Failed" Logs/webgl-build.log | head -5
ls -la ../web/public/unity/Build/
```

Expected: `EXIT=0`, `[WebGLBuild] Succeeded`, `Build/unity.loader.js`, `unity.data`, `unity.framework.js`, `unity.wasm` (수십 MB, gitignore). `web/public/unity/index.html` 도 생기지만 쓰지 않는다. WebGL 모듈이 없다는 오류가 나면 `ls /Applications/Unity/Hub/Editor/6000.3.24f1/PlaybackEngines/WebGLSupport` 확인.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Plugins unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs unity/Assets/ShipHdMap/Editor/WebGLBuild.cs unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs unity/ProjectSettings/ProjectSettings.asset
git commit -m "unity: jslib bridge to React, Delete message, WebGL build method"
```

(`ProjectSettings.asset` 은 압축 설정 변경으로 바뀐다. `.meta` 포함.)

---

### Task 3: web 골격과 `shipFrame.ts`

**Files:**
- Create: `web/` (Vite react-ts 템플릿) — `package.json`, `vite.config.ts`, `tsconfig*.json`, `index.html`, `src/main.tsx`, `src/App.tsx`, `src/App.css`
- Create: `web/src/geo/shipFrame.ts`, `web/src/geo/shipFrame.test.ts`

**Interfaces:**
- `shipFrame.ts`: `wrapDeg(a)`, `wrapRad(a)`, `toUnity(x, y, z): [number, number, number]` = `[x, z, -y]`, `unityYawDeg(psi) = psi`, `toWgs84(g: Georef, x, y): {lat, lon}` (Java 와 같은 식: `e = x sin h − y cos h`, `n = x cos h + y sin h`), `type Georef = {ap_lat, ap_lon, heading_deg}`
- Vite: `server.proxy = { "/api": "http://localhost:8081" }`; 테스트는 Vitest(`pnpm test`), `environment: "jsdom"` 은 컴포넌트 테스트가 없으므로 `node`

- [ ] **Step 1: 템플릿 생성과 의존성**

```bash
cd /path/to/repo && pnpm create vite@latest web --template react-ts   # 프롬프트가 나오면 기본값
cd web && pnpm add zustand@5 react-unity-webgl@10 && pnpm add -D vitest@latest
```

`package.json` `scripts` 에 `"test": "vitest run"` 추가. `vite.config.ts`:

```ts
import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

export default defineConfig({
  plugins: [react()],
  server: { proxy: { "/api": "http://localhost:8081" } },
  test: { environment: "node" },
});
```

(`test` 키 타입 오류가 나면 `import { defineConfig } from "vitest/config"` 로 바꾼다.)

- [ ] **Step 2: 실패하는 테스트**

`src/geo/shipFrame.test.ts`:

```ts
import { describe, it, expect } from "vitest";
import { readFileSync } from "node:fs";
import { resolve } from "node:path";
import { wrapDeg, wrapRad, toUnity, unityYawDeg, toWgs84 } from "./shipFrame";

const vectors = JSON.parse(readFileSync(resolve(__dirname, "../../../docs/test-vectors/ship-frame.json"), "utf8"));

describe("shipFrame (shared vectors)", () => {
  it("points map to unity", () => {
    for (const p of vectors.points) expect(toUnity(p.ship[0], p.ship[1], p.ship[2])).toEqual(p.unity);
  });
  it("headings", () => {
    for (const h of vectors.headings) expect(unityYawDeg(h.heading_deg)).toBe(h.unity_yaw_deg);
  });
  it("wrap", () => {
    for (const w of vectors.wrap_deg) expect(wrapDeg(w.in)).toBeCloseTo(w.out, 9);
    expect(wrapRad(3 * Math.PI)).toBeCloseTo(Math.PI, 12);
    expect(wrapRad(-Math.PI)).toBeCloseTo(Math.PI, 12);
  });
  it("wgs84 planar approximation", () => {
    for (const c of vectors.wgs84) {
      const { lat, lon } = toWgs84(c.georef, c.ship[0], c.ship[1]);
      expect(lat).toBeCloseTo(c.expect.lat, 7);
      expect(lon).toBeCloseTo(c.expect.lon, 7);
    }
  });
});
```

- [ ] **Step 3: 실패 확인** — `pnpm test`. Expected: `Cannot find module './shipFrame'` 류 실패.

- [ ] **Step 4: 구현**

`src/geo/shipFrame.ts`:

```ts
/** Ship Frame: x forward from AP, y port (+), z up, metres. Heading psi CCW from +x (deg).
 *  Georef.heading_deg is a different quantity: bow bearing clockwise from true north (deg). */
export type Georef = { ap_lat: number; ap_lon: number; heading_deg: number };

export const METRES_PER_DEG_LAT = 111_320;

/** (-180, 180] */
export function wrapDeg(a: number): number {
  a %= 360;
  if (a <= -180) a += 360; else if (a > 180) a -= 360;
  return a;
}

/** (-pi, pi] */
export function wrapRad(a: number): number {
  a %= 2 * Math.PI;
  if (a <= -Math.PI) a += 2 * Math.PI; else if (a > Math.PI) a -= 2 * Math.PI;
  return a;
}

/** Same as C#/Java: (x, y, z) -> (x, z, -y). */
export function toUnity(x: number, y: number, z: number): [number, number, number] { return [x, z, -y]; }

export function unityYawDeg(psiDeg: number): number { return psiDeg; }

export function toWgs84(g: Georef, x: number, y: number): { lat: number; lon: number } {
  const h = (g.heading_deg * Math.PI) / 180;
  const e = x * Math.sin(h) - y * Math.cos(h);
  const n = x * Math.cos(h) + y * Math.sin(h);
  return { lat: g.ap_lat + n / METRES_PER_DEG_LAT, lon: g.ap_lon + e / (METRES_PER_DEG_LAT * Math.cos((g.ap_lat * Math.PI) / 180)) };
}
```

- [ ] **Step 5: 통과 확인** — `pnpm test` → 4 passed. `pnpm build` 로 타입 오류 없음 확인(템플릿 App 은 그대로).

- [ ] **Step 6: Commit**

```bash
git add web
git commit -m "web: Vite React TS scaffold with API proxy; shipFrame with shared vectors"
```

`web/node_modules`, `web/dist`, `web/public/unity` 는 gitignore 로 제외돼야 한다(`git status` 로 확인).

---

### Task 4: 타입, API 클라이언트, Zustand 스토어

**Files:**
- Create: `web/src/api/types.ts`, `web/src/api/client.ts`, `web/src/store/editor.ts`
- Test: `web/src/api/client.test.ts`, `web/src/store/editor.test.ts`

**Interfaces:**
- `types.ts`:
  - `type Layer = "A1"|"A2"|"B2"|"C"|"LM"|"LP"|"MEP"`; `type Geometry = { type: "Point"|"LineString"|"Polygon"; coordinates: number[] | number[][] | number[][][] }`
  - `type Feature = { id: string; deck_id?: string; layer: Layer; kind: string; geometry: Geometry; props: Record<string, unknown>; created_at?: string; updated_at?: string }`
  - `type Deck = { id: string; name: string; z_surface: number; z_clear: number; movable: boolean; outline: number[][] }`
  - `type Dataset = { id: string; name: string; version: number; ap_lat: number; ap_lon: number; heading_deg: number; lpp_m: number }`
  - `type Pose = { draft_fwd_m?: number; draft_aft_m?: number; heel_deg?: number; heading_deg?: number; tide_m?: number; quay_z_m?: number; ap_lat?: number; ap_lon?: number; measured_at?: string; trim_deg?: number }`
  - `type RampState = { id: string; length_m: number; angle_deg: number; state: "deployed"|"blocked" }`
  - `type FeatureIn = { id?: string; deck_id?: string; layer: Layer; kind: string; geometry: Geometry; props?: Record<string, unknown> }`
- `client.ts` (`const BASE = "/api"`, `fetch` 래퍼 `req<T>(method, path, body?)` 가 4xx 면 `ApiError{status, message, field}` throw):
  - `getDataset(id)`, `listDecks(id)` — 갑판 목록은 `GET /datasets/{id}/vehicle-map` 의 `decks` 로 얻는다(M2 에 갑판 목록 엔드포인트가 없음; ETag 는 무시)
  - `listFeatures(id, {deck?, layer?})`, `createFeature(id, FeatureIn)`, `updateFeature(id, fid, Partial<FeatureIn>)`, `deleteFeature(id, fid)`
  - `getPose(id)`, `putPose(id, Partial<Pose>)`, `getRamp(id, rid)`, `vehicleMapUrl(id)`, `geojsonUrl(id)`
- `editor.ts` (Zustand `create<EditorState>()`):
  - state: `datasetId`, `dataset: Dataset|null`, `decks: Deck[]`, `features: Record<string, Feature>`, `drafts: Record<string, Draft>` (`Draft = { tempId, layer, deck_id, geometry, props }`), `selectedId: string|null`, `deckFilter: string` ("all" 기본), `mode: "edit"|"drive"`, `pose: Pose|null`, `ramp: RampState|null`, `localization: LocalizationEvt|null`, `error: string|null`
  - actions: `load(datasetId)` (dataset·decks·features·pose·ramp 를 API 로 채움), `select(id|null)`, `setDeckFilter(d)`, `setMode(m)`, `addDraft(evt: FeatureCreatedEvt)`, `applyDraft(tempId, patch)` → `createFeature` → `features` 에 넣고 draft 제거, `{tempId, id}` 반환, `updateFeature(id, patch)`, `removeFeature(id)`, `savePose(patch)`, `setLocalization(evt)`, `bumpVersion(v)`
  - selectors: `visibleFeatures(state)` (deckFilter 적용), `unsavedCount = Object.keys(drafts).length`
  - 스토어는 Unity 를 모른다. Unity 호출은 Task 5 의 훅이 스토어 액션 결과를 받아서 한다

- [ ] **Step 1: 실패하는 테스트**

`src/api/client.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from "vitest";
import { api, ApiError } from "./client";

function mockFetch(status: number, body: unknown) {
  return vi.fn(async () => new Response(JSON.stringify(body), { status, headers: { "content-type": "application/json" } }));
}

describe("api client", () => {
  beforeEach(() => vi.restoreAllMocks());

  it("lists features with query params", async () => {
    const f = mockFetch(200, [{ id: "LM-0001", layer: "LM", kind: "apriltag", geometry: { type: "Point", coordinates: [1, 2, 3] }, props: {} }]);
    vi.stubGlobal("fetch", f);
    const list = await api.listFeatures("ds1", { deck: "D3", layer: "LM" });
    expect(list[0].id).toBe("LM-0001");
    expect(f.mock.calls[0][0]).toBe("/api/datasets/ds1/features?deck=D3&layer=LM");
  });

  it("throws ApiError with field on 400", async () => {
    vi.stubGlobal("fetch", mockFetch(400, { status: 400, error: "Bad Request", message: "layer must be one of", field: "layer" }));
    await expect(api.createFeature("ds1", { layer: "LM", kind: "x", geometry: { type: "Point", coordinates: [0, 0, 0] } }))
      .rejects.toMatchObject({ status: 400, field: "layer" } satisfies Partial<ApiError>);
  });

  it("delete returns void on 204", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => new Response(null, { status: 204 })));
    await expect(api.deleteFeature("ds1", "LM-0001")).resolves.toBeUndefined();
  });
});
```

`src/store/editor.test.ts`:

```ts
import { describe, it, expect, vi, beforeEach } from "vitest";
import { useEditorStore, visibleFeatures } from "./editor";
import { api } from "../api/client";

vi.mock("../api/client", () => ({
  api: {
    getDataset: vi.fn(async () => ({ id: "ds1", name: "d", version: 3, ap_lat: 0, ap_lon: 0, heading_deg: 0, lpp_m: 120 })),
    listDecks: vi.fn(async () => [{ id: "D3", name: "Deck 3", z_surface: 10.6, z_clear: 2.2, movable: false, outline: [] }]),
    listFeatures: vi.fn(async () => [
      { id: "LM-0001", deck_id: "D3", layer: "LM", kind: "apriltag", geometry: { type: "Point", coordinates: [12, -6.2, 11.8] }, props: { code: 1 } },
      { id: "A2-D1-0001", deck_id: "D1", layer: "A2", kind: "centerline", geometry: { type: "LineString", coordinates: [[2, 0, 5.4], [118, 0, 5.4]] }, props: {} },
    ]),
    getPose: vi.fn(async () => ({ draft_fwd_m: 8.1, draft_aft_m: 8.6, tide_m: 0, trim_deg: 0.24 })),
    getRamp: vi.fn(async () => ({ id: "RAMP-STERN", length_m: 30, angle_deg: 2.9, state: "deployed" })),
    createFeature: vi.fn(async (_ds: string, f: { layer: string }) => ({ id: "LM-0020", deck_id: "D3", layer: f.layer, kind: "apriltag", geometry: { type: "Point", coordinates: [84, -6.2, 11.8] }, props: { code: 7 } })),
    updateFeature: vi.fn(async (_ds: string, id: string, patch: object) => ({ id, deck_id: "D3", layer: "LM", kind: "apriltag", geometry: { type: "Point", coordinates: [12, -6.2, 11.8] }, props: { code: 9 }, ...patch })),
    deleteFeature: vi.fn(async () => undefined),
    putPose: vi.fn(async (_ds: string, p: object) => ({ draft_fwd_m: 8.1, draft_aft_m: 8.6, tide_m: 1.2, trim_deg: 0.24, ...p })),
  },
}));

describe("editor store", () => {
  beforeEach(() => useEditorStore.setState(useEditorStore.getInitialState()));

  it("load fills dataset, decks, features, pose, ramp", async () => {
    await useEditorStore.getState().load("ds1");
    const s = useEditorStore.getState();
    expect(s.dataset?.version).toBe(3);
    expect(s.decks.map((d) => d.id)).toEqual(["D3"]);
    expect(Object.keys(s.features)).toEqual(["LM-0001", "A2-D1-0001"]);
    expect(s.pose?.tide_m).toBe(0);
    expect(s.ramp?.state).toBe("deployed");
  });

  it("deck filter selects visible features", async () => {
    await useEditorStore.getState().load("ds1");
    useEditorStore.getState().setDeckFilter("D3");
    expect(visibleFeatures(useEditorStore.getState()).map((f) => f.id)).toEqual(["LM-0001"]);
    useEditorStore.getState().setDeckFilter("all");
    expect(visibleFeatures(useEditorStore.getState())).toHaveLength(2);
  });

  it("draft -> apply creates a feature and returns the id mapping", async () => {
    await useEditorStore.getState().load("ds1");
    useEditorStore.getState().addDraft({ tempId: "LM-0002", layer: "LM", x: 84, y: -6.2, z: 11.8, deck: "D3" });
    expect(useEditorStore.getState().unsavedCount()).toBe(1);
    expect(useEditorStore.getState().selectedId).toBe("LM-0002");
    const map = await useEditorStore.getState().applyDraft("LM-0002", { kind: "apriltag", props: { code: 7 } });
    expect(map).toEqual({ tempId: "LM-0002", id: "LM-0020" });
    expect(useEditorStore.getState().features["LM-0020"].props.code).toBe(7);
    expect(useEditorStore.getState().unsavedCount()).toBe(0);
    expect(useEditorStore.getState().selectedId).toBe("LM-0020");
    expect(api.createFeature).toHaveBeenCalledWith("ds1", expect.objectContaining({ layer: "LM", deck_id: "D3", geometry: { type: "Point", coordinates: [84, -6.2, 11.8] } }));
  });

  it("update and remove", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().updateFeature("LM-0001", { props: { code: 9 } });
    expect(useEditorStore.getState().features["LM-0001"].props.code).toBe(9);
    useEditorStore.getState().select("LM-0001");
    await useEditorStore.getState().removeFeature("LM-0001");
    expect(useEditorStore.getState().features["LM-0001"]).toBeUndefined();
    expect(useEditorStore.getState().selectedId).toBeNull();
  });

  it("savePose merges the response", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().savePose({ tide_m: 1.2 });
    expect(useEditorStore.getState().pose?.tide_m).toBe(1.2);
    expect(api.putPose).toHaveBeenCalledWith("ds1", { tide_m: 1.2 });
  });
});
```

- [ ] **Step 2: 실패 확인** — `pnpm test`. Expected: 모듈 없음 실패.

- [ ] **Step 3: 구현**

`src/api/types.ts`:

```ts
export type Layer = "A1" | "A2" | "B2" | "C" | "LM" | "LP" | "MEP";
export type Geometry = { type: "Point" | "LineString" | "Polygon"; coordinates: number[] | number[][] | number[][][] };
export type Feature = { id: string; deck_id?: string; layer: Layer; kind: string; geometry: Geometry; props: Record<string, unknown>; created_at?: string; updated_at?: string };
export type FeatureIn = { id?: string; deck_id?: string; layer: Layer; kind: string; geometry: Geometry; props?: Record<string, unknown> };
export type Deck = { id: string; name: string; z_surface: number; z_clear: number; movable: boolean; outline: number[][] };
export type Dataset = { id: string; name: string; ship_name?: string; version: number; ap_lat: number; ap_lon: number; heading_deg: number; lpp_m: number };
export type Pose = { draft_fwd_m?: number; draft_aft_m?: number; heel_deg?: number; heading_deg?: number; tide_m?: number; quay_z_m?: number; ap_lat?: number; ap_lon?: number; measured_at?: string; trim_deg?: number };
export type RampState = { id: string; length_m: number; width_m?: number; angle_deg: number; state: "deployed" | "blocked"; connects_lane?: string };
/** Unity -> React events (spec §10). */
export type FeatureCreatedEvt = { tempId: string; layer: Layer; x: number; y: number; z: number; deck: string };
export type LocalizationEvt = { est_x: number; est_y: number; est_psi: number; true_x: number; true_y: number; true_psi: number; residual_rms: number; n_obs: number; frame: string };
```

`src/api/client.ts`:

```ts
import type { Dataset, Deck, Feature, FeatureIn, Pose, RampState } from "./types";

const BASE = "/api";

export class ApiError extends Error {
  constructor(public status: number, message: string, public field?: string) { super(message); }
}

async function req<T>(method: string, path: string, body?: unknown): Promise<T> {
  const res = await fetch(BASE + path, { method, headers: body === undefined ? {} : { "content-type": "application/json" }, body: body === undefined ? undefined : JSON.stringify(body) });
  if (res.status === 204) return undefined as T;
  const text = await res.text();
  const json = text ? JSON.parse(text) : null;
  if (!res.ok) throw new ApiError(res.status, json?.message ?? res.statusText, json?.field);
  return json as T;
}

const q = (params: Record<string, string | undefined>) => {
  const s = new URLSearchParams(Object.entries(params).filter(([, v]) => v !== undefined) as [string, string][]).toString();
  return s ? "?" + s : "";
};

export const api = {
  getDataset: (ds: string) => req<Dataset>("GET", `/datasets/${ds}`),
  /** M2 has no deck endpoint; decks come from the vehicle map. */
  listDecks: async (ds: string) => (await req<{ decks: Deck[] }>("GET", `/datasets/${ds}/vehicle-map`)).decks,
  listFeatures: (ds: string, f: { deck?: string; layer?: string } = {}) => req<Feature[]>("GET", `/datasets/${ds}/features${q(f)}`),
  createFeature: (ds: string, f: FeatureIn) => req<Feature>("POST", `/datasets/${ds}/features`, f),
  updateFeature: (ds: string, id: string, patch: Partial<FeatureIn>) => req<Feature>("PUT", `/datasets/${ds}/features/${id}`, patch),
  deleteFeature: (ds: string, id: string) => req<void>("DELETE", `/datasets/${ds}/features/${id}`),
  getPose: (ds: string) => req<Pose>("GET", `/datasets/${ds}/pose`),
  putPose: (ds: string, p: Partial<Pose>) => req<Pose>("PUT", `/datasets/${ds}/pose`, p),
  getRamp: (ds: string, rid: string) => req<RampState>("GET", `/datasets/${ds}/ramps/${rid}`),
  vehicleMapUrl: (ds: string) => `${BASE}/datasets/${ds}/vehicle-map`,
  geojsonUrl: (ds: string) => `${BASE}/datasets/${ds}/export.geojson`,
};
```

`src/store/editor.ts`:

```ts
import { create } from "zustand";
import { api } from "../api/client";
import type { Dataset, Deck, Feature, FeatureCreatedEvt, FeatureIn, Geometry, Layer, LocalizationEvt, Pose, RampState } from "../api/types";

export type Draft = { tempId: string; layer: Layer; deck_id: string; geometry: Geometry; props: Record<string, unknown> };
export type Mode = "edit" | "drive";

export type EditorState = {
  datasetId: string; dataset: Dataset | null; decks: Deck[]; features: Record<string, Feature>; drafts: Record<string, Draft>;
  selectedId: string | null; deckFilter: string; mode: Mode; pose: Pose | null; ramp: RampState | null; localization: LocalizationEvt | null; error: string | null;
  load: (datasetId: string) => Promise<void>;
  select: (id: string | null) => void;
  setDeckFilter: (d: string) => void;
  setMode: (m: Mode) => void;
  addDraft: (e: FeatureCreatedEvt) => void;
  applyDraft: (tempId: string, patch: { kind: string; props?: Record<string, unknown>; deck_id?: string }) => Promise<{ tempId: string; id: string }>;
  updateFeature: (id: string, patch: Partial<FeatureIn>) => Promise<void>;
  removeFeature: (id: string) => Promise<void>;
  savePose: (patch: Partial<Pose>) => Promise<void>;
  setLocalization: (e: LocalizationEvt | null) => void;
  bumpVersion: (v: number) => void;
  unsavedCount: () => number;
};

const RAMP_ID = "RAMP-STERN";

export const useEditorStore = create<EditorState>()((set, get) => ({
  datasetId: "roro-demo-01", dataset: null, decks: [], features: {}, drafts: {}, selectedId: null, deckFilter: "all", mode: "edit",
  pose: null, ramp: null, localization: null, error: null,

  async load(datasetId) {
    try {
      const [dataset, decks, list, pose] = await Promise.all([api.getDataset(datasetId), api.listDecks(datasetId), api.listFeatures(datasetId), api.getPose(datasetId)]);
      const ramp = await api.getRamp(datasetId, RAMP_ID).catch(() => null);
      set({ datasetId, dataset, decks, features: Object.fromEntries(list.map((f) => [f.id, f])), pose, ramp, error: null });
    } catch (e) { set({ error: (e as Error).message }); }
  },
  select: (id) => set({ selectedId: id }),
  setDeckFilter: (deckFilter) => set({ deckFilter }),
  setMode: (mode) => set({ mode }),
  addDraft: (e) => set((s) => ({
    drafts: { ...s.drafts, [e.tempId]: { tempId: e.tempId, layer: e.layer, deck_id: e.deck, geometry: { type: "Point", coordinates: [e.x, e.y, e.z] }, props: {} } },
    selectedId: e.tempId,
  })),
  async applyDraft(tempId, patch) {
    const d = get().drafts[tempId]; if (!d) throw new Error("no draft " + tempId);
    const created = await api.createFeature(get().datasetId, { layer: d.layer, deck_id: patch.deck_id ?? d.deck_id, kind: patch.kind, geometry: d.geometry, props: patch.props ?? d.props });
    set((s) => { const drafts = { ...s.drafts }; delete drafts[tempId]; return { drafts, features: { ...s.features, [created.id]: created }, selectedId: created.id }; });
    return { tempId, id: created.id };
  },
  async updateFeature(id, patch) {
    const f = await api.updateFeature(get().datasetId, id, patch);
    set((s) => ({ features: { ...s.features, [id]: f } }));
  },
  async removeFeature(id) {
    await api.deleteFeature(get().datasetId, id);
    set((s) => { const features = { ...s.features }; delete features[id]; return { features, selectedId: s.selectedId === id ? null : s.selectedId }; });
  },
  async savePose(patch) {
    const pose = await api.putPose(get().datasetId, patch);
    const ramp = await api.getRamp(get().datasetId, RAMP_ID).catch(() => get().ramp);
    set({ pose, ramp });
  },
  setLocalization: (localization) => set({ localization }),
  bumpVersion: (v) => set((s) => (s.dataset ? { dataset: { ...s.dataset, version: v } } : {})),
  unsavedCount: () => Object.keys(get().drafts).length,
}));

export function visibleFeatures(s: EditorState): Feature[] {
  const all = Object.values(s.features);
  return s.deckFilter === "all" ? all : all.filter((f) => f.deck_id === s.deckFilter);
}
```

- [ ] **Step 4: 통과 확인** — `pnpm test` → 12 passed (4 + 3 + 5). `pnpm build` 타입 통과. `getInitialState()` 가 없다는 타입 오류가 나면 Zustand 5 에서는 존재한다 — `zustand` 버전을 확인(`pnpm list zustand`).

- [ ] **Step 5: Commit**

```bash
git add web/src/api web/src/store
git commit -m "web: API client, types, editor store with tests"
```

---

### Task 5: Unity 브리지 훅과 앱 골격

**Files:**
- Create: `web/src/bridge/useShipUnity.ts`
- Modify: `web/src/App.tsx`, `web/src/App.css`, `web/index.html`
- Create: `web/src/components/TopBar.tsx`, `web/src/components/StatusBar.tsx`

**Interfaces:**
- `useShipUnity()` → `{ unityProvider, isLoaded, send: (name: BridgeName, payload?: string | object) => void }`. 내부: `useUnityContext({ loaderUrl: "/unity/Build/unity.loader.js", dataUrl: "/unity/Build/unity.data", frameworkUrl: "/unity/Build/unity.framework.js", codeUrl: "/unity/Build/unity.wasm" })`; `send` 는 객체면 `JSON.stringify` 해서 `sendMessage("Map", name, json)`
- 이벤트 → 스토어: `onFeatureCreated` → `addDraft(JSON.parse(json))`; `onSelected` → `select(id)`; `onLocalization` → `setLocalization(...)`; `onSeedReady` 는 무시(M2 시드가 이미 DB 에 있음)
- 로드 순서: `isLoaded` 가 true 가 되고 스토어 `features` 가 채워지면 한 번 `Load` 를 보낸다. 페이로드는 vehicle-map 과 같은 구조여야 하므로(스펙 §10) `Load` 는 **API 의 `GET vehicle-map` 본문을 그대로** 보낸다(`fetch(api.vehicleMapUrl(ds))`). 그 뒤 `SetMode("edit")`, `SetDeck(deckFilter)`
- `deckFilter` 변경 → `SetDeck`; `selectedId` 변경(사용자 클릭 기원) → `Select(id)`; `mode` 변경 → `SetMode`
- `App.tsx`: 3분할 그리드(좌 300px / 중 1fr / 우 340px) + 상단 TopBar + 하단 StatusBar. 중앙에 `<Unity unityProvider={unityProvider} style={{ width: "100%", height: "100%" }} />` 와 로딩 중 표시. 이 Task 에서는 좌·우 패널 자리에 placeholder 텍스트만 둔다(Task 6–8 에서 채움)
- `TopBar`: 제품명 `Ship HD Map Editor`, `편집`/`주행` 탭(스토어 `mode`), 데이터셋 id·version, `GeoJSON`·`차량지도` 링크(`api.geojsonUrl`, `api.vehicleMapUrl`, `target=_blank`)
- `StatusBar`: 갑판 필터, 선택 id, 미저장 초안 수, Unity 로드 상태, `error`

- [ ] **Step 1: 구현** (컴포넌트 테스트는 두지 않는다 — 스토어·클라이언트 테스트가 로직을 덮고, 화면은 Task 9 수동 검증)

`src/bridge/useShipUnity.ts`:

```ts
import { useCallback, useEffect, useRef } from "react";
import { useUnityContext } from "react-unity-webgl";
import { api } from "../api/client";
import { useEditorStore } from "../store/editor";

export type BridgeName = "Load" | "SetMode" | "SetDeck" | "Select" | "Confirm" | "Delete" | "SetNoise" | "StartScenario" | "SetPose";

const URLS = { loaderUrl: "/unity/Build/unity.loader.js", dataUrl: "/unity/Build/unity.data", frameworkUrl: "/unity/Build/unity.framework.js", codeUrl: "/unity/Build/unity.wasm" };

export function useShipUnity() {
  const { unityProvider, isLoaded, sendMessage, addEventListener, removeEventListener } = useUnityContext(URLS);
  const { datasetId, features, deckFilter, selectedId, mode, addDraft, select, setLocalization } = useEditorStore();
  const loadedOnce = useRef(false);

  const send = useCallback((name: BridgeName, payload?: string | object) => {
    if (!isLoaded) return;
    sendMessage("Map", name, payload === undefined ? "" : typeof payload === "string" ? payload : JSON.stringify(payload));
  }, [isLoaded, sendMessage]);

  // Unity -> store
  useEffect(() => {
    const onCreated = (json: string) => addDraft(JSON.parse(json));
    const onSelected = (json: string) => select(JSON.parse(json).id ?? null);
    const onLoc = (json: string) => setLocalization(JSON.parse(json));
    addEventListener("onFeatureCreated", onCreated); addEventListener("onSelected", onSelected); addEventListener("onLocalization", onLoc);
    return () => { removeEventListener("onFeatureCreated", onCreated); removeEventListener("onSelected", onSelected); removeEventListener("onLocalization", onLoc); };
  }, [addEventListener, removeEventListener, addDraft, select, setLocalization]);

  // initial Load: the vehicle-map body is exactly the Load payload (spec §10)
  useEffect(() => {
    if (!isLoaded || loadedOnce.current || Object.keys(features).length === 0) return;
    loadedOnce.current = true;
    fetch(api.vehicleMapUrl(datasetId)).then((r) => r.text()).then((json) => { send("Load", json); send("SetMode", mode); send("SetDeck", deckFilter); });
  }, [isLoaded, features, datasetId, mode, deckFilter, send]);

  useEffect(() => { if (loadedOnce.current) send("SetDeck", deckFilter); }, [deckFilter, send]);
  useEffect(() => { if (loadedOnce.current) send("SetMode", mode); }, [mode, send]);
  useEffect(() => { if (loadedOnce.current && selectedId) send("Select", selectedId); }, [selectedId, send]);

  return { unityProvider, isLoaded, send };
}
```

`Select` 는 Unity 가 `onSelected` 로 보낸 선택을 다시 되돌려 보내는 루프가 생기지만, `MapRuntime.Select` 는 `onSelected` 를 재송신하므로 한 번 더 왕복하고 멈춘다(같은 id 로 `set` 해도 상태 변화가 없어 effect 가 다시 돌지 않는다). Task 9 검증 항목에 넣는다.

`src/App.tsx`:

```tsx
import { useEffect } from "react";
import { Unity } from "react-unity-webgl";
import { useShipUnity } from "./bridge/useShipUnity";
import { useEditorStore } from "./store/editor";
import { TopBar } from "./components/TopBar";
import { StatusBar } from "./components/StatusBar";
import "./App.css";

export default function App() {
  const load = useEditorStore((s) => s.load);
  const datasetId = useEditorStore((s) => s.datasetId);
  const { unityProvider, isLoaded, send } = useShipUnity();
  useEffect(() => { void load(datasetId); }, [load, datasetId]);
  return (
    <div className="app">
      <TopBar />
      <aside className="left">left panel (Task 6)</aside>
      <main className="center">
        {!isLoaded && <div className="loading">Unity 로딩 중…</div>}
        <Unity unityProvider={unityProvider} style={{ width: "100%", height: "100%" }} />
      </main>
      <aside className="right">right panel (Task 7–8)</aside>
      <StatusBar unityLoaded={isLoaded} />
      {/* send is threaded to panels in later tasks */}
      <span hidden>{typeof send}</span>
    </div>
  );
}
```

`src/App.css`:

```css
* { box-sizing: border-box; }
html, body, #root { height: 100%; margin: 0; font: 13px/1.4 system-ui, sans-serif; background: #f4f5f7; color: #222; }
.app { height: 100%; display: grid; grid-template-columns: 300px 1fr 340px; grid-template-rows: 40px 1fr 28px; grid-template-areas: "top top top" "left center right" "status status status"; }
.topbar { grid-area: top; display: flex; align-items: center; gap: 12px; padding: 0 12px; background: #2b2f36; color: #eee; }
.topbar .tab { padding: 3px 10px; border-radius: 4px; background: #444; cursor: pointer; }
.topbar .tab.on { background: #1e88e5; }
.topbar .spacer { flex: 1; }
.topbar a { color: #9cf; }
.left { grid-area: left; overflow: auto; border-right: 1px solid #ccc; background: #fff; }
.center { grid-area: center; position: relative; background: #111; }
.center .loading { position: absolute; inset: 0; display: grid; place-items: center; color: #ccc; }
.right { grid-area: right; overflow: auto; border-left: 1px solid #ccc; background: #fff; }
.statusbar { grid-area: status; display: flex; gap: 16px; align-items: center; padding: 0 12px; background: #eee; border-top: 1px solid #ccc; color: #555; }
.panel { padding: 8px 10px; border-bottom: 1px solid #e3e3e3; }
.panel h4 { margin: 0 0 6px; font-size: 11px; text-transform: uppercase; letter-spacing: .04em; color: #666; }
.row { display: flex; gap: 6px; align-items: center; margin: 3px 0; }
.row label { width: 84px; color: #666; }
.row input, .row select { flex: 1; }
.btn { border: 1px solid #888; border-radius: 3px; padding: 2px 8px; background: #fff; cursor: pointer; }
.btn.primary { background: #1e88e5; color: #fff; border-color: #1e88e5; }
.tree li { list-style: none; padding: 1px 4px; cursor: pointer; }
.tree li.sel { background: #e3f2fd; }
.tree ul { margin: 0; padding-left: 14px; }
.decktabs span { display: inline-block; border: 1px solid #999; border-radius: 3px; padding: 1px 8px; margin: 2px; cursor: pointer; }
.decktabs span.on { background: #1e88e5; color: #fff; border-color: #1e88e5; }
.err { color: #c62828; }
```

`src/components/TopBar.tsx`:

```tsx
import { api } from "../api/client";
import { useEditorStore } from "../store/editor";

export function TopBar() {
  const { dataset, datasetId, mode, setMode } = useEditorStore();
  return (
    <header className="topbar">
      <strong>Ship HD Map Editor</strong>
      <span className={"tab" + (mode === "edit" ? " on" : "")} onClick={() => setMode("edit")}>편집</span>
      <span className={"tab" + (mode === "drive" ? " on" : "")} onClick={() => setMode("drive")}>주행</span>
      <span className="spacer" />
      <span>{datasetId} · v{dataset?.version ?? "-"}</span>
      <a href={api.geojsonUrl(datasetId)} target="_blank" rel="noreferrer">GeoJSON</a>
      <a href={api.vehicleMapUrl(datasetId)} target="_blank" rel="noreferrer">차량지도</a>
    </header>
  );
}
```

`src/components/StatusBar.tsx`:

```tsx
import { useEditorStore } from "../store/editor";

export function StatusBar({ unityLoaded }: { unityLoaded: boolean }) {
  const { deckFilter, selectedId, error } = useEditorStore();
  const unsaved = useEditorStore((s) => Object.keys(s.drafts).length);
  return (
    <footer className="statusbar">
      <span>갑판 {deckFilter}</span>
      <span>선택 {selectedId ?? "-"}</span>
      <span>미저장 초안 {unsaved}</span>
      <span>Unity {unityLoaded ? "연결됨" : "로딩"}</span>
      {error && <span className="err">{error}</span>}
    </footer>
  );
}
```

`index.html` 의 `<title>` 을 `Ship HD Map Editor` 로.

- [ ] **Step 2: 확인**

```bash
cd web && pnpm build 2>&1 | tail -3 && pnpm test 2>&1 | tail -3
```

Expected: 빌드 성공(경고 없음), 테스트 12 passed. 브라우저 확인은 Task 9 에서 한 번에.

- [ ] **Step 3: Commit**

```bash
git add web/src web/index.html
git commit -m "web: Unity bridge hook, app layout, top and status bars"
```

---

### Task 6: 갑판 탭, 레이어 트리, SVG 미니맵

**Files:**
- Create: `web/src/components/DeckTabs.tsx`, `LayerTree.tsx`, `MiniMap.tsx`
- Modify: `web/src/App.tsx` (좌측 패널 채움)

**Interfaces:**
- `DeckTabs`: 스토어 `decks` 로 `D1…Dn` + `전체` 버튼, `deckFilter` 표시·변경
- `LayerTree`: `visibleFeatures` 를 layer 별로 그룹(순서 A2, A1, B2, LP, C, LM, MEP), 그룹 헤더에 건수, 항목 클릭 → `select(id)`. 초안(`drafts`)은 LM 그룹 맨 위에 `(초안)` 표시. LP 는 건수만 보이고 항목은 접힌 상태(49개 이상이므로)
- `MiniMap`: SVG `viewBox` 를 갑판 윤곽(선택 갑판, 전체면 가장 큰 갑판)의 bbox 로 잡고 **y 반전**(`transform="scale(1,-1)"` + translate). 그리는 것: 갑판 윤곽(선), A2 차로(파선), B2 폴리곤(반투명), C 기둥(작은 사각형), LM 점(빨강 마름모), 선택 피처는 굵은 파란 외곽선. 클릭 → `select(id)`. 좌표 헬퍼 `toXY(coord: number[]) → [x, -y]` 는 파일 안의 순수 함수로 export 해 테스트한다

- [ ] **Step 1: 실패하는 테스트**

`src/components/MiniMap.test.ts` (순수 함수만):

```ts
import { describe, it, expect } from "vitest";
import { bbox, ringPath } from "./MiniMap";

describe("minimap geometry", () => {
  it("bbox of a closed ring with margin", () => {
    const b = bbox([[0, -12, 10.6], [120, -12, 10.6], [120, 12, 10.6], [0, 12, 10.6], [0, -12, 10.6]], 2);
    expect(b).toEqual({ x: -2, y: -14, w: 124, h: 28 });
  });
  it("ring path flips y", () => {
    expect(ringPath([[0, 0, 1], [10, 0, 1], [10, 5, 1]])).toBe("M0,0 L10,0 L10,-5 Z");
  });
});
```

- [ ] **Step 2: 실패 확인** — `pnpm test` → 모듈 없음.

- [ ] **Step 3: 구현**

`src/components/MiniMap.tsx`:

```tsx
import { useEditorStore, visibleFeatures } from "../store/editor";
import type { Feature } from "../api/types";

export function bbox(ring: number[][], margin = 0) {
  const xs = ring.map((p) => p[0]), ys = ring.map((p) => -p[1]);
  const x = Math.min(...xs) - margin, y = Math.min(...ys) - margin;
  return { x, y, w: Math.max(...xs) - Math.min(...xs) + 2 * margin, h: Math.max(...ys) - Math.min(...ys) + 2 * margin };
}
/** Ship y (port +) points up on screen, so SVG y = -ship y. */
export function ringPath(pts: number[][]): string { return pts.map((p, i) => `${i ? "L" : "M"}${p[0]},${-p[1]}`).join(" ") + " Z"; }
export function linePath(pts: number[][]): string { return pts.map((p, i) => `${i ? "L" : "M"}${p[0]},${-p[1]}`).join(" "); }

export function MiniMap() {
  const s = useEditorStore();
  const decks = s.deckFilter === "all" ? s.decks : s.decks.filter((d) => d.id === s.deckFilter);
  const deck = decks[0] ?? s.decks[0];
  if (!deck) return <div className="panel"><h4>평면도</h4><span>갑판 없음</span></div>;
  const b = bbox(deck.outline, 3);
  const feats = visibleFeatures(s);
  const sel = s.selectedId;
  const mark = (f: Feature) => (f.id === sel ? { stroke: "#1e88e5", strokeWidth: 0.8 } : {});
  return (
    <div className="panel">
      <h4>{deck.name} 평면도</h4>
      <svg viewBox={`${b.x} ${b.y} ${b.w} ${b.h}`} style={{ width: "100%", background: "#f3f6f9" }}>
        {decks.map((d) => <path key={d.id} d={ringPath(d.outline)} fill="none" stroke="#7a8" strokeWidth={0.4} />)}
        {feats.filter((f) => f.layer === "A2").map((f) => <path key={f.id} d={linePath(f.geometry.coordinates as number[][])} fill="none" stroke="#1e88e5" strokeWidth={0.4} strokeDasharray="2 1" onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "B2").map((f) => <path key={f.id} d={ringPath((f.geometry.coordinates as number[][][])[0])} fill="rgba(30,136,229,.15)" stroke="#1e88e5" strokeWidth={0.2} onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "C" && f.geometry.type === "Polygon").map((f) => <path key={f.id} d={ringPath((f.geometry.coordinates as number[][][])[0])} fill="#999" stroke="#666" strokeWidth={0.2} onClick={() => s.select(f.id)} {...mark(f)} />)}
        {feats.filter((f) => f.layer === "LM").map((f) => { const c = f.geometry.coordinates as number[]; return <rect key={f.id} x={c[0] - 0.7} y={-c[1] - 0.7} width={1.4} height={1.4} transform={`rotate(45 ${c[0]} ${-c[1]})`} fill="#e53935" onClick={() => s.select(f.id)} {...mark(f)} />; })}
        {Object.values(s.drafts).map((d) => { const c = d.geometry.coordinates as number[]; return <circle key={d.tempId} cx={c[0]} cy={-c[1]} r={0.9} fill="none" stroke="#ff9800" strokeWidth={0.4} />; })}
      </svg>
    </div>
  );
}
```

`src/components/DeckTabs.tsx`:

```tsx
import { useEditorStore } from "../store/editor";

export function DeckTabs() {
  const { decks, deckFilter, setDeckFilter } = useEditorStore();
  const ids = [...decks.map((d) => d.id), "all"];
  return (
    <div className="panel decktabs">
      <h4>갑판</h4>
      {ids.map((id) => <span key={id} className={deckFilter === id ? "on" : ""} onClick={() => setDeckFilter(id)}>{id === "all" ? "전체" : id}</span>)}
    </div>
  );
}
```

`src/components/LayerTree.tsx`:

```tsx
import { useState } from "react";
import { useEditorStore, visibleFeatures } from "../store/editor";
import type { Layer } from "../api/types";

const ORDER: Layer[] = ["A2", "A1", "B2", "LP", "C", "LM", "MEP"];
const LABEL: Record<Layer, string> = { A2: "A2 차로중심선", A1: "A1 차선", B2: "B2 노면표시·구획", LP: "LP 래싱 포인트", C: "C 시설물", LM: "LM 랜드마크", MEP: "MEP" };

export function LayerTree() {
  const s = useEditorStore();
  const feats = visibleFeatures(s);
  const [open, setOpen] = useState<Record<string, boolean>>({ LM: true, A2: true, B2: true, C: true });
  return (
    <div className="panel tree">
      <h4>레이어 / 객체</h4>
      {ORDER.map((layer) => {
        const items = feats.filter((f) => f.layer === layer).sort((a, b) => a.id.localeCompare(b.id));
        const drafts = layer === "LM" ? Object.values(s.drafts) : [];
        if (items.length === 0 && drafts.length === 0) return null;
        return (
          <ul key={layer}>
            <li onClick={() => setOpen({ ...open, [layer]: !open[layer] })}>{open[layer] ? "▾" : "▸"} {LABEL[layer]} ({items.length}{drafts.length ? ` +${drafts.length} 초안` : ""})</li>
            {open[layer] && (
              <ul>
                {drafts.map((d) => <li key={d.tempId} className={s.selectedId === d.tempId ? "sel" : ""} onClick={() => s.select(d.tempId)}>{d.tempId} (초안)</li>)}
                {items.map((f) => <li key={f.id} className={s.selectedId === f.id ? "sel" : ""} onClick={() => s.select(f.id)}>{f.id} <small style={{ color: "#888" }}>{f.kind}{f.deck_id ? ` · ${f.deck_id}` : ""}</small></li>)}
              </ul>
            )}
          </ul>
        );
      })}
    </div>
  );
}
```

`App.tsx` 좌측: `<aside className="left"><DeckTabs /><LayerTree /><MiniMap /></aside>`.

- [ ] **Step 4: 통과 확인** — `pnpm test` → 14 passed, `pnpm build` 통과.

- [ ] **Step 5: Commit**

```bash
git add web/src
git commit -m "web: deck tabs, layer tree, SVG minimap"
```

---

### Task 7: 속성 폼 — 초안 적용, 수정, 삭제, Confirm/Delete 브리지

**Files:**
- Create: `web/src/components/PropertyForm.tsx`
- Modify: `web/src/App.tsx` (우측 패널에 배치, `send` 전달)

**Interfaces:**
- `PropertyForm({ send })`: 선택이 초안이면 `deck_id`(select), `kind`(기본 `apriltag`), 랜드마크 props(`family` 고정 `apriltag-36h11`, `code` 숫자, `size_m` 기본 0.3, `mounted_on` 텍스트, `normal` 은 읽기 전용 표시 — 초안엔 없으므로 `[0,1,0]`/`[0,-1,0]` 중 y 부호로 추정: 초안 y < 0 이면 `[0,1,0]`, 아니면 `[0,-1,0]`) 을 편집하고 **적용** → `applyDraft` → 결과 `{tempId, id}` 로 `send("Confirm", …)`
- 선택이 저장된 피처면 `deck_id`, `kind`, props(JSON 텍스트 영역, 랜드마크는 `code`/`size_m`/`mounted_on` 필드 + 나머지 JSON) 편집 → **적용** → `updateFeature(id, {deck_id, kind, props})`. `geometry` 는 읽기 전용 표시(이동은 M3b). **삭제** → `removeFeature(id)` 성공 후 `send("Delete", id)`; 409(구획이 참조하는 래싱)면 오류 메시지 표시
- 초안 **취소**: 스토어에 `discardDraft(tempId)` 액션 추가(드래프트 제거) + `send("Delete", tempId)` (Unity 에는 tempId 로 마커가 있음)
- 오류는 `ApiError.message`(+`field`)를 폼 위에 표시

- [ ] **Step 1: 실패하는 테스트** — `src/store/editor.test.ts` 에 추가:

```ts
  it("discardDraft removes the draft and clears selection", async () => {
    await useEditorStore.getState().load("ds1");
    useEditorStore.getState().addDraft({ tempId: "LM-0002", layer: "LM", x: 84, y: -6.2, z: 11.8, deck: "D3" });
    useEditorStore.getState().discardDraft("LM-0002");
    expect(useEditorStore.getState().unsavedCount()).toBe(0);
    expect(useEditorStore.getState().selectedId).toBeNull();
  });
```

- [ ] **Step 2: 실패 확인** — `pnpm test` → `discardDraft is not a function`.

- [ ] **Step 3: 구현**

스토어에 추가(타입과 구현):

```ts
  discardDraft: (tempId: string) => void;
  // ...
  discardDraft: (tempId) => set((s) => { const drafts = { ...s.drafts }; delete drafts[tempId]; return { drafts, selectedId: s.selectedId === tempId ? null : s.selectedId }; }),
```

`src/components/PropertyForm.tsx`:

```tsx
import { useEffect, useState } from "react";
import { ApiError } from "../api/client";
import { useEditorStore } from "../store/editor";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;

export function PropertyForm({ send }: { send: Send }) {
  const s = useEditorStore();
  const id = s.selectedId;
  const draft = id ? s.drafts[id] : undefined;
  const feat = id ? s.features[id] : undefined;
  const [deck, setDeck] = useState(""); const [kind, setKind] = useState(""); const [propsText, setPropsText] = useState("{}"); const [err, setErr] = useState<string | null>(null);

  useEffect(() => {
    setErr(null);
    if (draft) { setDeck(draft.deck_id); setKind("apriltag"); const y = (draft.geometry.coordinates as number[])[1];
      setPropsText(JSON.stringify({ family: "apriltag-36h11", code: nextCode(Object.values(s.features)), normal: y < 0 ? [0, 1, 0] : [0, -1, 0], size_m: 0.3, mounted_on: "" }, null, 1)); }
    else if (feat) { setDeck(feat.deck_id ?? ""); setKind(feat.kind); setPropsText(JSON.stringify(feat.props, null, 1)); }
  }, [id, draft, feat]); // eslint-disable-line react-hooks/exhaustive-deps

  if (!id) return <div className="panel"><h4>속성</h4><span style={{ color: "#888" }}>객체를 선택하거나 3D 에서 기둥 면을 클릭해 마커를 놓으세요</span></div>;

  const parseProps = () => { try { return JSON.parse(propsText) as Record<string, unknown>; } catch { throw new Error("props 는 JSON 이어야 합니다"); } };
  const run = async (fn: () => Promise<void>) => { try { setErr(null); await fn(); } catch (e) { setErr(e instanceof ApiError ? `${e.message}${e.field ? ` (${e.field})` : ""}` : (e as Error).message); } };

  return (
    <div className="panel">
      <h4>속성 — {id}{draft ? " (초안)" : ""}</h4>
      {err && <div className="err">{err}</div>}
      <div className="row"><label>갑판</label><select value={deck} onChange={(e) => setDeck(e.target.value)}><option value="">(없음)</option>{s.decks.map((d) => <option key={d.id} value={d.id}>{d.id}</option>)}</select></div>
      <div className="row"><label>kind</label><input value={kind} onChange={(e) => setKind(e.target.value)} /></div>
      <div className="row"><label>위치</label><span>{JSON.stringify((draft ?? feat)!.geometry.coordinates)}</span></div>
      <div className="row"><label>props</label><textarea rows={7} style={{ flex: 1, fontFamily: "monospace" }} value={propsText} onChange={(e) => setPropsText(e.target.value)} /></div>
      <div className="row">
        {draft ? (<>
          <button className="btn primary" onClick={() => run(async () => { const m = await s.applyDraft(id, { kind, deck_id: deck || undefined, props: parseProps() }); send("Confirm", m); })}>적용(저장)</button>
          <button className="btn" onClick={() => { s.discardDraft(id); send("Delete", id); }}>취소</button>
        </>) : (<>
          <button className="btn primary" onClick={() => run(() => s.updateFeature(id, { kind, deck_id: deck || undefined, props: parseProps() }))}>적용</button>
          <button className="btn" onClick={() => run(async () => { await s.removeFeature(id); send("Delete", id); })}>삭제</button>
        </>)}
      </div>
    </div>
  );
}

function nextCode(features: { layer: string; props: Record<string, unknown> }[]): number {
  const used = new Set(features.filter((f) => f.layer === "LM").map((f) => Number(f.props.code)));
  for (let c = 1; c < 20; c++) if (!used.has(c)) return c;
  return 0;
}
```

`App.tsx`: `useShipUnity` 의 `send` 를 `<PropertyForm send={send} />` 로 전달하고 우측 패널에 배치. (임시 `<span hidden>` 제거.)

- [ ] **Step 4: 통과 확인** — `pnpm test` → 15 passed, `pnpm build` 통과.

- [ ] **Step 5: Commit**

```bash
git add web/src
git commit -m "web: property form with draft apply, update, delete via API and bridge"
```

---

### Task 8: pose 패널과 최소 주행 패널

**Files:**
- Create: `web/src/components/PosePanel.tsx`, `web/src/components/DrivePanel.tsx`
- Modify: `web/src/App.tsx`

**Interfaces:**
- `PosePanel`: 슬라이더 4개 — 흘수 선수(6–10 m), 흘수 선미(6–10), 횡경사(−3–3°), 조위(−1–3 m). 변경이 멈추면(`onMouseUp`/`onKeyUp` 또는 300 ms 디바운스) `savePose({field: value})`. 표시: `trim_deg`, 램프 `angle_deg` 와 `state`(`deployed` 초록 / `blocked` 빨강). `SetPose` 는 M5 에서 Unity 에 연결하므로 지금은 보내지 않는다
- `DrivePanel({ send })` (mode === "drive" 일 때만 표시): 버튼 `선적 시나리오 시작` → `send("StartScenario", { mode: "load" })`; `정지` → `send("SetMode", "edit")` 후 `setMode("edit")`; 슬라이더 σ_r(0–1 m), σ_θ(0–5°), σ_α(0–10°) → `send("SetNoise", { sigma_r, sigma_theta, sigma_alpha, sigma_gps: 0.5 })`; 위치 추정 패널: 스토어 `localization` 의 N, RMS, est/true, 오차(m, deg — `wrapDeg` 사용), frame. 이벤트가 없으면 "추정 없음"
- App 우측: `mode === "edit"` 이면 `PropertyForm` + `PosePanel`, `drive` 면 `DrivePanel` + `PosePanel`

- [ ] **Step 1: 구현**

`src/components/PosePanel.tsx`:

```tsx
import { useEffect, useState } from "react";
import { useEditorStore } from "../store/editor";
import type { Pose } from "../api/types";

const FIELDS: { key: keyof Pose; label: string; min: number; max: number; step: number; unit: string }[] = [
  { key: "draft_fwd_m", label: "흘수 선수", min: 6, max: 10, step: 0.1, unit: "m" },
  { key: "draft_aft_m", label: "흘수 선미", min: 6, max: 10, step: 0.1, unit: "m" },
  { key: "heel_deg", label: "횡경사", min: -3, max: 3, step: 0.1, unit: "°" },
  { key: "tide_m", label: "조위", min: -1, max: 3, step: 0.1, unit: "m" },
];

export function PosePanel() {
  const { pose, ramp, savePose } = useEditorStore();
  const [local, setLocal] = useState<Pose>({});
  useEffect(() => { if (pose) setLocal(pose); }, [pose]);
  if (!pose) return null;
  const commit = (key: keyof Pose) => { const v = local[key]; if (typeof v === "number" && v !== pose[key]) void savePose({ [key]: v }); };
  return (
    <div className="panel">
      <h4>선박 자세 (pose)</h4>
      {FIELDS.map((f) => (
        <div className="row" key={f.key}>
          <label>{f.label}</label>
          <input type="range" min={f.min} max={f.max} step={f.step} value={(local[f.key] as number) ?? f.min}
            onChange={(e) => setLocal({ ...local, [f.key]: Number(e.target.value) })} onMouseUp={() => commit(f.key)} onKeyUp={() => commit(f.key)} onTouchEnd={() => commit(f.key)} />
          <span style={{ width: 52, textAlign: "right" }}>{(local[f.key] as number)?.toFixed(1)} {f.unit}</span>
        </div>
      ))}
      <div className="row"><label>트림</label><span>{pose.trim_deg?.toFixed(2)}°</span></div>
      <div className="row"><label>램프</label>
        {ramp ? <span>{ramp.angle_deg.toFixed(2)}° <b style={{ color: ramp.state === "deployed" ? "#2a2" : "#c62828" }}>{ramp.state}</b></span> : <span>-</span>}
      </div>
    </div>
  );
}
```

`src/components/DrivePanel.tsx`:

```tsx
import { useState } from "react";
import { wrapDeg } from "../geo/shipFrame";
import { useEditorStore } from "../store/editor";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;

export function DrivePanel({ send }: { send: Send }) {
  const { localization: l, setMode } = useEditorStore();
  const [sig, setSig] = useState({ sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2 });
  const noise = (patch: Partial<typeof sig>) => { const n = { ...sig, ...patch }; setSig(n); send("SetNoise", { ...n, sigma_gps: 0.5 }); };
  const err = l ? Math.hypot(l.est_x - l.true_x, l.est_y - l.true_y) : null;
  return (
    <div className="panel">
      <h4>주행 시뮬레이션</h4>
      <div className="row">
        <button className="btn primary" onClick={() => send("StartScenario", { mode: "load" })}>▶ 선적 시나리오</button>
        <button className="btn" onClick={() => { send("SetMode", "edit"); setMode("edit"); }}>정지</button>
      </div>
      <div className="row"><label>σ 거리 (m)</label><input type="range" min={0} max={1} step={0.05} value={sig.sigma_r} onChange={(e) => noise({ sigma_r: Number(e.target.value) })} /><span>{sig.sigma_r.toFixed(2)}</span></div>
      <div className="row"><label>σ 방위 (°)</label><input type="range" min={0} max={5} step={0.5} value={sig.sigma_theta} onChange={(e) => noise({ sigma_theta: Number(e.target.value) })} /><span>{sig.sigma_theta}</span></div>
      <div className="row"><label>σ 방향각 (°)</label><input type="range" min={0} max={10} step={0.5} value={sig.sigma_alpha} onChange={(e) => noise({ sigma_alpha: Number(e.target.value) })} /><span>{sig.sigma_alpha}</span></div>
      <h4 style={{ marginTop: 8 }}>위치 추정</h4>
      {!l ? <span style={{ color: "#888" }}>추정 없음</span> : (
        <div style={{ fontFamily: "monospace", fontSize: 12 }}>
          <div>frame {l.frame} · N {l.n_obs} · RMS {l.residual_rms.toFixed(3)}</div>
          <div>est  x {l.est_x.toFixed(2)} y {l.est_y.toFixed(2)} ψ {l.est_psi.toFixed(1)}</div>
          <div>true x {l.true_x.toFixed(2)} y {l.true_y.toFixed(2)} ψ {l.true_psi.toFixed(1)}</div>
          <div>err {err!.toFixed(2)} m · {wrapDeg(l.est_psi - l.true_psi).toFixed(1)}°</div>
        </div>
      )}
    </div>
  );
}
```

`App.tsx` 우측:

```tsx
      <aside className="right">
        {mode === "edit" ? <PropertyForm send={send} /> : <DrivePanel send={send} />}
        <PosePanel />
      </aside>
```

(`mode` 는 `useEditorStore((s) => s.mode)`.)

- [ ] **Step 2: 확인** — `pnpm build` 와 `pnpm test`(15 passed).

- [ ] **Step 3: Commit**

```bash
git add web/src
git commit -m "web: pose sliders with ramp state, minimal drive panel"
```

---

### Task 9: 개발 스크립트, README, 브라우저 검증

**Files:**
- Create: `scripts/m3-dev.sh`
- Modify: `README.md`

**Interfaces:**
- `scripts/m3-dev.sh`: compose up → `gradlew bootRun`(백그라운드, 로그 `api/build/bootrun.log`) → 준비 대기(`Started ApiApplication`) → 데이터 없으면 `POST /datasets` + 시드(픽스처) → `pnpm dev --host`(포그라운드). Ctrl-C 로 끝나면 트랩이 API 를 내린다(compose 는 유지)
- 검증 체크리스트(아래)는 컨트롤러가 Chrome 으로 수행한다; 구현자는 스크립트가 뜨고 첫 화면(Unity 로드, 트리 채움)까지 `curl` 로 확인한다

- [ ] **Step 1: 스크립트**

`scripts/m3-dev.sh`:

```bash
#!/usr/bin/env bash
# M3 dev: PostGIS (compose :5433) -> API (bootRun :8081) -> seed if empty -> Vite dev server (:5173, /api proxied)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="http://localhost:${API_PORT:-8081}/api"; DS="roro-demo-01"
export JAVA_HOME="${JAVA_HOME:-/opt/homebrew/opt/openjdk}"
mkdir -p "$ROOT/api/build"
docker compose -f "$ROOT/db/compose.yaml" up -d --wait
( cd "$ROOT/api" && ./gradlew bootRun --console=plain > "$ROOT/api/build/bootrun.log" 2>&1 ) &
BOOT_PID=$!
trap 'kill $BOOT_PID 2>/dev/null || true; pkill -f "com.shiphdmap.api.ApiApplication" 2>/dev/null || true' EXIT
for i in $(seq 1 90); do grep -q "Started ApiApplication" "$ROOT/api/build/bootrun.log" 2>/dev/null && break; sleep 2; done
if [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/datasets/$DS")" != "200" ]; then
  curl -s -o /dev/null -X POST "$API/datasets" -H 'content-type: application/json' -d "{\"id\":\"$DS\",\"name\":\"RORO demo\",\"ap_lat\":12.3456,\"ap_lon\":45.6789,\"heading_deg\":87.5}"
  curl -s -o /dev/null -X POST "$API/datasets/$DS/seed" -H 'content-type: application/json' --data-binary @"$ROOT/docs/fixtures/vehicle-map.sample.json"
  echo "seeded $DS"
fi
[ -f "$ROOT/web/public/unity/Build/unity.loader.js" ] || echo "WARN: no WebGL build at web/public/unity — run the Unity menu ShipHdMap/Build WebGL first"
cd "$ROOT/web" && pnpm install --frozen-lockfile >/dev/null && pnpm dev --host
```

`chmod +x scripts/m3-dev.sh`.

- [ ] **Step 2: 실행과 기본 확인**

```bash
./scripts/m3-dev.sh &     # 별도 터미널이 없으면 백그라운드
sleep 20; curl -s localhost:5173/ | grep -o "<title>[^<]*"; curl -s localhost:5173/api/datasets/roro-demo-01 | head -c 120; curl -s -o /dev/null -w '%{http_code}\n' localhost:5173/unity/Build/unity.loader.js
```

Expected: `<title>Ship HD Map Editor`, 데이터셋 JSON, `200`. 마치면 스크립트를 종료(`kill %1` 또는 Ctrl-C)하고 `pgrep -f ApiApplication || echo stopped`.

- [ ] **Step 3: README**

`## 구성` 의 `web/` 줄을 `- \`web/\` React 19 + Vite + TS + Zustand. Unity WebGL 을 품은 편집기: 갑판 트리·미니맵·속성 폼·pose·주행 패널. \`./scripts/m3-dev.sh\` 로 실행` 으로, `## 상태` 에 `- M3a 웹 편집기 완료 (브라우저 편집 → DB 반영, Vitest 15)` 와 `- 다음: M3b Unity 씬 UX` 로 갱신. 실행 절 추가:

```markdown
## 실행

- 준비: Docker, JDK 25(`JAVA_HOME=/opt/homebrew/opt/openjdk`), Node 22 + pnpm, Unity 6.3(WebGL 빌드 1회: 메뉴 `ShipHdMap/Build WebGL`)
- `./scripts/m3-dev.sh` → http://localhost:5173 (PostGIS 5433, API 8081)
- API 만: `./scripts/m2-smoke.sh`
```

- [ ] **Step 4: 브라우저 검증 체크리스트** (컨트롤러가 Chrome 으로; 구현자는 보고서에 "미수행" 표기)

1. 첫 화면: 상단 바에 `roro-demo-01 · v<n>`, 좌측 갑판 탭 D1·D2·D3·전체, 트리에 LM 19+ 개, 미니맵에 갑판 사각형과 마커 점, Unity 캔버스에 선박, 상태바 `Unity 연결됨`
2. `D3` 탭 → 트리·미니맵이 D3 만, Unity 의 D1·D2 반투명
3. 트리에서 `LM-0001` 클릭 → 미니맵 파란 외곽선, 상태바 `선택 LM-0001`, 속성 폼에 props
4. 속성 폼에서 `code` 를 바꿔 `적용` → 상단 version +1(새로고침 후 확인 또는 store `bumpVersion` — M3a 에서는 새로고침으로 확인)
5. Unity 캔버스에서 기둥 옆면 좌클릭 → 트리에 `(초안)`, 미니맵 주황 원, 폼 `적용(저장)` → 트리에 `LM-00xx` 로 바뀜, DB 조회로 확인(`curl .../features?layer=LM`)
6. `삭제` → 트리·미니맵·Unity 에서 사라짐. 래싱 포인트(LP) 하나를 골라 삭제하면 409 메시지
7. `주행` 탭 → `선적 시나리오` → 위치 추정 패널에 N·err 갱신, σ 슬라이더로 변화. `정지` → 편집 탭으로
8. pose 슬라이더 조위를 1.2 로 → 램프 `blocked`(빨강), 0 으로 → `deployed`

- [ ] **Step 5: 전체 테스트 + 민감 용어 검사 + Commit**

```bash
(cd web && pnpm test 2>&1 | tail -2) && (cd api && ./gradlew test --console=plain 2>&1 | grep -E "BUILD|FAILED")
grep -rn -I -f "${SENSITIVE_TERMS_FILE:?set to a private wordlist outside the repo}" web/src web/index.html scripts README.md docs/api-contract.md unity/Assets/Plugins unity/Assets/ShipHdMap || echo "sensitive scan: none"
git add scripts/m3-dev.sh README.md
git commit -m "web: dev script and README for the browser editor"
```

---

## M3a 완료 보고 형식

(1) 한 것 (2) 결정과 이유 (3) 검증 결과(Vitest·Gradle·EditMode 수, 브라우저 체크리스트 결과와 스크린샷) (4) 직접 확인 명령(`./scripts/m3-dev.sh`, 체크리스트) 순으로 보고하고 M3b 는 확인 후 시작한다.

## 자체 검토 기록

- 스펙 커버리지(M3a 행): 웹 셸·브리지·WebGL 빌드(T2·T3·T5), 갑판 탭·트리·미니맵·속성 폼·pose·상태바(T5–T8), 최소 주행 탭(T8), `Delete` 메시지(T2·T7), `docs/api-contract.md`(T1), M2 파킹 2건(T1), 완료 기준 = T9 체크리스트 4·5·7
- 이름 일관성: `BridgeName` 은 스펙 §10 + `Delete`; `send(name, payload)` 시그니처를 T5 가 정의하고 T7·T8 이 사용; 스토어 액션 `applyDraft`/`discardDraft`/`removeFeature`/`savePose`/`setLocalization` 이 T4·T7·T8 에서 같은 이름; `LocalizationEvt`/`FeatureCreatedEvt` 필드는 Unity `BridgeMessages.cs` 의 DTO 와 동일(snake_case 는 Unity 가 그대로 씀)
- 알려진 단순화: version 표시는 새로고침으로 갱신(쓰기 응답에 version 이 없음 — M4 에서 API 가 `X-Dataset-Version` 헤더를 주면 `bumpVersion` 연결); `Select` 왕복 1회; 컴포넌트 렌더 테스트 없음(수동 체크리스트로 대체); `SetPose` 미연결(M5)
- 환경 의존: WebGL 빌드에 10분 안팎, 에디터 GUI 닫아야 함; 5173/8081/5433 포트
