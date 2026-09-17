# M5a 갑판 주행·주차 판정·pose 기울임 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 선적 시나리오가 빈 구획을 `sequence_no` 순으로 연속 채우며(이탈 시점 추정 pose 로 경로를 짜 개루프 실행 → 실제 pose 로 tolerance 판정) `onSlotFilled` 마다 DB status 와 3D 색이 따라오고, 하역이 역순으로 비우며, pose 슬라이더로 선체가 기울어도 마커·차량·구획의 Ship Frame 좌표는 변하지 않는다.

**Architecture:** Unity 의 Map 루트(`MapRuntime.transform`) 로컬 좌표를 Ship Frame, 월드를 Quay Frame 으로 삼고 `SetPose` 는 루트 회전만 바꾼다(월드 좌표를 쓰던 자리 8곳을 로컬로). 시나리오는 `MapRuntime` 안의 상태기 `Idle → OnLane → Parking → Idle`(하역은 `Departing`)이며 순수 정적 `ScenarioPlanner`(다음 구획·이탈점·접근/출차 경로·판정)를 호출한다. 웹은 `onSlotFilled` 를 받아 기존 `PUT /slots/{sid}/status` 를 부르고 로그를 쌓을 뿐 재로드하지 않는다. API 변경 없음.

**Tech Stack:** Unity 6000.3.24f1(Built-in RP, UI Toolkit, EditMode NUnit, Newtonsoft); web: pnpm + Vite 8 + React 19 + TypeScript 6(`erasableSyntaxOnly`) + Zustand 5 + Vitest(node); api: Spring Boot 4.1 + Java 25 + Testcontainers(테스트 3곳 상수만 변경)

**Spec:** `docs/superpowers/specs/2026-09-16-m5a-drive-pose-design.md` (전체), 기본 스펙 `docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md` §3.2·§3.3·§4·§9.3·§10

## Global Constraints

- 좌표: Ship Frame(x 선수+, y 좌현+, z 상방+). Unity 는 `ShipFrame.ToUnity/ToShip` 만 쓰며 그 결과는 **Map 루트 로컬** 좌표다. 월드가 필요한 자리(레이캐스트 결과, 카메라)는 루트 `TransformPoint/InverseTransformPoint/TransformDirection/InverseTransformDirection` 으로 오간다
- pose 부호(스펙 §4.2): `trim_deg = atan((draft_aft − draft_fwd) / lpp)` 양수 = 선수 위, `heel_deg` 양수 = 우현(−y, Unity +z) 아래. 회전 원점 = Map 루트 원점. 적용 뒤 `Physics.SyncTransforms()`(autoSyncTransforms 꺼짐)
- 시나리오 상수: 이탈점 `x_exit = t.x − 2 − |t.y − y_lane|`(차로 시작점으로 클램프), 접근 경로 `[est, (t.x−2, t.y), t]`, 실행은 `(truth − est)` 평행이동 개루프, 접근·출차 속도 2 m/s, 차로 속도 `speed_limit_kmh / 3.6`. 판정: `|err_lat| ≤ lat_m && |err_lon| ≤ lon_m && |err_heading| ≤ heading_deg` → `filled`, 아니면 `needs_adjust`. tolerance 가 없으면 0.15/0.30/2
- 브리지(스펙 §6): `SetPose{draft_fwd_m, draft_aft_m, heel_deg, lpp_m, ramp?:{id, angle_deg, state}}`, `StartScenario{mode}`, `SetTimeScale{scale}`, `onSlotFilled{slot_id, status, err_lat?, err_lon?, err_heading?}`(하역은 `status:"empty"`, 오차 없음), `onScenario{event, mode?, slot_id?, detail?}`. 페이로드는 JSON 문자열, null 필드 생략
- 재생성은 status 를 초기화한다(정책 확정). 웹은 시나리오 중 `reloadScene` 을 부르지 않는다
- 응답 규약(absent = null), snake_case. Jackson 3(`tools.jackson.*`; 애노테이션은 `com.fasterxml.jackson.annotation.JsonProperty` 만)
- 포트: API 8081, PostGIS 5433, Vite 5173. WebGL 설정은 `WebGLBuild.cs` 안에서만. 산출물 `web/public/unity/` 는 gitignore
- 저장소는 public. 기관·회사·과제명·이력 표현, 사용자 계정명이 든 절대경로, 실제 항구 좌표 금지. 사설 단어목록은 `SENSITIVE_TERMS_FILE` 환경변수로만 참조
- 커밋은 각 Task 끝에. 푸시는 사용자 요청 시에만. Unity 배치 명령은 에디터 GUI 를 닫고 `unity/` 를 `-projectPath` 로, 순차 실행. `U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity`
- 테스트 목표: api 42 → 42(내용만 변경), Unity EditMode 53 → 81, web 22 → 26

---

## 파일 구조

```
unity/Assets/ShipHdMap/Editor/FixtureExporter.cs          (T1) 래싱 창 필터 제거, SeedLandmarks(seed) 로 좌표 유도
api/src/test/.../dataset/SeedImportTests.java             (T1) fixtureLashingCount 헬퍼, 49 제거
api/src/test/.../export/VehicleMapExportTests.java        (T1) 49 제거
api/src/test/.../export/GeoJsonExportTests.java           (T1) 49 제거
docs/fixtures/vehicle-map.sample.json                     (T1) 재생성
unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs   (T2, T4) SetPoseMsg·RampMsg / SetTimeScaleMsg·SlotFilledEvt·ScenarioEvt·상수
unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs       (T2, T4) SetPose·ApplyPose·AttachShip / 상태기·Step·NextVehicle·SetTimeScale
unity/Assets/ShipHdMap/Runtime/Ship/ShipMeshBuilder.cs    (T2) localPosition/localRotation
unity/Assets/ShipHdMap/Runtime/Ship/MapOverlay.cs         (T2, T4) LineRenderer 로컬 / SetStatus·SpawnParked·RemoveParked
unity/Assets/ShipHdMap/Runtime/Landmarks/LandmarkMarker.cs (T2) Spawn 월드 변환, ToModel 부모 기준
unity/Assets/ShipHdMap/Runtime/Landmarks/LandmarkPlacer.cs (T2) DeckIdForHeight 를 루트 로컬 y 로
unity/Assets/ShipHdMap/Runtime/Vehicle/HudView.cs         (T2) 커서·라벨 루트 기준, SetRamp
unity/Assets/ShipHdMap/Runtime/Vehicle/VehicleController.cs (T4) StartPath·Advance·AtEnd, Update 제거, local 좌표
unity/Assets/ShipHdMap/Runtime/Vehicle/ScenarioPlanner.cs (T3) 신규 순수 정적
unity/Assets/ShipHdMap/Editor/BridgeStubWindow.cs         (T2, T4) pose 슬라이더 / 하역·배율 버튼
unity/Assets/ShipHdMap/Tests/EditMode/FixtureExporterTests.cs (T1) 신규
unity/Assets/ShipHdMap/Tests/EditMode/PoseTests.cs        (T2) 신규
unity/Assets/ShipHdMap/Tests/EditMode/ScenarioPlannerTests.cs (T3) 신규
unity/Assets/ShipHdMap/Tests/EditMode/ScenarioRunTests.cs (T4) 신규
unity/Assets/ShipHdMap/Tests/EditMode/ScenarioIntegrationTests.cs (T4) 새 흐름으로 교체
web/src/api/types.ts, client.ts                           (T5) SlotStatus·SlotFilledEvt·ScenarioEvt·ScenarioLine, putSlotStatus
web/src/store/editor.ts, editor.test.ts                   (T5) scenarioLog·appendLog·clearLog·onSlotFilled·scenarioLine
web/src/bridge/useShipUnity.ts                            (T5) SetPose 효과, onSlotFilled·onScenario 리스너, edit 시 SetTimeScale 1
web/src/components/DrivePanel.tsx                         (T5) 선적·하역·정지·배율·로그
docs/api-contract.md, README.md                           (T6)
```

---

### Task 1: 픽스처 래싱 격자 확장과 좌표 유도

**Files:**
- Modify: `unity/Assets/ShipHdMap/Editor/FixtureExporter.cs`
- Create: `unity/Assets/ShipHdMap/Tests/EditMode/FixtureExporterTests.cs`
- Modify: `api/src/test/java/com/shiphdmap/api/dataset/SeedImportTests.java`, `api/src/test/java/com/shiphdmap/api/export/VehicleMapExportTests.java`, `api/src/test/java/com/shiphdmap/api/export/GeoJsonExportTests.java`
- Regenerate: `docs/fixtures/vehicle-map.sample.json`

**Interfaces:**
- Produces: `FixtureExporter.SeedLandmarks(SeedData seed) → List<Landmark>` (public static; 기둥 안쪽 면·`z_surface + 1.2`·선체 윤곽 y − 0.1 에서 유도). `SeedImportTests.fixtureLashingCount(ObjectMapper json) → int`
- 픽스처의 `lashing_points` 가 D3 전체(약 4,700). 랜드마크 좌표는 현재와 동일(LM-0001 = (12, −6.2, 11.8), LM-0019 = (40, 11.9, 11.8))

- [ ] **Step 1: 실패하는 Unity 테스트** — `unity/Assets/ShipHdMap/Tests/EditMode/FixtureExporterTests.cs`

```csharp
using System.Linq;
using NUnit.Framework;
using ShipHdMap.Editor;

namespace ShipHdMap.Tests
{
    public class FixtureExporterTests
    {
        [Test]
        public void SeedLandmarksSitOnPillarInnerFacesAndHull()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var lms = FixtureExporter.SeedLandmarks(seed);
            Assert.That(lms.Count, Is.EqualTo(19));
            var lm1 = lms.First(l => l.id == "LM-0001");
            Assert.That(lm1.position, Is.EqualTo(new[] { 12.0, -6.2, 11.8 }).Within(1e-9));   // inner face of the first -y pillar, tag 1.2 m above Deck 3
            Assert.That(lm1.normal, Is.EqualTo(new[] { 0.0, 1.0, 0.0 }));
            Assert.That(lm1.mounted_on, Is.EqualTo("C-PILLAR-D3-001"));
            var lm2 = lms.First(l => l.id == "LM-0002");
            Assert.That(lm2.position, Is.EqualTo(new[] { 12.0, 6.2, 11.8 }).Within(1e-9));
            Assert.That(lm2.normal, Is.EqualTo(new[] { 0.0, -1.0, 0.0 }));
            var hull = lms.First(l => l.id == "LM-0019");
            Assert.That(hull.position, Is.EqualTo(new[] { 40.0, 11.9, 11.8 }).Within(1e-9));  // port hull (outline y max) minus 0.1
            Assert.That(hull.mounted_on, Is.EqualTo("HULL-PORT"));
        }

        [Test]
        public void SeedLandmarksFollowAChangedShip()
        {
            var p = new ShipParams { beamM = 30, firstDeckZ = 6.0 };  // D3 at 11.2, pillars at y = ±(15 − 5.5)
            var lms = FixtureExporter.SeedLandmarks(ShipSeedBuilder.Build(p));
            var lm1 = lms.First(l => l.id == "LM-0001");
            Assert.That(lm1.position[1], Is.EqualTo(-9.2).Within(1e-9));
            Assert.That(lm1.position[2], Is.EqualTo(11.2 + 1.2).Within(1e-9));
            Assert.That(lms.First(l => l.id == "LM-0019").position[1], Is.EqualTo(14.9).Within(1e-9));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** (에디터 GUI 닫고)

```bash
U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
cd unity && "$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' Logs/editmode-results.xml | head -1
```
Expected: 컴파일 오류(`SeedLandmarks` 없음) 또는 failed ≥ 1.

- [ ] **Step 3: `FixtureExporter` 수정** — `ExportFromSeed` 의 랜드마크 루프와 `BuildMap` 의 창 필터를 교체

`ExportFromSeed` 에서 `var d3Pillars = …` 부터 `landmarks.Add(Lm("LM-0019", …));` 까지를 다음 한 줄로:

```csharp
            var landmarks = SeedLandmarks(seed);
