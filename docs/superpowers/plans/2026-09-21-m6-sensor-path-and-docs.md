# M6 센서 경로·문서 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 웹이 센서 기하 파라미터의 단일 발신자가 되어 커버리지 히트맵과 가상 관측점이 같은 센서를 보게 하고, 주행 한 번을 재현 가능한 문서와 그림으로 남긴다.

**Architecture:** Unity Task 1~2 는 `unity/` 만, 웹 Task 3~5 는 `web/` 만 건드리므로 두 트랙이 독립이고 병렬로 돌아간다. Task 6 이 문서의 거짓 문장을 고치고, Task 7 이 브라우저에서 잇는다. Task 8~10 은 그 위에 캡처와 워크스루 문서를 올린다. 새 런타임 의존성은 없다.

**Tech Stack:** React 19 + Zustand 5 + Vite + TS + Vitest, Unity 6.3 / C# (EditMode + NUnit), Spring Boot 4 (이 마일스톤에서 `api/` 는 건드리지 않는다), bash + Node(내장 WebSocket) + Chrome CDP

**Spec:** `docs/superpowers/specs/2026-09-18-m6-sensor-path-and-docs-design.md`

## Global Constraints

- 웹 들여쓰기 2 칸, C# 4 칸 (기존 파일 그대로)
- **타입 검사는 `pnpm build` 로만 한다. `pnpm tsc --noEmit` 은 이 저장소에서 0 개 파일을 검사한다** — `web/tsconfig.json` 이 `{"files": [], "references": [...]}` 솔루션 설정이라 `--noEmit` 은 참조를 따라가지 않는다. 참조를 따라가는 것은 `tsc -b` 뿐이고 `pnpm build` 가 그것을 부른다
- **Unity 는 HTTP 를 호출하지 않는다.** API 조회는 웹이 하고 브리지로 넘긴다
- **새 런타임 의존성 없음.** devDependency 도 늘리지 않는다
- **새 MonoBehaviour 를 씬에 두지 않는다.** `Assets/Scenes/Demo.unity` 에는 `Map`(MapRuntime)과 카메라뿐이고 나머지는 `MapRuntime.InitForTest()` 가 만든다 (M3a 함정)
- **HUD 문자열은 ASCII 만.** 런타임 UI Toolkit 기본 테마 폰트에 한글 글리프가 없어 WebGL 에서 빈 네모가 된다 (M5f)
- **공개 저장소.** 기관·회사·과제명을 커밋 메시지·코드·문서 어디에도 쓰지 않는다. 푸시 전 `grep -rniE "회사|기관|과제|주식회사|연구원|사업|대학|병원"` 으로 점검한다
- Unity 배치 테스트는 `-quit` 없이: `-batchmode -projectPath unity -runTests -testPlatform EditMode -testResults <경로>`. `-quit` 를 같이 주면 이 버전(6000.3.24f1)은 테스트를 조용히 건너뛴다. 빌드(`-executeMethod ShipHdMap.Editor.WebGLBuild.Build`)는 `-quit` 가 필요하다
- 커밋 메시지는 한 줄 영문 요약. 자명하지 않은 변경은 본문에 왜 그랬는지 적는다. 본문 끝에:
  `Co-Authored-By: Claude Opus 5 <noreply@anthropic.com>`

**시작 기준선:** Unity EditMode **160**, Vitest **87** (12 파일), api **79** (이 마일스톤에서 안 건드림). 브랜치는 `main`(`3e66b79`)에서 딴다.

---

## File Structure

| 파일 | 책임 | 트랙 |
| --- | --- | --- |
| `unity/…/Bridge/BridgeMessages.cs` | `SetSensor` 상수 + `SetSensorMsg` | Unity T1 |
| `unity/…/Bridge/MapRuntime.cs` | `SetSensor` 배선, 관측점 재조준, HUD 줄 매 프레임 갱신 | Unity T1·T2 |
| `unity/…/Vehicle/HudView.cs` | `SensorConfigLine` (순수) + `SetSensorConfig` + 출력 순서 | Unity T2 |
| `unity/…/Tests/EditMode/MapRuntimeTests.cs` | `SetSensor` 대입·재조준 | Unity T1 |
| `unity/…/Tests/EditMode/HudViewTests.cs` | `SensorConfigLine` 서식 | Unity T2 |
| `web/src/store/editor.ts` | `noise` → `sigmaGps`, `noiseMsg`·`sensorMsg` 조립, 스키마 2 | 웹 T3 |
| `web/src/store/editor.test.ts` | persist 키 집합 (먼저 고친다), 조립 함수 | 웹 T3 |
| `web/src/bridge/useShipUnity.ts` | `editorStateMessages` 에 `SetSensor`, 커버리지 변화 전송 | 웹 T4·T5 |
| `web/src/bridge/bridge.test.ts` | 새 메시지 목록·페이로드 | 웹 T4 |
| `web/src/components/DrivePanel.tsx` | σ 슬라이더 3개 제거, 읽기 전용 줄 | 웹 T5 |
| `web/src/geo/coverage.ts` | `SENSOR_DEFAULTS` 주석을 3자 등식으로 | 문서 T6 |
| `docs/api-contract.md` | "Unity 와 동일" 문장을 사실로 | 문서 T6 |
| `scripts/capture.sh` | 캡처 전용 데이터셋 시드 + 서버 기동 + 드라이버 호출 | 문서 T8 |
| `scripts/capture.mjs` | CDP 드라이버 — 장별 상태·이벤트 대기·PNG·요약표 | 문서 T8 |
| `docs/architecture-walkthrough.md` | **신규.** 주행 한 번을 1~8장으로 | 문서 T9 |
| `docs/img/` | **신규.** 캡처 산출물 | 문서 T8·T9 |

---

### Task 1: Unity — `SetSensor` 가 기하 세 값을 바꾸고 살아 있는 관측점을 다시 겨눈다

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs` (`SetTool` 바로 위)
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs`

**Interfaces:**
- Produces: `BridgeMessages.SetSensor` (문자열 상수 `"SetSensor"`)
- Produces: `class SetSensorMsg { public double fov_deg, max_dist_m, max_view_angle_deg; }`
- Produces: `MapRuntime.SetSensor(string json)` — 웹이 `sendMessage("Map", "SetSensor", json)` 으로 부른다
- Consumes: 기존 `LandmarkSensor.fovDeg` / `.maxDist` / `.maxViewAngleDeg` (전부 `public float`), `MapRuntime.Probe` (`ProbeView`), `ProbeView.Active`, `ProbeView.Aim()`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs` 의 마지막 `[Test]` 뒤, 클래스 닫는 중괄호 앞에 붙인다:

```csharp
        [Test]
        public void SetSensorMovesTheThreeGeometryFields()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            rt.SetSensor("{\"fov_deg\":55,\"max_dist_m\":12,\"max_view_angle_deg\":40}");
            Assert.That(rt.Sensor.fovDeg, Is.EqualTo(55f).Within(1e-4f));
            Assert.That(rt.Sensor.maxDist, Is.EqualTo(12f).Within(1e-4f));
            Assert.That(rt.Sensor.maxViewAngleDeg, Is.EqualTo(40f).Within(1e-4f));
        }

        [Test]
        public void SetSensorReAimsALiveProbeInsteadOfWaitingForTheNextClick()
        {
            // The drive re-reads the three fields every frame through VisibleFrom, so it needs nothing. The probe
            // only recomputes in PlaceAt and on an arrow key -- without the re-aim its cone and its count keep the
            // old angle until the operator happens to touch it, which is exactly the disagreement M6 exists to end.
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest(); rt.Load(Fixture());
            var d3 = rt.CurrentMap.decks.Find(d => d.id == "D3");
            Assert.That(d3, Is.Not.Null, "fixture must carry Deck 3");
            rt.Probe.PlaceAt(rt.transform.TransformPoint(ShipFrame.ToUnity(60, 0, d3.z_surface)), d3.z_surface);
            string wide = rt.Hud.SensorText;
            Assert.That(wide, Does.StartWith("seen "), "placing the probe should already have produced a count");

            rt.SetSensor("{\"fov_deg\":10,\"max_dist_m\":25,\"max_view_angle_deg\":70}");

            Assert.That(rt.Hud.SensorText, Is.Not.EqualTo(wide), "narrowing the cone to 10 deg must re-aim the live probe");
        }
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath "$PWD/unity" -runTests -testPlatform EditMode \
  -testResults /tmp/m6-t1.xml -logFile /tmp/m6-t1.log
