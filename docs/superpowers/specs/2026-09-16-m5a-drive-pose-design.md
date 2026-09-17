# M5a 설계 스펙 — 갑판 위 연속 적재·하역과 pose 기울임 (2026-09-16)

기본 스펙 `2026-09-15-ship-hdmap-demo-design.md` 의 M5 행을 둘로 나눈 앞부분이다. 브레인스토밍에서 확정한 결정만 담는다.

- **M5a (이 문서)**: 픽스처 래싱 격자 확장, pose → 선체 기울임·램프 각도, 갑판 위 연속 적재·하역 시나리오, 주차 판정, 시나리오 로그, WebGL 재빌드
- **M5b (다음)**: 부두 지오메트리, GPS, Quay Frame ↔ Ship Frame 전환, 램프 위 차로 실시간 생성, 입구 랜드마크 쌍(`transition_landmarks`) 시드 채우기

---

## 1. 범위

### 1.1 들어가는 것

1. 이월: `FixtureExporter` 의 래싱 창 필터 제거(D3 전체 격자 내보내기), API 테스트 3곳의 상수 49 를 픽스처에서 센 값으로 교체
2. Unity: `SetPose` 본체(선체 기울임·램프 각도), 연속 적재/하역 상태기, 주차 판정, `onSlotFilled`·`onScenario` 송신, 주차 차량 박스, `SetTimeScale`
3. 웹: pose 를 Unity 로 전달, `onSlotFilled` 수신 후 `PUT /slots/{sid}/status`, 시나리오 로그, 선적·하역 버튼, 시간 배율
4. 기본 스펙 정정: `transition_lm_ids` → `transition_landmarks`, §9.2 램프 장애물 문구, §10 페이로드, §3.3 루트 좌표계 한 줄, §4.1 heel 부호
5. WebGL 재빌드, 브라우저 검증, README

### 1.2 제외

- API 변경 없음. `PUT /datasets/{ds}/slots/{sid}/status`, pose GET/PUT, `GET /ramps/{id}` 가 이미 있다
- 충돌 물리, 폐루프 조향, 다중 차량 동시 주행, 다갑판 주행(차로 `next`), 구획 재생성 시 status 보존(재생성 = 새 계획 = 전부 `empty`. 되돌리기는 하역 시나리오가 담당)
- 이월 유지: 3D 선택 강조 강화, 적재 패널 갑판이 탭을 따르게, `SeedImportTests` 헬퍼
- M5b 로: `Load` 가 vehicle-map 의 갑판·기둥·램프·래싱으로 선체 메시를 다시 짓기(지금은 Unity 자체 `ShipParams` 로 지어 DB 와 어긋날 수 있다), 재시드 시 시드 계층 피처 선삭제 옵션

### 1.3 완료 기준

선적 시나리오가 빈 구획을 순서대로 채우며 `onSlotFilled` 마다 DB status 가 바뀌고 3D 색이 따라온다. 노이즈 0 이면 전부 `filled`, 노이즈를 키우면 `needs_adjust` 가 나온다. 하역이 역순으로 비운다. pose 슬라이더로 선체가 기울어도 마커·차량·구획의 Ship Frame 좌표는 변하지 않는다.

---

## 2. 구성

