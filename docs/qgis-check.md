# QGIS 검증 절차

- 목적: API 가 내보낸 WGS84 GeoJSON 이 갑판 윤곽 안에 기둥·랜드마크·차로·구획을 올바른 상대 위치로 놓는지 눈으로 확인
- 준비: `./scripts/m2-smoke.sh` 실행 → `api/build/export.geojson` 생성. QGIS 3.x 설치(https://qgis.org)
- 열기: QGIS → Layer → Add Layer → Add Vector Layer → `api/build/export.geojson`. CRS 는 EPSG:4326 로 인식됨
- 확인 1: 속성 테이블(F6)에서 `layer` 값이 DECK, C, LM, LP, A2, B2 로 나뉘는지
- 확인 2: `layer = 'DECK'` 필터 → 사각형 3개가 겹쳐 보이는지(길이 120 m, 폭 24 m). 축척 막대로 길이 확인
- 확인 3: `layer = 'LM'` → 점 19개가 갑판 안쪽, 기둥(`kind = 'pillar'`) 옆에 붙어 있는지. 선체 벽 마커 1개는 긴 변 위
- 확인 4: `layer = 'A2'` → 갑판 중앙을 지나는 선 3개. `layer = 'B2'` → 구획 사각형 2개가 선수 쪽(x 95–105 m 부근)
- 확인 5: pose 를 바꾸면(`PUT /api/datasets/{id}/pose` 의 heading_deg) 다시 내보낸 파일에서 전체가 회전하는지 — 선내 상대 위치는 그대로
- 정박 좌표(ap_lat, ap_lon)는 임의값이다. 실제 항구 위에 올려 보지 않는다
- GeoJSON 좌표의 세 번째 값(z)은 타원체고가 아니라 Ship Frame z(기준선 위 높이, m)다