```

클래스에 추가(`BuildMap` 위):

```csharp
        /// Landmarks derived from seed geometry, so a changed ShipParams (beam, deck pitch, pillar layout) still puts every tag
        /// on a structure: one pair per pillar station on the ramp deck (tag on the pillar's centreline-side face, 1.2 m above
        /// the deck), plus one on the port hull. Ids/codes stay LM-0001.. so the fixture's slots and ramp keep resolving.
        public static List<Landmark> SeedLandmarks(SeedData seed)
        {
            var deck = seed.decks[Math.Min(new ShipParams().rampDeckIndex, seed.decks.Count - 1)];
            double zTag = deck.z_surface + 1.2;
            var pillars = seed.facilities.Where(f => f.deck_id == deck.id && f.kind == "pillar").ToList();
            var stbdRow = pillars.Where(f => PillarCenter(f).y < 0).OrderBy(f => PillarCenter(f).x).ToList();   // -y = starboard
            var portRow = pillars.Where(f => PillarCenter(f).y > 0).OrderBy(f => PillarCenter(f).x).ToList();   // +y = port
            var landmarks = new List<Landmark>();
            int stationCount = Math.Min(9, Math.Min(stbdRow.Count, portRow.Count));
            for (int i = 0; i < stationCount; i++)
            {
                var s = stbdRow[i]; var p = portRow[i];
                landmarks.Add(Lm($"LM-{2 * i + 1:0000}", (2 * i + 1) % 20, PillarCenter(s).x, s.footprint.Max(pt => pt[1]), zTag, 0, 1, 0, s.id, deck.id));
                landmarks.Add(Lm($"LM-{2 * i + 2:0000}", (2 * i + 2) % 20, PillarCenter(p).x, p.footprint.Min(pt => pt[1]), zTag, 0, -1, 0, p.id, deck.id));
            }
            landmarks.Add(Lm("LM-0019", 19, 40, deck.outline.Max(pt => pt[1]) - 0.1, zTag, 0, -1, 0, "HULL-PORT", deck.id));
            return landmarks;
        }
```

`Lm` 에 `deckId` 인자를 추가:

```csharp
        static Landmark Lm(string id, int code, double x, double y, double z, double nx, double ny, double nz, string mountedOn, string deckId = "D3") =>
            new Landmark { id = id, marker = new Marker { family = "apriltag-36h11", code = code },
                position = new[] { x, y, z }, normal = new[] { nx, ny, nz }, size_m = 0.30, deck_id = deckId, mounted_on = mountedOn };
```

`BuildMap` 에서 창 필터 두 줄을 지우고 전체를 내보낸다:

```csharp
            // (delete) var lashingExport = d3Lashing.Where(l => (l.position[0] >= 94 && …) || needed.Contains(l.id)).ToList();
            …
                lashing_points = d3Lashing,   // whole Deck 3 grid (~4,700): slot generation maps every corner to a socket
```
`needed` 집합은 더 이상 쓰지 않으므로 선언과 `needed.Add(...)` 도 지운다. `BuildMap` 위 요약 주석의 "lashing-point window widened …" 문장을 "the whole Deck 3 lashing grid" 로 고친다.

- [ ] **Step 4: Unity 테스트 통과 확인** — Step 2 명령. Expected: total 55, failed 0.

- [ ] **Step 5: 픽스처 재생성**

```bash
cd unity && "$U" -batchmode -nographics -projectPath "$PWD" -executeMethod ShipHdMap.Editor.FixtureExporter.ExportFromSeed -quit -logFile "$PWD/Logs/fixture.log"; grep "Fixture written" Logs/fixture.log
cd .. && python3 -c "
import json; m=json.load(open('docs/fixtures/vehicle-map.sample.json'))
print('version', m['version'], 'LP', len(m['lashing_points']), 'LM', len(m['landmarks']), 'slots', len(m['parking_slots']))
print(m['landmarks'][0]['position'], m['landmarks'][18]['position'])
print([s['lashing_points'] for s in m['parking_slots']])"
ls -l docs/fixtures/vehicle-map.sample.json
```
Expected: LP 약 4,700(정확한 수를 적어 둔다), LM 19, slots 2, `[12.0, -6.2, 11.8]`, `[40.0, 11.9, 11.8]`, 각 슬롯 래싱 4개, 파일 300–500 KB.

- [ ] **Step 6: API 테스트를 픽스처 파생 값으로** — `SeedImportTests.java`

`fixtureAsSeed` 아래에 추가:

```java
	/** Lashing-point count of the fixture; tests compare against this instead of a literal so a regenerated fixture does not break them. */
	public static int fixtureLashingCount(ObjectMapper json) throws Exception {
		return json.readValue(Files.readString(Path.of("..", "docs", "fixtures", "vehicle-map.sample.json")), VehicleMap.class).lashingPoints().size();
	}
```

같은 파일의 두 곳:
```java
		int lp = fixtureLashingCount(json);
		…layer = 'LP'…).isEqualTo(lp);                         // was 49
		…).isEqualTo(19 + 18 + 1 + 3 + lp + 2);                // was … + 49 + 2
```
`VehicleMapExportTests.java`: `assertThat(m.lashingPoints()).hasSize(SeedImportTests.fixtureLashingCount(json));` (import `com.shiphdmap.api.dataset.SeedImportTests`; `json` 은 `@Autowired ObjectMapper json` 이 이미 있다 — 없으면 추가).
`GeoJsonExportTests.java`: `isEqualTo(3 + 19 + 18 + 1 + 3 + SeedImportTests.fixtureLashingCount(json) + 2)`.

- [ ] **Step 7: API 테스트** — `cd api && JAVA_HOME=/opt/homebrew/opt/openjdk ./gradlew test`. Expected: 42/42 (Docker 필요). `SlotGenerateTests` 의 "래싱 매핑이 낮다" 주석이 있으면 이제 픽스처로도 높게 나오므로 단언이 `lashing_coverage` 하한을 걸고 있는지 확인하고, 상한을 걸고 있으면 제거한다.

- [ ] **Step 8: 커밋**

```bash
git add unity/Assets/ShipHdMap/Editor/FixtureExporter.cs unity/Assets/ShipHdMap/Tests/EditMode/FixtureExporterTests.cs* api/src/test docs/fixtures/vehicle-map.sample.json
git commit -m "fixture: export the whole Deck 3 lashing grid, derive landmark placement from seed geometry"
```

---

### Task 2: Map 루트 = Ship Frame, `SetPose` 로 선체 기울임·램프 각도

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs`, `Bridge/MapRuntime.cs`, `Ship/ShipMeshBuilder.cs`, `Ship/MapOverlay.cs`, `Landmarks/LandmarkMarker.cs`, `Landmarks/LandmarkPlacer.cs`, `Vehicle/HudView.cs`, `Vehicle/VehicleController.cs`(Apply 만), `Editor/BridgeStubWindow.cs`
- Create: `unity/Assets/ShipHdMap/Tests/EditMode/PoseTests.cs`

**Interfaces:**
- Produces: `SetPoseMsg { double draft_fwd_m, draft_aft_m, heel_deg, lpp_m; RampMsg ramp }`, `RampMsg { string id; double angle_deg; string state }`; `MapRuntime.SetPose(string json)`, `MapRuntime.ApplyPose()`, `MapRuntime.PoseRotation(double trimDeg, double heelDeg) → Quaternion`(static), `MapRuntime.AttachShip()`; `HudView.SetRamp(string line)`; `HudView.SetDeckLabels` 의 점은 **루트 로컬** 좌표
- 불변: `LandmarkMarker.MoveTo(world pos, world normal, …)` 입력은 월드 그대로. `ToModel()` 은 부모 기준 로컬을 Ship Frame 으로

- [ ] **Step 1: 실패하는 테스트** — `unity/Assets/ShipHdMap/Tests/EditMode/PoseTests.cs`

```csharp
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class PoseTests
    {
        GameObject go;
        [TearDown] public void Cleanup() { if (go) Object.DestroyImmediate(go); var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship); }
        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));
        const string TrimOnly = "{\"draft_fwd_m\":8.1,\"draft_aft_m\":10.1,\"heel_deg\":0,\"lpp_m\":120,\"ramp\":{\"id\":\"RAMP-STERN\",\"angle_deg\":4,\"state\":\"deployed\"}}";
        const string HeelOnly = "{\"draft_fwd_m\":8.6,\"draft_aft_m\":8.6,\"heel_deg\":3,\"lpp_m\":120}";

        MapRuntime NewRuntime() { go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); return rt; }

        [Test]
        public void PositiveTrimRaisesTheBow()
        {
            var rt = NewRuntime();
            rt.SetPose(TrimOnly);   // trim = atan(2/120) = 0.955 deg; bow point 100 m forward rises ~1.67 m
            var bow = rt.transform.TransformPoint(ShipFrame.ToUnity(100, 0, 10));
            Assert.That(bow.y, Is.GreaterThan(11.5f));
            var ap = rt.transform.TransformPoint(ShipFrame.ToUnity(0, 0, 10));
            Assert.That(ap.y, Is.EqualTo(10f).Within(1e-4f));   // rotation about the AP origin
        }

        [Test]
        public void PositiveHeelLowersStarboard()
        {
            var rt = NewRuntime();
            rt.SetPose(HeelOnly);   // starboard point 10 m off the centreline drops 10·sin(3°) = 0.52 m
            var stbd = rt.transform.TransformPoint(ShipFrame.ToUnity(50, -10, 10));
            Assert.That(stbd.y, Is.LessThan(9.6f));
            var port = rt.transform.TransformPoint(ShipFrame.ToUnity(50, 10, 10));
            Assert.That(port.y, Is.GreaterThan(10.4f));
        }

        [Test]
        public void PoseKeepsShipFrameCoordinatesAndTiltsRampAndShip()
        {
            var rt = NewRuntime();
            var p = new ShipParams(); ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);   // built at the scene root, like the Demo scene before Awake attaches it
            rt.Load(Fixture());
            var lm = rt.LandmarksRoot.Find("LM-0001").GetComponent<LandmarkMarker>();
            var before = lm.ToModel();
            var lane = rt.CurrentMap.lanes.Find(l => l.id == "A2-D3-0001");
            rt.Vehicle.StartLane(lane, 10.6);
            var vehicleBefore = ShipFrame.ToShip(rt.transform.InverseTransformPoint(rt.Vehicle.transform.position));

            rt.SetPose(TrimOnly);

            var after = lm.ToModel();
            Assert.That(after.position, Is.EqualTo(before.position).Within(1e-4));
            Assert.That(after.normal, Is.EqualTo(before.normal).Within(1e-4));
            Assert.That(lm.transform.position.y, Is.Not.EqualTo(before.position[2]).Within(0.05));   // but the world position did move
            var vehicleAfter = ShipFrame.ToShip(rt.transform.InverseTransformPoint(rt.Vehicle.transform.position));
            Assert.That(vehicleAfter.x, Is.EqualTo(vehicleBefore.x).Within(1e-4)); Assert.That(vehicleAfter.z, Is.EqualTo(vehicleBefore.z).Within(1e-4));
            Assert.That(rt.Vehicle.transform.position.y, Is.Not.EqualTo(11.1f).Within(0.02f));   // x = 2 rises 2·tan(0.955°) ≈ 0.033 m

            var ship = GameObject.Find("Ship");
            Assert.That(ship.transform.parent, Is.EqualTo(rt.transform));                                   // attached under the Map root
            var ramp = ship.transform.Find("Ramp");
            Assert.That(Mathf.DeltaAngle(ramp.localRotation.eulerAngles.z, -4f), Is.EqualTo(0f).Within(1e-3f)); // SetRampAngle(4) → local z −4
            var floor = ship.transform.Find("D3/Floor");
            Assert.That(floor.position.y, Is.GreaterThan(10.5f + 60f * Mathf.Tan(0.955f * Mathf.Deg2Rad) - 0.6f)); // floor centre (x=60) rose with the root

            var overlayLine = rt.transform.Find("Overlay/D3/A2-D3-0001").GetComponent<LineRenderer>();
            Assert.That(overlayLine.useWorldSpace, Is.False);
        }

        [Test]
        public void PoseBeforeLoadIsAppliedAfterLoad()
        {
            var rt = NewRuntime();
            rt.SetPose(HeelOnly);
            var p = new ShipParams(); ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            rt.Load(Fixture());
            Assert.That(GameObject.Find("Ship").transform.parent, Is.EqualTo(rt.transform));
            var stbd = rt.transform.TransformPoint(ShipFrame.ToUnity(50, -10, 10));
            Assert.That(stbd.y, Is.LessThan(9.6f));
        }

        [Test]
        public void PoseRotationSigns()
        {
            var bow = MapRuntime.PoseRotation(10, 0) * Vector3.right;   Assert.That(bow.y, Is.GreaterThan(0.1f));
            var stbd = MapRuntime.PoseRotation(0, 10) * Vector3.forward; Assert.That(stbd.y, Is.LessThan(-0.1f));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — Task 1 Step 2 명령. Expected: 컴파일 오류(`PoseRotation`/`SetPoseMsg` 없음).

- [ ] **Step 3: `BridgeMessages.cs`** — 클래스 뒤에 추가

```csharp
    public class RampMsg { public string id; public double angle_deg; public string state; }
    /// Only what the tilt needs; absolute draft / tide / quay height are M5b (quay geometry).
    public class SetPoseMsg { public double draft_fwd_m = 8.1, draft_aft_m = 8.6, heel_deg, lpp_m = 120; public RampMsg ramp; }