```
unity/…/Runtime/Vehicle/ScenarioPlanner.cs   신규, 순수 정적. NextSlot / ApproachPath / DeparturePath / Judge
unity/…/Runtime/Bridge/MapRuntime.cs         SetPose 본체, 시나리오 상태기 Step(dt), onSlotFilled·onScenario 송신, SetTimeScale
unity/…/Runtime/Bridge/BridgeMessages.cs     SetTimeScale, onScenario 상수와 DTO(SetPoseMsg, SlotFilledEvt, ScenarioEvt)
unity/…/Runtime/Vehicle/VehicleController.cs 폴리라인 주행을 경로·속도 인자로 일반화(StartPath), localPosition/localRotation
unity/…/Runtime/Ship/ShipMeshBuilder.cs      Ship 을 Map 루트 아래에 생성, Prim·램프를 localPosition 으로, SetRampAngle 을 localRotation 으로
unity/…/Runtime/Ship/MapOverlay.cs           LineRenderer 로컬 공간, SetStatus(overlay, id, status), 주차 차량 박스 Spawn/Remove
unity/…/Runtime/Landmarks/LandmarkMarker.cs  MoveTo 는 월드 입력 유지, ToModel 은 부모 기준 로컬로
unity/…/Runtime/Vehicle/HudView.cs           커서·갑판 라벨을 루트 기준으로, 램프 상태 한 줄
unity/…/Editor/FixtureExporter.cs            래싱 창 필터 제거
web/src/bridge/useShipUnity.ts               pose/ramp → SetPose 효과, onSlotFilled·onScenario 리스너
web/src/store/editor.ts                      scenarioLog, onSlotFilled 액션(putSlotStatus), appendLog
web/src/api/client.ts, types.ts              putSlotStatus, SlotStatus, SlotFilledEvt, ScenarioEvt, ScenarioLine
web/src/components/DrivePanel.tsx            선적·하역·정지, 시간 배율, 로그 목록
docs/api-contract.md                         브리지 표 갱신
```

---

## 3. Unity 시나리오와 주차 판정

### 3.1 상태기

`MapRuntime` 안의 enum `Phase { Idle, OnLane, Parking, Departing }`. `Update` 는 `Step(Time.deltaTime)` 을 부르고, EditMode 테스트는 `Step` 을 직접 부른다. `SetMode("edit")` 은 `Idle` 로 되돌리고 차량을 숨긴다. 이미 주차된 박스는 남는다.

### 3.2 선적(load)

1. `StartScenario{mode:"load"}` → `onScenario{event:"start", mode}`. 대상 = `CurrentMap.parking_slots` 중 `status == "empty"` 이고 `access_lane_id` 가 해석되는 구획 가운데 `sequence_no` 최소. 없으면 `onScenario{event:"finished", reason:"no_empty_slot"}` 후 `Idle`
2. `onScenario{event:"target", slot_id}`. 차량을 그 차로의 시작점에 스폰(`StartPath(centerline, speed)`), 속도 = `speed_limit_kmh / 3.6`. 기존 로컬라이제이션과 `onLocalization` 은 그대로 돈다
3. **이탈점**: 차로 위 `x_exit = target.x − 2 − |target.y − y_lane|` 인 점. 45° 사선을 만든다. 차로 시작점보다 뒤면 시작점으로 클램프. 실제 아크길이가 이탈점에 닿으면 그 순간의 추정 pose(`_prev`; 없으면 실제 pose)로 접근 경로를 짠다. `onScenario{event:"leave_lane", detail:"est x y ψ"}`
4. **접근 경로**: `[est, (target.x − 2, target.y), target]`. 마지막 2 m 구간이 `target.heading_deg` 를 만든다(현재 구획은 모두 0°이며 함수는 일반형). 실행은 개루프: 차량은 자기가 `est` 의 위치와 방향에 있다고 믿으므로, 계획 경로 전체를 믿음 좌표계에서 실제 좌표계로 옮기는 **강체변환**(이탈 지점 중심 `truth.psi − est.psi` 회전 + `truth − est` 이동)을 적용한 폴리라인을 `LaneFollower` 로 2 m/s 로 달린다. 그래서 이탈 순간의 추정 오차가 위치 2축과 방향까지 그대로 주차 오차가 된다. 평행이동만 적용하면 차량이 언제나 목표 heading 에 정렬해 끝나 방향 오차가 구조적으로 0 이 되고 허용값의 heading 축이 죽는다(2026-09-17 사용자가 큰 노이즈로 직접 돌려 발견한 결함). 코드에 `ponytail:` 주석(업그레이드 경로: 매 프레임 추정으로 pure pursuit)
5. 경로 끝에서 정차 → `Judge`: 실제 pose 를 `target_pose` 프레임으로 회전해 `err_lon`(진행 방향, m), `err_lat`(옆, m), `err_heading`(도, wrap). `|err_lat| ≤ tolerance.lat_m && |err_lon| ≤ tolerance.lon_m && |err_heading| ≤ tolerance.heading_deg` 면 `filled`, 아니면 `needs_adjust`
6. `onSlotFilled{slot_id, status, err_lat, err_lon, err_heading}` 송신. Unity 는 `CurrentMap` 의 status 와 채움면 색을 즉시 바꾸고(`MapOverlay.SetStatus`), 정차한 실제 pose 에 주차 차량 박스(정적 큐브 4.8 × 1.5 × 1.85, 이름 `PARKED-{slot_id}`)를 Overlay 의 갑판 그룹 아래 남긴다. 웹은 재로드하지 않는다
7. 1 로 돌아가 다음 차량

