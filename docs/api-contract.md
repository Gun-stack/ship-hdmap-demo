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
