# 선박 HD map 데모 설계 스펙 (2026-09-15)

RORO 선박 내부 정밀지도 편집과 자율차 로컬라이제이션을 다루는 개인 학습·시연용 데모다. 브레인스토밍에서 확정한 결정만 담는다.

---

## 1. 목적과 범위

### 1.1 목적

- 운전자 없는 자율차를 RORO(자동차운반선) 선박 안의 정해진 위치에 정확하고 효율적으로 주차시켜, 한 배에 최대한 많은 차를 싣는다
- 그 근거 데이터가 되는 선내 정밀지도(국내 정밀도로지도 HD map 표준의 레이어·속성 구조)를 웹에서 3D로 편집·저장하고, 자율차에 전달하고, 자율차가 그 지도로 자기 위치를 잡는 과정을 한 화면에서 시연한다

### 1.2 데모가 보여주는 것

1. 편집기: RORO 선박 3D 모델 위에 차로·차선·주차구획·시설물·랜드마크·래싱 포인트를 배치·수정·삭제하고 PostGIS에 저장
2. 적재 계획: 갑판·차량 등급·간격을 주면 주차구획 격자를 자동 생성하고 대수와 면적 활용률을 표시
3. 지도 전달: 선박 좌표계(Ship Frame) 기반 차량 지도 JSON을 REST로 제공
4. 주행 시뮬레이션: 가상 차량이 부두에서 램프를 거쳐 갑판에 들어와 랜드마크만으로 위치를 추정하며 지정 구획에 주차
5. 선박 자세: 흘수·트림·횡경사·조위가 바뀌어도 선내 지도 좌표는 불변임을 화면으로 확인
6. GIS 검증: 저장 데이터를 WGS84로 파생해 QGIS에서 확인

### 1.3 제외

- 경로 탐색(A* 등). 차량은 차로중심선 그래프를 순서대로 따라간다
- 실제 이미지 인식(OpenCV/ArUco). 랜드마크 인식은 레이캐스트로 대체
- 파티클 필터/EKF. 최소제곱 단발 추정만
- 인증·권한·다중 사용자·운영 배포
- 가동 갑판(hoistable deck) 이동 동작. 스키마 필드만 둔다
- 차량 등급 다종. 승용 1종
- 래싱 종류 다종. 클로버리프 소켓 1종

---

## 2. 전체 구조

```
ship-hdmap-demo/
  api/        Spring Boot 4 + JdbcClient + PostGIS     저장·조회·구획 생성·지도 내보내기
  web/        React 19 + Vite + TS + Zustand           2D UI, 속성 폼, 적재 계획, pose, 시나리오 제어
              react-unity-webgl                        React ↔ Unity 브리지
  unity/      Unity 6.3 LTS (C#), WebGL 빌드           편집 모드 + 주행 모드 (빌드 하나)
  db/         docker-compose (imresamu/postgis:17-3.5) 스키마 SQL
  docs/       설계·스펙·QGIS 검증 절차
```

- Unity WebGL 빌드 산출물은 `web/public/unity/` 에 둔다
- Unity 빌드 하나가 `편집`·`주행` 두 모드를 가진다. React 상단 탭이 `SetMode` 로 전환한다
- 상태 관리는 Zustand 스토어 하나. Redux 는 쓰지 않는다

### 2.1 컴포넌트 책임

| 컴포넌트 | 책임 |
| --- | --- |
| unity 편집 모드 | RORO 모델 로드, 갑판별 표시·반투명, RaycastHit 배치·이동·삭제, 구획 생성 결과 렌더, 선택 이벤트 |
| unity 주행 모드 | 가상 차량, FOV·거리 제한 레이캐스트 관측(노이즈), 최소제곱 위치 추정, 프레임 전환, 주차 판정, pose 반영 선체 기울임, 램프 각도 |
| web | 갑판·레이어 트리, 2D 평면도 미니맵, 속성 폼, 적재 계획 패널, pose 슬라이더, 시나리오 제어, API 호출 |
| api | 데이터셋·피처 CRUD, 구획 자동생성, vehicle-map·pose·GeoJSON, Ship Frame→WGS84 파생 |
| db | PostGIS 저장, WGS84 파생 지오메트리, QGIS 접속 대상 |

---

## 3. 좌표계

### 3.1 Ship Frame (기준 좌표계)

- 원점: AP(선미수선) × 기선(Baseline, 몰디드 킬 상면) × 선체 중심선
- X: AP→선수 방향 (+), 단위 m
- Y: 좌현 (+), 우현 (−)
- Z: 기선→상방 (+)
- 선박은 강체이므로 선내 모든 객체의 Ship Frame 좌표는 배가 어떻게 떠 있든 변하지 않는다