### 3.3 하역(unload)

1. 대상 = `status ∈ {filled, needs_adjust}` 중 `sequence_no` 최대. 없으면 `finished(no_filled_slot)`
2. 그 구획의 주차 박스를 지우고 그 자리(`target_pose`)에 차량 스폰
3. **출차 경로**: `[target, (target.x − 2, target.y), (target.x − 2 − |target.y − y_lane|, y_lane), 차로 시작점]`. 마지막 구간은 선미 방향(ψ = 180°)이다. 노이즈는 쓰지 않고 실제 pose 로 달린다
4. 경로 끝에서 차량 제거 → `onSlotFilled{slot_id, status:"empty"}`(오차 필드 없음) → status·색 갱신 → 1 로

### 3.4 Load 시 복원

`Load` 가 `status ∈ {filled, needs_adjust}` 인 구획마다 `target_pose` 에 주차 박스를 세운다. 새로고침·재로드 뒤에도 하역이 성립한다. 박스는 오버레이와 함께 파괴·재생성된다.

### 3.5 ScenarioPlanner (순수 정적)

```
IsFilled(string status)                                    → status 가 "filled" 또는 "needs_adjust" 인지
NextSlot(List<ParkingSlot> slots, string mode)             → ParkingSlot 또는 null
ApproachPath(Pose2D est, TargetPose t, double z)           → double[][] {est, (t.x−2, t.y), (t.x, t.y)}
ToTruthFrame(double[][] path, Pose2D est, Pose2D truth)    → double[][] 믿음 좌표계 → 실제 좌표계 강체변환
ExitS(Lane lane, TargetPose t)                             → 이탈점의 아크길이(0 이상으로 클램프)
DeparturePath(TargetPose t, Lane lane, double z)           → double[][]
Judge(Pose2D truth, TargetPose t, Tolerance tol)           → (status, errLat, errLon, errHeadingDeg)
```

`ToTruthFrame(path, est, truth)` 는 믿음 좌표계의 경로를 실제 좌표계로 옮기는 강체변환 헬퍼다(이탈 지점을 중심으로 `truth.psi − est.psi` 만큼 회전한 뒤 `truth − est` 만큼 이동). 평행이동만 하면 차량이 항상 목표 heading 에 정렬해 끝나 방향 오차가 구조적으로 0 이 된다. `FinalRunM`(2.0)·`ParkSpeedMps`(2.0) 는 접근·출차 구간의 상수다. 모두 EditMode 테스트 대상이다.

### 3.6 시간 배율

`SetTimeScale{scale}` → `Time.timeScale = scale`(1·5·20). `onLocalization` 의 0.2 초 타이머도 `deltaTime` 이라 같이 빨라지며, 웹 갱신 빈도는 배율에 비례한다. 편집 모드로 돌아가면 1 로 되돌린다.

---

## 4. pose 와 선체 기울임

### 4.1 원칙

Map 루트(`MapRuntime.transform`)의 **로컬 좌표가 Ship Frame, 월드 좌표가 Quay Frame** 이다. `SetPose` 는 Map 루트 하나만 회전시킨다. 자식(선체·마커·오버레이·차량·주차 박스)의 로컬 좌표는 그대로라 "선내 좌표 불변"이 구조적으로 보장된다. 기본 스펙 §3.2("Unity 월드 = 부두 프레임")와 §3.3("Ship→Unity 고정 매핑")은 이 해석으로 둘 다 성립한다.

### 4.2 회전

