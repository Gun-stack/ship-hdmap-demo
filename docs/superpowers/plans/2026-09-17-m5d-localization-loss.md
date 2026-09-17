# M5d 측위 상실 판정과 역추적 회복 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 차량이 자기 측위 품질을 알고, 지도가 약속한 값보다 나빠지면 알아채고, 잃으면 지나온 자취를 되짚어 빠져나오고, 못 나오면 그 구획을 포기하고 다음으로 넘어간다.

**Architecture:** 판정 로직은 `BeliefMonitor` 라는 순수 정적 클래스에 모은다 — Unity 도 씬도 모르므로 EditMode 테스트가 프레임 없이 돈다 (`SlotGenerator`·`CoverageAnalyzer` 와 같은 자리). `Localizer` 는 이미 만들고 있는 정보행렬을 역행렬 해서 σ 를 함께 낸다. 예측 σ 격자는 **웹이** M5c 의 `POST …/coverage` 로 받아 브리지로 밀어 넣는다(Unity 는 HTTP 를 하지 않는다 — `Load` 와 같은 패턴). `MapRuntime` 은 상태 전이와 역추적 주행만 배선한다.

**Tech Stack:** Unity 6.3 / C# (EditMode + NUnit), Java 25 / Spring Boot 4 / Flyway / PostGIS, React 19 + Zustand + Vite, Vitest

**Spec:** `docs/superpowers/specs/2026-09-17-m5d-localization-loss-design.md`

## Global Constraints

- 좌표는 Ship Frame `[x, y, z]` m. 각도는 **와이어에서 도(°), 내부 계산은 라디안**
- JSON 키는 snake_case
- 판정 기본값은 스펙 §3.4·§3.5·§3.6 과 같다: `k = 2.0`, `N = 5` 프레임, `drift_rate = 0.05`, `budget_m = 1.0`, `max_lost_m = 5.0`, `trail_m = 20.0`, 자취 기록 간격 `0.25 m`
- **Unity 는 HTTP 를 호출하지 않는다.** API 조회는 전부 웹이 하고 브리지로 넘긴다
- C# 들여쓰기는 스페이스 4 칸, 자바는 탭, 웹은 스페이스 2 칸 (기존 파일 그대로)
- 순수 계산기는 `UnityEngine` 을 import 하지 않는다 (`BeliefMonitor` 가 그렇다)
- 커밋 메시지는 한 줄 영문 요약. **자명하지 않은 변경은 본문에 왜 그렇게 했는지 적는다** — 기존 이력이 그렇다. 본문 끝에 `Co-Authored-By:` 와 `Claude-Session:` 두 줄을 붙인다

---

## File Structure

| 파일 | 책임 |
| --- | --- |
| `unity/…/Localization/Observation.cs` | `LocalizerResult` 에 `sigmaXy`·`sigmaPsiDeg` 추가 |
| `unity/…/Localization/Localizer.cs` | `Information`·`Invert3` 추가, 모든 반환 경로가 σ 를 채운다 |
| `unity/…/Localization/LandmarkSensor.cs` | `occluded` 집합의 마커를 건너뛴다 |
| `unity/…/Vehicle/BeliefMonitor.cs` | **신규.** 순수 정적. 상태 기계·오도메트리 예산·역추적 자취 |
| `unity/…/Bridge/BridgeMessages.cs` | `SetBeliefParams`·`SetOccluded`·`SetPrediction`·`onBelief` |
| `unity/…/Bridge/MapRuntime.cs` | 예측 격자 보관, 상태 전이, 역추적 주행, 재시도·스킵 |
| `unity/…/Tests/EditMode/BeliefMonitorTests.cs` | **신규.** Task 2 의 단위 테스트 |
| `unity/…/Tests/EditMode/LocalizerSigmaTests.cs` | **신규.** Task 1 의 단위 테스트 |
| `api/src/main/resources/db/migration/V2__unreachable.sql` | **신규.** status 에 `unreachable` |
| `api/…/slots/SlotRepo.java` | status 화이트리스트에 `unreachable` |
| `web/src/api/types.ts`, `client.ts` | `BeliefEvt`, 예측 조회 재사용 |
| `web/src/store/editor.ts` | belief 상태·파라미터, 가림 집합, 예측 푸시 |
| `web/src/components/DrivePanel.tsx` | 상태 배지·파라미터·회복 로그·집계 |
| `web/src/components/LayerTree.tsx` | LM 가림 체크박스 |
| `web/src/geo/belief.ts` + `.test.ts` | 배지 매핑·배수 포맷 (순수 함수) |
| `docs/api-contract.md` | status 값 추가 |

---

### Task 1: Localizer 가 σ 를 낸다

`Solve` 는 이미 매 반복마다 `A = JᵀWJ` 를 쌓는다. 야코비안이 `CoverageAnalyzer` 와 **글자 그대로 같다.** 수렴 후의 자세에서 A 를 한 번 더 만들어 역행렬 하면 끝이다.

반복 안의 A 를 들고 나오지 않고 **끝에서 다시 만든다.** `used.Count == 1 && !previous.HasValue` 경로는 반복을 아예 돌지 않아 A 가 없는데, 그 경로도 σ 가 필요하기 때문이다. 한 번 더 도는 비용은 마커 수에 비례하는 산술 몇 줄이다.

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Localization/Observation.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Localization/Localizer.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/LocalizerSigmaTests.cs`

**Interfaces:**
- Produces: `LocalizerResult.sigmaXy` (`double?`), `LocalizerResult.sigmaPsiDeg` (`double?`) — 관측 0 이거나 특이행렬이면 둘 다 `null`
- Produces: `Localizer.Information(Pose2D p, IList<(Observation o, LandmarkRef lm)> used, double wr, double wt, double wa)` → `double[,]` (internal)
- Produces: `Localizer.Invert3(double[,] a)` → `double[,]` 또는 `null` (internal)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/LocalizerSigmaTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;
using ShipHdMap;

public class LocalizerSigmaTests
{
    const double SR = 0.2, ST = Math.PI / 180, SA = 2 * Math.PI / 180;

    /// A marker at range r on bearing beta from the origin, its normal pointing back at the origin.
    static LandmarkRef At(string id, double r, double betaDeg)
    {
        double b = betaDeg * Math.PI / 180;
        return new LandmarkRef { id = id, mx = r * Math.Cos(b), my = r * Math.Sin(b), phiRad = ShipFrame.WrapRad(b + Math.PI) };
    }

    static (List<Observation>, Dictionary<string, LandmarkRef>) Scene(params LandmarkRef[] lms)
    {
        var map = new Dictionary<string, LandmarkRef>();
        var obs = new List<Observation>();
        var truth = new Pose2D { x = 0, y = 0, psiRad = 0 };
        foreach (var lm in lms) { map[lm.id] = lm; obs.Add(Localizer.Observe(truth, lm)); }
        return (obs, map);
    }

    /// Reference values computed from the same information matrix the API's CoverageAnalyzer builds.
    /// If these drift, the two implementations have diverged and the coverage heatmap is lying.
    [Test]
    public void SigmaMatchesTheCoverageAnalyzerReference()
    {
        var (obs, map) = Scene(At("a", 10, -30), At("b", 10, 30));
        var r = Localizer.Solve(obs, map, SR, ST, SA, new Pose2D());
        Assert.That(r.sigmaXy, Is.Not.Null);
        Assert.That(r.sigmaXy.Value, Is.EqualTo(0.251583).Within(1e-4), "sigma_xy for two markers at r=10, beta=+/-30");
        Assert.That(r.sigmaPsiDeg.Value, Is.EqualTo(1.051229).Within(1e-4));
    }

    [Test]
    public void OneMarkerStillYieldsSigma()
    {
        var (obs, map) = Scene(At("a", 10, 0));
        var r = Localizer.Solve(obs, map, SR, ST, SA, new Pose2D());
        Assert.That(r.sigmaXy.Value, Is.EqualTo(0.438530).Within(1e-4));
        // one marker leaves only ja = {0,0,-1}, so heading precision is exactly the sensor's alpha noise
        Assert.That(r.sigmaPsiDeg.Value, Is.EqualTo(2.0).Within(1e-6));
    }

    [Test]
    public void SurroundingMarkersBeatClusteredOnes()
    {
        var (o3, m3) = Scene(At("a", 10, -40), At("b", 10, 0), At("c", 10, 40));
        var spread = Localizer.Solve(o3, m3, SR, ST, SA, new Pose2D());
        Assert.That(spread.sigmaXy.Value, Is.EqualTo(0.200347).Within(1e-4));
        var (o2, m2) = Scene(At("a", 10, -30), At("b", 10, 30));
        var fewer = Localizer.Solve(o2, m2, SR, ST, SA, new Pose2D());
        Assert.That(spread.sigmaXy.Value, Is.LessThan(fewer.sigmaXy.Value));
    }

    /// The closed-form shortcut (one marker, no previous pose) skips the Gauss-Newton loop entirely.
    /// It must still report sigma, which is why Information() is rebuilt at the end rather than captured inside.
    [Test]
    public void TheClosedFormPathAlsoReportsSigma()
    {
        var (obs, map) = Scene(At("a", 10, 0));
        var r = Localizer.Solve(obs, map, SR, ST, SA, null);
        Assert.That(r.iterations, Is.Zero, "this is the closed-form path");
        Assert.That(r.sigmaXy, Is.Not.Null);
        Assert.That(r.sigmaXy.Value, Is.EqualTo(0.438530).Within(1e-4));
    }

    [Test]
    public void NoObservationsMeansNoSigma()
    {
        var r = Localizer.Solve(new List<Observation>(), new Dictionary<string, LandmarkRef>(), SR, ST, SA, new Pose2D());
        Assert.That(r.ok, Is.False);
        Assert.That(r.nObs, Is.Zero);
        Assert.That(r.sigmaXy, Is.Null);
        Assert.That(r.sigmaPsiDeg, Is.Null);
    }

    [Test]
    public void WorseSensorMeansWorseSigma()
    {
        var (obs, map) = Scene(At("a", 10, -30), At("b", 10, 30));
        var good = Localizer.Solve(obs, map, SR, ST, SA, new Pose2D());
        var bad = Localizer.Solve(obs, map, SR * 4, ST * 4, SA * 4, new Pose2D());
        Assert.That(bad.sigmaXy.Value, Is.GreaterThan(good.sigmaXy.Value));
        Assert.That(bad.sigmaPsiDeg.Value, Is.GreaterThan(good.sigmaPsiDeg.Value));
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Unity Editor 를 열거나 CLI 로 EditMode 테스트를 돌린다:

```bash
/Applications/Unity/Hub/Editor/6000.3*/Unity.app/Contents/MacOS/Unity \
  -runTests -batchmode -projectPath "$PWD/unity" -testPlatform EditMode \
  -testResults /tmp/m5d.xml -logFile /tmp/m5d.log
