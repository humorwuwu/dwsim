#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
project="${1:-$root/tests/openplc/h2sim-three-loop}"
project="$(cd "$project" && pwd)"

editor_version="4.3.5"
appimage_sha256="ad27500153b8145091c20ef1649c37d598be8d536dadc397082f839c46f11370"
runtime_image="ghcr.io/autonomy-logic/openplc-runtime@sha256:378deec33b371eb09f8344e714d88b48efc7387f631f793371ba9f6a6e381c9f"
container_name="h2sim-openplc-runtime"
work_dir="${RUNNER_TEMP:-/tmp}/h2sim-openplc-runtime"
report_dir="$root/tests/reports/closed-loop"
report="$report_dir/v5.1.2-4-three-loop.csv"
mkdir -p "$work_dir" "$report_dir"

appimage="${OPENPLC_EDITOR_APPIMAGE:-$work_dir/OpenPLC.Editor-${editor_version}.AppImage}"
if [[ ! -f "$appimage" ]]; then
  curl -fL --retry 3 \
    "https://github.com/Autonomy-Logic/openplc-editor/releases/download/v${editor_version}/OpenPLC.Editor-${editor_version}.AppImage" \
    -o "$appimage"
fi
actual_sha256="$(sha256sum "$appimage" | awk '{print $1}')"
if [[ "$actual_sha256" != "$appimage_sha256" ]]; then
  printf 'OpenPLC Editor checksum mismatch: expected %s, got %s\n' \
    "$appimage_sha256" "$actual_sha256" >&2
  exit 1
fi
chmod +x "$appimage"

python3 -m venv "$work_dir/bench-venv"
"$work_dir/bench-venv/bin/pip" install --quiet 'pymodbus==3.8.6'

runtime_data="$work_dir/runtime-data"
mkdir -p "$runtime_data"
chmod 0777 "$runtime_data"
docker rm -f "$container_name" >/dev/null 2>&1 || true
docker run --detach \
  --name "$container_name" \
  --network host \
  --cap-add SYS_NICE \
  --cap-add SYS_RESOURCE \
  --volume "$runtime_data:/var/run/runtime" \
  "$runtime_image" >/dev/null

cleanup() {
  if [[ -n "${plant_pid:-}" ]]; then
    kill "$plant_pid" >/dev/null 2>&1 || true
    wait "$plant_pid" >/dev/null 2>&1 || true
  fi
  docker rm -f "$container_name" >/dev/null 2>&1 || true
}
trap cleanup EXIT

runtime_ready=0
for _ in $(seq 1 90); do
  if curl -skf https://127.0.0.1:8443/api/version >/dev/null; then
    runtime_ready=1
    break
  fi
  sleep 1
done
if [[ "$runtime_ready" -ne 1 ]]; then
  docker logs "$container_name" >&2 || true
  printf 'OpenPLC Runtime did not expose its API within 90 seconds\n' >&2
  exit 1
fi

plant_log="$work_dir/plant.log"
"$work_dir/bench-venv/bin/python" "$root/tests/openplc/three_loop_bench.py" \
  --report "$report" >"$plant_log" 2>&1 &
plant_pid=$!

upload_stdout="$work_dir/upload-stdout.txt"
upload_result="$work_dir/upload-result.json"
upload_log="$work_dir/upload.log"
env -u ELECTRON_RUN_AS_NODE "$appimage" \
  --appimage-extract-and-run \
  --no-sandbox \
  --cli \
  --user-data "$work_dir/user-data" \
  upload "$project" \
  --target 'OpenPLC Runtime v4' \
  --host 127.0.0.1 \
  --credentials 'admin:h2sim-ci' \
  --create-user \
  --yes \
  --json >"$upload_stdout" 2>"$upload_log"
awk 'found || /^{/ { found = 1; print }' "$upload_stdout" >"$upload_result"
if ! jq -e '.ok == true and .target == "OpenPLC Runtime v4" and .uploaded == true' "$upload_result" >/dev/null; then
  cat "$upload_stdout"
  cat "$upload_log" >&2
  exit 1
fi

token="$(curl -skf -X POST https://127.0.0.1:8443/api/login \
  -H 'Content-Type: application/json' \
  -d '{"username":"admin","password":"h2sim-ci"}' | jq -er .access_token)"
curl -skf https://127.0.0.1:8443/api/start-plc \
  -H "Authorization: Bearer $token" >"$work_dir/start-plc.json"

set +e
wait "$plant_pid"
plant_status=$?
plant_pid=""
set -e

if [[ "$plant_status" -ne 0 ]]; then
  cat "$plant_log" >&2
  docker logs "$container_name" >&2 || true
  exit "$plant_status"
fi

cat "$plant_log"
test -s "$report"
printf 'PASS: V-5.1.2/V-5.1.3/V-5.1.4 three-loop OpenPLC Runtime bench\n'
