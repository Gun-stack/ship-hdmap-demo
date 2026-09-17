# M5b 부두·프레임 전환 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 부두에서 GPS 로 출발한 차량이 램프를 올라오며 입구 랜드마크 쌍으로 Ship Frame 으로 전환해 갑판 구획에 주차하고, 하역은 역순으로 부두로 나간다. 선체 메시는 `ShipParams` 가 아니라 vehicle-map 으로 짓는다.

**Architecture:** 월드(Unity) 좌표가 Quay Frame 이고 수면이 `y = 0` 이다. Map 루트는 pose 로 회전(M5a)하고 이제 `y = −draft_aft` 로 내려간다. 부두 슬래브는 월드에 있고 배와 함께 움직이지 않는다. 차량은 부두 구간에서는 씬 루트의 자식(Quay Frame), 전환 후에는 Map 루트의 자식(Ship Frame)이며, 전환 순간 `SetParent(..., worldPositionStays: true)` 로 옮겨 붙는다. 믿음 좌표계에서 경로를 짜고 `ScenarioPlanner.ToTruthFrame` 으로 실제 좌표계에 옮겨 개루프로 달리는 M5a 의 구조를 부두 구간에도 그대로 쓴다.

**Tech Stack:** Unity 6000.3.24f1 (Built-in RP, EditMode NUnit), React 19 + TypeScript + Zustand + Vitest, Spring Boot 4.1 (이번 마일스톤에서 API 코드 변경 없음)

**Spec:** `docs/superpowers/specs/2026-09-17-m5b-quay-frame-design.md` (기반: `docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md`)

## Global Constraints

- **공개 저장소**: 기관·회사·과제명·인물명, 개인 경력을 드러내는 표현을 저장소 안 어떤 파일(코드·주석·문서·커밋 메시지)에도 쓰지 않는다. 사용자 계정명이 들어간 절대경로도 쓰지 않는다. 정박 좌표는 임의 값이다
- **좌표계**: Ship Frame x 선수(+), y 좌현(+), z 상방(+). `ShipFrame.ToUnity(x,y,z) => (x, z, −y)`, 역은 `ShipFrame.ToShip(Vector3)`
- **프레임**: Map 루트의 **로컬 = Ship Frame**, **월드 = Quay Frame**. 월드 `y = 0` 이 수면이다
- **pose 부호**: `trim_deg = atan((draft_aft − draft_fwd) / lpp)`, 양수면 선수가 올라간다. `heel_deg` 양수는 우현(Unity +z)이 내려간다. `PoseRotation = Quaternion.Euler(heel, 0, trim)`
- **램프 각도는 API 가 준다**: `angle = asin(((quay_z + tide) − (hinge_z − draft_aft)) / length)`. Unity 는 이 값을 `SetPose.ramp.angle_deg` 로 받아 쓰기만 하고 다시 계산하지 않는다
- **부두는 지도가 아니다**: DB·vehicle-map·레이어 트리·미니맵에 넣지 않는다. Unity 가 pose 로 그린다
- **접근 경로 상수**: `ScenarioPlanner.FinalRunM = 5.0` 은 `SlotGenerator.FINAL_RUN_M` 과 같은 값이어야 한다. 이 계획에서 바꾸지 않는다
- **테스트 명령**
  - Unity EditMode: `cd unity && "$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"` (`U` 는 Unity 6000.3.24f1 실행 파일. **`-quit` 를 붙이면 테스트가 조용히 건너뛰어진다**)
  - api: `cd api && ./gradlew test`
  - web: `cd web && pnpm vitest run`
- **현재 기준선**: Unity EditMode 81, api 46, Vitest 26 전부 통과

## File Structure

| 파일 | 책임 |
| --- | --- |
| `unity/Assets/ShipHdMap/Runtime/Ship/QuayBuilder.cs` (신규) | 부두 슬래브 생성과 높이 갱신. 월드 좌표 전용 |
| `unity/Assets/ShipHdMap/Runtime/Vehicle/LaneFollower.cs` | 경로 위 자세를 `PathPose`(x·y·z·heading·pitch·end)로 반환 |
| `unity/Assets/ShipHdMap/Runtime/Vehicle/VehicleController.cs` | 점별 z 와 피치 반영, 경로당 z 인자 제거 |
| `unity/Assets/ShipHdMap/Runtime/Vehicle/ScenarioPlanner.cs` | 부두 경로·램프 상단 경로·하역 경로 기하 추가 |
| `unity/Assets/ShipHdMap/Runtime/Localization/LandmarkSensor.cs` | GPS 관측(`Gps`)과 `sigmaGps` |
| `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs` | 루트 z, 부두, 상태기계 확장, 프레임 전환, 선체 재구성 |
| `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs` | `SetPoseMsg` 에 `tide_m`·`quay_z_m` |
| `unity/Assets/ShipHdMap/Runtime/Ship/ShipMeshBuilder.cs` | 입력을 `SeedData` → `VehicleMap` 으로 |
| `unity/Assets/ShipHdMap/Editor/FixtureExporter.cs` | 입구 랜드마크 쌍 유도, 전 갑판 기둥 내보내기 |
| `web/src/components/PosePanel.tsx` | `quay_z_m` 슬라이더 |
| `web/src/bridge/useShipUnity.ts`, `web/src/api/types.ts`, `web/src/store/editor.ts` | `SetPose` 필드 추가, `frame_switch` 로그 |

---

### Task 1: pose 의 z 와 부두 슬래브

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Ship/QuayBuilder.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs:23`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs` (`InitForTest`, `ApplyPose`)
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/PoseTests.cs`

**Interfaces:**
- Consumes: `SetPoseMsg`(기존 `draft_fwd_m`, `draft_aft_m`, `heel_deg`, `lpp_m`, `ramp`)
- Produces: `QuayBuilder.Build()`, `QuayBuilder.SetHeight(GameObject, double surfaceZ)`, `QuayBuilder.SurfaceZ(GameObject)`, `MapRuntime.Quay`, `SetPoseMsg.tide_m`, `SetPoseMsg.quay_z_m`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/PoseTests.cs` 에 추가한다(파일 상단 `using` 에 `System` 이 없으면 추가):