```

Expected: 컴파일 실패 — `LocalizerResult` 에 `sigmaXy` 가 없다

- [ ] **Step 3: `LocalizerResult` 에 두 필드를 더한다**

`unity/Assets/ShipHdMap/Runtime/Localization/Observation.cs` 의 마지막 줄을:

```csharp
    /// sigmaXy/sigmaPsiDeg are the achievable precision at this solve: the diagonal of (J^T W J)^-1, the same
    /// quantity the API's CoverageAnalyzer predicts for every deck cell. Null when nothing was seen or A is singular.
    public class LocalizerResult
    {
        public bool ok; public Pose2D pose; public int nObs; public double residualRms; public int iterations;
        public double? sigmaXy, sigmaPsiDeg;
    }
```

- [ ] **Step 4: `Information` 과 `Invert3` 를 더한다**

`Localizer.cs` 의 `Accumulate` 아래에:

```csharp
        /// A = J^T W J at pose p. Same Jacobians as the solve loop and as CoverageAnalyzer.estimate; no residuals,
        /// because precision does not depend on how wrong we currently are.
        static double[,] Information(Pose2D p, List<(Observation o, LandmarkRef lm)> used, double wr, double wt, double wa)
        {
            double[,] A = new double[3, 3];
            foreach (var (o, lm) in used)
            {
                var res = Residual(p, o, lm);
                double[] jr = { -res.dx / res.rh, -res.dy / res.rh, 0 };
                double[] jt = { res.dy / (res.rh * res.rh), -res.dx / (res.rh * res.rh), -1 };
                double[] ja = { 0, 0, -1 };
                foreach (var (j, w) in new[] { (jr, wr), (jt, wt), (ja, wa) })
                    for (int i = 0; i < 3; i++) for (int k = 0; k < 3; k++) A[i, k] += w * j[i] * j[k];
            }
            return A;
        }

        /// Cofactor inverse of a 3x3; null when singular. Mirrors CoverageAnalyzer.invert3.
        static double[,] Invert3(double[,] a)
        {
            double det = a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1])
                       - a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0])
                       + a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);
            if (Math.Abs(det) < 1e-12) return null;
            double[,] outM = new double[3, 3];
            for (int i = 0; i < 3; i++)
                for (int j = 0; j < 3; j++)
                {
                    int r0 = i == 0 ? 1 : 0, r1 = i == 2 ? 1 : 2, c0 = j == 0 ? 1 : 0, c1 = j == 2 ? 1 : 2;
                    double minor = a[r0, c0] * a[r1, c1] - a[r0, c1] * a[r1, c0];
                    outM[j, i] = ((i + j) % 2 == 0 ? minor : -minor) / det;   // transposed cofactor = adjugate
                }
            return outM;
        }

        /// Achievable (sigma_xy, sigma_psi_deg) at p, or (null, null) when A cannot be inverted.
        static (double?, double?) Sigma(Pose2D p, List<(Observation o, LandmarkRef lm)> used, double wr, double wt, double wa)
        {
            var c = Invert3(Information(p, used, wr, wt, wa));
            if (c == null) return (null, null);
            double vx = Math.Max(c[0, 0], 0), vy = Math.Max(c[1, 1], 0), vp = Math.Max(c[2, 2], 0);
            return (Math.Sqrt(vx + vy), Math.Sqrt(vp) * 180 / Math.PI);
        }
```

- [ ] **Step 5: 모든 반환 경로가 σ 를 채우게 한다**

`Solve` 안의 네 군데를 고친다. `wr`/`wt`/`wa` 선언이 초기 추정 계산보다 **뒤에** 있으므로, 선언을 `used.Count == 0` 검사 바로 아래로 끌어올린다.

```csharp
            if (used.Count == 0)
                return new LocalizerResult { ok = false, pose = previous ?? default, nObs = 0 };

            double wr = Weight(sigmaR), wt = Weight(sigmaTheta), wa = Weight(sigmaAlpha);   // moved up: every exit needs it
```

그 다음 각 반환문:

```csharp
            // closed-form shortcut
            if (used.Count == 1 && !previous.HasValue)
            {
                var (sxy0, sps0) = Sigma(p, used, wr, wt, wa);
                return new LocalizerResult { ok = true, pose = p, nObs = 1, residualRms = 0, iterations = 0, sigmaXy = sxy0, sigmaPsiDeg = sps0 };
            }
```

```csharp
                // singular normal equations inside the loop
                if (d == null) return new LocalizerResult { ok = false, pose = previous ?? p, nObs = used.Count, residualRms = ResidualRms(previous ?? p, used), iterations = iter };
```

(이 경로는 σ 를 채우지 않는다 — 풀이가 실패한 자세의 정밀도는 의미가 없다. `null` 인 채로 두면 §3.4 가 `LOST` 로 다룬다.)

```csharp
            double rms = ResidualRms(p, used);
            var (sxy, sps) = Sigma(p, used, wr, wt, wa);
            return new LocalizerResult { ok = true, pose = p, nObs = used.Count, residualRms = rms,
                iterations = Math.Min(iter, MaxIterations), sigmaXy = sxy, sigmaPsiDeg = sps };
```

기존 `double wr = Weight(...)` 한 줄은 지운다(위로 옮겼으므로 중복 선언이 된다).

- [ ] **Step 6: 테스트 통과 확인**

Expected: `LocalizerSigmaTests` 6 개 PASS, 기존 EditMode 테스트(102 개) 전부 그대로 PASS

- [ ] **Step 7: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Localization/ unity/Assets/ShipHdMap/Tests/EditMode/LocalizerSigmaTests.cs
git commit   # feat: report the achievable sigma alongside the pose
```

---

### Task 2: BeliefMonitor — 상태 기계·예산·자취

판정의 전부가 여기 들어간다. `UnityEngine` 을 import 하지 않으므로 EditMode 테스트가 씬도 프레임도 없이 돈다.

**스펙 §2 의 "순수 정적 클래스"는 부정확했다.** 상태 기계는 인스턴스 상태를 갖는다. 정적이 아니라 **UnityEngine 비의존 인스턴스 클래스**다. 테스트가 프레임 없이 돈다는 성질은 그대로다.

**자취는 자세가 아니라 호길이 `s` 다.** `VehicleController` 가 경로를 호길이로 매개화하고 `Rewind(double arcLength)` 를 이미 갖고 있다 — `s` 자체가 경로 이력이므로, 되짚기는 `s` 를 줄이는 것이고 자취에 좌표를 담을 이유가 없다. 경로가 바뀌면(`StartPath`) 자취는 `Reset()` 으로 비운다. 주차 경로에서 잃으면 그 경로의 `s = 0` 까지 되짚는데, 그 지점이 곧 차로다.

**자취는 `Ok` 뿐 아니라 `Degraded` 동안에도 기록한다.** 자취의 쓸모는 "이미 지나왔으니 장애물이 없다"와 "여기서는 마커가 보였다" 둘인데, `Degraded` 는 둘 다 만족한다 — 약속보다 나빴을 뿐 측위는 됐다. 되짚어 들어가면 오히려 먼저 회복된다.

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Vehicle/BeliefMonitor.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/BeliefMonitorTests.cs`

**Interfaces:**
- Consumes: 없음 (순수)
- Produces: `enum BeliefState { Ok, Degraded, Lost, Backtracking, Stopped }`
- Produces: `class BeliefParams { double k, driftRate, budgetM, maxLostM, trailM, trailStepM; int frames; }`
- Produces: `class BeliefMonitor` — 생성자 `BeliefMonitor(BeliefParams)`; `Step(double? sigmaXy, double? predictedSigmaXy, double pathS, double movedM)`; 읽기 전용 `State`, `LostM`, `SigmaOdo`, `TrailM`, `BacktrackTargetS` (`double?`); `ReachedBacktrackTarget()`; `Reset()`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/BeliefMonitorTests.cs`:

```csharp
using NUnit.Framework;
using ShipHdMap;

public class BeliefMonitorTests
{
    static BeliefParams P() => new BeliefParams();   // spec defaults: k 2, frames 5, drift .05, budget 1, maxLost 5, trail 20, step .25

    /// Drive forward with a healthy solve; arc length advances with the distance travelled.
    static BeliefMonitor Healthy(double metres, BeliefParams p = null)
    {
        var m = new BeliefMonitor(p ?? P());
        for (double s = 0; s < metres; s += 0.25) m.Step(0.30, 0.30, s, 0.25);
        return m;
    }

    /// Keep driving with nothing visible, starting from arc length s0. Returns where it ended up.
    static double GoBlind(BeliefMonitor m, double s0, int ticks)
    {
        double s = s0;
        for (int i = 0; i < ticks; i++) { m.Step(null, 0.30, s, 0.25); s += 0.25; }
        return s;
    }

    [Test]
    public void StartsOkAndStaysOkWhenTheSolveMatchesThePrediction()
    {
        var m = Healthy(10);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
        Assert.That(m.LostM, Is.Zero);
    }

    [Test]
    public void NoSigmaIsLostImmediately()
    {
        var m = Healthy(10);
        m.Step(null, 0.30, 10, 0.25);
        Assert.That(m.State, Is.EqualTo(BeliefState.Lost));
    }

    /// Worse than the map promised, but only after N frames in a row: one bad frame is noise.
    [Test]
    public void DegradedNeedsNConsecutiveFrames()
    {
        var m = Healthy(10);
        var p = P();
        for (int i = 0; i < p.frames - 1; i++) m.Step(0.90, 0.30, 10, 0);   // 3x the promise
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok), "N-1 frames must not trip it");
        m.Step(0.90, 0.30, 10, 0);
        Assert.That(m.State, Is.EqualTo(BeliefState.Degraded));
    }

    [Test]
    public void OneGoodFrameResetsTheDegradedCount()
    {
        var m = Healthy(10);
        var p = P();
        for (int i = 0; i < p.frames - 1; i++) m.Step(0.90, 0.30, 10, 0);
        m.Step(0.31, 0.30, 10, 0);                                          // within k
        for (int i = 0; i < p.frames - 1; i++) m.Step(0.90, 0.30, 10, 0);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
    }

    /// Nothing to compare against means nothing to be suspicious of; the blind path handles it.
    [Test]
    public void NoPredictionMeansNoDegradedJudgement()
    {
        var m = Healthy(10);
        for (int i = 0; i < 20; i++) m.Step(9.0, null, 10, 0);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
    }

    [Test]
    public void LostRecoversWhenMarkersComeBack()
    {
        var m = Healthy(10);
        double s = GoBlind(m, 10, 2);
        Assert.That(m.State, Is.EqualTo(BeliefState.Lost));
        m.Step(0.35, 0.30, s, 0.25);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
        Assert.That(m.LostM, Is.Zero, "the counter resets on recovery");
    }

    /// At the default 5 %/m the distance limit (5 m) binds before the sigma budget (1 m needs 20 m).
    [Test]
    public void DistanceLimitTripsBacktrackingAtDefaults()
    {
        var m = Healthy(10);
        GoBlind(m, 10, 21);                                                 // 5.25 m lost
        Assert.That(m.State, Is.EqualTo(BeliefState.Backtracking));
        Assert.That(m.LostM, Is.GreaterThan(5.0));
        Assert.That(m.SigmaOdo, Is.LessThan(P().budgetM), "the sigma budget did not fire");
    }

    /// Crank the drift and the sigma budget binds first instead -- both regimes must be reachable.
    [Test]
    public void SigmaBudgetTripsBacktrackingWhenDriftIsHigh()
    {
        var p = P(); p.driftRate = 0.5;                                     // 1.0 m of budget after 2 m
        var m = Healthy(10, p);
        GoBlind(m, 10, 9);                                                  // 2.25 m lost
        Assert.That(m.State, Is.EqualTo(BeliefState.Backtracking));
        Assert.That(m.LostM, Is.LessThan(p.maxLostM), "the distance limit did not fire");
        Assert.That(m.SigmaOdo, Is.GreaterThan(p.budgetM));
    }

    /// Backtracking hands back arc lengths, newest first, so the vehicle retraces the path it drove.
    [Test]
    public void BacktrackWalksTheTrailBackwards()
    {
        var m = Healthy(10);
        GoBlind(m, 10, 21);
        Assert.That(m.State, Is.EqualTo(BeliefState.Backtracking));
        double prev = double.MaxValue;
        for (int i = 0; i < 10; i++)
        {
            var t = m.BacktrackTargetS;
            Assert.That(t, Is.Not.Null);
            Assert.That(t.Value, Is.LessThan(prev), "targets must march backwards");
            prev = t.Value;
            m.ReachedBacktrackTarget();
        }
    }

    [Test]
    public void RunningOutOfTrailStops()
    {
        var p = P(); p.trailM = 1.0;                                        // a handful of entries
        var m = Healthy(10, p);
        double s = GoBlind(m, 10, 21);
        while (m.BacktrackTargetS != null) m.ReachedBacktrackTarget();
        m.Step(null, 0.30, s, 0.25);
        Assert.That(m.State, Is.EqualTo(BeliefState.Stopped));
    }

    [Test]
    public void StoppedIsTerminalUntilReset()
    {
        var p = P(); p.trailM = 1.0;
        var m = Healthy(10, p);
        double s = GoBlind(m, 10, 21);
        while (m.BacktrackTargetS != null) m.ReachedBacktrackTarget();
        m.Step(null, 0.30, s, 0.25);
        m.Step(0.30, 0.30, s, 0.25);                                        // even a good solve must not revive it
        Assert.That(m.State, Is.EqualTo(BeliefState.Stopped));
    }

    [Test]
    public void BacktrackingRecoversWhenMarkersReturn()
    {
        var m = Healthy(10);
        GoBlind(m, 10, 21);
        Assert.That(m.State, Is.EqualTo(BeliefState.Backtracking));
        m.Step(0.40, 0.30, 12, 0.25);
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
    }

    /// The trail is why backtracking is safe: it is road already driven. Degraded stretches count too --
    /// the solve worked there, it was merely worse than promised, so backtracking into one recovers sooner.
    [Test]
    public void TheTrailIsCappedAndRecordsDegradedToo()
    {
        var p = P(); p.trailM = 2.0;
        var m = new BeliefMonitor(p);
        for (double s = 0; s < 20; s += 0.25) m.Step(0.90, 0.30, s, 0.25);   // all Degraded after N frames
        Assert.That(m.State, Is.EqualTo(BeliefState.Degraded));
        Assert.That(m.TrailM, Is.LessThanOrEqualTo(p.trailM + 1e-9));
        Assert.That(m.TrailM, Is.GreaterThan(1.0), "a degraded stretch still lays trail");
    }

    [Test]
    public void ResetClearsEverything()
    {
        var m = Healthy(10);
        m.Step(null, 0.30, 10, 0.25);
        m.Reset();
        Assert.That(m.State, Is.EqualTo(BeliefState.Ok));
        Assert.That(m.TrailM, Is.Zero);
        Assert.That(m.BacktrackTargetS, Is.Null);
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

Expected: 컴파일 실패 — `BeliefMonitor`, `BeliefState`, `BeliefParams` 가 없다

- [ ] **Step 3: `BeliefMonitor` 를 만든다**

`unity/Assets/ShipHdMap/Runtime/Vehicle/BeliefMonitor.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    public enum BeliefState { Ok, Degraded, Lost, Backtracking, Stopped }

    /// Spec M5d §3.4-3.6. Wire values are already converted: everything here is metres and plain ratios.
    [Serializable]
    public class BeliefParams
    {
        public double k = 2.0;            // "worse than the map promised" multiplier
        public int frames = 5;            // consecutive frames before Degraded trips
        public double driftRate = 0.05;   // odometry sigma growth per metre travelled
        public double budgetM = 1.0;      // odometry sigma that ends the wait
        public double maxLostM = 5.0;     // distance that ends the wait, whichever comes first
        public double trailM = 20.0;      // how far back the escape route reaches
        public double trailStepM = 0.25;  // trail recording interval
    }

    /// Decides whether the vehicle still knows where it is, and how far to retreat when it does not.
    /// The trail is arc length on the vehicle's current path: VehicleController is parameterised by s and already
    /// has Rewind(s), so retracing is simply driving s down. Deliberately free of UnityEngine, so EditMode tests
    /// run without a scene or a frame.
    public class BeliefMonitor
    {
        readonly BeliefParams _p;
        readonly List<double> _trail = new();   // arc lengths, oldest first
        int _degraded;
        double _sinceRecord;

        public BeliefMonitor(BeliefParams p) { _p = p ?? new BeliefParams(); }

        public BeliefState State { get; private set; } = BeliefState.Ok;
        /// Distance travelled since the belief went Lost. Reset on recovery.
        public double LostM { get; private set; }
        /// Odometry uncertainty accumulated over LostM.
        public double SigmaOdo => _p.driftRate * LostM;
        /// Length of escape route currently held.
        public double TrailM => _trail.Count < 2 ? 0 : _trail[_trail.Count - 1] - _trail[0];
        /// Arc length to drive back to while backtracking; null when the trail is spent.
        public double? BacktrackTargetS => _trail.Count > 0 ? _trail[_trail.Count - 1] : (double?)null;

        public void ReachedBacktrackTarget() { if (_trail.Count > 0) _trail.RemoveAt(_trail.Count - 1); }

        /// Call on a new vehicle or whenever the path changes -- arc lengths from the old path mean nothing on a new one.
        public void Reset() { State = BeliefState.Ok; LostM = 0; _degraded = 0; _sinceRecord = 0; _trail.Clear(); }

        /// One tick. sigmaXy is null when the solve produced nothing; predictedSigmaXy is null where the coverage
        /// map says the cell is blind, in which case there is nothing to hold the reading against.
        public void Step(double? sigmaXy, double? predictedSigmaXy, double pathS, double movedM)
        {
            if (State == BeliefState.Stopped) return;

            if (sigmaXy.HasValue)
            {
                // Any state recovers the moment a solve comes back: that is the whole point of retreating.
                LostM = 0;
                Record(pathS, movedM);
                bool worseThanPromised = predictedSigmaXy.HasValue && sigmaXy.Value > _p.k * predictedSigmaXy.Value;
                _degraded = worseThanPromised ? _degraded + 1 : 0;
                State = _degraded >= _p.frames ? BeliefState.Degraded : BeliefState.Ok;
                return;
            }

            _degraded = 0;
            if (State == BeliefState.Ok || State == BeliefState.Degraded) { State = BeliefState.Lost; LostM = 0; }

            if (State == BeliefState.Lost)
            {
                LostM += movedM;
                if (LostM > _p.maxLostM || SigmaOdo > _p.budgetM) State = BeliefState.Backtracking;
                return;
            }

            // Backtracking: MapRuntime drives the trail down; we only notice when it runs out.
            if (_trail.Count == 0) State = BeliefState.Stopped;
        }

        void Record(double pathS, double movedM)
        {
            _sinceRecord += movedM;
            if (_trail.Count > 0 && _sinceRecord < _p.trailStepM) return;
            _sinceRecord = 0;
            _trail.Add(pathS);
            int cap = Math.Max(2, (int)Math.Round(_p.trailM / _p.trailStepM) + 1);
            while (_trail.Count > cap) _trail.RemoveAt(0);
        }
    }
}
```

- [ ] **Step 4: 테스트 통과 확인**

Expected: `BeliefMonitorTests` 14 개 PASS

- [ ] **Step 5: 스펙의 부정확한 한 줄을 고친다**

`docs/superpowers/specs/2026-09-17-m5d-localization-loss-design.md` §2 의 마지막 문단에서 "순수 정적 클래스" 를 "UnityEngine 을 모르는 순수 C# 클래스(상태 기계라 인스턴스 상태를 갖는다)" 로 고치고, §3.6 의 "자세를 링버퍼에 넣는다" 를 "호길이 `s` 를 링버퍼에 넣는다 — `VehicleController` 가 이미 `s` 로 매개화돼 있고 `Rewind(s)` 를 갖고 있다" 로 고친다.

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Vehicle/BeliefMonitor.cs unity/Assets/ShipHdMap/Tests/EditMode/BeliefMonitorTests.cs docs/superpowers/specs/2026-09-17-m5d-localization-loss-design.md
git commit   # feat: belief state machine with an odometry budget and an arc-length backtrack trail
```

