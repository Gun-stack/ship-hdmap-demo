# M6 설계 스펙 — 두 화면이 같은 센서를 보게 하고, 주행 한 번을 문서로 남긴다 (2026-09-18)

기본 스펙 `2026-09-15-ship-hdmap-demo-design.md` 의 M5e 다음에 넣는 단계이고, 원래 계획의 마지막 마일스톤이다. 브레인스토밍에서 확정한 결정만 담는다. 용어는 [용어집](../../glossary.md), 선택 근거는 [설계 근거](../../design-rationale.md) 참조.

- **M5e (앞)**: 쌓인 기능을 사람이 조작할 수 있는 편집기로 만들었다
- **M5f (앞)**: HUD 가 어느 눈이 살아 있고 그 눈이 몇 개를 보는지 말하게 했다
- **M6 (이 문서)**: 그 눈이 **무엇으로 보는지**를 웹이 정하게 하고, 주행 한 번을 문서와 그림으로 남긴다

---

## 1. 범위

### 1.1 목적

M5c 가 커버리지 히트맵을 만들고 M5e 가 가상 관측점을 만들었다. 둘은 같은 질문에 답한다 — "여기서 마커가 몇 개 보이는가". 그런데 **서로 다른 센서에게 묻는다.**

히트맵은 커버리지 패널의 슬라이더 값을 API 로 보낸다. 관측점은 Unity `LandmarkSensor` 의 하드코딩 상수(`fovDeg 90 / maxDist 25 / maxViewAngleDeg 70`)를 쓴다. 웹에서 Unity 의 센서 값을 바꾸는 경로가 **코드에 없다** — `Sensor.fovDeg` 에 대입하는 줄이 저장소 전체에 하나도 없다.

`docs/api-contract.md:20` 은 커버리지 API 의 기본값을 "기본은 Unity `LandmarkSensor` 와 동일" 이라고 적어 놓았다. **문서가 약속한 등식을 지키는 코드가 없다.**

M5f 가 이걸 더 잘 보이게 만들었다. HUD 가 이제 `seen 3 / 23` 이라고 숫자로 말하므로, 운영자가 시야각을 90 → 55° 로 내리면 히트맵이 약속한 수와 관측점이 센 수가 **한 화면에서 어긋난다.** 시연 중에 누가 알아채면 설명할 말이 없다.

M6 은 그 경로를 놓고, 그 위에서 이 데모가 무엇을 증명했는지를 문서와 그림으로 남긴다.

### 1.2 들어가는 것

1. **`SetSensor` 브리지 메시지** — 웹이 기하 파라미터 셋의 단일 발신자가 된다
2. **HUD 의 `sensor` 줄** — Unity 가 지금 쓰는 값을 화면이 말한다
3. **σ 통합** — 웹 안에서 두 벌로 갈라져 있는 노이즈 삼총사를 한 벌로
4. **워크스루 문서** — 주행 한 번을 부두에서 주차까지 따라가는 `docs/architecture-walkthrough.md`
5. **캡처 스크립트** — 문서의 그림과 숫자를 재생산하는 `scripts/capture.sh`

### 1.3 제외

