#!/usr/bin/env bash
# Reproduces every figure and every number in the architecture walkthrough.
#
# It seeds its OWN dataset. The working dataset (roro-demo-01) has hand edits in it -- 99 markers at the time
# of writing against the fixture's 23 -- so a figure shot there shows a ship nobody else can get back.
# docs/qgis-check.md is the evidence: its counts went stale silently because nothing could re-run it.
#
# Prerequisites, all already running (this script starts nothing):
#   - the API on 8081, the postgis container from db/compose.yaml on 5433
#   - vite on 5399 (--strictPort). NOT 5173: an unrelated container answers 200 there on this machine,
#     which makes every "is the dev server up" check lie.
#   - Chrome with --remote-debugging-port=9333 and a tab open on http://localhost:5399/
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="http://localhost:${API_PORT:-8081}/api"; DS="${CAPTURE_DS:-roro-demo-cap}"; PORT="${WEB_PORT:-5399}"
WASM="$ROOT/web/public/unity/Build/unity.wasm"
[ -f "$WASM" ] || { echo "no WebGL build at web/public/unity -- build it first" >&2; exit 1; }
BUILD_MTIME="$(date -r "$WASM" '+%Y-%m-%d %H:%M:%S')"

# The capture dataset is disposable by design: drop and re-seed every run so the numbers cannot drift.
# Dropped with SQL, not with the API: there is no DELETE /datasets/{id}. Everything else hangs off the
# dataset row with ON DELETE CASCADE (V1__init.sql), so one row is the whole drop. POST /seed alone would
# not do -- it upserts, so anything a previous run's hand poke added would survive into the figures, and
# the drive itself writes slot statuses back (PUT /slots/{id}/status) as it parks.
command -v psql >/dev/null || { echo "psql not found -- needed to drop the capture dataset" >&2; exit 1; }
PGPASSWORD="${DB_PASSWORD:-shiphdmap}" psql -h "${DB_HOST:-localhost}" -p "${DB_PORT:-5433}" -U shiphdmap -d shiphdmap \
  -v ON_ERROR_STOP=1 -Atc "DELETE FROM dataset WHERE id = '$DS'" >/dev/null

curl -fsS -o /dev/null -X POST "$API/datasets" -H 'content-type: application/json' \
  -d "{\"id\":\"$DS\",\"name\":\"capture\",\"ap_lat\":12.3456,\"ap_lon\":45.6789,\"heading_deg\":87.5}"
SEED_JSON="$(curl -fsS -X POST "$API/datasets/$DS/seed" -H 'content-type: application/json' \
  --data-binary @"$ROOT/docs/fixtures/vehicle-map.sample.json")"
# Slots are generated, not seeded: the fixture carries two by hand and the drive needs a full deck to aim at.
SLOTS_JSON="$(curl -fsS -X POST "$API/datasets/$DS/decks/D3/slots/generate" -H 'content-type: application/json' -d '{}')"

mkdir -p "$ROOT/docs/img"
BUILD_MTIME="$BUILD_MTIME" CAPTURE_DS="$DS" WEB_PORT="$PORT" SEED_JSON="$SEED_JSON" SLOTS_JSON="$SLOTS_JSON" \
  node "$ROOT/scripts/capture.mjs"
