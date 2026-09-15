# M1 Unity 수직 슬라이스 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unity 에디터 플레이 모드에서 코드 생성 RORO 갑판 위에 AprilTag 랜드마크를 배치하고, 가상 차량이 차로를 따라 주행하며 랜드마크 관측만으로 자기 위치를 추정해 HUD 에 오차를 보이는 최소 동작본을 만든다.

**Architecture:** 순수 C# 코어(ShipFrame, MapModel, ShipGenerator 시드, Localizer)는 MonoBehaviour 없이 EditMode 테스트로 검증한다. 씬 쪽(메시 생성, 감지 모델, 차량, HUD, 배치)은 얇은 MonoBehaviour 로 코어를 호출한다. React 는 아직 없으므로 EditorWindow 스텁이 브리지 메시지를 대신 보내고 받는다.

**Tech Stack:** Unity 6000.3.24f1, C# 9, Unity Test Framework 1.6.0 (EditMode), Newtonsoft.Json 3.2.2 (`com.unity.nuget.newtonsoft-json`, 이미 lock 에 있음), Built-in Render Pipeline

**Spec:** `docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md`

## Global Constraints

- Ship Frame: 원점 AP×기선×중심선, X 선수(+), **Y 좌현(+)**, Z 상방(+), 헤딩 ψ 는 X축 기준 반시계(+Y 쪽) 양수, 단위 m / 도 (스펙 3.1)
- Unity 매핑: Ship(x, y, z) → Unity(x, z, −y) (스펙 3.3)
- 차량은 2.5D: 추정은 (x, y, ψ), z 는 deck 조회 (스펙 3.4)
- 감지 모델: FOV 90°, 거리 ≤ 25 m, normal 과 시선 각 ≤ 70°, 레이캐스트 차폐 없음. 노이즈 σ_r 0.2 m, σ_θ 1°, σ_α 2° (스펙 9.4)
- 위치 추정: N=1 폐형해, N≥2 Gauss-Newton 최대 5회, |δ| < 1e-4 종료, N=0 직전 추정 유지 (스펙 9.4)
- 마커: apriltag-36h11, 코드표 20개, 시각용 프로시저럴 텍스처, 디코딩 안 함 (스펙 9.4)
- 생성기 기본값: 래싱 간격 0.75 m, 승용 4.8×1.85 m (스펙 5.3)
- 저장소는 public. 기관·회사·과제명·이력 표현, 사용자 계정명이 든 절대경로를 코드·주석·커밋 메시지에 쓰지 않는다
- 개발은 에디터 플레이 모드, WebGL 빌드는 M1 에서 하지 않는다 (스펙 2)
- 커밋은 각 Task 끝에 한다. 푸시는 사용자 요청 시에만
- Unity CLI 로 테스트를 돌릴 때는 에디터 GUI 를 먼저 닫아야 한다(프로젝트 락). 에디터가 열려 있으면 MCP `run_tests` 도구를 대신 쓴다

---

## 파일 구조

```
unity/Assets/ShipHdMap/
  Runtime/
    ShipHdMap.Runtime.asmdef
    Core/ShipFrame.cs              Ship↔Unity 좌표·헤딩 변환, 각도 wrap
    Core/MapModel.cs               vehicle-map v1 과 시드 JSON 의 DTO (Newtonsoft)
    Ship/ShipParams.cs             생성기 파라미터와 기본값
    Ship/ShipSeedBuilder.cs        파라미터 → SeedData (순수 C#, 갑판 윤곽·기둥·램프·래싱)
    Ship/ShipMeshBuilder.cs        SeedData → GameObject 계층 (갑판 바닥·기둥·램프·MEP·래싱 소켓)
    Localization/Observation.cs    관측 구조체 (id, r, theta, alpha)
    Localization/Localizer.cs      폐형해 + Gauss-Newton
    Localization/LandmarkSensor.cs 감지 모델 MonoBehaviour (FOV·거리·시야각·차폐·노이즈)
    Landmarks/AprilTag36h11.cs     코드표 20개 + 텍스처 생성
    Landmarks/Landmark.cs          씬 랜드마크 MonoBehaviour (id, code, normal, size)
    Landmarks/LandmarkPlacer.cs    플레이 모드 마우스 RaycastHit 배치·삭제
    Vehicle/VehicleController.cs   차로 폴리라인 추종, 실제 자세 제공
    Vehicle/LocalizationHud.cs     OnGUI HUD
    Bridge/BridgeMessages.cs       메시지 이름·페이로드 DTO
    Bridge/MapRuntime.cs           Load/SetMode/SetNoise/StartScenario 수신, 이벤트 송신 (GameObject "Map")
  Editor/
    ShipHdMap.Editor.asmdef
    BridgeStubWindow.cs            React 대신 메시지를 보내고 이벤트를 보여주는 창
    FixtureExporter.cs             생성기 + 씬 랜드마크 → docs/fixtures/vehicle-map.sample.json
  Tests/EditMode/
    ShipHdMap.Tests.EditMode.asmdef
    ShipFrameTests.cs
    MapModelTests.cs
    LocalizerTests.cs
    ShipSeedBuilderTests.cs
    ShipMeshBuilderTests.cs
    AprilTagTests.cs
unity/Assets/Scenes/Demo.unity
docs/test-vectors/ship-frame.json    C#·TS·Java 공용 변환 테스트 벡터
docs/fixtures/vehicle-map.sample.json
```

---

### Task 1: 어셈블리·테스트 골격

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/ShipHdMap.Runtime.asmdef`
- Create: `unity/Assets/ShipHdMap/Editor/ShipHdMap.Editor.asmdef`
- Create: `unity/Assets/ShipHdMap/Tests/EditMode/ShipHdMap.Tests.EditMode.asmdef`
- Create: `unity/Assets/ShipHdMap/Tests/EditMode/SmokeTests.cs`
- Modify: `unity/Packages/manifest.json` (test-framework, newtonsoft 명시)

**Interfaces:**
- Produces: 어셈블리 이름 `ShipHdMap.Runtime`, `ShipHdMap.Editor`, `ShipHdMap.Tests.EditMode`. 이후 모든 Task 는 이 안에 파일을 만든다

- [ ] **Step 1: manifest 에 패키지 명시**

`unity/Packages/manifest.json` 의 `dependencies` 맨 위에 두 줄 추가 (이미 lock 에 있는 버전과 같게):

```json
    "com.unity.nuget.newtonsoft-json": "3.2.2",
    "com.unity.test-framework": "1.6.0",
```

- [ ] **Step 2: Runtime asmdef**

```json
{
  "name": "ShipHdMap.Runtime",
  "rootNamespace": "ShipHdMap",
  "references": [ "Unity.Nuget.Newtonsoft-Json" ],
  "includePlatforms": [],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 3: Editor asmdef**

```json
{
  "name": "ShipHdMap.Editor",
  "rootNamespace": "ShipHdMap.Editor",
  "references": [ "ShipHdMap.Runtime", "Unity.Nuget.Newtonsoft-Json" ],
  "includePlatforms": [ "Editor" ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": false,
  "precompiledReferences": [],
  "autoReferenced": true,
  "defineConstraints": [],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 4: Tests asmdef**

```json
{
  "name": "ShipHdMap.Tests.EditMode",
  "rootNamespace": "ShipHdMap.Tests",
  "references": [ "ShipHdMap.Runtime", "ShipHdMap.Editor", "UnityEngine.TestRunner", "UnityEditor.TestRunner", "Unity.Nuget.Newtonsoft-Json" ],
  "includePlatforms": [ "Editor" ],
  "excludePlatforms": [],
  "allowUnsafeCode": false,
  "overrideReferences": true,
  "precompiledReferences": [ "nunit.framework.dll" ],
  "autoReferenced": false,
  "defineConstraints": [ "UNITY_INCLUDE_TESTS" ],
  "versionDefines": [],
  "noEngineReferences": false
}
```

- [ ] **Step 5: 스모크 테스트**

`SmokeTests.cs`:

```csharp
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class SmokeTests
    {
        [Test]
        public void TestRunnerWorks() => Assert.That(1 + 1, Is.EqualTo(2));
    }
}
```

- [ ] **Step 6: 테스트 실행 (에디터 GUI 닫은 상태)**

```bash
cd unity && U=/Applications/Unity/Hub/Editor/6000.3.24f1/Unity.app/Contents/MacOS/Unity
"$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode \
  -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"; echo EXIT=$?
grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' Logs/editmode-results.xml | head -1
```

Expected: `EXIT=0`, `total="1" passed="1" failed="0"`. 컴파일 오류가 있으면 `grep "error CS" Logs/editmode.log`.

- [ ] **Step 7: Commit**

```bash
git add unity/Assets/ShipHdMap unity/Packages/manifest.json unity/Packages/packages-lock.json
git commit -m "unity: assembly definitions and EditMode test scaffold"
```

---

### Task 2: ShipFrame 변환과 공용 테스트 벡터

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Core/ShipFrame.cs`
- Create: `docs/test-vectors/ship-frame.json`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/ShipFrameTests.cs`

**Interfaces:**
- Produces:
  - `static Vector3 ShipFrame.ToUnity(double x, double y, double z)` → `new Vector3((float)x, (float)z, (float)-y)`
  - `static (double x, double y, double z) ShipFrame.ToShip(Vector3 u)` → `(u.x, -u.z, u.y)`
  - `static float ShipFrame.UnityYawDeg(double headingDeg)` → Unity `Quaternion.Euler(0, yaw, 0)` 에 넣을 값. `= headingDeg` (Unity 의 +Y 회전은 +X 를 −Z, 즉 Ship +Y 쪽으로 돌리므로 부호가 같다)
  - `static Vector3 ShipFrame.HeadingVector(double headingDeg)` → Unity 방향 `(cos ψ, 0, −sin ψ)`
  - `static double ShipFrame.WrapDeg(double a)` → (−180, 180], `static double ShipFrame.WrapRad(double a)` → (−π, π]
- 테스트 벡터 파일은 M2(Java)·M3(TS)에서도 읽는다. 스키마를 바꾸지 말 것

- [ ] **Step 1: 테스트 벡터 파일**

`docs/test-vectors/ship-frame.json`:

```json
{
  "description": "Ship Frame (x fwd, y port, z up, heading CCW from +x) <-> Unity (x, y up, z starboard). Unity yaw equals heading.",
  "points": [
    { "ship": [0, 0, 0],        "unity": [0, 0, 0] },
    { "ship": [10, 0, 0],       "unity": [10, 0, 0] },
    { "ship": [0, 5, 0],        "unity": [0, 0, -5] },
    { "ship": [0, 0, 3],        "unity": [0, 3, 0] },
    { "ship": [82.4, 3.1, 10.6],"unity": [82.4, 10.6, -3.1] },
    { "ship": [-2.5, -6, 11.8], "unity": [-2.5, 11.8, 6] }
  ],
  "headings": [
    { "heading_deg": 0,   "unity_yaw_deg": 0,   "unity_dir": [1, 0, 0] },
    { "heading_deg": 90,  "unity_yaw_deg": 90,  "unity_dir": [0, 0, -1] },
    { "heading_deg": -90, "unity_yaw_deg": -90, "unity_dir": [0, 0, 1] },
    { "heading_deg": 180, "unity_yaw_deg": 180, "unity_dir": [-1, 0, 0] }
  ],
  "wrap_deg": [
    { "in": 190, "out": -170 }, { "in": -181, "out": 179 }, { "in": 180, "out": 180 }, { "in": 540, "out": 180 }
  ]
}
```

- [ ] **Step 2: 실패하는 테스트**

`ShipFrameTests.cs`:

```csharp
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class ShipFrameTests
    {
        static JObject Vectors()
        {
            // unity/ 에서 두 단계 위가 저장소 루트
            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "test-vectors", "ship-frame.json"));
            return JObject.Parse(File.ReadAllText(path));
        }

        [Test]
        public void PointsRoundTrip()
        {
            foreach (var p in Vectors()["points"])
            {
                var s = p["ship"]; var u = p["unity"];
                Vector3 got = ShipFrame.ToUnity((double)s[0], (double)s[1], (double)s[2]);
                Assert.That(got.x, Is.EqualTo((float)u[0]).Within(1e-5), p.ToString());
                Assert.That(got.y, Is.EqualTo((float)u[1]).Within(1e-5), p.ToString());
                Assert.That(got.z, Is.EqualTo((float)u[2]).Within(1e-5), p.ToString());
                var back = ShipFrame.ToShip(got);
                Assert.That(back.x, Is.EqualTo((double)s[0]).Within(1e-5));
                Assert.That(back.y, Is.EqualTo((double)s[1]).Within(1e-5));
                Assert.That(back.z, Is.EqualTo((double)s[2]).Within(1e-5));
            }
        }

        [Test]
        public void HeadingsMatchVectors()
        {
            foreach (var h in Vectors()["headings"])
            {
                double deg = (double)h["heading_deg"];
                Assert.That(ShipFrame.UnityYawDeg(deg), Is.EqualTo((float)h["unity_yaw_deg"]).Within(1e-5));
                Vector3 dir = ShipFrame.HeadingVector(deg);
                var e = h["unity_dir"];
                Assert.That(dir.x, Is.EqualTo((float)e[0]).Within(1e-5));
                Assert.That(dir.y, Is.EqualTo((float)e[1]).Within(1e-5));
                Assert.That(dir.z, Is.EqualTo((float)e[2]).Within(1e-5));
                // Quaternion 으로 +X 를 돌린 결과가 HeadingVector 와 같아야 한다
                Vector3 viaQuat = Quaternion.Euler(0, ShipFrame.UnityYawDeg(deg), 0) * Vector3.right;
                Assert.That(Vector3.Distance(viaQuat, dir), Is.LessThan(1e-4));
            }
        }

        [Test]
        public void WrapDeg()
        {
            foreach (var w in Vectors()["wrap_deg"])
                Assert.That(ShipFrame.WrapDeg((double)w["in"]), Is.EqualTo((double)w["out"]).Within(1e-9));
            Assert.That(ShipFrame.WrapRad(System.Math.PI * 3), Is.EqualTo(System.Math.PI).Within(1e-12));
            Assert.That(ShipFrame.WrapRad(-System.Math.PI), Is.EqualTo(System.Math.PI).Within(1e-12));
        }
    }
}
```

- [ ] **Step 3: 실패 확인**

Task 1 Step 6 의 명령 실행. Expected: 컴파일 오류 `error CS0103: The name 'ShipFrame' does not exist`.

- [ ] **Step 4: 구현**

`ShipFrame.cs`:

```csharp
using System;
using UnityEngine;