---

### Task 3: 마커 가림

훼손·적재 차폐를 손으로 만든다. 센서만 잃고 지도는 그대로여서, `DEGRADED` 가 실제로 일하는 장면을 만들 수 있다.

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Localization/LandmarkSensor.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/LocalizerSigmaTests.cs` (이어 붙인다)

**Interfaces:**
- Produces: `LandmarkSensor.occluded` (`HashSet<string>`, 기본 비어 있음)
- Produces: `BridgeMessages.SetOccluded = "SetOccluded"`, `class SetOccludedMsg { public string[] ids; }`
- Produces: `MapRuntime.SetOccluded(string json)`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`LocalizerSigmaTests.cs` 에 이어 붙인다:

```csharp
    [Test]
    public void OccludedMarkersAreNotSeen()
    {
        var lm = At("a", 10, 0);
        var sensor = new UnityEngine.GameObject("s").AddComponent<LandmarkSensor>();
        var map = new Dictionary<string, LandmarkRef> { [lm.id] = lm };
        var truth = new Pose2D { x = 0, y = 0, psiRad = 0 };
        UnityEngine.Vector3 PosOf(string id) => new UnityEngine.Vector3((float)lm.mx, 1.2f, -(float)lm.my);

        Assert.That(sensor.Sense(truth, map, PosOf), Has.Count.EqualTo(1));
        sensor.occluded.Add("a");
        Assert.That(sensor.Sense(truth, map, PosOf), Is.Empty, "a damaged marker is simply not detected");
        UnityEngine.Object.DestroyImmediate(sensor.gameObject);
    }
```

- [ ] **Step 2: 실패를 확인한다**

Expected: 컴파일 실패 — `LandmarkSensor` 에 `occluded` 가 없다

- [ ] **Step 3: 센서가 가려진 마커를 건너뛴다**

`LandmarkSensor.cs` 의 필드에 더한다:

```csharp
        /// Markers the sensor must pretend not to see: damage, or a parked car in the way. The map still has them,
        /// which is the point -- the coverage prediction keeps promising them and the belief monitor notices.
        public HashSet<string> occluded = new();
