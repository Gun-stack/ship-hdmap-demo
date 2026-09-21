// Drives one run of the demo in Chrome and writes docs/img/*.png plus a summary table.
//
// Determinism, four rules (M6 spec §6.3):
//  1. Shoot on EVENTS, never on a wall clock. The sim steps by Time.deltaTime, so "12 s after start" is a
//     different place on every machine. Two kinds of event are used here, and no third: a bridge event
//     (the scenario log lines, the slot verdict) and a PLACE -- the first localization emit at which the
//     vehicle's true x has passed a chosen station along the ship. A place is the stronger of the two for
//     a figure: the same metre of deck is photographed on every machine and at every frame rate.
//  2. x1 only. M5a was bitten by time-scale overshoot; a run captured at x20 is not the run being demoed.
//     The persisted UI blob is cleared before the page loads (it carries timeScale), and the select is
//     then checked, because a x20 blob would silently survive a reload.
//  3. Reseed. M5b: without it the frame switch happens somewhere else. capture.sh drops and re-seeds the
//     dataset every run -- it has to, because parking writes slot statuses back and the next run would
//     then aim at a different slot.
//  4. Stamp unity.wasm's mtime on everything. Without it a browser report is rumour, not evidence (M5e).
//
// The dataset is chosen with ?ds=, which store/editor.ts reads for exactly this reason, so the top bar in
// every figure names the dataset the figure is actually of. The version guard below re-checks that against
// what the API just returned -- a figure whose label and content disagree is the defect this milestone is
// about, and a person can open the same URL and see the same screen.
//
// The HUD is WebGL canvas pixels; the DOM cannot read it. A clipped, scaled capture is the only way in.
import fs from "node:fs";
import path from "node:path";

const PORT = process.env.WEB_PORT ?? "5399";
const DS = process.env.CAPTURE_DS ?? "roro-demo-cap";
const BUILD = process.env.BUILD_MTIME ?? "unknown";
const SEED = JSON.parse(process.env.SEED_JSON ?? "{}");
const SLOTS = JSON.parse(process.env.SLOTS_JSON ?? "{}");
const OUT = path.resolve(import.meta.dirname, "..", "docs", "img");

const vol = [];                                      // stderr: what this one run happened to measure
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));
const note = (k, v) => console.log(`  ${k.padEnd(24)}${v}`);   // stdout: what a document may cite