namespace ShipHdMap
{
    /// Ship Frame: x fwd (from AP), y port (+), z up (from baseline). Heading CCW from +x, degrees.
    /// Unity: x fwd, y up, z starboard. Unity yaw (about +Y) rotates +X toward -Z = ship +Y, so yaw == heading.
    public static class ShipFrame
    {
        public static Vector3 ToUnity(double x, double y, double z) => new Vector3((float)x, (float)z, (float)-y);

        public static (double x, double y, double z) ToShip(Vector3 u) => (u.x, -u.z, u.y);

        public static float UnityYawDeg(double headingDeg) => (float)headingDeg;

        public static Vector3 HeadingVector(double headingDeg)
        {
            double r = headingDeg * Math.PI / 180.0;
            return new Vector3((float)Math.Cos(r), 0f, (float)-Math.Sin(r));
        }

        /// (-180, 180]
        public static double WrapDeg(double a)
        {
            a %= 360.0;
            if (a <= -180.0) a += 360.0;
            else if (a > 180.0) a -= 360.0;
            return a;
        }

        /// (-pi, pi]
        public static double WrapRad(double a)
        {
            a %= 2 * Math.PI;
            if (a <= -Math.PI) a += 2 * Math.PI;
            else if (a > Math.PI) a -= 2 * Math.PI;
            return a;
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

같은 명령. Expected: `total="4" passed="4" failed="0"`.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/ShipHdMap/Runtime/Core/ShipFrame.cs unity/Assets/ShipHdMap/Tests/EditMode/ShipFrameTests.cs docs/test-vectors/ship-frame.json
git commit -m "unity: ShipFrame conversion with shared test vectors"
```

---

### Task 3: MapModel DTO 와 차량 지도 픽스처

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Core/MapModel.cs`
- Create: `docs/fixtures/vehicle-map.sample.json`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/MapModelTests.cs`

**Interfaces:**
- Produces (모두 `namespace ShipHdMap`, Newtonsoft 직렬화, 필드명은 스펙 6장 JSON 키와 같게 `[JsonProperty]`):
  - `class VehicleMap { string schema; string map_id; int version; string generated_at; FrameInfo frame; List<Deck> decks; List<Landmark> landmarks; List<Lane> lanes; List<ParkingSlot> parking_slots; List<LashingPoint> lashing_points; List<Marking> markings; List<Facility> facilities; List<Ramp> ramps; }`
  - `class Deck { string id; string name; double z_surface; double z_clear; bool movable; double[][] outline; }`
  - `class Landmark { string id; Marker marker; double[] position; double[] normal; double size_m; string deck_id; string mounted_on; }`, `class Marker { string family; int code; }`
  - `class Lane { string id; string deck_id; double[][] centerline; double width_m; string direction; double speed_limit_kmh; List<string> next; }`
  - `class ParkingSlot { string id; string deck_id; double[][] polygon; TargetPose target_pose; Tolerance tolerance; string vehicle_class; string access_lane_id; List<string> lashing_points; int sequence_no; string status; }`, `class TargetPose { double x; double y; double heading_deg; }`, `class Tolerance { double lat_m; double lon_m; double heading_deg; }`
  - `class LashingPoint { string id; string kind; double[] position; string deck_id; }`
  - `class Marking { string id; string kind; double[][] polygon; string deck_id; }`
  - `class Facility { string id; string kind; double[][] footprint; double z_min; double z_max; string deck_id; }`
  - `class Ramp { string id; string type; double[][] hinge; double length_m; double width_m; double[] angle_range_deg; string connects_lane; List<string> transition_landmarks; }`
  - `class SeedData { List<Deck> decks; List<Facility> facilities; List<LashingPoint> lashing_points; List<Ramp> ramps; List<Lane> lanes; }` (생성기 출력, 스펙 7 `POST .../seed` 본문)
  - `static class MapJson { static string Serialize(object o); static T Parse<T>(string json); }` — `NullValueHandling.Ignore`, 들여쓰기

- [ ] **Step 1: 픽스처 파일**

`docs/fixtures/vehicle-map.sample.json` (Deck 3 하나, 랜드마크 3, 차로 1, 구획 2, 래싱 8, 기둥 1, 램프 1. 좌표는 Task 6 생성기 기본값과 맞춘다: 갑판 길이 120 m, 폭 24 m, z_surface 10.6):

```json
{
  "schema": "ship-hdmap/vehicle-map/1.0",
  "map_id": "roro-demo-01",
  "version": 1,
  "generated_at": "2026-09-15T00:00:00Z",
  "frame": { "name": "SHIP_AP", "origin": "AP x Baseline x Centerline",
             "axes": { "x": "AP->bow (+)", "y": "port (+)", "z": "baseline->up (+)" }, "unit": "m" },
  "decks": [ { "id": "D3", "name": "Deck 3", "z_surface": 10.6, "z_clear": 2.2, "movable": false,
               "outline": [[0, -12, 10.6], [120, -12, 10.6], [120, 12, 10.6], [0, 12, 10.6], [0, -12, 10.6]] } ],
  "landmarks": [
    { "id": "LM-0001", "marker": { "family": "apriltag-36h11", "code": 1 }, "position": [4.0, -6.5, 11.8], "normal": [0, 1, 0], "size_m": 0.30, "deck_id": "D3", "mounted_on": "C-PILLAR-001" },
    { "id": "LM-0002", "marker": { "family": "apriltag-36h11", "code": 2 }, "position": [4.0, 6.5, 11.8], "normal": [0, -1, 0], "size_m": 0.30, "deck_id": "D3", "mounted_on": "C-PILLAR-002" },
    { "id": "LM-0003", "marker": { "family": "apriltag-36h11", "code": 3 }, "position": [40.0, 11.9, 11.8], "normal": [0, -1, 0], "size_m": 0.30, "deck_id": "D3", "mounted_on": "HULL-PORT" }
  ],
  "lanes": [ { "id": "A2-0001", "deck_id": "D3", "centerline": [[2, 0, 10.6], [60, 0, 10.6], [118, 0, 10.6]],
               "width_m": 3.2, "direction": "forward", "speed_limit_kmh": 10, "next": [] } ],
  "parking_slots": [
    { "id": "PS-D3-001", "deck_id": "D3", "polygon": [[100, 2.0, 10.6], [104.8, 2.0, 10.6], [104.8, 3.85, 10.6], [100, 3.85, 10.6], [100, 2.0, 10.6]],
      "target_pose": { "x": 102.4, "y": 2.925, "heading_deg": 0 }, "tolerance": { "lat_m": 0.15, "lon_m": 0.30, "heading_deg": 2 },
      "vehicle_class": "passenger", "access_lane_id": "A2-0001", "lashing_points": ["LP-0001", "LP-0002", "LP-0003", "LP-0004"], "sequence_no": 1, "status": "empty" },
    { "id": "PS-D3-002", "deck_id": "D3", "polygon": [[94.8, 2.0, 10.6], [99.6, 2.0, 10.6], [99.6, 3.85, 10.6], [94.8, 3.85, 10.6], [94.8, 2.0, 10.6]],
      "target_pose": { "x": 97.2, "y": 2.925, "heading_deg": 0 }, "tolerance": { "lat_m": 0.15, "lon_m": 0.30, "heading_deg": 2 },
      "vehicle_class": "passenger", "access_lane_id": "A2-0001", "lashing_points": ["LP-0005", "LP-0006", "LP-0007", "LP-0008"], "sequence_no": 2, "status": "empty" }
  ],
  "lashing_points": [
    { "id": "LP-0001", "kind": "cloverleaf", "position": [100.5, 2.25, 10.6], "deck_id": "D3" },
    { "id": "LP-0002", "kind": "cloverleaf", "position": [100.5, 3.75, 10.6], "deck_id": "D3" },
    { "id": "LP-0003", "kind": "cloverleaf", "position": [104.25, 2.25, 10.6], "deck_id": "D3" },
    { "id": "LP-0004", "kind": "cloverleaf", "position": [104.25, 3.75, 10.6], "deck_id": "D3" },
    { "id": "LP-0005", "kind": "cloverleaf", "position": [95.25, 2.25, 10.6], "deck_id": "D3" },
    { "id": "LP-0006", "kind": "cloverleaf", "position": [95.25, 3.75, 10.6], "deck_id": "D3" },
    { "id": "LP-0007", "kind": "cloverleaf", "position": [99.0, 2.25, 10.6], "deck_id": "D3" },
    { "id": "LP-0008", "kind": "cloverleaf", "position": [99.0, 3.75, 10.6], "deck_id": "D3" }
  ],
  "markings": [],
  "facilities": [ { "id": "C-PILLAR-001", "kind": "pillar", "footprint": [[3.7, -6.8, 10.6], [4.3, -6.8, 10.6], [4.3, -6.2, 10.6], [3.7, -6.2, 10.6], [3.7, -6.8, 10.6]], "z_min": 10.6, "z_max": 12.8, "deck_id": "D3" } ],
  "ramps": [ { "id": "RAMP-STERN", "type": "stern_quarter", "hinge": [[0, -6, 10.6], [0, 6, 10.6]], "length_m": 30, "width_m": 12,
               "angle_range_deg": [-7, 4], "connects_lane": "A2-0001", "transition_landmarks": ["LM-0001", "LM-0002"] } ]
}
```

- [ ] **Step 2: 실패하는 테스트**

`MapModelTests.cs`:

```csharp
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class MapModelTests
    {
        static string FixturePath() =>
            Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json"));

        [Test]
        public void FixtureParses()
        {
            var map = MapJson.Parse<VehicleMap>(File.ReadAllText(FixturePath()));
            Assert.That(map.schema, Is.EqualTo("ship-hdmap/vehicle-map/1.0"));
            Assert.That(map.decks.Count, Is.EqualTo(1));
            Assert.That(map.decks[0].outline.Length, Is.EqualTo(5));
            Assert.That(map.landmarks.Count, Is.EqualTo(3));
            Assert.That(map.landmarks[0].marker.code, Is.EqualTo(1));
            Assert.That(map.landmarks[0].normal, Is.EqualTo(new double[] { 0, 1, 0 }));
            Assert.That(map.parking_slots[0].target_pose.heading_deg, Is.EqualTo(0));
            Assert.That(map.parking_slots[0].lashing_points.Count, Is.EqualTo(4));
            Assert.That(map.ramps[0].transition_landmarks, Is.EqualTo(new[] { "LM-0001", "LM-0002" }));
        }

        [Test]
        public void RoundTripKeepsKeys()
        {
            string original = File.ReadAllText(FixturePath());
            var map = MapJson.Parse<VehicleMap>(original);
            string again = MapJson.Serialize(map);
            var back = MapJson.Parse<VehicleMap>(again);
            Assert.That(MapJson.Serialize(back), Is.EqualTo(again));
            Assert.That(again, Does.Contain("\"map_id\""));
            Assert.That(again, Does.Contain("\"z_surface\""));
            Assert.That(again, Does.Not.Contain("\"MapId\""));
        }

        [Test]
        public void NullListsAreOmitted()
        {
            string json = MapJson.Serialize(new SeedData { decks = new System.Collections.Generic.List<Deck>() });
            Assert.That(json, Does.Contain("\"decks\""));
            Assert.That(json, Does.Not.Contain("\"facilities\""));
        }
    }
}
```

- [ ] **Step 3: 실패 확인** — 컴파일 오류 `MapJson`, `VehicleMap` 없음.

- [ ] **Step 4: 구현**

`MapModel.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace ShipHdMap
{
    // Field names intentionally match the vehicle-map v1 JSON keys (spec §6). Do not rename.
    public class FrameInfo { public string name; public string origin; public Dictionary<string, string> axes; public string unit; }
    public class Marker { public string family; public int code; }
    public class Deck { public string id; public string name; public double z_surface; public double z_clear; public bool movable; public double[][] outline; }
    public class Landmark { public string id; public Marker marker; public double[] position; public double[] normal; public double size_m; public string deck_id; public string mounted_on; }
    public class Lane { public string id; public string deck_id; public double[][] centerline; public double width_m; public string direction; public double speed_limit_kmh; public List<string> next; }
    public class TargetPose { public double x; public double y; public double heading_deg; }
    public class Tolerance { public double lat_m; public double lon_m; public double heading_deg; }
    public class ParkingSlot
    {
        public string id; public string deck_id; public double[][] polygon; public TargetPose target_pose; public Tolerance tolerance;
        public string vehicle_class; public string access_lane_id; public List<string> lashing_points; public int sequence_no; public string status;
    }
    public class LashingPoint { public string id; public string kind; public double[] position; public string deck_id; }
    public class Marking { public string id; public string kind; public double[][] polygon; public string deck_id; }
    public class Facility { public string id; public string kind; public double[][] footprint; public double z_min; public double z_max; public string deck_id; }
    public class Ramp { public string id; public string type; public double[][] hinge; public double length_m; public double width_m; public double[] angle_range_deg; public string connects_lane; public List<string> transition_landmarks; }

    public class VehicleMap
    {
        public string schema; public string map_id; public int version; public string generated_at; public FrameInfo frame;
        public List<Deck> decks; public List<Landmark> landmarks; public List<Lane> lanes; public List<ParkingSlot> parking_slots;
        public List<LashingPoint> lashing_points; public List<Marking> markings; public List<Facility> facilities; public List<Ramp> ramps;
    }

    /// Output of the ship generator; body of POST /api/datasets/{id}/seed (spec §7).
    public class SeedData { public List<Deck> decks; public List<Facility> facilities; public List<LashingPoint> lashing_points; public List<Ramp> ramps; public List<Lane> lanes; }

    public static class MapJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore, Formatting = Formatting.Indented };
        public static string Serialize(object o) => JsonConvert.SerializeObject(o, Settings);
        public static T Parse<T>(string json) => JsonConvert.DeserializeObject<T>(json, Settings);
    }
}
```

- [ ] **Step 5: 통과 확인** — `passed="7"`.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/ShipHdMap/Runtime/Core/MapModel.cs unity/Assets/ShipHdMap/Tests/EditMode/MapModelTests.cs docs/fixtures/vehicle-map.sample.json
git commit -m "unity: vehicle-map DTOs and sample fixture"
```

---