```

- [ ] **Step 4: `MapRuntime.cs`**

필드 추가: `SetPoseMsg _pose;`

`Awake` 의 선체 생성을 루트 아래로:

```csharp
            Ship = GameObject.Find("Ship");
            if (Ship == null) { _seed = ShipSeedBuilder.Build(shipParams); Ship = ShipMeshBuilder.Build(_seed, shipParams, transform); }
            AttachShip();
```

`Placer.Created` 핸들러를 `ToModel` 값으로:

```csharp
            Placer.Created += lm => { var m = lm.ToModel(); _markers[lm.id] = lm; MapRefs[lm.id] = RefOf(m);
                Send(BridgeMessages.OnFeatureCreated, MapJson.Serialize(new FeatureCreatedEvt { tempId = lm.id, layer = "LM", x = m.position[0], y = m.position[1], z = m.position[2], deck = lm.deckId, mounted_on = lm.mountedOn, normal = m.normal })); };
```

`Load` 끝(`Hud.SetContext(_deck, null);` 다음)에 `ApplyPose();`.

`SetPose` 스텁을 교체:

```csharp
        /// Ship Frame is the Map root's local space; pose only rotates this root (spec §4.1). Children keep their local coordinates.
        public void SetPose(string json) { _pose = MapJson.Parse<SetPoseMsg>(json); ApplyPose(); }

        /// Trim > 0 (stern deeper) raises the bow (+x); heel > 0 lowers starboard (Unity +z). Rotation about the AP origin.
        public static Quaternion PoseRotation(double trimDeg, double heelDeg) => Quaternion.Euler((float)heelDeg, 0, (float)trimDeg);

        public void ApplyPose()
        {
            if (_pose == null) return;
            double trimDeg = Math.Atan2(_pose.draft_aft_m - _pose.draft_fwd_m, _pose.lpp_m <= 0 ? 120 : _pose.lpp_m) * 180 / Math.PI;
            transform.localRotation = PoseRotation(trimDeg, _pose.heel_deg);
            AttachShip();
            if (Ship && _pose.ramp != null) ShipMeshBuilder.SetRampAngle(Ship, _pose.ramp.angle_deg);
            Hud.SetRamp(_pose.ramp == null ? null : $"ramp {_pose.ramp.angle_deg:F1} deg  {_pose.ramp.state}");
            Physics.SyncTransforms();   // autoSyncTransforms is off; placement/drag raycasts must see the tilted colliders
        }

        /// The Demo scene may carry a pre-built "Ship" at the scene root; it must ride on the Map root to follow the pose.
        public void AttachShip()
        {
            if (!Ship) Ship = GameObject.Find("Ship");
            if (Ship && Ship.transform.parent != transform) Ship.transform.SetParent(transform, false);
        }
```

`PoseRotation` 의 부호가 Step 1 의 `PoseRotationSigns` 에서 틀리면 그 축의 부호만 뒤집는다(스펙은 결과 방향을 고정하고 축 부호는 테스트가 정한다).

- [ ] **Step 5: `ShipMeshBuilder.cs`** — 월드 → 로컬

```csharp
                ramp.transform.localPosition = ShipFrame.ToUnity((r.hinge[0][0] + r.hinge[1][0]) / 2, (r.hinge[0][1] + r.hinge[1][1]) / 2, r.hinge[0][2]);
                float len = (float)r.length_m, w = (float)r.width_m;
                Prim(ramp, "Plate", PrimitiveType.Cube, new Vector3(-len / 2, -FloorThick / 2, 0), new Vector3(len, FloorThick, w), Color(0.5f, 0.5f, 0.45f), layer, materials);   // local to the hinge
```
`SetRampAngle`: `ramp.localRotation = Quaternion.Euler(0, 0, (float)-angleDeg);`
MEP: `pipe.transform.localRotation = Quaternion.Euler(0, 0, 90);`
`Prim`: `g.transform.localPosition = pos;`

- [ ] **Step 6: `MapOverlay.Line`** — `lr.useWorldSpace = false;`

- [ ] **Step 7: `LandmarkMarker.cs`**

`Spawn` 의 마지막: `lm.MoveTo(parent.TransformPoint(unityPos), parent.TransformDirection(unityNormal), deckId, mountedOn);` 그리고 요약 주석에 "`unityPos`/`unityNormal` are in the parent's local space (Ship Frame mapped by `ShipFrame.ToUnity`); `MoveTo` takes world" 를 적는다.

`ToModel`:

```csharp
        /// DTO for vehicle-map / API, in Ship Frame = the parent's local space. Position is the mounting point (centre pushed back onto the surface).
        public Landmark ToModel()
        {
            Vector3 p = transform.position - NormalUnity * 0.01f, n = NormalUnity;
            var root = transform.parent;
            if (root) { p = root.InverseTransformPoint(p); n = root.InverseTransformDirection(n); }
            var (x, y, z) = ShipFrame.ToShip(p);
            var (nx, ny, nz) = ShipFrame.ToShip(n);
            return new Landmark { … 기존과 동일 … };
        }
```

- [ ] **Step 8: `LandmarkPlacer.cs`** — 갑판 판정을 루트 로컬 높이로. `PlaceAt` 와 `DragTo` 의 `DeckIdForHeight(hit.point.y)` 를 `DeckIdForHeight(LocalY(hit.point))` 로, 헬퍼 추가:

```csharp
        /// Deck lookup must use the Map-root-local height: with a 2° trim the bow floor is 4 m higher in world space.
        float LocalY(Vector3 world) => landmarksRoot ? landmarksRoot.InverseTransformPoint(world).y : world.y;
```

- [ ] **Step 9: `HudView.cs`**

- 필드 `string _ramp;` 와 `public void SetRamp(string line) { _ramp = line; }`
- `LateUpdate`: `_info.text = string.Join("\n", Lines(…)) + (_ramp == null ? "" : "\n" + _ramp);`
- 갑판 라벨: `_deckLabels` 항목은 루트 로컬 점. 투영 시 `var world = transform.TransformPoint(local);` 를 써서 `cam.WorldToViewportPoint(world)` 와 `CameraTransformWorldToPanel(panel, world, cam)` 에 넘긴다(변수명 `world` → `local` 로 바꾸고 한 줄 추가). `SetDeckLabels` 의 매개변수 주석에 "root-local Unity points" 를 적는다.
- `CursorShip`: `return ShipFrame.ToShip(transform.InverseTransformPoint(hit.point));`

- [ ] **Step 10: `VehicleController.Apply`** — 로컬로

```csharp
            transform.localPosition = ShipFrame.ToUnity(p.x, p.y, deckZ + 0.5);
            transform.localRotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(h * 180 / System.Math.PI), 0);
```

- [ ] **Step 11: 스텁 창** — `BridgeStubWindow.OnGUI` 의 노이즈 슬라이더 아래에

```csharp
            EditorGUI.BeginChangeCheck();
            _aft = EditorGUILayout.Slider("draft aft (m)", _aft, 6, 10); _heel = EditorGUILayout.Slider("heel (deg)", _heel, -3, 3);
            if (EditorGUI.EndChangeCheck()) rt.SetPose($"{{\"draft_fwd_m\":8.1,\"draft_aft_m\":{_aft},\"heel_deg\":{_heel},\"lpp_m\":120}}");
```
필드 `float _aft = 8.6f, _heel;` 추가.

- [ ] **Step 12: 테스트 통과 확인** — Task 1 Step 2 명령. Expected: total 60, failed 0. 기존 `MapRuntimeTests`·`ShipMeshBuilderTests`·`LandmarkMarkerTests`·`AprilTagTests`·`HudViewTests` 가 그대로 통과해야 한다(부모가 항등이면 값이 같다).

- [ ] **Step 13: 커밋**

```bash
git add unity/Assets/ShipHdMap
git commit -m "unity: Map root is Ship Frame; SetPose tilts the root (trim/heel) and sets the ramp angle"
```

---

### Task 3: `ScenarioPlanner` — 다음 구획·이탈점·경로·판정 (순수)

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Vehicle/ScenarioPlanner.cs`
- Create: `unity/Assets/ShipHdMap/Tests/EditMode/ScenarioPlannerTests.cs`

**Interfaces:**
- Produces (모두 `public static`, 클래스 `ShipHdMap.ScenarioPlanner`):
  - `const double FinalRunM = 2.0, ParkSpeedMps = 2.0`
  - `bool IsFilled(string status)` — `filled` 또는 `needs_adjust`
  - `ParkingSlot NextSlot(IEnumerable<ParkingSlot> slots, string mode)` — load: `status` 가 null/`empty` 이고 `target_pose != null` 인 것 중 `sequence_no` 최소; unload: `IsFilled` 중 최대; 없으면 null
  - `(double s, double x, double y) NearestOnLine(double[][] line, double x, double y)` — 폴리라인 최근접점의 아크길이와 좌표
  - `double ExitS(Lane lane, TargetPose t)` — `x_exit = t.x − FinalRunM − |t.y − y_lane|` 의 최근접 아크길이(0 이상)
  - `double[][] ApproachPath(Pose2D est, TargetPose t, double z)` — `{est, t − FinalRun·(cos h, sin h), t}`
  - `double[][] DeparturePath(TargetPose t, Lane lane, double z)` — `{t, (t.x−2, t.y), (t.x−2−|t.y−y_lane|, y_lane), 차로 시작점}`
  - `double[][] Shift(double[][] path, double dx, double dy)`
  - `(string status, double errLat, double errLon, double errHeadingDeg) Judge(Pose2D truth, TargetPose t, Tolerance tol)`