```csharp
        [Test]
        public void PoseSinksTheRootByAftDraftAndPutsTheQuayAtQuayZPlusTide()
        {
            var go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.SetPose("{\"draft_fwd_m\":8.1,\"draft_aft_m\":8.6,\"heel_deg\":0,\"lpp_m\":120,\"tide_m\":0.4,\"quay_z_m\":3.5}");
            Assert.That(rt.transform.localPosition.y, Is.EqualTo(-8.6f).Within(1e-4f));     // waterline is world y = 0
            Assert.That(QuayBuilder.SurfaceZ(rt.Quay), Is.EqualTo(3.9).Within(1e-4));       // quay_z + tide
            Assert.That(rt.Quay.transform.parent, Is.Null, "the quay must not ride on the Map root");
            Assert.That(rt.Quay.GetComponent<Collider>(), Is.Null, "the quay must not catch placement raycasts");
            Object.DestroyImmediate(rt.Quay); Object.DestroyImmediate(go);
        }

        [Test]
        public void RampFreeEndLandsOnTheQuaySurface()
        {
            // The identity behind the whole layout: angle = asin(((quay_z + tide) - (hinge_z - draft_aft)) / length)
            // puts the ramp's free end exactly on the quay surface, so nothing has to be nudged to make them meet.
            var go = new GameObject("Map"); var rt = go.AddComponent<MapRuntime>(); rt.InitForTest();
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var ship = ShipMeshBuilder.Build(seed, new ShipParams(), rt.transform);
            var r = seed.ramps[0];
            double draftAft = 8.6, quayZ = 3.5, tide = 0.4, hingeZ = r.hinge[0][2];
            double angle = Math.Asin(((quayZ + tide) - (hingeZ - draftAft)) / r.length_m) * 180 / Math.PI;
            rt.SetPose($"{{\"draft_fwd_m\":8.1,\"draft_aft_m\":{draftAft},\"heel_deg\":0,\"lpp_m\":120,\"tide_m\":{tide},\"quay_z_m\":{quayZ},\"ramp\":{{\"id\":\"RAMP-STERN\",\"angle_deg\":{angle},\"state\":\"deployed\"}}}}");

            var ramp = ship.transform.Find("Ramp");
            float freeEndY = ramp.TransformPoint(new Vector3(-(float)r.length_m, 0, 0)).y;   // plate runs from the hinge toward -x
            Assert.That(freeEndY, Is.EqualTo((float)QuayBuilder.SurfaceZ(rt.Quay)).Within(0.01f));
            Object.DestroyImmediate(rt.Quay); Object.DestroyImmediate(go);
        }
```

- [ ] **Step 2: 실패를 확인한다**

Run: 위 Global Constraints 의 EditMode 명령
Expected: FAIL — `QuayBuilder` 와 `MapRuntime.Quay` 가 없어 컴파일되지 않는다

- [ ] **Step 3: `QuayBuilder` 를 만든다**

```csharp
using UnityEngine;

namespace ShipHdMap
{
    /// The quay slab. Not map data (the HD map is the ship's), so it lives in world space -- the Quay Frame -- and is
    /// never parented to the Map root: pose moves the ship, not the quay. Spec M5b §2.3.
    public static class QuayBuilder
    {
        public const double LengthM = 60, WidthM = 30, ThickM = 0.4;
        static Material _mat;

        /// Slab astern of the AP: ship -x, i.e. Unity x in [-LengthM, 0].
        public static GameObject Build()
        {
            var g = GameObject.CreatePrimitive(PrimitiveType.Cube);
            g.name = "Quay";
            Object.DestroyImmediate(g.GetComponent<Collider>());   // never catches a placement raycast or a sensor linecast
            g.transform.localScale = new Vector3((float)LengthM, (float)ThickM, (float)WidthM);
            if (!_mat) _mat = new Material(Shader.Find("Standard")) { color = new Color(0.62f, 0.6f, 0.56f), name = "quay" };
            g.GetComponent<Renderer>().sharedMaterial = _mat;
            SetHeight(g, 0);
            return g;
        }

        /// surfaceZ: the quay surface in the Quay Frame, i.e. quay_z_m + tide_m above the waterline.
        public static void SetHeight(GameObject quay, double surfaceZ)
        {
            if (!quay) return;
            quay.transform.position = new Vector3(-(float)LengthM / 2, (float)(surfaceZ - ThickM / 2), 0);
        }

        public static double SurfaceZ(GameObject quay) => quay.transform.position.y + ThickM / 2;
    }
}
```

- [ ] **Step 4: 메시지에 필드를 더하고 `MapRuntime` 을 잇는다**

`BridgeMessages.cs:23` 을 바꾼다:

```csharp
    public class SetPoseMsg { public double draft_fwd_m = 8.1, draft_aft_m = 8.6, heel_deg, lpp_m = 120, tide_m = 0, quay_z_m = 3.5; public RampMsg ramp; }
```

`MapRuntime` 에 프로퍼티를 더한다(다른 `public ... { get; private set; }` 들 옆):

```csharp
        public GameObject Quay { get; private set; }
```

`InitForTest` 의 `Hud = gameObject.AddComponent<HudView>();` 다음 줄에 넣는다:

```csharp
            Quay = QuayBuilder.Build();   // world space: the Quay Frame, never a child of this root
```

`ApplyPose` 의 `transform.localRotation = PoseRotation(...)` 바로 다음에 두 줄을 넣는다:

```csharp
            transform.localPosition = new Vector3(0, (float)(-_pose.draft_aft_m), 0);   // waterline is world y = 0; the AP origin sits one aft draft below it
            QuayBuilder.SetHeight(Quay, _pose.quay_z_m + _pose.tide_m);
```

그리고 `SetPose` 의 요약 주석을 고친다:

```csharp
        /// Ship Frame is the Map root's local space; pose rotates this root (M5a) and drops it by the aft draft (M5b).
        /// Children keep their local coordinates either way.
```

- [ ] **Step 5: 테스트가 통과하는지 확인한다**

Run: EditMode 명령
Expected: PASS (83 tests)

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Ship/QuayBuilder.cs unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs unity/Assets/ShipHdMap/Tests/EditMode/PoseTests.cs
git commit -m "feat: sink the ship by its aft draft and put a quay slab at quay_z + tide"
```

---

### Task 2: 경로의 z 와 피치

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Vehicle/LaneFollower.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Vehicle/VehicleController.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs:215,221,277`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/SensorAndVehicleTests.cs`, `ScenarioPlannerTests.cs:41,57`, `PoseTests.cs:52`

**Interfaces:**
- Produces: `LaneFollower.PathPose { double x, y, z, headingRad, pitchRad; bool end }`, `LaneFollower.At(double[][], double) → PathPose`, `VehicleController.StartPath(double[][] line, double speed)`, `VehicleController.StartLane(Lane lane)`, `VehicleController.Z`
- 주의: `StartPath`/`StartLane` 에서 z 인자가 사라진다. 경로 점의 3번째 성분이 z 다

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`SensorAndVehicleTests.cs` 의 기존 `LaneFollower` 테스트를 새 반환형으로 고치고(아래 Step 4 참조) 다음 테스트를 추가한다:

```csharp
        [Test]
        public void ClimbingPathGivesPerPointHeightAndNoseUpPitch()
        {
            var go = new GameObject("V"); var v = go.AddComponent<VehicleController>();
            // 10 m horizontal, 1 m up: pitch = atan(1/10) = 5.71 deg
            v.StartPath(new[] { new double[] { 0, 0, 5 }, new double[] { 10, 0, 6 } }, 1.0);
            v.Advance(5);
            Assert.That(v.Z, Is.EqualTo(5.5).Within(1e-9));
            Assert.That(v.transform.localPosition.y, Is.EqualTo(6.0f).Within(1e-4f));   // z + 0.5 ride height
            Assert.That(v.transform.forward.y, Is.EqualTo(Mathf.Sin(5.7106f * Mathf.Deg2Rad)).Within(1e-3f), "nose up on a climb");
            Object.DestroyImmediate(go);
        }
```

- [ ] **Step 2: 실패를 확인한다**

Run: EditMode 명령
Expected: FAIL — `VehicleController.Z` 가 없고 `StartPath` 인자가 3개다

- [ ] **Step 3: `LaneFollower` 를 바꾼다**

파일 전체를 이것으로 바꾼다:

```csharp
using System;

