#!/usr/bin/env bash
# M3 dev: PostGIS (compose :5433) -> API (bootRun :8081) -> seed if empty -> Vite dev server (:5173, /api proxied)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="http://localhost:${API_PORT:-8081}/api"; DS="roro-demo-01"
export JAVA_HOME="${JAVA_HOME:-/opt/homebrew/opt/openjdk}"
mkdir -p "$ROOT/api/build"
docker compose -f "$ROOT/db/compose.yaml" up -d --wait
( cd "$ROOT/api" && ./gradlew bootRun --console=plain > "$ROOT/api/build/bootrun.log" 2>&1 ) &
BOOT_PID=$!
trap 'kill $BOOT_PID 2>/dev/null || true; pkill -f "com.shiphdmap.api.ApiApplication" 2>/dev/null || true' EXIT
for i in $(seq 1 90); do grep -q "Started ApiApplication" "$ROOT/api/build/bootrun.log" 2>/dev/null && break; sleep 2; done
if [ "$(curl -s -o /dev/null -w '%{http_code}' "$API/datasets/$DS")" != "200" ]; then
  if ! curl -fsS -o /dev/null -X POST "$API/datasets" -H 'content-type: application/json' -d "{\"id\":\"$DS\",\"name\":\"RORO demo\",\"ap_lat\":12.3456,\"ap_lon\":45.6789,\"heading_deg\":87.5}"; then
    echo "seeding $DS failed (is the API up? see api/build/bootrun.log)" >&2; exit 1
  fi
  if ! curl -fsS -o /dev/null -X POST "$API/datasets/$DS/seed" -H 'content-type: application/json' --data-binary @"$ROOT/docs/fixtures/vehicle-map.sample.json"; then
    echo "seeding $DS failed (is the API up? see api/build/bootrun.log)" >&2; exit 1
  fi
  echo "seeded $DS"
fi
[ -f "$ROOT/web/public/unity/Build/unity.loader.js" ] || echo "WARN: no WebGL build at web/public/unity — run the Unity menu ShipHdMap/Build WebGL first"
cd "$ROOT/web" && pnpm install --frozen-lockfile >/dev/null
pnpm dev & VITE_PID=$!
trap 'kill $VITE_PID $BOOT_PID 2>/dev/null || true; pkill -f "com.shiphdmap.api.ApiApplication" 2>/dev/null || true' EXIT
wait $VITE_PID