- [ ] **Step 1: 실패하는 테스트** — `ScenarioPlannerTests.cs`

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class ScenarioPlannerTests
    {
        static Lane D3Lane() => new Lane { id = "A2-D3-0001", deck_id = "D3", centerline = new[] { new double[] { 2, 0, 10.6 }, new double[] { 60, 0, 10.6 }, new double[] { 118, 0, 10.6 } }, speed_limit_kmh = 10 };
        static ParkingSlot Slot(string id, int seq, string status, double x = 100, double y = 2.925) =>
            new ParkingSlot { id = id, deck_id = "D3", sequence_no = seq, status = status, access_lane_id = "A2-D3-0001", target_pose = new TargetPose { x = x, y = y, heading_deg = 0 }, tolerance = new Tolerance { lat_m = 0.15, lon_m = 0.30, heading_deg = 2 } };

        [Test]
        public void NextSlotLoadTakesLowestEmptySequenceAndUnloadHighestFilled()
        {
            var slots = new List<ParkingSlot> { Slot("c", 3, "empty"), Slot("a", 1, "filled"), Slot("b", 2, null), Slot("d", 4, "needs_adjust") };
            Assert.That(ScenarioPlanner.NextSlot(slots, "load").id, Is.EqualTo("b"));
            Assert.That(ScenarioPlanner.NextSlot(slots, "unload").id, Is.EqualTo("d"));
            Assert.That(ScenarioPlanner.NextSlot(new List<ParkingSlot> { Slot("a", 1, "filled") }, "load"), Is.Null);
            Assert.That(ScenarioPlanner.NextSlot(null, "load"), Is.Null);
        }

        [Test]
        public void ExitPointIsFortyFiveDegreesBeforeTheSlotAndClampsAtLaneStart()
        {
            var t = new TargetPose { x = 102.4, y = 2.925, heading_deg = 0 };
            Assert.That(ScenarioPlanner.ExitS(D3Lane(), t), Is.EqualTo(102.4 - 2 - 2.925 - 2).Within(1e-9));   // x_exit 97.475, lane starts at x = 2
            var stern = new TargetPose { x = 3.6, y = -8, heading_deg = 0 };
            Assert.That(ScenarioPlanner.ExitS(D3Lane(), stern), Is.EqualTo(0).Within(1e-9));
        }

        [Test]
        public void ApproachPathEndsAtTargetWithTargetHeading()
        {
            var t = new TargetPose { x = 102.4, y = 2.925, heading_deg = 0 };
            var path = ScenarioPlanner.ApproachPath(new Pose2D { x = 97.5, y = 0.1, psiRad = 0 }, t, 10.6);
            Assert.That(path.Length, Is.EqualTo(3));
            Assert.That(path[0], Is.EqualTo(new[] { 97.5, 0.1, 10.6 }).Within(1e-9));
            Assert.That(path[1], Is.EqualTo(new[] { 100.4, 2.925, 10.6 }).Within(1e-9));
            Assert.That(path[2], Is.EqualTo(new[] { 102.4, 2.925, 10.6 }).Within(1e-9));
            var (end, heading, _) = LaneFollower.At(path, 999);
            Assert.That(end.x, Is.EqualTo(102.4f).Within(1e-4f)); Assert.That(heading, Is.EqualTo(0).Within(1e-9));

            var turned = ScenarioPlanner.ApproachPath(new Pose2D { x = 0, y = 0 }, new TargetPose { x = 10, y = 10, heading_deg = 90 }, 0);
            Assert.That(turned[1], Is.EqualTo(new[] { 10.0, 8.0, 0.0 }).Within(1e-9));   // last 2 m run along +y
        }

        [Test]
        public void DeparturePathBacksOutToTheLaneStart()
        {
            var t = new TargetPose { x = 102.4, y = 2.925, heading_deg = 0 };
            var path = ScenarioPlanner.DeparturePath(t, D3Lane(), 10.6);
            Assert.That(path.Length, Is.EqualTo(4));
            Assert.That(path[1], Is.EqualTo(new[] { 100.4, 2.925, 10.6 }).Within(1e-9));
            Assert.That(path[2], Is.EqualTo(new[] { 97.475, 0.0, 10.6 }).Within(1e-9));
            Assert.That(path[3], Is.EqualTo(new[] { 2.0, 0.0, 10.6 }).Within(1e-9));
            var (_, heading, _) = LaneFollower.At(path, 999);
            Assert.That(Math.Abs(heading), Is.EqualTo(Math.PI).Within(1e-9));   // ends pointing astern
        }

        [Test]
        public void ShiftTranslatesEveryPoint()
        {
            var shifted = ScenarioPlanner.Shift(new[] { new double[] { 1, 2, 3 }, new double[] { 4, 5, 3 } }, 0.5, -0.25);
            Assert.That(shifted[0], Is.EqualTo(new[] { 1.5, 1.75, 3.0 }).Within(1e-12));
            Assert.That(shifted[1], Is.EqualTo(new[] { 4.5, 4.75, 3.0 }).Within(1e-12));
        }

        [Test]
        public void JudgeUsesTargetFrameAndTolerance()
        {
            var t = new TargetPose { x = 100, y = 2, heading_deg = 0 };
            var tol = new Tolerance { lat_m = 0.15, lon_m = 0.30, heading_deg = 2 };
            var ok = ScenarioPlanner.Judge(new Pose2D { x = 100.2, y = 2.1, psiRad = 1 * Math.PI / 180 }, t, tol);
            Assert.That(ok.status, Is.EqualTo("filled"));
            Assert.That(ok.errLon, Is.EqualTo(0.2).Within(1e-9)); Assert.That(ok.errLat, Is.EqualTo(0.1).Within(1e-9)); Assert.That(ok.errHeadingDeg, Is.EqualTo(1).Within(1e-9));
            Assert.That(ScenarioPlanner.Judge(new Pose2D { x = 100, y = 2.2, psiRad = 0 }, t, tol).status, Is.EqualTo("needs_adjust"));     // lat 0.20 > 0.15
            Assert.That(ScenarioPlanner.Judge(new Pose2D { x = 100.35, y = 2, psiRad = 0 }, t, tol).status, Is.EqualTo("needs_adjust"));    // lon 0.35 > 0.30
            Assert.That(ScenarioPlanner.Judge(new Pose2D { x = 100, y = 2, psiRad = 2.5 * Math.PI / 180 }, t, tol).status, Is.EqualTo("needs_adjust"));
            // rotated target frame: heading 90 → "lon" is along +y
            var r = ScenarioPlanner.Judge(new Pose2D { x = 10, y = 10.2, psiRad = Math.PI / 2 }, new TargetPose { x = 10, y = 10, heading_deg = 90 }, null);
            Assert.That(r.errLon, Is.EqualTo(0.2).Within(1e-9)); Assert.That(r.errLat, Is.EqualTo(0).Within(1e-9)); Assert.That(r.status, Is.EqualTo("filled"));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — Task 1 Step 2 명령. Expected: 컴파일 오류(`ScenarioPlanner` 없음).

- [ ] **Step 3: 구현** — `unity/Assets/ShipHdMap/Runtime/Vehicle/ScenarioPlanner.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace ShipHdMap
{
    /// Pure scenario geometry (spec §3): which slot next, where to leave the lane, the approach/departure polylines, and the
    /// parking judgement. No scene access, so it is unit-tested directly; MapRuntime owns the state machine.
    public static class ScenarioPlanner
    {
        public const double FinalRunM = 2.0, ParkSpeedMps = 2.0;
        const double D = Math.PI / 180.0;

        public static bool IsFilled(string status) => status == "filled" || status == "needs_adjust";

        public static ParkingSlot NextSlot(IEnumerable<ParkingSlot> slots, string mode)
        {
            if (slots == null) return null;
            return mode == "unload"
                ? slots.Where(s => IsFilled(s.status) && s.target_pose != null).OrderByDescending(s => s.sequence_no).FirstOrDefault()
                : slots.Where(s => (s.status ?? "empty") == "empty" && s.target_pose != null).OrderBy(s => s.sequence_no).FirstOrDefault();
        }

        /// Arc length and coordinates of the polyline point nearest to (x, y).
        public static (double s, double x, double y) NearestOnLine(double[][] line, double x, double y)
        {
            double best = double.MaxValue, bestS = 0, bx = line[0][0], by = line[0][1], acc = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                double ax = line[i][0], ay = line[i][1], dx = line[i + 1][0] - ax, dy = line[i + 1][1] - ay, len2 = dx * dx + dy * dy;
                double t = len2 < 1e-12 ? 0 : Math.Min(1, Math.Max(0, ((x - ax) * dx + (y - ay) * dy) / len2));
                double px = ax + dx * t, py = ay + dy * t, d2 = (px - x) * (px - x) + (py - y) * (py - y);
                if (d2 < best) { best = d2; bestS = acc + Math.Sqrt(len2) * t; bx = px; by = py; }
                acc += Math.Sqrt(len2);
            }
            return (bestS, bx, by);
        }

        /// Leave the lane FinalRunM + |lateral offset| before the target so the approach is a 45° diagonal plus a straight 2 m run.
        public static double ExitS(Lane lane, TargetPose t)
        {
            var (_, _, laneY) = NearestOnLine(lane.centerline, t.x, t.y);
            double xExit = t.x - FinalRunM - Math.Abs(t.y - laneY);
            return NearestOnLine(lane.centerline, xExit, laneY).s;
        }

        /// Planned in the vehicle's belief frame: from its estimated pose to the target, the last FinalRunM along the target heading.
        public static double[][] ApproachPath(Pose2D est, TargetPose t, double z)
        {
            double h = t.heading_deg * D;
            return new[] { new[] { est.x, est.y, z }, new[] { t.x - FinalRunM * Math.Cos(h), t.y - FinalRunM * Math.Sin(h), z }, new[] { t.x, t.y, z } };
        }

        /// Unload: back out of the slot to the lane and drive to the lane start (astern), where the vehicle disappears.
        public static double[][] DeparturePath(TargetPose t, Lane lane, double z)
        {
            var (_, _, laneY) = NearestOnLine(lane.centerline, t.x, t.y);
            return new[] { new[] { t.x, t.y, z }, new[] { t.x - FinalRunM, t.y, z }, new[] { t.x - FinalRunM - Math.Abs(t.y - laneY), laneY, z }, new[] { lane.centerline[0][0], lane.centerline[0][1], z } };
        }

        public static double[][] Shift(double[][] path, double dx, double dy) => path.Select(p => new[] { p[0] + dx, p[1] + dy, p[2] }).ToArray();

        /// Errors of the true pose in the target's frame: lon along the target heading, lat to its left (+y side), heading wrapped.
        public static (string status, double errLat, double errLon, double errHeadingDeg) Judge(Pose2D truth, TargetPose t, Tolerance tol)
        {
            tol ??= new Tolerance { lat_m = 0.15, lon_m = 0.30, heading_deg = 2 };
            double h = t.heading_deg * D, dx = truth.x - t.x, dy = truth.y - t.y;
            double lon = dx * Math.Cos(h) + dy * Math.Sin(h), lat = -dx * Math.Sin(h) + dy * Math.Cos(h);
            double hdg = ShipFrame.WrapDeg(truth.psiRad / D - t.heading_deg);
            bool ok = Math.Abs(lat) <= tol.lat_m && Math.Abs(lon) <= tol.lon_m && Math.Abs(hdg) <= tol.heading_deg;
            return (ok ? "filled" : "needs_adjust", lat, lon, hdg);
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — Task 1 Step 2 명령. Expected: total 66, failed 0.

- [ ] **Step 5: 커밋** — `git add unity/Assets/ShipHdMap && git commit -m "unity: ScenarioPlanner — next slot, exit point, approach/departure paths, parking judgement"`

---

### Task 4: Unity 시나리오 상태기, 주차 판정 송신, 주차 박스, 시간 배율

**Files:**
- Modify: `Bridge/BridgeMessages.cs`, `Bridge/MapRuntime.cs`, `Vehicle/VehicleController.cs`, `Ship/MapOverlay.cs`, `Editor/BridgeStubWindow.cs`
- Create: `Tests/EditMode/ScenarioRunTests.cs`
- Modify: `Tests/EditMode/ScenarioIntegrationTests.cs`(새 흐름)

**Interfaces:**
- Consumes: Task 3 `ScenarioPlanner.*`, Task 2 `ApplyPose`
- Produces:
  - `BridgeMessages.SetTimeScale = "SetTimeScale"`, `OnScenario = "onScenario"`; `SetTimeScaleMsg { double scale = 1 }`; `SlotFilledEvt { string slot_id, status; double? err_lat, err_lon, err_heading }`; `ScenarioEvt { string @event, mode, slot_id, detail }`; `StartScenarioMsg { string mode }`(`map` 제거)
  - `MapRuntime.Phase { Idle, OnLane, Parking, Departing }`, `MapRuntime.ScenarioPhase`, `MapRuntime.Step(float dt)`, `MapRuntime.SetTimeScale(string json)`, `MapRuntime.TargetSlotId`
  - `VehicleController.StartPath(double[][] path, double z, double speedMps)`, `StartLane(Lane, double z)`(속도 = `speed_limit_kmh/3.6`), `Advance(double dt)`, `AtEnd`, `path`(구 `centerline`). `Update` 는 없다 — `MapRuntime.Step` 이 민다
  - `MapOverlay.SetStatus(GameObject overlay, string id, string status)`, `SpawnParked(GameObject overlay, ParkingSlot slot, Pose2D pose, double z) → GameObject`(이름 `PARKED-{id}`, 슬롯의 갑판 그룹 아래), `RemoveParked(GameObject overlay, string slotId)`
  - `ResolveScenarioLane` 은 삭제

- [ ] **Step 1: 실패하는 테스트** — `ScenarioRunTests.cs`

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class ScenarioRunTests
    {
        GameObject go; readonly List<(string name, string json)> emitted = new();
        [TearDown] public void Cleanup() { Time.timeScale = 1f; if (go) Object.DestroyImmediate(go); var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship); }
        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));
        static string FilledFixture() => Fixture().Replace("\"status\": \"empty\"", "\"status\": \"filled\"");
        const string NoNoise = "{\"sigma_r\":0,\"sigma_theta\":0,\"sigma_alpha\":0,\"sigma_gps\":0}";

        MapRuntime NewRuntime(string fixture)
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Emit += (n, j) => emitted.Add((n, j));
            rt.Load(fixture); rt.SetNoise(NoNoise);
            return rt;
        }

        /// Steps until an event with this name arrives; fails after maxSteps.
        static string RunUntil(MapRuntime rt, List<(string name, string json)> log, string name, int maxSteps = 4000)
        {
            int start = log.Count;
            for (int i = 0; i < maxSteps; i++)
            {
                rt.Step(0.05f);
                var hit = log.Skip(start).FirstOrDefault(e => e.name == name);
                if (hit.name != null) return hit.json;
            }
            Assert.Fail($"no {name} within {maxSteps} steps (phase {rt.ScenarioPhase}, s {rt.Vehicle.s:F1})"); return null;
        }

        [Test]
        public void LoadScenarioParksFirstSlotEmitsAndSpawnsNextVehicle()
        {
            var rt = NewRuntime(Fixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"start\"") && e.json.Contains("\"load\"")));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"target\"") && e.json.Contains("PS-D3-001")));
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnLane));
            Assert.That(rt.Vehicle.speedMps, Is.EqualTo(10 / 3.6).Within(1e-9));

            var json = RunUntil(rt, emitted, "onSlotFilled");
            var evt = MapJson.Parse<SlotFilledEvt>(json);
            Assert.That(evt.slot_id, Is.EqualTo("PS-D3-001"));
            Assert.That(evt.status, Is.EqualTo("filled"));                       // zero noise → the estimate equals the truth
            Assert.That(System.Math.Abs(evt.err_lat.Value), Is.LessThan(0.15)); Assert.That(System.Math.Abs(evt.err_lon.Value), Is.LessThan(0.30));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"leave_lane\"")));
            Assert.That(rt.CurrentMap.parking_slots.First(s => s.id == "PS-D3-001").status, Is.EqualTo("filled"));
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001"), Is.Not.Null);
            var fill = rt.transform.Find("Overlay/D3/PS-D3-001/Fill").GetComponent<MeshRenderer>().sharedMaterial.color;
            Assert.That(fill.b, Is.EqualTo(1f).Within(1e-3));                    // "filled" blue
            // next vehicle already on the lane, heading for PS-D3-002
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnLane));
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-002"));
            Assert.That(rt.Vehicle.s, Is.LessThan(1.0));
        }

        [Test]
        public void LoadFinishesWhenNoEmptySlotRemains()
        {
            var rt = NewRuntime(FilledFixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Idle));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("\"finished\"") && e.json.Contains("no_empty_slot")));
        }

        [Test]
        public void LoadRestoresParkedCarsAndUnloadEmptiesInReverse()
        {
            var rt = NewRuntime(FilledFixture());
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001"), Is.Not.Null);
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-002"), Is.Not.Null);
            rt.StartScenario("{\"mode\":\"unload\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Departing));
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-002"));                 // highest sequence first
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-002"), Is.Null);   // the car became the vehicle
            var json = RunUntil(rt, emitted, "onSlotFilled");
            var evt = MapJson.Parse<SlotFilledEvt>(json);
            Assert.That(evt.slot_id, Is.EqualTo("PS-D3-002")); Assert.That(evt.status, Is.EqualTo("empty")); Assert.That(evt.err_lat, Is.Null);
            Assert.That(json, Does.Not.Contain("err_lat"));
            Assert.That(rt.CurrentMap.parking_slots.First(s => s.id == "PS-D3-002").status, Is.EqualTo("empty"));
            Assert.That(rt.TargetSlotId, Is.EqualTo("PS-D3-001"));
        }

        [Test]
        public void EditModeStopsTheScenarioAndResetsTimeScale()
        {
            var rt = NewRuntime(Fixture());
            rt.SetTimeScale("{\"scale\":5}");
            Assert.That(Time.timeScale, Is.EqualTo(5f).Within(1e-6f));
            rt.StartScenario("{\"mode\":\"load\"}");
            rt.Step(0.05f);
            rt.SetMode("edit");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Idle));
            Assert.That(rt.Vehicle.running, Is.False);
            Assert.That(rt.Vehicle.gameObject.activeSelf, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f).Within(1e-6f));
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001"), Is.Null);   // nothing was parked yet
        }

        [Test]
        public void SetDeckHidesParkedCarsWithTheirDeck()
        {
            var rt = NewRuntime(FilledFixture());
            rt.SetDeck("D1");
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001").GetComponent<Renderer>().enabled, Is.False);
            rt.SetDeck("all");
            Assert.That(rt.transform.Find("Overlay/D3/PARKED-PS-D3-001").GetComponent<Renderer>().enabled, Is.True);
        }
    }
}
```

`ScenarioIntegrationTests.cs` 의 테스트 본문을 새 흐름으로 교체(선체 있음 → 차폐 포함, 이름 유지):

```csharp
        [Test]
        public void VehicleAtLaneStartSeesAtLeastOneLandmark()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            ShipMeshBuilder.Build(seed, new ShipParams());
            Physics.SyncTransforms();

            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            rt.Load(Fixture());
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnLane));
            Assert.That(rt.Vehicle.Truth.x, Is.EqualTo(2).Within(1e-6));   // lane A2-D3-0001 starts at (2, 0)

            var obs = rt.Sensor.Sense(rt.Vehicle.Truth, rt.MapRefs, id => rt.Markers.First(m => m.id == id).transform.position);
            Assert.That(obs.Count, Is.GreaterThanOrEqualTo(1));
        }
```

- [ ] **Step 2: 실패 확인** — Task 1 Step 2 명령. Expected: 컴파일 오류(`Phase`, `Step`, `SlotFilledEvt` 없음).

- [ ] **Step 3: `BridgeMessages.cs`**

```csharp
        public const string Load = "Load", SetMode = "SetMode", SetDeck = "SetDeck", Select = "Select", Confirm = "Confirm",
            SetPose = "SetPose", SetNoise = "SetNoise", StartScenario = "StartScenario", SetTimeScale = "SetTimeScale";
        public const string OnSeedReady = "onSeedReady", OnFeatureCreated = "onFeatureCreated", OnFeatureMoved = "onFeatureMoved",
            OnSelected = "onSelected", OnSlotFilled = "onSlotFilled", OnLocalization = "onLocalization", OnScenario = "onScenario";
    …
    public class StartScenarioMsg { public string mode; }   // the map is whatever was Loaded
    public class SetTimeScaleMsg { public double scale = 1; }
    /// Parking result (load) or an emptied slot (unload: status "empty", no errors — nulls are dropped from the JSON).
    public class SlotFilledEvt { public string slot_id; public string status; public double? err_lat, err_lon, err_heading; }
    /// Scenario log line. `event`: start | target | leave_lane | finished.
    public class ScenarioEvt { [Newtonsoft.Json.JsonProperty("event")] public string evt; public string mode; public string slot_id; public string detail; }
```

- [ ] **Step 4: `VehicleController.cs`** — 전체 교체

```csharp
using UnityEngine;

namespace ShipHdMap
{
    /// Kinematic vehicle: walks a [x,y,z] polyline (a lane centreline or a planned approach/departure path) at a constant speed.
    /// No Update of its own — MapRuntime.Step advances it so sensing and motion happen in a fixed order.
    public class VehicleController : MonoBehaviour
    {
        public double speedMps = 2.0;
        public double[][] path; public double deckZ; public double s;
        public bool running;
        public bool AtEnd { get; private set; }
        public Pose2D Truth;

        public void StartLane(Lane lane, double z) => StartPath(lane.centerline, z, lane.speed_limit_kmh > 0 ? lane.speed_limit_kmh / 3.6 : speedMps);

        public void StartPath(double[][] line, double z, double speed) { path = line; deckZ = z; s = 0; speedMps = speed; running = true; AtEnd = false; Apply(); }

        public void Advance(double dt)
        {
            if (!running || path == null) return;
            s += speedMps * dt;
            var (_, _, end) = LaneFollower.At(path, s);
            if (end) { running = false; AtEnd = true; }
            Apply();
        }

        void Apply()
        {
            var (p, h, _) = LaneFollower.At(path, s);
            Truth = new Pose2D { x = p.x, y = p.y, psiRad = h };
            transform.localPosition = ShipFrame.ToUnity(p.x, p.y, deckZ + 0.5);
            transform.localRotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(h * 180 / System.Math.PI), 0);
        }
    }
}
```

- [ ] **Step 5: `MapOverlay.cs`** — 추가

```csharp
        static Material _parkedMat;

        /// Re-colours a slot fill after a parking judgement / unload. Selection highlight is re-applied by the next Highlight call.
        public static void SetStatus(GameObject overlay, string id, string status)
        {
            var slot = FindInDecks(overlay, id); var fill = slot ? slot.GetComponentInChildren<SlotFill>(true) : null;
            if (fill == null) return;
            fill.status = status ?? "empty";
            fill.GetComponent<MeshRenderer>().sharedMaterial = FillMat(fill.status, false);
        }

        /// Static car box at the parked pose, under the slot's deck group so SetDeck hides it with the deck. Replaces any previous box.
        public static GameObject SpawnParked(GameObject overlay, ParkingSlot slot, Pose2D pose, double z)
        {
            RemoveParked(overlay, slot.id);
            var deck = overlay.transform.Find(slot.deck_id ?? "none"); if (deck == null) return null;
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube); g.name = "PARKED-" + slot.id; g.transform.SetParent(deck, false);
            Object.DestroyImmediate(g.GetComponent<Collider>());   // never blocks placement raycasts or the sensor linecast
            g.transform.localPosition = ShipFrame.ToUnity(pose.x, pose.y, z + 0.75);
            g.transform.localRotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(pose.psiRad * 180 / System.Math.PI), 0);
            g.transform.localScale = new Vector3(4.8f, 1.5f, 1.85f);
            if (!_parkedMat) _parkedMat = new Material(Shader.Find("Standard")) { color = new Color(0.82f, 0.84f, 0.9f), name = "parked-car" };
            g.GetComponent<Renderer>().sharedMaterial = _parkedMat;
            return g;
        }

        public static void RemoveParked(GameObject overlay, string slotId)
        {
            var t = FindInDecks(overlay, "PARKED-" + slotId); if (!t) return;
            if (Application.isPlaying) Object.Destroy(t.gameObject); else Object.DestroyImmediate(t.gameObject);
        }

        static Transform FindInDecks(GameObject overlay, string name)
        {
            if (!overlay) return null;
            foreach (Transform deck in overlay.transform) { var t = deck.Find(name); if (t) return t; }
            return null;
        }
```
클래스 요약 주석에 "and parked-car boxes (`PARKED-{slot}`)" 를 덧붙인다.

- [ ] **Step 6: `MapRuntime.cs`** — 상태기

필드:

```csharp
        public enum Phase { Idle, OnLane, Parking, Departing }
        public Phase ScenarioPhase { get; private set; } = Phase.Idle;
        public string TargetSlotId => _target?.id;
        string _scenarioMode = "load"; ParkingSlot _target; Lane _targetLane; Deck _targetDeck; double _exitS;
        const double R2D = 180 / Math.PI;
```

`Load`: 시작에 `ScenarioPhase = Phase.Idle; Vehicle.running = false;`. 오버레이를 만든 뒤(`MapOverlay.SetDeck(_overlay, _deck);` 다음)에 주차 박스 복원:

```csharp
            foreach (var slot in CurrentMap.parking_slots ?? new List<ParkingSlot>())
            {
                if (!ScenarioPlanner.IsFilled(slot.status) || slot.target_pose == null) continue;
                var deck = CurrentMap.decks?.Find(d => d.id == slot.deck_id); if (deck == null) continue;
                MapOverlay.SpawnParked(_overlay, slot, new Pose2D { x = slot.target_pose.x, y = slot.target_pose.y, psiRad = slot.target_pose.heading_deg / R2D }, deck.z_surface);
            }
```

`SetMode`:

```csharp
        public void SetMode(string mode)
        {
            _mode = mode; Placer.enabledForInput = mode == "edit";
            if (mode == "edit")
            {
                ScenarioPhase = Phase.Idle; _target = null; Vehicle.running = false; Vehicle.gameObject.SetActive(false);
                Time.timeScale = 1f; if (Orbit) Orbit.follow = null;
            }
        }

        public void SetTimeScale(string json) => Time.timeScale = Mathf.Clamp((float)MapJson.Parse<SetTimeScaleMsg>(json).scale, 0.1f, 50f);
```

`StartScenario`·`ResolveScenarioLane` 를 교체:

```csharp
        public void StartScenario(string json)
        {
            var s = MapJson.Parse<StartScenarioMsg>(json);
            _scenarioMode = s?.mode == "unload" ? "unload" : "load";
            _prev = null; SetMode("drive"); Vehicle.gameObject.SetActive(true);
            if (Orbit) { Orbit.follow = Vehicle.transform; Orbit.distance = 25f; Orbit.pitchDeg = 35f; }
            Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "start", mode = _scenarioMode }));
            NextVehicle();
        }

        /// Picks the next slot (spec §3.2/§3.3) and puts a vehicle on the lane start (load) or in the slot (unload). Finishes when none is left.
        void NextVehicle()
        {
            _prev = null;   // a fresh vehicle must not seed Gauss-Newton with the previous car's pose
            _target = CurrentMap == null ? null : ScenarioPlanner.NextSlot(CurrentMap.parking_slots, _scenarioMode);
            _targetLane = _target == null ? null : CurrentMap.lanes?.Find(l => l.id == _target.access_lane_id);
            _targetDeck = _target == null ? null : CurrentMap.decks?.Find(d => d.id == _target.deck_id);
            if (_target == null) { Finish(_scenarioMode == "unload" ? "no_filled_slot" : "no_empty_slot"); return; }
            if (_targetLane == null || _targetDeck == null) { Finish("lane_or_deck_missing:" + _target.id); return; }
            Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "target", slot_id = _target.id }));
            double z = _targetDeck.z_surface;
            if (_scenarioMode == "unload")
            {
                MapOverlay.RemoveParked(_overlay, _target.id);
                Vehicle.StartPath(ScenarioPlanner.DeparturePath(_target.target_pose, _targetLane, z), z, ScenarioPlanner.ParkSpeedMps);
                ScenarioPhase = Phase.Departing;
            }
            else
            {
                _exitS = ScenarioPlanner.ExitS(_targetLane, _target.target_pose);
                Vehicle.StartLane(_targetLane, z);
                ScenarioPhase = Phase.OnLane;
            }
        }

        void Finish(string reason)
        {
            ScenarioPhase = Phase.Idle; _target = null; Vehicle.running = false;
            Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "finished", mode = _scenarioMode, detail = reason }));
        }
```

`Update` 를 `Step` 으로:

```csharp
        void Update() => Step(Time.deltaTime);

        /// One simulation tick: move the vehicle, localize, emit, then run the scenario transitions. Tests call this directly.
        public void Step(float dt)
        {
            if (_mode != "drive" || !Vehicle.running) return;
            Vehicle.Advance(dt);
            var obs = Sensor.Sense(Vehicle.Truth, MapRefs, id => _markers[id].transform.position);
            var res = Localizer.Solve(obs, MapRefs, Sensor.noise.sigmaR, Sensor.noise.sigmaThetaRad, Sensor.noise.sigmaAlphaRad, _prev);
            if (res.ok && double.IsFinite(res.pose.x) && double.IsFinite(res.pose.y) && double.IsFinite(res.pose.psiRad)) _prev = res.pose;
            Hud.Set(res, Vehicle.Truth, "SHIP_AP");
            _emitTimer += dt;
            if (_emitTimer >= 0.2f)
            {
                _emitTimer = 0;
                Send(BridgeMessages.OnLocalization, MapJson.Serialize(new LocalizationEvt { est_x = res.pose.x, est_y = res.pose.y, est_psi = res.pose.psiRad * R2D,
                    true_x = Vehicle.Truth.x, true_y = Vehicle.Truth.y, true_psi = Vehicle.Truth.psiRad * R2D, residual_rms = res.residualRms, n_obs = res.nObs, frame = "SHIP_AP" }));
            }
            StepScenario();
        }

        void StepScenario()
        {
            switch (ScenarioPhase)
            {
                case Phase.OnLane when Vehicle.s >= _exitS || Vehicle.AtEnd:
                {
                    // Plan in the belief frame at the moment of leaving the lane, then execute open-loop in the true frame:
                    // the estimation error at this instant becomes the parking error.
                    // ponytail: open-loop from one estimate; closed-loop pure pursuit on every frame's estimate is the upgrade path.
                    var est = _prev ?? Vehicle.Truth;
                    double z = _targetDeck.z_surface;
                    var path = ScenarioPlanner.Shift(ScenarioPlanner.ApproachPath(est, _target.target_pose, z), Vehicle.Truth.x - est.x, Vehicle.Truth.y - est.y);
                    Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "leave_lane", slot_id = _target.id, detail = $"est x {est.x:F2} y {est.y:F2} psi {est.psiRad * R2D:F1}" }));
                    Vehicle.StartPath(path, z, ScenarioPlanner.ParkSpeedMps);
                    ScenarioPhase = Phase.Parking;
                    break;
                }
                case Phase.Parking when Vehicle.AtEnd:
                {
                    var (status, lat, lon, hdg) = ScenarioPlanner.Judge(Vehicle.Truth, _target.target_pose, _target.tolerance);
                    _target.status = status; MapOverlay.SetStatus(_overlay, _target.id, status);
                    MapOverlay.SpawnParked(_overlay, _target, Vehicle.Truth, _targetDeck.z_surface);
                    Send(BridgeMessages.OnSlotFilled, MapJson.Serialize(new SlotFilledEvt { slot_id = _target.id, status = status, err_lat = lat, err_lon = lon, err_heading = hdg }));
                    NextVehicle();
                    break;
                }
                case Phase.Departing when Vehicle.AtEnd:
                {
                    _target.status = "empty"; MapOverlay.SetStatus(_overlay, _target.id, "empty");
                    Send(BridgeMessages.OnSlotFilled, MapJson.Serialize(new SlotFilledEvt { slot_id = _target.id, status = "empty" }));
                    NextVehicle();
                    break;
                }
            }
        }
```
`Step` 의 첫 줄 조건에 주의: `Vehicle.running` 이 false 가 되는 것은 `Advance` 가 끝점을 지난 뒤이며, 그 프레임의 `StepScenario` 가 전이를 처리하므로 다음 프레임에는 새 경로로 `running` 이 다시 true 다. 종료(`Finish`) 뒤에는 false 로 남아 `Step` 이 조용히 빠져나간다.

`Placer.Created` 핸들러 등 Task 2 의 변경은 유지. `ResolveScenarioLane` 삭제. 사용하지 않게 된 `using` 은 없다.

- [ ] **Step 7: 스텁 창** — `Start load scenario` 옆에

```csharp
                if (GUILayout.Button("Start unload scenario")) rt.StartScenario("{\"mode\":\"unload\"}");
            }
            using (new EditorGUILayout.HorizontalScope())
                foreach (var k in new[] { 1, 5, 20 }) if (GUILayout.Button($"x{k}")) rt.SetTimeScale($"{{\"scale\":{k}}}");
```

- [ ] **Step 8: 통과 확인** — Task 1 Step 2 명령. Expected: total 71, failed 0(Task 3 까지 66 + 이 Task 5). `LoadScenarioParksFirstSlot…` 이 4000 스텝 안에 끝나는지 본다(차로 95 m ÷ 2.78 m/s ≈ 34 s = 690 스텝, 주차 ≈ 3 s). 느리면 `RunUntil` 의 `maxSteps` 가 아니라 `Sense` 의 차폐 레이캐스트가 병목이므로 그대로 둔다.

- [ ] **Step 9: 커밋** — `git add unity/Assets/ShipHdMap && git commit -m "unity: continuous load/unload scenario with parking judgement, onSlotFilled/onScenario, parked cars, SetTimeScale"`

---

### Task 5: 웹 — pose 전달, 주차 결과 반영, 시나리오 로그

**Files:**
- Modify: `web/src/api/types.ts`, `web/src/api/client.ts`, `web/src/store/editor.ts`, `web/src/store/editor.test.ts`, `web/src/bridge/useShipUnity.ts`, `web/src/components/DrivePanel.tsx`

**Interfaces:**
- Produces: `SlotStatus`, `SlotFilledEvt`, `ScenarioEvt`, `ScenarioLine`(types); `api.putSlotStatus(ds, sid, status) → Promise<{id, status}>`; 스토어 `scenarioLog: ScenarioLine[]`, `appendLog(text)`, `clearLog()`, `onSlotFilled(e) → Promise<void>`; 순수 `scenarioLine(e: ScenarioEvt) → string`, `slotFilledLine(e: SlotFilledEvt) → string`; `BridgeName` 에 `"SetTimeScale"`
- Consumes: Unity 이벤트 `onSlotFilled`, `onScenario`(Task 4), `SetPose` 페이로드(Task 2)

- [ ] **Step 1: 실패하는 테스트** — `editor.test.ts` 의 `vi.mock` 에 `putSlotStatus: vi.fn(async (_ds: string, id: string, status: string) => ({ id, status })),` 를 추가하고 파일 끝에:

```ts
import { scenarioLine, slotFilledLine } from "./editor";

describe("scenario log and slot status", () => {
  beforeEach(() => useEditorStore.setState(useEditorStore.getInitialState()));

  it("onSlotFilled PUTs the status, logs a line and refreshes the version", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().onSlotFilled({ slot_id: "PS-D3-012", status: "filled", err_lat: 0.04, err_lon: -0.11, err_heading: 0.6 });
    expect(api.putSlotStatus).toHaveBeenCalledWith("ds1", "PS-D3-012", "filled");
    const s = useEditorStore.getState();
    expect(s.scenarioLog[0].text).toBe("PS-D3-012 filled  lat +0.04 lon -0.11 hdg +0.6°");
    expect(s.scenarioLog[0].t).toMatch(/^\d\d:\d\d:\d\d$/);
    expect(s.error).toBeNull();
  });

  it("unload lines carry no errors and a failed PUT shows the banner", async () => {
    await useEditorStore.getState().load("ds1");
    await useEditorStore.getState().onSlotFilled({ slot_id: "PS-D3-012", status: "empty" });
    expect(useEditorStore.getState().scenarioLog[0].text).toBe("PS-D3-012 empty");
    vi.mocked(api.putSlotStatus).mockRejectedValueOnce(new Error("boom"));
    await useEditorStore.getState().onSlotFilled({ slot_id: "PS-D3-013", status: "filled", err_lat: 0, err_lon: 0, err_heading: 0 });
    expect(useEditorStore.getState().error).toContain("boom");
  });

  it("log keeps the newest 100 lines, newest first, and clearLog empties it", () => {
    for (let i = 0; i < 105; i++) useEditorStore.getState().appendLog("line " + i);
    const log = useEditorStore.getState().scenarioLog;
    expect(log).toHaveLength(100);
    expect(log[0].text).toBe("line 104");
    expect(log[99].text).toBe("line 5");
    useEditorStore.getState().clearLog();
    expect(useEditorStore.getState().scenarioLog).toHaveLength(0);
  });

  it("formats scenario events", () => {
    expect(scenarioLine({ event: "start", mode: "load" })).toBe("▶ 선적 시작");
    expect(scenarioLine({ event: "start", mode: "unload" })).toBe("◀ 하역 시작");
    expect(scenarioLine({ event: "target", slot_id: "PS-D3-001" })).toBe("대상 PS-D3-001");
    expect(scenarioLine({ event: "leave_lane", slot_id: "PS-D3-001", detail: "est x 97.48 y 0.02 psi 0.1" })).toBe("차로 이탈 · est x 97.48 y 0.02 psi 0.1");
    expect(scenarioLine({ event: "finished", detail: "no_empty_slot" })).toBe("종료 (no_empty_slot)");
    expect(slotFilledLine({ slot_id: "PS-D3-002", status: "needs_adjust", err_lat: -0.2, err_lon: 0.05, err_heading: -2.5 })).toBe("PS-D3-002 needs_adjust  lat -0.20 lon +0.05 hdg -2.5°");
  });
});
```

- [ ] **Step 2: 실패 확인** — `cd web && pnpm test`. Expected: 타입 오류/실패(`onSlotFilled`, `scenarioLine` 없음).

- [ ] **Step 3: 타입과 클라이언트**

`types.ts` 끝에:

```ts
export type SlotStatus = "empty" | "filled" | "needs_adjust";
/** Unity -> React: parking judgement (load) or an emptied slot (unload: status "empty", no errors). */
export type SlotFilledEvt = { slot_id: string; status: SlotStatus; err_lat?: number; err_lon?: number; err_heading?: number };
export type ScenarioEvt = { event: "start" | "target" | "leave_lane" | "finished"; mode?: "load" | "unload"; slot_id?: string; detail?: string };
export type ScenarioLine = { t: string; text: string };
```

`client.ts` 의 import 에 `SlotStatus` 를 추가하고 `api` 에:

```ts
  putSlotStatus: (ds: string, sid: string, status: SlotStatus) => req<{ id: string; status: SlotStatus }>("PUT", `/datasets/${ds}/slots/${sid}/status`, { status }),
```

- [ ] **Step 4: 스토어** — `editor.ts`

import 에 `ScenarioEvt, ScenarioLine, SlotFilledEvt` 추가. `EditorState` 에:

```ts
  scenarioLog: ScenarioLine[];
  appendLog: (text: string) => void;
  clearLog: () => void;
  onSlotFilled: (e: SlotFilledEvt) => Promise<void>;
```

초기값에 `scenarioLog: [],`. 액션(`setLocalization` 근처):

```ts
  appendLog: (text) => set((s) => ({ scenarioLog: [{ t: clock(), text }, ...s.scenarioLog].slice(0, 100) })),
  clearLog: () => set({ scenarioLog: [] }),
  /** Unity judged a slot; persist it (the server bumps version) and log it. No scene reload — Unity already recoloured the fill. */
  async onSlotFilled(e) {
    get().appendLog(slotFilledLine(e));
    try { await api.putSlotStatus(get().datasetId, e.slot_id, e.status); await refreshVersion(get); set({ error: null }); }
    catch (err) { set({ error: "slot status failed: " + (err as Error).message }); }
  },
```

파일 끝에 순수 함수:

```ts
const clock = () => new Date().toTimeString().slice(0, 8);
const signed = (v: number, digits: number) => (v >= 0 ? "+" : "") + v.toFixed(digits);

export function slotFilledLine(e: SlotFilledEvt): string {
  if (e.err_lat === undefined || e.err_lon === undefined || e.err_heading === undefined) return `${e.slot_id} ${e.status}`;
  return `${e.slot_id} ${e.status}  lat ${signed(e.err_lat, 2)} lon ${signed(e.err_lon, 2)} hdg ${signed(e.err_heading, 1)}°`;
}

export function scenarioLine(e: ScenarioEvt): string {
  switch (e.event) {
    case "start": return e.mode === "unload" ? "◀ 하역 시작" : "▶ 선적 시작";
    case "target": return `대상 ${e.slot_id}`;
    case "leave_lane": return `차로 이탈 · ${e.detail ?? ""}`;
    case "finished": return `종료 (${e.detail ?? ""})`;
  }
}
```
`erasableSyntaxOnly` 이므로 enum·namespace 를 쓰지 않는다. `switch` 가 모든 경우를 덮어 반환 타입이 `string` 이 아니면 `default: return e.event;` 를 붙인다.

- [ ] **Step 5: 브리지** — `useShipUnity.ts`

`BridgeName` 에 `"SetTimeScale"` 추가. 스토어에서 `pose, ramp, dataset, onSlotFilled, appendLog` 를 더 꺼낸다. `send` 아래:

```ts
  /** Pose for the tilt only (spec §4.6); the ramp angle comes from the API's /ramps response the store already holds. */
  const sendPose = useCallback(() => {
    const { pose, ramp, dataset } = useEditorStore.getState();
    if (!pose) return;
    send("SetPose", { draft_fwd_m: pose.draft_fwd_m ?? 8.1, draft_aft_m: pose.draft_aft_m ?? 8.6, heel_deg: pose.heel_deg ?? 0, lpp_m: dataset?.lpp_m ?? 120,
      ...(ramp ? { ramp: { id: ramp.id, angle_deg: ramp.angle_deg, state: ramp.state } } : {}) });
  }, [send]);
```

Unity → store 효과에 두 리스너 추가(등록·해제 모두):

```ts
    const onSlot = (json: string) => { void onSlotFilled(JSON.parse(json)); };
    const onScenario = (json: string) => appendLog(scenarioLine(JSON.parse(json)));
    addEventListener("onSlotFilled", onSlot); addEventListener("onScenario", onScenario);
    … removeEventListener("onSlotFilled", onSlot); removeEventListener("onScenario", onScenario);
```
(의존성 배열에 `onSlotFilled, appendLog` 추가, `scenarioLine` 은 `../store/editor` 에서 import.)

초기 `Load` 의 `.then` 안 `send("SetDeck", deckFilter);` 다음에 `sendPose();` (의존성에 `sendPose`). 그리고 효과 두 개:

```ts
  useEffect(() => { if (loadedOnce.current) sendPose(); }, [pose, ramp, dataset?.lpp_m, sendPose]);
  useEffect(() => { if (loadedOnce.current) { send("SetMode", mode); if (mode === "edit") send("SetTimeScale", { scale: 1 }); } }, [mode, send]);
```
(기존 `SetMode` 효과를 위 두 번째로 교체.)

- [ ] **Step 6: `DrivePanel.tsx`** — 전체 교체

```tsx
import { useState } from "react";
import { wrapDeg } from "../geo/shipFrame";
import { useEditorStore } from "../store/editor";
import type { BridgeName } from "../bridge/useShipUnity";

type Send = (name: BridgeName, payload?: string | object) => void;
type Sig = { sigma_r: number; sigma_theta: number; sigma_alpha: number };
const SLIDERS: { key: keyof Sig; label: string; max: number; step: number; digits: number }[] = [
  { key: "sigma_r", label: "σ 거리 (m)", max: 1, step: 0.05, digits: 2 },
  { key: "sigma_theta", label: "σ 방위 (°)", max: 5, step: 0.5, digits: 1 },
  { key: "sigma_alpha", label: "σ 방향각 (°)", max: 10, step: 0.5, digits: 1 },
];
const SCALES = [1, 5, 20];

export function DrivePanel({ send }: { send: Send }) {
  const { localization: l, setMode, scenarioLog, clearLog } = useEditorStore();
  const [sig, setSig] = useState<Sig>({ sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2 });
  const [scale, setScale] = useState(1);
  const commit = () => send("SetNoise", { ...sig, sigma_gps: 0.5 }); // on release only — Unity's SetNoise is cheap but the bridge is not a slider event bus
  const start = (mode: "load" | "unload") => { clearLog(); send("SetTimeScale", { scale }); send("StartScenario", { mode }); };
  const err = l ? Math.hypot(l.est_x - l.true_x, l.est_y - l.true_y) : null;
  return (
    <div className="panel">
      <h4>주행 시뮬레이션</h4>
      <div className="row">
        <button className="btn primary" onClick={() => start("load")}>▶ 선적</button>
        <button className="btn" onClick={() => start("unload")}>◀ 하역</button>
        <button className="btn" onClick={() => setMode("edit")}>정지</button>
        <select value={scale} onChange={(e) => { const v = Number(e.target.value); setScale(v); send("SetTimeScale", { scale: v }); }} style={{ flex: "0 0 auto" }}>
          {SCALES.map((k) => <option key={k} value={k}>×{k}</option>)}
        </select>
      </div>
      {SLIDERS.map((s) => (
        <div className="row" key={s.key}><label>{s.label}</label>
          <input type="range" min={0} max={s.max} step={s.step} value={sig[s.key]} onChange={(e) => setSig({ ...sig, [s.key]: Number(e.target.value) })}
            onMouseUp={commit} onKeyUp={commit} onTouchEnd={commit} />
          <span>{sig[s.key].toFixed(s.digits)}</span></div>
      ))}
      <h4 style={{ marginTop: 8 }}>위치 추정</h4>
      {!l ? <span style={{ color: "#888" }}>추정 없음</span> : (
        <div style={{ fontFamily: "monospace", fontSize: 12 }}>
          <div>frame {l.frame} · N {l.n_obs} · RMS {l.residual_rms.toFixed(3)}</div>
          <div>est  x {l.est_x.toFixed(2)} y {l.est_y.toFixed(2)} ψ {l.est_psi.toFixed(1)}</div>
          <div>true x {l.true_x.toFixed(2)} y {l.true_y.toFixed(2)} ψ {l.true_psi.toFixed(1)}</div>
          <div>err {err!.toFixed(2)} m · {wrapDeg(l.est_psi - l.true_psi).toFixed(1)}°</div>
        </div>
      )}
      <h4 style={{ marginTop: 8 }}>시나리오 로그</h4>
      <div style={{ fontFamily: "monospace", fontSize: 11, maxHeight: 220, overflowY: "auto", whiteSpace: "pre" }}>
        {scenarioLog.length === 0 ? <span style={{ color: "#888" }}>없음</span> : scenarioLog.map((line, i) => <div key={i}>{line.t} {line.text}</div>)}
      </div>
    </div>
  );
}
```

- [ ] **Step 7: 통과 확인** — `cd web && pnpm test && pnpm build`. Expected: Vitest 26(22 + 4), tsc/빌드 오류 0. `useShipUnity` 의 의존성 경고(eslint `react-hooks/exhaustive-deps`)가 있으면 배열을 맞춘다.

- [ ] **Step 8: 커밋** — `git add web/src && git commit -m "web: SetPose to Unity, onSlotFilled → slot status PUT, scenario log, load/unload buttons, time scale"`

---

### Task 6: WebGL 재빌드, 계약 문서, 브라우저 검증, README

**Files:**
- Modify: `docs/api-contract.md`, `README.md`
- 산출물: `web/public/unity/`(gitignore)

- [ ] **Step 1: 계약 문서** — `docs/api-contract.md` 브리지 두 줄을 교체

```
- R→U `sendMessage("Map", name, json)`: `Load`, `SetMode("edit"|"drive")`(edit 는 시나리오 중단·차량 숨김·배율 1), `SetDeck("D3"|"all")`, `Select(id)`(씬 하이라이트만; 에코 없음; 빈 문자열이면 해제), `Confirm({tempId,id})`, `Delete(id)`, `SetNoise({sigma_r,sigma_theta,sigma_alpha,sigma_gps})`, `SetPose({draft_fwd_m,draft_aft_m,heel_deg,lpp_m,ramp?:{id,angle_deg,state}})`(Map 루트 회전 + 램프 각도; 각도는 API 가 계산), `StartScenario({mode:"load"|"unload"})`(맵은 `Load` 된 것; 연속 적재/하역), `SetTimeScale({scale})`
- U→R 이벤트(`addEventListener(name, (json) => …)`): `onSeedReady`, `onFeatureCreated{tempId,layer,x,y,z,deck,mounted_on,normal}`, `onFeatureMoved{id,x,y,z,normal,deck,mounted_on}`, `onSelected{id}`, `onLocalization{est_x,est_y,est_psi,true_x,true_y,true_psi,residual_rms,n_obs,frame}`, `onSlotFilled{slot_id,status,err_lat?,err_lon?,err_heading?}`(하역 완료는 `status:"empty"`, 오차 없음; 웹이 `PUT /slots/{sid}/status`), `onScenario{event:start|target|leave_lane|finished,mode?,slot_id?,detail?}`
```
첫 줄의 "쓰기(…)마다 `dataset.version` +1" 은 그대로(구획 상태 포함).

- [ ] **Step 2: 테스트와 빌드** — 에디터 GUI 닫힌 상태, 순차:

```bash
U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
cd unity && "$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' Logs/editmode-results.xml | head -1
"$U" -batchmode -nographics -projectPath "$PWD" -executeMethod ShipHdMap.Editor.WebGLBuild.Build -quit -logFile "$PWD/Logs/webgl-build.log"; grep -E "\[WebGLBuild\]" Logs/webgl-build.log
cd ../web && pnpm test && pnpm build
cd ../api && JAVA_HOME=/opt/homebrew/opt/openjdk ./gradlew test
```
Expected: 71/71, `[WebGLBuild] Succeeded`, Vitest 26, api 42.

- [ ] **Step 3: 기동과 재시드** — `./scripts/m3-dev.sh &` 뒤

```bash
curl -s -o /dev/null -w '%{http_code}\n' -X POST localhost:8081/api/datasets/roro-demo-01/seed -H 'content-type: application/json' --data-binary @docs/fixtures/vehicle-map.sample.json   # 200 (upsert: the 49 old LP ids are a subset of the full grid)
curl -s localhost:8081/api/datasets/roro-demo-01/vehicle-map | python3 -c "import json,sys; m=json.load(sys.stdin); print(len(m['lashing_points']), 'LP')"
curl -s -X POST localhost:8081/api/datasets/roro-demo-01/decks/D3/slots/generate -H 'content-type: application/json' -d '{}' | head -c 160   # lashing_coverage 1.0
```
종료는 컨트롤러 지시에 따른다(브라우저 검증에 서버를 쓴다).

- [ ] **Step 4: README** — `## 상태` 의 `- 다음: M5 주행 시나리오·pose` 를

```
- M5a 갑판 주행·pose 완료 (연속 선적·하역 시나리오, 추정 기반 주차 판정 → DB status, pose 로 선체 기울임·램프 각도, 시나리오 로그; api 42, EditMode 71, Vitest 26)
- 다음: M5b 부두·프레임 전환
```
로 바꾸고 `## 구성` 의 `unity/` 줄 끝에 ", 주행 모드는 연속 적재·하역과 추정 기반 주차 판정" 을, `## 실행` 에 "픽스처를 다시 시드했으면 적재 계획 패널에서 구획을 재생성한다(래싱 매핑 100 %)" 한 줄을 더한다.

- [ ] **Step 5: 브라우저 체크리스트** (컨트롤러가 Chrome 으로; 구현자는 "미수행 (컨트롤러 수행)")

1. 새로고침 → 적재 계획 패널 `재생성` → 래싱 매핑 100 %, 구획 수 1xx, 3D 에 초록 격자, 콘솔 오류 0, 로드 시간 체감 5 초 이내
2. pose 패널 `흘수 선미` 를 9.6 으로 → 3D 선수가 올라감(트림 표시 ≈ 0.48°). 트리에서 `LM-0001` 선택 → 속성 폼 좌표 `12, -6.2, 11.8` 그대로. 마커를 드래그해 놓아도 좌표가 갑판 위 값(z ≈ 11.x)으로 저장됨
3. `횡경사` −3 / +3 → 좌우 기울임; `조위` 를 2.5 → 램프 각도·상태 텍스트가 바뀌고 3D 램프가 따라 움직이며 HUD 마지막 줄에 `ramp …` 표시
4. 주행 탭 → 배율 ×20 → `▶ 선적` → 차량이 차로를 달리다 사선으로 구획에 들어가 멈추고 박스가 남음, 다음 차량이 곧바로 출발. 3대 뒤 로그에 `PS-D3-001 filled …`, `PS-D3-002 …`, `PS-D3-003 …`, 3D 채움면이 파랑, 상단 version 이 3 올라감, `curl localhost:8081/api/datasets/roro-demo-01/vehicle-map | python3 -c "import json,sys; print([s['status'] for s in json.load(sys.stdin)['parking_slots']][:3])"` → `['filled','filled','filled']`
5. `정지` → 편집 탭으로; 다시 주행 탭에서 σ 3개를 0 으로 → `▶ 선적` 3대 모두 `filled`, 오차 0.00
6. σ 거리 1.0, σ 방위 5 → `▶ 선적` → 몇 대 안에 `needs_adjust`(주황 채움면)가 나옴
7. `◀ 하역` → 마지막 구획부터 차량이 나와 선미로 사라지고 로그 `PS-D3-0xx empty`, 채움면 초록, DB status `empty`
8. 브라우저 새로고침 → 남은 주차 박스가 그대로 보이고 `◀ 하역` 이 이어짐

- [ ] **Step 6: 커밋** — `git add README.md docs/api-contract.md && git commit -m "docs: M5a status, bridge contract"`

---

## 자기 검토 메모

- 스펙 커버리지: §1.1 항목 1(T1), 2(T2·T3·T4), 3(T5), 4(스펙 커밋에서 완료), 5(T6). §3 상태기·선적·하역·복원·Planner·배율(T4·T3), §4 원칙·회전·램프·순서·8곳 표·페이로드(T2), §5 브리지·스토어·패널·타입(T5), §6 계약(T4·T5·T6), §7 픽스처(T1), §9 테스트(각 Task), §10 검증 8항목(T6 Step 5), §11 위험: 월드→로컬 누락은 `PoseTests` 3건이, 배율 시 emit 빈도는 T6 항목 4 에서, 픽스처 크기는 T6 항목 1 에서 본다
- §4.5 표 8곳 ↔ T2 Step: 선체 부모(Step 4), Prim·램프(5), SetRampAngle(5), 차량(10), 마커 Spawn·ToModel(7), Placer.Created(4), HUD 커서·라벨(9), 오버레이 LineRenderer(6), 추가로 `LandmarkPlacer.DeckIdForHeight`(8)
- 이름 일관성: `SetPoseMsg/RampMsg`(T2 ↔ T5 `sendPose` 키), `ScenarioPlanner.NextSlot/ExitS/ApproachPath/DeparturePath/Shift/Judge/IsFilled/ParkSpeedMps`(T3 ↔ T4), `MapRuntime.Phase/ScenarioPhase/Step/TargetSlotId/SetTimeScale`(T4 ↔ 테스트), `VehicleController.StartPath/Advance/AtEnd/path/speedMps`(T4), `MapOverlay.SetStatus/SpawnParked/RemoveParked`(T4), `SlotFilledEvt.slot_id/status/err_*`·`ScenarioEvt.event`(C# `evt` + JsonProperty ↔ TS `event`), `api.putSlotStatus`·`onSlotFilled`·`appendLog`·`clearLog`·`scenarioLine`·`slotFilledLine`(T5 ↔ 테스트)
- 테스트 수: Unity 53 → 55(T1) → 60(T2) → 66(T3) → 71(T4); web 22 → 26(T5); api 42 → 42
- 위험: (1) `Quaternion.Euler` 축 부호 — `PoseRotationSigns` 가 잡고 Step 4 가 뒤집는 법을 적었다. (2) `ScenarioRunTests` 의 첫 테스트가 EditMode 에서 수백 번 `Physics.Linecast` 를 돈다 — 선체가 없으므로 차폐 대상이 없어 빠르다. (3) 웹 `useShipUnity` 는 훅 테스트가 없다 — 브라우저 항목 2·3 이 `SetPose` 경로를 덮는다. (4) 픽스처 재시드가 upsert 라 옛 49개 id 가 새 격자 id 와 같은지 T6 Step 3 에서 LP 수로 확인한다(다르면 중복이 아니라 합집합으로 늘어난다 — 그때는 `DELETE FROM feature WHERE layer='LP'` 뒤 재시드)

---

### Task 7: 선수 격벽과 랜드마크 쌍 — 선수 구역 관측 공백 제거

브라우저 검증에서 드러난 구조적 공백을 메운다. 랜드마크가 기둥에만 있어 `x ≤ 108`, `y = ±6.2` 이고, 차로 중앙에서 FOV 90°(반각 45°)로 그 마커를 보려면 전방 거리가 `|y|` 이상이어야 하므로 `x > 101.8` 구간은 관측이 끊긴다. 이탈점은 101.58~111.65 이라 선수 쪽 구획은 낡은 추정으로 경로를 짜고 10 m 넘게 빗나간다. 감지 모델(스펙 §9.4)은 그대로 두고 지도 쪽을 고친다.

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Ship/ShipMeshBuilder.cs`(선수 격벽), `unity/Assets/ShipHdMap/Editor/FixtureExporter.cs`(`SeedLandmarks`)
- Modify: `unity/Assets/ShipHdMap/Tests/EditMode/FixtureExporterTests.cs`, `Tests/EditMode/MapRuntimeTests.cs`, `Tests/EditMode/ShipMeshBuilderTests.cs`
- Modify: `api/src/test/java/com/shiphdmap/api/dataset/SeedImportTests.java`, `api/src/test/java/com/shiphdmap/api/export/GeoJsonExportTests.java`
- Regenerate: `docs/fixtures/vehicle-map.sample.json`

**Interfaces:**
- `ShipMeshBuilder.Build` 이 갑판마다 `"{deck}/Bow"` Cube 를 만든다: 중심 Ship `(lengthM − WallThick/2, 0, z_surface + z_clear/2)`, 크기 `(WallThick, z_clear, beamM)`, 색은 측벽과 같은 `Color(0.4f, 0.45f, 0.5f)`, `ShipStructure` 레이어
- `FixtureExporter.SeedLandmarks` 가 21개를 돌려준다: 기존 19개 + `LM-0020`(`x = lengthM − 0.3`, `y = −3`, `normal (−1,0,0)`) 과 `LM-0021`(같은 x, `y = +3`, 같은 normal), 둘 다 `z = deck.z_surface + 1.2`, `mounted_on = "BOW-{deck.id}"`, 코드는 `20 % 20 = 0`·`21 % 20 = 1`
- 픽스처 랜드마크 19 → 21

- [ ] **Step 1: 실패하는 테스트** — `FixtureExporterTests.cs` 의 `SeedLandmarksSitOnPillarInnerFacesAndHull` 에 이어 붙인다

```csharp
        [Test]
        public void SeedLandmarksCoverTheBowApproach()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var lms = FixtureExporter.SeedLandmarks(seed);
            Assert.That(lms.Count, Is.EqualTo(21));
            var port = lms.First(l => l.id == "LM-0021");
            Assert.That(port.position, Is.EqualTo(new[] { 119.7, 3.0, 11.8 }).Within(1e-9));
            Assert.That(port.normal, Is.EqualTo(new[] { -1.0, 0.0, 0.0 }));
            Assert.That(port.mounted_on, Is.EqualTo("BOW-D3"));
            var stbd = lms.First(l => l.id == "LM-0020");
            Assert.That(stbd.position[1], Is.EqualTo(-3.0).Within(1e-9));

            // the reason they exist: from every lane exit point the bow pair is inside the 90 deg FOV (spec 9.4),
            // which the pillar markers (x <= 108, y = +-6.2) are not past x = 101.8
            foreach (double xExit in new[] { 101.58, 106.08, 111.65 })
            {
                double bearingDeg = Math.Atan2(3.0, 119.7 - xExit) * 180 / Math.PI;
                Assert.That(bearingDeg, Is.LessThan(45), $"bow marker outside the FOV half-angle at x {xExit}");
                Assert.That(119.7 - xExit, Is.LessThan(25), $"bow marker beyond the sensor range at x {xExit}");
            }
        }
```

`ShipMeshBuilderTests.BuildsDeckHierarchyInUnityCoordinates` 의 마지막 단언 앞에 한 줄 더한다:

```csharp
            var bow = ship.transform.Find("D3/Bow");
            Assert.That(bow, Is.Not.Null);
            Assert.That(bow.GetComponent<Collider>().bounds.center.x, Is.EqualTo(119.9f).Within(0.05f)); // inner face at x = 120
```

- [ ] **Step 2: 실패 확인** — 에디터 GUI 닫고

```bash
U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
cd unity && "$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' Logs/editmode-results.xml | head -1
```
Expected: failed ≥ 2 (`Bow` 없음, 랜드마크 19개).

- [ ] **Step 3: 선수 격벽** — `ShipMeshBuilder.Build` 의 `HullStbd` 줄 다음에

```csharp
                // Bow bulkhead: closes the deck at the forward end so landmarks placed there sit on structure and occlude like the hull.
                Prim(deck, "Bow", PrimitiveType.Cube, new Vector3(L - WallThick / 2, z + (float)d.z_clear / 2, 0), new Vector3(WallThick, (float)d.z_clear, B), Color(0.4f, 0.45f, 0.5f), layer, materials);
```

- [ ] **Step 4: 선수 랜드마크 쌍** — `SeedLandmarks` 의 `landmarks.Add(Lm("LM-0019", …));` 다음에

```csharp
            // Bow pair: the pillar rows stop at x = 108 and sit 6.2 m off the centreline, so past x ~ 101.8 nothing is
            // inside the 90 deg FOV from the lane. These two keep every lane exit point observable (spec 9.4).
            double bowX = new ShipParams().lengthM - 0.3;
            landmarks.Add(Lm("LM-0020", 20 % 20, bowX, -3, zTag, -1, 0, 0, "BOW-" + deck.id, deck.id));
            landmarks.Add(Lm("LM-0021", 21 % 20, bowX, 3, zTag, -1, 0, 0, "BOW-" + deck.id, deck.id));
```

- [ ] **Step 5: 통과 확인과 픽스처 재생성** — Step 2 명령으로 77/77 을 확인한 뒤

```bash
cd unity && "$U" -batchmode -nographics -projectPath "$PWD" -executeMethod ShipHdMap.Editor.FixtureExporter.ExportFromSeed -quit -logFile "$PWD/Logs/fixture.log"; grep "Fixture written" Logs/fixture.log
cd .. && python3 -c "
import json; m=json.load(open('docs/fixtures/vehicle-map.sample.json'))
print('LM', len(m['landmarks']), 'LP', len(m['lashing_points']), 'slots', len(m['parking_slots']))
print([l['position'] for l in m['landmarks'] if l['id'] in ('LM-0020','LM-0021')])"
```
Expected: LM 21, LP 4722, slots 2, 좌표 `[119.7, -3.0, 11.8]`·`[119.7, 3.0, 11.8]`.

- [ ] **Step 6: 랜드마크 수를 쓰는 테스트 갱신** — `MapRuntimeTests` 의 `Is.EqualTo(19)` 두 곳(`LoadSpawnsLandmarksAndBuildsMap`, `LoadTwiceReplacesLandmarks`)을 21 로. `SeedImportTests` 의 `layer = 'LM'` 건수 19 → 21 과 총합 `19 + 18 + 1 + 3 + lp + 2` 의 19 → 21. `GeoJsonExportTests` 의 `3 + 19 + …` 의 19 → 21. `VehicleMapExportTests` 에 랜드마크 수 단언이 있으면 같이.

- [ ] **Step 7: 전체 확인** — Unity 77/77, `cd api && JAVA_HOME=/opt/homebrew/opt/openjdk ./gradlew test` 42/42.

- [ ] **Step 8: 커밋**

```bash
git add unity/Assets/ShipHdMap api/src/test docs/fixtures/vehicle-map.sample.json
git commit -m "unity: bow bulkhead and its landmark pair so the lane exit points stay observable"
```