- 회전 원점: AP × 기선 × 중심선(Map 루트 원점)
- `trim_deg = atan((draft_aft_m − draft_fwd_m) / lpp_m)` 을 Unity 가 다시 계산한다. 양수(선미가 깊음)면 **선수가 올라간다**
- `heel_deg` 양수면 **우현이 내려간다**
- 부호는 EditMode 테스트가 고정한다: 선수 점 `(100, 0, 10)` 의 월드 y 가 trim 2° 에서 커지고, 우현 점 `(50, −10, 10)` 의 월드 y 가 heel 3° 에서 작아진다. 구현은 `Quaternion.Euler` 의 축 부호를 테스트에 맞춘다
- 적용 뒤 `Physics.SyncTransforms()`(프로젝트는 autoSyncTransforms 꺼짐). 마커 배치·드래그 레이캐스트가 기울어진 갑판에 맞는다
- 흘수 절대값·조위·`quay_z_m` 에 따른 상하 이동은 M5b(부두)에서 다룬다. M5a 는 회전만

### 4.3 램프

페이로드의 `ramp.angle_deg` 를 `ShipMeshBuilder.SetRampAngle` 에 넘긴다. 각도 산식은 API 에만 있고 Unity 는 값을 받기만 한다. `ramp.state` 는 HUD 한 줄(`ramp 3.2° deployed`)로 보인다.

### 4.4 순서

`SetPose` 가 `Load` 보다 먼저 와도 되게 마지막 pose 를 보관하고 `Load` 끝에 다시 적용한다. 선체(`Ship`)가 아직 없으면 회전만 적용하고 램프는 건너뛴다.

### 4.5 월드 좌표를 쓰던 자리

| 위치 | 현재 | 변경 |
| --- | --- | --- |
| `MapRuntime.Awake` | `ShipMeshBuilder.Build(_seed, shipParams)` 가 씬 루트에 생성 | `transform` 을 부모로 넘긴다 |
| `ShipMeshBuilder.Prim`, 램프 위치 | `transform.position` | `localPosition` |
| `ShipMeshBuilder.SetRampAngle` | `ramp.rotation` | `ramp.localRotation` |
| `VehicleController.Apply` | `position`/`rotation` | `localPosition`/`localRotation` (부모 = Map 루트) |
| `LandmarkMarker.Spawn` | `ToUnity(...)` 를 `MoveTo` 에 월드로 전달 | `parent.TransformPoint`/`TransformDirection` 뒤 전달. `MoveTo` 는 월드 입력 그대로(배치·드래그의 레이캐스트 결과가 월드) |
| `LandmarkMarker.ToModel` | `ToShip(transform.position …)` | `ToShip(parent.InverseTransformPoint(...))`, 법선은 `InverseTransformDirection` |
| `MapRuntime` `Placer.Created` | `ToShip(lm.transform.position)` | `lm.ToModel()` 값 재사용 |
| `HudView.CursorShip`, 갑판 라벨 | `ToShip(hit.point)`, `ToUnity(...)` 로 스크린 투영 | 루트 `InverseTransformPoint` / `TransformPoint` |
| `MapOverlay.Line` | `useWorldSpace = true` | `false` |

카메라(`OrbitCamera`)와 센서 차폐 레이캐스트(`LandmarkSensor.Sense`)는 월드끼리 비교하므로 손대지 않는다. `IsVisibleGeometric` 은 Ship Frame 값만 쓴다.

### 4.6 SetPose 페이로드

```json
{ "draft_fwd_m": 8.1, "draft_aft_m": 8.6, "heel_deg": 0, "lpp_m": 120, "ramp": { "id": "RAMP-STERN", "angle_deg": 3.2, "state": "deployed" } }
```

`ramp` 는 없을 수 있다(램프 조회 실패). `lpp_m` 은 dataset 값.

---

## 5. 웹

### 5.1 브리지 (`useShipUnity`)

- 스토어의 `pose`·`ramp`·`dataset.lpp_m` 가 바뀌면 `SetPose` 를 보내는 효과 하나(기존 `SetMode` 효과와 같은 꼴). 초기 `Load` 직후에도 한 번 보낸다
- `onSlotFilled` → 스토어 `onSlotFilled(e)`, `onScenario` → 스토어 `appendLog`