```
Expected: 컴파일 실패 — `error CS1061: 'MapRuntime' does not contain a definition for 'SetSensor'`. 로그에서 `grep "error CS" /tmp/m6-t1.log` 로 확인한다.

- [ ] **Step 3: 메시지 클래스를 더한다**

`BridgeMessages.cs` 의 `SetNormal = "SetNormal"` 뒤에 `, SetSensor = "SetSensor"` 를 붙이고, `SetNoiseMsg` 바로 위에 클래스를 넣는다:

```csharp
    /// Sensor geometry. The field names are the coverage API's own (`POST /datasets/{id}/decks/{deck}/coverage`)
    /// and that is the whole point: the heatmap and the probe have to read the same three numbers, and identical
    /// names are what lets a person check that by eye. Noise is NOT here -- SetNoise owns it, one owner per number.
    /// The defaults match LandmarkSensor's own fields, so a message with a missing key leaves the value where it was.
    public class SetSensorMsg { public double fov_deg = 90, max_dist_m = 25, max_view_angle_deg = 70; }
```

- [ ] **Step 4: 최소 구현을 쓴다**

`MapRuntime.cs` 의 `public void SetTool(string json)` **바로 위**에 넣는다:

```csharp
        /// The web owns the sensor's geometry (M6 spec §3). Until this arrives Unity runs on LandmarkSensor's
        /// own field defaults, which is the only reason those defaults must stay equal to the API's --
        /// docs/api-contract.md states that equality and nothing but this message enforces it.
        public void SetSensor(string json)
        {
            var m = MapJson.Parse<SetSensorMsg>(json); if (m == null) return;
            Sensor.fovDeg = (float)m.fov_deg;
            Sensor.maxDist = (float)m.max_dist_m;
            Sensor.maxViewAngleDeg = (float)m.max_view_angle_deg;
            // Drive: VisibleFrom reads these every frame, nothing to do. Probe: Aim() runs only on a click or an
            // arrow key, so without this it keeps drawing and counting through the old cone.
            if (Probe.Active) Probe.Aim();
        }
```

- [ ] **Step 5: 테스트가 통과하는지 확인한다**

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath "$PWD/unity" -runTests -testPlatform EditMode \
  -testResults /tmp/m6-t1.xml -logFile /tmp/m6-t1.log
python3 -c "import xml.etree.ElementTree as ET;r=ET.parse('/tmp/m6-t1.xml').getroot();print(r.get('total'),r.get('passed'),r.get('failed'))"
```
Expected: `162 162 0`

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs \
        unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs \
        unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs
git commit -m "feat(unity): let the web set the sensor's geometry, and re-aim a live probe when it does"
```

---

### Task 2: Unity — HUD 가 자기가 무엇으로 보는지 말한다

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Vehicle/HudView.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs` (`Update()`)
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/HudViewTests.cs`

**Interfaces:**
- Consumes: Task 1 의 `MapRuntime.SetSensor` (아무것도 직접 쓰지 않지만 이 줄이 보여 주는 값이 거기서 온다)
- Produces: `static string HudView.SensorConfigLine(double fovDeg, double maxDist, double maxViewAngleDeg)`
- Produces: `HudView.SetSensorConfig(string line)` 과 읽기용 `HudView.SensorConfigText`
- Consumes: 기존 `HudView.StatusText` / `SensorText` / `Extra(string)` / `LateUpdate`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/HudViewTests.cs` 의 `StatusLineNamesTheProbeEyeEvenThoughTheCameraSaysDriver` 뒤에 붙인다:

```csharp
        [Test]
        public void SensorConfigLineDropsTrailingZerosButKeepsAHalfStep()
        {
            // The sliders step in whole degrees and metres, so the common case must not read "90.0"; a value
            // that is not whole still has to survive, or the line would quietly lie about what the drive uses.
            Assert.That(HudView.SensorConfigLine(90, 25, 70), Is.EqualTo("sensor  fov 90  range 25 m  view 70"));
            Assert.That(HudView.SensorConfigLine(55.5, 12.5, 70), Is.EqualTo("sensor  fov 55.5  range 12.5 m  view 70"));
        }
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath "$PWD/unity" -runTests -testPlatform EditMode \
  -testResults /tmp/m6-t2.xml -logFile /tmp/m6-t2.log
grep "error CS" /tmp/m6-t2.log | head -2
```
Expected: `error CS0117: 'HudView' does not contain a definition for 'SensorConfigLine'`

- [ ] **Step 3: 순수 함수와 저장소를 더한다**

`HudView.cs` 의 `public string SensorText { get; private set; }` 뒤에 한 줄:

```csharp
        public string SensorConfigText { get; private set; }
```

`public void SetSensor(string line) { SensorText = line; }` 뒤에 한 줄:

```csharp
        public void SetSensorConfig(string line) { SensorConfigText = line; }
```

`SensorLine` 의 바로 위에 순수 함수를 넣는다:

```csharp
        /// What the eye is SET TO, as opposed to what it found. It sits directly above SensorLine so the counts
        /// are read next to the numbers that produced them -- "seen 1 / 23" means nothing without "fov 55".
        ///
        /// ASCII only, for the reason StatusLine gives. "0.#" rather than "F0" because the coverage sliders step
        /// in whole units today but nothing stops a future one from landing on 55.5, and a rounded line that
        /// disagrees with the panel beside it would be worse than no line.
        public static string SensorConfigLine(double fovDeg, double maxDist, double maxViewAngleDeg)
            => $"sensor  fov {fovDeg:0.#}  range {maxDist:0.#} m  view {maxViewAngleDeg:0.#}";
```

- [ ] **Step 4: 출력 순서에 끼운다**

`HudView.LateUpdate` 의 `_info.text = …` 를 고친다 — `SensorConfigText` 가 `SensorText` **앞**에 온다:

```csharp
            _info.text = string.Join("\n", Lines(_deck, _selected, CursorShip(), _last, _truth, _frame))
                + Extra(_ramp) + Extra(StatusText) + Extra(SensorConfigText) + Extra(SensorText) + Extra(_flash);
```

- [ ] **Step 5: 매 프레임 갱신을 배선한다**

