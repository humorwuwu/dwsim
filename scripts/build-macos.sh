#!/usr/bin/env bash
# macOS 本机构建：只构建 Eto 桌面程序，跳过 Windows-only 的 WPF 工程和 PostBuildEvent。
# 用法：bash scripts/build-macos.sh
# 产物：DWSIM.UI.Desktop/bin/<Configuration>/DWSIM.UI.Desktop.exe
#
# 运行需要 Mono 6.12 Framework（/Library/Frameworks/Mono.framework）。窗口能否起来
# 仍受 Gate 0 V-3 限制（Xamarin.Mac 运行时），见 docs/verification.md。
set -euo pipefail
cd "$(dirname "$0")/.."

CONFIG="${CONFIG:-Release}"
case "$CONFIG" in
  Debug|Release) ;;
  *) echo "CONFIG 只支持 Debug 或 Release（当前: $CONFIG）" >&2; exit 2 ;;
esac

if [[ "$(uname -s)" != "Darwin" ]]; then
  echo "scripts/build-macos.sh 只能在 macOS 上运行。" >&2
  exit 1
fi

for tool in mono msbuild nuget; do
  command -v "$tool" >/dev/null 2>&1 || {
    echo "缺少 $tool。请安装 Mono 6.12 Framework MDK（含 msbuild/nuget）。" >&2
    exit 1
  }
done

# Mono 的 netstandard facade 不在 4.6.2 参考程序集里，必须显式传给 MSBuild。
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

# 只构建桌面程序及其依赖图。完整 sln 会拉上 DWSIM.UI.Desktop.WPF 和两个 WPF
# Python 控制台，Mono 没有 WPF；PostBuildEvent 里的 xcopy 也不是 macOS 命令。
msbuild DWSIM.UI.Desktop/DWSIM.UI.Desktop.csproj \
  "/p:Configuration=$CONFIG" \
  /p:Platform=AnyCPU \
  /p:PostBuildEvent= \
  /p:GenerateSerializationAssemblies=Off \
  "/p:NetStandardFacadesPath=$FACADES" \
  /m:1 /v:m

OUT="DWSIM.UI.Desktop/bin/$CONFIG"
EXE="$OUT/DWSIM.UI.Desktop.exe"
if [[ ! -f "$EXE" ]]; then
  echo "编译完成但找不到产物: $EXE" >&2
  exit 1
fi

# PostBuildEvent 被跳过，数据文件由这里补。macOS 包不包含 ThermoCS（i386）和
# ProFeatures（Windows 经典 UI 专用），本机输出也保持一致。
if [[ -d PlatformFiles/Common ]]; then
  cp -R PlatformFiles/Common/. "$OUT/"
fi
if [[ -d PlatformFiles/macOS ]]; then
  for entry in PlatformFiles/macOS/*; do
    [[ "$(basename "$entry")" == "ThermoCS" ]] && continue
    cp -R "$entry" "$OUT/"
  done
fi
rm -rf "$OUT/ThermoCS"
rm -f "$OUT/DWSIM.Thermodynamics.ThermoC.dll" "$OUT/DWSIM.Thermodynamics.ThermoC.dll.config" \
      "$OUT/DWSIM.Thermodynamics.ThermoC.pdb" \
      "$OUT/DWSIM.ProFeatures.dll" "$OUT/DWSIM.ProFeatures.dll.config" "$OUT/DWSIM.ProFeatures.pdb"

echo
echo "BUILD OK: $EXE"
echo "数据文件已复制到 ${OUT}（不含 ThermoCS / ProFeatures）。"
echo "注意：Eto XamMac2 启动器不可用；GTK3 版需要 x86_64 GTK3，见 docs/verification.md。"