namespace ShipHdMap
{
    /// Pose on a [x,y,z] polyline. Arc length s is measured on the horizontal projection, so speed is ground speed
    /// and a slope does not slow the vehicle down.
    public struct PathPose { public double x, y, z, headingRad, pitchRad; public bool end; }

    public static class LaneFollower
    {
        public static PathPose At(double[][] line, double s)
        {
            double acc = 0;
            for (int i = 0; i + 1 < line.Length; i++)
            {
                double dx = line[i + 1][0] - line[i][0], dy = line[i + 1][1] - line[i][1], dz = line[i + 1][2] - line[i][2];
                double len = Math.Sqrt(dx * dx + dy * dy);
                double heading = Math.Atan2(dy, dx), pitch = Math.Atan2(dz, len < 1e-9 ? 1e-9 : len);
                if (s <= acc + len || i + 2 == line.Length)
                {
                    double t = len < 1e-9 ? 0 : Math.Min(1, Math.Max(0, (s - acc) / len));
                    return new PathPose { x = line[i][0] + dx * t, y = line[i][1] + dy * t, z = line[i][2] + dz * t,
                        headingRad = heading, pitchRad = pitch, end = s > acc + len };
                }
                acc += len;
            }
            return new PathPose { x = line[0][0], y = line[0][1], z = line[0][2], end = true };
        }
    }
}
```

- [ ] **Step 4: `VehicleController` 와 호출부를 바꾼다**

`VehicleController` 에서 `deckZ` 를 지우고 다음으로 바꾼다:

```csharp
        public double speedMps = 2.0;
        public double[][] path; public double s;
        public bool running;
        public bool AtEnd { get; private set; }
        public double Z { get; private set; }          // current path height, Ship Frame or Quay Frame depending on the parent
        public Pose2D Truth;

        public void StartLane(Lane lane) => StartPath(lane.centerline, lane.speed_limit_kmh > 0 ? lane.speed_limit_kmh / 3.6 : ScenarioPlanner.ParkSpeedMps);

        public void StartPath(double[][] line, double speed) { path = line; s = 0; speedMps = speed; running = true; AtEnd = false; Apply(); }

        public void Advance(double dt)
        {
            if (!running || path == null) return;
            s += speedMps * dt;
            if (LaneFollower.At(path, s).end) { running = false; AtEnd = true; }
            Apply();
        }

        public void Rewind(double arcLength) { s = arcLength; running = true; AtEnd = false; Apply(); }

        void Apply()
        {
            var p = LaneFollower.At(path, s);
            Truth = new Pose2D { x = p.x, y = p.y, psiRad = p.headingRad };
            Z = p.z;
            transform.localPosition = ShipFrame.ToUnity(p.x, p.y, p.z + 0.5);
            // Yaw first, then pitch about the yawed local Z: a positive Z rotation takes local +X (the nose) toward +Y.
            transform.localRotation = Quaternion.Euler(0, ShipFrame.UnityYawDeg(p.headingRad * 180 / System.Math.PI), 0)
                                    * Quaternion.Euler(0, 0, (float)(p.pitchRad * 180 / System.Math.PI));
        }
```

`MapRuntime` 의 세 곳을 고친다:

- `:215` → `Vehicle.StartPath(ScenarioPlanner.DeparturePath(_target.target_pose, _targetLane, z), ScenarioPlanner.ParkSpeedMps);`
- `:221` → `Vehicle.StartLane(_targetLane);`
- `:277` → `Vehicle.StartPath(path, ScenarioPlanner.ParkSpeedMps);`

기존 테스트의 호출부를 고친다:

- `PoseTests.cs:52` → `rt.Vehicle.StartLane(lane);`
- `ScenarioPlannerTests.cs:41` → `var end = LaneFollower.At(path, 999);` 이후 `end.x`, `end.headingRad` 로 읽는다:
  ```csharp
            var end = LaneFollower.At(path, 999);
            Assert.That(end.x, Is.EqualTo(102.4).Within(1e-4)); Assert.That(end.headingRad, Is.EqualTo(0).Within(1e-9));
  ```
- `ScenarioPlannerTests.cs:57` →
  ```csharp
            var last = LaneFollower.At(path, 999);
            Assert.That(Math.Abs(last.headingRad), Is.EqualTo(Math.PI).Within(1e-9));   // ends pointing astern
  ```
- `SensorAndVehicleTests.cs:44-49` →
  ```csharp
            var p0 = LaneFollower.At(line, 0);
            Assert.That(p0.end, Is.False);
            var p1 = LaneFollower.At(line, 12);
            ...
            var p2 = LaneFollower.At(line, 99);
            Assert.That(p2.end, Is.True);
  ```
  (원래 단언의 좌표·헤딩 검사는 `p0.x`, `p0.y`, `p1.headingRad` 로 이름만 바꿔 그대로 유지한다)

- [ ] **Step 5: 테스트 통과 확인**

Run: EditMode 명령
Expected: PASS (84 tests)

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Vehicle unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs unity/Assets/ShipHdMap/Tests/EditMode
git commit -m "feat: drive the path's own z with a matching pitch"
```

---

### Task 3: 부두 구간과 GPS

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Localization/LandmarkSensor.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Vehicle/ScenarioPlanner.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/ScenarioRunTests.cs`

**Interfaces:**
- Consumes: `ScenarioPlanner.ToTruthFrame(path, est, truth)`, `SetNoiseMsg.sigma_gps`
- Produces: `SensorNoise.sigmaGps`, `LandmarkSensor.Gps(Pose2D) → Pose2D`, `ScenarioPlanner.QuaySpawn`, `ScenarioPlanner.QuaySpeedMps`, `ScenarioPlanner.QuayPath(Pose2D est, double[] foot, double[] hinge)`, `MapRuntime.Phase.OnQuay`, `MapRuntime.RampEndsInQuay()`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`ScenarioRunTests.cs` 에 추가한다:

```csharp
        [Test]
        public void LoadStartsOnTheQuayAndGpsErrorOffsetsTheRampEntry()
        {
            // The vehicle is told where the berth's ramp is; GPS is what makes it miss. sigma_gps 0 enters dead centre.
            var rt = NewRuntime(Fixture());
            rt.SetPose(PoseJson(0));
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnQuay));
            Assert.That(rt.Vehicle.transform.parent, Is.Null, "on the quay the vehicle drives in the Quay Frame");
            Assert.That(rt.Vehicle.Truth.x, Is.EqualTo(ScenarioPlanner.QuaySpawn[0]).Within(1e-6));
            double centred = rt.Vehicle.path[rt.Vehicle.path.Length - 1][1];

            var noisy = NewRuntimeSecond(Fixture());
            noisy.SetNoise("{\"sigma_r\":0,\"sigma_theta\":0,\"sigma_alpha\":0,\"sigma_gps\":2.0}");
            noisy.SetPose(PoseJson(0));
            noisy.StartScenario("{\"mode\":\"load\"}");
            double offset = noisy.Vehicle.path[noisy.Vehicle.path.Length - 1][1];
            Assert.That(Math.Abs(offset - centred), Is.GreaterThan(0.3), "a 2 m GPS sigma must show up as a lateral miss");
        }
