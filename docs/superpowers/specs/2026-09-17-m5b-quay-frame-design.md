# M5b 부두·프레임 전환 설계

> 기반 스펙: `2026-09-15-ship-hdmap-demo-design.md` (§3 좌표계, §4 pose·램프, §9.3 시나리오, §10 브리지, §13 M5b 행)
> 선행: `2026-09-16-m5a-drive-pose-design.md` (갑판 주행·주차 판정·pose 회전)

## 1. 범위

부두에서 출발한 차량이 GPS 로 램프 입구를 찾아 올라오고, 입구 랜드마크 쌍을 인식한 순간 Ship Frame 으로 전환해 갑판 구획에 주차한다. 하역은 그 역순이다. 함께 선체 메시를 `ShipParams` 가 아니라 vehicle-map 으로 짓게 바꿔, 선박 모델이 바뀌어도 지도만으로 굴러가게 만든다.

- **포함**: 부두 지오메트리(Unity 생성), GPS 노이즈와 램프 진입 오차, 입구 랜드마크 쌍 시드와 프레임 전환, 램프 위 차로 실시간 생성, pose 의 상하 이동(heave), `Load` 의 선체 재구성, `quay_z_m` 슬라이더
- **제외**: 부두를 지도 데이터로 저장하기, 방향 GPS(헤딩은 안다고 본다), 가동 갑판, 다중 램프
- **다음(M6)**: 문서·시연

## 2. z 취급

z 는 이 마일스톤에서 처음으로 pose 에 따라 움직인다. 기준을 한곳에 모아 둔다.

1. **월드(Quay Frame)의 z 원점은 수면이다.** 부두·배·램프의 높이가 모두 여기서 갈린다
2. **Map 루트**: 위치 `z = −draft_aft`, 회전은 M5a 의 `Quaternion.Euler(heel, 0, trim)`. 루트 원점이 AP × 기선 × 중심선이므로 선미 흘수가 곧 침수 깊이다. 자식의 로컬 좌표는 여전히 손대지 않는다 — 선내 좌표 불변이 이제 상하 이동까지 포함해 성립한다
3. **부두 슬래브**: 월드 `z = quay_z + tide`
4. **램프 힌지**: 월드 `z = hinge_z − draft_aft`. 힌지가 x = 0(AP)에 있고 `draft_aft` 도 AP 에서 잰 값이라 **트림과 무관하게** 이 값이다. 끝단은 `hinge_z − draft_aft + length·sin(angle)` 이고, 기존 각도 공식 `angle = asin(((quay_z + tide) − (hinge_z − draft_aft)) / length)` 을 대입하면 정확히 `quay_z + tide`, 즉 부두면과 같다. 항등식이므로 램프 끝은 어떤 pose 에서도 부두에 닿는다
5. **횡경사에서의 램프**: 힌지선이 `y = ±width/2` 로 뻗어 있어 heel 이 들어가면 좌우 끝이 `(width/2)·sin(heel)` 만큼 어긋난다(heel 3°, 폭 12 m 에서 ±0.31 m). 램프는 강체 평면으로 두고 이 비틀림을 그대로 보여준다. 주행 z 는 램프 중앙선 기준이며, 한쪽 끝이 부두면을 약간 파고드는 것은 알려진 단순화다
6. **차량 z**: 구간별로 확정한다 — 부두는 슬래브면, 램프는 힌지 z 에서 끝단 z 로 선형 보간, 갑판은 `deck.z_surface`. `VehicleController` 는 경로 점별 z 를 보간하고(지금은 경로당 z 하나), 피치는 현재 구간의 기울기에서 파생한다
7. **추정의 z**: `Localizer` 는 x·y·ψ 만 푼다. z 는 갑판 위면 `deck_id` 로, 램프 위면 램프 기하로 확정한다(기반 스펙 §3.4 의 "deck_id 조회" 에 램프를 추가한다)
8. **가림 판정은 3D**: 관측 자체는 수평 거리·방위지만 `Physics.Linecast` 는 3D 다. 부두(낮은 곳)에서 입구 쌍(높은 곳)을 볼 때 램프 상판이 시선을 막을 수 있다. 쌍은 램프 상판 위 1.2 m 의 힌지 기둥에 달고, 부두 접근 구간 전체에서 실제로 보이는지 브라우저에서 확인한다
9. **주차 판정은 z 를 보지 않는다**: 허용 오차는 lat·lon·heading 그대로다

