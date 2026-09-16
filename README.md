# ship-hdmap-demo

RORO(자동차운반선) 선박 내부 정밀지도 편집기와 자율차 로컬라이제이션 시뮬레이션 데모. 개인 학습·시연용.

## 목적

- 운전자 없는 자율차를 선박 안 지정 위치에 정확·효율적으로 주차시켜 한 배에 최대 대수를 싣는 흐름을 한 화면에서 시연
- 선내 정밀지도(HD map 레이어·속성 구조)를 웹 3D에서 편집·저장 → 선박 좌표계 지도로 차량에 전달 → 차량이 랜드마크만으로 자기 위치 추정

## 구성

- `unity/` Unity 6.3 LTS (WebGL). 편집 모드 + 주행 시뮬레이션 모드, 빌드 하나
- `web/` React 19 + Vite + TS + Zustand. Unity WebGL 을 품은 편집기: 갑판 트리·미니맵·속성 폼·pose·주행 패널. `./scripts/m3-dev.sh` 로 실행
- `api/` Spring Boot 4 + JdbcClient + PostGIS. 데이터셋·피처 CRUD, 시드 적재, vehicle-map(ETag), pose·램프, WGS84 GeoJSON. `./scripts/m2-smoke.sh` 로 종단 확인
- `db/` docker compose PostGIS 17 (호스트 5433)
- `docs/superpowers/specs/` 설계 스펙

## 핵심 설계

- 좌표계: AP(선미수선) × 기선 × 중심선 원점, X 선수(+), Y 좌현(+), Z 상방(+). 선박은 강체라 선내 좌표는 배가 어떻게 떠 있든 불변
- 선박 자세(흘수·트림·횡경사·조위)는 지도가 아니라 별도 상태. 램프 각도는 자세에서 파생
- 랜드마크(AprilTag 36h11) 관측 → 최소제곱으로 (x, y, 헤딩) 추정. 램프 입구 랜드마크 쌍 인식 시 부두 좌표계 → 선박 좌표계 전환
- 주차구획 = 폴리곤 + 목표 자세 + 허용 오차 + 래싱 포인트 4점. 래싱 격자에 맞춘 구획 자동생성과 적재 대수 KPI

## 실행

- 준비: Docker, JDK 25(`JAVA_HOME=/opt/homebrew/opt/openjdk`), Node 22 + pnpm, Unity 6.3(WebGL 빌드 1회: 메뉴 `ShipHdMap/Build WebGL`, HUD 에셋은 저장소에 포함)
- `./scripts/m3-dev.sh` → http://localhost:5173 (PostGIS 5433, API 8081)
- API 만: `./scripts/m2-smoke.sh`

## 상태

- M1 Unity 수직 슬라이스 완료 (EditMode 테스트 39)
- M2 데이터·API 완료 (Testcontainers 테스트, 스모크 스크립트, QGIS 절차 `docs/qgis-check.md`)
- M3a 웹 편집기 완료 (브라우저 편집 → DB 반영, Vitest 15)
- M3b Unity 씬 UX 완료 (궤도 카메라, 클릭 선택·드래그 이동 → DB, 차로·구획 라인, UI Toolkit HUD; EditMode 49, Vitest 19)
- 다음: M4 적재 계획
- 상세: `docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md`