```

같은 파일에 헬퍼 두 개를 더한다(기존 `NewRuntime` 옆):

```csharp
        static string PoseJson(double heel) =>
            $"{{\"draft_fwd_m\":8.1,\"draft_aft_m\":8.6,\"heel_deg\":{heel},\"lpp_m\":120,\"tide_m\":0,\"quay_z_m\":3.5,\"ramp\":{{\"id\":\"RAMP-STERN\",\"angle_deg\":2.87,\"state\":\"deployed\"}}}}";

        GameObject go2;
        /// A second runtime in one test (the first one's Quay/Map stay alive until TearDown).
        MapRuntime NewRuntimeSecond(string fixture)
        {
            go2 = new GameObject("Map2"); var rt = go2.AddComponent<MapRuntime>(); rt.InitForTest();
            rt.Load(fixture); rt.SetNoise(NoNoise);
            return rt;
        }
```

`Cleanup` 을 고쳐 부두와 두 번째 런타임을 지운다:

```csharp
        [TearDown] public void Cleanup()
        {
            Time.timeScale = 1f;
            foreach (var rt in Object.FindObjectsByType<MapRuntime>(FindObjectsSortMode.None)) if (rt.Quay) Object.DestroyImmediate(rt.Quay);
            if (go) Object.DestroyImmediate(go);
            if (go2) Object.DestroyImmediate(go2);
            var ship = GameObject.Find("Ship"); if (ship) Object.DestroyImmediate(ship);
        }
```

- [ ] **Step 2: 실패를 확인한다**

Run: EditMode 명령
Expected: FAIL — `Phase.OnQuay` 도 `ScenarioPlanner.QuaySpawn` 도 없다

- [ ] **Step 3: GPS 관측을 더한다**

`LandmarkSensor.cs` 의 `SensorNoise` 에 필드를 더한다:

```csharp
    public class SensorNoise { public double sigmaR = 0.2; public double sigmaThetaRad = 1 * Math.PI / 180; public double sigmaAlphaRad = 2 * Math.PI / 180; public double sigmaGps = 0.5; public int seed = 1; }
```

`LandmarkSensor` 안 `Sense` 아래에 더한다:

```csharp
        /// GPS fix in the Quay Frame: position plus Gaussian noise, heading taken as known (compass/IMU).
        /// The vehicle has nothing else until the entrance landmark pair comes into view.
        public Pose2D Gps(Pose2D truth)
        {
            var rng = _rng ??= new System.Random(noise.seed);
            return new Pose2D { x = truth.x + Gauss(rng, noise.sigmaGps), y = truth.y + Gauss(rng, noise.sigmaGps), psiRad = truth.psiRad };
        }
```

`MapRuntime.SetNoise` 마지막에 더한다:

```csharp
            Sensor.noise.sigmaGps = n.sigma_gps;
```

- [ ] **Step 4: 부두 경로 기하를 더한다**

`ScenarioPlanner` 에 더한다:

```csharp
        /// Quay Frame spawn point (astern of the ship, off to one side) and the speed on the quay.
        public static readonly double[] QuaySpawn = { -45, 6 };
        public const double QuaySpeedMps = 5.0, LeadInM = 6.0;

        /// Belief-frame path on the quay: from the GPS fix, in behind the ramp, then up the ramp centreline to the hinge.
        /// foot/hinge are the ramp's real ends expressed in the Quay Frame — berth infrastructure the vehicle is told about.
        /// Executed through ToTruthFrame, so the GPS error becomes the lateral miss at the ramp.
        public static double[][] QuayPath(Pose2D est, double[] foot, double[] hinge) => new[]
        {
            new[] { est.x, est.y, foot[2] },
            new[] { foot[0] - LeadInM, foot[1], foot[2] },
            new[] { foot[0], foot[1], foot[2] },
            new[] { hinge[0], hinge[1], hinge[2] },
        };
```

- [ ] **Step 5: `MapRuntime` 에 부두 단계를 넣는다**

`Phase` 를 늘린다:

```csharp
        public enum Phase { Idle, OnQuay, OnRamp, OnLane, Parking, Departing, RampDown, QuayOut }
```

램프 양 끝을 Quay Frame 으로 주는 헬퍼를 더한다(`RefOf` 근처):

```csharp
        /// Ramp hinge midpoint and free-end midpoint in the Quay Frame, from the map's ramp geometry and the pose's angle.
        public (double[] hinge, double[] foot) RampEndsInQuay()
        {
            var r = CurrentMap?.ramps != null && CurrentMap.ramps.Count > 0 ? CurrentMap.ramps[0] : null;
            if (r == null) return (null, null);
            double hx = (r.hinge[0][0] + r.hinge[1][0]) / 2, hy = (r.hinge[0][1] + r.hinge[1][1]) / 2, hz = r.hinge[0][2];
            double a = (_pose?.ramp?.angle_deg ?? 0) * Math.PI / 180;
            return (InQuay(hx, hy, hz), InQuay(hx - r.length_m * Math.Cos(a), hy, hz + r.length_m * Math.Sin(a)));
        }

        double[] InQuay(double x, double y, double z)
        {
            var (qx, qy, qz) = ShipFrame.ToShip(transform.TransformPoint(ShipFrame.ToUnity(x, y, z)));
            return new[] { qx, qy, qz };
        }
```

`NextVehicle` 의 load 분기(`else { _exitS = ...; Vehicle.StartLane(...); ScenarioPhase = Phase.OnLane; }`)를 바꾼다:

```csharp
            else
            {
                _exitS = ScenarioPlanner.ExitS(_targetLane, _target.target_pose);
                var (hinge, foot) = RampEndsInQuay();
                if (hinge == null) { Vehicle.StartLane(_targetLane); ScenarioPhase = Phase.OnLane; return; }   // no ramp in the map: start on the lane as in M5a
                Vehicle.transform.SetParent(null, true);                                   // Quay Frame
                var truth = new Pose2D { x = ScenarioPlanner.QuaySpawn[0], y = ScenarioPlanner.QuaySpawn[1], psiRad = 0 };
                var est = Sensor.Gps(truth);
                Vehicle.StartPath(ScenarioPlanner.ToTruthFrame(ScenarioPlanner.QuayPath(est, foot, hinge), est, truth), ScenarioPlanner.QuaySpeedMps);
                ScenarioPhase = Phase.OnQuay;
            }
