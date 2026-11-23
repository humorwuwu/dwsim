#!/usr/bin/env bash
# 打 macOS 包：release/macos/H2sim-<version>-macos-x64.dmg
# 走 GTK3 + Mono 6.12（x86_64）。Eto 的 XamMac2 启动器已确认不可用。
# 运行目标机需要 x86_64 的 GTK3（Intel Homebrew 的 /usr/local）。
# 不打包 ThermoCS（i386）和 DWSIM.ProFeatures.dll。
set -euo pipefail
cd "$(dirname "$0")/.."

VERSION="$(tr -d '[:space:]' < VERSION)"
SRC="DWSIM.UI.Desktop/bin/Release"
ICON="packages/Eto.Platform.XamMac2.2.8.2/build/Icon.icns"

if [[ ! -f "$SRC/DWSIM.UI.Desktop.exe" ]]; then
  echo "缺少 $SRC/DWSIM.UI.Desktop.exe。先在本机构建 DWSIM.UI.Desktop（Release）。" >&2
  exit 1
fi
if [[ ! -f "DWSIM/References/Xamarin.Mac.dll" ]]; then
  echo "缺少 DWSIM/References/Xamarin.Mac.dll" >&2
  exit 1
fi

STAGE="$(mktemp -d "${TMPDIR:-/tmp}/h2sim-macos.XXXXXX")"
trap 'rm -rf "$STAGE"' EXIT
APP="$STAGE/H2sim.app"
BUNDLE="$APP/Contents/MonoBundle"

mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources" "$BUNDLE"

# GTK3 外壳：确认 Rosetta、Mono 和 x86_64 GTK3，然后用 mono 直接启动。
cat > "$APP/Contents/MacOS/H2sim" << 'EOF'
#!/bin/bash
set -euo pipefail
if ! arch -x86_64 /usr/bin/true >/dev/null 2>&1; then
  osascript -e 'display dialog "H2sim 需要 Rosetta。在终端执行：softwareupdate --install-rosetta --agree-to-license" buttons {"好"} default button 1 with title "H2sim"' || true
  exit 1
fi
MONO=/Library/Frameworks/Mono.framework/Versions/Current/bin/mono
if [[ ! -x "$MONO" ]]; then
  osascript -e 'display dialog "H2sim 需要 Mono 6.12。请安装 Mono Framework MDK 后再打开。" buttons {"好"} default button 1 with title "H2sim"' || true
  exit 1
fi
GTK=/usr/local/lib/libgtk-3.0.dylib
if [[ ! -f "$GTK" ]] || ! file "$GTK" | grep -q 'x86_64'; then
  osascript -e 'display dialog "H2sim 的 macOS GTK3 版需要 x86_64 GTK3。Intel Mac：brew install gtk+3。Apple Silicon：需要在 Rosetta 前缀 /usr/local 安装 x86_64 GTK3（arch -x86_64 /usr/local/bin/brew install gtk+3）。" buttons {"好"} default button 1 with title "H2sim"' || true
  exit 1
fi
DIR="$(cd "$(dirname "$0")" && pwd)"
export H2SIM_MAC_RENDERER=Gtk3
export PANGOCAIRO_BACKEND=fontconfig
export GTK_DATA_PREFIX=/usr/local
export XDG_DATA_DIRS="/usr/local/share:${XDG_DATA_DIRS:-}"
cd "$DIR/../MonoBundle"
exec arch -x86_64 "$MONO" DWSIM.UI.Desktop.exe
EOF
chmod +x "$APP/Contents/MacOS/H2sim"

# 托管程序集。pdb / xml 不进包。Pro 和 ThermoC 不进 macOS 包。
find "$SRC" -maxdepth 1 \( -name '*.dll' -o -name '*.exe' -o -name '*.config' -o -name '*.dylib' \) \
  ! -name 'DWSIM.ProFeatures.*' \
  ! -name 'DWSIM.Thermodynamics.ThermoC.*' \
  -exec cp {} "$BUNDLE/" \;
cp "DWSIM/References/Xamarin.Mac.dll" "$BUNDLE/Xamarin.Mac.dll"

# 数据库、样例、位图。macOS 原生库放在程序集旁边，ThermoCS 目录排除。
if [[ -d PlatformFiles/Common ]]; then
  cp -R PlatformFiles/Common/. "$BUNDLE/"
fi
if [[ -d PlatformFiles/macOS ]]; then
  find PlatformFiles/macOS -maxdepth 1 -mindepth 1 ! -name ThermoCS -exec cp -R {} "$BUNDLE/" \;
fi
rm -rf "$BUNDLE/ThermoCS"
rm -f "$BUNDLE/DWSIM.ProFeatures.dll" "$BUNDLE/DWSIM.Thermodynamics.ThermoC.dll"

if [[ -f "$ICON" ]]; then
  cp "$ICON" "$APP/Contents/Resources/Icon.icns"
fi

cat > "$APP/Contents/Info.plist" << EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
	<key>CFBundleName</key>
	<string>H2sim</string>
	<key>CFBundleDisplayName</key>
	<string>H2sim</string>
	<key>CFBundleIdentifier</key>
	<string>com.h2sim.app</string>
	<key>CFBundleVersion</key>
	<string>${VERSION}</string>
	<key>CFBundleShortVersionString</key>
	<string>${VERSION}</string>
	<key>CFBundlePackageType</key>
	<string>APPL</string>
	<key>CFBundleSignature</key>
	<string>????</string>
	<key>CFBundleExecutable</key>
	<string>H2sim</string>
	<key>CFBundleIconFile</key>
	<string>Icon.icns</string>
	<key>CFBundleInfoDictionaryVersion</key>
	<string>6.0</string>
	<key>LSMinimumSystemVersion</key>
	<string>10.14</string>
	<key>LSArchitecturePriority</key>
	<array>
		<string>x86_64</string>
	</array>
	<key>NSPrincipalClass</key>
	<string>NSApplication</string>
	<key>NSHighResolutionCapable</key>
	<true/>
	<key>NSRequiresAquaSystemAppearance</key>
	<false/>
	<key>MonoMinimumVersion</key>
	<string>6.12</string>
	<key>MonoBundleExecutable</key>
	<string>DWSIM.UI.Desktop.exe</string>
</dict>
</plist>
EOF

# 确认包里没有不该出现的东西。
if [[ -e "$BUNDLE/ThermoCS" || -e "$BUNDLE/DWSIM.ProFeatures.dll" || -e "$BUNDLE/DWSIM.Thermodynamics.ThermoC.dll" ]]; then
  echo "打包内容检查失败：ThermoCS 或 ProFeatures 仍在包内。" >&2
  exit 1
fi
if [[ ! -f "$BUNDLE/Xamarin.Mac.dll" || ! -f "$BUNDLE/DWSIM.UI.Desktop.exe" ]]; then
  echo "打包内容检查失败：缺少启动所需的程序集。" >&2
  exit 1
fi

mkdir -p release/macos
DMG="release/macos/H2sim-${VERSION}-macos-x64.dmg"
rm -f "$DMG"
hdiutil create -volname "H2sim ${VERSION}" -srcfolder "$APP" -ov -format UDZO "$DMG" >/dev/null

echo "PACKAGE OK: $DMG"
ls -lh "$DMG"