```

`Sense` 의 반복 첫 줄에 한 줄:

```csharp
            foreach (var lm in map.Values)
            {
                if (occluded.Contains(lm.id)) continue;
                if (!IsVisibleGeometric(truth, lm, fov, maxDist, mva)) continue;
```

`using System.Collections.Generic;` 는 이미 있다.

- [ ] **Step 4: 브리지 메시지를 더한다**

`BridgeMessages.cs` 의 `Load = "Load", …` 상수 줄 끝에 `SetOccluded` 를 더하고, 타입을 하나 더한다:

```csharp
    public class SetOccludedMsg { public string[] ids; }
```

`MapRuntime.cs` 의 `SetNoise` 아래에:

```csharp
        /// Not persisted anywhere: damage is a fact about this moment, not about the map (spec §4).
        public void SetOccluded(string json)
        {
            var m = MapJson.Parse<SetOccludedMsg>(json);
            Sensor.occluded.Clear();
            if (m?.ids != null) foreach (var id in m.ids) Sensor.occluded.Add(id);
        }
```

- [ ] **Step 5: 테스트 통과 확인**

Expected: `LocalizerSigmaTests` 7 개 PASS

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Localization/LandmarkSensor.cs unity/Assets/ShipHdMap/Runtime/Bridge/ unity/Assets/ShipHdMap/Tests/EditMode/LocalizerSigmaTests.cs
git commit   # feat: let a marker be occluded without removing it from the map
```

---

### Task 4: 예측 격자

M5c 가 계산한 σ 를 차량이 들고 다닌다. **Unity 는 HTTP 를 하지 않는다** — 웹이 `POST …/coverage` 로 받아 `{x, y, s}` 셋만 남기고 브리지로 밀어 넣는다(2,880 셀 기준 약 60 KB, `Load` 가 보내는 지도보다 작다).

조회는 격자가 규칙적이라 인덱스 산술로 O(1) 이다. 응답의 `cells` 는 윤곽 안·기둥 밖만 담은 성긴 격자이므로 사전으로 받는다.

**Files:**
- Create: `unity/Assets/ShipHdMap/Runtime/Vehicle/PredictionGrid.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/BeliefMonitorTests.cs` (이어 붙인다)

**Interfaces:**
- Consumes: 없음
- Produces: `class PredictionGrid` — 생성자 `PredictionGrid(double[] bbox, double gridM, IEnumerable<(double x, double y, double? s)> cells)`; `double? SigmaAt(double x, double y)`
- Produces: `BridgeMessages.SetPrediction = "SetPrediction"`, `class PredCellMsg { public double x, y; public double? s; }`, `class SetPredictionMsg { public double grid_m; public double[] bbox; public PredCellMsg[] cells; }`
- Produces: `MapRuntime.SetPrediction(string json)`, `MapRuntime.Prediction` (읽기 전용)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`BeliefMonitorTests.cs` 에 이어 붙인다:

```csharp
    static PredictionGrid Grid()
    {
        // 2 m grid over [0,10] x [0,4]; centres at 1,3,5,7,9 and 1,3
        var cells = new System.Collections.Generic.List<(double, double, double?)>();
        for (double y = 1; y < 4; y += 2)
            for (double x = 1; x < 10; x += 2)
                cells.Add((x, y, x < 5 ? (double?)0.30 : null));    // the far half is blind
        return new PredictionGrid(new[] { 0.0, 0.0, 10.0, 4.0 }, 2.0, cells);
    }

    [Test]
    public void PredictionLooksUpTheNearestCell()
    {
        var g = Grid();
        Assert.That(g.SigmaAt(1.1, 1.1), Is.EqualTo(0.30).Within(1e-9));
        Assert.That(g.SigmaAt(2.4, 0.2), Is.EqualTo(0.30).Within(1e-9), "still inside the first cell");
        Assert.That(g.SigmaAt(9.0, 3.0), Is.Null, "blind cells report null, not zero");
    }

    [Test]
    public void PredictionOutsideTheGridIsNull()
    {
        var g = Grid();
        Assert.That(g.SigmaAt(-5, 1), Is.Null);
        Assert.That(g.SigmaAt(50, 1), Is.Null);
    }

    [Test]
    public void AnEmptyPredictionAnswersNull()
    {
        var g = new PredictionGrid(new[] { 0.0, 0.0, 1.0, 1.0 }, 1.0,
            new System.Collections.Generic.List<(double, double, double?)>());
        Assert.That(g.SigmaAt(0.5, 0.5), Is.Null);
    }
```

- [ ] **Step 2: 실패를 확인한다**

Expected: 컴파일 실패 — `PredictionGrid` 가 없다

- [ ] **Step 3: `PredictionGrid` 를 만든다**

`unity/Assets/ShipHdMap/Runtime/Vehicle/PredictionGrid.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace ShipHdMap
{
    /// What the coverage map promised, indexed for O(1) lookup while driving (spec M5d §3.2).
    /// Sparse on purpose: the API only evaluates cells inside the deck outline and outside the pillars.
    public class PredictionGrid
    {
        readonly Dictionary<long, double?> _byCell = new();
        readonly double _x0, _y0, _grid;

        public PredictionGrid(double[] bbox, double gridM, IEnumerable<(double x, double y, double? s)> cells)
        {
            _grid = gridM > 0 ? gridM : 1.0;
            _x0 = bbox != null && bbox.Length >= 2 ? bbox[0] : 0;
            _y0 = bbox != null && bbox.Length >= 2 ? bbox[1] : 0;
            if (cells != null) foreach (var c in cells) _byCell[Key(c.x, c.y)] = c.s;
        }

        /// The promised sigma_xy at (x, y), or null where the map says blind -- or where we have no cell at all.
        /// Those two are deliberately the same answer: both mean "no promise to hold this reading against".
        public double? SigmaAt(double x, double y) => _byCell.TryGetValue(Key(x, y), out var s) ? s : null;

        long Key(double x, double y)
        {
            long ix = (long)Math.Floor((x - _x0) / _grid);
            long iy = (long)Math.Floor((y - _y0) / _grid);
            return (ix << 32) ^ (iy & 0xffffffffL);
        }
    }
}
```

- [ ] **Step 4: 브리지로 받는다**

`BridgeMessages.cs` 에 `SetPrediction` 상수와 두 타입을 더한다:

```csharp
    public class PredCellMsg { public double x, y; public double? s; }   // s null = the map says blind here
    public class SetPredictionMsg { public double grid_m = 1; public double[] bbox; public PredCellMsg[] cells; }
```

`MapRuntime.cs` 에:

```csharp
        public PredictionGrid Prediction { get; private set; }

        /// The coverage map for the deck being driven. The web fetches it (Unity does not do HTTP) and pushes it
        /// once per scenario; Ship Frame is invariant, so it only goes stale when a marker is added or occluded.
        public void SetPrediction(string json)
        {
            var m = MapJson.Parse<SetPredictionMsg>(json);
            var cells = new List<(double, double, double?)>();
            if (m?.cells != null) foreach (var c in m.cells) cells.Add((c.x, c.y, c.s));
            Prediction = new PredictionGrid(m?.bbox, m?.grid_m ?? 1, cells);
        }
```

- [ ] **Step 5: 테스트 통과 확인**

Expected: `BeliefMonitorTests` 17 개 PASS (기존 14 + 신규 3)

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Vehicle/PredictionGrid.cs unity/Assets/ShipHdMap/Runtime/Bridge/
git commit   # feat: carry the coverage prediction into the vehicle
```

---

### Task 5: MapRuntime 배선

`BeliefMonitor` 를 실제 주행에 붙이고, 역추적을 `Rewind` 로 실행하고, 상태를 웹으로 내보낸다.

**Files:**
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/BridgeMessages.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Test: `unity/Assets/ShipHdMap/Tests/EditMode/BeliefRunTests.cs` (신규)

**Interfaces:**
- Consumes: Task 1 의 `LocalizerResult.sigmaXy`, Task 2 의 `BeliefMonitor`, Task 4 의 `PredictionGrid`
- Produces: `BridgeMessages.SetBeliefParams = "SetBeliefParams"`, `OnBelief = "onBelief"`
- Produces: `class BeliefEvt { public string state; public double? sigma_xy, sigma_psi, predicted_sigma_xy; public int n_obs; public double lost_m, sigma_odo, trail_m; }`
- Produces: `MapRuntime.SetBeliefParams(string json)`, `MapRuntime.Belief` (읽기 전용 `BeliefMonitor`)

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`unity/Assets/ShipHdMap/Tests/EditMode/BeliefRunTests.cs`:

```csharp
using NUnit.Framework;
using ShipHdMap;

/// Wiring-level checks: the monitor must actually see the solve, and backtracking must move the vehicle.
public class BeliefRunTests
{
    [Test]
    public void ABlindStretchTakesTheBeliefThroughLostToBacktracking()
    {
        var rt = MapRuntimeTestHarness.LoadFixture();       // same helper the other run tests use
        rt.SetBeliefParams("{\"max_lost_m\":1.0}");          // trip quickly
        rt.StartScenario("{\"mode\":\"load\"}");

        // occlude every marker: the deck goes blind wherever the vehicle is
        var ids = new System.Collections.Generic.List<string>();
        foreach (var k in rt.MapRefs.Keys) ids.Add("\"" + k + "\"");
        rt.SetOccluded("{\"ids\":[" + string.Join(",", ids) + "]}");

        for (int i = 0; i < 400 && rt.Belief.State != BeliefState.Backtracking; i++) rt.Step(0.05f);
        Assert.That(rt.Belief.State, Is.EqualTo(BeliefState.Backtracking));
        Assert.That(rt.Belief.LostM, Is.GreaterThan(1.0));
    }

    [Test]
    public void BacktrackingDrivesTheArcLengthDown()
    {
        var rt = MapRuntimeTestHarness.LoadFixture();
        rt.SetBeliefParams("{\"max_lost_m\":1.0}");
        rt.StartScenario("{\"mode\":\"load\"}");
        for (int i = 0; i < 100; i++) rt.Step(0.05f);        // build a trail while markers are visible
        double sBefore = rt.Vehicle.s;

        var ids = new System.Collections.Generic.List<string>();
        foreach (var k in rt.MapRefs.Keys) ids.Add("\"" + k + "\"");
        rt.SetOccluded("{\"ids\":[" + string.Join(",", ids) + "]}");

        for (int i = 0; i < 400 && rt.Belief.State != BeliefState.Backtracking; i++) rt.Step(0.05f);
        for (int i = 0; i < 100; i++) rt.Step(0.05f);
        Assert.That(rt.Vehicle.s, Is.LessThan(sBefore), "the vehicle must retrace, not push on");
    }

    /// The payoff: uncovering the markers again puts the belief back together without human help.
    [Test]
    public void UncoveringTheMarkersRecoversTheBelief()
    {
        var rt = MapRuntimeTestHarness.LoadFixture();
        rt.SetBeliefParams("{\"max_lost_m\":1.0}");
        rt.StartScenario("{\"mode\":\"load\"}");
        for (int i = 0; i < 100; i++) rt.Step(0.05f);

        var ids = new System.Collections.Generic.List<string>();
        foreach (var k in rt.MapRefs.Keys) ids.Add("\"" + k + "\"");
        rt.SetOccluded("{\"ids\":[" + string.Join(",", ids) + "]}");
        for (int i = 0; i < 400 && rt.Belief.State != BeliefState.Backtracking; i++) rt.Step(0.05f);

        rt.SetOccluded("{\"ids\":[]}");
        for (int i = 0; i < 40; i++) rt.Step(0.05f);
        Assert.That(rt.Belief.State, Is.EqualTo(BeliefState.Ok));
    }

    [Test]
    public void ANormalLaneRunNeverLosesTheBelief()
    {
        var rt = MapRuntimeTestHarness.LoadFixture();
        rt.StartScenario("{\"mode\":\"load\"}");
        for (int i = 0; i < 200; i++)
        {
            rt.Step(0.05f);
            if (rt.ScenarioPhase != MapRuntime.Phase.OnLane) continue;
            Assert.That(rt.Belief.State, Is.Not.EqualTo(BeliefState.Lost), "the lane is 1.7 % blind; a lane run must not trip");
        }
    }
}
```

**`MapRuntimeTestHarness.LoadFixture()` 는 기존 셋업을 뽑아낸 것이다.** `ScenarioRunTests` 가 픽스처를 올려 `MapRuntime` 을 세우는 부분이 이미 있다 — 그것을 `static MapRuntime LoadFixture()` 로 빼내고 두 테스트 파일이 함께 부른다. 반환값이 `MapRuntime` 이므로 `Vehicle`, `MapRefs`, `ScenarioPhase`, `Step(float)`, `StartScenario`, `SetOccluded`, `SetBeliefParams`, `Belief` 이 전부 그대로 쓰인다. `ScenarioRunTests` 의 기존 케이스는 호출만 바꾸고 단언은 건드리지 않는다 — 그 파일이 그대로 통과하는 것이 추출이 옳았다는 확인이다.

- [ ] **Step 2: 실패를 확인한다**

Expected: 컴파일 실패 — `rt.Belief`, `rt.SetBeliefParams` 가 없다

- [ ] **Step 3: 메시지 타입을 더한다**

`BridgeMessages.cs`:

```csharp
    public class SetBeliefParamsMsg
    {
        public double k = 2.0, drift_rate = 0.05, budget_m = 1.0, max_lost_m = 5.0, trail_m = 20.0;
        public int frames = 5;
    }
    /// `state`: ok | degraded | lost | backtracking | stopped. sigma fields are null when nothing was solved
    /// or the coverage map calls this cell blind.
    public class BeliefEvt
    {
        public string state; public int n_obs;
        public double? sigma_xy, sigma_psi, predicted_sigma_xy;
        public double lost_m, sigma_odo, trail_m;
    }
```

상수 줄에 `SetBeliefParams = "SetBeliefParams"` 와 `OnBelief = "onBelief"` 를 더한다.

- [ ] **Step 4: `MapRuntime` 에 배선한다**

필드와 메시지 핸들러:

```csharp
        public BeliefMonitor Belief { get; private set; } = new BeliefMonitor(new BeliefParams());
        BeliefParams _beliefParams = new BeliefParams();
        double _lastS;    // arc length at the previous tick, to measure how far we moved

        public void SetBeliefParams(string json)
        {
            var m = MapJson.Parse<SetBeliefParamsMsg>(json) ?? new SetBeliefParamsMsg();
            _beliefParams = new BeliefParams { k = m.k, frames = m.frames, driftRate = m.drift_rate,
                budgetM = m.budget_m, maxLostM = m.max_lost_m, trailM = m.trail_m };
            Belief = new BeliefMonitor(_beliefParams);
        }
```

`Localize()` 의 끝, `Hud.Set(...)` 바로 앞에 판정을 붙인다:

```csharp
            double moved = Math.Abs(Vehicle.s - _lastS); _lastS = Vehicle.s;
            double? predicted = Prediction?.SigmaAt(truth.x, truth.y);
            Belief.Step(_lastRes.ok ? _lastRes.sigmaXy : null, predicted, Vehicle.s, moved);
```

`StepScenario` 의 맨 앞에 역추적 주행을 끼운다 — 어느 단계든 역추적이 우선한다:

```csharp
            if (Belief.State == BeliefState.Backtracking)
            {
                var target = Belief.BacktrackTargetS;
                if (target.HasValue)
                {
                    // Retracing the path we drove: no steering decision is needed, and none could be trusted anyway.
                    double back = Math.Max(target.Value, Vehicle.s - ScenarioPlanner.ParkSpeedMps * 0.5 * dt);
                    Vehicle.Rewind(back);
                    if (Vehicle.s <= target.Value + 1e-6) Belief.ReachedBacktrackTarget();
                }
                return;
            }
            if (Belief.State == BeliefState.Stopped) return;
```

`StepScenario` 가 `dt` 를 받지 않으면 `Step(float dt)` 에서 넘기도록 시그니처를 고친다.

경로가 바뀌는 자리마다 `Belief.Reset()` 과 `_lastS = 0` 을 넣는다 — `NextVehicle()`, 그리고 `Vehicle.StartPath`/`StartLane` 을 부르는 모든 지점. 호길이는 경로마다 의미가 다르므로 이월하면 안 된다.

emit 은 기존 0.2 초 주기 안에서 `onLocalization` 옆에:

```csharp
                Send(BridgeMessages.OnBelief, MapJson.Serialize(new BeliefEvt {
                    state = Belief.State.ToString().ToLowerInvariant(), n_obs = _lastRes?.nObs ?? 0,
                    sigma_xy = _lastRes?.sigmaXy, sigma_psi = _lastRes?.sigmaPsiDeg,
                    predicted_sigma_xy = Prediction?.SigmaAt(Vehicle.Truth.x, Vehicle.Truth.y),
                    lost_m = Belief.LostM, sigma_odo = Belief.SigmaOdo, trail_m = Belief.TrailM }));
```

- [ ] **Step 5: 테스트 통과 확인**

Expected: `BeliefRunTests` 4 개 PASS, 기존 EditMode 전부 PASS

`ANormalLaneRunNeverLosesTheBelief` 가 실패하면 예측 격자가 안 들어온 것이다 — 이 테스트는 예측 없이도 통과해야 한다(`predicted == null` 이면 `Degraded` 판정을 안 하고, 차로는 마커가 보이므로 `Lost` 도 아니다).

- [ ] **Step 6: 커밋**

```bash
git add unity/Assets/ShipHdMap/Runtime/Bridge/ unity/Assets/ShipHdMap/Tests/EditMode/
git commit   # feat: drive the belief state machine and retrace the path when it is lost
```

---

### Task 6: 도달 불가 구획

회복에 실패하면 그 구획을 포기하고 다음으로 간다. §6.6 이 "회복 시간이 곧 경제성"이라 적은 부분이다.

**Files:**
- Create: `api/src/main/resources/db/migration/V2__unreachable.sql`
- Modify: `api/src/main/java/com/shiphdmap/api/slots/SlotRepo.java`
- Modify: `unity/Assets/ShipHdMap/Runtime/Vehicle/ScenarioPlanner.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Ship/MapOverlay.cs`
- Modify: `unity/Assets/ShipHdMap/Runtime/Bridge/MapRuntime.cs`
- Modify: `docs/api-contract.md`
- Test: `api/src/test/java/com/shiphdmap/api/slots/SlotGenerateTests.java` (이어 붙인다), `unity/…/Tests/EditMode/ScenarioPlannerTests.cs` (이어 붙인다)

**Interfaces:**
- Consumes: Task 5 의 `BeliefState.Stopped`
- Produces: `parking_slot.status` 에 `'unreachable'`
- Produces: `ScenarioPlanner.IsFilled` 가 `unreachable` 도 참으로 본다

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`ScenarioPlannerTests.cs` 에:

```csharp
    [Test]
    public void UnreachableSlotsAreNotOfferedAgain()
    {
        Assert.That(ScenarioPlanner.IsFilled("unreachable"), Is.True);
        var slots = new List<ParkingSlot> { Slot("a", 1, "unreachable"), Slot("b", 2, "empty") };
        Assert.That(ScenarioPlanner.NextSlot(slots, "load").id, Is.EqualTo("b"));
    }
```

`SlotGenerateTests.java` 에:

```java
	@Test
	void unreachableIsAStorableStatus() {
		var body = Map.of("status", "unreachable");
		assertThat(rest.patchForObject("/api/datasets/roro-demo-01/slots/PS-D3-001", body, Map.class))
			.containsEntry("status", "unreachable");
	}
```

(PATCH 엔드포인트의 실제 경로와 호출 방식은 `SlotController` 의 기존 상태 갱신 경로에 맞춘다. 없다면 이 테스트 대신 `SlotRepo` 단위로 저장·조회를 확인한다.)

- [ ] **Step 2: 실패를 확인한다**

Run: `cd api && ./gradlew test --tests '*SlotGenerate*'`
Expected: FAIL — CHECK 제약 위반

- [ ] **Step 3: 마이그레이션을 더한다**

`api/src/main/resources/db/migration/V2__unreachable.sql` — 이 프로젝트의 첫 V2 다:

```sql
-- M5d: a slot the vehicle could not reach at all, as distinct from one it reached badly (needs_adjust).
ALTER TABLE parking_slot DROP CONSTRAINT parking_slot_status_check;
ALTER TABLE parking_slot ADD CONSTRAINT parking_slot_status_check
  CHECK (status IN ('empty', 'filled', 'needs_adjust', 'unreachable'));
```

`SlotRepo` 에 status 화이트리스트가 있다면 거기에도 더한다.

- [ ] **Step 4: 시나리오가 포기하고 넘어간다**

`ScenarioPlanner.IsFilled` 를:

```csharp
        /// A slot the planner must not offer again: parked in it, parked badly in it, or could not get to it.
        public static bool IsFilled(string status) => status == "filled" || status == "needs_adjust" || status == "unreachable";
```

`MapRuntime` 에 재시도 한 번을 넣는다. 필드 하나와 `StepScenario` 의 `Stopped` 분기:

```csharp
        string _retriedSlot;   // the slot we have already given one more go

        // ... in StepScenario, replacing the bare `if (Belief.State == BeliefState.Stopped) return;`
            if (Belief.State == BeliefState.Stopped)
            {
                if (_target == null) return;
                if (_retriedSlot != _target.id)
                {
                    _retriedSlot = _target.id;            // spec §3.7: one retry before giving up
                    Belief.Reset(); _lastS = 0;
                    Vehicle.Rewind(0);
                    return;
                }
                _target.status = "unreachable";
                MapOverlay.SetStatus(_overlay, _target.id, "unreachable");
                Send(BridgeMessages.OnSlotFilled, MapJson.Serialize(new SlotFilledEvt { slot_id = _target.id, status = "unreachable" }));
                NextVehicle();
                return;
            }
```

`MapOverlay.SetStatus` 의 색 매핑에 한 줄:

```csharp
            var c = status switch {
                "filled" => new Color(0.2f, 0.5f, 1f, 0.45f),
                "needs_adjust" => new Color(1f, 0.6f, 0.1f, 0.45f),
                "unreachable" => new Color(0.45f, 0.45f, 0.45f, 0.45f),   // grey: never got there, so nothing to adjust
                _ => new Color(0.2f, 0.9f, 0.4f, 0.35f) };
```

- [ ] **Step 5: `docs/api-contract.md` 를 고친다**

`status` 값을 적어 둔 줄에 `unreachable` 을 더하고, 한 문장으로 구분을 적는다: `needs_adjust` 는 댔는데 삐뚤어진 것, `unreachable` 은 가지 못한 것.

- [ ] **Step 6: 테스트 통과 확인**

Run: `cd api && ./gradlew test` 그리고 EditMode 전체
Expected: 전부 PASS

- [ ] **Step 7: 커밋**

```bash
git add api/src/main/resources/db/migration/V2__unreachable.sql api/src/main/java/com/shiphdmap/api/slots/ api/src/test/java/com/shiphdmap/api/slots/ unity/Assets/ShipHdMap/Runtime/ unity/Assets/ShipHdMap/Tests/EditMode/ScenarioPlannerTests.cs docs/api-contract.md
git commit   # feat: give up on a slot the vehicle cannot reach and move to the next
```

---

### Task 7: 웹

상태를 보이고, 파라미터를 돌리고, 예측 격자를 밀어 넣고, 마커를 가린다.

**Files:**
- Create: `web/src/geo/belief.ts`, `web/src/geo/belief.test.ts`
- Modify: `web/src/api/types.ts`, `web/src/bridge/useShipUnity.ts`, `web/src/store/editor.ts`, `web/src/components/DrivePanel.tsx`, `web/src/components/LayerTree.tsx`

**Interfaces:**
- Consumes: Task 5 의 `onBelief`·`SetBeliefParams`, Task 4 의 `SetPrediction`, Task 3 의 `SetOccluded`, M5c 의 `api.coverage`
- Produces: `BeliefEvt`, `BeliefParamsIn` (types.ts)
- Produces: `beliefBadge(state): { label: string; color: string }`, `fmtRatioOf(actual, predicted): string` (belief.ts)
- Produces: store 의 `belief`, `beliefParams`, `occluded`, `setBelief`, `toggleOccluded`, `pushPrediction`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`web/src/geo/belief.test.ts`:

```ts
import { describe, expect, it } from "vitest";
import { beliefBadge, fmtRatioOf } from "./belief";

describe("beliefBadge", () => {
  it("다섯 상태가 서로 다른 라벨과 색을 갖는다", () => {
    const states = ["ok", "degraded", "lost", "backtracking", "stopped"] as const;
    const badges = states.map(beliefBadge);
    expect(new Set(badges.map((b) => b.label)).size).toBe(5);
    expect(new Set(badges.map((b) => b.color)).size).toBe(5);
    badges.forEach((b) => expect(b.color).toMatch(/^#/));
  });
  it("모르는 값은 정상으로 떨어지지 않는다", () => {
    expect(beliefBadge("wat").label).not.toBe(beliefBadge("ok").label);
  });
});

describe("fmtRatioOf", () => {
  it("예측 대비 배수를 낸다", () => {
    expect(fmtRatioOf(0.6, 0.3)).toBe("2.0×");
  });
  it("예측이 없으면 대시", () => {
    expect(fmtRatioOf(0.6, null)).toBe("—");
    expect(fmtRatioOf(null, 0.3)).toBe("—");
  });
  it("예측이 0 이면 나눗셈을 하지 않는다", () => {
    expect(fmtRatioOf(0.6, 0)).toBe("—");
  });
});
```

- [ ] **Step 2: 실패를 확인한다**

Run: `cd web && pnpm vitest run belief`
Expected: FAIL — `./belief` 모듈이 없다

- [ ] **Step 3: 순수 함수를 만든다**

`web/src/geo/belief.ts`:

```ts
/** Five states, five colours; an unknown value must look wrong rather than healthy. */
export function beliefBadge(state: string): { label: string; color: string } {
  switch (state) {
    case "ok": return { label: "정상", color: "#2e7d32" };
    case "degraded": return { label: "저하", color: "#f9a825" };
    case "lost": return { label: "상실", color: "#d32f2f" };
    case "backtracking": return { label: "역추적", color: "#6a1b9a" };
    case "stopped": return { label: "정지", color: "#424242" };
    default: return { label: "알 수 없음", color: "#8d6e63" };
  }
}

/** How much worse than the map promised. Dash when there is nothing to compare against. */
export function fmtRatioOf(actual: number | null | undefined, predicted: number | null | undefined): string {
  if (actual == null || predicted == null || predicted <= 0) return "—";
  return `${(actual / predicted).toFixed(1)}×`;
}
```

- [ ] **Step 4: 타입과 브리지를 더한다**

`web/src/api/types.ts` 끝에:

```ts
export type BeliefState = "ok" | "degraded" | "lost" | "backtracking" | "stopped";
export type BeliefEvt = {
  state: BeliefState; n_obs: number;
  sigma_xy?: number; sigma_psi?: number; predicted_sigma_xy?: number;
  lost_m: number; sigma_odo: number; trail_m: number;
};
export type BeliefParamsIn = { k: number; frames: number; drift_rate: number; budget_m: number; max_lost_m: number; trail_m: number };
```

`web/src/bridge/useShipUnity.ts` 에 `onLoc` 와 같은 모양으로 한 쌍을 더한다:

```ts
    const onBel = (json: string) => setBelief(JSON.parse(json));
    // ... addEventListener("onBelief", onBel);  /  removeEventListener("onBelief", onBel);
```

`BridgeName` 유니온에 `"SetBeliefParams" | "SetOccluded" | "SetPrediction"` 을 더한다.

- [ ] **Step 5: 스토어에 상태와 액션을 더한다**

`EditorState` 에:

```ts
  belief: BeliefEvt | null;
  beliefParams: BeliefParamsIn;
  occluded: string[];
  setBelief: (e: BeliefEvt | null) => void;
  toggleOccluded: (id: string) => void;
  setBeliefParams: (p: Partial<BeliefParamsIn>) => void;
```

구현부:

```ts
  belief: null, occluded: [],
  // must match BeliefParams in BeliefMonitor.cs -- the panel showing one number while Unity uses another is the
  // same trap the coverage sliders hit in M5c
  beliefParams: { k: 2.0, frames: 5, drift_rate: 0.05, budget_m: 1.0, max_lost_m: 5.0, trail_m: 20.0 },
  setBelief: (belief) => set({ belief }),
  setBeliefParams: (p) => set((s) => ({ beliefParams: { ...s.beliefParams, ...p } })),
  toggleOccluded: (id) => set((s) => ({ occluded: s.occluded.includes(id) ? s.occluded.filter((x) => x !== id) : [...s.occluded, id] })),
```

- [ ] **Step 6: `DrivePanel` 에 구역을 더한다**

`SLIDERS` 옆에 판정 슬라이더 표를 하나 더 둔다:

```tsx
const BELIEF_SLIDERS: { key: keyof BeliefParamsIn; label: string; min: number; max: number; step: number; digits: number }[] = [
  { key: "k", label: "허용 배수", min: 1, max: 6, step: 0.5, digits: 1 },
  { key: "frames", label: "연속 프레임", min: 1, max: 20, step: 1, digits: 0 },
  { key: "drift_rate", label: "오도메트리 drift (/m)", min: 0, max: 0.5, step: 0.05, digits: 2 },
  { key: "budget_m", label: "예산 σ (m)", min: 0.2, max: 3, step: 0.1, digits: 1 },
  { key: "max_lost_m", label: "최대 상실 (m)", min: 1, max: 20, step: 1, digits: 0 },
  { key: "trail_m", label: "자취 (m)", min: 5, max: 50, step: 5, digits: 0 },
];
```

`위치 추정` 절 아래에:

```tsx
      <h4 style={{ marginTop: 8 }}>믿음 상태</h4>
      {b && (
        <>
          <div className="row"><label>상태</label>
            <span style={{ color: beliefBadge(b.state).color, fontWeight: 700 }}>{beliefBadge(b.state).label}</span>
            <span>마커 {b.n_obs} 개</span></div>
          <div className="row"><label>σxy</label>
            <span>실측 {b.sigma_xy?.toFixed(2) ?? "—"} m · 예측 {b.predicted_sigma_xy?.toFixed(2) ?? "—"} m · {fmtRatioOf(b.sigma_xy, b.predicted_sigma_xy)}</span></div>
          <div className="row"><label>상실</label>
            <span>{b.lost_m.toFixed(1)} m · 오도 σ {b.sigma_odo.toFixed(2)} m · 자취 {b.trail_m.toFixed(1)} m</span></div>
        </>
      )}
      {BELIEF_SLIDERS.map((s) => (
        <div className="row" key={s.key}><label>{s.label}</label>
          <input type="range" min={s.min} max={s.max} step={s.step} value={bp[s.key]}
            onChange={(e) => setBeliefParams({ [s.key]: Number(e.target.value) })}
            onMouseUp={commitBelief} onKeyUp={commitBelief} onTouchEnd={commitBelief} />
          <span>{bp[s.key].toFixed(s.digits)}</span></div>
      ))}
```

`commitBelief = () => send("SetBeliefParams", bp)` 를 `commit` 옆에 둔다. `b`·`bp`·`setBeliefParams` 는 스토어에서 꺼낸다.

`start()` 가 시나리오를 띄우기 전에 예측을 밀어 넣는다 — **Unity 는 HTTP 를 하지 않는다**:

```tsx
  const start = async (mode: "load" | "unload") => {
    clearLog();
    const deck = pickDeck(decks, deckFilter)?.id;
    if (deck) {
      const cov = await api.coverage(datasetId, deck, { mode, grid_m: 1.0, omit: occluded });
      // trim to what the vehicle needs: 2880 cells of {x, y, s} instead of the full response
      send("SetPrediction", { grid_m: cov.grid_m, bbox: cov.bbox, cells: cov.cells.map((c) => ({ x: c.x, y: c.y, s: c.sigma_xy ?? null })) });
    }
    send("SetOccluded", { ids: occluded });
    send("SetBeliefParams", bp);
    send("SetTimeScale", { scale });
    send("StartScenario", { mode });
  };
```

`omit: occluded` 를 넘기는 것이 §5.2 의 기본 동작이다 — 가린 마커는 예측에서도 빠져서 예측과 실제가 함께 움직인다.

- [ ] **Step 7: `LayerTree` 에 가림 체크박스를 더한다**

LM 항목 행에 체크박스를 붙이고 `toggleOccluded(f.id)` 를 부른다. 가려진 마커는 라벨을 흐리게(예: `opacity: 0.45`) 그린다. M5c 의 `omit` 과 **같은 집합**을 쓴다.

- [ ] **Step 8: 테스트·빌드 확인**

Run: `cd web && pnpm vitest run && pnpm tsc --noEmit && pnpm build`
Expected: 전부 성공. Vitest 신규 5 개 포함

- [ ] **Step 9: 커밋**

```bash
git add web/src/
git commit   # feat: show the belief state and feed the vehicle its coverage prediction
```

---

### Task 8: 문서와 브라우저 검증

- [ ] **Step 1: 전체 테스트를 돌린다**

```bash
cd api && ./gradlew test
cd ../web && pnpm vitest run && pnpm tsc --noEmit
```
그리고 EditMode 전체. 새 테스트 수를 세어 둔다.

**주의:** 에이전트가 둘 이상 같은 워킹트리에서 gradle 을 동시에 돌리면 `build/test-results` 가 깨진다(`NoSuchFileException: in-progress-results-*.bin`). 그때는 `./gradlew --stop` 후 `rm -rf api/build/test-results api/build/tmp/test` 하고 단독으로 다시 돌린다.

- [ ] **Step 2: 띄워서 검증한다**

```bash
cd db && docker compose -f compose.yaml up -d
cd ../api && ./gradlew bootRun &      # :8081
cd ../web && pnpm dev                 # :5174 (5173 은 다른 컨테이너가 쓴다)
```

**먼저 구획을 재생성한다** — 데모 DB 에 M5b 이전 생성분이 남아 있으면 숫자가 안 맞는다:

```bash
curl -s -X POST http://localhost:8081/api/datasets/roro-demo-01/decks/D3/slots/generate \
  -H 'Content-Type: application/json' -d '{}' | head -c 120   # count 76 이어야 한다
```

브라우저에서 순서대로 확인한다 (스펙 §6.5):

1. 차로 주행 중 상태가 `정상`, 실측 σxy 가 예측 σxy 의 2 배 안
2. 시나리오를 처음부터 돌리면 **첫 구획**이 바깥 열이라 곧 `저하` → `상실` 로 바뀐다
3. 예산을 다 쓰면 `역추적` 으로 바뀌고 차가 **뒤로 움직인다**
4. 차로로 돌아오면 `정상` 으로 복귀한다
5. 2 회차 실패 후 그 구획이 회색이 되고 다음 구획으로 넘어간다
6. 마커 하나를 `LayerTree` 에서 가리면 히트맵과 주행 상태가 **함께** 나빠진다
7. `허용 배수` 를 1.2 로 내리면 차로에서도 `저하` 가 뜬다
8. `오도메트리 drift` 를 0.5 로 올리면 거리 한계가 아니라 σ 한계가 먼저 걸린다 (`상실` 거리가 5 m 보다 훨씬 짧다)

- [ ] **Step 3: 기본 스펙의 구현 순서 표에 M5d 행을 더한다**

`docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md` §13 표의 M5c 행 아래:

```markdown
| M5d 측위 상실·회복 | Localizer 가 달성 σ 를 내고, 커버리지 예측과 대조해 저하·상실을 판정하고, 자취를 되짚어 회복하고, 실패한 구획을 `unreachable` 로 포기한다 (`2026-09-17-m5d-localization-loss-design.md`) | 바깥 열 구획을 목표로 하면 예측한 지점에서 상실 → 역추적 → 회복 또는 스킵이 일어난다 |
```

- [ ] **Step 4: README 에 한 절을 더한다**

`### 랜드마크 커버리지` 절 아래에 `### 측위 상실과 회복` 을 더한다. 내용: 판정 기준이 주차 허용오차가 아니라 **지도가 약속한 값**이라는 것, 회복이 역추적 하나라는 것, `unreachable` 과 `needs_adjust` 의 차이, 그리고 실측된 도달 불가 수.

- [ ] **Step 5: `## 상태` 절에 M5d 줄을 더한다**

M5c 줄 아래에 테스트 수와 함께. 그리고 `- 다음: M6 문서·시연` 은 그대로 둔다.

- [ ] **Step 6: 커밋**

```bash
git add README.md docs/superpowers/specs/2026-09-15-ship-hdmap-demo-design.md
git commit   # docs: record M5d in the README and the milestone table
```

---

## Self-Review

**스펙 커버리지**

| 스펙 절 | 담당 Task |
| --- | --- |
| §3.1 달성한 σ, `CoverageAnalyzer` 와 같은 수학 | 1 (`Information`/`Invert3`/`Sigma`, `SigmaMatchesTheCoverageAnalyzerReference`) |
| §3.2 예측 격자, 최근접 조회, in_scope 밖도 사용 | 4 (`PredictionGrid`), 7 Step 6 (웹이 받아 밀어 넣음) |
| §3.3 주차 허용오차를 안 쓰는 이유 | 설계 근거로만 존재 — 구현 없음이 옳다 |
| §3.4 판정 (관측 0 / 예측 대비 배수 / 연속 N) | 2 (`Step`), 5 (배선) |
| §3.5 오도메트리 예산 (거리·σ 두 한계) | 2 (`LostM`, `SigmaOdo`), 테스트 두 체제 모두 |
| §3.6 역추적, 자취, 회복 거리 | 2 (`_trail`, `BacktrackTargetS`), 5 (`Rewind`) |
| §3.7 재시도 1 회 → `unreachable` | 6 |
| §4 브리지 메시지 3 + 1 | 3 (`SetOccluded`), 4 (`SetPrediction`), 5 (`SetBeliefParams`, `onBelief`) |
| §5.1 DrivePanel 구역 | 7 Step 6 |
| §5.2 가림 체크박스, `omit` 공유 | 3, 7 Step 6·7 |
| §6.1 단위 9 항목 | 1 (6), 2 (14), 4 (3) |
| §6.2 회귀 (바깥 열에서 잃는다 / 차로에서 안 잃는다) | 5 (`BeliefRunTests`) |
| §6.3 API | 6 |
| §6.4 웹 Vitest | 7 |
| §6.5 브라우저 8 항목 | 8 |

**스펙에서 고친 것 (Task 안에 단계로 들어 있다)**

1. §2 의 "순수 정적 클래스" → 상태 기계는 인스턴스 상태를 갖는다. `UnityEngine` 비의존이라는 성질만 맞다 (Task 2 Step 5)
2. §3.6 의 "자세를 링버퍼에" → **호길이 `s`**. `VehicleController` 가 이미 `s` 로 매개화돼 있고 `Rewind(s)` 를 갖고 있어, 자취에 좌표를 담을 이유가 없다 (Task 2 Step 5)

**타입 일관성** — `LocalizerResult.sigmaXy`(Task 1)를 Task 5 가 `_lastRes.sigmaXy` 로 읽고, `BeliefMonitor.Step(double?, double?, double, double)`(Task 2)을 Task 5 가 `Belief.Step(sigma, predicted, Vehicle.s, moved)` 로 부르며, `PredictionGrid.SigmaAt(x, y)`(Task 4)를 Task 5 가 `Prediction?.SigmaAt(...)` 로 부른다. 웹의 `BeliefEvt` 필드명이 C# `BeliefEvt` 와 같다. `BeliefParams`(C#)와 `beliefParams`(스토어)의 기본값이 같은 표에서 나왔다.

**의도한 축소**

- §6.1-9 의 "가려진 마커는 감지되지 않는다" 는 Unity `GameObject` 를 만들어야 해서 `LocalizerSigmaTests` 에 넣었다(순수 테스트 파일이 아니다). `BeliefMonitorTests` 는 순수로 남긴다
- Task 6 의 API 테스트는 `SlotController` 에 상태 갱신 경로가 없으면 `SlotRepo` 단위 테스트로 대체한다 — 경로를 새로 만들지는 않는다. 상태는 Unity 가 `onSlotFilled` 로 알리고 웹이 기존 경로로 저장한다