### Task 4: Localizer — 폐형해(N=1)와 Gauss-Newton(N≥2)

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Localization/Observation.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Localization/Localizer.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/LocalizerTests.cs`

**Interfaces:**
- Produces:
  - `struct Observation { string id; double r; double thetaRad; double alphaRad; }` — 거리, 차량 진행방향 기준 상대방위, 차량 프레임에서 본 마커 normal 각도 (스펙 9.4)
  - `struct Pose2D { double x; double y; double psiRad; }`
  - `struct LandmarkRef { string id; double mx; double my; double phiRad; }` — 지도의 마커 위치와 normal 각도 `atan2(ny, nx)`
  - `class LocalizerResult { bool ok; Pose2D pose; int nObs; double residualRms; int iterations; }`
  - `static Observation Localizer.Observe(Pose2D truth, LandmarkRef lm)` — 노이즈 없는 관측 생성 (테스트·감지 모델 공용)
  - `static Pose2D Localizer.ClosedForm(Observation o, LandmarkRef lm)`
  - `static LocalizerResult Localizer.Solve(IList<Observation> obs, IDictionary<string, LandmarkRef> map, double sigmaR, double sigmaTheta, double sigmaAlpha, Pose2D? previous)` — N=0 이면 `ok=false`, `pose=previous ?? default`
- 각도는 내부 라디안. 도 변환은 호출자가 한다

- [ ] **Step 1: 실패하는 테스트**

`LocalizerTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class LocalizerTests
    {
        const double D = Math.PI / 180.0;

        static Dictionary<string, LandmarkRef> Map() => new Dictionary<string, LandmarkRef>
        {
            ["A"] = new LandmarkRef { id = "A", mx = 4.0, my = -6.5, phiRad = 90 * D },   // normal +y
            ["B"] = new LandmarkRef { id = "B", mx = 4.0, my = 6.5, phiRad = -90 * D },   // normal -y
            ["C"] = new LandmarkRef { id = "C", mx = 40.0, my = 11.9, phiRad = -90 * D },
        };

        [Test]
        public void ObserveThenClosedFormRecoversPose()
        {
            var truth = new Pose2D { x = 10, y = 1.5, psiRad = 20 * D };
            foreach (var lm in Map().Values)
            {
                var o = Localizer.Observe(truth, lm);
                var p = Localizer.ClosedForm(o, lm);
                Assert.That(p.x, Is.EqualTo(truth.x).Within(1e-6), lm.id);
                Assert.That(p.y, Is.EqualTo(truth.y).Within(1e-6), lm.id);
                Assert.That(ShipFrame.WrapRad(p.psiRad - truth.psiRad), Is.EqualTo(0).Within(1e-9), lm.id);
            }
        }

        [Test]
        public void ObserveGeometry()
        {
            // vehicle at origin heading +x; marker straight ahead at (5,0) facing -x: r=5, theta=0, alpha=180deg
            var o = Localizer.Observe(new Pose2D(), new LandmarkRef { id = "F", mx = 5, my = 0, phiRad = Math.PI });
            Assert.That(o.r, Is.EqualTo(5).Within(1e-9));
            Assert.That(o.thetaRad, Is.EqualTo(0).Within(1e-9));
            Assert.That(Math.Abs(o.alphaRad), Is.EqualTo(Math.PI).Within(1e-9));
            // marker to the left (+y) -> theta = +90deg
            var l = Localizer.Observe(new Pose2D(), new LandmarkRef { id = "L", mx = 0, my = 3, phiRad = -Math.PI / 2 });
            Assert.That(l.thetaRad, Is.EqualTo(Math.PI / 2).Within(1e-9));
        }

        [Test]
        public void GaussNewtonThreeMarkersNoNoise()
        {
            var truth = new Pose2D { x = 12, y = -2, psiRad = -15 * D };
            var obs = new List<Observation>();
            foreach (var lm in Map().Values) obs.Add(Localizer.Observe(truth, lm));
            var res = Localizer.Solve(obs, Map(), 0.2, 1 * D, 2 * D, null);
            Assert.That(res.ok);
            Assert.That(res.nObs, Is.EqualTo(3));
            Assert.That(res.pose.x, Is.EqualTo(truth.x).Within(0.05));
            Assert.That(res.pose.y, Is.EqualTo(truth.y).Within(0.05));
            Assert.That(ShipFrame.WrapRad(res.pose.psiRad - truth.psiRad), Is.EqualTo(0).Within(0.5 * D));
            Assert.That(res.residualRms, Is.LessThan(1e-6));
        }

        [Test]
        public void GaussNewtonConvergesFromBadInitial()
        {
            var truth = new Pose2D { x = 12, y = -2, psiRad = -15 * D };
            var obs = new List<Observation>();
            foreach (var lm in Map().Values) obs.Add(Localizer.Observe(truth, lm));
            var bad = new Pose2D { x = 14, y = 0, psiRad = 0 };
            var res = Localizer.Solve(obs, Map(), 0.2, 1 * D, 2 * D, bad);
            Assert.That(res.pose.x, Is.EqualTo(truth.x).Within(0.05));
            Assert.That(res.pose.y, Is.EqualTo(truth.y).Within(0.05));
        }

        [Test]
        public void NoiseAveragesOut()
        {
            var truth = new Pose2D { x = 12, y = -2, psiRad = 0 };
            var rng = new Random(7);
            double Gauss(double s) { double u1 = 1 - rng.NextDouble(), u2 = rng.NextDouble(); return s * Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2); }
            double errSingle = 0, errMulti = 0; int trials = 200;
            for (int t = 0; t < trials; t++)
            {
                var obs = new List<Observation>();
                foreach (var lm in Map().Values)
                {
                    var o = Localizer.Observe(truth, lm);
                    obs.Add(new Observation { id = o.id, r = o.r + Gauss(0.2), thetaRad = o.thetaRad + Gauss(1 * D), alphaRad = o.alphaRad + Gauss(2 * D) });
                }
                var single = Localizer.ClosedForm(obs[0], Map()["A"]);
                var multi = Localizer.Solve(obs, Map(), 0.2, 1 * D, 2 * D, null).pose;
                errSingle += Math.Sqrt(Math.Pow(single.x - truth.x, 2) + Math.Pow(single.y - truth.y, 2));
                errMulti += Math.Sqrt(Math.Pow(multi.x - truth.x, 2) + Math.Pow(multi.y - truth.y, 2));
            }
            Assert.That(errMulti / trials, Is.LessThan(errSingle / trials));
        }

        [Test]
        public void ZeroObservationsKeepsPrevious()
        {
            var prev = new Pose2D { x = 1, y = 2, psiRad = 0.3 };
            var res = Localizer.Solve(new List<Observation>(), Map(), 0.2, 1 * D, 2 * D, prev);
            Assert.That(res.ok, Is.False);
            Assert.That(res.pose.x, Is.EqualTo(1));
            Assert.That(res.nObs, Is.EqualTo(0));
        }

        [Test]
        public void UnknownMarkerIsIgnored()
        {
            var truth = new Pose2D { x = 12, y = -2, psiRad = 0 };
            var obs = new List<Observation> { Localizer.Observe(truth, Map()["A"]), new Observation { id = "ZZZ", r = 1, thetaRad = 0, alphaRad = 0 } };
            var res = Localizer.Solve(obs, Map(), 0.2, 1 * D, 2 * D, null);
            Assert.That(res.nObs, Is.EqualTo(1));
            Assert.That(res.pose.x, Is.EqualTo(truth.x).Within(1e-6));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — 컴파일 오류 `Localizer`, `Pose2D`, `LandmarkRef`, `Observation` 없음.

- [ ] **Step 3: 구현**

`Observation.cs`:

```csharp
namespace ShipHdMap
{
    /// One detected marker, in the vehicle frame. r metres; angles radians, CCW positive.
    public struct Observation { public string id; public double r; public double thetaRad; public double alphaRad; }

    /// Vehicle pose in Ship Frame (2.5D). psi CCW from +x.
    public struct Pose2D { public double x; public double y; public double psiRad; }

    /// Map entry for a marker: position and normal angle phi = atan2(ny, nx).
    public struct LandmarkRef { public string id; public double mx; public double my; public double phiRad; }

    public class LocalizerResult { public bool ok; public Pose2D pose; public int nObs; public double residualRms; public int iterations; }
}
```

`Localizer.cs` (스펙 9.4 의 식을 그대로):

```csharp
using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    public static class Localizer
    {
        public const int MaxIterations = 5;
        public const double StopDelta = 1e-4;

        /// Noise-free observation of lm from truth: r, theta = bearing - psi, alpha = phi - psi.
        public static Observation Observe(Pose2D truth, LandmarkRef lm)
        {
            double dx = lm.mx - truth.x, dy = lm.my - truth.y;
            return new Observation
            {
                id = lm.id,
                r = Math.Sqrt(dx * dx + dy * dy),
                thetaRad = ShipFrame.WrapRad(Math.Atan2(dy, dx) - truth.psiRad),
                alphaRad = ShipFrame.WrapRad(lm.phiRad - truth.psiRad),
            };
        }

        /// N = 1: psi = phi - alpha; x = mx - r cos(psi + theta); y = my - r sin(psi + theta).
        public static Pose2D ClosedForm(Observation o, LandmarkRef lm)
        {
            double psi = ShipFrame.WrapRad(lm.phiRad - o.alphaRad);
            return new Pose2D { x = lm.mx - o.r * Math.Cos(psi + o.thetaRad), y = lm.my - o.r * Math.Sin(psi + o.thetaRad), psiRad = psi };
        }

        public static LocalizerResult Solve(IList<Observation> obs, IDictionary<string, LandmarkRef> map,
            double sigmaR, double sigmaTheta, double sigmaAlpha, Pose2D? previous)
        {
            var used = new List<(Observation o, LandmarkRef lm)>();
            foreach (var o in obs) if (map.TryGetValue(o.id, out var lm)) used.Add((o, lm));

            if (used.Count == 0)
                return new LocalizerResult { ok = false, pose = previous ?? default, nObs = 0 };

            // Initial guess: closed form from the nearest marker (or the caller's previous estimate when given).
            Pose2D p;
            if (previous.HasValue) p = previous.Value;
            else { var nearest = used[0]; foreach (var u in used) if (u.o.r < nearest.o.r) nearest = u; p = ClosedForm(nearest.o, nearest.lm); }

            if (used.Count == 1 && !previous.HasValue)
                return new LocalizerResult { ok = true, pose = p, nObs = 1, residualRms = 0, iterations = 0 };

            double wr = 1 / (sigmaR * sigmaR), wt = 1 / (sigmaTheta * sigmaTheta), wa = 1 / (sigmaAlpha * sigmaAlpha);
            int iter = 0; double rms = 0;
            for (iter = 1; iter <= MaxIterations; iter++)
            {
                // Normal equations: (J^T W J) delta = J^T W e, 3x3
                double[,] A = new double[3, 3]; double[] b = new double[3]; double sumSq = 0; int n = 0;
                foreach (var (o, lm) in used)
                {
                    double dx = lm.mx - p.x, dy = lm.my - p.y;
                    double rh = Math.Sqrt(dx * dx + dy * dy); if (rh < 1e-9) rh = 1e-9;
                    double er = o.r - rh;
                    double et = ShipFrame.WrapRad(o.thetaRad - (Math.Atan2(dy, dx) - p.psiRad));
                    double ea = ShipFrame.WrapRad(o.alphaRad - (lm.phiRad - p.psiRad));
                    double[] jr = { -dx / rh, -dy / rh, 0 };
                    double[] jt = { dy / (rh * rh), -dx / (rh * rh), -1 };
                    double[] ja = { 0, 0, -1 };
                    Accumulate(A, b, jr, wr, er); Accumulate(A, b, jt, wt, et); Accumulate(A, b, ja, wa, ea);
                    sumSq += er * er + et * et + ea * ea; n += 3;
                }
                rms = Math.Sqrt(sumSq / n);
                double[] d = Solve3(A, b);
                p.x += d[0]; p.y += d[1]; p.psiRad = ShipFrame.WrapRad(p.psiRad + d[2]);
                if (Math.Sqrt(d[0] * d[0] + d[1] * d[1] + d[2] * d[2]) < StopDelta) break;
            }
            // Final residual after the last update
            { double sumSq = 0; int n = 0;
              foreach (var (o, lm) in used)
              { double dx = lm.mx - p.x, dy = lm.my - p.y, rh = Math.Sqrt(dx * dx + dy * dy);
                double er = o.r - rh, et = ShipFrame.WrapRad(o.thetaRad - (Math.Atan2(dy, dx) - p.psiRad)), ea = ShipFrame.WrapRad(o.alphaRad - (lm.phiRad - p.psiRad));
                sumSq += er * er + et * et + ea * ea; n += 3; }
              rms = Math.Sqrt(sumSq / n); }
            return new LocalizerResult { ok = true, pose = p, nObs = used.Count, residualRms = rms, iterations = Math.Min(iter, MaxIterations) };
        }

        static void Accumulate(double[,] A, double[] b, double[] j, double w, double e)
        {
            for (int i = 0; i < 3; i++) { b[i] += w * j[i] * e; for (int k = 0; k < 3; k++) A[i, k] += w * j[i] * j[k]; }
        }

        /// Cramer's rule for the 3x3 normal equations. ponytail: direct inverse, fine for 3 unknowns; use Cholesky if this ever grows.
        static double[] Solve3(double[,] A, double[] b)
        {
            double det = Det3(A[0, 0], A[0, 1], A[0, 2], A[1, 0], A[1, 1], A[1, 2], A[2, 0], A[2, 1], A[2, 2]);
            if (Math.Abs(det) < 1e-12) return new double[3];
            double d0 = Det3(b[0], A[0, 1], A[0, 2], b[1], A[1, 1], A[1, 2], b[2], A[2, 1], A[2, 2]);
            double d1 = Det3(A[0, 0], b[0], A[0, 2], A[1, 0], b[1], A[1, 2], A[2, 0], b[2], A[2, 2]);
            double d2 = Det3(A[0, 0], A[0, 1], b[0], A[1, 0], A[1, 1], b[1], A[2, 0], A[2, 1], b[2]);
            return new[] { d0 / det, d1 / det, d2 / det };
        }

        static double Det3(double a, double b, double c, double d, double e, double f, double g, double h, double i)
            => a * (e * i - f * h) - b * (d * i - f * g) + c * (d * h - e * g);
    }
}
```

- [ ] **Step 4: 통과 확인** — `passed="14"`. `NoiseAveragesOut` 이 실패하면 시드를 바꾸지 말고 야코비안 부호를 먼저 의심한다(θ̂ 의 ∂/∂ψ = −1).

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/ShipHdMap/Runtime/Localization unity/Assets/ShipHdMap/Tests/EditMode/LocalizerTests.cs
git commit -m "unity: landmark localizer (closed form + Gauss-Newton)"
```

---

### Task 5: 선박 생성기 — 시드 데이터 (순수 C#)

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Ship/ShipParams.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Ship/ShipSeedBuilder.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/ShipSeedBuilderTests.cs`

**Interfaces:**
- Produces:
  - `[Serializable] class ShipParams { double lengthM = 120; double beamM = 24; int deckCount = 3; double firstDeckZ = 5.4; double deckPitchM = 2.6; double deckClearM = 2.2; double pillarPitchM = 12; double pillarSizeM = 0.6; double pillarInsetM = 5.5; double lashingPitchM = 0.75; double rampLengthM = 30; double rampWidthM = 12; double rampAngleMin = -7; double rampAngleMax = 4; int rampDeckIndex = 2; }`
  - `static SeedData ShipSeedBuilder.Build(ShipParams p)`
  - ID 규칙: 갑판 `D{n}` (n=1..deckCount, 아래부터), 기둥 `C-PILLAR-{deck}-{i:000}`, 래싱 `LP-{deck}-{i:0000}`, 램프 `RAMP-STERN`, 차로 `A2-{deck}-0001`
  - 갑판 윤곽: 닫힌 사각형 `[0,-b/2],[L,-b/2],[L,b/2],[0,b/2],[0,-b/2]` (z = z_surface). 폴리곤 좌표는 `[x, y, z]`
  - 기둥: 양현 `y = ±(b/2 − pillarInset)` 두 열, x = pillarPitch 배수(0 제외, L 미만). 풋프린트는 pillarSize 정사각형
  - 래싱: 갑판 안쪽 `x ∈ [1, L−1]`, `y ∈ [−b/2+1, b/2−1]` 격자, 기둥 풋프린트 안은 제외
  - 램프: `rampDeckIndex` 갑판(0 기반) 선미 `x = 0`, 힌지 `[0, −w/2, z] – [0, w/2, z]`, `transition_landmarks` 는 비움(랜드마크는 배치로 생김)
  - 차로: 각 갑판 중심선 `[2,0,z] → [L/2,0,z] → [L−2,0,z]`, 폭 3.2, 10 km/h

- [ ] **Step 1: 실패하는 테스트**

`ShipSeedBuilderTests.cs`:

```csharp
using System.Linq;
using NUnit.Framework;

namespace ShipHdMap.Tests
{
    public class ShipSeedBuilderTests
    {
        [Test]
        public void DefaultParamsProduceThreeDecks()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            Assert.That(seed.decks.Select(d => d.id), Is.EqualTo(new[] { "D1", "D2", "D3" }));
            Assert.That(seed.decks[0].z_surface, Is.EqualTo(5.4).Within(1e-9));
            Assert.That(seed.decks[2].z_surface, Is.EqualTo(10.6).Within(1e-9));
            Assert.That(seed.decks[2].z_clear, Is.EqualTo(2.2).Within(1e-9));
            var o = seed.decks[2].outline;
            Assert.That(o.Length, Is.EqualTo(5));
            Assert.That(o[0], Is.EqualTo(new double[] { 0, -12, 10.6 }));
            Assert.That(o[2], Is.EqualTo(new double[] { 120, 12, 10.6 }));
            Assert.That(o[4], Is.EqualTo(o[0]));
        }

        [Test]
        public void PillarsTwoRowsAlongPitch()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var d3 = seed.facilities.Where(f => f.deck_id == "D3" && f.kind == "pillar").ToList();
            // x = 12, 24, ..., 108 -> 9 per row, 2 rows
            Assert.That(d3.Count, Is.EqualTo(18));
            Assert.That(d3.All(f => f.footprint.Length == 5));
            Assert.That(d3.Select(f => f.z_min).Distinct().Single(), Is.EqualTo(10.6).Within(1e-9));
            Assert.That(d3.Select(f => f.z_max).Distinct().Single(), Is.EqualTo(12.8).Within(1e-9));
            var ys = d3.Select(f => (f.footprint[0][1] + f.footprint[2][1]) / 2).Distinct().OrderBy(y => y).ToList();
            Assert.That(ys, Is.EqualTo(new[] { -6.5, 6.5 }).Within(1e-9));
            Assert.That(d3[0].id, Does.StartWith("C-PILLAR-D3-"));
        }

        [Test]
        public void LashingGridSkipsPillars()
        {
            var p = new ShipParams { lengthM = 20, beamM = 10, deckCount = 1, pillarPitchM = 10, pillarInsetM = 2, lashingPitchM = 1 };
            var seed = ShipSeedBuilder.Build(p);
            var lp = seed.lashing_points;
            Assert.That(lp.All(l => l.kind == "cloverleaf" && l.deck_id == "D1"));
            // grid x in [1..19] step 1 (19), y in [-4..4] step 1 (9) = 171, minus points inside the two 0.6 m pillars at (10, ±3) -> (10,3),(10,-3)
            Assert.That(lp.Count, Is.EqualTo(171 - 2));
            Assert.That(lp.Any(l => l.position[0] == 10 && l.position[1] == 3), Is.False);
            Assert.That(lp.Select(l => l.id).Distinct().Count(), Is.EqualTo(lp.Count));
        }

        [Test]
        public void RampAndLanes()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var ramp = seed.ramps.Single();
            Assert.That(ramp.id, Is.EqualTo("RAMP-STERN"));
            Assert.That(ramp.hinge, Is.EqualTo(new[] { new double[] { 0, -6, 10.6 }, new double[] { 0, 6, 10.6 } }));
            Assert.That(ramp.angle_range_deg, Is.EqualTo(new double[] { -7, 4 }));
            Assert.That(ramp.connects_lane, Is.EqualTo("A2-D3-0001"));
            Assert.That(seed.lanes.Count, Is.EqualTo(3));
            var lane = seed.lanes.Single(l => l.deck_id == "D3");
            Assert.That(lane.centerline, Is.EqualTo(new[] { new double[] { 2, 0, 10.6 }, new double[] { 60, 0, 10.6 }, new double[] { 118, 0, 10.6 } }));
            Assert.That(lane.width_m, Is.EqualTo(3.2));
        }

        [Test]
        public void SeedSerializesWithSpecKeys()
        {
            string json = MapJson.Serialize(ShipSeedBuilder.Build(new ShipParams()));
            Assert.That(json, Does.Contain("\"z_surface\"").And.Contain("\"lashing_points\"").And.Contain("\"footprint\""));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — 컴파일 오류 `ShipParams`, `ShipSeedBuilder` 없음.

- [ ] **Step 3: 구현**

`ShipParams.cs`:

```csharp
using System;

namespace ShipHdMap
{
    /// Generator parameters. Defaults produce the fixture geometry (L 120 m, B 24 m, Deck 3 at z 10.6).
    [Serializable]
    public class ShipParams
    {
        public double lengthM = 120, beamM = 24;
        public int deckCount = 3;
        public double firstDeckZ = 5.4, deckPitchM = 2.6, deckClearM = 2.2;
        public double pillarPitchM = 12, pillarSizeM = 0.6, pillarInsetM = 5.5;
        public double lashingPitchM = 0.75;
        public double rampLengthM = 30, rampWidthM = 12, rampAngleMin = -7, rampAngleMax = 4;
        public int rampDeckIndex = 2;
        public double laneWidthM = 3.2, laneSpeedKmh = 10;
        public double DeckZ(int i) => firstDeckZ + i * deckPitchM;
    }
}
```

`ShipSeedBuilder.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    public static class ShipSeedBuilder
    {
        public static SeedData Build(ShipParams p)
        {
            var seed = new SeedData { decks = new(), facilities = new(), lashing_points = new(), ramps = new(), lanes = new() };
            double hb = p.beamM / 2;
            for (int i = 0; i < p.deckCount; i++)
            {
                string deckId = $"D{i + 1}"; double z = p.DeckZ(i);
                seed.decks.Add(new Deck { id = deckId, name = $"Deck {i + 1}", z_surface = z, z_clear = p.deckClearM, movable = false,
                    outline = Rect(0, -hb, p.lengthM, hb, z) });

                var pillars = new List<Facility>(); int n = 0;
                foreach (double y in new[] { -(hb - p.pillarInsetM), hb - p.pillarInsetM })
                    for (double x = p.pillarPitchM; x < p.lengthM - 1e-9; x += p.pillarPitchM)
                    {
                        double h = p.pillarSizeM / 2;
                        pillars.Add(new Facility { id = $"C-PILLAR-{deckId}-{++n:000}", kind = "pillar", deck_id = deckId,
                            footprint = Rect(x - h, y - h, x + h, y + h, z), z_min = z, z_max = z + p.deckClearM });
                    }
                seed.facilities.AddRange(pillars);

                int m = 0;
                for (double x = 1; x <= p.lengthM - 1 + 1e-9; x += p.lashingPitchM)
                    for (double y = -hb + 1; y <= hb - 1 + 1e-9; y += p.lashingPitchM)
                    {
                        if (InsideAnyPillar(x, y, pillars)) continue;
                        seed.lashing_points.Add(new LashingPoint { id = $"LP-{deckId}-{++m:0000}", kind = "cloverleaf", deck_id = deckId,
                            position = new[] { Math.Round(x, 6), Math.Round(y, 6), z } });
                    }

                seed.lanes.Add(new Lane { id = $"A2-{deckId}-0001", deck_id = deckId, width_m = p.laneWidthM, direction = "forward", speed_limit_kmh = p.laneSpeedKmh, next = new(),
                    centerline = new[] { new[] { 2.0, 0, z }, new[] { p.lengthM / 2, 0, z }, new[] { p.lengthM - 2, 0, z } } });
            }
            double rz = p.DeckZ(p.rampDeckIndex); double hw = p.rampWidthM / 2;
            seed.ramps.Add(new Ramp { id = "RAMP-STERN", type = "stern_quarter", hinge = new[] { new[] { 0.0, -hw, rz }, new[] { 0.0, hw, rz } },
                length_m = p.rampLengthM, width_m = p.rampWidthM, angle_range_deg = new[] { p.rampAngleMin, p.rampAngleMax },
                connects_lane = $"A2-D{p.rampDeckIndex + 1}-0001", transition_landmarks = new() });
            return seed;
        }

        static double[][] Rect(double x0, double y0, double x1, double y1, double z) =>
            new[] { new[] { x0, y0, z }, new[] { x1, y0, z }, new[] { x1, y1, z }, new[] { x0, y1, z }, new[] { x0, y0, z } };

        static bool InsideAnyPillar(double x, double y, List<Facility> pillars)
        {
            foreach (var f in pillars)
                if (x >= f.footprint[0][0] - 1e-9 && x <= f.footprint[2][0] + 1e-9 && y >= f.footprint[0][1] - 1e-9 && y <= f.footprint[2][1] + 1e-9) return true;
            return false;
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — `passed="19"`. `LashingGridSkipsPillars` 는 부동소수 누적 때문에 격자 개수가 1 어긋날 수 있다. 그러면 루프를 정수 인덱스(`for (int k = 0; k * pitch <= span; k++)`)로 바꾼다.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/ShipHdMap/Runtime/Ship unity/Assets/ShipHdMap/Tests/EditMode/ShipSeedBuilderTests.cs
git commit -m "unity: ship seed builder (decks, pillars, lashing grid, ramp, lanes)"
```

---

### Task 6: 선박 생성기 — 메시와 씬 계층

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Ship/ShipMeshBuilder.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/ShipMeshBuilderTests.cs`

**Interfaces:**
- Produces:
  - `static GameObject ShipMeshBuilder.Build(SeedData seed, ShipParams p, Transform parent = null)` → 루트 `"Ship"`. 자식 구조: `Ship/D3/Floor`, `Ship/D3/Pillars/C-PILLAR-D3-001`, `Ship/D3/Lashing` (소켓 하나당 작은 원기둥, 갑판당 최대 2000개까지만 생성하고 나머지는 생략 — `ponytail:` 표시), `Ship/D3/MEP` (천장 아래 배관 3줄: x 방향 긴 원기둥, y = −8, 0, +8), `Ship/D3/HullPort`, `Ship/D3/HullStbd` (측벽 판), `Ship/Ramp` (힌지 기준 회전하는 판, 자식 `Plate`)
  - 모든 콜라이더는 `MeshCollider` 아니면 `BoxCollider`. 레이캐스트 배치·차폐 판정에 쓴다
  - 레이어: 갑판 바닥·기둥·벽·MEP 는 Unity 레이어 `"ShipStructure"` (Task 10 에서 TagManager 에 추가, 없으면 Default)
  - `static void ShipMeshBuilder.SetRampAngle(GameObject ship, double angleDeg)` — `Ship/Ramp` 를 힌지(y 축 = Unity Z 축)에 대해 회전. 양수는 램프 끝이 올라감
  - `static void ShipMeshBuilder.SetDeckVisibility(GameObject ship, string deckIdOrAll)` — 선택 갑판 외에는 렌더러 알파 0.15(머티리얼 Fade)

- [ ] **Step 1: 실패하는 테스트**

`ShipMeshBuilderTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class ShipMeshBuilderTests
    {
        GameObject ship;
        [TearDown] public void Cleanup() { if (ship) Object.DestroyImmediate(ship); }

        [Test]
        public void BuildsDeckHierarchyInUnityCoordinates()
        {
            var p = new ShipParams();
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            Assert.That(ship.name, Is.EqualTo("Ship"));
            var floor = ship.transform.Find("D3/Floor");
            Assert.That(floor, Is.Not.Null);
            // Deck 3 floor top at Unity y = 10.6, centred at x = 60, z = 0
            Assert.That(floor.GetComponent<Collider>().bounds.max.y, Is.EqualTo(10.6f).Within(1e-3));
            Assert.That(floor.GetComponent<Collider>().bounds.center.x, Is.EqualTo(60f).Within(1e-3));
            var pillar = ship.transform.Find("D3/Pillars/C-PILLAR-D3-001");
            Assert.That(pillar, Is.Not.Null);
            // first pillar: ship (12, -6.5) -> Unity (12, *, +6.5)
            Assert.That(pillar.position.x, Is.EqualTo(12f).Within(1e-3));
            Assert.That(pillar.position.z, Is.EqualTo(6.5f).Within(1e-3));
            Assert.That(ship.transform.Find("D3/MEP").childCount, Is.EqualTo(3));
            Assert.That(ship.transform.Find("Ramp/Plate"), Is.Not.Null);
        }

        [Test]
        public void RampAngleRotatesAboutHinge()
        {
            var p = new ShipParams();
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            var ramp = ship.transform.Find("Ramp");
            ShipMeshBuilder.SetRampAngle(ship, 0);
            float y0 = ramp.Find("Plate").GetComponent<Collider>().bounds.min.x; // far end of ramp is at x = -30
            Assert.That(y0, Is.EqualTo(-30f).Within(0.05f));
            ShipMeshBuilder.SetRampAngle(ship, 4);
            var b = ramp.Find("Plate").GetComponent<Collider>().bounds;
            Assert.That(b.min.x, Is.GreaterThan(-30f));      // shortened footprint
            Assert.That(ramp.position.y, Is.EqualTo(10.6f).Within(1e-3)); // hinge does not move
        }

        [Test]
        public void DeckVisibilityFadesOthers()
        {
            var p = new ShipParams();
            ship = ShipMeshBuilder.Build(ShipSeedBuilder.Build(p), p);
            ShipMeshBuilder.SetDeckVisibility(ship, "D3");
            float a1 = ship.transform.Find("D1/Floor").GetComponent<Renderer>().sharedMaterial.color.a;
            float a3 = ship.transform.Find("D3/Floor").GetComponent<Renderer>().sharedMaterial.color.a;
            Assert.That(a1, Is.LessThan(0.5f));
            Assert.That(a3, Is.EqualTo(1f).Within(1e-3));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — 컴파일 오류 `ShipMeshBuilder` 없음.

- [ ] **Step 3: 구현**

`ShipMeshBuilder.cs`:

```csharp
using UnityEngine;

namespace ShipHdMap
{
    public static class ShipMeshBuilder
    {
        const float FloorThick = 0.2f, WallThick = 0.2f, MepRadius = 0.15f, LashRadius = 0.06f;
        const int MaxLashingPerDeck = 2000; // ponytail: cap for editor perf; instancing/GPU batching if the full grid is ever needed
        static Material _opaque, _fade;

        public static GameObject Build(SeedData seed, ShipParams p, Transform parent = null)
        {
            var ship = new GameObject("Ship"); if (parent) ship.transform.SetParent(parent, false);
            int layer = LayerMask.NameToLayer("ShipStructure"); if (layer < 0) layer = 0;
            foreach (var d in seed.decks)
            {
                var deck = Child(ship, d.id);
                float z = (float)d.z_surface, L = (float)p.lengthM, B = (float)p.beamM;
                var floor = Prim(deck, "Floor", PrimitiveType.Cube, new Vector3(L / 2, z - FloorThick / 2, 0), new Vector3(L, FloorThick, B), Color(0.55f, 0.55f, 0.6f), layer);
                Prim(deck, "HullPort", PrimitiveType.Cube, new Vector3(L / 2, z + (float)d.z_clear / 2, -B / 2), new Vector3(L, (float)d.z_clear, WallThick), Color(0.4f, 0.45f, 0.5f), layer);
                Prim(deck, "HullStbd", PrimitiveType.Cube, new Vector3(L / 2, z + (float)d.z_clear / 2, B / 2), new Vector3(L, (float)d.z_clear, WallThick), Color(0.4f, 0.45f, 0.5f), layer);
                var pillars = Child(deck, "Pillars");
                foreach (var f in seed.facilities) if (f.deck_id == d.id && f.kind == "pillar")
                {
                    float cx = (float)(f.footprint[0][0] + f.footprint[2][0]) / 2, cy = (float)(f.footprint[0][1] + f.footprint[2][1]) / 2;
                    float sx = (float)(f.footprint[2][0] - f.footprint[0][0]), sy = (float)(f.footprint[2][1] - f.footprint[0][1]), h = (float)(f.z_max - f.z_min);
                    Prim(pillars, f.id, PrimitiveType.Cube, ShipFrame.ToUnity(cx, cy, f.z_min + h / 2), new Vector3(sx, h, sy), Color(0.35f, 0.35f, 0.38f), layer);
                }
                var lash = Child(deck, "Lashing"); int n = 0;
                foreach (var lp in seed.lashing_points) if (lp.deck_id == d.id && n++ < MaxLashingPerDeck)
                {
                    var g = Prim(lash, lp.id, PrimitiveType.Cylinder, ShipFrame.ToUnity(lp.position[0], lp.position[1], lp.position[2] + 0.005), new Vector3(LashRadius * 2, 0.005f, LashRadius * 2), Color(0.8f, 0.2f, 0.2f), layer);
                    Object.DestroyImmediate(g.GetComponent<Collider>()); // sockets are visual only
                }
                var mep = Child(deck, "MEP");
                foreach (float y in new[] { -8f, 0f, 8f })
                {
                    var pipe = Prim(mep, $"Pipe_{y:+0;-0;0}", PrimitiveType.Cylinder, ShipFrame.ToUnity(p.lengthM / 2, y, d.z_surface + d.z_clear - 0.3), new Vector3(MepRadius * 2, L / 2, MepRadius * 2), Color(0.75f, 0.6f, 0.2f), layer);
                    pipe.transform.rotation = Quaternion.Euler(0, 0, 90); // cylinder axis along Unity X
                }
            }
            foreach (var r in seed.ramps)
            {
                var ramp = Child(ship, "Ramp");
                ramp.transform.position = ShipFrame.ToUnity((r.hinge[0][0] + r.hinge[1][0]) / 2, (r.hinge[0][1] + r.hinge[1][1]) / 2, r.hinge[0][2]);
                float len = (float)r.length_m, w = (float)r.width_m;
                Prim(ramp, "Plate", PrimitiveType.Cube, ramp.transform.position + new Vector3(-len / 2, -FloorThick / 2, 0), new Vector3(len, FloorThick, w), Color(0.5f, 0.5f, 0.45f), layer);
            }
            return ship;
        }

        /// Positive angle lifts the free (stern, -x) end. Hinge line is along Unity Z, so rotate about Z.
        public static void SetRampAngle(GameObject ship, double angleDeg)
        {
            var ramp = ship.transform.Find("Ramp"); if (!ramp) return;
            ramp.rotation = Quaternion.Euler(0, 0, (float)-angleDeg);
        }

        public static void SetDeckVisibility(GameObject ship, string deckIdOrAll)
        {
            foreach (Transform deck in ship.transform)
            {
                if (deck.name == "Ramp") continue;
                bool visible = deckIdOrAll == "all" || deck.name == deckIdOrAll;
                foreach (var r in deck.GetComponentsInChildren<Renderer>()) SetAlpha(r, visible ? 1f : 0.15f);
            }
        }

        static GameObject Child(GameObject parent, string name) { var g = new GameObject(name); g.transform.SetParent(parent.transform, false); return g; }

        static GameObject Prim(GameObject parent, string name, PrimitiveType t, Vector3 pos, Vector3 scale, Color c, int layer)
        {
            var g = GameObject.CreatePrimitive(t); g.name = name; g.layer = layer; g.transform.SetParent(parent.transform, false);
            g.transform.position = pos; g.transform.localScale = scale;
            var mat = new Material(Shader.Find("Standard")) { color = c }; g.GetComponent<Renderer>().sharedMaterial = mat;
            return g;
        }

        static Color Color(float r, float g, float b) => new Color(r, g, b, 1f);

        static void SetAlpha(Renderer r, float a)
        {
            var m = r.sharedMaterial; var c = m.color; c.a = a; m.color = c;
            if (a < 1f) { m.SetFloat("_Mode", 2); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha); m.SetInt("_ZWrite", 0); m.EnableKeyword("_ALPHABLEND_ON"); m.renderQueue = 3000; }
            else { m.SetFloat("_Mode", 0); m.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One); m.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero); m.SetInt("_ZWrite", 1); m.DisableKeyword("_ALPHABLEND_ON"); m.renderQueue = -1; }
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — `passed="22"`. 실패 시 확인 순서: (1) `Shader.Find("Standard")` 가 null 이면 Built-in RP 가 아닌 것 — `ProjectSettings/GraphicsSettings.asset` 확인, (2) 램프 부호는 `SetRampAngle(4)` 후 `bounds.min.x > -30` 이면 맞음.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/ShipHdMap/Runtime/Ship/ShipMeshBuilder.cs unity/Assets/ShipHdMap/Tests/EditMode/ShipMeshBuilderTests.cs
git commit -m "unity: ship mesh builder (decks, pillars, lashing, MEP, ramp)"
```

---

### Task 7: AprilTag 텍스처와 랜드마크 배치

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Landmarks/AprilTag36h11.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Landmarks/Landmark.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Landmarks/LandmarkPlacer.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/AprilTagTests.cs`

**Interfaces:**
- Produces:
  - `static class AprilTag36h11 { static readonly ulong[] Codes; static int Count => Codes.Length; static bool[,] Bits(int code) /* 8x8 incl. black border */; static Texture2D MakeTexture(int code, int pixelsPerCell = 8); }`
  - `class LandmarkMarker : MonoBehaviour { string id; int code; float sizeM = 0.3f; string deckId; string mountedOn; Vector3 NormalUnity => -transform.forward; Landmark ToModel(); static LandmarkMarker Spawn(Transform parent, string id, int code, Vector3 unityPos, Vector3 unityNormal, float sizeM, string deckId, string mountedOn); }` — 쿼드 하나(정면이 normal 을 향함), `BoxCollider` 두께 0.02, 레이어 `"Landmark"`(없으면 Default). `ToModel()` 은 DTO `Landmark` 를 돌려준다
  - `class LandmarkPlacer : MonoBehaviour { Camera cam; Transform landmarksRoot; int nextCode; List<LandmarkMarker> All; List<Deck> decks; LandmarkMarker PlaceAt(RaycastHit hit); event Action<LandmarkMarker> Created; event Action<string> Deleted; }` — 플레이 모드에서 좌클릭: `ShipStructure` 레이어 히트점에 표면 normal 방향으로 스폰(`id = "LM-{n:0000}"`, code 는 1부터 순서대로, `mounted_on` = 히트 오브젝트 이름, deckId 는 히트 y 로 가장 가까운 갑판). 우클릭: 랜드마크 히트 시 삭제. `Landmark` 레이어와 `ShipStructure` 레이어를 마스크로 구분

- [ ] **Step 1: 실패하는 테스트**

`AprilTagTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class AprilTagTests
    {
        [Test]
        public void HasTwentyDistinctCodes()
        {
            Assert.That(AprilTag36h11.Count, Is.EqualTo(20));
            var set = new System.Collections.Generic.HashSet<ulong>(AprilTag36h11.Codes);
            Assert.That(set.Count, Is.EqualTo(20));
            Assert.That(AprilTag36h11.Codes[0], Is.EqualTo(0xd5d628584UL)); // tag36h11 id 0
        }

        [Test]
        public void BitsHaveBlackBorderAndPayload()
        {
            var b = AprilTag36h11.Bits(0);
            Assert.That(b.GetLength(0), Is.EqualTo(8));
            for (int i = 0; i < 8; i++) { Assert.That(b[0, i], Is.False); Assert.That(b[7, i], Is.False); Assert.That(b[i, 0], Is.False); Assert.That(b[i, 7], Is.False); }
            int white = 0; for (int r = 1; r < 7; r++) for (int c = 1; c < 7; c++) if (b[r, c]) white++;
            Assert.That(white, Is.GreaterThan(0).And.LessThan(36));
        }

        [Test]
        public void TextureIsBlackAndWhiteOnly()
        {
            var t = AprilTag36h11.MakeTexture(3, 4);
            Assert.That(t.width, Is.EqualTo(32));
            foreach (var px in t.GetPixels()) Assert.That(px.r == 0f || px.r == 1f, px.ToString());
            Object.DestroyImmediate(t);
        }

        [Test]
        public void LandmarkSpawnAndModel()
        {
            var root = new GameObject("Landmarks");
            var lm = LandmarkMarker.Spawn(root.transform, "LM-0042", 42, ShipFrame.ToUnity(82.4, 3.1, 10.6), ShipFrame.ToUnity(0, -1, 0), 0.3f, "D3", "C-PILLAR-017");
            var m = lm.ToModel();
            Assert.That(m.id, Is.EqualTo("LM-0042"));
            Assert.That(m.position[0], Is.EqualTo(82.4).Within(1e-4));
            Assert.That(m.position[1], Is.EqualTo(3.1).Within(1e-4));
            Assert.That(m.normal, Is.EqualTo(new double[] { 0, -1, 0 }).Within(1e-4));
            Assert.That(m.marker.family, Is.EqualTo("apriltag-36h11"));
            Assert.That(lm.GetComponent<BoxCollider>(), Is.Not.Null);
            Object.DestroyImmediate(root);
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — 컴파일 오류.

- [ ] **Step 3: 구현**

`AprilTag36h11.cs` (코드표는 공개된 tag36h11 패밀리의 앞 20개. 36비트, MSB 가 좌상단 셀, 행 우선):

```csharp
using UnityEngine;

namespace ShipHdMap
{
    /// First 20 codes of the AprilTag tag36h11 family (public domain code table). Visual only; nothing decodes them.
    public static class AprilTag36h11
    {
        public static readonly ulong[] Codes =
        {
            0xd5d628584UL, 0xd97f18b49UL, 0xdd280910eUL, 0xe479e9c98UL, 0xebcbca822UL,
            0xf31dab3acUL, 0x056a5d085UL, 0x10652e1d4UL, 0x22b1dfeadUL, 0x265ad0472UL,
            0x34fe91b86UL, 0x3ff962cd5UL, 0x43a25329aUL, 0x474b4385fUL, 0x4e9d243e9UL,
            0x5246149aeUL, 0x5997f5538UL, 0x60b8ce8a4UL, 0x6d1c2f7d5UL, 0x7092ea5d3UL,
        };
        public static int Count => Codes.Length;

        /// 8x8 cells: 1-cell black border, 6x6 payload. true = white.
        public static bool[,] Bits(int code)
        {
            ulong v = Codes[code % Codes.Length];
            var b = new bool[8, 8];
            for (int r = 0; r < 6; r++) for (int c = 0; c < 6; c++)
            {
                int bit = 35 - (r * 6 + c);
                b[r + 1, c + 1] = ((v >> bit) & 1UL) == 1UL;
            }
            return b;
        }

        public static Texture2D MakeTexture(int code, int pixelsPerCell = 8)
        {
            var bits = Bits(code); int n = 8 * pixelsPerCell;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++) for (int x = 0; x < n; x++)
            {
                bool white = bits[7 - y / pixelsPerCell, x / pixelsPerCell]; // row 0 at top
                px[y * n + x] = white ? Color.white : Color.black;
            }
            t.SetPixels(px); t.Apply(false, false);
            return t;
        }
    }
}
```

`LandmarkMarker.cs` (파일명도 `LandmarkMarker.cs`. DTO `Landmark` 와 이름이 겹치지 않게 MonoBehaviour 는 `LandmarkMarker`):

```csharp
using UnityEngine;

namespace ShipHdMap
{
    /// Scene marker. Unity's Quad renders on its -Z face, so the quad is rotated with LookRotation(-normal)
    /// and the outward normal is -transform.forward.
    public class LandmarkMarker : MonoBehaviour
    {
        public string id; public int code; public float sizeM = 0.3f; public string deckId; public string mountedOn;
        public Vector3 NormalUnity => -transform.forward;

        public static LandmarkMarker Spawn(Transform parent, string id, int code, Vector3 unityPos, Vector3 unityNormal, float sizeM, string deckId, string mountedOn)
        {
            var n = unityNormal.normalized;
            var g = GameObject.CreatePrimitive(PrimitiveType.Quad); g.name = id;
            int layer = LayerMask.NameToLayer("Landmark"); if (layer >= 0) g.layer = layer;
            g.transform.SetParent(parent, false);
            g.transform.position = unityPos + n * 0.01f;
            g.transform.rotation = Quaternion.LookRotation(-n, Mathf.Abs(n.y) > 0.9f ? Vector3.forward : Vector3.up);
            g.transform.localScale = new Vector3(sizeM, sizeM, 1);
            Object.DestroyImmediate(g.GetComponent<Collider>());
            var box = g.AddComponent<BoxCollider>(); box.size = new Vector3(1, 1, 0.02f / sizeM);
            var shader = Shader.Find("Unlit/Texture") ?? Shader.Find("Standard");
            g.GetComponent<Renderer>().sharedMaterial = new Material(shader) { mainTexture = AprilTag36h11.MakeTexture(code) };
            var lm = g.AddComponent<LandmarkMarker>();
            lm.id = id; lm.code = code; lm.sizeM = sizeM; lm.deckId = deckId; lm.mountedOn = mountedOn;
            return lm;
        }

        /// DTO for vehicle-map / API. Position is the mounting point (marker centre pushed back onto the surface).
        public Landmark ToModel()
        {
            var (x, y, z) = ShipFrame.ToShip(transform.position - NormalUnity * 0.01f);
            var (nx, ny, nz) = ShipFrame.ToShip(NormalUnity);
            return new Landmark { id = id, marker = new Marker { family = "apriltag-36h11", code = code },
                position = new[] { x, y, z }, normal = new[] { nx, ny, nz }, size_m = sizeM, deck_id = deckId, mounted_on = mountedOn };
        }
    }
}
```

테스트 `LandmarkSpawnAndModel` 의 호출은 `LandmarkMarker.Spawn(root.transform, "LM-0042", 42, ShipFrame.ToUnity(82.4, 3.1, 10.6), ShipFrame.ToUnity(0, -1, 0), 0.3f, "D3", "C-PILLAR-017")` 로 쓴다 (normal 벡터도 같은 축 매핑을 거친다).

`LandmarkPlacer.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// Play-mode placement: left click on ship structure spawns a marker facing the surface normal; right click on a marker deletes it.
    public class LandmarkPlacer : MonoBehaviour
    {
        public Camera cam; public Transform landmarksRoot; public float sizeM = 0.3f;
        public int nextCode = 1; public bool enabledForInput = true;
        public List<LandmarkMarker> All = new();
        public List<Deck> decks = new();   // for deckId lookup by height
        public event Action<LandmarkMarker> Created; public event Action<string> Deleted;

        void Update()
        {
            if (!enabledForInput || cam == null) return;
            if (Input.GetMouseButtonDown(0)) TryPlace();
            if (Input.GetMouseButtonDown(1)) TryDelete();
        }

        public LandmarkMarker PlaceAt(RaycastHit hit)
        {
            string id = $"LM-{All.Count + 1:0000}";
            int code = nextCode++ % AprilTag36h11.Count;
            var lm = LandmarkMarker.Spawn(landmarksRoot, id, code, hit.point, hit.normal, sizeM, DeckIdForHeight(hit.point.y), hit.collider.name);
            All.Add(lm); Created?.Invoke(lm); return lm;
        }

        void TryPlace()
        {
            int mask = LayerMask.GetMask("ShipStructure"); if (mask == 0) mask = ~LayerMask.GetMask("Landmark");
            if (Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f, mask)) PlaceAt(hit);
        }

        void TryDelete()
        {
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out var hit, 500f)) return;
            var lm = hit.collider.GetComponent<LandmarkMarker>(); if (lm == null) return;
            All.Remove(lm); Deleted?.Invoke(lm.id); Destroy(lm.gameObject);
        }

        string DeckIdForHeight(float unityY)
        {
            string best = "D1"; double bestD = double.MaxValue;
            foreach (var d in decks) { double dd = Math.Abs(unityY - d.z_surface); if (unityY + 0.5 >= d.z_surface && dd < bestD) { bestD = dd; best = d.id; } }
            return best;
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — `passed="26"`. `TextureIsBlackAndWhiteOnly` 는 `Unlit/Texture` 셰이더가 없어도 통과해야 한다(텍스처만 검사). `LandmarkSpawnAndModel` 에서 normal 부호가 뒤집히면 `NormalUnity` 정의를 확인한다.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/ShipHdMap/Runtime/Landmarks unity/Assets/ShipHdMap/Tests/EditMode/AprilTagTests.cs
git commit -m "unity: AprilTag textures, landmark marker, click placement"
```

---

### Task 8: 감지 모델, 차량, HUD

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Localization/LandmarkSensor.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Vehicle/LaneFollower.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Vehicle/VehicleController.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Vehicle/LocalizationHud.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/SensorAndVehicleTests.cs`

**Interfaces:**
- Produces:
  - `class SensorNoise { double sigmaR = 0.2; double sigmaThetaRad = 1°; double sigmaAlphaRad = 2°; int seed = 1; }`
  - `static bool LandmarkSensor.IsVisibleGeometric(Pose2D vehicle, LandmarkRef lm, double fovRad, double maxDist, double maxViewAngleRad)` — 순수 함수. 조건: 거리 ≤ maxDist, |θ| ≤ fov/2, 마커 normal 과 (차량−마커) 방향의 각도 ≤ maxViewAngle
  - `static Observation LandmarkSensor.AddNoise(Observation o, SensorNoise n, System.Random rng)`
  - `class LandmarkSensor : MonoBehaviour { float fovDeg = 90; float maxDist = 25; float maxViewAngleDeg = 70; SensorNoise noise; LayerMask occluders; List<Observation> Sense(Pose2D truth, IDictionary<string, LandmarkRef> map, Func<string, Vector3> unityPosOf) }` — 기하 조건 통과 후 `Physics.Linecast(camPos, markerPos, occluders)` 가 막히면 제외, 통과하면 `AddNoise`
  - `static (Vector2 pos, double headingRad, bool end) LaneFollower.At(double[][] centerline, double s)` — 폴리라인 호장 s (m) 위치와 진행 방향. 끝을 넘으면 `end=true`
  - `class VehicleController : MonoBehaviour { double speedMps = 2.0; double[][] centerline; double deckZ; Pose2D Truth; bool running; void StartLane(Lane lane, double z); }` — `Update` 에서 s 를 전진시키고 `transform` 을 Ship→Unity 로 갱신 (`position = ToUnity(x, y, deckZ + 0.5)`, `rotation = Euler(0, UnityYawDeg(ψ), 0)`)
  - `class LocalizationHud : MonoBehaviour { LocalizerResult last; Pose2D truth; int frameLabel; void Set(LocalizerResult r, Pose2D truth, string frame); }` — `OnGUI` 로 좌하단 박스: `N`, `RMS`, `est x y ψ`, `true x y ψ`, `err m / deg`, `frame`

- [ ] **Step 1: 실패하는 테스트**

`SensorAndVehicleTests.cs`:

```csharp
using System;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class SensorAndVehicleTests
    {
        const double D = Math.PI / 180.0;

        [Test]
        public void VisibilityRespectsFovDistanceAndViewAngle()
        {
            var v = new Pose2D { x = 0, y = 0, psiRad = 0 };
            var ahead = new LandmarkRef { id = "a", mx = 10, my = 0, phiRad = Math.PI };      // faces the vehicle
            var behind = new LandmarkRef { id = "b", mx = -10, my = 0, phiRad = 0 };
            var far = new LandmarkRef { id = "f", mx = 30, my = 0, phiRad = Math.PI };
            var backFacing = new LandmarkRef { id = "k", mx = 10, my = 0, phiRad = 0 };       // normal points away
            var side = new LandmarkRef { id = "s", mx = 0.1, my = 10, phiRad = -Math.PI / 2 }; // bearing ~89.4deg, outside 90deg fov half-angle 45
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, ahead, 90 * D, 25, 70 * D), Is.True);
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, behind, 90 * D, 25, 70 * D), Is.False);
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, far, 90 * D, 25, 70 * D), Is.False);
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, backFacing, 90 * D, 25, 70 * D), Is.False);
            Assert.That(LandmarkSensor.IsVisibleGeometric(v, side, 90 * D, 25, 70 * D), Is.False);
        }

        [Test]
        public void NoiseIsDeterministicPerSeedAndZeroWhenSigmaZero()
        {
            var o = new Observation { id = "a", r = 10, thetaRad = 0.1, alphaRad = 0.2 };
            var n = new SensorNoise { sigmaR = 0.2, sigmaThetaRad = 1 * D, sigmaAlphaRad = 2 * D };
            var a = LandmarkSensor.AddNoise(o, n, new System.Random(5));
            var b = LandmarkSensor.AddNoise(o, n, new System.Random(5));
            Assert.That(a.r, Is.EqualTo(b.r));
            Assert.That(a.r, Is.Not.EqualTo(10));
            var z = LandmarkSensor.AddNoise(o, new SensorNoise { sigmaR = 0, sigmaThetaRad = 0, sigmaAlphaRad = 0 }, new System.Random(5));
            Assert.That(z.r, Is.EqualTo(10)); Assert.That(z.thetaRad, Is.EqualTo(0.1)); Assert.That(z.alphaRad, Is.EqualTo(0.2));
        }

        [Test]
        public void LaneFollowerWalksPolyline()
        {
            var line = new[] { new double[] { 0, 0, 10 }, new double[] { 10, 0, 10 }, new double[] { 10, 5, 10 } };
            var (p0, h0, e0) = LaneFollower.At(line, 0);
            Assert.That(p0, Is.EqualTo(new Vector2(0, 0))); Assert.That(h0, Is.EqualTo(0).Within(1e-9)); Assert.That(e0, Is.False);
            var (p1, h1, _) = LaneFollower.At(line, 12);
            Assert.That(p1.x, Is.EqualTo(10).Within(1e-6)); Assert.That(p1.y, Is.EqualTo(2).Within(1e-6));
            Assert.That(h1, Is.EqualTo(Math.PI / 2).Within(1e-9));   // heading +y after the corner
            var (p2, _, e2) = LaneFollower.At(line, 99);
            Assert.That(p2.y, Is.EqualTo(5).Within(1e-6)); Assert.That(e2, Is.True);
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — 컴파일 오류.

- [ ] **Step 3: 구현**

`LandmarkSensor.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    [Serializable]
    public class SensorNoise { public double sigmaR = 0.2; public double sigmaThetaRad = 1 * Math.PI / 180; public double sigmaAlphaRad = 2 * Math.PI / 180; public int seed = 1; }

    /// Detection model (spec §9.4). This imitates the OUTPUT of a tag detector; nothing here decodes images.
    public class LandmarkSensor : MonoBehaviour
    {
        public float fovDeg = 90, maxDist = 25, maxViewAngleDeg = 70;
        public SensorNoise noise = new();
        public LayerMask occluders;
        public float eyeHeight = 1.2f;
        System.Random _rng;

        void Awake() { _rng = new System.Random(noise.seed); }
        public void Reseed(int seed) { noise.seed = seed; _rng = new System.Random(seed); }

        public static bool IsVisibleGeometric(Pose2D v, LandmarkRef lm, double fovRad, double maxDist, double maxViewAngleRad)
        {
            double dx = lm.mx - v.x, dy = lm.my - v.y, r = Math.Sqrt(dx * dx + dy * dy);
            if (r > maxDist || r < 1e-6) return false;
            double theta = ShipFrame.WrapRad(Math.Atan2(dy, dx) - v.psiRad);
            if (Math.Abs(theta) > fovRad / 2) return false;
            // angle between marker normal and the direction marker -> vehicle
            double toV = Math.Atan2(-dy, -dx);
            return Math.Abs(ShipFrame.WrapRad(toV - lm.phiRad)) <= maxViewAngleRad;
        }

        public static Observation AddNoise(Observation o, SensorNoise n, System.Random rng)
        {
            return new Observation { id = o.id, r = o.r + Gauss(rng, n.sigmaR), thetaRad = ShipFrame.WrapRad(o.thetaRad + Gauss(rng, n.sigmaThetaRad)), alphaRad = ShipFrame.WrapRad(o.alphaRad + Gauss(rng, n.sigmaAlphaRad)) };
        }

        static double Gauss(System.Random rng, double sigma)
        {
            if (sigma <= 0) return 0;
            double u1 = 1.0 - rng.NextDouble(), u2 = rng.NextDouble();
            return sigma * Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
        }

        /// truth: vehicle pose; map: landmark refs; unityPosOf: scene position of a marker (for occlusion linecast).
        public List<Observation> Sense(Pose2D truth, IDictionary<string, LandmarkRef> map, Func<string, Vector3> unityPosOf)
        {
            var result = new List<Observation>();
            double fov = fovDeg * Math.PI / 180, mva = maxViewAngleDeg * Math.PI / 180;
            Vector3 eye = transform.position + Vector3.up * eyeHeight;
            foreach (var lm in map.Values)
            {
                if (!IsVisibleGeometric(truth, lm, fov, maxDist, mva)) continue;
                Vector3 target = unityPosOf(lm.id);
                if (Physics.Linecast(eye, target - (target - eye).normalized * 0.05f, occluders)) continue;
                result.Add(AddNoise(Localizer.Observe(truth, lm), noise, _rng ??= new System.Random(noise.seed)));
            }
            return result;
        }
    }
}
```

`LaneFollower.cs`:

```csharp
using System;
using UnityEngine;