`MapRuntime.Update()` 의 `Hud.SetStatus(...)` 바로 뒤에 한 줄을 넣는다:

```csharp
            Hud.SetSensorConfig(HudView.SensorConfigLine(Sensor.fovDeg, Sensor.maxDist, Sensor.maxViewAngleDeg));
```

같은 이유로 매 프레임 다시 계산한다 — `SetSensor` 한 곳에서 밀어넣으면 인스펙터에서 값을 바꾸거나 나중에 다른 발신자가 생겼을 때 줄이 거짓이 된다. 이 줄은 `SensorText` 와 달리 **지우지 않는다**: 센서 설정은 눈이 살아 있든 아니든 항상 참이다.

- [ ] **Step 6: 테스트가 통과하는지 확인한다**

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath "$PWD/unity" -runTests -testPlatform EditMode \
  -testResults /tmp/m6-t2.xml -logFile /tmp/m6-t2.log
python3 -c "import xml.etree.ElementTree as ET;r=ET.parse('/tmp/m6-t2.xml').getroot();print(r.get('total'),r.get('passed'),r.get('failed'))"
```
Expected: `163 163 0`

- [ ] **Step 7: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Vehicle/HudView.cs \
        unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs \
        unity/Assets/ShipHdMap/Tests/EditMode/HudViewTests.cs
git commit -m "feat(unity): put the sensor the eye is using right above the count it produced"
```

---

### Task 3: 웹 — σ 를 커버리지 쪽으로 합치고 스키마를 2 로 올린다

**Files:**
- Modify: `web/src/store/editor.ts`
- Test: `web/src/store/editor.test.ts:164-170` (**먼저 고친다**)

**Interfaces:**
- Produces: `EDITOR_SCHEMA = 2`
- Produces: `EditorState.sigmaGps: number` (기본 `0.5`) — `EditorState.noise` 를 **대체**한다
- Produces: `setSigmaGps(v: number): void` — `setNoise` 를 **대체**한다
- Produces: `noiseMsg(s: Pick<EditorState, "coverageParams" | "sigmaGps">): NoiseParams`
- Produces: `sensorMsg(s: Pick<EditorState, "coverageParams">): { fov_deg: number; max_dist_m: number; max_view_angle_deg: number }`
- Consumes: 기존 `EditorState.coverageParams: CoverageSensor & { grid_m: number }`, `NoiseParams` (`web/src/api/types.ts`)

- [ ] **Step 1: persist 키 테스트를 새 집합으로 고친다 (이게 실패하는 테스트다)**

`web/src/store/editor.test.ts:168` 의 배열을 바꾼다. `noise` 가 빠지고 `sigmaGps` 가 들어온다 — 알파벳 순서라 위치가 다르다:

```ts
      ["beliefParams", "coverageMode", "coverageParams", "deckFilter", "mode", "occluded", "sigmaGps", "timeScale"],
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

```bash
cd web && pnpm vitest run src/store/editor.test.ts 2>&1 | tail -20
```
Expected: FAIL — `저장하는 키가 정확히 이것뿐이다`, 받은 배열에 `noise` 가 있고 `sigmaGps` 가 없다

- [ ] **Step 3: 스토어를 바꾼다**

`web/src/store/editor.ts` 에서 네 곳을 고친다.

`EDITOR_SCHEMA`:
```ts
// 2: M6 moved sigma r/theta/alpha into coverageParams and left only sigma_gps behind, so a v1 blob has
// `noise` and no `sigmaGps`. zustand drops a version mismatch outright (see the persist block's comment),
// which is the wanted behaviour here -- there is nothing in a v1 blob worth migrating.
export const EDITOR_SCHEMA = 2;
```

상태 선언(`noise: NoiseParams;` 자리):
```ts
  sigmaGps: number;
```

액션 선언(`setNoise` 자리):
```ts
  setSigmaGps: (v: number) => void;
```

기본값(`noise: { … }, timeScale: 1,` 자리):
```ts
  sigmaGps: 0.5, timeScale: 1,
```

액션 구현(`setNoise: …` 자리):
```ts
  setSigmaGps: (v) => set({ sigmaGps: v }),
```

`partialize`(`noise: s.noise,` 자리):
```ts
      beliefParams: s.beliefParams, sigmaGps: s.sigmaGps, timeScale: s.timeScale, occluded: s.occluded,
```

- [ ] **Step 4: 조립 함수 두 개를 더한다**

`editor.ts` 파일 끝, `visibleFeatures` 위에 넣는다:

```ts
/**
 * The SetNoise payload, assembled. sigma r/theta/alpha live in coverageParams so the heatmap's PREDICTION and
 * the drive's MEASUREMENT cannot be set to different numbers (M6 spec §4); sigma_gps has no coverage
 * counterpart -- the quay leg has no landmarks -- so it stays on its own.
 *
 * Four places send SetNoise: the reload replay, the sigma-GPS slider's release, the scenario start, and the
 * coverage slider effect. They all come through here, or the four drift.
 */
export function noiseMsg(s: Pick<EditorState, "coverageParams" | "sigmaGps">): NoiseParams {
  const { sigma_r, sigma_theta, sigma_alpha } = s.coverageParams;
  return { sigma_r, sigma_theta, sigma_alpha, sigma_gps: s.sigmaGps };
}

/**
 * The SetSensor payload: the same three names the coverage API takes, and nothing else (M6 spec §3.1).
 * Picked field by field on purpose -- coverageParams also carries grid_m and the three sigmas, and spreading
 * it would hand Unity keys SetSensorMsg has no field for, which Newtonsoft drops in silence.
 */
export function sensorMsg(s: Pick<EditorState, "coverageParams">) {
  const { fov_deg, max_dist_m, max_view_angle_deg } = s.coverageParams;
  return { fov_deg, max_dist_m, max_view_angle_deg };
}
```

- [ ] **Step 5: 조립 함수 테스트를 더한다**

`web/src/store/editor.test.ts` 파일 끝에 붙인다:

```ts
import { noiseMsg, sensorMsg } from "./editor";

describe("bridge payload assembly", () => {
  const s = {
    coverageParams: { fov_deg: 55, max_dist_m: 12, max_view_angle_deg: 40, sigma_r: 0.4, sigma_theta: 2, sigma_alpha: 3, grid_m: 2 },
    sigmaGps: 0.7,
  };

  it("SetNoise takes its three sigmas from coverageParams and sigma_gps from the store", () => {
    expect(noiseMsg(s)).toEqual({ sigma_r: 0.4, sigma_theta: 2, sigma_alpha: 3, sigma_gps: 0.7 });
  });

  it("SetSensor carries the API's three names and nothing else", () => {
    // grid_m and the sigmas must not ride along: SetSensorMsg has no field for them and Newtonsoft drops
    // unknown keys without a word, so a spread would look fine and quietly send a shape nobody reads.
    expect(sensorMsg(s)).toEqual({ fov_deg: 55, max_dist_m: 12, max_view_angle_deg: 40 });
  });
});
```

- [ ] **Step 6: v1 블롭이 되살아나지 않는지 고정한다**

같은 `describe("bridge payload assembly")` 아래에 붙인다. 스키마를 안 올렸을 때 정확히 무엇이 깨지는지를 못박는 테스트다:

```ts
import { EDITOR_KEY, EDITOR_SCHEMA, useEditorStore } from "./editor";