async function connect() {
  const list = await (await fetch("http://127.0.0.1:9333/json")).json();
  const t = list.find((x) => x.type === "page" && x.url.includes(`localhost:${PORT}`));
  if (!t) throw new Error(`no page on localhost:${PORT} -- open it in the debugging Chrome first`);
  const ws = new WebSocket(t.webSocketDebuggerUrl);
  let id = 0; const pend = new Map();
  await new Promise((r) => ws.addEventListener("open", r));
  ws.addEventListener("message", (m) => {
    const d = JSON.parse(m.data);
    if (d.id && pend.has(d.id)) { pend.get(d.id)(d); pend.delete(d.id); }
  });
  const send = (method, params = {}) => new Promise((res, rej) => {
    const i = ++id; pend.set(i, (d) => (d.error ? rej(new Error(method + ": " + JSON.stringify(d.error))) : res(d.result)));
    ws.send(JSON.stringify({ id: i, method, params }));
  });
  const ev = async (e) => {
    const r = await send("Runtime.evaluate", { expression: e, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error(JSON.stringify(r.exceptionDetails).slice(0, 400));
    return r.result.value;
  };
  await send("Page.enable"); await send("Runtime.enable");
  return { send, ev };
}

export async function main() {
  const d = await connect();

  // Rule 2: the persisted blob carries timeScale, the coverage sliders and the occlusion set -- every input
  // to the numbers below. A capture that inherits yesterday's sliders is not reproducible. The two keys are
  // fixed strings in the app (EDITOR_KEY, UI_KEY); they do not follow ?ds=.
  await d.ev(`localStorage.removeItem("shiphdmap.editor.roro-demo-01");localStorage.removeItem("shiphdmap.ui.roro-demo-01");window.__reloading=1`);
  await d.send("Page.navigate", { url: `http://localhost:${PORT}/?ds=${DS}` });
  for (let i = 0; ; i++) {
    await sleep(1000);
    if (i > 120) throw new Error("the app never finished loading");
    if (await d.ev(`window.__reloading===undefined && !document.querySelector(".loading") && document.querySelectorAll("aside.left button").length>3 && !!document.querySelector("canvas")`)) break;
  }
  await sleep(4000);                                 // Unity's Load/SetDeck/SetPose round trip

  const shown = await d.ev(`document.querySelector("header.topbar")?.textContent ?? ""`);
  if (!shown.includes(DS) || !shown.includes(`v${SLOTS.version ?? SEED.version}`))
    throw new Error(`the page is not on ${DS} at v${SLOTS.version ?? SEED.version}: top bar reads "${shown}"`);

  // --- watch the bridge instead of the log panel ---
  // The DOM log stamps every line with the wall clock, and it renders scenarioLine()'s prose rather than the
  // payload. Wrapping react-unity-webgl's dispatcher gives the events themselves, in order, with no clock in
  // them. Read-only: the original is always called.
  await d.ev(`(()=>{window.__cap=[];const o=window.dispatchReactUnityEvent;window.dispatchReactUnityEvent=function(n,...a){try{if(n!=="onBelief")window.__cap.push({n,p:a[0]});}catch(e){}return o.apply(this,arguments);};})()`);
  let cursor = 0;                                    // how much of the page's buffer has been pulled over
  const evts = [];
  let ptr = 0;                                       // how much of it has been WAITED past
  const pump = async () => {
    const got = JSON.parse(await d.ev(`JSON.stringify(window.__cap.slice(${cursor}))`));
    cursor += got.length;
    for (const e of got) evts.push({ n: e.n, v: JSON.parse(e.p) });
  };
  /**
   * Waits for the next event the predicate accepts, and consumes everything it walked past.
   * The pointer is shared and monotonic on purpose: a poll returns a BATCH, and a waitFor that dropped the
   * rest of its batch would silently skip the very event the next waitFor is about to ask for. That is not
   * hypothetical -- start and target land in the same 150 ms poll, and the first version of this file then
   * waited for the SECOND target, i.e. photographed the second car's quay run a minute and a half later.
   */
  const waitFor = async (pred, what, timeoutMs = 300000) => {
    const started = Date.now();
    for (;;) {
      while (ptr < evts.length) { const e = evts[ptr++]; if (pred(e)) return e; }
      if (Date.now() - started > timeoutMs) throw new Error(`timed out waiting for ${what}`);
      await sleep(150);
      await pump();
    }
  };
  const scen = (event) => waitFor((e) => e.n === "onScenario" && e.v.event === event, `scenario ${event}`);
  /** The first estimate emitted at or past a station along the ship: a place, not a moment (rule 1). */
  const station = (x) => waitFor((e) => e.n === "onLocalization" && e.v.true_x >= x, `true x >= ${x}`);

  // --- the screen ---
  const rect = (sel) => d.ev(`(()=>{const e=document.querySelector(${JSON.stringify(sel)});if(!e)return null;const r=e.getBoundingClientRect();return{x:r.x,y:r.y,w:r.width,h:r.height};})()`);
  const btn = (label) => d.ev(`(()=>{const b=[...document.querySelectorAll("button")].find(x=>x.textContent.trim()===${JSON.stringify(label)});if(!b)return null;const r=b.getBoundingClientRect();return{cx:r.x+r.width/2,cy:r.y+r.height/2};})()`);
  const click = async (x, y) => {
    await d.send("Input.dispatchMouseEvent", { type: "mouseMoved", x, y, button: "none", buttons: 0 }); await sleep(60);
    await d.send("Input.dispatchMouseEvent", { type: "mousePressed", x, y, button: "left", buttons: 1, clickCount: 1 }); await sleep(60);
    await d.send("Input.dispatchMouseEvent", { type: "mouseReleased", x, y, button: "left", buttons: 0, clickCount: 1 });
  };
  // Buttons are found by their label and re-measured on every press: they shift with the tool hint's text,
  // and a click that lands on a label instead of a button fails in silence.
  const press = async (label) => { const p = await btn(label); if (!p) throw new Error(`no button "${label}"`); await click(p.cx, p.cy); await sleep(900); };
  const shot = async (file, clip, scale = 1) => {
    const r = await d.send("Page.captureScreenshot", clip ? { format: "png", clip: { ...clip, scale } } : { format: "png" });
    fs.writeFileSync(path.join(OUT, file), Buffer.from(r.data, "base64"));
  };
  const canvas = await rect("canvas");
  if (!canvas) throw new Error("no canvas");
  // The HUD box is anchored 10 px from the canvas's bottom-left and grows upward; during a drive it runs to
  // ten lines. Derived from the live canvas rect, not hardcoded: the window is not the same size everywhere.
  const HUD = { x: canvas.x, y: canvas.y + canvas.h - 210, width: Math.min(560, canvas.w), height: 205 };
  const chapter = async (n, name, text) => {
    await shot(`${n}-${name}.png`);
    await shot(`${n}-${name}-hud.png`, HUD, 3);
    note(`${n}-${name}`, text);
  };
  const coverageText = () => d.ev(`[...document.querySelectorAll("aside.right .row")]
    .filter(r=>/사각지대|허용오차 미달|대상/.test(r.querySelector("label")?.textContent??""))
    .map(r=>[...r.children].map(e=>e.textContent.replace(/\\s+/g," ").trim()).join(" ")).join(" · ")`);

  note("build", `unity.wasm ${BUILD}`);
  note("dataset", `${DS} · v${SLOTS.version ?? SEED.version} · ${SEED.decks} decks · ${SEED.features} features`);
  note("slots", `D3 ${SLOTS.count} 구획 · 면적 활용률 ${(SLOTS.utilization * 100).toFixed(1)} % · 래싱 ${(SLOTS.lashing_coverage * 100).toFixed(0)} %`);

  await press("편집"); await press("궤도"); await press("전체");
  await chapter("01", "frames", `갑판 전체 · 궤도 카메라 · ${SEED.decks} 갑판`);

  await press("D3");
  // The per-layer counts of what is actually on the deck being photographed -- the one number the stale
  // docs/qgis-check.md got wrong in both directions, and the app's own count of it.
  note("D3 레이어", await d.ev(`[...document.querySelectorAll("aside.left .tree > ul > li")].map(e=>e.textContent.replace(/^[\u25be\u25b8]\\s*/,"").trim()).join(" · ")`));
  await press("커버리지");
  for (let i = 0; i < 60 && !(await coverageText()).includes("사각지대"); i++) await sleep(500);
  await chapter("02", "coverage", await coverageText());

  await press("주행");
  // Only now: the time-scale select lives in the drive panel, and edit mode's right pane has a deck select
  // in the same place that would answer this question with "D3".
  const scale = await d.ev(`[...document.querySelectorAll("aside.right select")].find(s=>[...s.options].some(o=>o.textContent==="\u00d71"))?.value ?? "?"`);
  note("time scale", `x${scale}`);
  if (scale !== "1") throw new Error("rule 2: the run must be at x1");
  await press("▶ 선적");
  vol.push(["start", JSON.stringify((await scen("start")).v)]);
  const target = await scen("target");
  // The driver's eye, for the whole drive. The orbit camera does follow the vehicle (StartScenario sets
  // follow at 25 m) but frames it against the hull, and the quay leg then photographs as a grey wall; the
  // driver's eye is also the only view that draws the sensor cone and the markers it is scoring.
  await press("차량 시선");
  await chapter("03", "quay", `안벽 · GPS 만 · 목표 ${target.v.slot_id}`);

  // Observed order, confirmed against a live run and against MapRuntime.StepScenario: the frame switch comes
  // BEFORE the ramp and long before 차로 이탈. The ramp phase has no log line of its own -- it is the stretch
  // between the frame_switch line and the leave_lane line -- so the ramp chapter is bracketed by those two
  // and pinned to a place inside the bracket. The ramp runs from the quay foot (x about -24) to the lane's
  // first point (x = 2), so x = -12 is mid-climb.
  const sw = await scen("frame_switch");
  vol.push(["frame_switch", sw.v.detail]);
  await chapter("04", "frame-switch", "안벽 프레임 → 선박 프레임 · 램프 발치");
  await station(-12);
  await chapter("05", "ramp", "램프 주행 · 경사 중간 (true x ≥ -12 m)");

  await station(50);
  await chapter("06", "lane", "차로 주행 · 차로 중간 (true x ≥ 50 m)");

  const filled = await waitFor((e) => e.n === "onSlotFilled", "the first slot verdict");
  vol.push(["parked", JSON.stringify(filled.v)]);
  await chapter("07", "parked", `첫 구획 판정 · ${filled.v.slot_id} · 지도의 약속과 대조`);
  // The verdict is DOM, not canvas -- and it is not in the HUD either, because the sim spawns the next car
  // in the SAME frame as the verdict, so by the time anything can be photographed the HUD already describes
  // that next car on the quay. This third clip, the scenario log, is the only place the parking error of the
  // car that just parked is legible. It is taken before 정지 on purpose: edit mode has no drive panel.
  const log = await d.ev(`(()=>{const h=[...document.querySelectorAll("aside.right h4")].find(x=>x.textContent.includes("시나리오 로그"));if(!h)return null;const a=h.getBoundingClientRect(),b=h.nextElementSibling.getBoundingClientRect();return{x:Math.min(a.x,b.x),y:a.y,width:Math.max(a.width,b.width),height:b.bottom-a.top};})()`);
  if (log) await shot("07-parked-log.png", log, 3);

  // 정지 first: in drive mode the right pane is the drive panel and has no coverage tab at all. It also stops
  // the scenario, which otherwise drives on to the next slot for as long as the page is open. The camera
  // needs no press back to 궤도: the toolbar's own edit-mode correction does that (Toolbar.tsx:30).
  await press("정지"); await press("커버리지");
  for (let i = 0; i < 60 && !(await coverageText()).includes("사각지대"); i++) await sleep(500);
  await chapter("08", "promise-vs-measured", await coverageText());

  console.error("\n  this run only, and NOT reproducible: the sensor's noise is drawn once per RENDERED frame, so how");
  console.error("  many draws separate two events depends on the frame rate. The map, the coverage numbers and");
  console.error("  the chapter boundaries above do not move; these estimates do, and so does the verdict they");
  console.error("  produce -- the same slot comes out filled on one run and needs_adjust on the next.");
  for (const [k, v] of vol) console.error(`  ${k.padEnd(24)}${v}`);
}

await main();
process.exit(0);