### 3.2 Unity 매핑

- Ship(x, y, z) → Unity(x, z, −y). Unity X=선수 방향, Y=상방, Z=우현
- 변환 함수는 C# `ShipFrame.cs`, TS `shipFrame.ts`, Java `ShipFrame.java` 에 각각 두고, 같은 테스트 벡터 파일 `docs/test-vectors/ship-frame.json` 로 세 벌을 검증

### 3.3 Z 정의

- 갑판마다 `z_surface`(차량이 밟는 면), `z_clear`(천장 유효높이)
- 차량은 2.5D. 로컬라이제이션은 x·y·헤딩만 풀고 z 는 `deck_id` 조회로 확정
- 가동 갑판: `movable`, `z_positions[]`, `z_current`. 데모는 고정 갑판만 사용

### 3.4 WGS84 파생

- DB 기준 좌표는 Ship Frame. WGS84 는 저장 시 API 가 계산해 `geom_wgs84` 컬럼에 함께 넣는다
- 변환 입력: pose 의 `ap_lat`, `ap_lon`, `heading_deg`. 평면 근사(동서·남북 m→도 변환)로 충분. `ST_Transform` 은 쓰지 않는다
- QGIS 는 `geom_wgs84` 를 본다. 자율차는 Ship Frame 만 받는다

---

## 4. 선박 자세(pose)와 램프

### 4.1 pose

- 지도가 아니라 상태. DB 에 두지 않고 API 메모리에 둔다. 슬라이더 값이 곧 상태
- 필드: `draft_fwd_m`, `draft_aft_m`, `trim_deg`, `heel_deg`, `heading_deg`, `tide_m`, `quay_z_m`, `ap_lat`, `ap_lon`, `measured_at`
- 6자유도 강체변환 하나로 Ship Frame ↔ 부두 좌표를 오간다
- 선내 주행 중 차량은 pose 를 몰라도 된다. 램프를 건널 때만 필요

### 4.2 램프

- 힌지 라인·길이·폭·허용 각도 범위는 Ship Frame 에 고정된 지도 정보
- `angle_deg` 는 파생: `asin(((quay_z + tide) − (hinge_z − draft_aft)) / length)`. 범위 밖이면 `state: "blocked"`
- 램프 위 차로는 저장하지 않고 힌지·각도로 실시간 생성
- `transition_landmarks`: 램프 입구 랜드마크 쌍. 둘을 동시에 인식하면 부두 프레임→Ship Frame 으로 전환, 하역 시 역방향

### 4.3 선하역 시나리오

1. 접안 → pose 수신 → 램프 각도 계산 → 범위 내면 `deployed`
2. 차량: 부두 프레임 주행 → 입구 랜드마크 쌍 인식 → Ship Frame 전환 → 램프·A2 차로 → `sequence_no` 순 구획 도착
3. 적재가 늘며 흘수·횡경사 변화 → pose 갱신 → 램프 재계산. 선내 지도는 불변
4. 하역은 역순. 램프 `stowed`

---

## 5. 데이터 모델

### 5.1 레이어

| 레이어 | 지오메트리 | 내용 |
| --- | --- | --- |
| A2 차로중심선 | LineString | 차량 이동 경로. `width_m`, `direction`, `speed_limit_kmh`, `next[]` |
| A1 차선 | LineString | 차로 경계 |
| B2 노면표시 | Polygon / Point | 주차구획(핵심), 화살표·정지선 |
| C 시설물 | Point / Polygon | 기둥, 램프, 방열문 |
| LM 랜드마크 | Point | apriltag-36h11 마커. `code`, `normal`, `size_m`, `mounted_on` |
| LP 래싱 포인트 | Point | 클로버리프 소켓. 갑판 격자 매립 |
| MEP | LineString / Polygon | 배관·덕트·케이블트레이. 랜드마크 부착 대상. 표시 전용 |

### 5.2 주차구획(parking_slot)

- 폴리곤 + 목표 자세(`target_x`, `target_y`, `target_heading_deg`) + 허용 오차(`tol_lat_m` 0.15, `tol_lon_m` 0.30, `tol_heading_deg` 2) + 래싱 4점 참조 + `vehicle_class` `passenger` + `sequence_no` + `status`(`empty` / `filled` / `needs_adjust`)
- 래싱 4점이 차량 바퀴 기준 도달 거리 안에 있는지가 "정확한 주차"의 물리적 기준

### 5.3 차량 등급 (1종)