describe("persisted schema", () => {
  it("drops a v1 blob instead of restoring it without sigmaGps", async () => {
    // A v1 blob has `noise` and no `sigmaGps`. Restoring it would leave sigmaGps undefined, and the first
    // noiseMsg() would put `sigma_gps: undefined` on the wire -- Newtonsoft then leaves the field at
    // SetNoiseMsg's default and the quay leg silently drives on a sigma nobody chose.
    localStorage.setItem(EDITOR_KEY, JSON.stringify({ version: 1, state: { noise: { sigma_r: 9, sigma_theta: 9, sigma_alpha: 9, sigma_gps: 9 }, deckFilter: "D2" } }));
    await useEditorStore.persist.rehydrate();
    expect(useEditorStore.getState().sigmaGps).toBe(0.5);
    expect(useEditorStore.getState().deckFilter).not.toBe("D2");
  });

  it("is on version 2", () => { expect(EDITOR_SCHEMA).toBe(2); });
});
```

- [ ] **Step 7: 테스트가 통과하는지 확인한다**

```bash
cd web && pnpm vitest run src/store/editor.test.ts 2>&1 | tail -6
```
Expected: PASS. 이 시점에 `DrivePanel.tsx` 는 아직 `noise`·`setNoise` 를 쓰므로 `pnpm build` 는 실패한다 — Task 5 가 고친다. 지금은 **이 테스트 파일만** 돌린다.

- [ ] **Step 8: 커밋**

```bash
git add web/src/store/editor.ts web/src/store/editor.test.ts
git commit -m "refactor(web): one sigma set, owned by the coverage panel, assembled in one place"
```

---

### Task 4: 웹 — `editorStateMessages` 가 센서도 재생한다

**Files:**
- Modify: `web/src/bridge/useShipUnity.ts` (`BridgeName`, `editorStateMessages`)
- Test: `web/src/bridge/bridge.test.ts`

**Interfaces:**
- Consumes: Task 3 의 `noiseMsg`, `sensorMsg`, `EditorState.sigmaGps`
- Produces: `BridgeName` 에 `"SetSensor"` 추가
- Produces: `editorStateMessages(ui: { tool: Tool; cam: CamMode }, ed: Pick<EditorState, "coverageParams" | "sigmaGps" | "timeScale" | "occluded">): [BridgeName, object][]` — 인자 타입이 바뀐다

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`web/src/bridge/bridge.test.ts` 를 통째로 바꾼다:

```ts
import { describe, expect, it } from "vitest";
import { editorStateMessages } from "./useShipUnity";
import { useUiStore } from "../store/ui";
import { useEditorStore } from "../store/editor";

describe("editorStateMessages", () => {
  const ui = { tool: "place" as const, cam: "fly" as const };
  const ed = {
    coverageParams: { fov_deg: 55, max_dist_m: 12, max_view_angle_deg: 40, sigma_r: 0.3, sigma_theta: 2, sigma_alpha: 3, grid_m: 1 },
    sigmaGps: 0.6, timeScale: 4, occluded: ["LM-0001"],
  };

  it("replays every piece of state Unity forgets on a Load", () => {
    // If one of these ever drops off the list the symptom is silent: the scene simply behaves as if
    // the user had never set it, and only after a reload.
    expect(editorStateMessages(ui, ed).map(([name]) => name)).toEqual(
      ["SetTool", "SetCamMode", "SetSensor", "SetNoise", "SetTimeScale", "SetOccluded"],
    );
  });

  it("wraps each payload the way its Unity message class reads it", () => {
    // SetTimeScaleMsg has `scale` and SetOccludedMsg has `ids`, but SetNoiseMsg IS the noise object.
    // Send a bare number or a bare array and Newtonsoft leaves the field at its default, in silence.
    expect(Object.fromEntries(editorStateMessages(ui, ed))).toEqual({
      SetTool: { tool: "place" },
      SetCamMode: { mode: "fly" },
      SetSensor: { fov_deg: 55, max_dist_m: 12, max_view_angle_deg: 40 },
      SetNoise: { sigma_r: 0.3, sigma_theta: 2, sigma_alpha: 3, sigma_gps: 0.6 },
      SetTimeScale: { scale: 4 },
      SetOccluded: { ids: ["LM-0001"] },
    });
  });

  it("sends the sensor Unity will actually run on, not the panel's spare fields", () => {
    // grid_m and the sigmas share coverageParams with the three geometry names; SetSensorMsg has no field
    // for them. This is the reload path, where a wrong shape is hardest to notice.
    expect(Object.fromEntries(editorStateMessages(ui, ed)).SetSensor).not.toHaveProperty("grid_m");
    expect(Object.fromEntries(editorStateMessages(ui, ed)).SetSensor).not.toHaveProperty("sigma_r");
  });

  it("accepts the live stores' own defaults, so the first replay after a reload is well formed", () => {
    // The two stores are the real arguments; this is what catches a field being renamed on one side only.
    const msgs = Object.fromEntries(editorStateMessages(useUiStore.getState(), useEditorStore.getState()));
    expect(msgs.SetTool).toEqual({ tool: "select" });
    expect(msgs.SetCamMode).toEqual({ mode: "orbit" });
    expect(msgs.SetOccluded).toEqual({ ids: [] });
    expect(msgs.SetTimeScale).toEqual({ scale: 1 });
    expect(msgs.SetSensor).toEqual({ fov_deg: 90, max_dist_m: 25, max_view_angle_deg: 70 });
    expect(msgs.SetNoise).toEqual({ sigma_r: 0.2, sigma_theta: 1, sigma_alpha: 2, sigma_gps: 0.5 });
  });
});
```

- [ ] **Step 2: 테스트가 실패하는지 확인한다**

```bash
cd web && pnpm vitest run src/bridge/bridge.test.ts 2>&1 | tail -20
```
Expected: FAIL — 목록에 `SetSensor` 가 없다

- [ ] **Step 3: 최소 구현을 쓴다**

`web/src/bridge/useShipUnity.ts` 에서:

`BridgeName` 유니온 끝에 `| "SetSensor"` 를 더한다.

임포트에 조립 함수를 더한다:
```ts
import { noiseMsg, sensorMsg, useEditorStore } from "../store/editor";
```
(기존 `useEditorStore` 임포트 줄에 합친다. `EditorState` 타입도 필요하면 `import type { EditorState } from "../store/editor";`)

`editorStateMessages` 를 바꾼다:
```ts
export function editorStateMessages(
  ui: { tool: Tool; cam: CamMode },
  ed: Pick<EditorState, "coverageParams" | "sigmaGps" | "timeScale" | "occluded">,
): [BridgeName, object][] {
  return [
    ["SetTool", { tool: ui.tool }],
    ["SetCamMode", { mode: ui.cam }],
    // Before SetNoise on purpose: both end up in LandmarkSensor, and a reader of the replay should meet the
    // geometry before the noise that is measured through it.
    ["SetSensor", sensorMsg(ed)],
    ["SetNoise", noiseMsg(ed)],
    ["SetTimeScale", { scale: ed.timeScale }],
    ["SetOccluded", { ids: ed.occluded }],
  ];
}
```

`sendEditorState` 의 주석도 사실에 맞춘다 — 이제 진짜로 센서를 재전송한다:
```ts
  /** Everything Unity forgets on a fresh Load or a page reload: the tool, the camera, the sensor's geometry,
   *  its noise and the occlusion set. */