namespace ShipHdMap
{
    public static class LaneFollower
    {
        /// Position (ship x,y) and heading at arc length s along a [x,y,z] polyline. end=true once s exceeds the total length.
        public static (Vector2 pos, double headingRad, bool end) At(double[][] line, double s)
        {
            double acc = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                double dx = line[i + 1][0] - line[i][0], dy = line[i + 1][1] - line[i][1], len = Math.Sqrt(dx * dx + dy * dy);
                double heading = Math.Atan2(dy, dx);
                if (s <= acc + len || i + 2 == line.Length)
                {
                    double t = len < 1e-9 ? 0 : Math.Min(1, Math.Max(0, (s - acc) / len));
                    var p = new Vector2((float)(line[i][0] + dx * t), (float)(line[i][1] + dy * t));
                    return (p, heading, s > acc + len);
                }
                acc += len;
            }
            return (new Vector2((float)line[0][0], (float)line[0][1]), 0, true);
        }
    }
}
```

`VehicleController.cs`:

```csharp
using UnityEngine;

namespace ShipHdMap
{
    public class VehicleController : MonoBehaviour
    {
        public double speedMps = 2.0;
        public double[][] centerline; public double deckZ; public double s;
        public bool running;
        public Pose2D Truth;

        public void StartLane(Lane lane, double z) { centerline = lane.centerline; deckZ = z; s = 0; running = true; Apply(); }