### 5.2 스토어

- `scenarioLog: ScenarioLine[]`(최근 100줄, `{t, text}`), `appendLog(text)`, `clearLog()`(시나리오 시작 시)
- `onSlotFilled(e)`: `api.putSlotStatus(ds, e.slot_id, e.status)` → `bumpVersion` → 로그 `PS-D3-012 filled  lat +0.04 lon −0.11 hdg +0.6°`(하역은 `PS-D3-012 empty`). 실패 시 `error` 배너. 재로드하지 않는다
- `mode` 를 `edit` 로 바꾸면 `SetTimeScale{scale:1}` 도 보낸다(Unity 도 되돌리지만 웹 셀렉트 표시를 맞춘다)

### 5.3 DrivePanel

- 버튼: `▶ 선적`, `◀ 하역`, `정지`(= `setMode("edit")`)
- 시간 배율 셀렉트 ×1/×5/×20 → `SetTimeScale`
- 기존 노이즈 슬라이더·위치 추정 패널 유지
- 로그 목록(최신 위, 고정폭 글꼴, 스크롤)

### 5.4 타입·클라이언트

```ts
type SlotStatus = "empty" | "filled" | "needs_adjust";
type SlotFilledEvt = { slot_id: string; status: SlotStatus; err_lat?: number; err_lon?: number; err_heading?: number };
type ScenarioEvt = { event: "start" | "target" | "leave_lane" | "finished"; mode?: "load" | "unload"; slot_id?: string; detail?: string };
type ScenarioLine = { t: string; text: string };
api.putSlotStatus(ds, sid, status) → { id, status }   // PUT /datasets/{ds}/slots/{sid}/status
```

---

## 6. 브리지 계약 변경 (기본 스펙 §10, `docs/api-contract.md`)

| 방향 | 메시지 | 페이로드 |
| --- | --- | --- |
| R→U | `SetPose` | `{draft_fwd_m, draft_aft_m, heel_deg, lpp_m, ramp?:{id, angle_deg, state}}` |
| R→U | `StartScenario` | `{mode:"load"\|"unload"}` — `map` 필드 제거. 맵은 `Load` 된 것 |
| R→U | `SetTimeScale` | `{scale}` |
| U→R | `onSlotFilled` | `{slot_id, status, err_lat?, err_lon?, err_heading?}` — 하역 완료는 `status:"empty"`, 오차 없음 |
| U→R | `onScenario` | `{event:"start"\|"target"\|"leave_lane"\|"finished", mode?, slot_id?, detail?}` |

---

## 7. 픽스처

- `FixtureExporter.BuildMap` 의 래싱 창 필터(`x 94~105, y 2~4 || needed`)를 지우고 D3 래싱 전체(약 4,700 점)를 내보낸다. 슬롯 모서리 재매핑 로직은 유지
- 픽스처는 약 28 KB → 약 776 KB. Unity 는 이미 시드로 전 갑판 래싱을 그리므로 렌더 부담은 늘지 않는다. `Load` 페이로드와 DB 시드 행 수만 는다
- 메뉴 `ShipHdMap/Export vehicle-map fixture (from seed)` 로 재생성(배치 모드 가능)
- 같은 파일에서 랜드마크 좌표의 상수(x `12+12i`, y `±6.2`, z `11.8`, 선체 y `11.9`)를 시드에서 유도한다: 기둥은 풋프린트의 중심선 쪽 면, z 는 `deck.z_surface + 1.2`, 선체 마커는 갑판 윤곽의 좌현 y − 0.1. 선박 모델(`ShipParams`)이 바뀌어도 마커가 구조물 위에 앉는다. 결과 픽스처의 좌표는 현재와 같아야 한다(회귀 확인)
- API 테스트 3곳(`SeedImportTests`, `VehicleMapExportTests`, `GeoJsonExportTests`)은 49 대신 픽스처 JSON 의 `lashing_points` 길이를 읽어 비교한다. 다음 재생성에도 흔들리지 않는다
- 브라우저 검증은 DB 재시드 → 구획 재생성 → 래싱 매핑 100 % 확인 순서로 시작한다

---

