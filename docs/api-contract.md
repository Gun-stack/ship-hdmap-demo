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
- `POST /datasets/{id}/decks/{deck}/slots/generate` `{vehicle_class?, gap_lat_m?, gap_lon_m?, lashing_pitch_m?}`(기본 passenger, 0.30, 0.40, 0.75) → `{deck, count, utilization, lashing_coverage, version, slots[]}`. 그 갑판의 기존 구획을 전부 교체한다. 진입 경로(이탈점 → 45° 사선 → 5 m 직진)가 장애물에 막히거나 이탈점이 차로 밖으로 나가는 셀은 구획이 되지 않는다 · 404 갑판 없음 · 400 알 수 없는 차량 등급
- `PUT /datasets/{id}/slots/{sid}/status` `{status: empty|filled|needs_adjust|unreachable}` → `{id, status}`. `needs_adjust` 는 댔는데 삐뚤어진 것, `unreachable` 은 가지 못한 것
- `POST /api/datasets/{ds}/decks/{deck}/coverage` — 갑판 격자의 달성 가능 추정 정밀도. 본문 전체가 선택: `mode`(`load`|`unload`, 기본 `load`), `grid_m`(기본 1.0, (0,10]), `fov_deg`·`max_dist_m`·`max_view_angle_deg`·`sigma_r`·`sigma_theta`·`sigma_alpha`(기본은 Unity `LandmarkSensor` 의 필드 기본값과 같은 값이고, 각도는 도. **등식을 지키는 것은 실행 중의 `SetSensor` 브리지 메시지다** — 웹이 이 세 값을 API 와 Unity 양쪽에 같은 이름으로 보내므로, 기본값 세 벌이 어긋나 있어도 화면에 사는 것은 웹이 보낸 한 값뿐이다. 첫 `Load` 전까지만 Unity 의 기본값이 산다), `extra_landmarks[{x,y,phi_deg}]`, `omit[id]`. 응답 `cells[{x,y,n,sigma_xy,sigma_psi,stability,in_scope}]` — `n=0` 이면 σ 생략, `in_scope` 는 false 일 때만 실린다. `n_cells` 는 **구획 ∪ 차로 회랑** 셀 수이자 `blind_ratio`·`weak_ratio` 의 분모이고, `n_drawn` 은 `cells` 길이(갑판 전체)다. `worst`·`other_mode` 동봉. **읽기 전용 — DB 를 바꾸지 않는다**
- `POST /api/datasets/{ds}/decks/{deck}/coverage/suggest` — 다음에 놓을 마커 위치. 위 파라미터 + `budget`(기본 3, 최대 10). **`grid_m` 은 무시하고 2.0 으로 고정하며 응답에 실제 쓴 값을 싣는다.** 응답 `before`·`after`·`suggestions[{rank,x,y,phi_deg,mounted_on,blind_after,weak_after,gain}]`. 순위는 누적(2 순위는 1 순위를 놓은 상태의 계산)이고 `gain` 은 그 순위에서 줄어든 `blind_ratio` 다
- `GET /datasets/{id}/vehicle-map` → 스펙 §6 JSON, `ETag`
- `GET /datasets/{id}/export.geojson` → WGS84 FeatureCollection (`application/geo+json`)
- `GET|PUT /datasets/{id}/pose` → pose + `trim_deg`. PUT 은 부분 갱신, `measured_at` 은 요청값으로 교체
- `GET /datasets/{id}/ramps/{rid}` → 램프 props + `hinge`, `angle_deg`, `state(deployed|blocked)`

## 브리지 (React ↔ Unity, 스펙 §10)

- R→U `sendMessage("Map", name, json)`: `Load`, `SetMode("edit"|"drive")`(edit 는 시나리오 중단·차량 숨김·배율 1), `SetDeck("D3"|"all")`, `Select(id)`(씬 하이라이트만; 에코 없음; 빈 문자열이면 해제), `Confirm({tempId,id})`, `Delete(id)`, `SetNoise({sigma_r,sigma_theta,sigma_alpha,sigma_gps})`, `SetPose({draft_fwd_m,draft_aft_m,heel_deg,lpp_m,ramp?:{id,angle_deg,state}})`(Map 루트 회전 + 램프 각도; 각도는 API 가 계산), `StartScenario({mode:"load"|"unload"})`(맵은 `Load` 된 것; 연속 적재/하역), `SetTimeScale({scale})`
- U→R 이벤트(`addEventListener(name, (json) => …)`): `onSeedReady`, `onFeatureCreated{tempId,layer,x,y,z,deck,mounted_on,normal}`, `onFeatureMoved{id,x,y,z,normal,deck,mounted_on}`, `onSelected{id}`, `onLocalization{est_x,est_y,est_psi,true_x,true_y,true_psi,residual_rms,n_obs,frame}`, `onSlotFilled{slot_id,status,err_lat?,err_lon?,err_heading?}`(하역 완료는 `status:"empty"`, 오차 없음; 웹이 `PUT /slots/{sid}/status`), `onScenario{event:start|target|leave_lane|finished,mode?,slot_id?,detail?}`