- `passenger`: 길이 4.8 m, 폭 1.85 m, 높이 ≤ 1.9 m
- 간격 기본값: 측면 0.30 m, 전후 0.40 m

### 5.4 DB 스키마 (PostGIS)

```sql
dataset      (id, name, ship_name, version int, ap_lat, ap_lon, heading_deg, created_at)
deck         (id, dataset_id, code, name, z_surface, z_clear, movable bool, z_current)
feature      (id, dataset_id, deck_id, layer text, kind text,
              geom_ship  geometry(GeometryZ, 0),
              geom_wgs84 geometry(GeometryZ, 4326),
              props jsonb, created_at, updated_at)
parking_slot (feature_id PK→feature, target_x, target_y, target_heading_deg,
              tol_lat_m, tol_lon_m, tol_heading_deg, vehicle_class, sequence_no, status)
slot_lashing (slot_id→parking_slot, lashing_feature_id→feature)   -- 구획당 4행
ramp         (id, dataset_id, type, hinge geometry(LineStringZ, 0), length_m, width_m,
              angle_min_deg, angle_max_deg, connects_lane_id, transition_lm_ids text[])
```

- 랜드마크·래싱·기둥·차선은 전부 `feature` 에 두고 `layer`+`props` 로 구분. 주차구획만 컬럼이 많아 별도 테이블
- `dataset.version` 은 피처 변경마다 +1. vehicle-map 의 ETag
- `dataset.ap_lat/ap_lon/heading_deg` 는 정박 기준값. 런타임 pose 가 있으면 pose 값이 우선하고, 없으면 이 값으로 WGS84 를 파생

---

## 6. 차량 지도 포맷 (vehicle-map v1)

- 자체 JSON 봉투 + GeoJSON 좌표 규약(`[x, y, z]` 배열). 좌표는 Ship Frame, 단위 m
- Lanelet2/OpenDRIVE 는 쓰지 않는다. 필요 시 나중에 변환기 하나

```json
{
  "schema": "ship-hdmap/vehicle-map/1.0",
  "map_id": "roro-demo-01",
  "version": 7,
  "generated_at": "2026-09-15T09:00:00Z",
  "frame": { "name": "SHIP_AP", "origin": "AP × Baseline × Centerline",
             "axes": { "x": "AP→선수 (+)", "y": "좌현 (+)", "z": "기선→상방 (+)" }, "unit": "m" },
  "decks": [ { "id": "D3", "name": "Deck 3", "z_surface": 10.6, "z_clear": 2.2, "movable": false } ],
  "landmarks": [ { "id": "LM-0042", "marker": { "family": "apriltag-36h11", "code": 42 },
                   "position": [82.4, 3.1, 10.6], "normal": [0, -1, 0], "size_m": 0.30,
                   "deck_id": "D3", "mounted_on": "C-PILLAR-017" } ],
  "lanes": [ { "id": "A2-0007", "deck_id": "D3",
               "centerline": [[10.0, 2.5, 10.6], [40.0, 2.5, 10.6], [80.0, 2.6, 10.6]],
               "width_m": 3.2, "direction": "forward", "speed_limit_kmh": 10, "next": ["A2-0008"] } ],
  "parking_slots": [ { "id": "PS-D3-041", "deck_id": "D3", "polygon": [[...]],
                       "target_pose": { "x": 82.4, "y": 3.1, "heading_deg": 0 },
                       "tolerance": { "lat_m": 0.15, "lon_m": 0.30, "heading_deg": 2 },
                       "vehicle_class": "passenger", "lashing_points": ["LP-1203", "LP-1204", "LP-1219", "LP-1220"],
                       "sequence_no": 41, "status": "empty" } ],
  "lashing_points": [ { "id": "LP-1203", "kind": "cloverleaf", "position": [81.2, 2.0, 10.6], "deck_id": "D3" } ],
  "markings": [ { "id": "B2-0101", "kind": "arrow", "polygon": [[...]], "deck_id": "D3" } ],
  "facilities": [ { "id": "C-PILLAR-017", "kind": "pillar", "footprint": [[...]], "z_min": 9.2, "z_max": 11.8, "deck_id": "D3" } ],
  "ramps": [ { "id": "RAMP-STERN", "type": "stern_quarter",
               "hinge": [[2.0, -6.0, 11.8], [2.0, 6.0, 11.8]], "length_m": 30, "width_m": 12,
               "angle_range_deg": [-7, 4], "connects_lane": "A2-0001",
               "transition_landmarks": ["LM-0001", "LM-0002"] } ]
}
```

