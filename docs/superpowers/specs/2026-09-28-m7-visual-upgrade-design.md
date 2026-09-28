# M7 설계 스펙 — 보이는 것을 기능만큼 다듬는다 (2026-09-28)

기본 스펙 `2026-09-15-ship-hdmap-demo-design.md` 와 M6 다음 단계. 기능은 더하지 않는다. 용어는 [용어집](../../glossary.md) 참조.

---

## 1. 범위

### 1.1 목적

M1~M6 으로 편집·적재계획·주행·측위·커버리지·센서 경로는 닫혔지만, 화면은 M1 수직 슬라이스 때 모습 그대로다.

- Unity 는 Built-in 파이프라인에 기본 스카이박스를 쓰고, 안티앨리어싱이 꺼져 있다. 재질은 7개 파일 11곳에서 `Shader.Find` 로 만들고 RGB 를 직접 적는다. 차량은 기본 재질 큐브다.
- 웹은 `App.css` 한 파일에 헥스 리터럴을 쓰고, 토큰도 다크 모드도 없다.
- **같은 뜻을 두 화면이 다른 색으로 말한다.** 3D 슬롯 채움은 filled=파랑·empty=초록이고, 평면도 구획은 상태와 무관하게 파랑이다. 커버리지 blind/weak/ok 는 빨강→주황→초록이라 적록색약에게는 한 색으로 보인다.

목표는 시연 화면의 첫인상과 정보 가독성을 **함께** 올리는 것이다.

### 1.2 확정 결정

| 항목 | 결정 |
| --- | --- |
| 범위 | Unity 3D 장면 · Unity HUD · 웹 셸 · 평면도 |
| 렌더 파이프라인 | **URP 로 이전** |
| 외부 에셋 | 허용한다. 저장소 안에 라이선스를 명시한다 |
| 차량 | CC0 로우폴리 승용차 1종. 주행 차량과 주차된 차가 같은 모델을 쓴다 |
| HUD | 영문을 유지하고 디자인만 바꾼다(모노 폰트, 카드, 상태 색). **행 문자열은 바꾸지 않는다** |
| 웹 톤 | 다크를 기본으로 하고 라이트 토글을 둔다 |

### 1.3 제외

- **SSAO·물 셰이더·반사.** WebGL 프레임 예산에 비해 값이 작다. 수면은 평면 재질로 대신한다
- **아이콘 라이브러리, UI 프레임워크.** 새 런타임 의존성을 넣지 않는다
- **HUD 한글화.** 폰트 서브셋 비용이 크다. 문서와 캡처가 인용하는 영문 문자열이 계약이기도 하다
- **카메라 연출(컷신, 자동 궤도).** 기능이 아니라 연출이다. 필요해지면 따로 한다
- **라벨·버튼 문구 변경.** `scripts/capture.mjs` 가 버튼을 **문구로** 찾는다. 문구가 바뀌면 캡처가 깨진다

---

## 2. 원칙: 색은 한 곳에서

M6 의 교훈("값을 조작할 수 있게 만들면 그 값을 읽는 소비자를 전부 센다")을 색에 적용한다.

- **의미 색**(슬롯 상태 4, 커버리지 3 + 범위 밖, 차로, 랜드마크, 선택, 초안, 후보, 센서 콘, 믿음 상태)은 두 파일에만 둔다.
  - 웹: `web/src/theme/palette.ts`
  - Unity: `unity/Assets/ShipHdMap/Runtime/Core/Palette.cs` — 같은 이름에 같은 헥스 문자열
- **표면 색**(배경·패널·글자·테두리)은 `web/src/theme/tokens.css` 의 CSS 변수로 둔다. `:root` 가 다크, `[data-theme="light"]` 가 라이트다.
- **동기화 검사**: Vitest `palette-sync.test.ts` 가 `Palette.cs` 를 파일로 읽고, `public const string Name = "#rrggbb"` 를 뽑아 `palette.ts` 의 같은 이름 값과 대조한다. 대조할 이름은 두 파일의 교집합이 아니라 **웹 쪽 목록 전부**다. Unity 에 빠진 이름은 실패한다. **하한 단언**도 둔다. 정규식이 아무것도 못 찾으면 검사가 자동으로 통과해 장식품이 되기 때문이다.

---

## 3. 하위 마일스톤

### M7a — 웹 디자인 시스템

- `tokens.css` 에 표면 3단(bg / panel / raised), 테두리, 글자 3단, 강조색, ok / warn / danger, 포커스 링, 반경, 폰트를 둔다. 수치 판독은 `ui-monospace` 에 `tabular-nums` 를 쓴다.
- `App.css` 의 헥스 리터럴을 전부 변수로 바꾼다. 색을 담은 인라인 `style={{}}` 도 클래스로 옮긴다.
- 테마 토글은 `TopBar` 에 둔다. `<html data-theme>` 로 적용하고, `localStorage` 키 `shiphdmap.theme` 에 저장한다(try/catch). zustand persist 스키마는 건드리지 않는다.
- 믿음 상태와 램프 상태는 색 배지로 보여 준다. `beliefBadge` 의 색은 팔레트에서 가져온다.
- `.center` 배경을 Unity 카메라의 배경색과 맞춰, 로딩 중에 보이는 이음매를 없앤다.