```

- [ ] **Step 6: 테스트 통과 확인**

Run: EditMode 명령
Expected: PASS. 기존 `LoadScenarioParksFirstSlot...` 은 이제 `Phase.OnQuay` 로 시작하므로 그 단언을 `Phase.OnQuay` 로 고치고, 주차까지 도달하는 `RunUntil` 은 Task 4·5 가 끝나야 다시 녹색이 된다. 이 태스크에서는 해당 테스트들에 `[Ignore("M5b Task 5 까지 보류")]` 를 붙이고 Task 5 Step 6 에서 되돌린다

- [ ] **Step 7: 커밋**

```bash
git add unity/Assets/ShipHdMap
git commit -m "feat: spawn on the quay and let the GPS error decide where the ramp is entered"
```

---

### Task 4: 프레임 전환

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Vehicle/ScenarioPlanner.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs` (없으면 `ScenarioEvt` 그대로 사용)
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/ScenarioRunTests.cs`

**Interfaces:**
- Consumes: `Localizer.Solve`, `MapRuntime.RampEndsInQuay`, `ramp.transition_landmarks`
- Produces: `MapRuntime.ShipTruth()`, `ScenarioPlanner.RampTopPath(Pose2D est, double[] hingeShip)`, `onScenario{event:"frame_switch"}`, `finished{detail:"no_frame_switch"}`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

```csharp
        [Test]
        public void EntranceLandmarkPairSwitchesToTheShipFrame()
        {
            var rt = NewRuntime(Fixture());
            rt.SetPose(PoseJson(0));
            rt.StartScenario("{\"mode\":\"load\"}");
            var json = RunUntil(rt, emitted, "onScenario", 4000, 0.05f, e => e.Contains("frame_switch"));
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.OnRamp));
            Assert.That(rt.Vehicle.transform.parent, Is.EqualTo(rt.transform), "after the switch the vehicle rides the Map root");
            Assert.That(json, Does.Contain("est"));
        }

        [Test]
        public void MissingEntrancePairEndsTheRunInsteadOfDrivingOn()
        {
            var f = Fixture().Replace("\"transition_landmarks\": [", "\"transition_landmarks\": [\"LM-NOPE\",");
            var rt = NewRuntime(f);
            rt.SetPose(PoseJson(0));
            rt.StartScenario("{\"mode\":\"load\"}");
            RunUntil(rt, emitted, "onScenario", 4000, 0.05f, e => e.Contains("no_frame_switch"));
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Idle));
        }
```

`RunUntil` 에 술어 인자를 더한다(기존 호출은 기본값으로 그대로 동작한다):

```csharp
        static string RunUntil(MapRuntime rt, List<(string name, string json)> log, string name, int maxSteps = 4000, float step = 0.05f, Func<string, bool> where = null)
        {
            int start = log.Count;
            for (int i = 0; i < maxSteps; i++)
            {
                rt.Step(step);
                var hit = log.Skip(start).FirstOrDefault(e => e.name == name && (where == null || where(e.json)));
                if (hit.name != null) return hit.json;
            }
            Assert.Fail($"no {name} within {maxSteps} steps (phase {rt.ScenarioPhase}, s {rt.Vehicle.s:F1})"); return null;
        }
```

- [ ] **Step 2: 실패를 확인한다**

Run: EditMode 명령
Expected: FAIL — 전환이 없어 `frame_switch` 가 나오지 않는다

- [ ] **Step 3: 진실 자세를 Ship Frame 으로 투영한다**

`MapRuntime` 에 더한다:

```csharp
        /// The sensor and the map live in Ship Frame. While the vehicle drives in the Quay Frame its TRUE pose is
        /// projected through the Map root's inverse so observations stay meaningful; the vehicle's own belief is GPS
        /// until the entrance pair is seen.
        public Pose2D ShipTruth()
        {
            if (Vehicle.transform.parent == transform) return Vehicle.Truth;
            var (x, y, _) = ShipFrame.ToShip(transform.InverseTransformPoint(Vehicle.transform.position));
            var fwd = transform.InverseTransformDirection(Vehicle.transform.forward);
            return new Pose2D { x = x, y = y, psiRad = Math.Atan2(-fwd.z, fwd.x) };
        }
```

`Localize()` 와 `Step()` 에서 `Vehicle.Truth` 를 `ShipTruth()` 로 바꾼다:

```csharp
        void Localize()
        {
            var truth = ShipTruth();
            var obs = Sensor.Sense(truth, MapRefs, id => _markers[id].transform.position);
            _seen.Clear(); foreach (var o in obs) _seen.Add(o.id);
            _lastRes = Localizer.Solve(obs, MapRefs, Sensor.noise.sigmaR, Sensor.noise.sigmaThetaRad, Sensor.noise.sigmaAlphaRad, _prev);
            if (_lastRes.ok && double.IsFinite(_lastRes.pose.x) && double.IsFinite(_lastRes.pose.y) && double.IsFinite(_lastRes.pose.psiRad)) _prev = _lastRes.pose;
            Hud.Set(_lastRes, truth, "SHIP_AP");
        }
```

`Step` 의 `onLocalization` 배출에서도 `Vehicle.Truth` 를 지역 변수 `var truth = ShipTruth();` 로 바꿔 쓴다. 필드도 더한다:

```csharp
        readonly HashSet<string> _seen = new();
```

- [ ] **Step 4: 전환을 넣는다**

`ScenarioPlanner` 에 더한다:

```csharp
        /// Belief-frame path from wherever the vehicle thinks it is to the ramp hinge (Ship Frame), where the deck lane starts.
        public static double[][] RampTopPath(Pose2D est, double[] hingeShip) => new[]
        {
            new[] { est.x, est.y, hingeShip[2] },
            new[] { hingeShip[0], hingeShip[1], hingeShip[2] },
        };
```

`MapRuntime.StepScenario` 의 `switch` 에 두 개를 더한다(`case Phase.OnLane ...` 앞):

```csharp
                case Phase.OnQuay when SawEntrancePair():
                {
                    // The estimate at this instant is everything the vehicle knows about where the ship is; it decides
                    // how squarely the car arrives at the top of the ramp. Lane keeping re-centres it after that.
                    var est = _lastRes.ok ? _lastRes.pose : ShipTruth();
                    var r = CurrentMap.ramps[0];
                    var hingeShip = new[] { (r.hinge[0][0] + r.hinge[1][0]) / 2, (r.hinge[0][1] + r.hinge[1][1]) / 2, r.hinge[0][2] };
                    Vehicle.transform.SetParent(transform, true);                       // Ship Frame, same world pose
                    var truth = ShipTruth();
                    Vehicle.StartPath(ScenarioPlanner.ToTruthFrame(ScenarioPlanner.RampTopPath(est, hingeShip), est, truth), ScenarioPlanner.ParkSpeedMps);
                    ScenarioPhase = Phase.OnRamp;
                    Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "frame_switch",
                        detail = $"est x {est.x:F2} y {est.y:F2} psi {est.psiRad * R2D:F1}" }));
                    break;
                }
                case Phase.OnQuay when Vehicle.AtEnd:
                    Finish("no_frame_switch");
                    break;
                case Phase.OnRamp when Vehicle.AtEnd:
                    Vehicle.StartLane(_targetLane);
                    ScenarioPhase = Phase.OnLane;
                    break;
```

그리고 판정 헬퍼를 더한다:

```csharp
        /// Both entrance markers of the stern ramp in one frame (spec §3.2): the trigger for the frame switch.
        bool SawEntrancePair()
        {
            var ids = CurrentMap?.ramps != null && CurrentMap.ramps.Count > 0 ? CurrentMap.ramps[0].transition_landmarks : null;
            if (ids == null || ids.Count < 2) return false;
            foreach (var id in ids) if (!_seen.Contains(id)) return false;
            return true;
        }
```

- [ ] **Step 5: 테스트 통과 확인**

Run: EditMode 명령
Expected: PASS — 전환 두 테스트가 녹색. 주차까지 가는 테스트는 아직 보류 상태다

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap
git commit -m "feat: switch to the ship frame on the entrance landmark pair"
```

---