```

- [ ] **Step 4: 테스트가 통과하는지 확인한다**

```bash
cd web && pnpm vitest run src/bridge/bridge.test.ts 2>&1 | tail -6
```
Expected: PASS (4 tests)

- [ ] **Step 5: 커밋**

```bash
git add web/src/bridge/useShipUnity.ts web/src/bridge/bridge.test.ts
git commit -m "feat(web): replay the sensor's geometry too -- the comment promised it, the list did not"
```

---

### Task 5: 웹 — 슬라이더가 움직이면 바로 보내고, 주행 패널을 정리한다

**Files:**
- Modify: `web/src/bridge/useShipUnity.ts` (새 `useEffect`)
- Modify: `web/src/components/DrivePanel.tsx`

**Interfaces:**
- Consumes: Task 3 의 `noiseMsg`, `sensorMsg`, `setSigmaGps`, `sigmaGps`; Task 4 의 `BridgeName` 에 추가된 `"SetSensor"`
- Produces: 없음 (배선과 UI 만)

- [ ] **Step 1: 라이브 전송 효과를 더한다**

`useShipUnity` 의 `useEditorStore()` 구조분해에 `coverageParams` 를 더하고, `send("SetMode", mode)` 효과 줄 뒤에 넣는다:

```ts
  // Sent on every change, not on release like the coverage POST. Three doubles over the bridge cost nothing,
  // and a release-only trigger has a hole in it -- moving the slider with the arrow keys never fires mouseup.
  // The visible effect is that the 3D cone narrows while the drag is still happening and the heatmap catches
  // up when it ends, which is the two screens showing that they are wired to one number.
  useEffect(() => {
    if (!loadedOnce.current) return;
    const ed = useEditorStore.getState();
    send("SetSensor", sensorMsg(ed));
    send("SetNoise", noiseMsg(ed));
  }, [coverageParams, send]);
```

`SetNoise` 도 같이 보내는 이유: σ 삼총사가 `coverageParams` 안에 있으므로 커버리지 슬라이더 하나가 두 메시지의 출처다. 이게 없으면 완료 기준 4 가 성립하지 않는다 — `editorStateMessages` 는 재로드 때만 돈다.

- [ ] **Step 2: 주행 패널의 σ 슬라이더 셋을 지운다**

`DrivePanel.tsx` 상단의 `SIGMAS` 배열에서 앞의 세 줄을 지우고 `sigma_gps` 만 남긴다:

```ts
const SIGMAS = [
  { key: "sigma_gps", label: "σ GPS (m)", max: 3, step: 0.1, digits: 1 },   // quay leg only: decides how squarely the vehicle enters the ramp
] as const;
```
배열 한 줄짜리를 남기는 이유: 렌더 루프가 이미 이 모양이고, 인라인 슬라이더로 펴는 편이 지금은 더 큰 diff 다.

- [ ] **Step 3: 스토어 사용을 바꾼다**

구조분해에서 `noise: sig, setNoise` 를 `sigmaGps, setSigmaGps, coverageParams` 로 바꾸고, `commit` 과 시나리오 시작의 재전송을 조립 함수로 바꾼다:

```ts
  const commit = () => send("SetNoise", noiseMsg(useEditorStore.getState())); // on release only — Unity's SetNoise is cheap but the bridge is not a slider event bus
```

시나리오 시작 쪽(기존 `send("SetNoise", sig);` 줄):
```ts
    send("SetNoise", noiseMsg(useEditorStore.getState())); // reload can restore a saved value while Unity still holds SetNoiseMsg's default -- resend it every start
```

슬라이더 렌더에서 `sig[s.key]` → `sigmaGps`, `setNoise({ [s.key]: … })` → `setSigmaGps(Number(e.target.value))`.

- [ ] **Step 4: 읽기 전용 줄을 더한다**

σ GPS 슬라이더 바로 위에 넣는다:

```tsx
      <div className="row">
        <label>σ</label>
        <span>거리 {coverageParams.sigma_r.toFixed(2)} m · 방위 {coverageParams.sigma_theta.toFixed(1)}° · 방향각 {coverageParams.sigma_alpha.toFixed(1)}°
          {" "}<span style={{ color: "#888" }}>(커버리지 탭에서 조정)</span></span>
      </div>
```
주행을 보면서 σ 를 만지려면 탭을 건너가야 한다는 대가를 메우는 한 줄이다 — 슬라이더는 늘지 않고, 주행 화면은 자기가 무슨 노이즈로 달리는지 계속 말한다.

- [ ] **Step 5: 전체 검사를 돌린다**

```bash
cd web && pnpm vitest run 2>&1 | tail -6 && pnpm build 2>&1 | tail -4
```
Expected: Vitest `92 passed` (87 + Task 3 의 4 + Task 4 의 1 — `bridge.test.ts` 는 3 개에서 4 개로 는다), `pnpm build` 성공. **여기서 처음으로 `pnpm build` 가 통과한다** — Task 3 이 남긴 타입 오류가 이 태스크에서 닫힌다.

- [ ] **Step 6: oxlint 을 돌린다**

```bash
cd web && pnpm lint 2>&1 | tail -5
```
Expected: 오류 0. 경고는 기존 4개를 넘지 않는다.

- [ ] **Step 7: 커밋**

```bash
git add web/src/bridge/useShipUnity.ts web/src/components/DrivePanel.tsx
git commit -m "feat(web): push the sensor on every slider tick, and give the drive panel one source of sigma"
```

---

### Task 6: 문서의 거짓 문장 둘을 고친다

**Files:**
- Modify: `docs/api-contract.md:20`
- Modify: `web/src/geo/coverage.ts:4`

**Interfaces:**
- Consumes: Task 1~5 (문장이 사실이 되려면 경로가 먼저 있어야 한다)
- Produces: 없음

- [ ] **Step 1: API 계약서의 문장을 사실로 고친다**

`docs/api-contract.md:20` 의 괄호 안 `(기본은 Unity `LandmarkSensor` 와 동일, 각도는 도)` 를 바꾼다:

```
(기본은 Unity `LandmarkSensor` 의 필드 기본값과 같은 값이고, 각도는 도. **등식을 지키는 것은 실행 중의 `SetSensor` 브리지 메시지다** — 웹이 이 세 값을 API 와 Unity 양쪽에 같은 이름으로 보내므로, 기본값 세 벌이 어긋나 있어도 화면에 사는 것은 웹이 보낸 한 값뿐이다. 첫 `Load` 전까지만 Unity 의 기본값이 산다)
```

- [ ] **Step 2: 웹 상수의 주석을 3자 등식으로 고친다**

`web/src/geo/coverage.ts:4` 의 한 줄 주석을 바꾼다:

```ts
/**
 * The three geometry names are also Unity's (LandmarkSensor) and the API's (CoverageAnalyzer.DEFAULTS), so
 * three copies of 90/25/70 exist in three languages. Nothing generates them from one source -- that would be
 * heavier than this demo needs. What keeps them from mattering is that the web SENDS these to both sides
 * (SetSensor over the bridge, the POST body to the API), so only one value is ever live at once. These
 * defaults decide what the sliders start at, and what Unity runs on for the moment before the first Load.
 */