- **세 언어에 걸친 상수 공유.** `SENSOR_DEFAULTS`(TS) · `CoverageAnalyzer.DEFAULTS`(Java) · `LandmarkSensor`(C#) 를 한 곳에서 생성하는 것은 이 데모에 과하다. 세 벌을 남기되 **웹이 유일한 발신자**가 되게 해서 실행 중에는 한 값만 살아 있게 한다
- **카메라 리그.** 카메라 여러 개(테슬라 수준 7~8개)로 가는 것은 M6 이후다. 이 마일스톤은 **눈 하나**의 파라미터만 옮긴다. 다만 와이어 형식을 정할 때 그 방향을 고려한다(8장)
- **영상.** 스틸 PNG 와 재생산 스크립트만. 움직임이 있어야 전달되는 것(프레임 전환의 순간)은 문서의 서술과 전후 두 장으로 대신한다
- **README 재정리, 시연 대본.** README 는 마일스톤 진행 로그로 그대로 둔다
- **문서 숫자의 자동 대조.** 문서를 파싱해 스크립트 출력과 비교하는 것은 과하다. 스크립트가 요약표를 뱉고 사람이 대조한다
- **`sigma_gps` 의 커버리지 반영.** 커버리지 모델에 GPS 개념이 없다. 주행 전용으로 남는다
- **가변 내부 램프, 항해 시뮬레이션.** 둘 다 M6 이후 고도화 후보

### 1.4 완료 기준

1. 커버리지 패널에서 시야각을 90 → 55° 로 내리면, 같은 자리 관측점의 **콘이 좁아지고** `seen` 수가 줄고 HUD 의 `sensor` 줄이 `fov 55` 를 말한다
2. 새로고침 뒤에도 1 이 성립한다. 확인은 툴바가 아니라 **3D 클릭**으로 한다 (M5e §3.3 의 교훈)
3. 구획 재생성 등으로 `reloadScene()` 이 돈 뒤에도 1 이 성립한다
4. 커버리지 탭에서 σr 을 올리면 **주행의 측위가 실제로 나빠진다** — HUD 의 `RMS` 와 `err` 가 움직인다
5. `scripts/capture.sh` 를 두 번 돌리면 **같은 숫자**가 나온다
6. 워크스루 문서의 숫자가 캡처 스크립트의 요약표와 일치한다
7. 문서가 참조하는 이미지가 전부 존재하고, 요약표에 **어느 빌드인지**(`unity.wasm` mtime)가 찍혀 있다

---

## 2. 구성

| 파일 | 책임 | 트랙 |
| --- | --- | --- |
| `unity/…/Bridge/BridgeMessages.cs` | `SetSensor` + `SetSensorMsg` | Unity |
| `unity/…/Bridge/MapRuntime.cs` | `SetSensor` 배선, 관측점 갱신 | Unity |
| `unity/…/Vehicle/HudView.cs` | `SensorConfigLine` (순수) + `SetSensorConfig` | Unity |
| `web/src/bridge/useShipUnity.ts` | `editorStateMessages` 에 `SetSensor` 추가, 슬라이더 변화 전송 | 웹 |
| `web/src/store/editor.ts` | `noise` → `sigmaGps`, `SetNoise` 조립, 스키마 2 | 웹 |
| `web/src/components/DrivePanel.tsx` | σ 슬라이더 3개 제거, 읽기 전용 한 줄 추가 | 웹 |
| `web/src/geo/coverage.ts` | `SENSOR_DEFAULTS` 주석을 3자 등식으로 | 웹 |
| `scripts/capture.sh` | 캡처 전용 데이터셋 시드 + 장별 스크린샷 + 요약표 | 문서 |
| `docs/architecture-walkthrough.md` | **신규.** 주행 한 번을 끝까지 | 문서 |
| `docs/img/` | **신규.** 캡처 산출물 | 문서 |
| `docs/api-contract.md` | "Unity 와 동일" 문장을 사실로 고친다 | 문서 |

---

## 3. 센서 경로

### 3.1 와이어는 스칼라 셋이다

```csharp
public class SetSensorMsg { public double fov_deg = 90, max_dist_m = 25, max_view_angle_deg = 70; }
```

필드 이름이 커버리지 API 의 것과 **글자 그대로 같다**. 이게 이 결정의 전부다 — 한 화면에서 `POST /coverage` 의 본문과 `SetSensor` 의 본문을 나란히 놓으면 세 이름이 똑같이 나타나고, "두 화면이 같은 센서를 보는가"를 눈으로 대조할 수 있다.

카메라 리스트(`{cameras:[{…}]}`)로 잡는 안을 검토했고 기각했다. API 는 평평한 스칼라 셋을 받는다. 브리지만 리스트로 만들면 Unity 가 API 와 모양이 달라져서, **낡은 이음매를 닫으려고 새 이음매를 하나 만드는 셈**이 된다. 리그로 갈 때는 API 와 브리지를 같이 바꾼다 — 어차피 같이 바꿔야 한다(8장).

`SetSensor` 는 σ 를 싣지 않는다. 노이즈는 `SetNoise` 가 이미 갖고 있고, 하나가 둘을 쏘면 어느 쪽이 이겼는지 추적할 곳이 둘이 된다.

### 3.2 어디서 쏘나 — 두 곳

**① `editorStateMessages` 에 한 줄.** 이 순수 함수의 주석이 이미 이렇게 적혀 있다:

> *Everything Unity forgets on a fresh Load or a page reload: the tool, the camera, **the sensor** and the occlusion set.*

그런데 목록에 있는 건 `SetNoise` 뿐이다. **주석이 약속한 것의 절반만 지켜져 있다.** 여기 넣으면 재로드 후 재전송이 공짜로 따라온다 — 첫 `Load` 와 `reloadScene()` 둘 다 이미 `sendEditorState()` 를 부른다. M4 의 "재로드 후 `Select` 재전송을 빠뜨린" 실수가 구조적으로 불가능해진다.

함수 서명이 `coverageParams` 를 받도록 는다. 순수 함수이므로 `bridge.test.ts` 가 케이스 하나로 고정한다.

**② 슬라이더가 움직일 때 — `useEffect`, 디바운스 없이.**

커버리지 패널은 `onChange` 로 상태만 바꾸고 API 는 `onMouseUp`/`onTouchEnd` 에서 부른다(`CoveragePanel.tsx:19-20`). `SetSensor` 는 **release 가 아니라 change 에 붙인다.** 이유 둘:

- 비싼 것(커버리지 POST)은 release 트리거를 유지하고, **싼 것**(부동소수 셋을 브리지로)은 매 변화마다 쏴서 놓칠 수가 없게 한다. `onMouseUp` 에 걸면 "키보드로 슬라이더를 움직이면?" 같은 구멍이 생긴다
- 부수효과가 좋다 — 슬라이더를 끄는 동안 **3D 콘이 실시간으로 좁아지고** 히트맵은 손을 뗄 때 따라온다. 둘이 연결돼 있다는 것을 화면이 보여 준다

같은 `useEffect` 가 **`SetNoise` 도 같이 쏜다.** σ 삼총사가 `coverageParams` 로 옮겨 오므로(4장), 커버리지 슬라이더 하나가 두 메시지의 출처가 된다. 완료 기준 4("커버리지 탭에서 σr 을 올리면 주행의 측위가 실제로 나빠진다")는 이 전송이 있어야만 성립한다 — `editorStateMessages` 는 재로드 때만 돌기 때문이다.

### 3.3 Unity 쪽

`MapRuntime.SetSensor` 가 `Sensor.fovDeg / maxDist / maxViewAngleDeg` 에 대입한다. 세 필드는 이미 public 이고 `VisibleFrom` 이 매 프레임 읽으므로 주행은 자동으로 따라온다. **관측점은 안 따라온다** — `ProbeView.Aim()` 은 클릭과 화살표 키에서만 돌기 때문이다. `SetSensor` 는 `Probe.Active` 일 때 `Aim()` 을 다시 부른다.

### 3.4 HUD 의 `sensor` 줄

`SensorLine` **바로 위**에 놓는다. 세는 것 바로 위에 무엇으로 셌는지가 오게:

```
tool probe   cam probe-eye   [left/right] turn the eye
sensor  fov 55  range 25 m  view 70
seen 1 / 23   fov 14  range 6  facing 2  hidden 0  blocked 0
```

`HudView.SensorConfigLine(fovDeg, maxDist, maxViewAngleDeg)` 는 순수 정적 함수라 EditMode 테스트로 고정한다. ASCII 만 쓴다 — 런타임 UI Toolkit 기본 테마 폰트에 한글 글리프가 없다(M5f).

`StatusLine`·`SensorLine` 과 같은 규칙으로 `MapRuntime.Update()` 가 매 프레임 다시 계산한다. 네 곳에서 밀어넣지 않는다.

### 3.5 기본값 세 벌 — 없애지 않고 격하시킨다

`90 / 25 / 70` 을 세 곳이 각자 들고 있다: `SENSOR_DEFAULTS`(웹), `CoverageAnalyzer.DEFAULTS`(Java), `LandmarkSensor`(C#). 등식을 지키는 건 주석 한 줄뿐이다 — `coverage.ts:4` 의 *"Must match CoverageAnalyzer.DEFAULTS"*.

M6 은 합치지 않는다. 대신 **웹이 유일한 발신자**가 되고 나머지 둘은 "웹이 말하기 전까지의 값"으로 격하된다. 웹은 부팅 직후 `SetSensor` 를 쏘므로 Unity 의 하드코딩 값이 화면에 사는 시간은 첫 `Load` 전까지다. `coverage.ts:4` 의 주석을 3자 등식으로 고치고, 세 값이 다르면 무슨 일이 생기는지를 적는다.

---

## 4. σ 통합

### 4.1 같은 병이 σ 에도 있다

`coverageParams` 에 `sigma_r`·`sigma_theta`·`sigma_alpha` 가 있고(커버리지 패널 → API), `ed.noise` 에도 같은 셋이 있다(주행 패널 → `SetNoise` → Unity). **σ 가 웹 안에서 이미 두 벌로 갈라져 있다.** 커버리지 패널에서 σr 을 0.5 로 올려도 주행은 0.2 로 달린다 — 예측이 실측을 예측하지 않게 된다.

기하 삼총사만 고치고 이걸 두면, M6 이 닫은 이음매 바로 옆에 같은 이음매가 남는다.

### 4.2 커버리지 쪽을 단일 출처로 삼고 주행 쪽을 지운다

- 스토어의 `noise: {sigma_r, sigma_theta, sigma_alpha, sigma_gps}` → **`sigmaGps: number`** 하나만 남는다
- `SetNoise` 페이로드는 **조립**된다 — 셋은 `coverageParams` 에서, `sigma_gps` 는 스토어에서. 메시지 자체는 안 바뀐다(Unity 는 여전히 넷을 받는다)
- 따라서 `SetNoise` 의 발신 시점이 는다: 지금은 재로드 때뿐인데, 커버리지 슬라이더가 움직일 때도 쏴야 한다(§3.2)
- 주행 패널에서 σ 슬라이더 **3개가 사라진다**. σ GPS 는 남는다

합치는 쪽이 아니라 **지우는 쪽**을 고른 이유: 결과는 같은데 UI 가 늘지 않고 줄어든다. 그리고 "예측과 실측이 같은 노이즈를 쓴다"는 주장은 M5d 가 이미 한 이야기다 — 판정 기준을 고정 허용오차가 아니라 지도가 약속한 값으로 삼은 것과 같은 논리다.

### 4.3 스키마 2 와 반드시 깨지는 테스트

**`EDITOR_SCHEMA` 를 1 → 2 로 올린다.** 옛 `localStorage` 블롭에는 `noise` 가 있고 `sigmaGps` 가 없다. zustand 는 버전 불일치를 통째로 버리고 기본값으로 간다 — 이 저장소가 이미 고른 동작이다(`editor.ts` 의 `// No migrate:` 주석). **안 올리면 옛 블롭이 `sigmaGps` 없이 되살아난다.**

**`editor.test.ts` 의 "저장하는 키가 정확히 이것뿐이다" 가 깨진다.** 그게 `partialize` 목록의 표류를 막는 **유일한** 장치라고 주석이 적어 뒀다(`editor.ts:239`). 깨지는 것이 정상이며, 구현 순서는 **그 테스트를 먼저 새 키 집합으로 고치고** 나서 스토어를 바꾼다.

### 4.4 주행 패널이 잃는 것과 메우는 법

주행을 보면서 σ 를 만지려면 커버리지 탭으로 건너가야 한다. 시연 중에 "노이즈를 올리면 어떻게 되나"를 보여 줄 때 탭 전환이 한 번 낀다. 메우는 것은 읽기 전용 한 줄이다:

```
σ  거리 0.20 m · 방위 1.0° · 방향각 2.0°      (커버리지 탭에서 조정)
```

슬라이더는 늘지 않고, 주행 화면은 자기가 무슨 노이즈로 달리는지 계속 말한다 — HUD 의 `sensor` 줄과 같은 발상이다.

---

## 5. 워크스루 문서

### 5.1 장 구성 — 코드가 이미 갖고 있는 경계를 쓴다

장 경계를 새로 발명하지 않는다. `MapRuntime.Phase` 와 `ScenarioEvt` 가 이미 갖고 있다. phase 가 늘면 문서가 빠진 장을 스스로 드러낸다.

| 장 | 코드의 경계 | 답하는 질문 |
|---|---|---|
| 1 배와 좌표계 | (정지) | AP×기선×중심선 원점, Y 좌현+, Ship→Unity 매핑. 왜 WGS84 를 저장하지 않는가 |
| 2 지도가 약속하는 것 | (편집 모드) | 커버리지 히트맵, 관측점, 마커 법선. σ 가 어디서 낮고 왜 |
| 3 부두 | `Phase.OnQuay` | Quay Frame, GPS 만으로 달린다. `N 0`, `seen 0 / 23  range 23` |
| 4 램프 | `Phase.OnRamp` | 램프 각도는 저장이 아니라 pose 파생. 흘수·조위·부두 높이 |
| 5 프레임 전환 | `frame_switch` 이벤트 | 입구 마커 쌍 동시 인식 → Quay Frame 을 버리고 Ship Frame 으로 |
| 6 차로 | `Phase.OnLane` | 측위, 믿음 상태, 예측 대비 저하 판정 |
| 7 주차 | `Phase.Parking` | 추정 기반 판정 → DB status. 허용오차가 아니라 지도가 약속한 값 |
| 8 약속과 실측의 대조 | (정지) | 2장이 약속한 σ 와 6~7장이 낸 σ |

### 5.2 8장이 결론이다

8장은 2장의 예측과 6~7장의 실측을 나란히 놓는다. **이 비교는 두 장이 같은 센서 파라미터를 썼기 때문에만 성립한다.** M6 이전에는 이 장을 쓸 수 없었다 — 두 화면이 다른 센서를 봤으니까. 3장이 존재하는 이유가 8장이다.

5장이 이 데모의 심장이다. 선내 좌표가 부두 좌표와 무관하다는 주장이 실제로 값을 내는 유일한 순간이고, `frame_switch` 이벤트의 `detail` 이 그 순간의 추정값을 이미 싣고 있다.

---

## 6. 캡처

### 6.1 `scripts/capture.sh`

```
1. roro-demo-cap 을 픽스처로 시드           ← 작업용 roro-demo-01 은 건드리지 않는다
2. API + vite + Chrome(원격 디버깅) 기동
3. unity.wasm 의 mtime 을 읽어 둔다          ← 모든 출력에 도장
4. 장마다:
     - 그 장의 상태를 만든다 (탭·도구·카메라, 또는 시나리오 이벤트를 기다린다)
     - docs/img/NN-<name>.png          전체 화면
     - docs/img/NN-<name>-hud.png      HUD clip 확대
     - 관측한 숫자를 stdout 으로
5. 요약표 출력 — 문서의 숫자와 사람이 대조
```

HUD 는 WebGL 캔버스 픽셀이라 DOM 으로 읽을 수 없다. CDP `Page.captureScreenshot` 의 `clip` + `scale` 로 잘라 확대 캡처하는 것이 유일한 방법이다.

### 6.2 캡처 전용 데이터셋을 쓰는 이유

작업용 `roro-demo-01` 은 여러 세션의 편집이 쌓여 마커가 99개로 불어나 있다. 거기서 찍으면 오늘 찍은 그림과 내일 찍은 그림이 다른 배를 보여 주고, 문서는 **재현할 수 없는 한 순간**을 서술하게 된다.

이건 가설이 아니다. `docs/qgis-check.md` 가 정확히 그렇게 썩었다 — 마커 19→23, 구획 2→76 으로 숫자가 낡아 고쳐야 했다. 워크스루 문서는 그보다 훨씬 크다. **`qgis-check.md` 가 조용히 썩은 이유는 돌려볼 것이 없었기 때문이다.**

전용 데이터셋을 쓰면 "데모 DB 를 리셋할까" 라는 질문도 같이 사라진다. 작업용 DB 는 그대로 둔다.

### 6.3 결정론 네 가지

**① 벽시계가 아니라 이벤트에서 찍는다.** 시뮬레이션이 `Time.deltaTime` 으로 도니 "선적 시작 12초 뒤"는 머신마다 다른 위치다. 대신 시나리오 로그 줄이 나타나기를 기다린다 — `프레임 전환 · …`, `대상 PS-D3-…`, `차로 이탈 · …`. DOM 에 그대로 렌더되고(`DrivePanel.tsx:104`), `frame_switch` 의 `detail` 은 그 순간의 추정값을 싣고 있다. **문서의 숫자가 스톱워치가 아니라 이벤트 페이로드에서 나온다.**

**② ×1 로만 돌린다.** M5a 가 시간 배율 오버슛에 물렸다. 캡처가 ×20 으로 달리면 문서가 서술하는 주행과 데모에서 보는 주행이 달라진다.

**③ 재시드한다.** M5b 의 교훈 — 재시드하지 않으면 프레임 전환이 엉뚱한 데서 일어난다. `LandmarkSensor.Reseed` 가 이미 있고 `noise.seed = 1` 이 기본이다.

**④ 모든 출력에 `unity.wasm` mtime 을 찍는다.** M5e 교훈 여섯을 스크립트로 굳히는 것이다 — *"브라우저 패스의 모든 관측에는 어느 빌드였나가 붙어야 한다. 없으면 증거가 아니라 소문이다."*

---

## 7. 테스트

### 7.1 단위 (Vitest)

- `editorStateMessages` 가 `SetSensor` 를 세 이름 그대로 싣는다 — `bridge.test.ts`
- `SetNoise` 페이로드가 `coverageParams` 의 σ 셋 + 스토어의 `sigma_gps` 로 조립된다
- `partialize` 가 저장하는 키 집합이 정확히 새 목록이다 (`noise` 없음, `sigmaGps` 있음) — **이 테스트를 먼저 고친다**
- 스키마 2 의 블롭만 복원되고 1 은 버려진다

### 7.2 단위 (Unity EditMode)

- `HudView.SensorConfigLine` 의 서식 — 정수·소수, 경계값
- `MapRuntime.SetSensor` 가 세 필드에 대입한다
- `SetSensor` 가 `Probe.Active` 일 때 관측점을 다시 겨눈다 (`SensorText` 가 갱신된다)
- 기존 160 개가 그대로 통과한다

### 7.3 브라우저 검증

완료 기준 1~4 를 손으로 돈다. 각 관측에 `unity.wasm` mtime 을 적는다. 도구와 절차는 M5f 패스와 같다.

### 7.4 캡처

완료 기준 5~7. `scripts/capture.sh` 를 두 번 돌려 요약표를 `diff` 한다.

---

## 8. 결정과 이유

| 결정 | 이유 | 대가 |
|---|---|---|
| 와이어를 스칼라 셋으로 | 커버리지 API 의 세 이름과 1:1. 두 본문을 나란히 놓고 대조할 수 있다 | 리그로 갈 때 API 와 브리지를 같이 바꾼다 |
| `editorStateMessages` 에 넣기 | 재로드·새로고침 재전송이 공짜. 순수 함수라 테스트가 이미 있다 | 함수 서명이 는다 |
| 슬라이더 change 에 전송(디바운스 없이) | 싼 것은 놓치지 않는 쪽이, 비싼 것은 미루는 쪽이 맞다 | 드래그 한 번에 브리지 호출 수십 번 — 부동소수 셋이라 무시할 만하다 |
| 기본값 세 벌을 남기되 격하 | 세 언어 상수 공유는 이 데모에 과하다 | 부팅 직후 짧은 순간 Unity 값이 산다 |
| σ 를 커버리지 쪽으로 합치고 주행 쪽을 지움 | 예측이 실측을 예측해야 한다. 지우는 쪽이라 코드가 준다 | 주행 중 σ 조정에 탭 전환 한 번 |
| 장 경계를 `Phase` 에 맞춤 | 새 개념을 만들지 않는다. phase 가 늘면 문서가 빠진 장을 드러낸다 | 편집 모드 이야기(2·8장)는 phase 가 없어 따로 붙는다 |
| 캡처 전용 데이터셋 | 문서·그림·숫자가 한 상태에서 나온다. `qgis-check.md` 의 재발을 막는다 | 데이터셋이 하나 는다 |
| 이벤트에서 찍기 | 벽시계는 머신마다 다르다. 이벤트 페이로드는 그 순간의 값을 이미 싣고 있다 | 이벤트가 안 오면 스크립트가 멈춘다 — 타임아웃이 필요하다 |
| 문서 숫자 대조는 사람이 | 자동 대조는 과하다 | 돌려보지 않으면 여전히 썩는다. 요약표가 그 비용을 낮춘다 |
| 영상 제외 | 스틸 + 스크립트로 재생산 가능성이 확보된다 | 프레임 전환의 "순간"은 전후 두 장과 서술로 대신한다 |