### M7b — 평면도

- `PlanDock.tsx` · `geo/coverage.ts` · `geo/plan.ts` 의 색을 팔레트로 옮긴다. 테마가 바뀌면 SVG 도 따라 바뀐다(색은 CSS 변수를 통해 적용).
- 커버리지 색 단계: blind 와 ok 가 적록색약 시뮬레이션에서도 구별되게 한다. weak→ok 의 연속 램프는 유지한다(`stability` 0..2).
- 구획을 **상태별로** 칠한다. 색 이름은 Unity 슬롯 채움과 같다.
- 선수(+X) 방향 화살표와 축척 막대를 SVG 모서리에 둔다.

### M7c — URP 이전과 조명·재질

- `com.unity.render-pipelines.universal` 을 추가한다. URP Asset·Renderer·Volume Profile 은 에디터 스크립트로 만든다(`HudAssets.cs` 와 같은 방식). Graphics 설정과 모든 Quality 레벨에 지정한다.
- `Runtime/Core/Mats.cs` 에 `Lit(hex)` · `Unlit(hex)` · `UnlitTransparent(hex, a)` · `Textured(tex)` · `SetFade(mat, a)` 를 둔다. 11개 `Shader.Find` 자리를 전부 이것으로 바꾸고, 색은 전부 `Palette.cs` 에서 가져온다.
- `WebGLBuild.cs` 가 강제로 포함하는 셰이더 목록을 URP 셰이더로 바꾼다. 여기서 빠지면 **WebGL 에서만** 분홍색이 된다.
- 조명과 환경은 런타임 컴포넌트 `SceneLook` 이 만든다. 씬 파일을 손으로 고치지 않고 코드로 남긴다.
  - 태양: 소프트 그림자
  - 하늘: 절차적 스카이박스와 거리 포그
  - 수면: 평면 하나
  - 포스트: 톤매핑, 약한 블룸, 비네팅, 안티앨리어싱
- 재질: 갑판 강판, 선체 도장, 기둥, 래싱 소켓, 배관의 색과 smoothness 를 정리한다.
- **성능 예산**: 1440×900 브라우저에서 평균 50 fps 이상. 빌드 크기 증가량을 기록한다.

### M7d — 차량 모델과 HUD

- CC0 승용차 1종을 `Assets/ShipHdMap/Art/Vehicles/` 에 두고, `Art/LICENSES.md` 에 출처를 적는다.
  - 에디터 스크립트가 하위 메시를 **하나의 Mesh 로 굽는다**. 전방은 +X, 길이·높이·폭은 4.8 × 1.5 × 1.85 m, 원점은 바닥 중심이다.
  - 주행 차량 `Body` 와 `PARKED-*` 는 모두 **자기 GameObject 에 Renderer 하나**를 가진 채로 남는다. 테스트와 덱 필터가 그 구조를 가정한다.
- HUD 는 카드 하나 안에 행 단위 `Label` 을 둔다.
  - 폰트는 JetBrains Mono(OFL)를 쓰고 라이선스를 동봉한다.
  - 상태 색: 경고(`holding previous`, 클릭 실패 플래시)는 warn, 센서 줄은 강조색, 커서·도구 줄은 흐리게.
  - `HudView.Lines` / `StatusLine` / `SensorLine` / `SensorConfigLine` 의 출력 문자열은 바꾸지 않는다.

### M7e — 캡처 재생산과 문서

- `scripts/capture.sh` 로 `docs/img/` 를 다시 만든다. HUD 크기가 바뀌었으면 `capture.mjs` 의 HUD clip 을 맞춘다.
- README 대표 이미지를 갱신한다.

---

## 4. 완료 기준

1. `palette-sync` 테스트가 통과하고, `Palette.cs` 의 값 하나를 바꾸면 실패한다(사보타주로 확인).
2. 웹의 다크와 라이트 두 테마 모두에서 본문 글자 대비가 WCAG AA(4.5:1) 이상이다.
3. 슬롯 하나의 상태(empty / filled / needs_adjust / unreachable)가 3D 와 평면도에서 **같은 색 이름**으로 칠해진다.
4. WebGL 빌드에 분홍(누락 셰이더) 재질이 없다. 갑판 페이드, 슬롯 선택 강조, 랜드마크 선택·관측 링, 센서 콘, 법선 링이 모두 보인다.
5. 선적 한 사이클(부두 → 램프 → 차로 → 주차)이 M6 과 같은 결과로 끝난다(`capture.sh` 요약표 대조).
6. 1440×900 브라우저에서 평균 fps 가 50 이상이다.
7. Unity EditMode, Vitest, `pnpm build`, oxlint 가 모두 통과한다.