### Task 5: 램프 차단·하역 복귀와 보류 테스트 해제

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Vehicle/ScenarioPlanner.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/ScenarioRunTests.cs`

**Interfaces:**
- Produces: `ScenarioPlanner.QuayOutPath(double[] hinge, double[] foot)`, `finished{detail:"ramp_blocked"}`, `Phase.RampDown`, `Phase.QuayOut`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

```csharp
        [Test]
        public void BlockedRampRefusesToStart()
        {
            var rt = NewRuntime(Fixture());
            rt.SetPose("{\"draft_fwd_m\":8.1,\"draft_aft_m\":8.6,\"heel_deg\":0,\"lpp_m\":120,\"tide_m\":0,\"quay_z_m\":3.5,\"ramp\":{\"id\":\"RAMP-STERN\",\"angle_deg\":31,\"state\":\"blocked\"}}");
            rt.StartScenario("{\"mode\":\"load\"}");
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.Idle));
            Assert.That(emitted.Any(e => e.name == "onScenario" && e.json.Contains("ramp_blocked")));
        }

        [Test]
        public void UnloadDrivesDownTheRampAndLeavesOnTheQuay()
        {
            var rt = NewRuntime(FilledFixture());
            rt.SetPose(PoseJson(0));
            rt.StartScenario("{\"mode\":\"unload\"}");
            RunUntil(rt, emitted, "onSlotFilled");                 // PS-D3-002 emptied at the lane start
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.RampDown));
            for (int i = 0; i < 2000 && rt.ScenarioPhase == MapRuntime.Phase.RampDown; i++) rt.Step(0.05f);
            Assert.That(rt.ScenarioPhase, Is.EqualTo(MapRuntime.Phase.QuayOut));
            Assert.That(rt.Vehicle.transform.parent, Is.Null, "back in the Quay Frame on the way out");
        }

        [Test]
        public void LoadRunsQuayToSlot()
        {
            var rt = NewRuntime(Fixture());
            rt.SetPose(PoseJson(0));
            rt.StartScenario("{\"mode\":\"load\"}");
            var json = RunUntil(rt, emitted, "onSlotFilled", 8000);
            var evt = MapJson.Parse<SlotFilledEvt>(json);
            Assert.That(evt.slot_id, Is.EqualTo("PS-D3-001"));
            Assert.That(evt.status, Is.EqualTo("filled"));          // zero noise all the way through
        }
```

그리고 Task 3 Step 6 에서 붙인 `[Ignore]` 를 모두 뗀다. 기존 테스트 중 `rt.StartScenario` 전에 pose 를 주지 않던 것들은 `rt.SetPose(PoseJson(0));` 를 추가한다(램프가 없으면 M5a 경로로 떨어지므로 대부분 그대로 통과하지만, 부두 구간을 타야 하는 것은 pose 가 필요하다).

- [ ] **Step 2: 실패를 확인한다**

Run: EditMode 명령
Expected: FAIL — `Phase.RampDown` 이 없고 blocked 가 무시된다

- [ ] **Step 3: 차단과 하역 복귀를 넣는다**

`ScenarioPlanner` 에 더한다:

```csharp
        /// Quay Frame path off the ship: down from the hinge to the ramp foot, then out to the spawn point.
        public static double[][] QuayOutPath(double[] hinge, double[] foot) => new[]
        {
            new[] { hinge[0], hinge[1], hinge[2] },
            new[] { foot[0], foot[1], foot[2] },
            new[] { QuaySpawn[0], QuaySpawn[1], foot[2] },
        };
```

`MapRuntime.StartScenario` 의 `SetMode("drive")` 앞에 넣는다:

```csharp
            if (_pose?.ramp != null && _pose.ramp.state == "blocked")
            {
                Send(BridgeMessages.OnScenario, MapJson.Serialize(new ScenarioEvt { evt = "finished", mode = _scenarioMode, detail = "ramp_blocked" }));
                return;
            }
```

`StepScenario` 의 `case Phase.Departing when Vehicle.AtEnd:` 본문에서 `NextVehicle();` 을 다음으로 바꾼다:

```csharp
                    var (hingeQ, footQ) = RampEndsInQuay();
                    if (hingeQ == null) { NextVehicle(); break; }
                    Vehicle.transform.SetParent(null, true);
                    Vehicle.StartPath(ScenarioPlanner.QuayOutPath(hingeQ, footQ), ScenarioPlanner.ParkSpeedMps);
                    ScenarioPhase = Phase.RampDown;
                    break;
```

그리고 두 단계를 더한다:

```csharp
                case Phase.RampDown when Vehicle.Z <= QuayBuilder.SurfaceZ(Quay) + 0.05:
                    ScenarioPhase = Phase.QuayOut;   // wheels are on the quay: GPS is what the vehicle has again
                    break;
                case Phase.QuayOut when Vehicle.AtEnd:
                    NextVehicle();
                    break;
```

`Finish` 와 `Load` 의 초기화, `SetMode("edit")` 에서 차량을 씬 루트에 두고 온 경우를 대비해 한 줄을 더한다(세 곳 모두 `Vehicle.gameObject.SetActive(false);` 옆):

```csharp
            if (Vehicle.transform.parent != transform) Vehicle.transform.SetParent(transform, false);
```

- [ ] **Step 4: 테스트 통과 확인**

Run: EditMode 명령
Expected: PASS — 보류 테스트 포함 전부 녹색

- [ ] **Step 5: 커밋**

```bash
git add unity/Assets/ShipHdMap
git commit -m "feat: refuse a blocked ramp and drive the unloaded car back onto the quay"
```

---

### Task 6: 입구 랜드마크 쌍과 전 갑판 기둥 픽스처

**Files:**
- Modify: `unity/Assets/ShipHdMap/Editor/FixtureExporter.cs`
- Modify: `docs/fixtures/vehicle-map.sample.json` (재생성)
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/FixtureExporterTests.cs`

**Interfaces:**
- Produces: `LM-0022`·`LM-0023` (램프 힌지 기둥, 법선 −x), `ramp.transition_landmarks = ["LM-0022","LM-0023"]`, `map.facilities` = 전 갑판

- [ ] **Step 1: 실패하는 테스트를 쓴다**

```csharp
        [Test]
        public void SeedLandmarksIncludeTheRampEntrancePair()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var lms = FixtureExporter.SeedLandmarks(seed);
            var pair = lms.Where(l => l.mounted_on == seed.ramps[0].id).OrderBy(l => l.position[1]).ToList();
            Assert.That(pair, Has.Count.EqualTo(2));
            Assert.That(pair[0].position[2], Is.EqualTo(seed.ramps[0].hinge[0][2] + 1.2).Within(1e-9));   // above the ramp plate, so the line of sight from the quay is clear
            Assert.That(pair[0].normal, Is.EqualTo(new double[] { -1, 0, 0 }));                            // facing astern, toward a vehicle coming up the ramp
            Assert.That(pair[0].position[1], Is.LessThan(0)); Assert.That(pair[1].position[1], Is.GreaterThan(0));
            Assert.That(seed.ramps[0].transition_landmarks, Is.EqualTo(new[] { pair[0].id, pair[1].id }).AsCollection);
        }

        [Test]
        public void ExportedMapCarriesEveryDecksPillars()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var map = FixtureExporter.BuildMap(seed, new VehicleMap(), FixtureExporter.SeedLandmarks(seed));
            foreach (var d in seed.decks)
                Assert.That(map.facilities.Count(f => f.deck_id == d.id && f.kind == "pillar"), Is.GreaterThan(0), "pillars on " + d.id);
        }
```

- [ ] **Step 2: 실패를 확인한다**

Run: EditMode 명령
Expected: FAIL — 쌍이 없고 `facilities` 는 D3 만 담고 있다

