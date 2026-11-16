#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "$0")/../.." && pwd)"
project="${1:-$root/tests/openplc/h2sim-three-loop}"
project="$(cd "$project" && pwd)"

editor_version="4.3.5"
appimage_sha256="ad27500153b8145091c20ef1649c37d598be8d536dadc397082f839c46f11370"
work_dir="${RUNNER_TEMP:-/tmp}/h2sim-openplc-editor"
mkdir -p "$work_dir"

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

result="$work_dir/compile-result.json"
log="$work_dir/compile.log"
env -u ELECTRON_RUN_AS_NODE "$appimage" \
  --appimage-extract-and-run \
  --no-sandbox \
  --cli \
  --user-data "$work_dir/user-data" \
  compile "$project" \
  --target 'OpenPLC Runtime v4' \
  --json >"$result" 2>"$log"

jq -e '.ok == true and .target == "OpenPLC Runtime v4" and .uploaded == false' "$result" >/dev/null

build_dir="$project/build/OpenPLC Runtime v4/src"
test -f "$build_dir/program.st"
test -f "$build_dir/pou_MAIN.cpp"
test -f "$build_dir/image.conf"
test -f "$build_dir/conf/modbus_master.json"

jq -e '
  length == 1 and
  .[0].name == "H2sim" and
  .[0].config.type == "SLAVE" and
  .[0].config.io_points[0].fc == 4 and
  .[0].config.io_points[0].iec_location == "%IW0" and
  .[0].config.io_points[0].len == 3 and
  .[0].config.io_points[1].fc == 16 and
  .[0].config.io_points[1].iec_location == "%QW0" and
  .[0].config.io_points[1].len == 3
' "$build_dir/conf/modbus_master.json" >/dev/null

printf 'PASS: OpenPLC Editor %s compiled the Runtime v4 three-loop fixture\n' "$editor_version"