- 램프의 `state`·`angle_deg` 는 지도가 아니라 `GET .../ramps/{id}` 응답에서 pose 로 계산해 준다
- 로컬라이제이션에 필요한 랜드마크 필드: `marker.code`(키), `position`(기준점), `normal`(가시성 판정), `size_m`(실차의 거리 역산용, 시뮬레이션은 레이캐스트 거리 사용)

---

## 7. REST API

```
GET    /api/datasets/{id}
GET    /api/datasets/{id}/features?deck=D3&layer=LM
POST   /api/datasets/{id}/features                      1건 생성 → id 반환
PUT    /api/datasets/{id}/features/{fid}
DELETE /api/datasets/{id}/features/{fid}
POST   /api/datasets/{id}/decks/{deck}/slots/generate   {vehicle_class, gap_lat_m, gap_lon_m} → 구획 목록·KPI
PUT    /api/datasets/{id}/slots/{sid}/status            {status}
GET    /api/datasets/{id}/vehicle-map                   ETag = version, If-None-Match → 304
GET    /api/datasets/{id}/export.geojson                WGS84 FeatureCollection (QGIS)
GET    /api/datasets/{id}/pose
PUT    /api/datasets/{id}/pose
GET    /api/datasets/{id}/ramps/{rid}                   angle_deg·state 포함
```

- 저장 단위는 피처 1건. 일괄 저장 버튼은 없고 미저장 카운트만 표시
- 오류: 4xx 는 `{error, field}` JSON, 5xx 는 메시지 없이 상태코드만

---

## 8. 화면 (편집기 A안)

- 상단 바: 제품명, `편집`/`주행` 탭, 데이터셋·버전, 저장·GeoJSON·차량지도 버튼
- 좌측: 갑판 선택 탭(D1~D5, 전체) → 선택 갑판의 레이어·객체 트리 → 2D 평면도 미니맵. 다른 갑판은 3D 에서 반투명
- 중앙: Unity WebGL. 도구 막대(선택·점·선·면·랜드마크·구획 자동생성·삭제), HUD(갑판, 커서 Ship Frame 좌표, 선택 객체)
- 우측: 적재 계획 패널(구획 수, 면적 활용률, 래싱 매핑, 차량 등급·간격·순서, 재생성) → 속성 폼 → pose 슬라이더
- 하단 상태바: 갑판, 선택 수, 미저장 변경 수, PostGIS 연결, 전 갑판 적재 가능 대수
- 주행 모드: 좌 전경 3D(pose 반영), 우상 차량 카메라 시야(인식 랜드마크 태그), 우하 로컬라이제이션 패널(관측 수·잔차·노이즈·프레임 전환), 맨 우측 pose·램프와 시나리오 로그
- 화면 시안 파일: `.superpowers/brainstorm/*/content/layout-v2.html` (gitignore 대상, 저장소에는 포함되지 않음)

---

## 9. 핵심 흐름

### 9.1 편집·저장

1. 페이지 로드 → React 가 features·decks·pose 조회 → `sendMessage("Map", "Load", json)` 으로 Unity 에 일괄 전달
2. 3D 클릭 배치 → Unity `onFeatureCreated{tempId, layer, x, y, z, deck}` → React draft 추가 → 속성 폼 → "적용" 시 POST → 응답 id 로 tempId 교체 → Unity `Confirm(tempId, id)`
3. 2D 트리·평면도 선택 ↔ Unity 선택은 양방향 `Select(id)` 하나로 통일
4. 저장 시 API 가 `geom_wgs84` 파생 계산, `dataset.version` +1

### 9.2 구획 자동생성

- 입력: 갑판 폴리곤, 장애물(기둥·램프 풋프린트), 래싱 격자, 차량 등급, 간격
- 갑판을 래싱 격자 간격으로 순회하며 차량 크기+간격 사각형이 갑판 안에 들어가고 장애물과 겹치지 않으면 구획 생성, 가장 가까운 래싱 4점 매핑
- 선수→선미, 깊은 곳 먼저 순으로 `sequence_no`
- 격자 채우기이며 최적화 없음. 코드에 `ponytail:` 표시(업그레이드 경로: 2D bin packing)

### 9.3 주행 시나리오

1. "선적" 클릭 → React 가 vehicle-map·pose 조회 → Unity `StartScenario(map, pose, "load")`
2. Unity: 램프 각도 계산 → 부두 시작점에 차량 스폰 → 입구 랜드마크 쌍 인식 시 프레임 전환 → A2 차로 따라 `sequence_no` 순 구획으로 주행 → 정차 후 오차 판정 → `onSlotFilled{slotId, err}` → React 가 PUT status
3. pose 슬라이더 변경 → Unity `SetPose` → 선체 기울임·램프 재계산