- [ ] **Step 3: 내보내기를 고친다**

`SeedLandmarks` 의 `return landmarks;` 앞에 더한다:

```csharp
            // Frame-transition pair (spec §3.2): two tags on the stern ramp's hinge posts, facing astern so a vehicle
            // coming up the ramp sees both at once. 1.2 m above the hinge keeps the sight line clear of the ramp plate.
            var ramp = seed.ramps.FirstOrDefault();
            if (ramp != null)
            {
                double hy = (ramp.hinge[0][1] + ramp.hinge[1][1]) / 2, hz = ramp.hinge[0][2], half = ramp.width_m / 2 - 0.5;
                landmarks.Add(Lm("LM-0022", 22 % 20, 0.3, hy - half, hz + 1.2, -1, 0, 0, ramp.id, deck.id));
                landmarks.Add(Lm("LM-0023", 23 % 20, 0.3, hy + half, hz + 1.2, -1, 0, 0, ramp.id, deck.id));
                ramp.transition_landmarks = new List<string> { "LM-0022", "LM-0023" };
            }
```

`ExportFromSeed` 의 임시 줄을 지운다:

```csharp
            foreach (var r in seed.ramps) r.transition_landmarks = new List<string> { "LM-0001", "LM-0002" };   // 삭제
```

`BuildMap` 의 facilities 필터를 바꾼다:

```csharp
                decks = seed.decks, lanes = seed.lanes, ramps = seed.ramps,
                facilities = seed.facilities,   // every deck: Load rebuilds the hull from this map (M5b Task 7)
```

- [ ] **Step 4: 픽스처를 다시 만든다**

Run:
```bash
cd unity && "$U" -batchmode -nographics -quit -projectPath "$PWD" -executeMethod ShipHdMap.Editor.FixtureExporter.ExportFromSeed -logFile "$PWD/Logs/fixture.log"
python3 -c "import json;m=json.load(open('../docs/fixtures/vehicle-map.sample.json'));print({k:(len(v) if isinstance(v,list) else v) for k,v in m.items()});print(m['ramps'][0]['transition_landmarks'])"
```
Expected: `landmarks 23`, `facilities 54`, `transition_landmarks ['LM-0022', 'LM-0023']`

- [ ] **Step 5: 테스트 통과 확인**

Run: EditMode 명령
Expected: PASS. `MapModelTests` 의 `transition_landmarks` 단언이 `LM-0001/0002` 를 기대하면 새 id 로 고친다

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap docs/fixtures/vehicle-map.sample.json
git commit -m "feat: seed the ramp entrance landmark pair and export every deck's pillars"
```

---

### Task 7: 지도로 선체 짓기

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Ship/ShipMeshBuilder.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs` (`Awake`, `Load`)
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/ShipMeshBuilderTests.cs`

**Interfaces:**
- Produces: `ShipMeshBuilder.Build(VehicleMap map, Transform parent = null)` — 기존 `Build(SeedData, ShipParams, Transform)` 는 남겨 시드 경로(에디터 생성기·기존 테스트)가 계속 쓰게 한다
- `MapRuntime` 은 `Load` 에서 지도가 바뀌면 선체를 다시 짓는다(플레이 모드에서만)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

```csharp
        [Test]
        public void BuildFromMapMatchesBuildFromSeed()
        {
            var seed = ShipSeedBuilder.Build(new ShipParams());
            var map = FixtureExporter.BuildMap(seed, new VehicleMap(), new List<Landmark>());
            var fromSeed = ShipMeshBuilder.Build(seed, new ShipParams());
            var fromMap = ShipMeshBuilder.Build(map);
            foreach (var d in seed.decks)
            {
                var a = fromSeed.transform.Find($"{d.id}/Floor"); var b = fromMap.transform.Find($"{d.id}/Floor");
                Assert.That(b, Is.Not.Null, "deck " + d.id);
                Assert.That(b.localPosition, Is.EqualTo(a.localPosition));
                Assert.That(b.localScale, Is.EqualTo(a.localScale));
                Assert.That(fromMap.transform.Find($"{d.id}/Pillars").childCount, Is.EqualTo(fromSeed.transform.Find($"{d.id}/Pillars").childCount));
            }
            Assert.That(fromMap.transform.Find("Ramp").localPosition, Is.EqualTo(fromSeed.transform.Find("Ramp").localPosition));
            Object.DestroyImmediate(fromSeed); Object.DestroyImmediate(fromMap);
        }
```

(`FixtureExporter` 는 Editor 어셈블리다. `ShipHdMap.Tests.EditMode.asmdef` 의 참조에 Editor 어셈블리가 없으면 추가하거나, 테스트에서 `BuildMap` 대신 손으로 `new VehicleMap { decks = seed.decks, facilities = seed.facilities, ramps = seed.ramps, lashing_points = seed.lashing_points }` 를 만든다 — 후자를 먼저 시도한다)

- [ ] **Step 2: 실패를 확인한다**

Run: EditMode 명령
Expected: FAIL — `Build(VehicleMap)` 오버로드가 없다

- [ ] **Step 3: 지도 기반 오버로드를 만든다**

`ShipMeshBuilder` 의 기존 `Build(SeedData seed, ShipParams p, Transform parent = null)` 본문에서 갑판 치수를 뽑는 두 줄만 바꿔 공통 코드를 재사용한다. 가장 작은 변경은 시드를 지도로 감싸는 어댑터다:

```csharp
        /// Build the hull from the map instead of the generator's parameters: L and B come from each deck's own outline,
        /// so a different ship model in the DB gives a different hull without touching ShipParams (spec M5b §3).
        public static GameObject Build(VehicleMap map, Transform parent = null)
        {
            var seed = new SeedData
            {
                decks = map.decks ?? new List<Deck>(),
                facilities = map.facilities ?? new List<Facility>(),
                lashing_points = map.lashing_points ?? new List<LashingPoint>(),
                ramps = map.ramps ?? new List<Ramp>(),
                lanes = map.lanes ?? new List<Lane>(),
            };
            return Build(seed, null, parent);
        }
```

그리고 `Build(SeedData seed, ShipParams p, Transform parent = null)` 안에서 `p` 를 쓰는 세 곳을 갑판 윤곽에서 뽑도록 바꾼다(`p` 가 null 이어도 동작해야 한다):

```csharp
                var deck = Child(ship, d.id);
                // Hull dimensions come from the deck's own outline; ShipParams is only a fallback for the seed path.
                double x0 = d.outline.Min(pt => pt[0]), x1 = d.outline.Max(pt => pt[0]);
                double y0 = d.outline.Min(pt => pt[1]), y1 = d.outline.Max(pt => pt[1]);
                float z = (float)d.z_surface, L = (float)(x1 - x0), B = (float)(y1 - y0);