```

- [ ] **Step 3: 커밋**

```bash
git add docs/api-contract.md web/src/geo/coverage.ts
git commit -m "docs: say what actually keeps the three sensor defaults equal"
```

---

### Task 7: 빌드하고 브라우저에서 완료 기준 1~4 를 돈다

**Files:**
- 없음 (검증만)

**Interfaces:**
- Consumes: Task 1~6 전부

- [ ] **Step 1: WebGL 을 다시 빌드한다**

```bash
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode -quit \
  -projectPath "$PWD/unity" -executeMethod ShipHdMap.Editor.WebGLBuild.Build -logFile /tmp/m6-build.log
grep "Result:" /tmp/m6-build.log | tail -1
ls -la web/public/unity/Build/unity.wasm
```
Expected: `Build Finished, Result: Success.` `unity.wasm` 의 mtime 이 마지막 커밋보다 나중이어야 한다. **이 mtime 을 적어 둔다 — 아래 모든 관측에 붙인다.**

- [ ] **Step 2: 스택을 띄운다**

`5173` 은 이 머신에서 docker/nginx 가 점유하고 있고 `curl` 에 200 을 돌려주므로 쓰지 않는다. 포트 주인을 먼저 확인한다:

```bash
lsof -nP -iTCP:8081 -sTCP:LISTEN; lsof -nP -iTCP:5399 -sTCP:LISTEN
docker compose -f db/compose.yaml up -d --wait
(cd api && JAVA_HOME=/opt/homebrew/opt/openjdk ./gradlew bootRun --console=plain > /tmp/m6-api.log 2>&1 &)
(cd web && pnpm dev --port 5399 --strictPort > /tmp/m6-vite.log 2>&1 &)
```
`Started ApiApplication` 이 로그에 뜨고 `curl -s -o /dev/null -w '%{http_code}' localhost:8081/api/datasets/roro-demo-01` 가 200 이면 준비된 것이다.

- [ ] **Step 3: 완료 기준 1 — 시야각을 내리면 세 곳이 같이 움직인다**

브라우저에서 `http://localhost:5399/` 를 연다.
1. 커버리지 탭에서 시야각을 90 → 55° 로 내린다
2. 관측 도구로 갑판을 클릭한다
3. HUD 를 읽는다

Expected: HUD 에 `sensor  fov 55  range 25 m  view 70`, 그 아래 `seen` 수가 90° 일 때보다 **작고**, 3D 콘이 눈에 띄게 좁다. 슬라이더를 끄는 동안 콘이 **실시간으로** 좁아진다.

- [ ] **Step 4: 완료 기준 2 — 새로고침 뒤에도 그대로**

새로고침한 뒤 **툴바를 보지 말고** 관측 도구로 갑판을 다시 클릭한다. 툴바가 무엇을 보여 주든 Unity 가 실제로 무슨 값을 쓰는지는 3D 와 HUD 만 말한다 (M5e §3.3).

Expected: `sensor  fov 55` 가 그대로고 콘이 여전히 좁다.

- [ ] **Step 5: 완료 기준 3 — `reloadScene()` 뒤에도 그대로**

적재 탭에서 `재생성` 을 눌러 구획을 다시 만든다(`reloadScene()` 이 돈다). 그 뒤 관측 클릭.

Expected: `sensor  fov 55` 유지.

- [ ] **Step 6: 완료 기준 4 — 커버리지의 σ 가 주행을 바꾼다**

1. 시야각을 90 으로 되돌린다
2. 커버리지 탭에서 σ 거리를 0.20 → 0.80 으로 올린다
3. 주행 탭으로 가서 `▶ 선적` 을 ×1 로 돌린다
4. 차로 구간에서 HUD 의 `RMS` 와 `err` 를 읽는다
5. σ 거리를 0.20 으로 되돌리고 다시 돌려 비교한다

Expected: σ 가 높을 때 `RMS` 와 `err` 가 눈에 띄게 크다. 주행 패널의 읽기 전용 σ 줄도 `거리 0.80 m` 를 말한다.

- [ ] **Step 7: 관측을 기록하고 커밋한다**

각 관측 줄에 Step 1 의 `unity.wasm` mtime 을 적는다. 동시 작업 환경에서 그게 없으면 브라우저 보고서는 증거가 아니라 소문이다 (M5e 교훈 여섯). 코드 변경이 없으면 커밋할 것도 없다 — 기록은 다음 태스크의 문서로 간다.

---

### Task 8: 캡처 — 전용 데이터셋에서 장별 그림과 숫자를 뽑는다

**Files:**
- Create: `scripts/capture.sh`
- Create: `scripts/capture.mjs`
- Create: `docs/img/` (스크립트가 만든다)

**Interfaces:**
- Consumes: Task 7 이 통과한 빌드
- Produces: `docs/img/NN-<name>.png`, `docs/img/NN-<name>-hud.png`
- Produces: stdout 요약표 — Task 9 의 문서가 이 숫자를 쓴다

- [ ] **Step 1: 셸 래퍼를 쓴다**

`scripts/capture.sh`:

```bash
#!/usr/bin/env bash
# Reproduces every figure and every number in docs/architecture-walkthrough.md.
#
# It seeds its OWN dataset. The working dataset (roro-demo-01) has years of hand edits in it -- 99 markers at
# the time of writing against the fixture's 23 -- so a figure shot there shows a ship nobody else can get back.
# docs/qgis-check.md is the evidence: its counts went stale silently because nothing could re-run it.
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="http://localhost:${API_PORT:-8081}/api"; DS="${CAPTURE_DS:-roro-demo-cap}"; PORT="${WEB_PORT:-5399}"
WASM="$ROOT/web/public/unity/Build/unity.wasm"
[ -f "$WASM" ] || { echo "no WebGL build at web/public/unity -- build it first" >&2; exit 1; }
BUILD_MTIME="$(date -r "$WASM" '+%Y-%m-%d %H:%M:%S')"

# The capture dataset is disposable by design: drop and re-seed every run so the numbers cannot drift.
curl -fsS -o /dev/null -X DELETE "$API/datasets/$DS" 2>/dev/null || true
curl -fsS -o /dev/null -X POST "$API/datasets" -H 'content-type: application/json' \
  -d "{\"id\":\"$DS\",\"name\":\"capture\",\"ap_lat\":12.3456,\"ap_lon\":45.6789,\"heading_deg\":87.5}"
curl -fsS -o /dev/null -X POST "$API/datasets/$DS/seed" -H 'content-type: application/json' \
  --data-binary @"$ROOT/docs/fixtures/vehicle-map.sample.json"
curl -fsS -o /dev/null -X POST "$API/datasets/$DS/decks/D3/slots/generate" -H 'content-type: application/json' -d '{}'
echo "seeded $DS"

mkdir -p "$ROOT/docs/img"
BUILD_MTIME="$BUILD_MTIME" CAPTURE_DS="$DS" WEB_PORT="$PORT" node "$ROOT/scripts/capture.mjs"
```

`chmod +x scripts/capture.sh`.

전제: API(8081) 와 vite(`--port 5399 --strictPort`) 와 원격 디버깅 Chrome(9333) 이 이미 떠 있어야 한다. 스크립트가 서버를 띄우지 않는 이유는 Task 7 의 스택을 그대로 쓰기 때문이고, 5173 을 피하는 이유는 이 머신에서 docker/nginx 가 그 포트를 잡고 200 을 돌려주기 때문이다.

- [ ] **Step 2: CDP 드라이버를 쓴다**

