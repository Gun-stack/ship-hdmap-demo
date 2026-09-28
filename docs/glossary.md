# 용어집

약어의 풀네임, 한글 표기, 읽는 법을 정리합니다. 코드 식별자는 원문 그대로 둡니다.

## 1. 좌표계·선박 기본

| 약어 | 풀네임 | 한글 | 읽는 법 | 뜻 |
|---|---|---|---|---|
| `AP` | **A**ft **P**erpendicular | 선미수선 | 에이피 | 좌표 원점의 세로 기준선. 보통 타축(rudder stock) 중심을 지나는 수직선 |
| — | Baseline | 기선 | 베이스라인 | `z = 0` 기준면. 몰디드 킬(용골) 상면 |
| — | Centerline | 중심선 | 센터라인 | `y = 0`, 배를 좌우로 가르는 면 |
| `lpp_m` | **L**ength **b**etween **P**erpendiculars | 수선간장 | 엘피피 | AP 와 FP(선수수선) 사이 거리. trim 계산의 분모 |
| `port` | port side | 좌현 | 포트 | 선수를 향해 섰을 때 왼쪽. `y` 의 + 방향 |
| `starboard` | starboard side | 우현 | 스타보드 | 오른쪽. `y` 음수 |
| `bow` | bow | 선수(이물) | 바우 | 배 앞. `x` 최대 |
| `stern` / `aft` | stern | 선미(고물) | 스턴 / 애프트 | 배 뒤. `x = 0` 부근 |
| `beamM` | beam | 선폭 | 빔 | 배의 폭 (24 m) |
| `RoRo` | **Ro**ll-on/**Ro**ll-off | 로로선 | 로로 | 차가 스스로 굴러 타고 내리는 배. 데이터셋 id `roro-demo-01` |

Ship Frame 축 정의 (`ShipFrame.cs`, `ShipFrame.java`):

| 변수 | 의미 |
|---|---|
| `x` | 선미(AP)부터 선수 방향, m |
| `y` | 좌현(port) + 방향, m |
| `z` | 기선(baseline)부터 위, m |
| `psi` / `heading_deg` | +x 축에서 반시계(CCW). 라디안은 `psiRad`, 와이어·DB 는 도(°) |

Unity 엔진 좌표는 다릅니다(x 전방, y 위, z 우현). 변환은 `ToUnity(x,y,z) → (x, z, -y)` 하나뿐이고, 그 덕에 **Unity yaw == ship heading**이므로 각도 변환이 없습니다.

혼동에 주의해야 합니다. `Georef.headingDeg`(`ap_lat`/`ap_lon`과 함께 쓰는 값)는 **다른 양**이며, 진북 기준 시계방향 선수 방위각으로 WGS84 변환에만 사용합니다.

각도는 항상 `WrapDeg`/`WrapRad`로 (-180, 180] 범위로 감습니다.

## 2. 그리스 문자 — 각도 변수

| 기호 | 코드 변수 | 읽는 법 | 한글 | 뜻 |
|---|---|---|---|---|
| ψ | `psi`, `psiRad` | 프사이 (영어권은 "사이") | 선수각 / 방위각 | 차량이 향한 방향. +x 에서 CCW |
| θ | `theta`, `thetaRad` | 세타 | 상대 방위각 | 차량 정면 기준 마커가 보이는 각도 |
| α | `alpha`, `alphaRad` | 알파 | 법선 상대각 | 마커가 나를 향해 얼마나 비스듬한지 |
| φ | `phi`, `phiRad` | 파이 (정확히는 "피") | 법선 방위각 | 마커 법선의 절대 방향. 지도에 기록된 값 |
| σ | `sigma`, `sigmaR` | 시그마 | 표준편차 | 노이즈 크기 |

관계식은 **α = φ − ψ**입니다. 따라서 마커 하나만 봐도 `ψ = φ − α`로 방향을 알 수 있고, 이것이 `ClosedForm`(폐형해)입니다.

`CCW`는 **C**ounter-**C**lock**W**ise, 즉 반시계방향입니다. 수학 관례에 따라 각도는 모두 이 방향으로 증가합니다.

## 3. 선박 자세

| 약어 | 풀네임 | 한글 | 읽는 법 | 뜻 |
|---|---|---|---|---|
| `pose` | pose | 자세 | 포즈 | 위치 + 방향. 여기선 `(x, y, ψ)` 3 개 |
| `Pose2D` | 2-dimensional pose | 2 차원 자세 | 포즈 투디 | 높이는 갑판이 정하니 평면만 |
| `draft_fwd_m` | **d**raft **f**or**w**ar**d** | 선수 흘수 | 드래프트 포워드 | 선수 쪽이 물에 잠긴 깊이 |
| `draft_aft_m` | draft aft | 선미 흘수 | 드래프트 애프트 | 선미 쪽 잠긴 깊이 |
| `trim_deg` | trim | 트림(종경사) | 트림 | 앞뒤 기울기. `atan((aft − fwd) / lpp)` 로 계산되는 파생값 |
| `heel_deg` | heel | 힐(횡경사) | 힐 | 좌우 기울기 |
| `tide_m` | tide | 조위 | 타이드 | 조수 높이 |
| `quay_z_m` | quay | 안벽(부두) 높이 | 키 (발음 주의 — "쿼이" 아님) | 부두 상면 높이 |
| `pitchRad` | pitch | 피치(경사각) | 피치 | 경로 기울기. 램프를 오를 때의 차량 종경사 |

`Pose` 레코드는 부분 갱신이 가능하도록 모두 박싱 타입입니다(null = "안 바꿈").

트림·힐에서는 **배가 기울어도 Ship Frame 좌표가 변하지 않습니다.** 기울기는 Unity에서 맵 루트를 회전시키는 표시용이고, 마커·구획의 `x, y, z` 숫자는 그대로입니다.

## 4. 레이어 코드

| 코드 | 풀네임 | 한글 | 읽는 법 |
|---|---|---|---|
| `A1` | — | 차선 | 에이원 |
| `A2` | — | 차로중심선 | 에이투 |
| `B2` | — | 노면표시 (주차구획) | 비투 |
| `C` | — | 규제/시설 | 씨 |
| `LM` | **L**and**m**ark | 랜드마크 | 엘엠 |
| `LP` | **L**ashing **P**oint | 래싱 포인트 | 엘피 |
| `MEP` | **M**echanical, **E**lectrical, **P**lumbing | 설비(배관·전기·덕트) | 엠이피 |

`A1`/`A2`/`B2`/`C`는 정밀도로지도(HD Map) 업계의 관행적 레이어 번호를 빌린 것입니다. 숫자 자체보다 "A 계열은 차로, B 계열은 노면" 정도의 분류입니다.

**래싱(lashing)**은 배가 흔들려도 차가 굴러다니지 않도록 결박하는 것을 뜻합니다. `LP`는 갑판에 박힌 고리 소켓이고, 모양이 클로버 잎과 같아 **클로버리프 소켓**이라고 부릅니다. `lashing_pitch_m 0.75`는 소켓이 0.75 m 격자로 깔려 있다는 뜻이며, 주차구획 크기가 이 격자의 배수로 올림(`ceilTo`)되는 이유입니다.

## 5. ID 접두사

| 예시 | 풀네임 | 뜻 |
|---|---|---|
| `LM-0042` | Landmark | 42 번 랜드마크 |
| `LP-1203` | Lashing Point | 1203 번 래싱 소켓 |
| `PS-D3-001` | **P**arking **S**lot | Deck 3 의 1 번 주차구획 |
| `A2-0007` | 레이어 코드 | 7 번 차로중심선 |
| `D3` | **D**eck 3 | 3 번 갑판 |
| `RAMP-STERN` | — | 선미 램프 |
| `RAMP-D3-D2` | — | D3 와 D2 를 잇는 내부 램프 (선미 램프 갑판에 가까운 쪽 먼저, 먼 쪽 나중) |
| `ROUTE-D1` | — | D3 입구에서 D1 주 차로 시작점까지 가는 경로 |
| `PARKED-*` | — | 이미 주차된 차를 나타내는 박스 |

## 6. 센서·위치 추정

| 약어 | 풀네임 | 한글 | 읽는 법 | 뜻 |
|---|---|---|---|---|
| `r` | **r**ange | 거리 | 알 / 레인지 | 마커까지 직선거리 |
| `obs` / `nObs` | **obs**ervation | 관측 / 관측 수 | 옵스 | 본 마커 하나 = 관측 하나 |
| `est` | **est**imate | 추정치 | 에스티메이트 | 차량이 믿는 자세 |
| `truth` | ground truth | 참값 | 트루스 | 시뮬레이터만 아는 실제 자세 |
| `RMS` | **R**oot **M**ean **S**quare | 제곱평균제곱근 | 알엠에스 | 잔차를 제곱→평균→루트. 오차 크기 한 숫자 요약 |
| `residual` | residual | 잔차 | 레지듀얼 | 예측 관측값과 실제 관측값의 차 |
| `FOV` | **F**ield **O**f **V**iew | 시야각 | 에프오브이 / 포브 | 카메라가 보는 좌우 각도 (90°) |
| `GPS` | **G**lobal **P**ositioning **S**ystem | 위성항법 | 지피에스 | 부두에서만 쓴다. 배 안은 전파가 안 들어와 마커를 쓰는 것 |
| `AprilTag` | — | 에이프릴태그 | 에이프릴태그 | 정사각 인식 마커 규격. `36h11` 은 36 비트 / 최소 해밍거리 11 인 코드군 |
| `seed` | seed | 시드 | 시드 | 난수 씨앗. 같은 시드 = 같은 노이즈 = 재현 가능 |

**관측 한 번**은 마커 하나에 대한 세 값(`Observation`), 즉 `r`(거리), `thetaRad`(상대 방위), `alphaRad`(법선 상대각)로 구성됩니다.

**자세 변수 세 종류를 구분해야 합니다.** 이 프로젝트에서 가장 혼동하기 쉬운 부분입니다.

- `Truth` (`VehicleController.Truth`) — 차량의 실제 자세. 시뮬레이터만 알고 있습니다.
- `est` / `LocalizerResult.pose` — 관측으로 추정한 자세. 차량이 믿는 값
- `target_pose` — 구획이 요구하는 목표 자세

**노이즈**는 센서 관측에 의도적으로 더하는 가우시안 오차입니다. 실제 태그 인식기가 완벽하지 않다는 점을 모사하는 시뮬레이션 장치입니다(`LandmarkSensor.AddNoise`).

| 항목 | 의미 | 기본 σ |
|---|---|---|
| `sigma_r` | 마커까지 거리 오차 | 0.2 m |
| `sigma_theta` | 차량 기준 마커 방위각 오차 | 1° |
| `sigma_alpha` | 마커 법선 각도 오차 | 2° |
| `sigma_gps` | 부두 위 차량 GPS 위치 오차 | 0.5 m |

노이즈는 관측을 흐리는 동시에 추정기의 가중치를 정합니다(`Weight = 1/σ²`). 따라서 노이즈는 주차 오차의 원인이고, σ를 0으로 내리면 모두 `filled`, 올리면 `needs_adjust`가 나오는 것이 데모의 주요 관찰 지점입니다. 주행 탭 슬라이더에서 조작하면 `SetNoise` 브리지 메시지로 전달됩니다(각도는 와이어에서 도, 내부에서는 라디안). 테스트에서는 σ를 모두 0으로 두고 결정론을 검증합니다.

**Gauss-Newton** — "가우스-뉴턴". 관측이 여러 개일 때 오차 제곱합을 최소화하는 자세를 반복해서 찾는 방법입니다. `MaxIterations 5`이며, 변화량이 `StopDelta 1e-4`보다 작으면 조기 종료합니다.

**폐형해(closed form)** — "클로즈드 폼". 반복 없이 공식 한 줄로 바로 나오는 해입니다. 마커가 1개일 때 사용합니다.

**Jacobian(야코비안)** — 코드의 `jr`, `jt`, `ja`입니다. "자세를 조금 움직이면 관측값이 얼마나 변하나"를 담은 미분 행렬입니다. `j` + `r`/`t`/`a`는 range/theta/alpha 각각에 대한 행을 뜻합니다.

**normal equations(정규방정식)** — `JᵀWJ · δ = JᵀW · e`입니다. 이 식을 풀면 자세 보정량 `δ`(델타)가 나옵니다. `W`는 가중치 `1/σ²`이며, 노이즈가 작은 관측일수록 더 신뢰합니다.

## 7. 기하·경로

| 약어 | 풀네임 | 한글 | 뜻 |
|---|---|---|---|
| `s` | arc length | 호 길이 | 경로 시작점부터 잰 주행 거리. 수평 투영 기준이라 경사가 속도를 늦추지 않는다 |
| `_exitS` | exit `s` | 이탈 지점 | 차로를 벗어나는 `s` 값 |
| `bbox` | **b**ounding **box** | 경계 상자 | 도형을 감싸는 최소 직사각형 |
| `ring` | ring | 링 | 첫 점 = 마지막 점인 닫힌 폴리곤 |
| `shoelace` | shoelace formula | 신발끈 공식 | 좌표만으로 다각형 넓이를 구하는 법 |
| `centerline` | centerline | 중심선 | 차로 한가운데 선 |
| `polyline` | polyline | 폴리라인 | 점들을 이은 꺾은선 |
| `FinalRunM` | final run | 최종 직진 구간 | 구획 직전 5 m 직진 |
| `pivot` | pivot | 선회점 | 45° 꺾는 지점 |
| `errLat` | error **lat**eral | 횡방향 오차 | 좌우로 얼마나 벗어났나 |
| `errLon` | error **lon**gitudinal | 종방향 오차 | 앞뒤로 얼마나 벗어났나 |

`lat`/`lon`은 **위도/경도가 아닙니다.** `Judge`에서는 lateral(횡)/longitudinal(종)의 약자입니다. 같은 프로젝트에 `ap_lat`/`ap_lon`(실제 위경도)도 있어 가장 혼동하기 쉬운 지점입니다.

`utilization`(이용률)은 구획 넓이 합 ÷ 갑판 넓이입니다. `lashing_coverage`(래싱 커버리지)는 네 모서리에 모두 래싱점을 가진 구획의 비율입니다.

주행 상태 기계 `Phase`는 `Idle → OnLane → Parking → Departing` 순서입니다. M8 부터 다른 갑판으로 가는 차는 `OnLane` 앞에 `OnRoute`(경로 추종)를, 하역 때는 `Departing` 뒤에 `ReturnRoute`(경로 역순)를 거칩니다.

**내부 램프(internal ramp)**는 갑판과 갑판을 잇는 호이스트형 램프입니다(`type` `internal_hoistable`). 힌지는 항상 위층 끝에 있습니다. **전개(deployed)**하면 아래층으로 경사지고, **수납(stowed)**하면 위층 바닥과 같은 높이의 덮개가 됩니다. 선미 램프(`stern_quarter`)는 D3 에 닿고, 내부 램프는 좌현으로 D3→D2→D1, 우현으로 D3→D4→D5 를 잇습니다.

**가까운 쪽 / 먼 쪽**은 선미 램프 갑판(D3)을 기준으로 정합니다. 램프의 먼 쪽 발자국(내려가는 램프의 착지 자리, 올라가는 램프가 들어가는 개구)은 항상 주차할 수 없습니다. 가까운 쪽 발자국은 램프를 수납하면 덮개 위에 추가 구획이 생깁니다.

**경로(route)**는 D3 입구에서 목표 갑판 주 차로 시작점(x 8)까지 이어지는 3D 폴리라인입니다. 차로처럼 정확히 추종하므로, 믿음 판정은 경로 위가 아니라 `OnLane`·`Parking`·`Departing` 에서만 합니다.

**갑판 순위(`rank`)**는 선미 램프 갑판에서 먼 순서이고, 같은 거리면 아래층이 먼저입니다. 결과는 D1 → D5 → D2 → D4 → D3 이며, 먼 갑판부터 싣고 하역은 역순입니다.

`FinalRunM = 5.0`은 Unity `ScenarioPlanner`와 API `SlotGenerator.FINAL_RUN_M`에서 **반드시 같아야 합니다.** 45° 선회가 이웃 열을 침범하지 않는 최소값이 4.75 m라 여유가 거의 없습니다.

`ToTruthFrame(path, est, truth)`는 믿음 좌표계에서 실제 좌표계로의 강체변환입니다. 회전(`truth.psi − est.psi`)을 반드시 포함해야 `errHeading`이 유지됩니다. 평행이동만 하면 방향 오차가 구조적으로 0이 됩니다.

## 8. 지도 데이터 (vehicle-map v1)

필드명이 곧 JSON 키이므로 이름을 변경하면 안 됩니다(`MapModel.cs`).

| 타입 | 주요 변수 |
|---|---|
| `Deck` | `z_surface`(갑판면 높이), `z_clear`(유효 높이), `movable`(가변 갑판), `outline`(윤곽 링) |
| `Landmark` | `marker{family, code}`, `position[x,y,z]`, `normal[nx,ny,nz]`, `size_m`, `mounted_on` |
| `Lane` | `centerline`, `width_m`, `direction`, `speed_limit_kmh`, `next[]` |
| `ParkingSlot` | `polygon`, `target_pose{x,y,heading_deg}`, `tolerance{lat_m,lon_m,heading_deg}`, `access_lane_id`, `lashing_points[]`, `sequence_no`, `status` |
| `Ramp` | `type`(`stern_quarter` \| `internal_hoistable`), `hinge`, `length_m`, `angle_range_deg`, `connects_lane`, `transition_landmarks`, 내부 램프만 `lower_deck`·`upper_deck`·`toe`(아랫끝 두 점) |
| `Route` | `id`, `deck_id`(목표 갑판), `ramps[]`(지나는 램프 순서), `path[][]`(3D 폴리라인) |

`status`는 `empty | filled | needs_adjust | unreachable` 네 값을 사용합니다. `unreachable`은 믿음이 멈춰 주차하지 못하고 돌아 나온 구획입니다.

`sequence_no`는 선적 순서이자 생성 순서(선수→선미, 좌현→우현)입니다. 단순 번호가 아니라 "주차 접근로에 이미 세워진 차가 없음"을 보장하는 기능적 제약입니다. M8 부터는 전역 번호 `rank × 1000 + 갑판 안 순번`이라, 번호 순서가 곧 먼 갑판부터의 적재 순서입니다.

## 9. 시스템·브리지

| 약어 | 풀네임 | 뜻 |
|---|---|---|
| `R→U` / `U→R` | React → Unity | 브리지 메시지 방향 |
| `Emit` | emit | Unity 가 React 로 이벤트를 쏘는 것 |
| `tempId` | temporary id | DB 저장 전 임시 식별자. `Confirm({tempId, id})` 로 실제 id 와 교환 |
| `KPI` | **K**ey **P**erformance **I**ndicator | 핵심 지표 (구획 수, 이용률) |
| `HUD` | **H**eads-**U**p **D**isplay | 화면 겹침 정보 표시 |
| `WKT` | **W**ell-**K**nown **T**ext | `POLYGON((...))` 형태의 표준 도형 문자열. PostGIS 저장 형식 |
| `GeoJSON` | — | 지도 데이터 표준 JSON |
| `WGS84` | **W**orld **G**eodetic **S**ystem 1984 | GPS 가 쓰는 전 지구 좌표계. 위경도 |
| `CRUD` | Create/Read/Update/Delete | 기본 4 연산 |
| `dt` | delta time | 프레임 간 경과 시간(초) |
| `_prev` | previous | 직전 추정치 |
| `mps` | **m**etres **p**er **s**econd | m/s |
| `kmh` | km/h | 시속 |
| `deg` / `rad` | degree / radian | 도 / 라디안 |
| `frame_switch` | frame switch | 프레임 전환. 부두(GPS) 좌표계에서 Ship Frame 으로 갈아타는 시나리오 이벤트 |
| `route` / `lane` / `ramp` | — | M8 시나리오 이벤트. 경로 출발 / 목표 갑판 차로 진입 / 내부 램프 전개·수납 |

접미사 규칙은 **`_m` = 미터, `_deg` = 도, `Rad` = 라디안, `Mps` = m/s**입니다. 이름에 단위가 없으면 SI 기본 단위(m, 초)를 사용합니다.

웹 상태(`store/editor.ts`)에서 도메인적으로 중요한 항목은 다음과 같습니다.

- `features` vs `drafts` — 저장된 피처 vs Unity 가 방금 찍었지만 아직 DB 에 없는 초안
- `mode` — `"edit" | "drive"`. edit로 전환하면 시나리오가 중단되고 배율이 1로 초기화됩니다.
- `deckFilter` — 갑판 id(`"D1"`~`"D5"`) 또는 `"all"`. 선택 갑판보다 위층은 3D 에서 숨깁니다.
- `rampStates` — 내부 램프별 `deployed | stowed`. 주행 중에는 `ramp` 이벤트로 갱신됩니다.
- `dataset.version` — 쓰기마다 서버가 올리는 낙관적 버전. 상태바에 표시됩니다.
- `localization` — 마지막 `onLocalization` 이벤트 (`est_*`, `true_*`, `residual_rms`, `n_obs`)

## 10. 자주 틀리는 발음

- `quay` → **키** ("쿼이" 아님). 부두/안벽입니다.
- `psi` → **프사이** (그리스어 원음). 영어권은 "사이", 압력 단위 psi 와 구분은 문맥으로
- `phi` → **피** 또는 **파이**. 그리스 글자 φ
- `aft` → **애프트**
- `berth` → **버스** (정박지). `birth` 와 철자만 다름
- `hinge` → **힌지** (경첩). 램프가 회전하는 축
- `bow` → **바우** ("보우" 아님, 활이 아니라 뱃머리)
- `starboard` → **스타보드**
- `AprilTag` → 4월과 무관하며, 만든 연구실(APRIL Lab)의 이름에서 유래했습니다.

---

한 문장으로 요약하면, 모든 좌표는 Ship Frame `(x 전방, y 좌현, z 상방, ψ CCW)`을 사용하고 자세는 `truth / est / target` 셋으로 나뉩니다. 노이즈가 `truth`와 `est`의 차이를 만들고, 그 차이는 `err_lat / err_lon / err_heading`으로 나타납니다.
