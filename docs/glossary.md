# 용어집

약어의 풀네임, 한글 표기, 읽는 법. 코드 식별자는 원문 그대로 둔다.

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

Unity 엔진 좌표는 다르다(x 전방, y 위, z 우현). 변환은 `ToUnity(x,y,z) → (x, z, -y)` 하나뿐이고, 그 덕에 **Unity yaw == ship heading** 이라 각도 변환이 없다.

혼동 주의: `Georef.headingDeg`(`ap_lat`/`ap_lon` 과 함께 쓰는 값)는 **다른 양** — 진북 기준 시계방향 선수 방위각이고 WGS84 변환에만 쓴다.

각도는 항상 `WrapDeg`/`WrapRad` 로 (-180, 180] 범위로 감는다.

## 2. 그리스 문자 — 각도 변수

| 기호 | 코드 변수 | 읽는 법 | 한글 | 뜻 |
|---|---|---|---|---|
| ψ | `psi`, `psiRad` | 프사이 (영어권은 "사이") | 선수각 / 방위각 | 차량이 향한 방향. +x 에서 CCW |
| θ | `theta`, `thetaRad` | 세타 | 상대 방위각 | 차량 정면 기준 마커가 보이는 각도 |
| α | `alpha`, `alphaRad` | 알파 | 법선 상대각 | 마커가 나를 향해 얼마나 비스듬한지 |
| φ | `phi`, `phiRad` | 파이 (정확히는 "피") | 법선 방위각 | 마커 법선의 절대 방향. 지도에 기록된 값 |
| σ | `sigma`, `sigmaR` | 시그마 | 표준편차 | 노이즈 크기 |

관계식 한 줄: **α = φ − ψ**. 그래서 마커 하나만 봐도 `ψ = φ − α` 로 방향을 알 수 있고, 이것이 `ClosedForm`(폐형해)이다.

`CCW` = **C**ounter-**C**lock**W**ise, 반시계방향. 수학 관례라 각도는 전부 이 방향으로 증가한다.

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

`Pose` 레코드는 부분 갱신이 되도록 전부 박싱 타입이다 (null = "안 바꿈").

트림·힐은 **배가 기울어도 Ship Frame 좌표는 변하지 않는다**. 기울기는 Unity 에서 맵 루트를 회전시키는 표시용이고, 마커·구획의 `x, y, z` 숫자는 그대로다.

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

`A1`/`A2`/`B2`/`C` 는 정밀도로지도(HD Map) 업계의 관행적 레이어 번호를 빌린 것. 숫자 자체보다 "A 계열은 차로, B 계열은 노면" 정도의 분류다.

**래싱(lashing)** = 배가 흔들려도 차가 굴러다니지 않게 결박하는 것. `LP` 는 갑판에 박힌 고리 소켓이고, 모양이 클로버 잎 같아 **클로버리프 소켓**이라 부른다. `lashing_pitch_m 0.75` 는 소켓이 0.75 m 격자로 깔려 있다는 뜻이고, 주차구획 크기가 이 격자의 배수로 올림(`ceilTo`)되는 이유다.

## 5. ID 접두사

| 예시 | 풀네임 | 뜻 |
|---|---|---|
| `LM-0042` | Landmark | 42 번 랜드마크 |
| `LP-1203` | Lashing Point | 1203 번 래싱 소켓 |
| `PS-D3-001` | **P**arking **S**lot | Deck 3 의 1 번 주차구획 |
| `A2-0007` | 레이어 코드 | 7 번 차로중심선 |
| `D3` | **D**eck 3 | 3 번 갑판 |
| `RAMP-STERN` | — | 선미 램프 |
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

**관측 한 번** = 마커 하나에 대한 세 값 (`Observation`): `r`(거리), `thetaRad`(상대 방위), `alphaRad`(법선 상대각).

**자세 변수 세 종류를 구분해야 한다.** 이 프로젝트에서 가장 헷갈리는 부분:

- `Truth` (`VehicleController.Truth`) — 차량의 실제 자세. 시뮬레이터만 안다
- `est` / `LocalizerResult.pose` — 관측으로 추정한 자세. 차량이 믿는 값
- `target_pose` — 구획이 요구하는 목표 자세

**노이즈** = 센서 관측에 일부러 더하는 가우시안 오차. 실제 태그 인식기가 완벽하지 않다는 걸 흉내 내는 시뮬레이션 장치다 (`LandmarkSensor.AddNoise`).

| 항목 | 의미 | 기본 σ |
|---|---|---|
| `sigma_r` | 마커까지 거리 오차 | 0.2 m |
| `sigma_theta` | 차량 기준 마커 방위각 오차 | 1° |
| `sigma_alpha` | 마커 법선 각도 오차 | 2° |
| `sigma_gps` | 부두 위 차량 GPS 위치 오차 | 0.5 m |

노이즈는 관측을 흐리는 동시에 추정기의 가중치를 정한다(`Weight = 1/σ²`). 그래서 노이즈 = 주차 오차의 원인이고, σ 를 0 으로 내리면 전부 `filled`, 올리면 `needs_adjust` 가 나오는 것이 데모의 볼거리다. 조작은 주행 탭 슬라이더 → `SetNoise` 브리지 메시지(각도는 와이어에서 도, 내부는 라디안). 테스트는 σ 전부 0 으로 두고 결정론을 검증한다.

