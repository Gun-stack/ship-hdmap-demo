#!/usr/bin/env bash
# M2 smoke: PostGIS (compose, :5433) -> API (bootRun, :8081) -> seed fixture -> CRUD -> vehicle-map ETag -> pose/ramp -> export.geojson
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
API="http://localhost:${API_PORT:-8081}/api"
DS="roro-demo-01"
export JAVA_HOME="${JAVA_HOME:-/opt/homebrew/opt/openjdk}"

docker compose -f "$ROOT/db/compose.yaml" up -d --wait
( cd "$ROOT/api" && ./gradlew bootRun --console=plain > "$ROOT/api/build/bootrun.log" 2>&1 ) &
BOOT_PID=$!
trap 'kill $BOOT_PID 2>/dev/null || true; pkill -f "com.shiphdmap.api.ApiApplication" 2>/dev/null || true' EXIT
for i in $(seq 1 90); do curl -sf "$API/datasets/nope" -o /dev/null -w '' && break || true; grep -q "Started ApiApplication" "$ROOT/api/build/bootrun.log" 2>/dev/null && break; sleep 2; done

code() { curl -s -o "$2" -w '%{http_code}' "${@:3}"; }
expect() { local want=$1 got=$2 what=$3; [ "$got" = "$want" ] && echo "ok   $what ($got)" || { echo "FAIL $what: got $got want $want"; cat "$4" 2>/dev/null; exit 1; }; }

mkdir -p "$ROOT/api/build/smoke"; S="$ROOT/api/build/smoke"
c=$(code x "$S/ds.json" -X POST "$API/datasets" -H 'content-type: application/json' -d "{\"id\":\"$DS\",\"name\":\"RORO demo\",\"ap_lat\":12.3456,\"ap_lon\":45.6789,\"heading_deg\":87.5}")
[ "$c" = "201" ] || [ "$c" = "409" ] || { echo "FAIL create dataset ($c)"; cat "$S/ds.json"; exit 1; }; echo "ok   create dataset ($c)"
c=$(code x "$S/seed.json" -X POST "$API/datasets/$DS/seed" -H 'content-type: application/json' --data-binary @"$ROOT/docs/fixtures/vehicle-map.sample.json"); expect 200 "$c" "seed fixture" "$S/seed.json"
c=$(code x "$S/lm.json" -X POST "$API/datasets/$DS/features" -H 'content-type: application/json' -d '{"deck_id":"D3","layer":"LM","kind":"apriltag","geometry":{"type":"Point","coordinates":[84.0,-6.2,11.8]},"props":{"family":"apriltag-36h11","code":7,"normal":[0,1,0],"size_m":0.3,"mounted_on":"C-PILLAR-D3-007"}}'); expect 201 "$c" "create landmark" "$S/lm.json"
ETAG=$(curl -s -D - -o "$S/map.json" "$API/datasets/$DS/vehicle-map" | awk 'tolower($1)=="etag:"{print $2}' | tr -d '\r')
[ -n "$ETAG" ] && echo "ok   vehicle-map etag $ETAG" || { echo "FAIL no etag"; exit 1; }
c=$(code x /dev/null "$API/datasets/$DS/vehicle-map" -H "If-None-Match: $ETAG"); expect 304 "$c" "vehicle-map not modified"
c=$(code x "$S/pose.json" -X PUT "$API/datasets/$DS/pose" -H 'content-type: application/json' -d '{"draft_fwd_m":8.1,"draft_aft_m":8.6,"heel_deg":-0.4,"heading_deg":87.5,"tide_m":0.5,"quay_z_m":3.5,"ap_lat":12.3456,"ap_lon":45.6789,"measured_at":"2026-09-15T09:30:00Z"}'); expect 200 "$c" "put pose" "$S/pose.json"
c=$(code x "$S/ramp.json" "$API/datasets/$DS/ramps/RAMP-STERN"); expect 200 "$c" "ramp state" "$S/ramp.json"; grep -o '"state":"[a-z]*"' "$S/ramp.json"
c=$(code x "$ROOT/api/build/export.geojson" "$API/datasets/$DS/export.geojson"); expect 200 "$c" "export.geojson"
echo "landmarks in map: $(grep -o '"family"' "$S/map.json" | wc -l | tr -d ' ')  geojson bytes: $(wc -c < "$ROOT/api/build/export.geojson" | tr -d ' ')"
echo "SMOKE OK — open api/build/export.geojson in QGIS (docs/qgis-check.md)"
