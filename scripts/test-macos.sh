#!/usr/bin/env bash
# macOS 本地跑动力学/Io 测试：构建 DWSIM.Automation.Dynamics.Tests，补齐 Private=False
# 的程序集，然后运行。参数会原样传给测试程序（例如 --generate-samples <dir>）。
# 用法：
#   bash scripts/test-macos.sh
#   bash scripts/test-macos.sh --generate-samples "PlatformFiles/Common/samples"
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${CONFIG:-Release}"
case "$CONFIG" in
  Debug|Release) ;;
  *) echo "CONFIG 只支持 Debug 或 Release（当前: $CONFIG）" >&2; exit 2 ;;
esac

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "scripts/test-macos.sh 只能在 macOS 上运行。" >&2
  exit 1
fi

for tool in mono msbuild nuget; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "缺少 $tool。请安装 Mono 6.12 Framework MDK（含 msbuild/nuget）。" >&2
    exit 1
  }
done

MONO_ROOT="$(cd "$(dirname "$(command -v mono)")/.." && pwd)"
FACADES=""
for api in 4.8-api 4.7.2-api 4.7.1-api 4.7-api 4.6.2-api; do
  if [[ -f "$MONO_ROOT/lib/mono/$api/Facades/netstandard.dll" ]]; then
    FACADES="$MONO_ROOT/lib/mono/$api/Facades"
    break
  fi
done
if [[ -z "$FACADES" ]]; then
  echo "找不到 Mono 的 netstandard facade（在 $MONO_ROOT/lib/mono/*-api/Facades）。" >&2
  exit 1
fi

echo "Mono:    $MONO_ROOT"
echo "Facades: $FACADES"
echo "Config:  $CONFIG"

nuget restore DWSIM.sln -PackagesDirectory packages

msbuild DWSIM.Automation.Dynamics.Tests/DWSIM.Automation.Dynamics.Tests.csproj \
  "/p:Configuration=$CONFIG" \
  /p:Platform=AnyCPU \
  /p:PostBuildEvent= \
  /p:GenerateSerializationAssemblies=Off \
  "/p:NetStandardFacadesPath=$FACADES" \
  /m:1 /v:m

TEST_DIR="DWSIM.Automation.Dynamics.Tests/bin/$CONFIG"
TEST_EXE="$TEST_DIR/DWSIM.Automation.Dynamics.Tests.exe"
if [[ ! -f "$TEST_EXE" ]]; then
  echo "编译完成但找不到测试程序: $TEST_EXE" >&2
  exit 1
fi

# DWSIM.Interfaces 等工程的 ProjectReference 是 Private=False，不拷贝运行时缺程序集。
find "DWSIM/bin/$CONFIG" -maxdepth 1 \( -name '*.dll' -o -name '*.dylib' -o -name '*.config' \) \
  -exec cp -f {} "$TEST_DIR/" \;

echo
echo "RUN: $TEST_EXE"
exec mono "$TEST_EXE" "$@"