```

MEP 파이프의 `p.lengthM / 2` 도 `(x0 + x1) / 2` 로 바꾼다.

- [ ] **Step 4: `MapRuntime` 이 지도로 다시 짓게 한다**

`MapRuntime` 에 필드와 헬퍼를 더한다:

```csharp
        string _shipSignature;

        /// Rebuild only when the map's ship-defining parts actually change: a slot regeneration reloads the whole map
        /// and rebuilding thousands of primitives every time would stall the browser.
        static string ShipSignature(VehicleMap m)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var d in m.decks ?? new List<Deck>()) sb.Append(d.id).Append(':').Append(d.z_surface).Append(':').Append(d.z_clear).Append('|');
            sb.Append('#').Append((m.facilities ?? new List<Facility>()).Count).Append('#').Append((m.lashing_points ?? new List<LashingPoint>()).Count);
            foreach (var r in m.ramps ?? new List<Ramp>()) sb.Append('#').Append(r.id).Append(':').Append(r.length_m).Append(':').Append(r.hinge[0][2]);
            return sb.ToString();
        }
```

`Load` 의 `ApplyPose();` 바로 앞에 넣는다:

```csharp
            var sig = ShipSignature(CurrentMap);
            if (Application.isPlaying && sig != _shipSignature)
            {
                if (Ship) Destroy(Ship);
                Ship = ShipMeshBuilder.Build(CurrentMap, transform);
                _shipSignature = sig;
                ShipMeshBuilder.SetDeckVisibility(Ship, _deck);
            }
```

`Awake` 의 선체 생성은 그대로 둔다(지도가 오기 전 씬을 채운다). 주석 한 줄을 더한다:

```csharp
            // Seed-built hull so the scene is not empty before the first Load; Load replaces it with the map's own hull.
```

- [ ] **Step 5: 테스트 통과 확인**

Run: EditMode 명령
Expected: PASS

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap
git commit -m "feat: build the hull from the vehicle-map instead of ShipParams"
```

---

### Task 8: 웹·문서·빌드

**Files:**
- Modify: `web/src/components/PosePanel.tsx`, `web/src/bridge/useShipUnity.ts`, `web/src/api/types.ts`, `web/src/store/editor.ts`
- Test: `web/src/store/editor.test.ts` (없으면 `scenarioLine` 테스트가 있는 파일)
- Modify: `docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md`, `README.md`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`scenarioLine` 테스트가 있는 파일에 추가한다:

```ts
  it("frame_switch 를 사람이 읽는 줄로 만든다", () => {
    expect(scenarioLine({ event: "frame_switch", detail: "est x 1.20 y -0.30 psi 0.4" })).toBe("프레임 전환 · est x 1.20 y -0.30 psi 0.4");
    expect(scenarioLine({ event: "finished", detail: "ramp_blocked" })).toBe("종료 (ramp_blocked)");
  });
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd web && pnpm vitest run`
Expected: FAIL — `frame_switch` 가 `default` 로 떨어져 이벤트 이름 그대로 나온다

- [ ] **Step 3: 웹을 고친다**

`web/src/api/types.ts`:

```ts
export type ScenarioEvt = { event: "start" | "target" | "leave_lane" | "frame_switch" | "finished"; mode?: "load" | "unload"; slot_id?: string; detail?: string };
```

`web/src/store/editor.ts` 의 `scenarioLine` 에 한 줄:

```ts
    case "frame_switch": return `프레임 전환 · ${e.detail ?? ""}`;
```

`web/src/components/PosePanel.tsx` 의 `FIELDS` 에 한 줄:

```ts
  { key: "quay_z_m", label: "부두 높이", min: 0, max: 8, step: 0.1, unit: "m" },
```

`web/src/bridge/useShipUnity.ts` 의 `sendPose` 를 고친다:

```ts
    send("SetPose", { draft_fwd_m: pose.draft_fwd_m ?? 8.1, draft_aft_m: pose.draft_aft_m ?? 8.6, heel_deg: pose.heel_deg ?? 0, lpp_m: dataset?.lpp_m ?? 120,
      tide_m: pose.tide_m ?? 0, quay_z_m: pose.quay_z_m ?? 3.5,
      ...(ramp ? { ramp: { id: ramp.id, angle_deg: ramp.angle_deg, state: ramp.state } } : {}) });
```

- [ ] **Step 4: 문서를 고친다**

`docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md`:

1. §3.4 의 z 규칙에 한 줄: `- 램프 위에서는 z 를 램프 기하(힌지 z 와 각도)로 확정한다. 갑판 위에서는 deck_id 로 확정한다`
2. §4.1 의 `6자유도 강체변환 하나로 ...` 줄 다음에: `- Unity 반영: pose 는 Map 루트의 회전(trim·heel)과 위치(z = −draft_aft)로만 들어간다. 월드 z = 0 이 수면이다`
3. §4.2 의 `램프 위 차로는 저장하지 않고 ...` 다음에: `- 횡경사에서는 힌지선이 기울어 램프 끝단 좌우가 (폭/2)·sin(heel) 만큼 어긋난다. 램프는 강체 평면으로 두고 주행 z 는 중앙선 기준으로 본다`
4. §10 브리지 표의 `SetPose` 행에 `tide_m, quay_z_m` 을, `onScenario` 행에 `frame_switch` 를 더한다

`README.md` 상태 절에 한 줄 더한다:

```markdown
- M5b 부두·프레임 전환 완료 (부두 GPS 주행, 램프 진입, 입구 랜드마크 쌍으로 Ship Frame 전환, 지도로 선체 재구성; api 46, EditMode NN, Vitest NN)
```

`NN` 은 Step 5 에서 실제로 나온 수를 그대로 적는다.

- [ ] **Step 5: 전체 테스트와 빌드**

Run:
```bash
cd api && ./gradlew test -q
cd ../web && pnpm vitest run
cd ../unity && "$U" -batchmode -nographics -projectPath "$PWD" -runTests -testPlatform EditMode -testResults "$PWD/Logs/editmode-results.xml" -logFile "$PWD/Logs/editmode.log"; grep -o 'total="[0-9]*" passed="[0-9]*" failed="[0-9]*"' Logs/editmode-results.xml | head -1
"$U" -batchmode -nographics -quit -projectPath "$PWD" -executeMethod ShipHdMap.Editor.WebGLBuild.Build -logFile "$PWD/Logs/webgl.log"
```
Expected: 모두 통과, `web/public/unity` 가 갱신된다

- [ ] **Step 6: 커밋**

```bash
git add web docs README.md
git commit -m "feat: quay height slider and frame-switch log line"
```

---

## 브라우저 검증 (스펙 §8)

`./scripts/m3-dev.sh` 로 띄운 뒤 순서대로 확인한다. 각 항목의 결과를 기록한다.

1. 부두 슬래브가 선미 쪽에 보이고 램프 끝이 그 위에 닿아 있다
2. 흘수·조위·부두 높이 슬라이더로 배가 상하로 움직이고 램프 각도가 따라온다. 갑판 좌표 표시(HUD)는 불변
3. σ_gps 0 에서 램프 중앙으로 진입한다
4. σ_gps 2 m 에서 눈에 띄게 치우쳐 진입하고, 전환 로그가 램프 위에서 뜬다
5. 램프를 오르는 동안 차량이 기울어 있다
6. 갑판에 올라서면 M5a 와 같이 주차되고 구획 색이 바뀐다
7. 하역이 램프를 내려가 부두에서 끝난다
8. 부두 높이를 범위 밖으로 밀면 램프가 `blocked` 이고 선적이 시작되지 않는다
9. 횡경사 3°에서 램프 끝단이 비틀리는 것이 보인다(알려진 단순화)

문제가 나오면 해당 태스크로 돌아가 고치고, 원인과 교훈을 `docs/learning/07-m5b-review.md` 에 적는다.