        void Update()
        {
            if (!running || centerline == null) return;
            s += speedMps * Time.deltaTime;
            var (_, _, end) = LaneFollower.At(centerline, s);
            if (end) running = false;
            Apply();
        }

        void Apply()
        {
            var (p, h, _) = LaneFollower.At(centerline, s);
            Truth = new Pose2D { x = p.x, y = p.y, psiRad = h };
            transform.position = ShipFrame.ToUnity(p.x, p.y, deckZ + 0.5);
            transform.rotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(h * 180 / System.Math.PI), 0);
        }
    }
}
```

`LocalizationHud.cs`:

```csharp
using System;
using UnityEngine;

namespace ShipHdMap
{
    public class LocalizationHud : MonoBehaviour
    {
        LocalizerResult _last; Pose2D _truth; string _frame = "SHIP_AP";
        public void Set(LocalizerResult r, Pose2D truth, string frame) { _last = r; _truth = truth; _frame = frame; }

        void OnGUI()
        {
            const double R2D = 180 / Math.PI;
            var box = new Rect(10, Screen.height - 130, 420, 120);
            GUI.Box(box, "Localization");
            if (_last == null) { GUI.Label(new Rect(20, box.y + 25, 400, 20), "no estimate yet"); return; }
            var e = _last.pose; var t = _truth;
            double err = Math.Sqrt((e.x - t.x) * (e.x - t.x) + (e.y - t.y) * (e.y - t.y));
            double herr = ShipFrame.WrapDeg((e.psiRad - t.psiRad) * R2D);
            string[] lines =
            {
                $"frame {_frame}   N {_last.nObs}   RMS {_last.residualRms:F3}   iter {_last.iterations}   {(_last.ok ? "" : "(holding previous)")}",
                $"est  x {e.x,7:F2}  y {e.y,7:F2}  psi {e.psiRad * R2D,7:F1}",
                $"true x {t.x,7:F2}  y {t.y,7:F2}  psi {t.psiRad * R2D,7:F1}",
                $"err  {err:F2} m   {herr:F1} deg",
            };
            for (int i = 0; i < lines.Length; i++) GUI.Label(new Rect(20, box.y + 25 + i * 22, 400, 20), lines[i]);
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — `passed="29"`.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/ShipHdMap/Runtime/Localization/LandmarkSensor.cs unity/Assets/ShipHdMap/Runtime/Vehicle unity/Assets/ShipHdMap/Tests/EditMode/SensorAndVehicleTests.cs
git commit -m "unity: landmark sensor model, lane-following vehicle, localization HUD"
```

---

### Task 9: 브리지 메시지, MapRuntime, 에디터 스텁 창, 픽스처 내보내기

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs`
- Create: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Create: `unity/Assets/ShipHdMap/Editor/BridgeStubWindow.cs`
- Create: `unity/Assets/ShipHdMap/Editor/FixtureExporter.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs`

**Interfaces:**
- Produces:
  - `static class BridgeMessages { const string Load = "Load", SetMode = "SetMode", SetDeck = "SetDeck", Select = "Select", Confirm = "Confirm", SetPose = "SetPose", SetNoise = "SetNoise", StartScenario = "StartScenario"; const string OnSeedReady = "onSeedReady", OnFeatureCreated = "onFeatureCreated", OnFeatureMoved = "onFeatureMoved", OnSelected = "onSelected", OnSlotFilled = "onSlotFilled", OnLocalization = "onLocalization"; }`
  - 페이로드 DTO: `class FeatureCreatedEvt { string tempId; string layer; double x, y, z; string deck; }`, `class LocalizationEvt { double est_x, est_y, est_psi, true_x, true_y, true_psi, residual_rms; int n_obs; string frame; }`, `class SetNoiseMsg { double sigma_r, sigma_theta, sigma_alpha, sigma_gps; }`, `class StartScenarioMsg { VehicleMap map; string mode; }` (pose 는 M5)
  - `class MapRuntime : MonoBehaviour` — GameObject 이름 `"Map"`. React 의 `sendMessage("Map", "<name>", json)` 이 그대로 호출할 public 메서드: `Load(string json)`, `SetMode(string mode)`, `SetDeck(string deck)`, `Select(string id)`, `Confirm(string json)`, `SetNoise(string json)`, `StartScenario(string json)`. 송신은 `event Action<string name, string json> Emit` 하나로 모아서, WebGL 에서는 M3 에서 `.jslib` 로 연결하고 에디터에서는 스텁 창이 구독한다
  - `Load`: `VehicleMap` 파싱 → 기존 랜드마크 삭제 후 `LandmarkMarker.Spawn` 으로 재생성, `_map` 딕셔너리(`LandmarkRef`) 갱신, `placer.decks` 설정
  - `SetMode("edit")`: placer 입력 켜고 차량 정지. `SetMode("drive")`: placer 입력 끄고 `StartScenario` 대기
  - `StartScenario`: `map.lanes` 중 첫 갑판 차로로 `vehicle.StartLane(lane, deck.z_surface)`. 매 `Update` 에서 drive 모드면 `sensor.Sense` → `Localizer.Solve` → HUD 갱신 → 0.2 초마다 `OnLocalization` 송신
  - `Awake` 에서 씬에 `Ship` 이 없으면 `ShipSeedBuilder.Build(params)` + `ShipMeshBuilder.Build` 로 생성하고 `OnSeedReady` 송신
  - `BridgeStubWindow : EditorWindow` (메뉴 `ShipHdMap/Bridge Stub`): 버튼 `Load fixture`(docs/fixtures 파일 읽어 `Load`), `Mode edit / drive`, `Deck D1/D2/D3/all`, `Start load scenario`, 슬라이더 σ_r σ_θ σ_α → `SetNoise`, 수신 이벤트 최근 20줄 표시. 플레이 모드에서만 동작
  - `FixtureExporter` (메뉴 `ShipHdMap/Export vehicle-map fixture`): 플레이 모드에서 현재 씬의 생성기 시드 + 배치된 랜드마크 + 픽스처의 parking_slots 를 합쳐 `docs/fixtures/vehicle-map.sample.json` 에 덮어쓴다. Task 3 의 손으로 쓴 픽스처를 생성기 출력으로 갈아끼우는 용도

- [ ] **Step 1: 실패하는 테스트**

`MapRuntimeTests.cs` (MonoBehaviour 지만 `Load` 는 Physics/Update 없이 동작하므로 EditMode 에서 검증 가능):

```csharp
using System.IO;
using NUnit.Framework;
using UnityEngine;

namespace ShipHdMap.Tests
{
    public class MapRuntimeTests
    {
        GameObject go;
        [TearDown] public void Cleanup() { if (go) Object.DestroyImmediate(go); }

        static string Fixture() => File.ReadAllText(Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json")));

        [Test]
        public void LoadSpawnsLandmarksAndBuildsMap()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>();
            rt.InitForTest();
            string emitted = null; rt.Emit += (n, j) => { if (n == BridgeMessages.OnSeedReady) emitted = j; };
            rt.Load(Fixture());
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(3));
            Assert.That(rt.MapRefs.ContainsKey("LM-0003"));
            Assert.That(rt.MapRefs["LM-0001"].phiRad, Is.EqualTo(System.Math.PI / 2).Within(1e-6)); // normal +y
            Assert.That(rt.CurrentMap.parking_slots.Count, Is.EqualTo(2));
        }

        [Test]
        public void LoadTwiceReplacesLandmarks()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(Fixture()); rt.Load(Fixture());
            Assert.That(rt.LandmarksRoot.childCount, Is.EqualTo(3));
        }

        [Test]
        public void SetNoiseParsesAndApplies()
        {
            go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.SetNoise("{\"sigma_r\":0.5,\"sigma_theta\":2,\"sigma_alpha\":3,\"sigma_gps\":0.5}");
            Assert.That(rt.Sensor.noise.sigmaR, Is.EqualTo(0.5));
            Assert.That(rt.Sensor.noise.sigmaThetaRad, Is.EqualTo(2 * System.Math.PI / 180).Within(1e-9));
        }
    }
}
```

- [ ] **Step 2: 실패 확인** — 컴파일 오류.

- [ ] **Step 3: 구현**

`BridgeMessages.cs`:

```csharp
namespace ShipHdMap
{
    public static class BridgeMessages
    {
        public const string Load = "Load", SetMode = "SetMode", SetDeck = "SetDeck", Select = "Select", Confirm = "Confirm",
            SetPose = "SetPose", SetNoise = "SetNoise", StartScenario = "StartScenario";
        public const string OnSeedReady = "onSeedReady", OnFeatureCreated = "onFeatureCreated", OnFeatureMoved = "onFeatureMoved",
            OnSelected = "onSelected", OnSlotFilled = "onSlotFilled", OnLocalization = "onLocalization";
    }
    public class FeatureCreatedEvt { public string tempId; public string layer; public double x, y, z; public string deck; }
    public class LocalizationEvt { public double est_x, est_y, est_psi, true_x, true_y, true_psi, residual_rms; public int n_obs; public string frame; }
    public class SetNoiseMsg { public double sigma_r = 0.2, sigma_theta = 1, sigma_alpha = 2, sigma_gps = 0.5; } // angles in degrees on the wire
    public class ConfirmMsg { public string tempId; public string id; }
    public class StartScenarioMsg { public VehicleMap map; public string mode; }
}
```

`MapRuntime.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShipHdMap
{
    /// GameObject "Map". React calls these via react-unity-webgl sendMessage("Map", name, json). Outgoing events go through Emit.
    public class MapRuntime : MonoBehaviour
    {
        public ShipParams shipParams = new();
        public Camera cam;
        public event Action<string, string> Emit;

        public Transform LandmarksRoot { get; private set; }
        public Dictionary<string, LandmarkRef> MapRefs { get; } = new();
        public VehicleMap CurrentMap { get; private set; }
        public LandmarkSensor Sensor { get; private set; }
        public VehicleController Vehicle { get; private set; }
        public LandmarkPlacer Placer { get; private set; }
        public LocalizationHud Hud { get; private set; }
        public GameObject Ship { get; private set; }

        string _mode = "edit"; Pose2D? _prev; float _emitTimer; SeedData _seed;
        readonly Dictionary<string, LandmarkMarker> _markers = new();

        void Awake()
        {
            InitForTest();
            Ship = GameObject.Find("Ship");
            if (Ship == null) { _seed = ShipSeedBuilder.Build(shipParams); Ship = ShipMeshBuilder.Build(_seed, shipParams); }
            Placer.decks = _seed?.decks ?? new List<Deck>();
            if (cam == null) cam = Camera.main; Placer.cam = cam;
            Placer.Created += lm => { _markers[lm.id] = lm; MapRefs[lm.id] = RefOf(lm.ToModel());
                var (x, y, z) = ShipFrame.ToShip(lm.transform.position);
                Send(BridgeMessages.OnFeatureCreated, MapJson.Serialize(new FeatureCreatedEvt { tempId = lm.id, layer = "LM", x = x, y = y, z = z, deck = lm.deckId })); };
            Placer.Deleted += id => { _markers.Remove(id); MapRefs.Remove(id); };
            if (_seed != null) Send(BridgeMessages.OnSeedReady, MapJson.Serialize(_seed));
        }

        /// Creates child objects without touching the scene ship or camera; used by Awake and by EditMode tests.
        public void InitForTest()
        {
            if (LandmarksRoot != null) return;
            LandmarksRoot = new GameObject("Landmarks").transform; LandmarksRoot.SetParent(transform, false);
            Sensor = new GameObject("Vehicle").AddComponent<LandmarkSensor>(); Sensor.transform.SetParent(transform, false);
            Sensor.occluders = LayerMask.GetMask("ShipStructure"); if (Sensor.occluders == 0) Sensor.occluders = ~LayerMask.GetMask("Landmark");
            Vehicle = Sensor.gameObject.AddComponent<VehicleController>();
            var body = GameObject.CreatePrimitive(PrimitiveType.Cube); body.name = "Body"; body.transform.SetParent(Sensor.transform, false);
            body.transform.localScale = new Vector3(4.8f, 1.5f, 1.85f); body.transform.localPosition = new Vector3(0, 0.25f, 0);
            UnityEngine.Object.DestroyImmediate(body.GetComponent<Collider>());
            Placer = gameObject.AddComponent<LandmarkPlacer>(); Placer.landmarksRoot = LandmarksRoot;
            Hud = gameObject.AddComponent<LocalizationHud>();
        }

        // ---- incoming (React -> Unity) ----
        public void Load(string json)
        {
            CurrentMap = MapJson.Parse<VehicleMap>(json);
            foreach (var m in _markers.Values) if (m) DestroyImmediate(m.gameObject);
            _markers.Clear(); MapRefs.Clear(); Placer.All.Clear();
            foreach (var lm in CurrentMap.landmarks ?? new List<Landmark>())
            {
                var mk = LandmarkMarker.Spawn(LandmarksRoot, lm.id, lm.marker.code, ShipFrame.ToUnity(lm.position[0], lm.position[1], lm.position[2]),
                    ShipFrame.ToUnity(lm.normal[0], lm.normal[1], lm.normal[2]), (float)lm.size_m, lm.deck_id, lm.mounted_on);
                _markers[lm.id] = mk; MapRefs[lm.id] = RefOf(lm); Placer.All.Add(mk);
            }
            Placer.decks = CurrentMap.decks ?? Placer.decks;
            Placer.nextCode = _markers.Count + 1;
        }

        public void SetMode(string mode) { _mode = mode; Placer.enabledForInput = mode == "edit"; if (mode == "edit") Vehicle.running = false; }
        public void SetDeck(string deck) { if (Ship) ShipMeshBuilder.SetDeckVisibility(Ship, deck); }
        public void Select(string id) { Send(BridgeMessages.OnSelected, "{\"id\":\"" + id + "\"}"); }
        public void Confirm(string json) { var c = MapJson.Parse<ConfirmMsg>(json); if (_markers.TryGetValue(c.tempId, out var m)) { _markers.Remove(c.tempId); m.id = c.id; m.name = c.id; _markers[c.id] = m; MapRefs[c.id] = MapRefs[c.tempId]; MapRefs.Remove(c.tempId); } }
        public void SetPose(string json) { /* M5 */ }

        public void SetNoise(string json)
        {
            var n = MapJson.Parse<SetNoiseMsg>(json);
            Sensor.noise.sigmaR = n.sigma_r; Sensor.noise.sigmaThetaRad = n.sigma_theta * Math.PI / 180; Sensor.noise.sigmaAlphaRad = n.sigma_alpha * Math.PI / 180;
        }

        public void StartScenario(string json)
        {
            var s = MapJson.Parse<StartScenarioMsg>(json); if (s.map != null) Load(MapJson.Serialize(s.map));
            var lane = CurrentMap.lanes[0]; var deck = CurrentMap.decks.Find(d => d.id == lane.deck_id);
            _prev = null; SetMode("drive"); Vehicle.StartLane(lane, deck.z_surface);
        }

        // ---- per frame ----
        void Update()
        {
            if (_mode != "drive" || !Vehicle.running) return;
            var obs = Sensor.Sense(Vehicle.Truth, MapRefs, id => _markers[id].transform.position);
            var res = Localizer.Solve(obs, MapRefs, Sensor.noise.sigmaR, Sensor.noise.sigmaThetaRad, Sensor.noise.sigmaAlphaRad, _prev);
            if (res.ok) _prev = res.pose;
            Hud.Set(res, Vehicle.Truth, "SHIP_AP");
            _emitTimer += Time.deltaTime;
            if (_emitTimer >= 0.2f)
            {
                _emitTimer = 0;
                Send(BridgeMessages.OnLocalization, MapJson.Serialize(new LocalizationEvt { est_x = res.pose.x, est_y = res.pose.y, est_psi = res.pose.psiRad * 180 / Math.PI,
                    true_x = Vehicle.Truth.x, true_y = Vehicle.Truth.y, true_psi = Vehicle.Truth.psiRad * 180 / Math.PI, residual_rms = res.residualRms, n_obs = res.nObs, frame = "SHIP_AP" }));
            }
        }

        static LandmarkRef RefOf(Landmark lm) => new LandmarkRef { id = lm.id, mx = lm.position[0], my = lm.position[1], phiRad = Math.Atan2(lm.normal[1], lm.normal[0]) };
        void Send(string name, string json) => Emit?.Invoke(name, json);

        public SeedData Seed => _seed;
        public IEnumerable<LandmarkMarker> Markers => _markers.Values;
    }
}
```

`BridgeStubWindow.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShipHdMap.Editor
{
    /// Stands in for the React shell while there is no web/ yet. Play mode only.
    public class BridgeStubWindow : EditorWindow
    {
        [MenuItem("ShipHdMap/Bridge Stub")] static void Open() => GetWindow<BridgeStubWindow>("Bridge Stub");

        readonly List<string> _log = new(); MapRuntime _rt; float _sr = 0.2f, _st = 1f, _sa = 2f;

        MapRuntime Runtime()
        {
            if (_rt != null) return _rt;
            _rt = FindFirstObjectByType<MapRuntime>();
            if (_rt != null) _rt.Emit += (n, j) => { _log.Insert(0, $"{n}: {(j.Length > 160 ? j.Substring(0, 160) + "…" : j)}"); if (_log.Count > 20) _log.RemoveAt(20); Repaint(); };
            return _rt;
        }

        void OnGUI()
        {
            if (!Application.isPlaying) { EditorGUILayout.HelpBox("Enter Play mode.", MessageType.Info); _rt = null; return; }
            var rt = Runtime(); if (rt == null) { EditorGUILayout.HelpBox("No MapRuntime in scene (GameObject 'Map').", MessageType.Warning); return; }
            if (GUILayout.Button("Load fixture")) rt.Load(File.ReadAllText(FixturePath()));
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Mode edit")) rt.SetMode("edit");
                if (GUILayout.Button("Mode drive")) rt.SetMode("drive");
                if (GUILayout.Button("Start load scenario")) rt.StartScenario("{\"mode\":\"load\"}");
            }
            using (new EditorGUILayout.HorizontalScope())
                foreach (var d in new[] { "D1", "D2", "D3", "all" }) if (GUILayout.Button(d)) rt.SetDeck(d);
            EditorGUI.BeginChangeCheck();
            _sr = EditorGUILayout.Slider("sigma r (m)", _sr, 0, 1); _st = EditorGUILayout.Slider("sigma theta (deg)", _st, 0, 5); _sa = EditorGUILayout.Slider("sigma alpha (deg)", _sa, 0, 10);
            if (EditorGUI.EndChangeCheck()) rt.SetNoise($"{{\"sigma_r\":{_sr},\"sigma_theta\":{_st},\"sigma_alpha\":{_sa},\"sigma_gps\":0.5}}");
            EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
            foreach (var l in _log) EditorGUILayout.LabelField(l, EditorStyles.miniLabel);
        }

        public static string FixturePath() => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "docs", "fixtures", "vehicle-map.sample.json"));
    }
}
```

`FixtureExporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShipHdMap.Editor
{
    public static class FixtureExporter
    {
        [MenuItem("ShipHdMap/Export vehicle-map fixture")]
        static void Export()
        {
            var rt = UnityEngine.Object.FindFirstObjectByType<MapRuntime>();
            if (rt == null || !Application.isPlaying) { Debug.LogWarning("Enter Play mode with a Map in the scene first."); return; }
            var seed = rt.Seed ?? ShipSeedBuilder.Build(rt.shipParams);
            var existing = File.Exists(BridgeStubWindow.FixturePath()) ? MapJson.Parse<VehicleMap>(File.ReadAllText(BridgeStubWindow.FixturePath())) : new VehicleMap();
            var map = new VehicleMap
            {
                schema = "ship-hdmap/vehicle-map/1.0", map_id = "roro-demo-01", version = (existing.version) + 1, generated_at = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                frame = existing.frame ?? new FrameInfo { name = "SHIP_AP", origin = "AP x Baseline x Centerline", axes = new Dictionary<string, string> { ["x"] = "AP->bow (+)", ["y"] = "port (+)", ["z"] = "baseline->up (+)" }, unit = "m" },
                decks = seed.decks, lanes = seed.lanes, ramps = seed.ramps,
                facilities = seed.facilities.Where(f => f.deck_id == "D3").ToList(),            // keep the fixture small
                lashing_points = seed.lashing_points.Where(l => l.deck_id == "D3" && l.position[0] >= 94 && l.position[0] <= 105 && l.position[1] >= 2 && l.position[1] <= 4).ToList(),
                landmarks = rt.Markers.Select(m => m.ToModel()).OrderBy(l => l.id).ToList(),
                parking_slots = existing.parking_slots ?? new List<ParkingSlot>(), markings = new List<Marking>(),
            };
            File.WriteAllText(BridgeStubWindow.FixturePath(), MapJson.Serialize(map));
            Debug.Log($"Fixture written: {BridgeStubWindow.FixturePath()} (landmarks {map.landmarks.Count})");
        }
    }
}
```

- [ ] **Step 4: 통과 확인** — `passed="32"`. `LoadSpawnsLandmarksAndBuildsMap` 에서 `phiRad` 가 −π/2 로 나오면 `Load` 가 normal 을 Unity 로 바꾼 뒤 `RefOf` 에 Unity 벡터를 넘긴 것이다. `RefOf` 는 DTO(Ship Frame)만 받는다.

- [ ] **Step 5: Commit**

```bash
git add unity/Assets/ShipHdMap/Runtime/Bridge unity/Assets/ShipHdMap/Editor unity/Assets/ShipHdMap/Tests/EditMode/MapRuntimeTests.cs
git commit -m "unity: bridge messages, MapRuntime, editor bridge stub, fixture exporter"
```

---

### Task 10: 데모 씬, 레이어, 수동 검증, 픽스처 갱신

**Files:**
- Create: `unity/Assets/Scenes/Demo.unity`
- Modify: `unity/ProjectSettings/TagManager.asset` (레이어 8 `ShipStructure`, 9 `Landmark`)
- Modify: `unity/ProjectSettings/EditorBuildSettings.asset` (Demo 씬 등록)
- Modify: `docs/fixtures/vehicle-map.sample.json` (FixtureExporter 로 재생성)

**Interfaces:**
- Consumes: Task 9 의 `MapRuntime`, Task 6 의 레이어 이름
- Produces: 열면 바로 동작하는 씬. M3 의 WebGL 빌드 대상

- [ ] **Step 1: 레이어 추가**

`unity/ProjectSettings/TagManager.asset` 의 `layers:` 목록에서 8번째(인덱스 8)와 9번째 항목을 바꾼다. 파일은 YAML 이고 `- ` 로 시작하는 32줄이 순서대로 레이어 0~31 이다:

```yaml
  layers:
  - Default
  - TransparentFX
  - Ignore Raycast
  - 
  - Water
  - UI
  - 
  - 
  - ShipStructure
  - Landmark
  - 
  ...
```

에디터가 열려 있으면 파일 대신 Edit > Project Settings > Tags and Layers 에서 User Layer 8 = `ShipStructure`, 9 = `Landmark`.

- [ ] **Step 2: 씬 구성 (에디터 GUI 또는 MCP `manage_scene`/`manage_gameobject`)**

1. 새 씬 저장 `Assets/Scenes/Demo.unity`
2. `Main Camera`: position (60, 25, −40), rotation (30, −20, 0), Far 500. 태그 MainCamera 유지
3. `Directional Light` 기본값
4. 빈 GameObject `Map` 에 `MapRuntime` 추가, `cam` 에 Main Camera 연결. `shipParams` 는 기본값
5. File > Build Profiles(또는 Build Settings)에 Demo 씬 추가

- [ ] **Step 3: 수동 검증 체크리스트 (플레이 모드)**

1. ▶ 재생 → 콘솔에 오류 없음, Hierarchy 에 `Ship/D1..D3`, `Ship/Ramp`, `Map/Landmarks`, `Map/Vehicle`
2. `ShipHdMap/Bridge Stub` 창 열기 → 이벤트 목록에 `onSeedReady` 1건
3. `D3` 버튼 → D1·D2 가 반투명
4. Scene 뷰가 아닌 **Game 뷰**에서 기둥 옆면을 좌클릭 → AprilTag 쿼드가 기둥 면에 붙고 스텁 창에 `onFeatureCreated` 가 뜸. 우클릭으로 삭제
5. `Load fixture` → 랜드마크 3개(LM-0001·0002 는 선미 기둥, LM-0003 은 좌현 벽) 표시
6. `Start load scenario` → 차량 큐브가 x=2 에서 선수 쪽으로 이동, HUD 에 `N`, `err` 표시. 램프 입구 두 마커가 보이는 구간은 N=2, 이후 LM-0003 근처에서 N≥1, 그 밖은 `(holding previous)`
7. σ 슬라이더를 0 으로 → err 이 0.00 m 에 수렴. σ_r 1.0 → err 증가
8. `Mode edit` → 차량 정지, 클릭 배치 다시 가능

- [ ] **Step 4: 픽스처를 생성기 출력으로 갱신**

플레이 모드에서 `Load fixture` 후 갑판 3 에 마커를 5~6개 더 붙인다(차로 양쪽 기둥 면에 10 m 간격). 그다음 `ShipHdMap/Export vehicle-map fixture`. `git diff docs/fixtures/vehicle-map.sample.json` 으로 `landmarks` 가 늘고 `decks/lanes/ramps` 가 생성기 값과 같은지 확인. 그 뒤 EditMode 테스트 전체 재실행(픽스처 기반 테스트 `MapModelTests.FixtureParses` 의 `landmarks.Count == 3` 단언을 실제 개수로 고친다).

- [ ] **Step 5: 전체 테스트 + 민감 용어 검사**

```bash
cd unity && "$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"; echo EXIT=$?
grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' Logs/editmode-results.xml | head -1
cd .. && grep -rn -I -f ~/ship-hdmap-private/blocklist.txt unity/Assets docs README.md || echo "sensitive scan: none"
```

Expected: `failed="0"`, `sensitive scan: none`.

- [ ] **Step 6: Commit**

```bash
git add unity/Assets/Scenes unity/ProjectSettings/TagManager.asset unity/ProjectSettings/EditorBuildSettings.asset docs/fixtures/vehicle-map.sample.json unity/Assets/ShipHdMap/Tests/EditMode/MapModelTests.cs
git commit -m "unity: demo scene, layers, regenerated vehicle-map fixture"
```

---

## M1 완료 보고 형식

스펙 13장 규칙대로 (1) 한 것 (2) 결정과 이유 (3) 검증 결과(테스트 수, 수동 체크리스트 결과) (4) 직접 확인 명령(테스트 CLI, 씬 여는 법, 스텁 창 사용법) 순으로 보고하고 M2 는 확인 후 시작한다.

## 자체 검토 기록

- 스펙 커버리지(M1 범위): 3.1·3.3 ShipFrame(Task 2), 5.1 레이어 중 LM·LP·C 기둥·램프·A2 차로(Task 5·7), 6 vehicle-map DTO·픽스처(Task 3), 9.4 감지 모델·폐형해·Gauss-Newton·HUD(Task 4·8), 10 브리지 메시지 중 Load/SetMode/SetDeck/Select/Confirm/SetNoise/StartScenario 와 onSeedReady/onFeatureCreated/onSelected/onLocalization(Task 9), 2.1 생성기·편집 모드·주행 모드 최소본(Task 6·7·8·9), 11 C# 테스트(전 Task), 13 M1 산출물(Task 3·10). `SetPose`·`onSlotFilled`·`onFeatureMoved`·부두 프레임·램프 각도 파생은 M5 로 미룸(스펙 13 표와 일치)
- 이름 일관성: MonoBehaviour 는 `LandmarkMarker`, DTO 는 `Landmark`. `LandmarkRef.phiRad` 는 항상 Ship Frame normal 에서 계산. 각도는 코어에서 라디안, 브리지 JSON 에서 도
- 알려진 단순화(`ponytail:` 표시 대상): Cramer 3×3 역행렬, 갑판당 래싱 소켓 2000개 상한, 추측항법 없음(N=0 이면 유지)