## 3. 구성 요소

| 대상 | 변경 |
| --- | --- |
| `QuayBuilder`(신규, Unity) | pose 로 부두 슬래브 생성·갱신. 선미 쪽 60 × 30 m, 월드 z = `quay_z + tide`, 선측 끝은 **램프 발끝**(AP 가 아니라). 부두면이 힌지보다 높으면 램프는 배를 향해 내려가므로 AP 까지 이어진 슬래브는 램프 전체를 덮어버린다. 지도가 아니므로 레이어 트리·미니맵에는 없고 편집 모드에서도 보인다 |
| `MapRuntime` | 루트 위치 z 적용, 부두 생성 호출, 상태기계 확장, 프레임 전환, GPS |
| `ScenarioPlanner` | `QuayPath(spawn, est, ramp)`, `RampPath(ramp, angleDeg)`, `RampAngleFromPose` 검증 헬퍼 |
| `VehicleController` | 경로 점별 z 보간, 구간 기울기로 피치, `SetParent` 를 통한 프레임 이동 |
| `LandmarkSensor` | 변경 없음. 부두 구간에서는 차량 월드 자세를 Map 루트 역변환으로 Ship Frame 에 투영해 넘긴다 |
| `ShipMeshBuilder` | 입력을 `SeedData` 에서 `VehicleMap` 으로. 갑판 윤곽에서 L·B, `decks`·`facilities`·`ramps`·`lashing_points` 로 생성 |
| `FixtureExporter` | 입구 랜드마크 쌍 유도와 `transition_landmarks` 채우기, 전 갑판 기둥 내보내기 확인 |
| 웹 | pose 패널에 `quay_z_m` 슬라이더, 시나리오 로그에 `frame_switch` 줄 |

## 4. 시나리오 상태기계

`Idle → OnQuay → OnRamp → OnLane → Parking → Idle`, 하역은 `Departing → OnRamp → OnQuay → Idle`.

1. **시작**: 램프 `state` 가 `blocked` 면 시작을 거부하고 `onScenario{event:"finished", reason:"ramp_blocked"}`
2. **OnQuay**: 부두 스폰점(월드 `(−40, 8)`)에 차량을 놓고, GPS 추정 위치에서 램프 입구를 겨냥한 직선 경로를 만들어 개루프로 달린다. GPS = 참 위치 + 가우시안 σ_gps(위치만, 헤딩은 안다). **σ_gps 가 곧 램프 진입 횡오차가 된다**
3. **전환**: 입구 랜드마크 쌍이 동시에 보이는 첫 프레임에 `Localizer.Solve`(N=2 Gauss-Newton)로 Ship Frame 자세를 얻고, 차량을 `SetParent(Map, worldPositionStays: true)` 로 옮긴다. `onScenario{event:"frame_switch", detail:"est x y ψ · gps 오차 …"}`. 램프 끝까지 쌍을 못 보면 `finished{reason:"no_frame_switch"}`
4. **OnRamp**: 램프 차로는 저장하지 않고 힌지 중점 → 끝단 중점 2점으로 실시간 생성한다. 갑판 랜드마크가 들어오기 시작하면 M5a 의 0.2 s 주기 추정이 이어받는다
   - **전환 오차가 가는 곳**: 전환 시점의 추정 오차는 차량이 램프 상단에 얼마나 반듯하게 도착하는지로 나타난다. 갑판에 올라서면 차로를 따라가므로(차선 유지) 그 오차는 거기서 지워지고, 주차 오차는 M5a 그대로 차로 이탈 순간의 추정이 정한다. 전환의 값어치는 "제자리에서 일어나는가"와 "이후 수렴하는가"이지 주차 오차로 전파되는 것이 아니다
5. **OnLane 이후**: M5a 그대로 — 이탈 → 접근 → 판정 → `onSlotFilled` → 다음 차량
6. **하역**: `Departing` 이 차로 시작점에 닿으면 램프를 내려가고, 힌지를 지나면 Quay Frame 으로 되돌린 뒤 스폰점에서 사라진다

## 5. 브리지 계약 변경

| 방향 | 메시지 | 변경 |
| --- | --- | --- |
| R→U | `SetPose` | `tide_m`, `quay_z_m` 추가(부두 높이와 루트 z 에 필요) |
| U→R | `onScenario` | `event: "frame_switch"` 추가. `finished` 의 `reason` 에 `ramp_blocked`·`no_frame_switch` 추가 |

