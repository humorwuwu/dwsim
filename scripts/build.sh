#!/usr/bin/env bash
# 构建 H2sim（macOS/Linux 需要 mono 的 msbuild；Windows 用 scripts/build.ps1）
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${CONFIG:-Release}"
PLATFORM="${PLATFORM:-x64}"

# 上游 sln 没有 Release|x64 这个 solution 配置；ReleaseWinMac|x64 映射到项目 Release|x64
case "$CONFIG" in
  Debug) SLN_CONFIG=Debug ;;
  Release) SLN_CONFIG=ReleaseWinMac ;;
  *) echo "CONFIG 只支持 Debug 或 Release（当前: $CONFIG）" >&2; exit 2 ;;
esac

command -v msbuild >/dev/null 2>&1 || {
  echo "需要 msbuild（Windows 或 mono/msbuild）。本机没有时请走 Windows CI。" >&2
  exit 1
}

if command -v nuget >/dev/null 2>&1; then
  nuget restore DWSIM.sln -PackagesDirectory packages
elif command -v mono >/dev/null 2>&1; then
  mono .nuget/NuGet.exe restore DWSIM.sln -PackagesDirectory packages
else
  echo "需要 nuget，或 mono 运行 .nuget/NuGet.exe" >&2
  exit 1
fi

msbuild DWSIM.sln -p:Configuration="$SLN_CONFIG" -p:Platform="$PLATFORM" -m -v:m

ls -l "DWSIM/bin/$PLATFORM/$CONFIG/DWSIM.exe"