`scripts/capture.mjs`:

```js
// Drives one run of the demo in Chrome and writes docs/img/*.png plus a summary table.
//
// Determinism, four rules (M6 spec §6.3):
//  1. Shoot on EVENTS, never on a wall clock. The sim steps by Time.deltaTime, so "12 s after start" is a
//     different place on every machine. The scenario log lines are the real boundaries and `frame_switch`
//     already carries the estimate at that instant.
//  2. x1 only. M5a was bitten by time-scale overshoot; a run captured at x20 is not the run being demoed.
//  3. Reseed. M5b: without it the frame switch happens somewhere else.
//  4. Stamp unity.wasm's mtime on everything. Without it a browser report is rumour, not evidence (M5e).
import fs from "node:fs";
import path from "node:path";

const PORT = process.env.WEB_PORT ?? "5399";
const DS = process.env.CAPTURE_DS ?? "roro-demo-cap";
const BUILD = process.env.BUILD_MTIME ?? "unknown";
const OUT = path.resolve(import.meta.dirname, "..", "docs", "img");
const rows = [];

const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function connect() {
  const list = await (await fetch("http://127.0.0.1:9333/json")).json();
  const t = list.find((x) => x.type === "page" && x.url.includes(`localhost:${PORT}`));
  if (!t) throw new Error(`no page on localhost:${PORT} -- open it in the debugging Chrome first`);
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const pend = new Map();
  await new Promise((r) => ws.addEventListener("open", r));
  ws.addEventListener("message", (m) => { const d = JSON.parse(m.data); if (d.id && pend.has(d.id)) { pend.get(d.id)(d); pend.delete(d.id); } });
  const send = (method, params = {}) => new Promise((res, rej) => {
    const i = ++id; pend.set(i, (d) => (d.error ? rej(new Error(method + ": " + JSON.stringify(d.error))) : res(d.result)));
    ws.send(JSON.stringify({ id: i, method, params }));
  });
  const ev = async (e) => {
    const r = await send("Runtime.evaluate", { expression: e, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails).slice(0, 400));
    return r.result.value;
  };
  await send("Page.enable"); await send("Runtime.enable");
  return { send, ev };
}

const CANVAS = { x: 300, y: 72, w: 640, h: 533 };
const HUD = { x: CANVAS.x, y: 400, width: 560, height: 210 };

export async function main() {
  const d = await connect();
  const btn = (label) => d.ev(`(()=>{const b=[...document.querySelectorAll("button")].find(x=>x.textContent.trim()===${JSON.stringify(label)});if(!b)return null;const r=b.getBoundingClientRect();return{cx:r.x+r.width/2,cy:r.y+r.height/2};})()`);
  const click = async (x, y) => {
    await d.send("Input.dispatchMouseEvent", { type: "mouseMoved", x, y, button: "none", buttons: 0 }); await sleep(60);
    await d.send("Input.dispatchMouseEvent", { type: "mousePressed", x, y, button: "left", buttons: 1, clickCount: 1 }); await sleep(60);
    await d.send("Input.dispatchMouseEvent", { type: "mouseReleased", x, y, button: "left", buttons: 0, clickCount: 1 });
  };
  const press = async (label) => { const p = await btn(label); if (!p) throw new Error(`no button "${label}"`); await click(p.cx, p.cy); await sleep(700); };
  const shot = async (file, clip, scale = 1) => {
    const r = await d.send("Page.captureScreenshot", clip ? { format: "png", clip: { ...clip, scale } } : { format: "png" });
    fs.writeFileSync(path.join(OUT, file), Buffer.from(r.data, "base64"));
  };
  // The HUD is WebGL canvas pixels; the DOM cannot read it. A clipped, scaled capture is the only way in.
  const chapter = async (n, name, note) => {
    await shot(`${n}-${name}.png`);
    await shot(`${n}-${name}-hud.png`, HUD, 3);
    rows.push([`${n}-${name}`, note]);
    console.log(`  ${n}-${name}  ${note}`);
  };
  // Rule 1: wait for a log LINE, not for a clock.
  const waitLog = async (re, timeoutMs = 180000) => {
    const started = Date.now();
    for (;;) {
      const txt = await d.ev(`[...document.querySelectorAll(".panel div, aside div")].map(e=>e.textContent).join("\\n")`);
      const hit = (txt ?? "").split("\n").find((l) => re.test(l));
      if (hit) return hit.trim();
      if (Date.now() - started > timeoutMs) throw new Error(`timed out waiting for ${re}`);
      await sleep(1000);
    }
  };

  await d.send("Page.navigate", { url: `http://localhost:${PORT}/?ds=${DS}` });
  await sleep(15000);

  await press("편집"); await press("궤도"); await press("전체");
  await chapter("01", "frames", "ship and the three decks, orbit camera");

  await press("커버리지");
  await chapter("02", "coverage", await d.ev(`document.querySelector(".panel")?.textContent?.match(/사각지대[^·]*/)?.[0] ?? ""`));

  await press("주행");
  await press("▶ 선적");
  rows.push(["--", await waitLog(/선적 시작/)]);
  await chapter("03", "quay", "on the quay, GPS only");
  rows.push(["--", await waitLog(/프레임 전환/)]);
  await chapter("05", "frame-switch", await waitLog(/프레임 전환/));
  rows.push(["--", await waitLog(/대상 PS-/)]);
  await press("차량 시선");
  await chapter("06", "lane", "driver's eye on the lane");
  rows.push(["--", await waitLog(/종료 \(/)]);
  await press("궤도");
  await chapter("07", "parked", "parked, judged against the map's promise");

  console.log("\n  build: unity.wasm " + BUILD + "   dataset: " + DS);
  console.log("  " + rows.map(([a, b]) => `${a}  ${b}`).join("\n  "));
}

await main();
```

램프(04)와 대조(08)는 Step 3 에서 붙인다 — 먼저 이 골격이 끝까지 도는지 본다.

- [ ] **Step 3: 한 번 돌려서 끝까지 도는지 본다**

```bash
chmod +x scripts/capture.sh && ./scripts/capture.sh
ls -la docs/img/
```
Expected: PNG 가 장마다 두 장씩 생기고, 끝에 빌드 mtime 과 데이터셋 이름이 붙은 요약표가 나온다. **타임아웃으로 죽으면** `waitLog` 의 셀렉터가 시나리오 로그를 못 읽는 것이다 — `document.querySelector(".panel")` 대신 실제 로그 컨테이너의 클래스를 브라우저에서 확인해 고친다.

- [ ] **Step 4: 램프 장과 대조 장을 붙인다**

램프 구간은 **시나리오 로그에 자기 줄이 없다** — `scenarioLine`(`editor.ts:258-267`)이 내보내는 것은 `start`·`target`·`leave_lane`·`frame_switch`·`finished` 뿐이고 `Phase.OnRamp` 는 그 사이를 지난다. 그래서 램프 장은 로그가 아니라 **순서**로 잡는다: `차로 이탈` 줄이 뜬 뒤 `프레임 전환` 줄이 뜨기 **전** 구간이 램프다. Step 3 의 출력에서 두 줄의 실제 순서를 확인하고, 앞 줄을 기다린 직후에 찍는다:

```js
  rows.push(["--", await waitLog(/차로 이탈/)]);
  await chapter("04", "ramp", "climbing the ramp, angle derived from pose");