`SetNoise.sigma_gps` 는 이미 배선돼 있고 M5b 에서 처음 쓰인다.

## 6. 픽스처

- **입구 랜드마크 쌍**: 램프 힌지 양옆 `(0.3, ±5.5, hinge_z + 1.2)`, 법선 −x(선미를 본다), `mounted_on: "RAMP-STERN"`. `FixtureExporter.SeedLandmarks` 가 시드의 램프 기하에서 유도하고 `ramp.transition_landmarks` 에 두 id 를 넣는다(지금은 기둥 마커 `LM-0001/0002` 가 임시로 들어가 있다)
- **전 갑판 기둥**: 지도로 선체를 지으므로 `facilities` 에 D1·D2 기둥도 있어야 한다. 현재 픽스처는 18개뿐이라 갑판별로 확인하고 필요하면 내보내기를 고친다

## 7. 테스트

- **변환 항등식**: 임의의 pose 에서 램프 끝단 월드 z = `quay_z + tide`, 힌지 월드 z = `hinge_z − draft_aft`(트림을 넣어도 불변)
- **z 보간**: 램프 경로 중간점의 z 가 힌지·끝단의 선형 보간과 일치, 피치가 램프 각도와 일치
- **GPS → 진입 오차**: σ_gps 를 올리면 램프 진입 횡오차가 커진다(0 이면 0)
- **전환**: 노이즈가 있으면 전환 순간의 추정 오차가 0 이 아니고, 그만큼 램프 상단 도착 위치가 어긋난다(주차 오차로는 전파되지 않는다 — §4.4). 쌍이 없으면 `no_frame_switch` 로 끝난다
- **blocked**: 각도 범위 밖 pose 로 시작하면 거부된다
- **지도로 지은 선체**: vehicle-map 으로 지은 메시가 시드로 지은 것과 갑판 z·기둥 수·램프 힌지에서 일치한다
- **왕복**: 선적 후 하역하면 차량이 부두 스폰점으로 돌아오고 구획 status 가 empty 로 돌아간다

## 8. 브라우저 검증

1. 부두에 차량이 서고 램프가 부두면에 닿아 있다
2. 흘수·조위·`quay_z` 를 움직이면 배가 상하로 움직이고 램프 각도가 따라온다. 선내 좌표 표시는 불변
3. σ_gps 0 → 램프 중앙으로 진입. σ_gps 2 m → 눈에 띄게 치우쳐 진입
4. 전환 로그가 램프 위에서 뜨고, 추정 오차가 이후 줄어든다
5. 램프 위에서 차량이 기울어 올라간다
6. 갑판 도착 후 M5a 와 동일하게 주차되고 status 가 바뀐다
7. 하역이 램프를 내려가 부두에서 끝난다
8. `quay_z` 를 범위 밖으로 밀면 램프가 `blocked` 이고 시나리오가 시작되지 않는다
9. 횡경사 3°에서 램프 끝단이 비틀리는 것이 보인다(알려진 단순화)

## 9. 기반 스펙 수정 목록

1. §3.4: z 확정 규칙에 "램프 위면 램프 기하" 추가
2. §4.1: pose 가 Map 루트의 회전(M5a)과 **위치 z = −draft_aft**(M5b)로 반영된다고 명시
3. §4.2: 횡경사에서 램프 끝단이 비틀린다는 단순화 한 줄
4. §10: 브리지 표에 `SetPose` 의 `tide_m`·`quay_z_m`, `onScenario` 의 `frame_switch` 반영

## 10. 위험

- **가림**: 부두에서 입구 쌍이 램프 상판에 가려 전환이 안 될 수 있다. 쌍의 높이(힌지 + 1.2 m)와 부두 접근 각도로 조절하고 브라우저 검증 4에서 본다
- **선체 재구성 회귀**: 지도로 짓는 순간 M1 부터 쌓인 `ShipParams` 기반 테스트가 흔들린다. 시드로 지은 것과의 동치 테스트를 먼저 세우고 바꾼다
- **분량**: M5a(7 태스크)보다 크다. 계획에서 부두·전환·램프 / 선체 재구성 / 픽스처·UI 로 갈라 태스크를 잘게 나눈다