**Gauss-Newton** — "가우스-뉴턴". 관측이 여러 개일 때 오차 제곱합을 최소화하는 자세를 반복해서 찾는 방법. `MaxIterations 5`, 변화량이 `StopDelta 1e-4` 밑이면 조기 종료.

**폐형해 (closed form)** — "클로즈드 폼". 반복 없이 공식 한 줄로 바로 나오는 해. 마커가 1 개일 때 쓴다.

**Jacobian (야코비안)** — 코드의 `jr`, `jt`, `ja`. "자세를 조금 움직이면 관측값이 얼마나 변하나"를 담은 미분 행렬. `j` + `r`/`t`/`a` = range/theta/alpha 각각에 대한 행.

**normal equations (정규방정식)** — `JᵀWJ · δ = JᵀW · e`. 풀면 자세 보정량 `δ`(델타)가 나온다. `W` 는 가중치 = `1/σ²`, 노이즈가 작은 관측일수록 더 믿는다.

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

`lat`/`lon` 이 **위도/경도가 아니다.** `Judge` 에서는 lateral(횡)/longitudinal(종)의 약자다. 같은 프로젝트에 `ap_lat`/`ap_lon`(진짜 위경도)도 있어 가장 헷갈리는 지점이다.

`utilization`(이용률) = 구획 넓이 합 ÷ 갑판 넓이. `lashing_coverage`(래싱 커버리지) = 네 모서리 모두 래싱점을 가진 구획의 비율.

주행 상태 기계 `Phase`: `Idle → OnLane → Parking → Departing`.

`FinalRunM = 5.0` 은 Unity `ScenarioPlanner` 와 API `SlotGenerator.FINAL_RUN_M` 이 **반드시 같아야 한다.** 45° 선회가 이웃 열을 긁지 않는 최소값이 4.75 m 라 여유가 거의 없다.

`ToTruthFrame(path, est, truth)` 는 믿음 좌표계 → 실제 좌표계 강체변환. 회전(`truth.psi − est.psi`)을 반드시 포함해야 `errHeading` 이 살아난다 — 평행이동만 하면 방향 오차가 구조적으로 0 이 된다.

## 8. 지도 데이터 (vehicle-map v1)

필드명이 곧 JSON 키라 이름 변경 금지 (`MapModel.cs`).

| 타입 | 주요 변수 |
|---|---|
| `Deck` | `z_surface`(갑판면 높이), `z_clear`(유효 높이), `movable`(가변 갑판), `outline`(윤곽 링) |
| `Landmark` | `marker{family, code}`, `position[x,y,z]`, `normal[nx,ny,nz]`, `size_m`, `mounted_on` |
| `Lane` | `centerline`, `width_m`, `direction`, `speed_limit_kmh`, `next[]` |
| `ParkingSlot` | `polygon`, `target_pose{x,y,heading_deg}`, `tolerance{lat_m,lon_m,heading_deg}`, `access_lane_id`, `lashing_points[]`, `sequence_no`, `status` |
| `Ramp` | `hinge`, `length_m`, `angle_range_deg`, `connects_lane`, `transition_landmarks` |

`status` 는 `empty | filled | needs_adjust` 세 값.

`sequence_no` 는 선적 순서이자 생성 순서(선수→선미, 좌현→우현)다. 단순 번호가 아니라 "주차 접근로에 이미 세워진 차가 없음"을 보장하는 기능적 제약이다.

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

접미사 규칙: **`_m` = 미터, `_deg` = 도, `Rad` = 라디안, `Mps` = m/s.** 이름에 단위가 없으면 SI 기본(m, 초)이다.

웹 상태(`store/editor.ts`)에서 도메인적으로 중요한 것:

- `features` vs `drafts` — 저장된 피처 vs Unity 가 방금 찍었지만 아직 DB 에 없는 초안
- `mode` — `"edit" | "drive"`. edit 로 가면 시나리오가 중단되고 배율이 1 로 리셋된다
- `deckFilter` — `"D3"` 또는 `"all"`
- `dataset.version` — 쓰기마다 서버가 올리는 낙관적 버전. 상태바가 보여준다
- `localization` — 마지막 `onLocalization` 이벤트 (`est_*`, `true_*`, `residual_rms`, `n_obs`)

## 10. 자주 틀리는 발음

- `quay` → **키** ("쿼이" 아님). 부두/안벽
- `psi` → **프사이** (그리스어 원음). 영어권은 "사이", 압력 단위 psi 와 구분은 문맥으로
- `phi` → **피** 또는 **파이**. 그리스 글자 φ
- `aft` → **애프트**
- `berth` → **버스** (정박지). `birth` 와 철자만 다름
- `hinge` → **힌지** (경첩). 램프가 회전하는 축
- `bow` → **바우** ("보우" 아님, 활이 아니라 뱃머리)
- `starboard` → **스타보드**
- `AprilTag` → 4 월과 무관. 만든 연구실(APRIL Lab) 이름

---

한 문장 요약: 모든 좌표는 Ship Frame `(x 전방, y 좌현, z 상방, ψ CCW)` 이고, 자세는 `truth / est / target` 셋으로 나뉘며, 노이즈가 `truth` 와 `est` 를 벌리고 그 차이가 `err_lat / err_lon / err_heading` 으로 나타난다.