### 9.4 로컬라이제이션

- 관측: 카메라 FOV 90°, 거리 25 m 안에서 `normal` 반대편 반구에 있는 랜드마크를 레이캐스트로 확인 → (id, 거리 r, 상대방위 θ). 노이즈 σ 거리 0.2 m, 각 1°(슬라이더로 조절)
- 추정: 관측 2개 이상이면 첫 두 관측의 교점을 초기값으로 Gauss-Newton 5회 반복해 (x, y, ψ). 잔차와 실제 위치 대비 오차를 HUD 출력. 관측 1개면 이전 추정 유지

---

## 10. 브리지 계약 (React ↔ Unity)

| 방향 | 메시지 | 페이로드 |
| --- | --- | --- |
| R→U | `Load` | vehicle-map 과 같은 구조 + 편집용 전체 피처 |
| R→U | `SetMode` | `"edit"` / `"drive"` |
| R→U | `SetDeck` | deck_id 또는 `"all"` |
| R→U | `Select` | feature id |
| R→U | `Confirm` | tempId, id |
| R→U | `SetPose` | pose JSON |
| R→U | `StartScenario` | map, pose, `"load"` / `"unload"` |
| U→R | `onFeatureCreated` | tempId, layer, x, y, z, deck |
| U→R | `onFeatureMoved` | id, x, y, z |
| U→R | `onSelected` | id |
| U→R | `onSlotFilled` | slotId, err_lat, err_lon, err_heading |
| U→R | `onLocalization` | est_x, est_y, est_psi, true_x, true_y, residual, n_obs, frame |

- 페이로드는 전부 JSON 문자열. 좌표는 Ship Frame

---

## 11. 테스트

- Java (JUnit): ShipFrame 테스트 벡터, 구획 자동생성(기둥 하나 있는 20×10 m 갑판 → 기대 개수), vehicle-map 직렬화 스냅샷
- TS (Vitest): shipFrame.ts 같은 벡터, 스토어 draft→confirm 전이
- C# (Unity Test Framework EditMode): ShipFrame 벡터, 로컬라이제이션(정답 위치에서 관측 생성, 노이즈 0 → 오차 < 0.05 m)
- 통합: `docker compose up` → API 기동 → curl 시나리오 스크립트 1개 → QGIS 에서 export.geojson 열기 (수동, 절차 문서화)

---

## 12. 선행 조건과 환경

- Unity 6000.3.24f1 + WebGL 모듈 설치됨. CoplayDev MCP for Unity 10.2.0 이 `unity/` 에 들어 있고 Claude Code 와 stdio 로 연결됨 (에디터 창이 열려 있어야 함)
- JDK 25, Node 22, Docker, `imresamu/postgis:17-3.5` 이미지 준비됨
- RORO 선박 모델: Sketchfab CC 라이선스 후보를 먼저 찾고, 없으면 C# 으로 갑판·램프·기둥·MEP 를 프리미티브로 생성. glTF 임포트에는 glTFast 패키지 필요
- QGIS 는 검증 단계에서 설치

---

## 13. 구현 순서 (서브 프로젝트)

| 단계 | 산출물 | 완료 기준 |
| --- | --- | --- |
| M1 데이터·API | db 스키마, api CRUD, ShipFrame 3벌, vehicle-map·export.geojson | curl 로 피처 저장 → vehicle-map 조회 → QGIS 에서 WGS84 확인 |
| M2 선박 모델·편집 모드 | RORO 모델(또는 생성기), 갑판·MEP, RaycastHit 배치, 브리지, React 편집 화면 | 브라우저에서 랜드마크 배치·수정·삭제 후 DB 반영 |
| M3 적재 계획 | 래싱 격자, 구획 자동생성, KPI 패널 | Deck 3 에서 구획 생성, 대수·활용률 표시 |
| M4 주행 시뮬레이션 | 가상 차량, 관측·최소제곱, 프레임 전환, 주차 판정, pose·램프 | 선적 시나리오 완주, 오차 HUD, pose 슬라이더로 선체 기울임 |
| M5 문서·시연 | README(문어체 불릿), 시연 스크립트, 스크린샷 | 처음 보는 사람이 README 만으로 실행 |

각 단계 끝에 (1) 한 것 (2) 결정과 이유 (3) 검증 결과 (4) 직접 확인 명령 순으로 보고하고 다음 단계는 확인 후 진행한다.