## 8. 기본 스펙 정정 (같은 커밋)

1. §4.2: `transition_lm_ids` → `transition_landmarks`(코드·API·Unity 모두 이 이름)
2. §9.2: "장애물(기둥·램프 풋프린트)" → "장애물(기둥; 램프는 힌지가 윤곽선이라 몸체가 갑판 밖이므로 제외, 진입로는 차로 회랑이 지킨다)"
3. §10 표: `SetPose`·`StartScenario`·`onSlotFilled` 페이로드 갱신, `SetTimeScale`·`onScenario` 행 추가
4. §3.3: "Unity 씬에서는 Map 루트의 로컬 좌표가 Ship Frame, 월드가 Quay Frame 이다. pose 는 Map 루트의 회전(M5b: 위치)으로만 반영한다" 한 줄 추가
5. §4.1: heel 양수 = 우현 아래, trim 양수 = 선수 위 명시
6. §9.3·§13: M5 행을 M5a/M5b 로 분할

---

## 9. 테스트

목표: api 42→42(내용만 변경), Unity EditMode 53→78, web 22→26.

- **Unity EditMode**
  - `ScenarioPlanner`: `NextSlot` 이 load 에서 `empty` 중 최소 `sequence_no`, unload 에서 `filled/needs_adjust` 중 최대; `ApproachPath` 끝점 = target, 마지막 구간 heading = `target.heading_deg`; `ExitS` 45° 와 0 클램프; `Judge` 경계(각 오차를 tolerance 바로 안/밖)
  - `SetPose`: 선수 점 상승, 우현 점 하강, 마커·차량의 `ToModel`/`Truth` 가 pose 전후 동일(1e-6), 램프 `localRotation` 이 `angle_deg` 를 따름
  - 시나리오: 픽스처 로드 → `StartScenario(load)` → `Step` 반복 → `onSlotFilled` 1회(status `filled`, 노이즈 0) → 다음 차량이 차로 시작점에 있음; `StartScenario(unload)` → `empty` 송신; `Load` 가 `filled` 구획에 `PARKED-*` 박스를 세움
- **Web Vitest**: `onSlotFilled` → `putSlotStatus` 호출·로그 1줄·version 갱신; 실패 → `error`; 로그 100줄 상한; pose 변경 → `SetPose` 송신(브리지 훅은 기존 테스트 방식이 있으면 따르고 없으면 스토어 단위만)
- **API**: 3곳을 픽스처 파생 값으로

---

## 10. 브라우저 검증 8항목

1. DB 재시드 → D3 구획 재생성 → 래싱 매핑 100 %
2. pose 슬라이더(흘수 선미 +1 m)로 선수가 올라가고, 마커를 선택한 속성 폼의 좌표가 그대로
3. 횡경사 −3°/+3° 로 좌우 기울임, 조위 변경으로 램프 각도·상태 텍스트 변화
4. 선적 ×20 배율, 노이즈 기본값으로 3대 연속 주차, 로그 3줄, 3D 색 변화, DB status 변화(`curl`)
5. 노이즈 0 → 전부 `filled`
6. σ 거리 1 m, σ 방위 5° → `needs_adjust` 발생
7. 하역 → 역순으로 비워지고 로그·색·DB 가 따라옴
8. 새로고침 뒤 주차 박스가 복원되고 하역이 이어짐

---

## 11. 위험

- **월드→로컬 전환 누락**: 자리 하나를 빠뜨리면 pose 0 에서는 티가 안 나고 기울일 때만 어긋난다. §4.5 표를 계획서 체크리스트로 옮기고, "pose 전후 좌표 동일" 테스트를 마커·차량·커서 셋에 건다
- **WebGL 에서 `Time.timeScale` 과 브리지 빈도**: ×20 에서 `onLocalization` 이 초당 100회가 되어 React 렌더가 밀릴 수 있다. 밀리면 emit 타이머를 `unscaledDeltaTime` 으로 바꾼다
- **픽스처 크기**: 776 KB JSON 을 WebGL 에서 파싱하는 시간은 M4 의 136 구획 로드에서 문제없던 규모의 15배다. 브라우저 검증 1 에서 로드 시간을 본다