```

두 줄의 순서가 Step 3 에서 반대로 나오면 그 순서에 맞춰 옮긴다 — 여기서 추측하지 않는다.

8장은 주행이 끝난 뒤 커버리지 탭으로 돌아가 찍는다. 2장과 같은 화면을 같은 파라미터로 다시 찍는 것이므로 그림 자체는 2장과 거의 같고, **문서에서 7장의 실측 오차 옆에 놓이는 것**이 8장의 내용이다:

```js
  await press("커버리지");
  await chapter("08", "promise-vs-measured", await d.ev(`document.querySelector(".panel")?.textContent?.match(/사각지대[^·]*/)?.[0] ?? ""`));
```

- [ ] **Step 5: 두 번 돌려 같은 숫자가 나오는지 본다 (완료 기준 5)**

```bash
./scripts/capture.sh > /tmp/cap1.txt
./scripts/capture.sh > /tmp/cap2.txt
diff /tmp/cap1.txt /tmp/cap2.txt && echo "DETERMINISTIC"
```
Expected: `DETERMINISTIC`. 어긋나면 결정론 네 규칙 중 무엇이 깨졌는지 찾는다 — 대개 ×1 이 아니거나 재시드가 안 된 것이다.

- [ ] **Step 6: 커밋**

```bash
git add scripts/capture.sh scripts/capture.mjs docs/img
git commit -m "feat(docs): a capture script that seeds its own dataset, so the figures can be got back"
```

---

### Task 9: 워크스루 문서를 쓴다

**Files:**
- Create: `docs/architecture-walkthrough.md`

**Interfaces:**
- Consumes: Task 8 의 `docs/img/*` 와 요약표 숫자

- [ ] **Step 1: 장 뼈대를 만든다**

장 경계는 `MapRuntime.Phase` 와 `ScenarioEvt` 가 이미 갖고 있다. 새로 발명하지 않는다 — phase 가 늘면 문서가 빠진 장을 스스로 드러낸다.

```markdown
# 주행 한 번을 끝까지 따라가기

> 이 문서의 그림과 숫자는 전부 `./scripts/capture.sh` 가 만든다. 숫자가 의심스러우면 다시 돌려서 요약표와 대조한다.
> 기준 빌드: `unity.wasm` <Task 8 요약표의 mtime> · 데이터셋: `roro-demo-cap` (픽스처 시드 + Deck 3 구획 자동생성)

| 장 | 코드의 경계 |
|---|---|
| 1 배와 좌표계 | (정지) |
| 2 지도가 약속하는 것 | (편집 모드) |
| 3 부두 | `Phase.OnQuay` |
| 4 램프 | `Phase.OnRamp` |
| 5 프레임 전환 | `frame_switch` |
| 6 차로 | `Phase.OnLane` |
| 7 주차 | `Phase.Parking` |
| 8 약속과 실측의 대조 | (정지) |
```

- [ ] **Step 2: 1~7장을 쓴다**

각 장은 같은 모양으로 쓴다: **그림 → 무엇이 보이는가 → 어떤 좌표계에서 어떤 계산이 도는가 → 어느 파일** 순서. 숫자는 Task 8 요약표에서 그대로 옮긴다. 각 장은 스펙 §5.1 표의 "답하는 질문" 하나에 답하면 끝이다.

5장이 이 데모의 심장이다 — 선내 좌표가 부두 좌표와 무관하다는 주장이 실제로 값을 내는 유일한 순간이고, `frame_switch` 의 `detail` 이 그 순간의 추정값을 싣고 있다.

- [ ] **Step 3: 8장을 쓴다**

2장의 히트맵이 약속한 σ 와 6~7장이 실제로 낸 σ 를 나란히 놓는다. **이 비교는 두 장이 같은 센서 파라미터를 썼기 때문에만 성립한다** — M6 이전에는 이 장을 쓸 수 없었다. 3장(센서 경로)이 존재하는 이유가 이 장이다. 스펙 §5.2 를 인용하지 말고 실제 두 숫자를 적는다.

- [ ] **Step 4: 완료 기준 6~7 을 확인한다**

```bash
./scripts/capture.sh > /tmp/cap3.txt
grep -o 'docs/img/[0-9a-z-]*\.png' docs/architecture-walkthrough.md | sort -u | while read -r f; do [ -f "$f" ] || echo "MISSING $f"; done
```
Expected: `MISSING` 없음. 그리고 문서의 숫자를 `/tmp/cap3.txt` 요약표와 사람이 눈으로 대조한다.

- [ ] **Step 5: 공개 저장소 점검**

```bash
grep -rniE "회사|기관|과제|주식회사|연구원|사업|대학|병원" docs/architecture-walkthrough.md scripts/capture.sh scripts/capture.mjs
```
Expected: 출력 없음

- [ ] **Step 6: 커밋**

```bash
git add docs/architecture-walkthrough.md
git commit -m "docs: follow one run from the quay to a parked slot"
```

---

### Task 10: 마무리 — 전체 검사와 병합

**Files:**
- Modify: `README.md` (상태 절에 한 줄)

- [ ] **Step 1: 세 검사를 전부 돌린다**

```bash
cd web && pnpm vitest run 2>&1 | tail -4 && pnpm build 2>&1 | tail -3 && cd ..
(cd api && JAVA_HOME=/opt/homebrew/opt/openjdk ./gradlew test --console=plain 2>&1 | tail -4)
/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity -batchmode \
  -projectPath "$PWD/unity" -runTests -testPlatform EditMode -testResults /tmp/m6-final.xml -logFile /tmp/m6-final.log
python3 -c "import xml.etree.ElementTree as ET;r=ET.parse('/tmp/m6-final.xml').getroot();print(r.get('total'),r.get('passed'),r.get('failed'))"
```
Expected: Vitest `92 passed`, `pnpm build` 성공, api `79` 그대로, Unity `163 163 0`

- [ ] **Step 2: README 의 상태 절에 한 줄 더한다**

`- 다음: M6 문서·시연` 줄을 지우고 그 자리에 넣는다:

```
- M6 센서 경로·문서 완료 (웹이 센서 기하의 단일 발신자 — 히트맵과 관측점이 같은 세 값을 본다, HUD 가 그 값을 말한다, σ 는 커버리지 패널이 단일 출처, 주행 워크스루 문서와 재생산 캡처 스크립트; EditMode 163, Vitest 92)
- 다음: 카메라 리그(눈 하나 → 여러 대), 가변 내부 램프, 항해 시뮬레이션
```

- [ ] **Step 3: 커밋하고 병합한다**

```bash
git add README.md && git commit -m "docs: M6 done -- one sensor for both screens, and a run you can reproduce"
git checkout main && git merge --no-ff <branch> -F <메시지 파일>
```
병합 메시지는 이 저장소 관례대로 `Merge M6: …` 로 시작하고, 무엇을 닫았는지와 남은 것(카메라 리그)을 본문에 적는다. `git merge -F -` 는 stdin 을 읽지 않으므로 메시지는 파일로 넘긴다.

- [ ] **Step 4: 푸시 전 점검**

```bash
git diff origin/main..main --stat
grep -rniE "회사|기관|과제|주식회사|연구원|사업|대학|병원" $(git diff --name-only origin/main..main)
```
Expected: 두 번째 명령 출력 없음. 푸시는 사용자 요청이 있을 때만 한다.
